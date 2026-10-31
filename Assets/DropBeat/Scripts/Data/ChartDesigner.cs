using System;
using System.Collections.Generic;

namespace DropBeat
{
    public enum CameraMode : byte
    {
        Side = 0,
        AngledLeft = 1,
        AngledRight = 2,
        Tunnel = 3
    }

    [Serializable]
    public struct CameraCue
    {
        public double time;
        public CameraMode mode;
        /// <summary>A quick cut on the beat (Dance Cam) instead of a slow blend with a whoosh.</summary>
        public bool cut;
    }

    /// <summary>A playable chart: notes (time-sorted) plus camera cues for perspective shifts.</summary>
    public sealed class Chart
    {
        public readonly List<ChartNote> notes = new List<ChartNote>(512);
        public readonly List<CameraCue> cameraCues = new List<CameraCue>(16);
        /// <summary>The song's twist and where in the song it is active.</summary>
        public SongTwist twist;
        public readonly List<TwistSpan> twistSpans = new List<TwistSpan>(8);
        public int goldenCount;

        public int HoldCount
        {
            get
            {
                int c = 0;
                foreach (var n in notes) if (n.type == NoteType.Hold) c++;
                return c;
            }
        }

        /// <summary>Number of judgements: one per note, two for duals, plus one per hold tail.</summary>
        public int JudgementCount
        {
            get
            {
                int c = 0;
                foreach (var n in notes) c += n.type == NoteType.Dual ? 2 : n.type == NoteType.Hold ? 2 : 1;
                return c;
            }
        }
    }

    /// <summary>
    /// Turns the base beat chart into a level with variety, driven by the music's structure:
    /// the song is split into 8-bar phrases ranked by energy, and each element appears where it fits.
    ///  - Swipe dashes on phrase openings where the energy rises (section changes).
    ///  - Dual rings on the heaviest kick accents.
    ///  - Companion balls fill the skipped beats in the most intense phrases (choruses / drops).
    ///  - Moving rings in mid-energy phrases.
    ///  - Camera: side view, angled views and a tunnel view for the most intense phrases. Intense phrases
    ///    alternate between companion balls (always in the clear side view) and the tunnel view, so the
    ///    player never has to read several balls through a tilted camera.
    /// Everything stays on the beat grid, and is deterministic per song and difficulty.
    /// </summary>
    public static class ChartDesigner
    {
        private const int PhraseBeats = 32;

