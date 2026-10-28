using UnityEngine;

namespace DropBeat
{
    /// <summary>Smooth follow camera with beat "kick" zoom and shake.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private float distance = 13f;
        [SerializeField] private float heightOffset = 2.2f;
        [SerializeField] private float pitch = 9f;
        [SerializeField] private float baseFov = 55f;

        private Camera _cam;
        private Vector3 _focus;
        private Vector3 _focusVel;
        private float _kick;
        private float _shake;
        private float _shakeSeed;

        public Camera Cam => _cam != null ? _cam : (_cam = GetComponent<Camera>());

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _shakeSeed = Random.value * 100f;
        }

        /// <summary>Half the horizontal size of the view at the ring plane (z = 0).</summary>
        public float HalfWidthAtRings
        {
            get
            {
                float halfH = distance * Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
                return halfH * Cam.aspect;
            }
        }

        public void Snap(Vector3 focus)
        {
            _focus = focus;
            _focusVel = Vector3.zero;
            Apply();
        }

        public void Follow(Vector3 focus, float smoothTime)
        {
            _focus = Vector3.SmoothDamp(_focus, focus, ref _focusVel, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            Apply();
        }

        public void Kick(float amount) => _kick = Mathf.Max(_kick, amount);
        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        private void Apply()
        {
            float dt = Time.unscaledDeltaTime;
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 6f);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.5f);

            Vector3 shake = Vector3.zero;
            if (_shake > 0f)
            {
                float t = Time.unscaledTime * 38f;
                shake = new Vector3(Mathf.PerlinNoise(_shakeSeed, t) - 0.5f, Mathf.PerlinNoise(t, _shakeSeed) - 0.5f, 0f) * (_shake * 0.5f);
            }

            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 target = _focus + new Vector3(0f, heightOffset * 0.3f, 0f);
            transform.position = target - rot * Vector3.forward * distance + new Vector3(0f, heightOffset, 0f) + shake;
            transform.rotation = rot;
            Cam.fieldOfView = baseFov - _kick * 2.2f;
        }
    }
}
