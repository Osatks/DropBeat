using UnityEngine;

namespace DropBeat
{
    /// <summary>Chrome-neon ball with glow, trail and squash/stretch. Position is set by the GameController.</summary>
    public sealed class BallView : MonoBehaviour
    {
        [SerializeField] private MeshRenderer body;
        [SerializeField] private MeshRenderer glow;
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private float radius = 0.3f;

        private MaterialPropertyBlock _mpb;
        private Color _accent = new Color(0f, 2.2f, 2.6f, 1f);
        private Color _flash;
        private float _flashTime = -10f;
        private float _squashTime = -10f;
        private float _squashAmount;
        private Vector3 _velocity;

        public float Radius => radius;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
        }

        public void Configure(MeshRenderer bodyRenderer, MeshRenderer glowRenderer, TrailRenderer trailRenderer)
        {
            body = bodyRenderer;
            glow = glowRenderer;
            trail = trailRenderer;
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
            // Stretch along velocity in flight, squash briefly on landing.
            float speed = _velocity.magnitude;
            float stretch = 1f + Mathf.Clamp(speed * 0.018f, 0f, 0.25f);
            float sq = Mathf.Clamp01(1f - (Time.time - _squashTime) / 0.12f) * _squashAmount;
            Vector3 dir = speed > 0.01f ? _velocity / speed : Vector3.up;
            if (body != null)
            {
                Transform bt = body.transform;
                bt.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                float along = stretch * (1f - 0.35f * sq);
                float across = (1f / Mathf.Sqrt(stretch)) * (1f + 0.25f * sq);
                bt.localScale = new Vector3(across, along, across) * (radius * 2f);
            }

            float flash = Mathf.Clamp01(1f - (Time.time - _flashTime) / 0.25f);
            Color core = Color.Lerp(new Color(1.1f, 1.1f, 1.25f, 1f), _flash, flash * 0.6f);
            Color rim = Color.Lerp(_accent, _flash, flash);
            if (body != null)
            {
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, core);
                _mpb.SetColor(VisualAssets.RimId, rim * 1.4f);
                body.SetPropertyBlock(_mpb);
            }

            if (glow != null)
            {
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, Color.Lerp(_accent * 0.22f, _flash * 0.4f, flash));
                glow.SetPropertyBlock(_mpb);
                glow.transform.localScale = Vector3.one * (radius * 5f * (1f + 0.4f * flash));
            }
        }
    }
}
