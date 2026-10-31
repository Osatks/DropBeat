using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Per-song hit instruments. Every song picks a <see cref="HitStyle"/>; its hits, arpeggio and fanfare
    /// are synthesised with that instrument, still tuned to the song's key and climbing its pentatonic scale.
    /// </summary>
    public sealed partial class SfxPlayer
    {
        /// <summary>One note of the song's hit instrument at <paramref name="freq"/>, with a short release so it never ends in a click.</summary>
        private float[] Voice(HitStyle style, float freq, float seconds, System.Random rng)
        {
            float[] y = VoiceRaw(style, freq, seconds, rng);
            FadeEnd(y, 0.012f);
            return y;
        }

        /// <summary>Cosine fade over the last <paramref name="seconds"/> of a buffer.</summary>
        private void FadeEnd(float[] y, float seconds)
        {
            int fade = Mathf.Min(y.Length, Mathf.CeilToInt(_sr * seconds));
            int start = y.Length - fade;
            for (int i = start; i < y.Length; i++) y[i] *= 0.5f * (1f + Mathf.Cos(Mathf.PI * (i - start) / fade));
        }

        private float[] VoiceRaw(HitStyle style, float freq, float seconds, System.Random rng)
        {
            int sr = _sr;
            switch (style)
            {
                case HitStyle.AnalogPluck:
                {
                    return Pluck(sr, freq, seconds, 3500f, 300f, 0.09f, 0.22f, 0.012f, 0.35f);
                }
                case HitStyle.GlassBell:
                {
                    float[] y = Fm(sr, freq, seconds, 3.5f, 2.2f, 0.25f, 0.6f);
                    Mix(y, Sine(sr, seconds, freq * 2.76f, 0.2f), 0.25f);
                    return y;
                }
                case HitStyle.Zap:
                {
                    int n = Samples(seconds);
                    var y = new float[n];
                    double ph = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double f = freq * (1.0 + 2.0 * System.Math.Exp(-t / 0.02));
                        ph += 2 * System.Math.PI * f / sr;
                        y[i] = (float)(System.Math.Tanh(3.0 * System.Math.Sin(ph)) * System.Math.Exp(-t / 0.12) * Attack(t));
                    }
                    return y;
                }
                case HitStyle.Koto:
                    return KarplusStrong(sr, freq, seconds, 0.996f, 0.7f, rng);
                case HitStyle.ChipBlip:
                {
                    int n = Samples(Mathf.Min(seconds, 0.22f));
                    var y = new float[n];
                    double ph = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double f = t < 0.045 ? freq : freq * 2.0; // the classic octave blip
                        ph += f / sr;
                        double sq = Frac(ph) < 0.25 ? 0.75 : -0.25; // 25% pulse, zero mean
                        double env = t < 0.15 ? 1.0 : System.Math.Max(0.0, 1.0 - (t - 0.15) / 0.07);
                        y[i] = (float)(sq * 0.5 * env);
                    }
                    return y;
                }
                case HitStyle.Darbuka:
                {
                    int n = Samples(seconds);
                    var y = HiHat(sr, seconds, 0.03f, rng); // the "tak" slap
                    for (int i = 0; i < y.Length; i++) y[i] *= 0.6f;
                    double ph = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double f = freq * 2.0 * (1.0 + 0.25 * System.Math.Exp(-t / 0.015));
                        ph += 2 * System.Math.PI * f / sr;
                        y[i] += (float)(System.Math.Sin(ph) * System.Math.Exp(-t / 0.09) * Attack(t) * 0.7);
                    }
                    return y;
                }
                case HitStyle.SonarPing:
                {
                    int n = Samples(seconds);
                    var y = new float[n];
                    double ph = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        ph += 2 * System.Math.PI * freq * 2.0 * (1.0 + 0.03 * t / seconds) / sr;
                        y[i] = (float)(System.Math.Sin(ph) * System.Math.Exp(-t / 0.45) * Attack(t));
                    }
                    return Echo(y, sr, 0.18f, 0.15f, 0.06f);
                }
                case HitStyle.Chime:
                {
                    float[] y = Fm(sr, freq * 2f, seconds, 2f, 0.8f, 0.15f, 0.9f);
                    Mix(y, Fm(sr, freq * 4f, seconds, 2f, 0.5f, 0.1f, 0.5f), 0.35f);
                    Mix(y, Fm(sr, freq * 6f, seconds, 2f, 0.3f, 0.08f, 0.3f), 0.15f);
                    return y;
                }
                case HitStyle.Anvil:
                {
                    int n = Samples(seconds);
                    var y = new float[n];
                    float[] ratios = { 1f, 2.32f, 3.86f, 5.41f, 7.13f };
                    float[] taus = { 0.5f, 0.3f, 0.2f, 0.12f, 0.08f };
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double s = 0;
                        for (int k = 0; k < ratios.Length; k++) s += System.Math.Sin(2 * System.Math.PI * freq * ratios[k] * t) * System.Math.Exp(-t / taus[k]) / (k + 1);
                        y[i] = (float)(System.Math.Tanh(1.6 * s) * Attack(t));
                    }
                    return y;
                }
                case HitStyle.SupersawStab:
                {
                    int n = Samples(seconds);
                    var y = new float[n];
                    var ph = new double[7];
                    for (int v = 0; v < ph.Length; v++) ph[v] = rng.NextDouble();
                    double low = 0, band = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double osc = 0;
                        for (int v = 0; v < ph.Length; v++)
                        {
                            ph[v] += freq * (1.0 + (v - 3) * 0.0035) / sr;
                            osc += Saw(ph[v]);
                        }
                        osc /= ph.Length;
                        double fc = 1200 + 4800 * System.Math.Exp(-t / 0.15);
                        double ff = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
                        low += ff * band;
                        band += ff * (osc - low - 0.5 * band);
                        y[i] = (float)(low * System.Math.Exp(-t / 0.18) * Attack(t));
                    }
                    return Echo(y, sr, 0.14f, 0.35f, 0.18f);
                }
                case HitStyle.Marimba:
                {
                    float[] y = Sine(sr, seconds, freq, 0.35f);
                    Mix(y, Sine(sr, seconds, freq * 4f, 0.06f), 0.35f);
                    Mix(y, Sine(sr, seconds, freq * 10f, 0.02f), 0.1f);
                    float[] mallet = HiHat(sr, 0.01f, 0.002f, rng);
                    Mix(y, mallet, 0.25f);
                    return y;
                }
                case HitStyle.Clav:
                {
                    int n = Samples(seconds);
                    var y = new float[n];
                    double ph = 0, low = 0, band = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        ph += freq / sr;
                        double pulse = Frac(ph) < 0.2 ? 1.0 : -0.25;
                        double fc = 900 + 1800 * System.Math.Exp(-t / 0.05);
                        double ff = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
                        low += ff * band;
                        band += ff * (pulse - low - 0.18 * band); // high resonance: the "wah"
                        y[i] = (float)((0.6 * low + 0.5 * band) * System.Math.Exp(-t / 0.12) * Attack(t)); // body + wah, peaks at once
                    }
                    return y;
                }
                default:
                    return Pluck(sr, freq, seconds, 8000f, 550f, 0.075f, 0.2f, 0.007f, 0.25f);
            }
        }

        /// <summary>The hit's "tak": a short bright transient tinted to the instrument family.</summary>
        private float[] SnapFor(HitStyle style, System.Random rng)
        {
            int sr = _sr;
            switch (style)
            {
                case HitStyle.Marimba:
                case HitStyle.Koto:
                case HitStyle.Darbuka:
                    return Snap(sr, 2800f, 2f, 0.0025f, rng); // wood
                case HitStyle.Anvil:
                case HitStyle.Chime:
                case HitStyle.GlassBell:
                {
                    // Metal: three bright partials plus a little noise.
                    int n = Samples(0.03f);
                    var y = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        double t = i / (double)sr;
                        double s = System.Math.Sin(2 * System.Math.PI * 3100 * t) + System.Math.Sin(2 * System.Math.PI * 4600 * t) + System.Math.Sin(2 * System.Math.PI * 6300 * t);
                        y[i] = (float)(s / 3.0 * System.Math.Exp(-t / 0.006) * System.Math.Min(1.0, t / 0.0003));
                    }
                    Mix(y, Snap(sr, 4500f, 0.8f, 0.0035f, rng), 0.3f);
                    return y;
                }
                case HitStyle.ChipBlip:
                case HitStyle.Zap:
                {
                    // Bit: sample-and-hold noise at 6 kHz.
                    int n = Samples(0.025f);
                    var y = new float[n];
                    int hold = Mathf.Max(1, sr / 6000);
                    float v = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        if (i % hold == 0) v = rng.NextDouble() < 0.5 ? -1f : 1f;
                        double t = i / (double)sr;
                        y[i] = (float)(v * System.Math.Exp(-t / 0.004));
                    }
                    return y;
                }
                case HitStyle.SonarPing:
                {
                    float[] y = Snap(sr, 3500f, 1.2f, 0.0035f, rng);
                    for (int i = 0; i < y.Length; i++) y[i] *= 0.6f;
                    return y;
                }
                default:
                    return Snap(sr, 4500f, 0.8f, 0.0035f, rng); // synth
            }
        }

        private float[] StyledArpeggio(HitStyle style, float[] freqs, int[] steps, float spacing, float seconds, System.Random rng)
        {
            float[] y = new float[(int)(_sr * seconds)];
            for (int a = 0; a < steps.Length; a++)
            {
                float f = freqs[Mathf.Min(ScaleSteps - 1, steps[a])];
                MixAt(y, Voice(style, f, 0.4f, rng), (int)(a * spacing * _sr), 0.8f);
            }
            FadeEnd(y, 0.015f);
            return Echo(y, _sr, 0.11f, 0.3f, 0.12f);
        }

        // ------------------------------------------------------------------ building blocks

        private int Samples(float seconds) => Mathf.CeilToInt(_sr * seconds);

        private static double Attack(double t) => System.Math.Min(1.0, t / 0.0015);

        /// <summary>Two-operator FM: carrier at <paramref name="freq"/>, modulator at freq x ratio, index decaying.</summary>
        private static float[] Fm(int sr, float freq, float seconds, float ratio, float index, float indexTau, float ampTau)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double mod = index * System.Math.Exp(-t / indexTau) * System.Math.Sin(2 * System.Math.PI * freq * ratio * t);
                y[i] = (float)(System.Math.Sin(2 * System.Math.PI * freq * t + mod) * System.Math.Exp(-t / ampTau) * Attack(t));
            }
            return y;
        }

        /// <summary>Plucked string (Karplus-Strong): a noise burst ringing through a tuned, damped delay line.</summary>
        private static float[] KarplusStrong(int sr, float freq, float seconds, float decay, float brightness, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            int period = Mathf.Max(2, Mathf.RoundToInt(sr / freq));
            var line = new float[period];
            for (int i = 0; i < period; i++) line[i] = (float)(rng.NextDouble() * 2 - 1);
            var y = new float[n];
            int idx = 0;
            for (int i = 0; i < n; i++)
            {
                int next = (idx + 1) % period;
                float s = line[idx];
                line[idx] = decay * (brightness * s + (1f - brightness) * 0.5f * (s + line[next]));
                y[i] = s;
                idx = next;
            }
            return y;
        }
    }
}
