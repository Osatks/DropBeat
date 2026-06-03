using UnityEngine;

namespace BeatDrop
{
    /// <summary>
    /// Runtime note object driven entirely by DSP-time math.
    /// </summary>
    public sealed class RhythmNote : MonoBehaviour
    {
        public LaneId Lane { get; private set; }
        public double HitTime { get; private set; }
        public bool IsActive { get; private set; }

        private Transform _cachedTransform;

        private void Awake()
        {
            _cachedTransform = transform;
            IsActive = false;
        }

        public void Activate(LaneId lane, double hitTime, Vector3 spawnPos)
        {
            Lane = lane;
            HitTime = hitTime;
            IsActive = true;
            _cachedTransform.position = spawnPos;
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        public void SetWorldPosition(in Vector3 worldPos)
        {
            _cachedTransform.position = worldPos;
        }
    }
}
