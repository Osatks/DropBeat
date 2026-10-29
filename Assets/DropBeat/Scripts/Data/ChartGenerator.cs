using System;
using System.Collections.Generic;

namespace DropBeat
{
    public enum NoteKind : byte
    {
        Beat = 0,
        Downbeat = 1,
        Offbeat = 2
    }

    public enum NoteType : byte
    {
        /// <summary>Tap when the ball lands.</summary>
        Tap = 0,
        /// <summary>Tap, keep holding while the ball grinds the rail, release on the end beat.</summary>
        Hold = 1,
        /// <summary>Swipe in <see cref="ChartNote.swipe"/> on the beat; the ball dashes that way.</summary>
        Swipe = 2,
        /// <summary>The ball splits and lands in two rings at once: tap both (two thumbs / both hands).</summary>
        Dual = 3
    }

    public enum SwipeDir : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
        Up = 3
    }

    [Serializable]
    public struct ChartNote
    {
        /// <summary>Song time in seconds (per-song offset already applied).</summary>
        public double time;
        public NoteKind kind;
        /// <summary>Beat index this note belongs to (offbeats use the beat before them).</summary>
        public int beat;
        public NoteType type;
        /// <summary>Hold notes: when the rail ends (release here).</summary>
        public double endTime;
        public SwipeDir swipe;
        /// <summary>0 = the main ball, 1..3 = a companion ball.</summary>
        public int ball;
        /// <summary>Ring sways in rhythm and arrives at its spot exactly on the beat.</summary>
        public bool moving;
        /// <summary>One of the song's big moments (a drop, a new section, its hardest hits): extra feedback.</summary>
        public bool impact;
    }

    /// <summary>
    /// Turns a beat grid into a chart. Notes are only ever placed on grid positions (beats, or exact
    /// half-beats on Hard), so their timing always comes from the music's tempo.
    ///
    /// Which grid positions become notes is chosen by dynamic programming over the whole song. A beat is
    /// worth tapping when you can feel a hit there:
    ///  - hit: a kick or snare/clap lands on it (pads and hats count much less);
    ///  - accent: the hit stands out from the beats around it;
    ///  - groove: within each 8-bar phrase the same positions of the 2-bar pattern score alike, so the notes
    ///    follow the song's repeating rhythm and the player can feel it coming;
    ///  - impact: drops / new sections and the song's very hardest hits are always charted (and flagged).
    /// Beats with no audible hit are penalised hard. Gaps are only loosely pulled toward a target spacing
    /// (tighter in loud sections), so the music decides where the notes go, not a metronome.
    /// </summary>
    public static class ChartGenerator
    {
        private struct Rules
        {
            /// <summary>Hard floor between notes, seconds.</summary>
            public double minGap;
            /// <summary>Preferred gap in an average-energy section, seconds.</summary>
            public double targetGap;
            /// <summary>Longest allowed gap, in beats.</summary>
            public int maxGapBeats;
            /// <summary>Allow half-beat (off-beat eighth) notes.</summary>
            public bool offbeats;
            /// <summary>How strongly gaps are pulled toward the target.</summary>
            public double gapWeight;
        }

        private static Rules For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy:
                    return new Rules { minGap = 0.62, targetGap = 1.35, maxGapBeats = 8, offbeats = false, gapWeight = 1.1 };
                case Difficulty.Hard:
                    return new Rules { minGap = 0.2, targetGap = 0.46, maxGapBeats = 6, offbeats = true, gapWeight = 0.8 };
                default:
                    return new Rules { minGap = 0.4, targetGap = 0.78, maxGapBeats = 8, offbeats = false, gapWeight = 0.9 };
            }
        }

        private struct Candidate
        {
            public double time;
            public int beat;
            public NoteKind kind;
            public double value;
            public double target;
            public bool impact;
        }

        public static List<ChartNote> Generate(SongData song, Difficulty difficulty)
        {
            var notes = new List<ChartNote>(512);
            if (song == null || !song.IsAnalyzed) return notes;

            Rules rules = For(difficulty);
            int n = song.beatTimes.Length;
            double spb = song.SecondsPerBeat;

            float[] activity = new float[n];
            for (int k = 0; k < n; k++) activity[k] = Math.Max(Get(song.beatStrength, k), Get(song.kickStrength, k));
            float[] energy = SectionEnergy(song);
            float[] hit = HitStrength(song);
            float[] accent = Accent(hit);
            float[] groove = Groove(song, hit, 8, 32);
            bool[] impact = Impacts(song, hit, accent);

            int start = FindStart(song, activity);
            if (start < 0) return notes;
            int end = n - 1;
            while (end > start && (activity[end] < 0.3f || Get(song.loudness, end) < 0.08f)) end--;

            // Candidates: every beat (and half-beat on Hard) between start and end.
            var cands = new List<Candidate>(2 * (end - start + 1));
            for (int k = start; k <= end; k++)
            {
                int pos = ((k - song.downbeatPhase) % 4 + 4) % 4;
                bool silent = Get(song.loudness, k) < 0.06f;
                double metric = pos == 0 ? 0.3 : pos == 2 ? 0.12 : 0.0;
                double target = rules.targetGap * (1.35 - 0.6 * energy[k]);
                double value = 1.1 * hit[k] + 0.7 * Math.Max(-0.3f, accent[k]) + 0.6 * groove[k] + metric;
                if (hit[k] < 0.22f) value -= 0.9;  // nothing to feel here
                if (silent) value -= 0.6;
                if (impact[k]) value += 2.5;       // always charted
                cands.Add(new Candidate
                {
                    time = song.BeatTime(k),
                    beat = k,
                    kind = pos == 0 ? NoteKind.Downbeat : NoteKind.Beat,
                    value = value,
                    target = target,
                    impact = impact[k]
                });

                if (rules.offbeats && k < end && spb * 0.5 >= rules.minGap * 0.98)
                {
                    double off = Get(song.offStrength, k);
                    cands.Add(new Candidate
                    {
                        time = 0.5 * (song.BeatTime(k) + song.BeatTime(k + 1)),
                        beat = k,
                        kind = NoteKind.Offbeat,
                        // Off-beats only pay off when the music really has an accent there.
                        value = off * 1.1 - 0.35 - (silent ? 0.6 : 0.0),
                        target = target
                    });
                }
            }

            int m = cands.Count;
            if (m == 0) return notes;

            // Centre the values so adding a note is only worth it for above-average positions;
            // density then comes from the gap targets, not from "more notes = more points".
            double mean = 0;
            for (int i = 0; i < m; i++) mean += cands[i].value;
            mean /= m;

            double maxGap = rules.maxGapBeats * spb + 1e-3;
            double openWindow = 4 * spb * 2 + 1e-3; // first note within two bars of the start
            var best = new double[m];
            var prev = new int[m];
            for (int i = 0; i < m; i++)
            {
                Candidate c = cands[i];
                double own = c.value - mean;
                best[i] = double.NegativeInfinity;
                prev[i] = -1;
                if (c.time - cands[0].time <= openWindow) best[i] = own;

                for (int j = i - 1; j >= 0; j--)
                {
                    double gap = c.time - cands[j].time;
                    if (gap > maxGap) break;
                    if (gap < rules.minGap - 1e-4 || double.IsNegativeInfinity(best[j])) continue;
                    double ratio = Math.Log(gap / (0.5 * (c.target + cands[j].target)), 2.0);
                    double score = best[j] + own - rules.gapWeight * ratio * ratio;
                    if (score > best[i])
                    {
                        best[i] = score;
                        prev[i] = j;
                    }
                }
            }

            // Finish on a note within the last two bars.
            int last = -1;
            double lastScore = double.NegativeInfinity;
            double finish = cands[m - 1].time - openWindow;
            for (int i = 0; i < m; i++)
            {
                if (cands[i].time >= finish && best[i] > lastScore)
                {
                    lastScore = best[i];
                    last = i;
                }
            }

            for (int i = last; i >= 0; i = prev[i])
            {
                Candidate c = cands[i];
                notes.Add(new ChartNote { time = c.time, kind = c.kind, beat = c.beat, impact = c.impact });
            }
            notes.Reverse();
            return notes;
        }

        /// <summary>Loudness smoothed over two bars and ranked within the song: 0 = calmest, 1 = most intense.</summary>
        public static float[] SectionEnergy(SongData song)
        {
            int n = song.beatTimes.Length;
            var smooth = new float[n];
            for (int k = 0; k < n; k++)
            {
                double s = 0;
                int c = 0;
                for (int j = Math.Max(0, k - 4); j <= Math.Min(n - 1, k + 4); j++)
                {
                    s += Get(song.loudness, j) + 0.5 * Math.Max(Get(song.beatStrength, j), Get(song.kickStrength, j));
                    c++;
                }
                smooth[k] = (float)(s / Math.Max(1, c));
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (a, b) => smooth[a].CompareTo(smooth[b]));
            var energy = new float[n];
            for (int r = 0; r < n; r++) energy[order[r]] = n > 1 ? r / (float)(n - 1) : 0.5f;
            return energy;
        }

        /// <summary>How much of a drum hit lands on each beat: kick or snare/clap; broadband (pads, hats) counts less.</summary>
        public static float[] HitStrength(SongData song)
        {
            int n = song.beatTimes.Length;
            var hit = new float[n];
            for (int k = 0; k < n; k++)
            {
                hit[k] = Math.Max(Get(song.kickStrength, k), Math.Max(0.85f * Get(song.snareStrength, k), 0.55f * Get(song.beatStrength, k)));
            }
            return hit;
        }

        /// <summary>Hit strength above the median of the surrounding beats (+-4): how much it stands out.</summary>
        public static float[] Accent(float[] hit)
        {
            int n = hit.Length;
            var accent = new float[n];
            var window = new List<float>(8);
            for (int k = 0; k < n; k++)
            {
                window.Clear();
                for (int j = Math.Max(0, k - 4); j <= Math.Min(n - 1, k + 4); j++) if (j != k) window.Add(hit[j]);
                window.Sort();
                float median = window.Count > 0 ? window[window.Count / 2] : 0f;
                accent[k] = hit[k] - median;
            }
            return accent;
        }

        /// <summary>
        /// Groove: for each phrase of <paramref name="phraseBeats"/>, the average hit at each position of a
        /// <paramref name="cycle"/>-beat pattern, minus the phrase average. Positions the song keeps hitting
        /// bar after bar score high, so the chart repeats the song's own rhythm.
        /// </summary>
        public static float[] Groove(SongData song, float[] hit, int cycle, int phraseBeats)
        {
            int n = hit.Length;
            var groove = new float[n];
            var sum = new float[cycle];
            var count = new int[cycle];
            int phase = ((song.downbeatPhase % cycle) + cycle) % cycle;
            int first = song.downbeatPhase % phraseBeats - phraseBeats;
            for (int s = first; s < n; s += phraseBeats)
            {
                Array.Clear(sum, 0, cycle);
                Array.Clear(count, 0, cycle);
                float total = 0f;
                int all = 0;
                int from = Math.Max(0, s), to = Math.Min(n, s + phraseBeats);
                for (int k = from; k < to; k++)
                {
                    int pos = ((k - phase) % cycle + cycle) % cycle;
                    sum[pos] += hit[k];
                    count[pos]++;
                    total += hit[k];
                    all++;
                }
                if (all == 0) continue;
                float mean = total / all;
                for (int k = from; k < to; k++)
                {
                    int pos = ((k - phase) % cycle + cycle) % cycle;
                    groove[k] = sum[pos] / count[pos] - mean;
                }
            }
            return groove;
        }

        /// <summary>
        /// The song's big moments: downbeats where the music jumps in loudness (a drop or a new section
        /// kicking in), plus the hardest, most isolated hits. At least 4 bars apart.
        /// </summary>
        public static bool[] Impacts(SongData song, float[] hit, float[] accent)
        {
            int n = hit.Length;
            var impact = new bool[n];
            if (n < 20) return impact;
            var sorted = (float[])hit.Clone();
            Array.Sort(sorted);
            float top = sorted[Math.Min(n - 1, (int)(n * 0.97f))];
            int last = -1000;
            for (int k = 8; k < n - 8; k++)
            {
                if (k - last < 16 || hit[k] < 0.55f) continue;
                float before = 0f, after = 0f;
                for (int j = 1; j <= 8; j++)
                {
                    before += Get(song.loudness, k - j);
                    after += Get(song.loudness, k + j - 1);
                }
                bool drop = song.IsDownbeat(k) && (after - before) / 8f >= 0.22f;
                bool slam = hit[k] >= top && accent[k] >= 0.35f;
                if (drop || slam)
                {
                    impact[k] = true;
                    last = k;
                }
            }
            return impact;
        }

        private static int FindStart(SongData song, float[] activity)
        {
            int n = song.beatTimes.Length;
            int start = -1;
            for (int k = 0; k < n; k++)
            {
                if (song.beatTimes[k] >= 0.4f && activity[k] >= 0.35f && Get(song.loudness, k) > 0.1f)
                {
                    start = k;
                    break;
                }
            }
            if (start < 0) return -1;

            // Prefer to open on a downbeat if one is close.
            for (int k = start; k < Math.Min(n, start + 4); k++)
            {
                if (song.IsDownbeat(k)) return k;
            }
            return start;
        }

        private static float Get(float[] a, int i)
        {
            return a != null && i >= 0 && i < a.Length ? a[i] : 0f;
        }
    }
}
