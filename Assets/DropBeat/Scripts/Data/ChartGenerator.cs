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

    [Serializable]
    public struct ChartNote
    {
        /// <summary>Song time in seconds (per-song offset already applied).</summary>
        public double time;
        public NoteKind kind;
        /// <summary>Beat index this note belongs to (offbeats use the beat before them).</summary>
        public int beat;
    }

    /// <summary>
    /// Turns a beat grid into a chart. Notes are only ever placed on grid positions (beats, or the
    /// exact half-beat for Hard), so timing comes from the music's tempo, never from random peaks.
    /// The music's features only decide *which* grid positions get a note.
    /// </summary>
    public static class ChartGenerator
    {
        private struct Rules
        {
            public float minGap;
            public bool everyBeat;
            public bool offbeats;
        }

        private static Rules For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return new Rules { minGap = 0.6f, everyBeat = false, offbeats = false };
                case Difficulty.Hard: return new Rules { minGap = 0.16f, everyBeat = true, offbeats = true };
                default: return new Rules { minGap = 0.3f, everyBeat = true, offbeats = false };
            }
        }

        public static List<ChartNote> Generate(SongData song, Difficulty difficulty)
        {
            var notes = new List<ChartNote>(512);
            if (song == null || !song.IsAnalyzed) return notes;

            Rules rules = For(difficulty);
            int n = song.beatTimes.Length;
            double spb = song.SecondsPerBeat;

            float[] activity = new float[n];
            for (int k = 0; k < n; k++)
            {
                activity[k] = Math.Max(Get(song.beatStrength, k), Get(song.kickStrength, k));
            }

            // Bar activity decides between full sections and breakdowns.
            int bars = n / 4 + 2;
            float[] barActivity = new float[bars];
            int[] barCount = new int[bars];
            for (int k = 0; k < n; k++)
            {
                int b = BarOf(song, k);
                if (b < 0 || b >= bars) continue;
                barActivity[b] += activity[k];
                barCount[b]++;
            }

            var barValues = new List<float>();
            for (int b = 0; b < bars; b++)
            {
                if (barCount[b] > 0)
                {
                    barActivity[b] /= barCount[b];
                    barValues.Add(barActivity[b]);
                }
            }
            barValues.Sort();
            float barMedian = barValues.Count > 0 ? barValues[barValues.Count / 2] : 0.5f;

            int start = -1;
            for (int k = 0; k < n; k++)
            {
                if (song.beatTimes[k] >= 0.4f && activity[k] >= 0.35f && Get(song.loudness, k) > 0.1f)
                {
                    start = k;
                    break;
                }
            }
            if (start < 0) return notes;

            // Prefer to open on a downbeat if one is close.
            for (int k = start; k < Math.Min(n, start + 4); k++)
            {
                if (song.IsDownbeat(k))
                {
                    start = k;
                    break;
                }
            }

            int end = n - 1;
            while (end > start && (activity[end] < 0.3f || Get(song.loudness, end) < 0.08f)) end--;

            double lastTime = double.NegativeInfinity;
            int lastBeat = start - 100;
            int eighthRun = 0;

            for (int k = start; k <= end; k++)
            {
                int bar = BarOf(song, k);
                bool quiet = bar >= 0 && bar < bars && barActivity[bar] < 0.35f * barMedian;
                bool silent = Get(song.loudness, k) < 0.06f;
                bool down = song.IsDownbeat(k);
                bool half = ((k - song.downbeatPhase) % 2 + 2) % 2 == 0;

                bool take;
                if (difficulty == Difficulty.Easy)
                {
                    take = quiet ? down && !silent : half && activity[k] >= 0.12f;
                }
                else if (difficulty == Difficulty.Normal)
                {
                    take = quiet ? half && !silent : activity[k] >= 0.12f || half;
                }
                else
                {
                    take = quiet ? (half || activity[k] >= 0.25f) && !silent : true;
                }

                // Keep playing through silent stretches: at most two bars without a note.
                if (!take && down && k - lastBeat >= 8) take = true;

                double t = song.BeatTime(k);
                if (take && t - lastTime >= rules.minGap)
                {
                    notes.Add(new ChartNote { time = t, kind = down ? NoteKind.Downbeat : NoteKind.Beat, beat = k });
                    lastTime = t;
                    lastBeat = k;
                }

                bool addedOff = false;
                if (rules.offbeats && !quiet && k + 1 <= end)
                {
                    double offTime = 0.5 * (song.BeatTime(k) + song.BeatTime(k + 1));
                    float off = Get(song.offStrength, k);
                    float around = Math.Max(Get(song.beatStrength, k), Get(song.beatStrength, k + 1));
                    bool strongOff = off >= 0.6f && off >= 0.75f * around;
                    // eighthRun caps continuous eighth-note streams at 4 off-beats in a row.
                    if (strongOff && eighthRun < 4 && offTime - lastTime >= rules.minGap && spb * 0.5 >= rules.minGap * 0.98)
                    {
                        notes.Add(new ChartNote { time = offTime, kind = NoteKind.Offbeat, beat = k });
                        lastTime = offTime;
                        addedOff = true;
                    }
                }

                eighthRun = addedOff ? eighthRun + 1 : 0;
            }

            return notes;
        }

        private static int BarOf(SongData song, int k)
        {
            int rel = k - song.downbeatPhase;
            return rel >= 0 ? rel / 4 : -1;
        }

        private static float Get(float[] a, int i)
        {
            return a != null && i >= 0 && i < a.Length ? a[i] : 0f;
        }
    }
}