        public static Chart Design(SongData song, Difficulty difficulty)
        {
            var chart = new Chart { twist = SongTwists.For(song) };
            List<ChartNote> baseNotes = ChartGenerator.Generate(song, difficulty);
            if (baseNotes.Count == 0) return chart;

            int n = song.beatTimes.Length;
            double spb = song.SecondsPerBeat;
            float[] energy = ChartGenerator.SectionEnergy(song);
            var rng = new Random(StableHash(song.name) ^ ((int)difficulty * 7919));

            // Phrase energy (8 bars starting at the downbeat phase).
            int phraseCount = n / PhraseBeats + 2;
            var phraseEnergy = new float[phraseCount];
            var phraseHits = new int[phraseCount];
            for (int k = 0; k < n; k++)
            {
                int p = PhraseOf(song, k);
                if (p < 0 || p >= phraseCount) continue;
                phraseEnergy[p] += energy[k];
                phraseHits[p]++;
            }
            for (int p = 0; p < phraseCount; p++) phraseEnergy[p] = phraseHits[p] > 0 ? phraseEnergy[p] / phraseHits[p] : 0f;

            // Intense phrases take turns: even ones get companion balls, odd ones the tunnel view.
            const float IntenseEnergy = 0.68f;
            var companionPhrase = new bool[phraseCount];
            var tunnelPhrase = new bool[phraseCount];
            int intense = 0;
            for (int p = 0; p < phraseCount; p++)
            {
                if (phraseEnergy[p] < IntenseEnergy) continue;
                if ((intense++ & 1) == 0) companionPhrase[p] = true;
                else tunnelPhrase[p] = true;
            }

            // The song's twist can reshape which phrases get companions, the tunnel or drum runs.
            var runPhrase = new bool[phraseCount];
            TwistDesigner.ShapePhrases(chart.twist, phraseEnergy, companionPhrase, tunnelPhrase, runPhrase);
            bool spirit = chart.twist == SongTwist.SpiritBalls; // companions on every difficulty
            bool warp = chart.twist == SongTwist.Warp;          // tunnel on every difficulty

            bool easy = difficulty == Difficulty.Easy;
            bool hard = difficulty == Difficulty.Hard;
            double minGap = ChartGenerator.MinGap(difficulty);

            var notes = new List<ChartNote>(baseNotes);
            if (chart.twist == SongTwist.Stampede) TwistDesigner.AddStampedeRuns(song, difficulty, notes, runPhrase, chart);
            int lastSwipeBeat = -100, lastDualBeat = -100;
            int lastPhrase = -1;
            for (int i = 2; i < notes.Count - 1; i++)
            {
                ChartNote c = notes[i];
                ChartNote next = notes[i + 1];
                int phrase = PhraseOf(song, c.beat);
                float pe = phrase >= 0 && phrase < phraseCount ? phraseEnergy[phrase] : 0.5f;
                float prevPe = phrase - 1 >= 0 && phrase - 1 < phraseCount ? phraseEnergy[phrase - 1] : 0f;
                double gapNext = next.time - c.time;
                double gapBeats = gapNext / spb;
                bool phraseStart = phrase != lastPhrase;
                lastPhrase = phrase;
                // Companion phrases keep the main ball in a centre lane: no dashes or split rings there.
                bool multiBall = (!easy || spirit) && phrase >= 0 && phrase < phraseCount && companionPhrase[phrase];

                // Swipe dash: first note of a phrase where the energy steps up (or periodic accents on Hard).
                if (!multiBall && c.kind != NoteKind.Offbeat && gapBeats >= 1.0 && c.beat - lastSwipeBeat >= 8 &&
                    ((phraseStart && pe > prevPe + 0.08f) || (hard && pe > 0.7f && c.kind == NoteKind.Downbeat && rng.NextDouble() < 0.18)))
                {
                    c.type = NoteType.Swipe;
                    c.swipe = PickSwipe(rng);
                    lastSwipeBeat = c.beat;
                    notes[i] = c;
                    continue;
                }

                // Dual rings: the heaviest kick accents in energetic phrases (not on Easy).
                if (!easy && !multiBall && c.kind == NoteKind.Downbeat && pe > 0.55f && c.beat - lastDualBeat >= 16 &&
                    Get(song.kickStrength, c.beat) >= 0.85f && gapBeats >= 1.0)
                {
                    c.type = NoteType.Dual;
                    lastDualBeat = c.beat;
                    notes[i] = c;
                    continue;
                }

                // Moving rings in every other mid-energy phrase.
                if (pe >= 0.4f && pe < 0.72f && (phrase & 1) == 0 && c.type == NoteType.Tap) c.moving = true;
                notes[i] = c;
            }

            // Companion balls fill skipped beats in the most intense phrases.
            if (!easy || spirit)
            {
                var companions = new List<ChartNote>();
                int nextBall = 1;
                // A third ball only for Spirit Balls (it gets its own far lane); elsewhere two keep the lanes clear.
                int maxBalls = hard && spirit ? 3 : easy ? 1 : 2;
                float minStrength = spirit ? 0.2f : 0.35f;
                for (int i = 0; i + 1 < notes.Count; i++)
                {
                    ChartNote a = notes[i];
                    ChartNote b = notes[i + 1];
                    int phrase = PhraseOf(song, a.beat);
                    float pe = phrase >= 0 && phrase < phraseCount ? phraseEnergy[phrase] : 0f;
                    if (phrase < 0 || phrase >= phraseCount || !companionPhrase[phrase] || a.type == NoteType.Hold || a.type == NoteType.Swipe) continue;
                    int span = b.beat - a.beat;
                    if (a.kind == NoteKind.Offbeat) continue;
                    // Never closer to your own notes than the difficulty's minimum gap.
                    double compGap = spirit ? Math.Min(minGap, spb * 0.95) : minGap;
                    double t;
                    int beat;
                    NoteKind kind;
                    if (span >= 2)
                    {
                        beat = a.beat + span / 2;
                        t = song.BeatTime(beat);
                        if (Math.Max(Get(song.beatStrength, beat), Get(song.kickStrength, beat)) < minStrength) continue;
                        kind = song.IsDownbeat(beat) ? NoteKind.Downbeat : NoteKind.Beat;
                    }
                    else if (hard && span == 1 && Get(song.offStrength, a.beat) >= 0.45f)
                    {
                        // Hard: companions answer on the off-beat eighth, i.e. exactly half a beat after
                        // a's beat (not halfway to b, which is off the grid when b is itself an off-beat).
                        beat = a.beat;
                        t = 0.5 * (song.BeatTime(a.beat) + song.BeatTime(a.beat + 1));
                        if (b.time <= t) continue;
                        kind = NoteKind.Offbeat;
                    }
                    else continue;
                    if (t - a.time < compGap || b.time - t < compGap) continue;
                    // Companions (and their 2-beat exit) end inside their own phrase unless the next is multi-ball
                    // too: the next phrase may open with the tunnel or an angled camera.
                    int nextPhrase = phrase + 1;
                    bool nextShared = nextPhrase < phraseCount && companionPhrase[nextPhrase];
                    if (!nextShared && (PhraseOf(song, beat) != phrase || t > song.BeatTime(song.downbeatPhase + nextPhrase * PhraseBeats) - 2 * spb)) continue;
                    companions.Add(new ChartNote
                    {
                        time = t,
                        beat = beat,
                        kind = kind,
                        type = NoteType.Tap,
                        ball = nextBall
                    });
                    nextBall = nextBall % maxBalls + 1;
                }
                notes.AddRange(companions);
                notes.Sort((x, y) => x.time.CompareTo(y.time));
            }

            // The last ring of the main ball is the song's big finish.
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                ChartNote last = notes[i];
                if (last.ball != 0 || last.type == NoteType.Swipe) continue;
                last.impact = true;
                notes[i] = last;
                break;
            }
            chart.notes.AddRange(notes);

