using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Procedural sound effects. Assist ticks and the calibration metronome are scheduled on the DSP
    /// clock (sample-exact, same audio path as the music); hit sounds play immediately on tap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayer : MonoBehaviour
    {
        private const int ScheduledVoices = 12;

        private AudioSource[] _voices;
        private double[] _voiceEnd;
        private int _nextVoice;
        private AudioSource _oneShot;

        public AudioClip Tick { get; private set; }
        public AudioClip TickAccent { get; private set; }
        public AudioClip Hit { get; private set; }
        public AudioClip HitPerfect { get; private set; }
        public AudioClip Miss { get; private set; }
        public AudioClip UiClick { get; private set; }

        public float Volume { get; set; } = 0.6f;

        private void Awake()
        {
            int sr = Mathf.Max(22050, AudioSettings.outputSampleRate);
            Tick = Make("tick", sr, 0.04f, (t, i) => (Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 4400f * t) * 0.2f) * Mathf.Exp(-t * 120f));
            TickAccent = Make("tickAccent", sr, 0.05f, (t, i) => (Mathf.Sin(2f * Mathf.PI * 3000f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 1500f * t) * 0.3f) * Mathf.Exp(-t * 90f));
            var rng = new System.Random(5);
            Hit = Make("hit", sr, 0.12f, (t, i) =>
            {
                float f = 520f + 420f * Mathf.Exp(-t * 40f);
                float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 32f);
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 400f) * 0.35f;
                return (body * 0.55f + click);
            });
            HitPerfect = Make("hitPerfect", sr, 0.16f, (t, i) =>
            {
                float body = Mathf.Sin(2f * Mathf.PI * 1046.5f * t) * 0.45f + Mathf.Sin(2f * Mathf.PI * 1568f * t) * 0.25f;
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 500f) * 0.3f;
                return body * Mathf.Exp(-t * 26f) + click;
            });
            Miss = Make("miss", sr, 0.2f, (t, i) =>
            {
                float body = Mathf.Sin(2f * Mathf.PI * (140f - 60f * t) * t) * Mathf.Exp(-t * 18f);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 30f) * 0.25f;
                return (body * 0.6f + noise) * 0.8f;
            });
            UiClick = Make("ui", sr, 0.05f, (t, i) => Mathf.Sin(2f * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 110f) * 0.5f);

            _voices = new AudioSource[ScheduledVoices];
            _voiceEnd = new double[ScheduledVoices];
            for (int i = 0; i < ScheduledVoices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _voices[i] = src;
            }

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 0f;
        }

        /// <summary>Plays <paramref name="clip"/> exactly at DSP time <paramref name="dspTime"/>.</summary>
        public void PlayAt(AudioClip clip, double dspTime, float volume = 1f)
        {
            if (clip == null) return;
            // Prefer a voice that has finished; otherwise steal round-robin.
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

        public void StopScheduled()
        {
            for (int i = 0; i < ScheduledVoices; i++)
            {
                _voices[i].Stop();
                _voiceEnd[i] = 0;
            }
        }

        public void PlayNow(AudioClip clip, float volume = 1f)
        {
            if (clip != null) _oneShot.PlayOneShot(clip, volume * Volume);
        }

        private static AudioClip Make(string name, int sr, float seconds, System.Func<float, int, float> f)
        {
            int n = Mathf.CeilToInt(sr * seconds);
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float fadeOut = Mathf.Clamp01((n - i) / (sr * 0.004f));
                data[i] = Mathf.Clamp(f(t, i) * fadeOut, -1f, 1f);
            }

            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
