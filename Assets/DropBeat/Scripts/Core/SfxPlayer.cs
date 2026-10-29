using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Procedural synthwave sound effects, all generated at startup (no audio files).
    ///
    /// Hits are detuned-saw "plucks" through a resonant low-pass sweep, layered with a click, a sub
    /// thump and a short echo, tuned to the song's detected key: consecutive good hits climb a
    /// pentatonic scale. Also: hold riser + release sparkle, swipe whoosh + glass smash, overdrive
    /// power-up/down, claps and arpeggio for the combo layers, ticks and a glitchy miss.
    /// Scheduled sounds (assist ticks, metronome, combo layers) use the DSP clock, so they are
    /// sample-locked to the music; hit sounds play immediately on tap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayer : MonoBehaviour
    {
        private const int ScheduledVoices = 12;
        private const int LayerVoices = 10;
        private const int ScaleSteps = 8;
        private static readonly int[] ArpPattern = { 0, 2, 4, 5, 4, 2, 1, 3 };

        private AudioSource[] _voices;
        private double[] _voiceEnd;
        private int _nextVoice;
        private AudioSource[] _layer;
        private int _nextLayer;
        private AudioSource _oneShot, _panLeft, _panRight, _holdSource;
        private int _sr;

        private AudioClip[] _perfect;
        private AudioClip[] _great;
        private AudioClip[] _arp;
        private AudioClip _good;
        private AudioClip _fanfare;
        private AudioClip _riser;
        private AudioClip _holdRelease;
        private int _keyRoot = -1;
        private bool _keyMinor;
        private readonly Dictionary<int, AudioClip[]> _cache = new Dictionary<int, AudioClip[]>();

        public AudioClip Tick { get; private set; }
        public AudioClip TickAccent { get; private set; }
        public AudioClip Miss { get; private set; }
        public AudioClip UiClick { get; private set; }
        public AudioClip Whoosh { get; private set; }
        public AudioClip Smash { get; private set; }
        public AudioClip PowerUp { get; private set; }
        public AudioClip PowerDown { get; private set; }
        public AudioClip Clap { get; private set; }
        /// <summary>Big-moment hit: deep falling boom + crack + glass shimmer.</summary>
        public AudioClip Impact { get; private set; }
        public AudioClip Fanfare => _fanfare;
        public AudioClip HoldRelease => _holdRelease;

        public float Volume { get; set; } = 0.6f;

        private void Awake()
        {
            _sr = Mathf.Max(22050, AudioSettings.outputSampleRate);
            var rng = new System.Random(5);

            // Closed hi-hat tick and an open-ish accent: crisp, short, fits the synthwave kit.
            Tick = ToClip("tick", Normalize(HiHat(_sr, 0.045f, 0.012f, rng), 0.7f));
            float[] accent = HiHat(_sr, 0.08f, 0.03f, rng);
            Mix(accent, Sine(_sr, 0.08f, 1760f, 0.02f), 0.35f);
            TickAccent = ToClip("tickAccent", Normalize(accent, 0.8f));

            Miss = ToClip("miss", Normalize(Glitch(_sr, rng), 0.55f));
            UiClick = ToClip("ui", Normalize(Pluck(_sr, 1318.5f, 0.09f, 5000f, 900f, 0.02f, 0.03f, 0.004f, 0f), 0.35f));
            Whoosh = ToClip("whoosh", Normalize(SweepNoise(_sr, 0.42f, 5500f, 500f, rng), 0.75f));
            Smash = ToClip("smash", Normalize(Glass(_sr, rng), 0.7f));
            PowerUp = ToClip("powerUp", Normalize(PowerSweep(_sr, true, rng), 0.9f));
            PowerDown = ToClip("powerDown", Normalize(PowerSweep(_sr, false, rng), 0.7f));
            Clap = ToClip("clap", Normalize(ClapSound(_sr, rng), 0.8f));
            Impact = ToClip("impact", Normalize(Boom(_sr, rng), 0.95f));
            SetKey(9, true);
        }

        private void OnEnable()
        {
            if (_voices != null) return;
            _voices = new AudioSource[ScheduledVoices];
            _voiceEnd = new double[ScheduledVoices];
            for (int i = 0; i < ScheduledVoices; i++) _voices[i] = MakeSource("Voice" + i, 0f);
            _layer = new AudioSource[LayerVoices];
            for (int i = 0; i < LayerVoices; i++) _layer[i] = MakeSource("Layer" + i, 0f);
            _oneShot = MakeSource("OneShot", 0f);
            _panLeft = MakeSource("PanLeft", -0.65f);
            _panRight = MakeSource("PanRight", 0.65f);
            _holdSource = MakeSource("Hold", 0f);
        }

        private AudioSource MakeSource(string name, float pan)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.panStereo = pan;
            return src;
        }

        /// <summary>Retunes the musical sounds to a key (root 0 = C .. 11 = B). Cached per key.</summary>
        public void SetKey(int root, bool minor)
        {
            root = ((root % 12) + 12) % 12;
            if (root == _keyRoot && minor == _keyMinor && _perfect != null) return;
            _keyRoot = root;
            _keyMinor = minor;

            int cacheKey = root * 2 + (minor ? 1 : 0);
            if (!_cache.TryGetValue(cacheKey, out AudioClip[] clips))
            {
                clips = BuildKey(root, minor);
                _cache[cacheKey] = clips;
            }

            _perfect = new AudioClip[ScaleSteps];
            _great = new AudioClip[ScaleSteps];
            _arp = new AudioClip[ScaleSteps];
            System.Array.Copy(clips, 0, _perfect, 0, ScaleSteps);
            System.Array.Copy(clips, ScaleSteps, _great, 0, ScaleSteps);
            System.Array.Copy(clips, ScaleSteps * 2, _arp, 0, ScaleSteps);
            _good = clips[ScaleSteps * 3];
            _fanfare = clips[ScaleSteps * 3 + 1];
            _riser = clips[ScaleSteps * 3 + 2];
            _holdRelease = clips[ScaleSteps * 3 + 3];
        }

        private AudioClip[] BuildKey(int root, bool minor)
        {
            int[] scale = minor ? new[] { 0, 3, 5, 7, 10 } : new[] { 0, 2, 4, 7, 9 };
            // Root placed between E4 and D#5 so the plucks sit above most basslines but stay warm.
            double rootHz = 440.0 * System.Math.Pow(2.0, (root - 9) / 12.0);
            while (rootHz < 329.6) rootHz *= 2.0;
            while (rootHz >= 622.0) rootHz *= 0.5;

            var freqs = new float[ScaleSteps];
            for (int i = 0; i < ScaleSteps; i++)
            {
                int octave = i / scale.Length;
                int semis = scale[i % scale.Length] + 12 * octave;
                freqs[i] = (float)(rootHz * System.Math.Pow(2.0, semis / 12.0));
            }

            var rng = new System.Random(root * 31 + (minor ? 7 : 3));
            var clips = new AudioClip[ScaleSteps * 3 + 4];
            for (int i = 0; i < ScaleSteps; i++)
            {
                clips[i] = ToClip("perfect" + i, Normalize(PerfectHit(freqs[i], rng), 0.9f));
                clips[ScaleSteps + i] = ToClip("great" + i, Normalize(GreatHit(freqs[i], rng), 0.7f));
                float[] arp = Pluck(_sr, freqs[i] * 2f, 0.28f, 6000f, 900f, 0.03f, 0.07f, 0.005f, 0f);
                clips[ScaleSteps * 2 + i] = ToClip("arp" + i, Normalize(Echo(arp, _sr, 0.09f, 0.25f, 0.08f), 0.6f));
            }
            clips[ScaleSteps * 3] = ToClip("good", Normalize(Pluck(_sr, freqs[0] * 0.5f, 0.22f, 1400f, 300f, 0.04f, 0.07f, 0.004f, 0.2f), 0.5f));

            // Fanfare / hold release: quick rising arpeggio root-third-fifth-octave.
            clips[ScaleSteps * 3 + 1] = ToClip("fanfare", Normalize(Arpeggio(freqs, new[] { 0, 2, 3, 5 }, 0.065f, 0.75f), 0.85f));
            clips[ScaleSteps * 3 + 3] = ToClip("holdRelease", Normalize(Arpeggio(freqs, new[] { 3, 5, 7 }, 0.045f, 0.55f), 0.8f));

            // Hold riser: a sustained chord whose filter opens over 4 seconds.
            clips[ScaleSteps * 3 + 2] = ToClip("riser", Normalize(Riser((float)rootHz, minor), 0.7f));
            return clips;
        }

        private float[] Arpeggio(float[] freqs, int[] steps, float spacing, float seconds)
        {
            float[] y = new float[(int)(_sr * seconds)];
            for (int a = 0; a < steps.Length; a++)
            {
                float f = freqs[Mathf.Min(ScaleSteps - 1, steps[a])];
                float[] note = Pluck(_sr, f, 0.45f, 7000f, 600f, 0.07f, 0.16f, 0.007f, 0.15f);
                MixAt(y, note, (int)(a * spacing * _sr), 0.8f);
            }
            return Echo(y, _sr, 0.11f, 0.3f, 0.12f);
        }

        private float[] PerfectHit(float freq, System.Random rng)
        {
            float[] y = Pluck(_sr, freq, 0.65f, 8000f, 550f, 0.075f, 0.2f, 0.007f, 0.25f);
            Mix(y, Pluck(_sr, freq * 2f, 0.65f, 9000f, 1200f, 0.05f, 0.12f, 0.005f, 0f), 0.3f);
            Mix(y, Sine(_sr, 0.65f, freq * 4f, 0.16f), 0.07f);
            Mix(y, Sine(_sr, 0.65f, freq * 6f, 0.09f), 0.04f);
            Mix(y, Thump(_sr, 0.2f), 0.55f);
            Mix(y, Click(_sr, rng), 0.45f);
            return Echo(y, _sr, 0.12f, 0.3f, 0.13f);
        }

        private float[] GreatHit(float freq, System.Random rng)
        {
            float[] y = Pluck(_sr, freq, 0.45f, 4200f, 450f, 0.05f, 0.14f, 0.006f, 0.2f);
            Mix(y, Thump(_sr, 0.15f), 0.3f);
            Mix(y, Click(_sr, rng), 0.3f);
            return Echo(y, _sr, 0.12f, 0.2f, 0.07f);
        }

        private float[] Riser(float rootHz, bool minor)
        {
            int n = (int)(_sr * 4.2f);
            var y = new float[n];
            float third = minor ? 1.1892f : 1.2599f;
            float[] f = { rootHz * 0.5f, rootHz * 0.5f * third, rootHz * 0.75f, rootHz };
            double[] ph = new double[f.Length * 2];
            double low = 0, band = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)_sr;
                double osc = 0;
                for (int v = 0; v < f.Length; v++)
                {
                    ph[v * 2] += f[v] * 1.004 / _sr;
                    ph[v * 2 + 1] += f[v] * 0.996 / _sr;
                    osc += Saw(ph[v * 2]) + Saw(ph[v * 2 + 1]);
                }
                osc /= f.Length * 2;
                double fc = 300 + 5200 * System.Math.Pow(t / 4.2, 1.6);
                double ff = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, _sr * 0.16) / _sr);
                low += ff * band;
                double high = osc - low - 0.45 * band;
                band += ff * high;
                double env = System.Math.Min(1.0, t / 0.05) * (0.55 + 0.45 * t / 4.2);
                y[i] = (float)(low * env);
            }
            return y;
        }

        // ------------------------------------------------------------------ playback

        /// <summary>Hit feedback. <paramref name="combo"/> is the combo after this hit; pan -1..1 for dual rings.</summary>
        public void PlayHit(Grade grade, int combo, float pan = 0f)
        {
            int step = Mathf.Max(0, combo - 1) % ScaleSteps;
            switch (grade)
            {
                case Grade.Perfect: PlayNow(_perfect[step], 0.6f, pan); break;
                case Grade.Great: PlayNow(_great[step], 0.55f, pan); break;
                case Grade.Good: PlayNow(_good, 0.5f, pan); break;
                default: PlayNow(Miss, 0.5f, pan); break;
            }
        }

        /// <summary>Companion catch: a chord tone above the main hit, so balls joining build a harmony.</summary>
        public void PlayHarmony(int combo, int ball)
        {
            int step = (Mathf.Max(0, combo - 1) + 2 * ball) % ScaleSteps;
            PlayNow(_great[step], 0.35f, ball == 2 ? 0.5f : ball == 1 ? -0.5f : 0f);
        }

        public AudioClip ArpNote(int index)
        {
            return _arp[ArpPattern[((index % ArpPattern.Length) + ArpPattern.Length) % ArpPattern.Length]];
        }

        /// <summary>Starts the hold riser, stretched so its sweep peaks as the rail ends.</summary>
        public void StartHold(float seconds)
        {
            if (_holdSource == null || _riser == null) return;
            _holdSource.Stop();
            _holdSource.clip = _riser;
            _holdSource.pitch = Mathf.Clamp(3.6f / Mathf.Max(0.3f, seconds), 0.85f, 1.6f);
            _holdSource.volume = 0.45f * Volume;
            _holdSource.time = 0f;
            _holdSource.Play();
        }

        public void StopHold()
        {
            if (_holdSource != null) _holdSource.Stop();
        }

        /// <summary>Plays <paramref name="clip"/> exactly at DSP time <paramref name="dspTime"/>.</summary>
        public void PlayAt(AudioClip clip, double dspTime, float volume = 1f)
        {
            if (clip == null || _voices == null) return;
            int idx = -1;
            for (int i = 0; i < ScheduledVoices; i++)
            {
                int v = (_nextVoice + i) % ScheduledVoices;
                if (_voiceEnd[v] < AudioSettings.dspTime)
                {
                    idx = v;
                    break;
                }
            }
            if (idx < 0) idx = _nextVoice;
            _nextVoice = (idx + 1) % ScheduledVoices;

            AudioSource src = _voices[idx];
            src.Stop();
            src.clip = clip;
            src.volume = volume * Volume;
            src.PlayScheduled(dspTime);
            _voiceEnd[idx] = dspTime + clip.length;
        }

        /// <summary>Schedules a combo-layer sound (claps / arpeggio); all can be cut at once on a miss.</summary>
        public void PlayLayerAt(AudioClip clip, double dspTime, float volume)
        {
            if (clip == null || _layer == null) return;
            AudioSource src = _layer[_nextLayer];
            _nextLayer = (_nextLayer + 1) % LayerVoices;
            src.Stop();
            src.clip = clip;
            src.volume = volume * Volume;
            src.PlayScheduled(dspTime);
        }

        public void StopLayer()
        {
            if (_layer == null) return;
            for (int i = 0; i < LayerVoices; i++) _layer[i].Stop();
        }

        public void StopScheduled()
        {
            if (_voices == null) return;
            for (int i = 0; i < ScheduledVoices; i++)
            {
                _voices[i].Stop();
                _voiceEnd[i] = 0;
            }
            StopLayer();
        }

        public void PlayNow(AudioClip clip, float volume = 1f, float pan = 0f)
        {
            if (clip == null || _oneShot == null) return;
            AudioSource src = pan < -0.1f ? _panLeft : pan > 0.1f ? _panRight : _oneShot;
            src.PlayOneShot(clip, volume * Volume);
        }

        // ------------------------------------------------------------------ synthesis

        /// <summary>Three detuned saws (+ optional square sub-octave) through a Chamberlin state-variable low-pass with a decaying cutoff.</summary>
        private static float[] Pluck(int sr, float freq, float seconds, float cutoffStart, float cutoffEnd, float cutoffTau, float ampTau, float detune, float sub)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            double p1 = 0, p2 = 0.33, p3 = 0.66, p4 = 0;
            double low = 0, band = 0;
            const double damping = 0.55; // ~ resonance
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                p1 += freq * (1.0 + detune) / sr;
                p2 += freq * (1.0 - detune) / sr;
                p3 += freq / sr;
                p4 += freq * 0.5 / sr;
                double osc = (Saw(p1) + Saw(p2) + Saw(p3)) / 3.0 + sub * (Frac(p4) < 0.5 ? 1.0 : -1.0);
                double fc = cutoffEnd + (cutoffStart - cutoffEnd) * System.Math.Exp(-t / cutoffTau);
                double f = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
                low += f * band;
                double high = osc - low - damping * band;
                band += f * high;
                double env = (1.0 - System.Math.Exp(-t / 0.0015)) * System.Math.Exp(-t / ampTau);
                y[i] = (float)(low * env);
            }
            return y;
        }

        private static float[] Thump(int sr, float seconds)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                ph += 2 * System.Math.PI * (55 + 70 * System.Math.Exp(-t / 0.025)) / sr;
                y[i] = (float)(System.Math.Sin(ph) * System.Math.Exp(-t / 0.055));
            }
            return y;
        }

        private static float[] Click(int sr, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.02f);
            var y = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                y[i] = (noise - prev) * Mathf.Exp(-t / 0.0035f); // first difference = bright click
                prev = noise;
            }
            return y;
        }

        private static float[] HiHat(int sr, float seconds, float tau, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.35f * (noise - lp);
                y[i] = (noise - lp) * Mathf.Exp(-t / tau); // high-passed noise
            }
            return y;
        }

        private static float[] Sine(int sr, float seconds, float freq, float tau)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                y[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t / tau) * Mathf.Clamp01(t / 0.002f);
            }
            return y;
        }

        /// <summary>Band-passed noise whose centre sweeps from <paramref name="f0"/> to <paramref name="f1"/>: a whoosh.</summary>
        private static float[] SweepNoise(int sr, float seconds, float f0, float f1, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            double low = 0, band = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double u = t / seconds;
                double fc = f0 * System.Math.Pow(f1 / f0, u);
                double f = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
                double x = rng.NextDouble() * 2 - 1;
                low += f * band;
                double high = x - low - 0.35 * band;
                band += f * high;
                double env = System.Math.Sin(System.Math.PI * System.Math.Min(1.0, u * 1.6)) * (1.0 - u);
                y[i] = (float)(band * env);
            }
            return y;
        }

        /// <summary>Glass smash: a noise crack plus a scatter of bright, quickly decaying tinkles.</summary>
        private static float[] Boom(int sr, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.9f);
            var y = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                // Sub boom: pitch falls 150 -> 38 Hz, slow decay, soft-clipped for weight.
                float f = 38f + 112f * Mathf.Exp(-t * 14f);
                phase += 2.0 * System.Math.PI * f / sr;
                float sub = (float)System.Math.Tanh(1.8 * System.Math.Sin(phase)) * Mathf.Exp(-t * 4.2f) * Mathf.Min(1f, t * 400f);
                // Crack: bright noise burst on the attack.
                float crack = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 55f) * 0.55f;
                y[i] = sub + crack;
            }
            MixAt(y, Glass(sr, rng), sr / 50, 0.22f);
            return y;
        }

        private static float[] Glass(int sr, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.6f);
            var y = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                y[i] = (noise - prev) * Mathf.Exp(-t / 0.03f) * 0.8f;
                prev = noise;
            }
            for (int k = 0; k < 14; k++)
            {
                float start = (float)rng.NextDouble() * 0.18f;
                float f = 2500f + (float)rng.NextDouble() * 4500f;
                float[] s = Sine(sr, 0.35f, f, 0.04f + (float)rng.NextDouble() * 0.06f);
                MixAt(y, s, (int)(start * sr), 0.25f);
            }
            return y;
        }

        /// <summary>Overdrive: rising (or falling) detuned saw sweep, ending in a boom when rising.</summary>
        private static float[] PowerSweep(int sr, bool up, System.Random rng)
        {
            float seconds = up ? 1.1f : 0.7f;
            int n = Mathf.CeilToInt(sr * seconds);
            var y = new float[n];
            double p1 = 0, p2 = 0, low = 0, band = 0;
            float sweep = up ? 0.65f : 0.7f;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double u = System.Math.Min(1.0, t / sweep);
                double f = up ? 180 * System.Math.Pow(8.0, u) : 1100 * System.Math.Pow(1.0 / 8.0, u);
                p1 += f * 1.01 / sr;
                p2 += f * 0.99 / sr;
                double osc = (Saw(p1) + Saw(p2)) * 0.5;
                double fc = up ? 600 + 6000 * u : 6000 - 5500 * u;
                double ff = 2.0 * System.Math.Sin(System.Math.PI * System.Math.Min(fc, sr * 0.16) / sr);
                low += ff * band;
                double high = osc - low - 0.5 * band;
                band += ff * high;
                double env = up ? (t < sweep ? 0.4 + 0.6 * u : System.Math.Exp(-(t - sweep) / 0.05)) : (1.0 - u);
                y[i] = (float)(low * env * 0.8);
            }
            if (up)
            {
                int at = (int)(sweep * sr);
                float[] boom = Thump(sr, 0.45f);
                MixAt(y, boom, at, 1.1f);
                float[] crack = Glass(sr, rng);
                MixAt(y, crack, at, 0.35f);
            }
            return y;
        }

        private static float[] ClapSound(int sr, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.22f);
            var y = new float[n];
            double low = 0, band = 0;
            double f = 2.0 * System.Math.Sin(System.Math.PI * 1300.0 / sr);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double x = rng.NextDouble() * 2 - 1;
                low += f * band;
                double high = x - low - 0.6 * band;
                band += f * high;
                // Three quick slaps then a short tail.
                double env = 0;
                for (int k = 0; k < 3; k++)
                {
                    double tk = t - k * 0.009;
                    if (tk >= 0) env = System.Math.Max(env, System.Math.Exp(-tk / 0.005));
                }
                env = System.Math.Max(env, t > 0.02 ? 0.5 * System.Math.Exp(-(t - 0.02) / 0.06) : 0);
                y[i] = (float)(band * env);
            }
            return y;
        }

        /// <summary>Downward pitch-bent, bit-crushed saw: a short "glitch" for misses.</summary>
        private static float[] Glitch(int sr, System.Random rng)
        {
            int n = Mathf.CeilToInt(sr * 0.26f);
            var y = new float[n];
            double ph = 0, low = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)sr;
                double f = 70 + 230 * System.Math.Exp(-t / 0.05);
                ph += f / sr;
                double osc = Saw(ph) + (rng.NextDouble() * 2 - 1) * 0.25;
                low += 0.12 * (osc - low);
                double crushed = System.Math.Round(low * 6.0) / 6.0;
                y[i] = (float)(crushed * System.Math.Exp(-t / 0.08));
            }
            return y;
        }

        private static float[] Echo(float[] dry, int sr, float delay, float g1, float g2)
        {
            int d = Mathf.RoundToInt(delay * sr);
            var y = new float[dry.Length + 2 * d];
            for (int i = 0; i < dry.Length; i++)
            {
                y[i] += dry[i];
                y[i + d] += dry[i] * g1;
                y[i + 2 * d] += dry[i] * g2;
            }
            return y;
        }

        private static double Saw(double p) => 2.0 * Frac(p) - 1.0;
        private static double Frac(double p) => p - System.Math.Floor(p);

        private static void Mix(float[] into, float[] src, float gain) => MixAt(into, src, 0, gain);

        private static void MixAt(float[] into, float[] src, int offset, float gain)
        {
            for (int i = 0; i < src.Length && i + offset < into.Length; i++) into[i + offset] += src[i] * gain;
        }

        private static float[] Normalize(float[] y, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < y.Length; i++) m = Mathf.Max(m, Mathf.Abs(y[i]));
            float g = peak / m;
            int fade = Mathf.Min(y.Length, 480);
            for (int i = 0; i < y.Length; i++)
            {
                float edge = i >= y.Length - fade ? (y.Length - i) / (float)fade : 1f;
                y[i] *= g * edge;
            }
            return y;
        }

        private AudioClip ToClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, _sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