            // Camera cues per phrase.
            CameraMode current = CameraMode.Side;
            bool flip = false;
            chart.cameraCues.Add(new CameraCue { time = double.NegativeInfinity, mode = CameraMode.Side });
            for (int p = 1; p < phraseCount; p++)
            {
                int k = song.downbeatPhase + p * PhraseBeats;
                if (k >= n) break;
                float pe = phraseEnergy[p];
                CameraMode mode;
                if ((!easy || spirit) && companionPhrase[p]) mode = CameraMode.Side; // several balls: clearest view
                else if ((!easy || warp) && tunnelPhrase[p]) mode = CameraMode.Tunnel;
                else if (pe >= 0.45f)
                {
                    mode = flip ? CameraMode.AngledLeft : CameraMode.AngledRight;
                }
                else mode = CameraMode.Side;

                bool angled = mode == CameraMode.AngledLeft || mode == CameraMode.AngledRight;
                bool wasAngled = current == CameraMode.AngledLeft || current == CameraMode.AngledRight;
                if (angled && wasAngled) mode = current; // stay on the same side through consecutive angled phrases
                if (mode != current)
                {
                    if (angled) flip = !flip;
                    chart.cameraCues.Add(new CameraCue { time = song.BeatTime(k), mode = mode });
                    current = mode;
                }
            }

            TwistDesigner.Finish(song, difficulty, chart, phraseEnergy, companionPhrase);
            return chart;
        }

        private static SwipeDir PickSwipe(Random rng)
        {
            double r = rng.NextDouble();
            return r < 0.4 ? SwipeDir.Left : r < 0.8 ? SwipeDir.Right : SwipeDir.Up;
        }

        /// <summary>8-bar phrase of beat <paramref name="k"/> (-1 before the first downbeat).</summary>
        public static int PhraseOf(SongData song, int k)
        {
            int rel = k - song.downbeatPhase;
            return rel >= 0 ? rel / PhraseBeats : -1;
        }

        private static float Get(float[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : 0f;

        /// <summary>Deterministic string hash (string.GetHashCode is not guaranteed stable across runtimes).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char ch in s) h = h * 31 + ch;
                return h;
            }
        }
    }
}
