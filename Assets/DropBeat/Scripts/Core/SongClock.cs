using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// The single source of musical time.
    ///
    /// Music is started with AudioSource.PlayScheduled against the DSP clock, so song time 0 is
    /// sample-exact. AudioSettings.dspTime only advances once per audio buffer (~21 ms), so reading it
    /// directly makes everything stutter. Instead the clock tracks the smallest observed
    /// (realtime - dspTime) offset (the observation closest to a buffer boundary) and extrapolates
    /// with the high-resolution realtime clock. That gives a smooth, monotonic song time that stays
    /// locked to the audio hardware, and lets input timestamps be converted exactly.
    ///
    /// SongTime is "heard" time: raw DSP song time minus the user's calibrated output latency.
    /// Visuals and judging both use it, so what you see, hear and press line up.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SongClock : MonoBehaviour
    {
        [SerializeField] private AudioSource music;

        private double _startDsp;
        private double _minRealMinusDsp;
        private bool _hasOffset;
        private double _lastSampleReal;
        private double _pausedRaw;
        private int _pausedSample;

        public AudioSource Music => music;
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsFrozen { get; private set; }

        /// <summary>Calibrated audio output latency in seconds (+ = sound reaches you later).</summary>
        public double OutputLatency { get; set; }

        /// <summary>DSP time at which song time 0 is mixed.</summary>
        public double StartDsp => _startDsp;

        /// <summary>Smoothed estimate of AudioSettings.dspTime right now.</summary>
        public double DspNow => Time.realtimeSinceStartupAsDouble - _minRealMinusDsp;

        /// <summary>Song time of the sample being mixed right now.</summary>
        public double RawTime
        {
            get
            {
                if (IsPaused || IsFrozen) return _pausedRaw;
                if (!IsRunning) return double.NegativeInfinity;
                return DspNow - _startDsp;
            }
        }

        /// <summary>Song time the player is hearing right now.</summary>
        public double SongTime => RawTime - OutputLatency;

        public float ClipLength => music != null && music.clip != null ? music.clip.length : 0f;

        private void Awake()
        {
            if (music == null) music = GetComponent<AudioSource>();
            if (music == null) music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = false;
            Sample(true);
        }

        private void Update()
        {
            Sample(false);
        }

        private void Sample(bool reset)
        {
            double real = Time.realtimeSinceStartupAsDouble;
            double off = real - AudioSettings.dspTime;
            if (reset || !_hasOffset || off < _minRealMinusDsp || off - _minRealMinusDsp > 0.25)
            {
                _minRealMinusDsp = off;
                _hasOffset = true;
            }
            else
            {
                // Let the estimate creep later very slowly so it can follow audio-clock drift (<500 ppm).
                _minRealMinusDsp += (real - _lastSampleReal) * 0.0005;
            }
            _lastSampleReal = real;
        }

        /// <summary>Inverse of <see cref="SongTimeAtRealtime"/>: when (realtimeSinceStartup) song time t will be heard.</summary>
        public double RealtimeAtSongTime(double songTime) => songTime + OutputLatency + _startDsp + _minRealMinusDsp;

        /// <summary>DSP time corresponding to a realtimeSinceStartup timestamp.</summary>
        public double DspAtRealtime(double realtime) => realtime - _minRealMinusDsp;

        /// <summary>Heard song time for an event stamped on the realtimeSinceStartup timeline.</summary>
        public double SongTimeAtRealtime(double realtime)
        {
            if (IsPaused || IsFrozen || !IsRunning) return SongTime;
            return realtime - _minRealMinusDsp - _startDsp - OutputLatency;
        }

        /// <summary>Starts the clip so that song time <paramref name="fromTime"/> (can be negative = lead-in) begins after <paramref name="delay"/> seconds.</summary>
        public void Play(AudioClip clip, double fromTime, double delay = 0.1)
        {
            music.Stop();
            music.clip = clip;
            music.pitch = 1f;
            IsFrozen = false;
            IsPaused = false;
            Sample(true);
            Schedule(fromTime, delay);
        }

        private void Schedule(double fromTime, double delay)
        {
            double at = AudioSettings.dspTime + delay;
            _startDsp = at - fromTime;
            IsRunning = true;
            if (music.clip == null) return;

            if (fromTime >= 0.0)
            {
                int sample = Mathf.Clamp((int)(fromTime * music.clip.frequency), 0, Mathf.Max(0, music.clip.samples - 1));
                music.timeSamples = sample;
                _startDsp = at - sample / (double)music.clip.frequency;
                music.PlayScheduled(at);
            }
            else
            {
                music.timeSamples = 0;
                music.PlayScheduled(_startDsp);
            }
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            _pausedRaw = RawTime;
            _pausedSample = music.isPlaying ? music.timeSamples : -1;
            music.Pause();
            IsPaused = true;
        }

        /// <summary>Resumes from the exact paused sample after <paramref name="delay"/> seconds.</summary>
        public void Resume(double delay = 0.1)
        {
            if (!IsPaused) return;
            IsPaused = false;
            music.Stop();
            Sample(true);
            double from = _pausedSample >= 0 && music.clip != null
                ? _pausedSample / (double)music.clip.frequency
                : _pausedRaw;
            Schedule(from, delay);
        }

        /// <summary>Stops the clock where it is (used for the fail slow-down; audio keeps playing).</summary>
        public void Freeze()
        {
            if (!IsRunning || IsFrozen) return;
            _pausedRaw = RawTime;
            IsFrozen = true;
        }

        public void Stop()
        {
            music.Stop();
            music.pitch = 1f;
            IsRunning = false;
            IsPaused = false;
            IsFrozen = false;
        }
    }
}
