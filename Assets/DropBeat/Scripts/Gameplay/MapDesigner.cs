using System;
using UnityEngine;

namespace DropBeat
{
    /// <summary>Ring shapes a musical phrase can be drawn with (see <see cref="MapDesigner"/>).</summary>
    public enum MapPattern
    {
        Pulse,   // calm: small sway around the centre
        Wave,    // smooth sine across the screen, one period per two bars
        Zigzag,  // hard left / right on every beat
        Stairs,  // four steps climbing left to right, then back to the start
        Swing,   // pendulum: wide on beats 1 and 3, through the middle on 2 and 4
        Cross,   // diagonal sweeps, left-to-right then right-to-left
        Spiral   // corkscrew around the camera axis (tunnel view)
    }

    /// <summary>
    /// Designs where each ring sits: no randomness. The music decides the shape:
    ///  - every 8-bar phrase gets a pattern picked from the song's own palette by the phrase's energy
    ///    (calm parts gentle, loud parts wide), tunnel phrases always spiral;
    ///  - inside the pattern a ring's position is a function of its beat in a 2-bar motif, so the same
    ///    rhythm always draws the same shape and returning sections return with the same shape;
    ///  - phrases open from the centre, impact notes sit centre stage.
    /// </summary>
    public static class MapDesigner
    {
        private const int PhraseBeats = 32;
        private const double MotifBeats = 8.0;

        /// <summary>Per-song pattern palette, calm to intense, matched to its background scene.</summary>
        private static MapPattern[] Palette(BackdropScene scene)
        {
            switch (scene)
            {
                case BackdropScene.Outrun: return new[] { MapPattern.Wave, MapPattern.Zigzag, MapPattern.Swing, MapPattern.Cross };
                case BackdropScene.Aurora: return new[] { MapPattern.Pulse, MapPattern.Wave, MapPattern.Wave, MapPattern.Swing };
                case BackdropScene.Skyline: return new[] { MapPattern.Stairs, MapPattern.Zigzag, MapPattern.Cross, MapPattern.Swing };
                case BackdropScene.MistyMountains: return new[] { MapPattern.Pulse, MapPattern.Wave, MapPattern.Stairs, MapPattern.Swing };
                case BackdropScene.Pixel: return new[] { MapPattern.Stairs, MapPattern.Stairs, MapPattern.Zigzag, MapPattern.Cross };
                case BackdropScene.Ocean: return new[] { MapPattern.Pulse, MapPattern.Wave, MapPattern.Wave, MapPattern.Swing };
                case BackdropScene.Petals: return new[] { MapPattern.Pulse, MapPattern.Wave, MapPattern.Swing, MapPattern.Zigzag };
                case BackdropScene.Volcano: return new[] { MapPattern.Stairs, MapPattern.Zigzag, MapPattern.Cross, MapPattern.Swing };
                case BackdropScene.Galaxy: return new[] { MapPattern.Wave, MapPattern.Swing, MapPattern.Spiral, MapPattern.Cross };
                case BackdropScene.Jungle: return new[] { MapPattern.Wave, MapPattern.Zigzag, MapPattern.Swing, MapPattern.Cross };
                case BackdropScene.Disco: return new[] { MapPattern.Wave, MapPattern.Swing, MapPattern.Cross, MapPattern.Zigzag };
                case BackdropScene.Dunes: return new[] { MapPattern.Pulse, MapPattern.Wave, MapPattern.Swing, MapPattern.Stairs };
                default: return new[] { MapPattern.Pulse, MapPattern.Zigzag, MapPattern.Swing, MapPattern.Cross };
            }
        }

        /// <summary>Pattern of every phrase (index = phrase number from the song's first downbeat).</summary>
        public static MapPattern[] PlanPhrases(SongData song, Chart chart)
        {
            int n = song.beatTimes.Length;
            int phrases = n / PhraseBeats + 2;
            float[] energy = ChartGenerator.SectionEnergy(song);
            var pe = new float[phrases];
            var cnt = new int[phrases];
            for (int k = 0; k < n; k++)
            {
                int p = PhraseOf(song, k);
                if (p < 0 || p >= phrases) continue;
                pe[p] += energy[k];
                cnt[p]++;
            }
            MapPattern[] palette = Palette(song.scene);
            var plan = new MapPattern[phrases];
            for (int p = 0; p < phrases; p++)
            {
                float e = cnt[p] > 0 ? pe[p] / cnt[p] : 0f;
                int tier = e < 0.3f ? 0 : e < 0.5f ? 1 : e < 0.68f ? 2 : 3;
                plan[p] = palette[tier];
                double t = song.BeatTime(song.downbeatPhase + p * PhraseBeats + 1);
                if (CameraAt(chart, t) == CameraMode.Tunnel) plan[p] = MapPattern.Spiral;
            }
            return plan;
        }

