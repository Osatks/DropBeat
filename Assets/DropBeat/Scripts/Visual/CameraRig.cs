using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Follow camera with perspective modes that blend smoothly: side view, angled views (left/right)
    /// and a tunnel view looking up the ring chain. Adds beat "kick" zoom, shake and overdrive sway.
    /// Pulls back in portrait so the narrow phone view still shows the path.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private float distance = 13f;
        [SerializeField] private float heightOffset = 2.2f;
        [SerializeField] private float pitch = 9f;
        [SerializeField] private float baseFov = 55f;

        private struct Pose
        {
            public float yaw, pitch, distance, lift, ahead;

            public static Pose Lerp(Pose a, Pose b, float t)
            {
                return new Pose
                {
                    yaw = Mathf.Lerp(a.yaw, b.yaw, t),
                    pitch = Mathf.Lerp(a.pitch, b.pitch, t),
                    distance = Mathf.Lerp(a.distance, b.distance, t),
                    lift = Mathf.Lerp(a.lift, b.lift, t),
                    ahead = Mathf.Lerp(a.ahead, b.ahead, t)
                };
            }
        }

        private Camera _cam;
        private Vector3 _focus;
        private Vector3 _focusVel;
        private float _kick;
        private float _shake;
        private float _shakeSeed;
        private Pose _pose;
        private Pose _target;
        private bool _poseInit;
        private float _blendRate = 100f;
        private float _overdrive;
        private float _crowd, _crowdTarget;

        public Camera Cam => _cam != null ? _cam : (_cam = GetComponent<Camera>());
        public CameraMode Mode { get; private set; }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _shakeSeed = Random.value * 100f;
            EnsurePose();
        }

        private void EnsurePose()
        {
            if (_poseInit) return;
            _poseInit = true;
            _pose = _target = PoseFor(CameraMode.Side);
        }

        /// <summary>Phones in portrait see a narrow slice, so the camera pulls back to keep the rings readable.</summary>
        public float DistanceScale => Cam.aspect < 1f ? 1.45f : 1f;

        /// <summary>Half the horizontal size of the side view at the ring plane (z = 0).</summary>
        public float HalfWidthAtRings
        {
            get
            {
                float halfH = distance * DistanceScale * Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
                return halfH * Cam.aspect;
            }
        }

        private Pose PoseFor(CameraMode mode)
        {
            switch (mode)
            {
                case CameraMode.AngledLeft: return new Pose { yaw = 22f, pitch = 14f, distance = distance * 0.97f, lift = heightOffset * 1.3f, ahead = 0.6f };
                case CameraMode.AngledRight: return new Pose { yaw = -22f, pitch = 14f, distance = distance * 0.97f, lift = heightOffset * 1.3f, ahead = 0.6f };
                case CameraMode.Tunnel: return new Pose { yaw = 0f, pitch = -55f, distance = distance * 0.9f, lift = 0f, ahead = 3.2f };
                default: return new Pose { yaw = 0f, pitch = pitch, distance = distance, lift = heightOffset, ahead = 0.66f };
            }
        }

        /// <summary>Blends to a perspective over roughly <paramref name="seconds"/> (0 = cut).</summary>
        public void SetMode(CameraMode mode, float seconds)
        {
            EnsurePose();
            Mode = mode;
            _target = PoseFor(mode);
            if (seconds <= 0f)
            {
                _pose = _target;
                _blendRate = 100f;
            }
            else
            {
                _blendRate = 3.5f / seconds;
            }
        }

        /// <summary>Rotation of the default side view (the sky is laid out for it).</summary>
        public Quaternion RestRotation => Quaternion.Euler(pitch, 0f, 0f);

        public void SetOverdrive(float level) => _overdrive = Mathf.Clamp01(level);

        /// <summary>0..1: extra balls on screen. The camera eases back and centres so every lane fits.</summary>
        public void SetCrowd(float level) => _crowdTarget = Mathf.Clamp01(level);

        /// <summary>How far the camera is centred for multi-ball sections (0 = follows the ball sideways).</summary>
        public float Crowd => _crowd;

        public void Snap(Vector3 focus)
        {
            _focus = focus;
            _focusVel = Vector3.zero;
            _crowd = _crowdTarget;
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
            EnsurePose();
            float dt = Time.unscaledDeltaTime;
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 6f);
            _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.5f);
            _crowd = Mathf.MoveTowards(_crowd, _crowdTarget, dt * 1.2f);
            _pose = Pose.Lerp(_pose, _target, 1f - Mathf.Exp(-_blendRate * dt));

            Vector3 shake = Vector3.zero;
            if (_shake > 0f)
            {
                float t = Time.unscaledTime * 38f;
                shake = new Vector3(Mathf.PerlinNoise(_shakeSeed, t) - 0.5f, Mathf.PerlinNoise(t, _shakeSeed) - 0.5f, 0f) * (_shake * 0.5f);
            }

            float scale = DistanceScale * (1f + 0.14f * Mathf.SmoothStep(0f, 1f, _crowd));
            float roll = _overdrive * 3.2f * Mathf.Sin(Time.unscaledTime * 2.1f);
            Quaternion rot = Quaternion.Euler(_pose.pitch, _pose.yaw, roll);
            Vector3 target = _focus + new Vector3(0f, _pose.ahead * scale, 0f);
            transform.position = target - rot * Vector3.forward * (_pose.distance * scale) + new Vector3(0f, _pose.lift * scale, 0f) + shake;
            transform.rotation = rot;
            Cam.fieldOfView = baseFov - _kick * 2.2f + _overdrive * 5f;
        }
    }
}
