using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// The chrome ball: polished metal (DropBeat/Chrome), engraved neon seams, glow, trail,
    /// squash/stretch and a roll that follows its flight. Position is set by the GameController.
    /// Hierarchy: Ball (this) -> Squash (stretch along velocity) -> Body (spins in world space).
    /// </summary>
    public sealed class BallView : MonoBehaviour
    {
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int SeamId = Shader.PropertyToID("_SeamColor");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");

        [SerializeField] private Transform squash;
        [SerializeField] private MeshRenderer body;
        [SerializeField] private MeshRenderer glow;
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private float radius = 0.4f;
        [Tooltip("Fraction of true rolling speed used for the visual spin.")]
        [SerializeField] private float spinFactor = 0.45f;

        private MaterialPropertyBlock _mpb;
        private Color _accent = new Color(0f, 2.2f, 2.6f, 1f);
        private Color _flash;
        private float _flashTime = -10f;
        private float _squashTime = -10f;
        private float _squashAmount;
        private Vector3 _velocity;
        private Quaternion _spin = Quaternion.Euler(20f, 30f, 0f);

        private float _scaleMul = 1f;

        public float Radius => radius;

        /// <summary>Visual size multiplier (companion and twin balls are a bit smaller).</summary>
        public void SetScale(float multiplier) => _scaleMul = Mathf.Max(0.1f, multiplier);

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (squash == null && body != null) squash = body.transform.parent;
        }

        public void SetAccent(Color accent)
        {
            _accent = accent;
            if (trail != null)
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.Lerp(Color.white, accent.linear.gamma, 0.5f), 0.25f), new GradientColorKey(new Color(0.9f, 0.2f, 0.9f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = g;
            }
        }

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            if (trail != null) trail.Clear();
        }

        public void SetPose(Vector3 position, Vector3 velocity)
        {
            transform.position = position;
            _velocity = velocity;
        }

        /// <summary>Landing squash + colour flash (called on every judged hit).</summary>
        public void Pulse(Color color, float strength)
        {
            _flash = color;
            _flashTime = Time.time;
            _squashTime = Time.time;
            _squashAmount = strength;
        }

        public void SetTrailWidth(float multiplier)
        {
            if (trail != null) trail.widthMultiplier = 0.42f * multiplier;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float speed = _velocity.magnitude;
            Vector3 dir = speed > 0.01f ? _velocity / speed : Vector3.up;

            // Roll as if along the backdrop wall: axis is perpendicular to motion and the view direction.
            Vector3 axis = Vector3.Cross(dir, Vector3.forward);
            float degPerSec = speed / Mathf.Max(0.05f, radius) * Mathf.Rad2Deg * spinFactor;
            if (axis.sqrMagnitude < 1e-4f || speed < 0.05f)
            {
                axis = new Vector3(0.3f, 1f, 0.2f);
                degPerSec = 35f;
            }
            _spin = Quaternion.AngleAxis(degPerSec * dt, axis.normalized) * _spin;

            // Stretch along velocity in flight, squash briefly on landing.
            float stretch = 1f + Mathf.Clamp(speed * 0.016f, 0f, 0.22f);
            float sq = Mathf.Clamp01(1f - (Time.time - _squashTime) / 0.12f) * _squashAmount;
            if (squash != null)
            {
                squash.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                float along = stretch * (1f - 0.3f * sq);
                float across = (1f / Mathf.Sqrt(stretch)) * (1f + 0.22f * sq);
                squash.localScale = new Vector3(across, along, across) * (radius * 2f * _scaleMul);
            }

            float flash = Mathf.Clamp01(1f - (Time.time - _flashTime) / 0.25f);
            if (body != null)
            {
                body.transform.rotation = _spin;
                _mpb.Clear();
                _mpb.SetColor(AccentId, _accent);
                _mpb.SetColor(SeamId, Color.Lerp(_accent * 0.75f, _flash * 1.2f, flash));
                _mpb.SetColor(FlashId, _flash * (0.55f * flash));
                body.SetPropertyBlock(_mpb);
            }

            if (glow != null)
            {
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, Color.Lerp(_accent * 0.16f, _flash * 0.4f, flash));
                glow.SetPropertyBlock(_mpb);
                glow.transform.localScale = Vector3.one * (radius * 5f * _scaleMul * (1f + 0.4f * flash));
            }
        }
    }
}
