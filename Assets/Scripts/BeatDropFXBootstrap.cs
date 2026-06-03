using UnityEngine;
using System.Collections;

namespace BeatDrop
{
    /// <summary>
    /// Boots music + DSP beat clock for background FX.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropFXBootstrap : MonoBehaviour
    {
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private BeatDropSongPlan songPlan;
        [SerializeField] private GameSynthezerAndController gameplayController;
        [SerializeField] private BeatDropEffectManager effectManager;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool ensureEffectManagerOnAwake = true;
        [SerializeField] private bool syncWithCountdown = true;
        [SerializeField] private float extraStartDelaySeconds = 0.08f;
        [SerializeField] private bool startAfterFirstRingClear = true;
        [SerializeField] private bool strictSyncAfterWarmup = true;

        private void Awake()
        {
            if (beatSync == null)
            {
                beatSync = GetComponent<DspBeatSync>();
            }

            if (songPlan == null)
            {
                songPlan = GetComponent<BeatDropSongPlan>();
            }

            if (songPlan != null)
            {
                songPlan.ApplyStartTrackNow();
                songPlan.PreloadStartTrackNow();
            }

            if (gameplayController == null)
            {
                gameplayController = Object.FindAnyObjectByType<GameSynthezerAndController>();
            }

            if (ensureEffectManagerOnAwake)
            {
                effectManager = effectManager != null ? effectManager : BeatDropEffectManager.FindOrCreate();
            }
        }

        private void Start()
        {
            if (!autoStart)
            {
                return;
            }

            StartCoroutine(StartMusicWhenReady());
        }

        private IEnumerator StartMusicWhenReady()
        {
            if (startAfterFirstRingClear && gameplayController != null)
            {
                while (gameplayController.CurrentRingIndex < 1)
                {
                    yield return null;
                }
            }

            float delay = Mathf.Max(0f, extraStartDelaySeconds);
            if (strictSyncAfterWarmup && startAfterFirstRingClear)
            {
                // Avoid extra offsets after warmup so ALD note times stay phase-locked.
                delay = 0f;
            }
            if (!startAfterFirstRingClear && syncWithCountdown && gameplayController != null)
            {
                delay += gameplayController.CountdownSecondsForExternalSync;
            }

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (songPlan != null)
            {
                songPlan.ApplyStartTrackNow();
            }

            if (beatSync != null && !beatSync.IsRunning)
            {
                beatSync.StartClock();
            }
        }
    }
}
