using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DropBeat
{
    /// <summary>Result of analysing one song: a beat grid plus per-beat feature strengths.</summary>
    [Serializable]
    public sealed class BeatAnalysis
    {
        public float bpm;
        public float duration;
        public float[] beatTimes = Array.Empty<float>();
        /// <summary>Broadband onset strength at each beat, ~0..1.</summary>
        public float[] beatStrength = Array.Empty<float>();
        /// <summary>Kick-band (30-250 Hz) onset strength at each beat, ~0..1.</summary>
        public float[] kickStrength = Array.Empty<float>();
        /// <summary>Broadband onset strength half a beat after each beat (off-beat eighth), ~0..1.</summary>
        public float[] offStrength = Array.Empty<float>();
        /// <summary>RMS loudness of the span [beat, next beat), ~0..1.</summary>
        public float[] loudness = Array.Empty<float>();
        /// <summary>Beat k is a bar downbeat when (k - downbeatPhase) % 4 == 0.</summary>
        public int downbeatPhase;
        /// <summary>Kick score of the chosen beat phase divided by the off-beat phase. Higher = surer.</summary>
        public float phaseConfidence;
        /// <summary>Largest local deviation of the onsets from a rigid grid, in ms.</summary>
        public float driftMs;
        public bool driftCorrected;
        public float detectorBiasMs;
        public string log = string.Empty;
    }

    /// <summary>
    /// Offline beat tracker tuned for produced music with a steady tempo.
    ///
    /// Pipeline:
    ///  1. Spectral-flux onset envelopes (broadband, sub 30-90 Hz, kick 90-250 Hz) at ~5 ms resolution.
    ///  2. Coarse tempo from the onset autocorrelation with a log-normal tempo prior.
    ///  3. Fine tempo (0.0005 BPM) by maximising the sharpness of the onset envelope folded by the beat period.
    ///  4. Beat vs off-beat: the folded broadband envelope has two candidate phases half a beat apart;
    ///     the one carrying the kick drum is the beat. (Off-beat hats/bass often dominate broadband flux,
    ///     which is why picking the loudest transient puts notes on the wrong half of the beat.)
    ///  5. The detector's own timing bias is measured on a synthetic kick track and removed.
    ///  6. Per-segment phase check; only if the song really drifts is the grid bent locally.
    /// </summary>
    public static class BeatAnalyzer
    {
        public const int WindowSize = 1024;
        public const int HopSize = 128;

        [Serializable]
        public struct Settings
        {
            public float bpmOverride;
            public bool flipHalfBeat;
            public float minBpm;
            public float maxBpm;

            public static Settings Default => new Settings { minBpm = 70f, maxBpm = 190f };
        }

        private sealed class Onsets
        {
            public float[] time;
            public float[] broad;
            public float[] sub;
            public float[] kick;
            public float hop;
        }

        public static BeatAnalysis Analyze(float[] mono, int sampleRate, Settings settings)
        {
            if (mono == null || mono.Length < sampleRate * 4)
            {
                throw new ArgumentException("Need at least 4 seconds of audio to analyse.");
            }

            if (settings.minBpm <= 0f) settings.minBpm = 70f;
            if (settings.maxBpm <= settings.minBpm) settings.maxBpm = 190f;

            var log = new StringBuilder();
            var result = new BeatAnalysis();
            int factor = Math.Max(1, (int)Math.Round(sampleRate / 22050.0));
            float[] x = Downsample(mono, factor);
            int sr = sampleRate / factor;
            result.duration = mono.Length / (float)sampleRate;

            float bias = MeasureDetectorBias(sr);
            result.detectorBiasMs = bias * 1000f;

            Onsets on = ComputeOnsets(x, sr);
            float[] ob = Normalize(on.broad, on.hop);
            float[] osub = Normalize(on.sub, on.hop);
            float[] okick = Normalize(on.kick, on.hop);

            float bpm;
            if (settings.bpmOverride > 0f)
            {
                bpm = settings.bpmOverride;
                log.AppendLine("BPM override " + F(bpm, 4));
            }
            else
            {
                float[] combined = new float[ob.Length];
                for (int i = 0; i < ob.Length; i++) combined[i] = ob[i] + osub[i] + okick[i];
                float coarse = EstimateTempoAcf(combined, on.hop, settings.minBpm, settings.maxBpm);
                bpm = RefineTempo(on.time, ob, coarse);
                log.AppendLine("Tempo: coarse " + F(coarse, 2) + " -> fine " + F(bpm, 4) + " BPM");
            }

            result.bpm = bpm;
            float period = 60f / bpm;

            // Two candidate phases half a beat apart; the kick decides which one is the beat.
            var foldBroad = new Fold(on.time, ob, period);
            float p1 = foldBroad.PeakNear(foldBroad.ArgMaxPhase(), period / 8f, out _);
            float p2 = foldBroad.PeakNear(Wrap(p1 + period * 0.5f, period), period / 8f, out _);
            var foldKick = new Fold(on.time, okick, period);
            var foldSub = new Fold(on.time, osub, period);
            float k1 = KickScore(foldKick, foldSub, p1);
            float k2 = KickScore(foldKick, foldSub, p2);
            bool first = k1 >= k2;
            if (settings.flipHalfBeat) first = !first;
            float phase = first ? p1 : p2;
            result.phaseConfidence = Math.Max(k1, k2) / Math.Max(1e-5f, Math.Min(k1, k2));
            log.AppendLine("Phase candidates " + F(p1 * 1000f, 1) + "ms (kick " + F(k1, 2) + ") / " +
                           F(p2 * 1000f, 1) + "ms (kick " + F(k2, 2) + ") -> " + F(phase * 1000f, 1) + "ms" +
                           (settings.flipHalfBeat ? " [flipped]" : string.Empty) +
                           ", confidence " + F(result.phaseConfidence, 2));

            // Segment drift check.
            var centers = new List<float>();
            var offsets = new List<float>();
            MeasureSegmentDrift(on, ob, period, phase, centers, offsets);
            float median = Median(offsets);
            float drift = 0f;
            for (int i = 0; i < offsets.Count; i++) drift = Math.Max(drift, Math.Abs(offsets[i] - median));
            result.driftMs = drift * 1000f;
            bool bend = offsets.Count >= 3 && result.driftMs > 12f;
            float[] smoothOffsets = null;
            if (bend)
            {
                smoothOffsets = new float[offsets.Count];
                for (int i = 0; i < offsets.Count; i++)
                {
                    float a = offsets[Math.Max(0, i - 1)];
                    float b = offsets[i];
                    float c = offsets[Math.Min(offsets.Count - 1, i + 1)];
                    smoothOffsets[i] = Median3(a, b, c) - median;
                }
            }
            result.driftCorrected = bend;
            log.AppendLine("Segment drift max " + F(result.driftMs, 1) + "ms over " + offsets.Count + " segments" +
                           (bend ? " -> grid bent locally" : " -> rigid grid"));

            // Remove detector bias (detector fires slightly before the physical attack).
            float beatPhase = Wrap(phase - bias + median, period);
            log.AppendLine("Detector bias " + F(bias * 1000f, 1) + "ms, first beat at " + F(beatPhase, 4) + "s");

            var beats = new List<float>();
            for (int k = 0; ; k++)
            {
                float t = beatPhase + k * period;
                if (t > result.duration - 0.05f) break;
                if (bend) t += Interp(centers, smoothOffsets, t);
                beats.Add(t);
            }
            result.beatTimes = beats.ToArray();

            int n = beats.Count;
            result.beatStrength = new float[n];
            result.kickStrength = new float[n];
            result.offStrength = new float[n];
            result.loudness = new float[n];
            for (int k = 0; k < n; k++)
            {
                float t = result.beatTimes[k];
                // Onset frames are biased by `bias`, so sample them where the detector actually fires.
                result.beatStrength[k] = MaxNear(on, ob, t + bias, 0.025f);
                result.kickStrength[k] = Math.Max(MaxNear(on, okick, t + bias, 0.03f), 0.5f * MaxNear(on, osub, t + bias, 0.03f));
                result.offStrength[k] = MaxNear(on, ob, t + period * 0.5f + bias, 0.025f);
                result.loudness[k] = Rms(x, sr, t, t + period);
            }

            NormalizeByPercentile(result.beatStrength, 0.95f);
            NormalizeByPercentile(result.kickStrength, 0.95f);
            NormalizeByPercentile(result.offStrength, 0.95f);
            NormalizeByPercentile(result.loudness, 0.95f);

            result.downbeatPhase = EstimateDownbeatPhase(result);
            log.AppendLine("Beats " + n + ", downbeat phase " + result.downbeatPhase);
            result.log = log.ToString();
            return result;
        }

        // ------------------------------------------------------------------ onsets

        private static Onsets ComputeOnsets(float[] x, int sr)
        {
            int n = WindowSize;
            int hop = HopSize;
            int frames = 1 + (x.Length - n) / hop;
            if (frames < 8) throw new ArgumentException("Audio too short.");

            float[] window = new float[n];
            for (int i = 0; i < n; i++) window[i] = 0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * i / (n - 1));

            float binHz = sr / (float)n;
            int half = n / 2;
            int broadLo = Math.Max(1, (int)Math.Ceiling(30f / binHz));
            int subLo = broadLo;
            int subHi = Math.Max(subLo + 1, (int)Math.Ceiling(90f / binHz));
            int kickHi = Math.Max(subHi + 1, (int)Math.Ceiling(250f / binHz));

            var result = new Onsets
            {
                hop = hop / (float)sr,
                time = new float[frames - 1],
                broad = new float[frames - 1],
                sub = new float[frames - 1],
                kick = new float[frames - 1]
            };

            var fft = new FFT(n);
            float[] re = new float[n];
            float[] im = new float[n];
            float[] prev = new float[half + 1];
            float[] cur = new float[half + 1];
            bool havePrev = false;

            for (int f = 0; f < frames; f += 2)
            {
                int offA = f * hop;
                int offB = (f + 1) * hop;
                bool hasB = f + 1 < frames;
                for (int i = 0; i < n; i++)
                {
                    re[i] = x[offA + i] * window[i];
                    im[i] = hasB ? x[offB + i] * window[i] : 0f;
                }

                fft.Transform(re, im);

                for (int pass = 0; pass < (hasB ? 2 : 1); pass++)
                {
                    for (int k = 0; k <= half; k++)
                    {
                        int nk = (n - k) & (n - 1);
                        float mr, mi;
                        if (pass == 0)
                        {
                            mr = 0.5f * (re[k] + re[nk]);
                            mi = 0.5f * (im[k] - im[nk]);
                        }
                        else
                        {
                            mr = 0.5f * (im[k] + im[nk]);
                            mi = -0.5f * (re[k] - re[nk]);
                        }

                        cur[k] = (float)Math.Log(1.0 + 100.0 * Math.Sqrt(mr * mr + mi * mi));
                    }

                    int frame = f + pass;
                    if (havePrev)
                    {
                        float broad = 0f, sub = 0f, kick = 0f;
                        for (int k = broadLo; k <= half; k++)
                        {
                            float d = cur[k] - prev[k];
                            if (d <= 0f) continue;
                            broad += d;
                            if (k < subHi) sub += d;
                            else if (k < kickHi) kick += d;
                        }

                        int o = frame - 1;
                        result.broad[o] = broad;
                        result.sub[o] = sub;
                        result.kick[o] = kick;
                        result.time[o] = (frame * hop + n * 0.5f) / sr;
                    }

                    float[] swap = prev;
                    prev = cur;
                    cur = swap;
                    havePrev = true;
                }
            }

            return result;
        }

        private static float[] Normalize(float[] o, float hop)
        {
            int w = Math.Max(3, (int)(0.25f / hop) | 1);
            int r = w / 2;
            int n = o.Length;
            double[] prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + o[i];

            float[] outp = new float[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - r);
                int b = Math.Min(n, i + r + 1);
                float mean = (float)((prefix[b] - prefix[a]) / (b - a));
                outp[i] = Math.Max(0f, o[i] - mean);
            }

            float p99 = Percentile(outp, 0.99f);
            if (p99 > 1e-9f)
            {
                for (int i = 0; i < n; i++) outp[i] /= p99;
            }

            return outp;
        }

        // ------------------------------------------------------------------ tempo

        private static float EstimateTempoAcf(float[] o, float hop, float minBpm, float maxBpm)
        {
            int n = o.Length;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += o[i];
            mean /= n;

            int m = FFT.NextPow2(2 * n);
            float[] re = new float[m];
            float[] im = new float[m];
            for (int i = 0; i < n; i++) re[i] = (float)(o[i] - mean);

            var fft = new FFT(m);
            fft.Transform(re, im);
            for (int i = 0; i < m; i++)
            {
                re[i] = re[i] * re[i] + im[i] * im[i];
                im[i] = 0f;
            }
            fft.Transform(re, im, inverse: true);

            float best = 120f;
            double bestScore = double.MinValue;
            for (float bpm = minBpm; bpm <= maxBpm; bpm += 0.25f)
            {
                float lag = 60f / bpm / hop;
                double v = InterpArray(re, lag, n) + 0.5 * InterpArray(re, lag * 2f, n) + 0.25 * InterpArray(re, lag * 4f, n);
                double oct = Math.Log(bpm / 120.0, 2.0) / 0.9;
                double score = v * Math.Exp(-0.5 * oct * oct);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = bpm;
                }
            }

            return best;
        }

        private static float RefineTempo(float[] t, float[] o, float coarse)
        {
            float best = coarse;
            float bestValue = float.MinValue;
            for (float bpm = coarse - 1.5f; bpm <= coarse + 1.5f; bpm += 0.01f)
            {
                float v = new Fold(t, o, 60f / bpm).Max();
                if (v > bestValue)
                {
                    bestValue = v;
                    best = bpm;
                }
            }

            float center = best;
            for (float bpm = center - 0.012f; bpm <= center + 0.012f; bpm += 0.0005f)
            {
                float v = new Fold(t, o, 60f / bpm).Max();
                if (v > bestValue)
                {
                    bestValue = v;
                    best = bpm;
                }
            }

            return best;
        }

        private static float KickScore(Fold kick, Fold sub, float phase)
        {
            kick.PeakNear(phase, 0.03f, out float kv);
            sub.PeakNear(phase, 0.03f, out float sv);
            return kv / Math.Max(1e-6f, kick.Mean) + 0.5f * sv / Math.Max(1e-6f, sub.Mean);
        }

        private static void MeasureSegmentDrift(Onsets on, float[] ob, float period, float phase, List<float> centers, List<float> offsets)
        {
            const float segment = 16f;
            float end = on.time[on.time.Length - 1];
            var energies = new List<float>();
            var starts = new List<int>();
            var stops = new List<int>();
            int i = 0;
            for (float s0 = 0f; s0 + segment <= end; s0 += segment)
            {
                while (i < on.time.Length && on.time[i] < s0) i++;
                int a = i;
                int b = a;
                float e = 0f;
                while (b < on.time.Length && on.time[b] < s0 + segment)
                {
                    e += ob[b];
                    b++;
                }

                starts.Add(a);
                stops.Add(b);
                energies.Add(e);
            }

            float avg = 0f;
            for (int s = 0; s < energies.Count; s++) avg += energies[s];
            avg /= Math.Max(1, energies.Count);

            for (int s = 0; s < energies.Count; s++)
            {
                if (energies[s] < 0.25f * avg) continue;
                var fold = new Fold(on.time, ob, period, starts[s], stops[s]);
                float q = fold.PeakNear(phase, 0.03f, out _);
                offsets.Add(WrapSigned(q - phase, period));
                centers.Add(segment * (s + 0.5f));
            }
        }

        /// <summary>
        /// Runs the onset detector on synthetic kicks with known attack times and returns
        /// (detected - true) in seconds. Typically about -10 ms: spectral flux rises as soon as
        /// the attack enters the analysis window, before the window is centred on it.
        /// </summary>
        public static float MeasureDetectorBias(int sr)
        {
            const float period = 0.5f;
            const float phase = 0.25f;
            float total = 0f;
            int shapes = 0;
            for (int shape = 0; shape < 2; shape++)
            {
                float[] x = SynthesizeKickTrack(sr, 16f, period, phase, shape, null);
                Onsets on = ComputeOnsets(x, sr);
                float[] ob = Normalize(on.broad, on.hop);
                var fold = new Fold(on.time, ob, period);
                float p = fold.PeakNear(fold.ArgMaxPhase(), period / 8f, out _);
                total += WrapSigned(p - phase, period);
                shapes++;
            }

            return total / shapes;
        }

        /// <summary>Kick drum track for calibration/validation. shape 0 = punchy, 1 = 808-style.</summary>
        public static float[] SynthesizeKickTrack(int sr, float seconds, float period, float phase, int shape, Random noise)
        {
            float[] x = new float[(int)(sr * seconds)];
            int kickLen = (int)(0.3f * sr);
            float[] kick = new float[kickLen];
            double ph = 0;
            for (int i = 0; i < kickLen; i++)
            {
                double t = i / (double)sr;
                double freq = shape == 0 ? 50 + 150 * Math.Exp(-t * 50) : 45 + 90 * Math.Exp(-t * 30);
                ph += 2 * Math.PI * freq / sr;
                double amp = shape == 0 ? Math.Exp(-t * 14) : Math.Exp(-t * 5);
                kick[i] = (float)(Math.Sin(ph) * amp * 0.9);
            }

            for (int k = 0; ; k++)
            {
                int start = (int)Math.Round((phase + k * period) * sr);
                if (start >= x.Length) break;
                for (int i = 0; i < kickLen && start + i < x.Length; i++) x[start + i] += kick[i];
            }

            var rng = noise ?? new Random(1234);
            for (int i = 0; i < x.Length; i++) x[i] += (float)(rng.NextDouble() - 0.5) * 0.004f;
            return x;
        }

        // ------------------------------------------------------------------ features

        private static float MaxNear(Onsets on, float[] o, float t, float radius)
        {
            float t0 = on.time[0];
            int a = Math.Max(0, (int)Math.Floor((t - radius - t0) / on.hop));
            int b = Math.Min(o.Length - 1, (int)Math.Ceiling((t + radius - t0) / on.hop));
            float m = 0f;
            for (int i = a; i <= b; i++)
            {
                if (Math.Abs(on.time[i] - t) <= radius && o[i] > m) m = o[i];
            }
            return m;
        }

        private static float Rms(float[] x, int sr, float from, float to)
        {
            int a = Math.Max(0, (int)(from * sr));
            int b = Math.Min(x.Length, (int)(to * sr));
            if (b <= a) return 0f;
            double s = 0;
            for (int i = a; i < b; i++) s += x[i] * x[i];
            return (float)Math.Sqrt(s / (b - a));
        }

        private static int EstimateDownbeatPhase(BeatAnalysis a)
        {
            int n = a.beatTimes.Length;
            int best = 0;
            float bestScore = float.MinValue;
            for (int d = 0; d < 4; d++)
            {
                float score = 0f;
                for (int k = 0; k < n; k++)
                {
                    int m = ((k - d) % 4 + 4) % 4;
                    if (m == 0)
                    {
                        score += a.kickStrength[k];
                        if (k > 0) score += 2f * Math.Max(0f, a.loudness[k] - a.loudness[k - 1]);
                    }
                    else if (m == 2)
                    {
                        score += 0.5f * a.kickStrength[k];
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Onset envelope folded modulo one beat period with ~1 ms bins and 4 ms gaussian smoothing.</summary>
        private sealed class Fold
        {
            public readonly float Period;
            public readonly int Bins;
            public readonly float[] H;
            public readonly float Mean;

            public Fold(float[] t, float[] o, float period, int from = 0, int to = -1)
            {
                if (to < 0) to = o.Length;
                Period = period;
                Bins = Math.Max(16, (int)Math.Round(period / 0.001f));
                float[] raw = new float[Bins];
                for (int i = from; i < to; i++)
                {
                    double ph = t[i] % period;
                    if (ph < 0) ph += period;
                    int b = (int)(ph / period * Bins);
                    if (b >= Bins) b = Bins - 1;
                    raw[b] += o[i];
                }

                float sigmaBins = 0.004f / period * Bins;
                int rad = (int)Math.Ceiling(3f * sigmaBins);
                float[] kernel = new float[2 * rad + 1];
                for (int j = -rad; j <= rad; j++) kernel[j + rad] = (float)Math.Exp(-0.5 * (j / sigmaBins) * (j / sigmaBins));

                H = new float[Bins];
                double sum = 0;
                for (int b = 0; b < Bins; b++)
                {
                    float acc = 0f;
                    for (int j = -rad; j <= rad; j++)
                    {
                        int idx = b + j;
                        idx %= Bins;
                        if (idx < 0) idx += Bins;
                        acc += raw[idx] * kernel[j + rad];
                    }
                    H[b] = acc;
                    sum += acc;
                }
                Mean = (float)(sum / Bins);
            }

            public float Max()
            {
                float m = float.MinValue;
                for (int i = 0; i < Bins; i++) if (H[i] > m) m = H[i];
                return m;
            }

            public float ArgMaxPhase()
            {
                int best = 0;
                for (int i = 1; i < Bins; i++) if (H[i] > H[best]) best = i;
                return (best + 0.5f) / Bins * Period;
            }

            public float PeakNear(float phase, float radius, out float value)
            {
                int c = (int)Math.Round(phase / Period * Bins - 0.5f);
                int r = Math.Max(1, (int)Math.Round(radius / Period * Bins));
                int best = Mod(c, Bins);
                for (int j = c - r; j <= c + r; j++)
                {
                    int idx = Mod(j, Bins);
                    if (H[idx] > H[best]) best = idx;
                }

                float y0 = H[Mod(best - 1, Bins)];
                float y1 = H[best];
                float y2 = H[Mod(best + 1, Bins)];
                float den = y0 - 2f * y1 + y2;
                float d = Math.Abs(den) > 1e-12f ? 0.5f * (y0 - y2) / den : 0f;
                d = Math.Max(-0.5f, Math.Min(0.5f, d));
                value = y1;
                return Wrap((best + d + 0.5f) / Bins * Period, Period);
            }

            private static int Mod(int a, int m)
            {
                int r = a % m;
                return r < 0 ? r + m : r;
            }
        }

        private static float[] Downsample(float[] x, int factor)
        {
            if (factor <= 1) return (float[])x.Clone();
            float[] y = new float[x.Length / factor];
            float inv = 1f / factor;
            for (int i = 0; i < y.Length; i++)
            {
                float s = 0f;
                int o = i * factor;
                for (int j = 0; j < factor; j++) s += x[o + j];
                y[i] = s * inv;
            }
            return y;
        }

        private static double InterpArray(float[] a, float pos, int n)
        {
            if (pos < 0 || pos >= n - 1) return 0;
            int i = (int)pos;
            float f = pos - i;
            return a[i] * (1 - f) + a[i + 1] * f;
        }

        private static float Interp(List<float> xs, float[] ys, float x)
        {
            if (xs.Count == 0) return 0f;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[xs.Count - 1]) return ys[xs.Count - 1];
            for (int i = 1; i < xs.Count; i++)
            {
                if (x <= xs[i])
                {
                    float f = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                    return ys[i - 1] + (ys[i] - ys[i - 1]) * f;
                }
            }
            return ys[xs.Count - 1];
        }

        private static float Percentile(float[] a, float q)
        {
            if (a.Length == 0) return 0f;
            float[] c = (float[])a.Clone();
            Array.Sort(c);
            int idx = Math.Min(c.Length - 1, Math.Max(0, (int)(q * (c.Length - 1))));
            return c[idx];
        }

        private static void NormalizeByPercentile(float[] a, float q)
        {
            var nonZero = new List<float>(a.Length);
            for (int i = 0; i < a.Length; i++) if (a[i] > 0f) nonZero.Add(a[i]);
            if (nonZero.Count == 0) return;
            float p = Percentile(nonZero.ToArray(), q);
            if (p <= 1e-9f) return;
            for (int i = 0; i < a.Length; i++) a[i] = Math.Min(1.5f, a[i] / p);
        }

        private static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            var c = new List<float>(v);
            c.Sort();
            return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5f * (c[c.Count / 2 - 1] + c[c.Count / 2]);
        }

        private static float Median3(float a, float b, float c)
        {
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
        }

        public static float Wrap(float v, float p)
        {
            float r = v % p;
            return r < 0 ? r + p : r;
        }

        public static float WrapSigned(float v, float p)
        {
            float r = Wrap(v + p * 0.5f, p) - p * 0.5f;
            return r;
        }

        private static string F(float v, int decimals)
        {
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
    }
}
