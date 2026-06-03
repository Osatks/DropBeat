using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Holds level song selection and applies the start track to the target AudioSource.
    /// Keeps a reference URL for the next track so sequencing info stays in-scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatDropSongPlan : MonoBehaviour
    {
        [SerializeField] private AudioSource targetAudioSource;
        [SerializeField] private AudioClip startTrack;
        [SerializeField] private string nextTrackYoutubeUrl = string.Empty;
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private bool preloadAudioDataOnAwake = true;

        public AudioClip StartTrack => startTrack;
        public string NextTrackYoutubeUrl => nextTrackYoutubeUrl;

        private void Awake()
        {
            if (!applyOnAwake)
            {
                return;
            }

            ApplyStartTrackNow();
            PreloadStartTrackNow();
        }

        public void ApplyStartTrackNow()
        {
            if (targetAudioSource == null || startTrack == null)
            {
                return;
            }

            targetAudioSource.clip = startTrack;
        }

        public void PreloadStartTrackNow()
        {
            if (!preloadAudioDataOnAwake || startTrack == null)
            {
                return;
            }

            if (startTrack.loadState == AudioDataLoadState.Unloaded)
            {
                startTrack.LoadAudioData();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (targetAudioSource == null)
            {
                targetAudioSource = GetComponent<AudioSource>();
            }
        }
#endif
    }
}
