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
    }

    /// <summary>A playable chart: notes (time-sorted) plus camera cues for perspective shifts.</summary>
    public sealed class Chart
    {
        public readonly List<ChartNote> notes = new List<ChartNote>(512);
        public readonly List<CameraCue> cameraCues = new List<CameraCue>(16);

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
    ///  - Hold rails on long, calm gaps (sustained parts).
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
            var chart = new Chart();
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

            bool easy = difficulty == Difficulty.Easy;
            bool hard = difficulty == Difficulty.Hard;
            double minGap = easy ? 0.62 : hard ? 0.2 : 0.4;

            var notes = new List<ChartNote>(baseNotes);
            int lastHoldBeat = -100, lastSwipeBeat = -100, lastDualBeat = -100;
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
                bool multiBall = !easy && phrase >= 0 && phrase < phraseCount && companionPhrase[phrase];

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

                // Hard is dense (every beat): carve 2-beat holds out of calmer bars on downbeats.
                if (hard && !c.impact && c.kind == NoteKind.Downbeat && pe < 0.6f && c.beat - lastHoldBeat >= 16 &&
                    !ImpactWithin(notes, i + 1, c.time + 2.5 * spb))
                {
                    c.type = NoteType.Hold;
                    c.endTime = c.time + 2.0 * spb;
                    lastHoldBeat = c.beat;
                    notes[i] = c;
                    while (i + 1 < notes.Count - 1 && notes[i + 1].time <= c.endTime + 0.5 * spb) notes.RemoveAt(i + 1);
                    continue;
                }

                // Hold rail: long gap in a calmer phrase.
                double holdMinGap = easy ? 3.0 : 2.0;
                if (!c.impact && gapBeats >= holdMinGap && pe < 0.72f && c.beat - lastHoldBeat >= (easy ? 16 : 8) && c.kind != NoteKind.Offbeat)
                {
                    double holdBeats = Math.Min(4.0, Math.Floor(gapBeats - 1.0));
                    if (holdBeats >= 1.0)
                    {
                        c.type = NoteType.Hold;
                        c.endTime = c.time + holdBeats * spb;
                        lastHoldBeat = c.beat;
                        notes[i] = c;
                        continue;
                    }
                }

                // Moving rings in every other mid-energy phrase.
                if (pe >= 0.4f && pe < 0.72f && (phrase & 1) == 0 && c.type == NoteType.Tap) c.moving = true;
                notes[i] = c;
            }

            // Companion balls fill skipped beats in the most intense phrases.
            if (!easy)
            {
                var companions = new List<ChartNote>();
                int nextBall = 1;
                int maxBalls = hard ? 3 : 2;
                for (int i = 0; i + 1 < notes.Count; i++)
                {
                    ChartNote a = notes[i];
                    ChartNote b = notes[i + 1];
                    int phrase = PhraseOf(song, a.beat);
                    float pe = phrase >= 0 && phrase < phraseCount ? phraseEnergy[phrase] : 0f;
                    if (phrase < 0 || phrase >= phraseCount || !companionPhrase[phrase] || a.type == NoteType.Hold || a.type == NoteType.Swipe) continue;
                    int span = b.beat - a.beat;
                    if (a.kind == NoteKind.Offbeat) continue;
                    // A separate ball is easy to read, so companions may sit a little closer than main notes.
                    double compGap = minGap * 0.85;
                    double t;
                    int beat;
                    NoteKind kind;
                    if (span >= 2)
                    {
                        beat = a.beat + span / 2;
                        t = song.BeatTime(beat);
                        if (Math.Max(Get(song.beatStrength, beat), Get(song.kickStrength, beat)) < 0.35f) continue;
                        kind = song.IsDownbeat(beat) ? NoteKind.Downbeat : NoteKind.Beat;
                    }
                    else if (hard && span == 1 && Get(song.offStrength, a.beat) >= 0.45f)
                    {
                        // Hard: companions answer on the off-beat eighth.
                        beat = a.beat;
                        t = 0.5 * (a.time + b.time);
                        kind = NoteKind.Offbeat;
                    }
                    else continue;
                    if (t - a.time < compGap || b.time - t < compGap) continue;
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
                if (!easy && companionPhrase[p]) mode = CameraMode.Side; // several balls: clearest view
                else if (!easy && tunnelPhrase[p]) mode = CameraMode.Tunnel;
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

            return chart;
        }

        private static bool ImpactWithin(List<ChartNote> notes, int from, double until)
        {
            for (int j = from; j < notes.Count && notes[j].time <= until; j++) if (notes[j].impact) return true;
            return false;
        }

        private static SwipeDir PickSwipe(Random rng)
        {
            double r = rng.NextDouble();
            return r < 0.4 ? SwipeDir.Left : r < 0.8 ? SwipeDir.Right : SwipeDir.Up;
        }

        private static int PhraseOf(SongData song, int k)
        {
            int rel = k - song.downbeatPhase;
            return rel >= 0 ? rel / PhraseBeats : -1;
        }

        private static float Get(float[] a, int i) => a != null && i >= 0 && i < a.Length ? a[i] : 0f;

        private static int StableHash(string s)
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
