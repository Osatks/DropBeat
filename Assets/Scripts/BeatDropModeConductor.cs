using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BeatDrop
{
    /// <summary>
    /// Drives perspective mode changes from beat counts to match the spec's dynamic layout remix.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropModeConductor : MonoBehaviour
    {
        [SerializeField] private DspBeatSync beatSync;
        [SerializeField] private ModeTransitionManager modeManager;
        [SerializeField] private int beatsPerMode = 16;
        [SerializeField] private bool enableManualCycleKey = true;

        private long _lastBeatIndex = -1;

        private void OnEnable()
        {
            if (beatSync != null)
            {
                beatSync.OnBeat += OnBeat;
            }
        }

        private void OnDisable()
        {
            if (beatSync != null)
            {
                beatSync.OnBeat -= OnBeat;
            }
        }

        private void Update()
        {
            if (!enableManualCycleKey || modeManager == null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
            {
                modeManager.CycleNextMode();
            }
#else
            if (Input.GetKeyDown(KeyCode.M))
            {
                modeManager.CycleNextMode();
            }
#endif
        }

        private void OnBeat(long beatIndex, double _)
        {
            if (modeManager == null || beatsPerMode <= 0)
            {
                return;
            }

            if (beatIndex == _lastBeatIndex)
            {
                return;
            }
            _lastBeatIndex = beatIndex;

            if (beatIndex > 0 && beatIndex % beatsPerMode == 0)
            {
                modeManager.CycleNextMode();
            }
        }
    }
}
