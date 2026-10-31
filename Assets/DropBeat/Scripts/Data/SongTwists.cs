using System;
using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>The trick that makes a level feel different from the others (one per song).</summary>
    public enum SongTwist : byte
    {
        None,
        GoldenRings,  // gold rings worth x5, collect them all
        Overtime,     // rings slide in from the sides and the top, faster in the last stretch
        Drift,        // the world banks into long turns
        IceSlide,     // every ring glides on ice
        Surge,        // voltage surges: rings arrive much faster
        BossFight,    // the finale is a pixel boss: rings circle it, hits blast it
        SpiritBalls,  // extra balls from the very first beat
        Flip,         // the screen turns upside down
        Mirage,       // rings appear mirrored, then slide home
        Sonar,        // rings hide in the dark until the sonar ping
        PetalFall,    // rings float down into place like petals
        HeavyHits,    // anvil strikes: every other bar is a big hit
        Warp,         // corkscrew through the tunnel
        Stampede,     // double-time drum runs
        DanceCam      // the camera cuts on every bar
    }

    /// <summary>A stretch of song time where the twist is active.</summary>
    [Serializable]
    public struct TwistSpan
    {
        public double start, end;
        public bool Contains(double t) => t >= start && t < end;
    }

    /// <summary>Which twist each song has, and its name and one-line description.</summary>
    public static class SongTwists
    {
        public static SongTwist For(SongData song)
        {
            if (song == null) return SongTwist.None;
            switch (song.name)
            {
                case "Neon Orbit Run": return SongTwist.GoldenRings;
                case "Overtime": return SongTwist.Overtime;
                case "Solar Drift": return SongTwist.Drift;
                case "Glacier Bloom": return SongTwist.IceSlide;
                case "Voltage Run": return SongTwist.Surge;
                case "8-Bit Hero": return SongTwist.BossFight;
                case "Jade Temple": return SongTwist.SpiritBalls;
                case "Pixel Arcade": return SongTwist.Flip;
                case "Desert Mirage": return SongTwist.Mirage;
                case "Deep Ocean": return SongTwist.Sonar;
                case "Sakura Funk": return SongTwist.PetalFall;
                case "Thunder Forge": return SongTwist.HeavyHits;
                case "Cosmic Trance": return SongTwist.Warp;
                case "Jungle Pulse": return SongTwist.Stampede;
                case "Midnight Disco": return SongTwist.DanceCam;
                default: return SongTwist.None;
            }
        }

        public static string Title(SongTwist t)
        {
            switch (t)
            {
                case SongTwist.GoldenRings: return "GOLDEN RINGS";
                case SongTwist.Overtime: return "SLIDE RUSH";
                case SongTwist.Drift: return "DRIFT";
                case SongTwist.IceSlide: return "ICE SLIDE";
                case SongTwist.Surge: return "VOLTAGE SURGE";
                case SongTwist.BossFight: return "BOSS FIGHT";
                case SongTwist.SpiritBalls: return "SPIRIT BALLS";
                case SongTwist.Flip: return "GRAVITY FLIP";
                case SongTwist.Mirage: return "MIRAGE";
                case SongTwist.Sonar: return "SONAR";
                case SongTwist.PetalFall: return "PETAL FALL";
                case SongTwist.HeavyHits: return "ANVIL STRIKES";
                case SongTwist.Warp: return "WARP TUNNEL";
                case SongTwist.Stampede: return "STAMPEDE";
                case SongTwist.DanceCam: return "DANCE CAM";
                default: return string.Empty;
            }
        }

        public static string Blurb(SongTwist t)
        {
            switch (t)
            {
                case SongTwist.GoldenRings: return "gold rings score x5";
                case SongTwist.Overtime: return "rings slide in from the sides and top";
                case SongTwist.Drift: return "the world banks into turns";
                case SongTwist.IceSlide: return "every ring glides on ice";
                case SongTwist.Surge: return "surges make rings rush in";
                case SongTwist.BossFight: return "rings circle the boss: blast it";
                case SongTwist.SpiritBalls: return "extra balls from the first beat";
                case SongTwist.Flip: return "the screen turns upside down";
                case SongTwist.Mirage: return "rings appear mirrored, then slide home";
                case SongTwist.Sonar: return "rings hide until the sonar ping";
                case SongTwist.PetalFall: return "rings float down into place";
                case SongTwist.HeavyHits: return "every other bar hits like a hammer";
                case SongTwist.Warp: return "corkscrew through the tunnel";
                case SongTwist.Stampede: return "double-time drum runs";
                case SongTwist.DanceCam: return "the camera cuts on every bar";
                default: return string.Empty;
            }
        }

        /// <summary>Colour of the twist's badge and banner.</summary>
        public static Color Tint(SongTwist t)
        {
            switch (t)
            {
                case SongTwist.GoldenRings: return new Color(1f, 0.82f, 0.3f);
                case SongTwist.Overtime: return new Color(1f, 0.35f, 0.35f);
                case SongTwist.BossFight: return new Color(1f, 0.3f, 0.25f);
                case SongTwist.SpiritBalls: return new Color(0.45f, 1f, 0.65f);
                case SongTwist.Sonar: return new Color(0.3f, 0.95f, 1f);
                case SongTwist.Surge: return new Color(1f, 0.95f, 0.3f);
                default: return new Color(0.75f, 0.6f, 1f);
            }
        }
    }

    /// <summary>
    /// Builds each song's twist into its chart: which phrases it reshapes, which notes it changes, and the
    /// time spans where it is active. Like the rest of the chart it is fully deterministic per song.
    /// </summary>
    public static class TwistDesigner
    {
        private const int PhraseBeats = 32;

        /// <summary>Before notes are placed: twists that change which phrases get companions, tunnel or runs.</summary>
        public static void ShapePhrases(SongTwist twist, float[] phraseEnergy, bool[] companion, bool[] tunnel, bool[] runs)
        {
            int n = phraseEnergy.Length;
            switch (twist)
            {
                case SongTwist.SpiritBalls:
                    // Spirit balls fly with you from the very first phrase to the end.
                    for (int p = 0; p < n; p++)
                    {
                        companion[p] = true;
                        tunnel[p] = false;
                    }
                    break;
                case SongTwist.Warp:
                    // Every lively phrase warps into the tunnel; no companions in the way.
                    for (int p = 0; p < n; p++)
                    {
                        companion[p] = false;
                        tunnel[p] = p > 0 && phraseEnergy[p] >= 0.4f;
                    }
                    break;
                case SongTwist.Stampede:
                    // The phrases that would get companion balls get drum runs instead.
                    for (int p = 0; p < n; p++)
                    {
                        runs[p] = companion[p] || (p > 0 && phraseEnergy[p] >= 0.45f);
                        companion[p] = false;
                    }
                    break;
            }
        }

        /// <summary>Stampede: the 4th and 8th bar of each run phrase become a run of quick notes (eighths, quarters on Easy).</summary>
        public static void AddStampedeRuns(SongData song, Difficulty difficulty, List<ChartNote> notes, bool[] runs, Chart chart)
        {
            int n = song.beatTimes.Length;
            bool easy = difficulty == Difficulty.Easy;
            for (int p = 0; p < runs.Length; p++)
            {
                if (!runs[p]) continue;
                for (int half = 0; half < 2; half++)
                {
                    int b0 = song.downbeatPhase + p * PhraseBeats + half * 16 + 12;
                    if (b0 < 1 || b0 + 4 >= n) continue;
                    double t0 = song.BeatTime(b0), t1 = song.BeatTime(b0 + 4);
                    // Only where the song is already being played (no runs in an empty break).
                    bool any = false;
                    foreach (ChartNote c in notes) if (c.time >= t0 - song.SecondsPerBeat * 4 && c.time < t1) { any = true; break; }
                    if (!any) continue;
                    notes.RemoveAll(c => c.time >= t0 - 1e-4 && c.time < t1 - 1e-4);
                    for (int b = b0; b < b0 + 4; b++)
                    {
                        notes.Add(new ChartNote
                        {
                            time = song.BeatTime(b),
                            beat = b,
                            kind = song.IsDownbeat(b) ? NoteKind.Downbeat : NoteKind.Beat,
                            type = NoteType.Tap,
                            impact = b == b0
                        });
                        if (easy) continue;
                        notes.Add(new ChartNote
                        {
                            time = 0.5 * (song.BeatTime(b) + song.BeatTime(b + 1)),
                            beat = b,
                            kind = NoteKind.Offbeat,
                            type = NoteType.Tap
                        });
                    }
                    chart.twistSpans.Add(new TwistSpan { start = t0, end = t1 });
                }
            }
            notes.Sort((x, y) => x.time.CompareTo(y.time));
        }

        /// <summary>After the chart is built: twist spans, note changes and extra camera cues.</summary>
        public static void Finish(SongData song, Difficulty difficulty, Chart chart, float[] phraseEnergy, bool[] companion)
        {
            SongTwist twist = chart.twist;
            var notes = chart.notes;
            if (twist == SongTwist.None || notes.Count == 0) return;
            double spb = song.SecondsPerBeat;
            double first = notes[0].time, last = notes[notes.Count - 1].time;
            int phrases = phraseEnergy.Length;
            var spans = chart.twistSpans;
            float Pe(int p) => p >= 0 && p < phrases ? phraseEnergy[p] : 0f;
            double PhraseStart(int p) => song.BeatTime(song.downbeatPhase + p * PhraseBeats);
            int PhraseOf(double t) => Mathf.FloorToInt((song.BeatIndexAt(t + 1e-4) - song.downbeatPhase) / (float)PhraseBeats);
            void AddPhrases(Func<int, bool> pick)
            {
                for (int p = 0; p < phrases; p++)
                {
                    if (!pick(p)) continue;
                    double s = PhraseStart(p), e = PhraseStart(p + 1);
                    if (e < first || s > last) continue;
                    if (spans.Count > 0 && Math.Abs(spans[spans.Count - 1].end - s) < 1e-3)
                    {
                        TwistSpan prev = spans[spans.Count - 1];
                        prev.end = e;
                        spans[spans.Count - 1] = prev;
                    }
                    else spans.Add(new TwistSpan { start = s, end = e });
                }
            }
            void Whole() => spans.Add(new TwistSpan { start = first - spb * 4, end = last + 1.0 });
            // The finale: from the phrase holding the note at the given fraction of the chart to the end.
            void Finale(float fraction)
            {
                int p = PhraseOf(notes[Mathf.Clamp((int)(notes.Count * fraction), 0, notes.Count - 1)].time);
                spans.Add(new TwistSpan { start = Math.Max(first, PhraseStart(Math.Max(1, p))), end = last + 1.0 });
            }

            switch (twist)
            {
                case SongTwist.GoldenRings:
                    Whole();
                    MarkGolden(song, chart);
                    break;
                case SongTwist.Overtime:
                    // The first phrase plays it straight so the player finds the beat; then rings slide in.
                    spans.Add(new TwistSpan { start = Math.Max(first, PhraseStart(1)), end = last + 1.0 });
                    break;
                case SongTwist.BossFight:
                {
                    Finale(0.72f);
                    // The boss arena: the finale's rings circle the boss, so only the main ball plays (no
                    // companions), only taps (the rings keep turning), and the camera holds the side view.
                    TwistSpan arena = spans[spans.Count - 1];
                    notes.RemoveAll(c => c.ball != 0 && c.time >= arena.start - spb * 2);
                    TapsOnly(chart, spb);
                    for (int i = 0; i < notes.Count; i++)
                    {
                        if (notes[i].time < arena.start || !notes[i].moving) continue;
                        ChartNote c = notes[i];
                        c.moving = false;
                        notes[i] = c;
                    }
                    var cues = chart.cameraCues;
                    cues.RemoveAll(c => c.time >= arena.start - spb * 4);
                    cues.Add(new CameraCue { time = arena.start - spb * 4, mode = CameraMode.Side });
                    cues.Sort((a, b) => a.time.CompareTo(b.time));
                    break;
                }
                case SongTwist.Drift:
                    AddPhrases(p => p > 0 && Pe(p) >= 0.35f);
                    break;
                case SongTwist.IceSlide:
                    Whole();
                    for (int i = 0; i < notes.Count; i++)
                    {
                        ChartNote c = notes[i];
                        int p = PhraseOf(c.time);
                        if (c.ball != 0 || c.type != NoteType.Tap || c.impact || (p >= 0 && p < phrases && companion[p])) continue;
                        c.moving = true;
                        notes[i] = c;
                    }
                    break;
                case SongTwist.Surge:
                    AddPhrases(p => p > 0 && Pe(p) >= 0.6f);
                    if (spans.Count == 0) AddPhrases(p => p == Loudest(phraseEnergy));
                    break;
                case SongTwist.SpiritBalls:
                case SongTwist.PetalFall:
                    Whole();
                    break;
                case SongTwist.Flip:
                {
                    // Four bars upright, four bars upside down, in every phrase with some drive.
                    for (int p = 1; p < phrases; p++)
                    {
                        if (Pe(p) < 0.3f) continue;
                        double s = song.BeatTime(song.downbeatPhase + p * PhraseBeats + 16), e = PhraseStart(p + 1);
                        if (s >= first && e <= last + spb * 4) spans.Add(new TwistSpan { start = s, end = e });
                    }
                    if (spans.Count == 0) AddPhrases(p => p == Loudest(phraseEnergy));
                    TapsOnly(chart, spb); // upside down, left and right swap on screen
                    break;
                }
                case SongTwist.Mirage:
                    AddPhrases(p => p > 0 && Pe(p) >= 0.4f);
                    if (spans.Count == 0) Whole();
                    break;
                case SongTwist.Sonar:
                    // The first phrase stays lit so the player can find the rhythm, then the lights go out.
                    spans.Add(new TwistSpan { start = Math.Max(first, PhraseStart(1)), end = last + 1.0 });
                    break;
                case SongTwist.HeavyHits:
                    AddPhrases(p => Pe(p) >= 0.5f);
                    for (int i = 0; i < notes.Count; i++)
                    {
                        ChartNote c = notes[i];
                        if (c.ball != 0 || c.kind == NoteKind.Offbeat || c.type == NoteType.Dual) continue;
                        if (((c.beat - song.downbeatPhase) % 8 + 8) % 8 != 0 || !InSpans(spans, c.time)) continue;
                        c.impact = true;
                        notes[i] = c;
                    }
                    break;
                case SongTwist.Warp:
                    foreach (CameraCue cue in chart.cameraCues)
                    {
                        if (cue.mode != CameraMode.Tunnel) continue;
                        double end = last + 1.0;
                        foreach (CameraCue other in chart.cameraCues)
                        {
                            if (other.time > cue.time && other.mode != CameraMode.Tunnel) { end = other.time; break; }
                        }
                        spans.Add(new TwistSpan { start = cue.time, end = end });
                    }
                    TapsOnly(chart, spb); // the screen turns, so screen-space swipes and sides would be wrong
                    break;
                case SongTwist.Stampede:
                    break; // spans were added with the runs
                case SongTwist.DanceCam:
                    AddPhrases(p => p > 0 && Pe(p) >= 0.4f && !companion[p]);
                    AddDanceCuts(song, chart);
                    break;
            }
            spans.Sort((a, b) => a.start.CompareTo(b.start));
        }

        /// <summary>Inside the spans (where the screen rolls) split rings and swipe dashes become plain taps.</summary>
        private static void TapsOnly(Chart chart, double spb)
        {
            var notes = chart.notes;
            for (int i = 0; i < notes.Count; i++)
            {
                ChartNote c = notes[i];
                if (c.type != NoteType.Dual && c.type != NoteType.Swipe) continue;
                foreach (TwistSpan s in chart.twistSpans)
                {
                    if (c.time < s.start - spb * 2 || c.time > s.end + spb) continue;
                    c.type = NoteType.Tap;
                    c.swipe = SwipeDir.None;
                    notes[i] = c;
                    break;
                }
            }
        }

        public static bool InSpans(List<TwistSpan> spans, double t)
        {
            foreach (TwistSpan s in spans) if (s.Contains(t)) return true;
            return false;
        }

        private static int Loudest(float[] pe)
        {
            int best = 1;
            for (int p = 1; p < pe.Length; p++) if (pe[p] > pe[best]) best = p;
            return best;
        }

        /// <summary>One gold ring per phrase: the main ball's hardest-hitting tap in it.</summary>
        private static void MarkGolden(SongData song, Chart chart)
        {
            var notes = chart.notes;
            int bestIdx = -1, curPhrase = int.MinValue;
            float bestScore = -1f;
            void Commit()
            {
                if (bestIdx < 0) return;
                ChartNote g = notes[bestIdx];
                g.golden = true;
                notes[bestIdx] = g;
                chart.goldenCount++;
            }
            for (int i = 0; i < notes.Count; i++)
            {
                ChartNote c = notes[i];
                int p = (c.beat - song.downbeatPhase) / PhraseBeats;
                if (c.beat < song.downbeatPhase) continue;
                if (p != curPhrase)
                {
                    Commit();
                    curPhrase = p;
                    bestIdx = -1;
                    bestScore = -1f;
                }
                if (c.ball != 0 || c.type != NoteType.Tap || c.impact || c.kind == NoteKind.Offbeat) continue;
                float s = (c.beat >= 0 && song.kickStrength != null && c.beat < song.kickStrength.Length ? song.kickStrength[c.beat] : 0f) +
                          (c.kind == NoteKind.Downbeat ? 0.5f : 0f);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestIdx = i;
                }
            }
            Commit();
        }

        /// <summary>Dance cam: inside its spans the camera cuts to a new angle on every bar.</summary>
        private static void AddDanceCuts(SongData song, Chart chart)
        {
            var cues = chart.cameraCues;
            CameraMode[] cycle = { CameraMode.AngledRight, CameraMode.Side, CameraMode.AngledLeft, CameraMode.Side };
            foreach (TwistSpan s in chart.twistSpans)
            {
                CameraMode after = ModeAt(cues, s.end);
                cues.RemoveAll(c => c.time >= s.start && c.time <= s.end);
                int k0 = song.BeatIndexAt(s.start + 1e-4);
                int bar = 0;
                for (int k = k0; song.BeatTime(k) < s.end - 1e-4; k += 4, bar++)
                {
                    cues.Add(new CameraCue { time = song.BeatTime(k), mode = cycle[bar % cycle.Length], cut = true });
                }
                cues.Add(new CameraCue { time = s.end, mode = after });
            }
            cues.Sort((a, b) => a.time.CompareTo(b.time));
        }

        private static CameraMode ModeAt(List<CameraCue> cues, double t)
        {
            CameraMode mode = CameraMode.Side;
            foreach (CameraCue c in cues)
            {
                if (c.time > t) break;
                mode = c.mode;
            }
            return mode;
        }

        /// <summary>Per-ring effects for the layout (after rings are placed).</summary>
        /// <summary>Boss rings cycle through an 8-bit palette round the circle.</summary>
        private static readonly Color[] BossPalette =
        {
            new Color(2.8f, 0.45f, 0.3f, 1f),  // red
            new Color(2.8f, 1.8f, 0.25f, 1f),  // gold
            new Color(0.35f, 2.2f, 2.8f, 1f),  // cyan
            new Color(2.4f, 0.45f, 2.6f, 1f),  // magenta
            new Color(0.6f, 2.7f, 0.6f, 1f)    // green
        };

        /// <summary>
        /// Overtime: from this time on (the phrase holding the chart's last 30%) the rings land later, halfway
        /// through their timing circle instead of as it appears: the clock is running out.
        /// </summary>
        public static double OvertimeRushStart(SongData song, Chart chart)
        {
            var notes = chart.notes;
            if (notes.Count == 0) return double.PositiveInfinity;
            double t = notes[Mathf.Clamp((int)(notes.Count * 0.7f), 0, notes.Count - 1)].time;
            int p = Mathf.FloorToInt((song.BeatIndexAt(t + 1e-4) - song.downbeatPhase) / (float)PhraseBeats);
            return song.BeatTime(song.downbeatPhase + Math.Max(1, p) * PhraseBeats);
        }

        public static void Decorate(SongData song, Chart chart, List<PlayNote> rings, float halfWidth)
        {
            SongTwist twist = chart.twist;
            float spb = song.SecondsPerBeat;
            double rush = twist == SongTwist.Overtime ? OvertimeRushStart(song, chart) : double.PositiveInfinity;
            int orbit = 0;
            foreach (PlayNote n in rings)
            {
                bool on = InSpans(chart.twistSpans, n.time);
                if (n.golden) n.twistTint = new Color(2.8f, 2.0f, 0.35f, 1f);
                if (!on) continue;
                switch (twist)
                {
                    case SongTwist.Surge:
                        n.approachScale = 0.6f;
                        break;
                    case SongTwist.BossFight:
                        if (n.fx == NoteFx.Boss) n.twistTint = BossPalette[orbit++ % BossPalette.Length];
                        break;
                    case SongTwist.Overtime:
                    {
                        // Drum & bass, read from the drums: the big hits and the kick opening every other bar
                        // drop in from the top; every other ring is swept in from its own side of the screen
                        // like a snare (centre rings alternate sides by beat). Each slide takes one beat and
                        // lands on a beat of the grid: as its timing circle appears, or (in the overtime
                        // stretch) halfway through it, so the motion itself keeps time.
                        if (n.ball != 0 || n.type == NoteType.Dual) break;
                        int bar = (song.BeatIndexAt(n.time + 1e-4) - song.downbeatPhase) / 4;
                        bool top = n.impact || (n.kind == NoteKind.Downbeat && (bar & 1) == 0);
                        if (top) n.fxFrom = new Vector3(0f, 14f, 0f); // starts above the top of the screen
                        else
                        {
                            float side = Mathf.Abs(n.position.x) > 0.3f ? Mathf.Sign(n.position.x)
                                : (Mathf.RoundToInt((float)(n.time / spb)) & 1) == 0 ? 1f : -1f;
                            n.fxFrom = new Vector3(side * (halfWidth + 6f) - n.position.x, 0.5f, 0f); // off the screen edge
                        }
                        n.fx = NoteFx.Slide;
                        double approach = LevelLayout.ApproachTime(spb);
                        n.fxSettle = n.time >= rush ? approach * 0.5 : approach;
                        n.fxLead = n.fxSettle + spb;
                        break;
                    }
                    case SongTwist.Mirage:
                        if (n.ball == 0 && Mathf.Abs(n.position.x) > 0.4f)
                        {
                            n.fx = NoteFx.Mirage;
                            n.fxLead = spb * 1.75;
                        }
                        break;
                    case SongTwist.PetalFall:
                        if (n.type != NoteType.Hold)
                        {
                            n.fx = NoteFx.Petal;
                            n.fxLead = spb * 2.5;
                        }
                        break;
                    case SongTwist.Sonar:
                        n.hidden = true;
                        break;
                }
            }
        }
    }
}
