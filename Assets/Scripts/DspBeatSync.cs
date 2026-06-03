using System;
using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// DSP-time locked beat clock for rhythm gameplay.
    ///
    /// Key design goal:
    /// - Uses AudioSettings.dspTime as the single source of truth.
    /// - Never integrates movement with Time.deltaTime, so beat phase stays stable through frame drops.
    ///
    /// Exposes a repeating normalized [0..1) phase each beat and fires callbacks when a beat boundary is crossed.
    /// On the boundary callback we pass phase = 1.0f for pulse systems that want an exact "impact" value.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DspBeatSync : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioSource musicSource;

        [Tooltip("Song tempo in beats per minute.")]
        [Min(1f)]
        [SerializeField] private float bpm = 120f;

        [Tooltip("Optional lead-in before playback starts to allow deterministic scheduling.")]
        [Min(0f)]
        [SerializeField] private double startDelaySeconds = 0.1d;

        [Tooltip("Manual offset (seconds) for calibration (+ delays beat logic, - advances).")]
        [SerializeField] private double calibrationOffsetSeconds = 0d;

        [Header("Beat Shape")]
        [Tooltip("Multiply BPM phase speed (1 = quarter note beat, 2 = eighth-note pulses).")]
        [Min(0.01f)]
        [SerializeField] private float beatMultiplier = 1f;

        /// <summary>
        /// Repeating normalized beat phase [0..1).
        /// 0.0 is the start of a beat window, approaching 1.0 near the next beat.
        /// </summary>
        public float NormalizedBeatPhase { get; private set; }

        /// <summary>
        /// Total whole beats elapsed since song start (after multiplier).
        /// </summary>
        public long BeatIndex { get; private set; }

        /// <summary>
        /// True once audio has been scheduled and beat clock is active.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Absolute DSP timestamp when scheduled playback starts.
        /// </summary>
        public double SongStartDspTime => _songStartDspTime;

        /// <summary>
        /// Current song time in seconds from the scheduled start (includes calibration offset).
        /// </summary>
        public double CurrentSongTimeSeconds => AudioSettings.dspTime - _songStartDspTime - calibrationOffsetSeconds;

        /// <summary>
        /// Configured DSP pre-roll scheduling delay used by StartClock.
        /// </summary>
        public double StartDelaySeconds => startDelaySeconds;

        /// <summary>
        /// Invoked exactly when crossing a beat boundary.
        /// Parameters:
        /// - beatIndex: whole beat count
        /// - dspTimestamp: current AudioSettings.dspTime
        /// </summary>
        public event Action<long, double> OnBeat;

        /// <summary>
        /// Invoked alongside OnBeat for pulse systems that consume a normalized value.
        /// Always invokes with 1.0f at impact.
        /// </summary>
        public event Action<float> OnBeatPulse;

        private double _songStartDspTime;
        private double _secondsPerBeat;
        private long _lastDispatchedBeat = -1;

        private void Awake()
        {
            if (musicSource == null)
            {
                musicSource = GetComponent<AudioSource>();
            }

            RecomputeBeatDuration();
        }

        private void OnValidate()
        {
            bpm = Mathf.Max(1f, bpm);
            beatMultiplier = Mathf.Max(0.01f, beatMultiplier);
            RecomputeBeatDuration();
        }

        /// <summary>
        /// Schedules audio playback against the DSP clock and starts beat tracking.
        /// </summary>
        public void StartClock()
        {
            if (musicSource == null)
            {
                Debug.LogWarning("DspBeatSync: Missing AudioSource.", this);
                return;
            }

            RecomputeBeatDuration();

            _songStartDspTime = AudioSettings.dspTime + startDelaySeconds;
            _lastDispatchedBeat = -1;
            BeatIndex = 0;
            NormalizedBeatPhase = 0f;
            IsRunning = true;

            musicSource.Stop();
            if (musicSource.clip != null)
            {
                musicSource.PlayScheduled(_songStartDspTime);
            }
            else
            {
                Debug.LogWarning("DspBeatSync: No AudioClip assigned. Running silent DSP clock for gameplay testing.", this);
            }
        }

        /// <summary>
        /// Stops playback and beat tracking.
        /// </summary>
        public void StopClock()
        {
            IsRunning = false;
            musicSource?.Stop();
        }

        private void Update()
        {
            if (!IsRunning)
            {
                return;
            }

            double dspNow = AudioSettings.dspTime;
            double songTime = dspNow - _songStartDspTime - calibrationOffsetSeconds;

            // Track pre-roll without dispatching events.
            if (songTime < 0d)
            {
                NormalizedBeatPhase = 0f;
                BeatIndex = 0;
                return;
            }

            // Beat-space position in continuous units (0,1,2,3...).
            // This is pure DSP math, independent of rendering framerate.
            double beatPosition = songTime / _secondsPerBeat;
            long wholeBeat = (long)beatPosition;
            float phase = (float)(beatPosition - wholeBeat);

            BeatIndex = wholeBeat;
            NormalizedBeatPhase = phase;

            // If the frame skipped over one or more boundaries, dispatch all missed beats.
            // This preserves deterministic rhythm events even under stutter.
            if (wholeBeat > _lastDispatchedBeat)
            {
                for (long beat = _lastDispatchedBeat + 1; beat <= wholeBeat; beat++)
                {
                    OnBeat?.Invoke(beat, dspNow);
                    OnBeatPulse?.Invoke(1f);
                }

                _lastDispatchedBeat = wholeBeat;
            }
        }

        private void RecomputeBeatDuration()
        {
            // secondsPerBeat = 60 / (BPM * multiplier)
            _secondsPerBeat = 60d / (bpm * beatMultiplier);
        }
    }
}