        /// <summary>
        /// Designed position for a ring: x in -1..1 (times the lane half-width) and a depth factor -1..1
        /// (used only where the camera shows depth).
        /// </summary>
        public static Vector2 Place(SongData song, MapPattern[] plan, double time, out MapPattern pattern)
        {
            int k = song.BeatIndexAt(time + 1e-4);
            double beatPos = k + (time - song.BeatTime(k)) / Math.Max(1e-4, song.SecondsPerBeat); // beat index incl. fraction
            int phrase = PhraseOf(song, k);
            pattern = phrase >= 0 && phrase < plan.Length ? plan[phrase] : plan[0];
            double rel = beatPos - song.downbeatPhase;
            double inPhrase = rel - Math.Floor(rel / PhraseBeats) * PhraseBeats;
            double u = (inPhrase % MotifBeats) / MotifBeats; // 0..1 across a 2-bar motif
            int beatInMotif = (int)Math.Floor(inPhrase % MotifBeats + 1e-6);
            bool onBeat = Math.Abs(inPhrase - Math.Round(inPhrase)) < 0.01;
            const float TwoPi = Mathf.PI * 2f;

            float x, z;
            switch (pattern)
            {
                case MapPattern.Pulse:
                    x = 0.35f * Mathf.Sin(TwoPi * (float)u * 2f);
                    z = 0.5f * Mathf.Cos(TwoPi * (float)u * 2f);
                    break;
                case MapPattern.Wave:
                    x = 0.9f * Mathf.Sin(TwoPi * (float)u);
                    z = 0.7f * Mathf.Cos(TwoPi * (float)u);
                    break;
                case MapPattern.Zigzag:
                    x = onBeat ? (beatInMotif % 2 == 0 ? -0.85f : 0.85f) : 0f;
                    z = beatInMotif % 2 == 0 ? 0.7f : -0.7f;
                    break;
                case MapPattern.Stairs:
                    x = -0.9f + 1.8f * Mathf.Floor((float)u * 4f) / 3f;
                    z = -0.6f + 1.2f * Mathf.Floor((float)u * 4f) / 3f;
                    break;
                case MapPattern.Swing:
                {
                    // Wide on the strong beats, through the middle on the weak ones.
                    int half = beatInMotif / 2;
                    float side = half % 2 == 0 ? -1f : 1f;
                    x = beatInMotif % 2 == 0 ? 0.9f * side : 0.25f * side;
                    z = beatInMotif % 2 == 0 ? 0.8f : 0f;
                    break;
                }
                case MapPattern.Cross:
                {
                    float f = (float)(u * 2.0 - Math.Floor(u * 2.0));
                    float dir = Math.Floor(u * 2.0) % 2 == 0 ? 1f : -1f;
                    x = (2f * f - 1f) * 0.9f * dir;
                    z = (1f - 2f * f) * 0.7f;
                    break;
                }
                default: // Spiral
                    x = 0.85f * Mathf.Cos(TwoPi * (float)u);
                    z = Mathf.Sin(TwoPi * (float)u);
                    break;
            }

            // Phrase openings start from the centre: a clear "here we go" for each section.
            if (onBeat && inPhrase < 0.5) x *= 0.25f;
            return new Vector2(x, z);
        }

        private static CameraMode CameraAt(Chart chart, double t)
        {
            CameraMode mode = CameraMode.Side;
            foreach (CameraCue cue in chart.cameraCues)
            {
                if (cue.time > t) break;
                mode = cue.mode;
            }
            return mode;
        }

        private static int PhraseOf(SongData song, int k)
        {
            int rel = k - song.downbeatPhase;
            return rel >= 0 ? rel / PhraseBeats : 0;
        }
    }
}
