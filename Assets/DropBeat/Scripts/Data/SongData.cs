using System;
using UnityEngine;

namespace DropBeat
{
    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2
    }

    /// <summary>
    /// One playable song: the audio clip plus the beat grid produced by <see cref="BeatAnalyzer"/>.
    /// Charts are generated from the grid at load time, so every note sits exactly on a beat or half-beat.
    /// Re-run analysis from the inspector (or DropBeat > Analyze All Songs) after changing the overrides.
    /// </summary>
    [CreateAssetMenu(menuName = "DropBeat/Song", fileName = "Song")]
    public sealed class SongData : ScriptableObject
    {
        [Header("Song")]
        public AudioClip clip;
        public string title = "Untitled";
        public string artist = "Unknown";
        [ColorUsage(false, true)] public Color accent = new Color(0f, 2.2f, 2.6f, 1f);
        [Tooltip("Where the menu preview starts playing, in seconds.")]
        public float previewTime = 30f;

        [Header("Analysis overrides (re-analyse after changing)")]
        [Tooltip("Force this BPM instead of detecting it. 0 = auto.")]
        public float bpmOverride;
        [Tooltip("Use the other half of the beat. Only needed if the ticks land between the kicks.")]
        public bool flipHalfBeat;

        [Header("Fine tuning (applies instantly)")]
        [Tooltip("Shifts every note of this song. + = later.")]
        [Range(-100f, 100f)] public float songOffsetMs;

        [Header("Analysis result (generated)")]
        public float bpm;
        public float duration;
        public int downbeatPhase;
        public float[] beatTimes = Array.Empty<float>();
        public float[] beatStrength = Array.Empty<float>();
        public float[] kickStrength = Array.Empty<float>();
        public float[] snareStrength = Array.Empty<float>();
        public float[] offStrength = Array.Empty<float>();
        public float[] loudness = Array.Empty<float>();
        public float phaseConfidence;
        public float driftMs;
        public bool driftCorrected;
        public float detectorBiasMs;
        [Tooltip("Detected key root, 0 = C .. 11 = B. Hit sounds are tuned to it.")]
        public int keyRoot = 9;
        public bool keyMinor = true;
        [TextArea(3, 10)] public string analysisLog;
        public string analyzedAt;
        public int analyzedSampleRate;

        public bool IsAnalyzed => beatTimes != null && beatTimes.Length > 8 && bpm > 0f;
        public float SecondsPerBeat => bpm > 0f ? 60f / bpm : 0.5f;
        public float OffsetSeconds => songOffsetMs * 0.001f;

        public void Apply(BeatAnalysis a, int sampleRate)
        {
            bpm = a.bpm;
            duration = a.duration;
            downbeatPhase = a.downbeatPhase;
            beatTimes = a.beatTimes;
            beatStrength = a.beatStrength;
            kickStrength = a.kickStrength;
            snareStrength = a.snareStrength;
            offStrength = a.offStrength;
            loudness = a.loudness;
            phaseConfidence = a.phaseConfidence;
            driftMs = a.driftMs;
            driftCorrected = a.driftCorrected;
            detectorBiasMs = a.detectorBiasMs;
            keyRoot = a.keyRoot;
            keyMinor = a.keyMinor;
            analysisLog = a.log;
            analyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            analyzedSampleRate = sampleRate;
        }

        /// <summary>Beat time in song seconds including the per-song offset. Extrapolates outside the grid.</summary>
        public double BeatTime(int k)
        {
            int n = beatTimes.Length;
            if (n == 0) return k * 0.5;
            double spb = SecondsPerBeat;
            if (k < 0) return beatTimes[0] + k * spb + OffsetSeconds;
            if (k >= n) return beatTimes[n - 1] + (k - n + 1) * spb + OffsetSeconds;
            return beatTimes[k] + OffsetSeconds;
        }

        /// <summary>Index of the last beat at or before time t (song seconds incl. offset). May be negative.</summary>
        public int BeatIndexAt(double t)
        {
            int n = beatTimes.Length;
            double spb = SecondsPerBeat;
            if (n == 0) return (int)Math.Floor(t / spb);
            double first = beatTimes[0] + OffsetSeconds;
            if (t < first) return (int)Math.Floor((t - first) / spb);
            double last = beatTimes[n - 1] + OffsetSeconds;
            if (t >= last) return n - 1 + (int)Math.Floor((t - last) / spb);

            int lo = 0, hi = n - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (beatTimes[mid] + OffsetSeconds <= t) lo = mid;
                else hi = mid - 1;
            }
            return lo;
        }

        public bool IsDownbeat(int k)
        {
            return ((k - downbeatPhase) % 4 + 4) % 4 == 0;
        }
    }
}
