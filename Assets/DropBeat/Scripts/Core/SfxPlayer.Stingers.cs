using System.Threading.Tasks;
using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Reward stingers in the current song's key, instrument and tempo: streak level-ups, chord stabs,
    /// section clears, the finale chord, rank stings, new best, and the overdrive gold layer.
    /// Built off the main thread when a song starts (the count-in covers it); each falls back to the
    /// fanfare until it is ready.
    /// </summary>
    public sealed partial class SfxPlayer
    {
        private const int StingSteps = 11;

        private int _stingKey = -1;
        private Task<float[][]> _stingTask;
        private int _stingTaskKey = -1;
        private AudioClip[] _tier = new AudioClip[4];
        private AudioClip[] _stab = new AudioClip[2];
        private AudioClip[] _section = new AudioClip[2];
        private AudioClip[] _finale = new AudioClip[2];
        private AudioClip[] _rank = new AudioClip[4];
        private AudioClip[] _gold = new AudioClip[StingSteps];
        private AudioClip[] _scale = new AudioClip[StingSteps];
        private AudioClip _newBest;

        // Layer fades (misses fade the combo layers out instead of cutting them).
        private float _layerFade = -1f, _layerFadeLen;
        private float[] _layerVol;
        private double[] _layerStart;

        public AudioClip TierSting(int t) => Pick(_tier, t);
        public AudioClip Stab(int i) => Pick(_stab, i);
        public AudioClip SectionSting(bool flawless) => Pick(_section, flawless ? 1 : 0);
        public AudioClip Finale(bool fullCombo) => Pick(_finale, fullCombo ? 1 : 0);
        public AudioClip NewBest => _newBest != null ? _newBest : _fanfare;
        public AudioClip Gold(int step) => Pick(_gold, step);
        /// <summary>A single bright note of the scale (results count-up, stars).</summary>
        public AudioClip ScaleNote(int i) => Pick(_scale, i);

        public AudioClip RankSting(string rank)
        {
            switch (rank)
            {
                case "SS": return Pick(_rank, 3);
                case "S": return Pick(_rank, 2);
                case "A":
                case "B": return Pick(_rank, 1);
                case "C":
                case "D": return Pick(_rank, 0);
                default: return null;
            }
        }

        private AudioClip Pick(AudioClip[] clips, int i)
        {
            AudioClip c = clips[Mathf.Clamp(i, 0, clips.Length - 1)];
            return c != null ? c : _fanfare;
        }

        /// <summary>Starts building the stingers for the current key, instrument and tempo (call after SetKey).</summary>
        public void PrepareStingers(float bpm)
        {
            int key = (((_keyRoot * 2 + (_keyMinor ? 1 : 0)) * 64 + (int)_style) * 400) + Mathf.RoundToInt(bpm);
            if (key == _stingKey || key == _stingTaskKey) return;
            _stingTaskKey = key;
            int root = _keyRoot;
            bool minor = _keyMinor;
            HitStyle style = _style;
            float spb = 60f / Mathf.Max(40f, bpm);
            _stingTask = Task.Run(() => BuildStingers(root, minor, style, spb));
        }

        private void Update()
        {
            if (_stingTask != null && _stingTask.IsCompleted)
            {
                Task<float[][]> task = _stingTask;
                _stingTask = null;
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                {
                    float[][] b = task.Result;
                    int k = 0;
                    for (int i = 0; i < 4; i++) _tier[i] = ToClip("tier" + i, b[k++]);
                    for (int i = 0; i < 2; i++) _stab[i] = ToClip("stab" + i, b[k++]);
                    for (int i = 0; i < 2; i++) _section[i] = ToClip("section" + i, b[k++]);
                    for (int i = 0; i < 2; i++) _finale[i] = ToClip("finale" + i, b[k++]);
                    for (int i = 0; i < 4; i++) _rank[i] = ToClip("rank" + i, b[k++]);
                    _newBest = ToClip("newBest", b[k++]);
                    for (int i = 0; i < StingSteps; i++) _gold[i] = ToClip("gold" + i, b[k++]);
                    for (int i = 0; i < StingSteps; i++) _scale[i] = ToClip("scale" + i, b[k++]);
                    _stingKey = _stingTaskKey;
                }
                else if (task.Exception != null)
                {
                    Debug.LogException(task.Exception);
                }
                _stingTaskKey = -1;
            }
            TickLayerFade();
        }

        /// <summary>Fades the combo layers out over <paramref name="seconds"/> (sounds not started yet stop at once).</summary>
        public void FadeLayer(float seconds)
        {
            if (_layer == null) return;
            if (_layerVol == null || _layerVol.Length != LayerVoices) _layerVol = new float[LayerVoices];
            double now = AudioSettings.dspTime;
            for (int i = 0; i < LayerVoices; i++)
            {
                if (_layerStart != null && _layerStart[i] > now) _layer[i].Stop();
                _layerVol[i] = _layer[i].volume;
            }
            _layerFade = 0f;
            _layerFadeLen = Mathf.Max(0.01f, seconds);
        }

        /// <summary>Remembers when a layer voice starts, so a fade can drop sounds that have not begun.</summary>
        private void NoteLayerStart(int voice, double dspTime)
        {
            if (_layerStart == null || _layerStart.Length != LayerVoices) _layerStart = new double[LayerVoices];
            _layerStart[voice] = dspTime;
        }

        private void TickLayerFade()
        {
            if (_layerFade < 0f || _layer == null) return;
            _layerFade += Time.unscaledDeltaTime;
            float g = 1f - Mathf.Clamp01(_layerFade / _layerFadeLen);
            for (int i = 0; i < LayerVoices; i++) _layer[i].volume = _layerVol[i] * g;
            if (g > 0f) return;
            for (int i = 0; i < LayerVoices; i++)
            {
                _layer[i].Stop();
                _layer[i].volume = _layerVol[i];
            }
            _layerFade = -1f;
        }

        // ------------------------------------------------------------------ synthesis (worker thread: maths only)

        private float[][] BuildStingers(int root, bool minor, HitStyle style, float spb)
        {
            var rng = new System.Random(root * 977 + (minor ? 13 : 5) + (int)style * 31);
            float[] f = StingFreqs(root, minor);
            float sixteenth = spb * 0.25f;
            float dotted8 = spb * 0.75f;
            var outList = new System.Collections.Generic.List<float[]>(40);

            // Streak level-ups: rising 4-note arpeggios, higher for each tier, glass on the last note.
            for (int t = 0; t < 4; t++)
            {
                int[] steps = { t, t + 2, t + 3, t + 5 };
                var y = new float[Samples(0.75f)];
                for (int a = 0; a < steps.Length; a++)
                {
                    float fr = f[Mathf.Min(StingSteps - 1, steps[a])];
                    MixAt(y, Voice(style, fr, 0.4f, rng), (int)(a * sixteenth * _sr), 0.8f);
                }
                MixAt(y, Glass(_sr, rng), (int)(3 * sixteenth * _sr), 0.18f);
                outList.Add(Normalize(Echo(y, _sr, dotted8, 0.3f, 0.1f), 0.95f));
            }

            // Chord stabs (backbeats): root-third-fifth lifted into 1-3 kHz, short and dry.
            for (int s = 0; s < 2; s++)
            {
                int[] chord = s == 0 ? new[] { 0, minor ? 1 : 2, 3 } : new[] { minor ? 1 : 2, 3, 5 };
                var y = new float[Samples(0.2f)];
                foreach (int c in chord)
                {
                    float fr = f[c];
                    while (fr < 1000f) fr *= 2f;
                    while (fr > 3000f) fr *= 0.5f;
                    Mix(y, Voice(style, fr, 0.2f, rng), 0.5f);
                }
                Tail(y, 0.06f, 0.05f);
                outList.Add(Normalize(y, 0.95f));
            }

            // Section clear (and flawless): a strummed triad plus glass, dotted-8th echo.
            for (int s = 0; s < 2; s++)
            {
                var y = new float[Samples(0.9f)];
                MixAt(y, Voice(style, f[0], 0.6f, rng), 0, 0.7f);
                MixAt(y, Voice(style, f[3], 0.6f, rng), (int)(0.012f * _sr), 0.65f);
                MixAt(y, Voice(style, f[5], 0.6f, rng), (int)(0.024f * _sr), 0.6f);
                Mix(y, Glass(_sr, rng), 0.15f);
                if (s == 1) Mix(y, Sine(_sr, 0.9f, f[0] * 4f, 0.3f), 0.3f);
                outList.Add(Normalize(Echo(y, _sr, dotted8, 0.3f, 0.12f), 0.95f));
            }

            // Finale: a 5-note run landing on a held chord that opens up (and sparkles on a full combo).
            for (int s = 0; s < 2; s++) outList.Add(Normalize(FinaleSting(f, minor, style, sixteenth, dotted8, s == 1, rng), 0.95f));

            // Rank stings: C/D, A/B, S, SS.
            for (int r = 0; r < 4; r++)
            {
                int[] steps = r == 0 ? new[] { 0, 2, 3 } : new[] { 0, 2, 3, 5 };
                var y = new float[Samples(1.4f)];
                for (int a = 0; a < steps.Length; a++) MixAt(y, Voice(style, f[steps[a]], 0.5f, rng), (int)(a * 0.07f * _sr), 0.75f);
                int land = (int)(steps.Length * 0.07f * _sr);
                if (r >= 1)
                {
                    MixAt(y, Voice(style, f[0], 0.7f, rng), land, 0.5f);
                    MixAt(y, Voice(style, f[3], 0.7f, rng), land, 0.45f);
                    MixAt(y, Voice(style, f[5], 0.7f, rng), land, 0.45f);
                }
                if (r >= 2)
                    for (int k = 0; k < 3; k++) MixAt(y, Sine(_sr, 0.6f, f[5 + k] * 2f, 0.25f), land + (int)(k * 0.05f * _sr), 0.25f);
                if (r >= 3)
                {
                    for (int k = 0; k < 4; k++) MixAt(y, Glass(_sr, rng), land + (int)(k * 0.06f * _sr), 0.12f);
                    MixAt(y, Boom(_sr, rng), land, 0.5f);
                }
                outList.Add(Normalize(Echo(y, _sr, dotted8, 0.25f, 0.08f), 0.95f));
            }

            // New best: two bright bell notes.
            {
                var y = new float[Samples(0.5f)];
                MixAt(y, Fm(_sr, f[8] * 2f, 0.35f, 2f, 1.2f, 0.06f, 0.15f), 0, 0.8f);
                MixAt(y, Fm(_sr, f[10] * 2f, 0.35f, 2f, 1.2f, 0.06f, 0.18f), (int)(0.11f * _sr), 0.8f);
                outList.Add(Normalize(y, 0.95f));
            }

            // Overdrive gold layer: a 60 ms saw stab an octave up with a bright snap.
            for (int i = 0; i < StingSteps; i++)
            {
                float fr = f[i] * 2f;
                var y = new float[Samples(0.09f)];
                double ph = 0;
                for (int k = 0; k < y.Length; k++)
                {
                    double t = k / (double)_sr;
                    ph += fr / _sr;
                    y[k] = (float)(Saw(ph) * System.Math.Exp(-t / 0.03) * Attack(t));
                }
                Mix(y, Snap(_sr, 5000f, 0.7f, 0.004f, rng), 0.6f);
                outList.Add(Normalize(y, 0.95f));
            }

            // Plain scale notes in the instrument (results count-up, stars).
            for (int i = 0; i < StingSteps; i++)
            {
                float[] y = Voice(style, f[i] * 2f, 0.3f, rng);
                Tail(y, 0.08f, 0.06f);
                outList.Add(Normalize(y, 0.95f));
            }
            return outList.ToArray();
        }

        private float[] FinaleSting(float[] f, bool minor, HitStyle style, float sixteenth, float dotted8, bool fullCombo, System.Random rng)
        {
            var y = new float[Samples(2.2f)];
            for (int a = 0; a < 5; a++) MixAt(y, Voice(style, f[a], 0.35f, rng), (int)(a * sixteenth * _sr), 0.6f);
            int land = (int)(5 * sixteenth * _sr);

            // Held chord: two detuned saws per tone on root, third, fifth, octave and the sub root.
            float third = minor ? 1.1892f : 1.2599f;
            float r0 = f[0] * 0.5f;
            float[] tones = { r0, r0 * third, r0 * 1.4983f, r0 * 2f, r0 * 0.5f };
            int n = y.Length - land;
            var pad = new float[n];
            var ph = new double[tones.Length * 2];
            double low = 0, band = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)_sr;
                double osc = 0;
                for (int v = 0; v < tones.Length; v++)
                {
                    ph[v * 2] += tones[v] * 1.0023 / _sr;
                    ph[v * 2 + 1] += tones[v] * 0.9977 / _sr;
                    osc += Saw(ph[v * 2]) + Saw(ph[v * 2 + 1]);
                }
                osc /= tones.Length * 2;
                double fc = 800 + 5200 * (1 - System.Math.Exp(-t / 0.15));
                double ff = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, _sr * 0.16) / _sr);
                low += ff * band;
                band += ff * (osc - low - 0.5 * band);
                pad[i] = (float)(low * System.Math.Min(1.0, t / 0.005) * System.Math.Exp(-t / 0.7));
            }
            MixAt(y, pad, land, 1.1f);
            MixAt(y, Voice(style, f[0] * 2f, 0.8f, rng), land, 0.5f);
            MixAt(y, Voice(style, f[3] * 2f, 0.8f, rng), land, 0.4f);
            MixAt(y, Thump(_sr, 0.3f), land, 0.5f);
            MixAt(y, Glass(_sr, rng), land, fullCombo ? 0.3f : 0.15f);
            if (fullCombo)
                for (int k = 0; k < 3; k++) MixAt(y, Sine(_sr, 0.5f, f[5 + k] * 2f, 0.2f), land + (int)((0.25f + k * 0.045f) * _sr), 0.3f);
            return Echo(y, _sr, dotted8, 0.25f, 0.08f);
        }

        private float[] StingFreqs(int root, bool minor)
        {
            int[] scale = minor ? new[] { 0, 3, 5, 7, 10 } : new[] { 0, 2, 4, 7, 9 };
            double rootHz = 440.0 * System.Math.Pow(2.0, (root - 9) / 12.0);
            while (rootHz < 329.6) rootHz *= 2.0;
            while (rootHz >= 622.0) rootHz *= 0.5;
            var freqs = new float[StingSteps];
            for (int i = 0; i < StingSteps; i++)
            {
                int semis = scale[i % scale.Length] + 12 * (i / scale.Length);
                freqs[i] = (float)(rootHz * System.Math.Pow(2.0, semis / 12.0));
            }
            return freqs;
        }

        /// <summary>Dries a tail: full level for <paramref name="hold"/> s, then an exponential fade.</summary>
        private void Tail(float[] y, float hold, float tau)
        {
            for (int i = 0; i < y.Length; i++)
            {
                float t = i / (float)_sr;
                if (t > hold) y[i] *= Mathf.Exp(-(t - hold) / tau);
            }
        }

        /// <summary>Short band-passed noise "tak" in the 2-6 kHz band the music leaves open.</summary>
        private static float[] Snap(int sr, float fc, float q, float tau, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.025f);
            var y = new float[n];
            double low = 0, band = 0;
            double f = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
            double damp = 1.0 / q;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double x = rng.NextDouble() * 2 - 1;
                low += f * band;
                double high = x - low - damp * band;
                band += f * high;
                y[i] = (float)(band * System.Math.Exp(-t / tau) * System.Math.Min(1.0, t / 0.0003));
            }
            return y;
        }
    }
}
