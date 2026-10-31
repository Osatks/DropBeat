using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// The ball: a skin (DropBeat/Chrome _Skin: chrome, gold, plasma, lava, disco, pixel, planet...),
    /// glow, a trail effect (ribbon, rainbow, comet embers, sparkles, pixel blocks, electric arc, ghost
    /// afterimages), squash/stretch and a roll that follows its flight. Position is set by the GameController.
    /// Hierarchy: Ball (this) -> Squash (stretch along velocity) -> Body (spins in world space).
    /// Trail effect objects live in a separate root that follows the ball, so cloned extra balls
    /// (companions, twins) keep the plain look.
    /// </summary>
    public sealed class BallView : MonoBehaviour
    {
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int SeamId = Shader.PropertyToID("_SeamColor");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int SkinId = Shader.PropertyToID("_Skin");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Transform squash;
        [SerializeField] private MeshRenderer body;
        [SerializeField] private MeshRenderer glow;
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private float radius = 0.4f;
        [Tooltip("Fraction of true rolling speed used for the visual spin.")]
        [SerializeField] private float spinFactor = 0.45f;

        private MaterialPropertyBlock _mpb;
        private Color _accent = new Color(0f, 2.2f, 2.6f, 1f);
        private Color _accent2 = new Color(2.6f, 0.35f, 1.9f, 1f);
        private Color _flash;
        private float _flashTime = -10f;
        private float _squashTime = -10f;
        private float _squashAmount;
        private Vector3 _velocity;
        private Quaternion _spin = Quaternion.Euler(20f, 30f, 0f);
        private float _scaleMul = 1f;
        private float _trailWidth = 1f;

        private BallSkin _skin = BallSkin.Chrome;
        private TrailEffect _effect = TrailEffect.Classic;
        private VisualAssets _assets;
        private Transform _fxRoot;
        private ParticleSystem _particles;
        private LineRenderer _arc;
        private MeshRenderer[] _ghosts;
        private readonly List<(float time, Vector3 pos)> _history = new List<(float, Vector3)>(64);
        private float _arcJitterTime;
        private Vector3[] _arcPoints = new Vector3[18];

        public float Radius => radius;

        /// <summary>Material and mesh of the ball body (used to render the locker's skin previews).</summary>
        public Material BodyMaterial => body != null ? body.sharedMaterial : null;
        public Mesh BodyMesh => body != null ? body.GetComponent<MeshFilter>().sharedMesh : null;

        /// <summary>Visual size multiplier (companion and twin balls are a bit smaller).</summary>
        public void SetScale(float multiplier) => _scaleMul = Mathf.Max(0.1f, multiplier);

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (squash == null && body != null) squash = body.transform.parent;
        }

        private void OnDestroy()
        {
            if (_fxRoot != null) Destroy(_fxRoot.gameObject);
        }

        public void SetAccent(Color accent) => SetAccent(accent, _accent2);

        public void SetAccent(Color accent, Color accent2)
        {
            _accent = accent;
            _accent2 = accent2;
            ApplyTrailLook();
        }

        public void SetSkin(BallSkin skin) => _skin = skin;

        /// <summary>Switches the trail effect (needs the shared materials once to build particles/lines).</summary>
        public void SetTrailEffect(TrailEffect effect, VisualAssets assets)
        {
            _effect = effect;
            if (assets != null) _assets = assets;
            BuildEffect();
            ApplyTrailLook();
        }

        public void Teleport(Vector3 position)
        {
            transform.position = position;
            if (trail != null) trail.Clear();
            _history.Clear();
            // Move the effects root too, then restart the emitter: rate-over-distance would otherwise spray
            // particles all along the jump.
            if (_fxRoot != null) _fxRoot.position = position;
            if (_particles != null && _particles.gameObject.activeInHierarchy)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _particles.Play(true);
            }
        }

        // The effects root lives at the scene root (so clones do not copy it): follow the ball's active state.
        /// <summary>Soft across the width only: a round soft glow would fade a trail to nothing at the ball.</summary>
        private static void SoftLine(Renderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat("_Soft", 2f);
            r.SetPropertyBlock(mpb);
        }

        private void OnEnable()
        {
            if (_fxRoot != null) _fxRoot.gameObject.SetActive(true);
            _history.Clear();
        }

        private void OnDisable()
        {
            if (_fxRoot != null) _fxRoot.gameObject.SetActive(false);
        }

        private static Color Ldr(Color hdr)
        {
            float m = Mathf.Max(1f, Mathf.Max(hdr.r, Mathf.Max(hdr.g, hdr.b)));
            return new Color(hdr.r / m, hdr.g / m, hdr.b / m, hdr.a);
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
            if (_particles != null && (_effect == TrailEffect.Sparkle || _effect == TrailEffect.Comet || _effect == TrailEffect.Pixel))
            {
                _particles.Emit(_effect == TrailEffect.Pixel ? 10 : 14); // a burst on every hit
            }
        }

        public void SetTrailWidth(float multiplier) => _trailWidth = multiplier;

        // ------------------------------------------------------------------ trail looks

        private float BaseTrailWidth
        {
            get
            {
                switch (_effect)
                {
                    case TrailEffect.Comet: return 0.62f;
                    case TrailEffect.Sparkle: return 0.2f;
                    case TrailEffect.Electric: return 0.3f;
                    case TrailEffect.Ghost: return 0.18f;
                    default: return 0.42f;
                }
            }
        }

        private void ApplyTrailLook()
        {
            if (trail == null) return;
            SoftLine(trail);
            Color a = _accent.linear.gamma;
            var g = new Gradient();
            switch (_effect)
            {
                case TrailEffect.Rainbow:
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.2f, 0.2f), 0.15f),
                            new GradientColorKey(new Color(1f, 0.8f, 0.1f), 0.32f), new GradientColorKey(new Color(0.2f, 1f, 0.3f), 0.5f),
                            new GradientColorKey(new Color(0.2f, 0.6f, 1f), 0.68f), new GradientColorKey(new Color(0.7f, 0.2f, 1f), 0.85f)
                        },
                        new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
                    trail.time = 0.42f;
                    break;
                case TrailEffect.Comet:
                    g.SetKeys(
                        new[]
                        {
                            new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0.2f),
                            new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.55f), new GradientColorKey(new Color(0.6f, 0.05f, 0.02f), 1f)
                        },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.45f), new GradientAlphaKey(0f, 1f) });
                    trail.time = 0.45f;
                    break;
                case TrailEffect.Electric:
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(a, 1f) },
                        new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
                    trail.time = 0.25f;
                    break;
                case TrailEffect.Ghost:
                case TrailEffect.Sparkle:
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(a, 1f) },
                        new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
                    trail.time = 0.3f;
                    break;
                default:
                    g.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.Lerp(Color.white, a, 0.5f), 0.25f), new GradientColorKey(new Color(0.9f, 0.2f, 0.9f), 1f) },
                        new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
                    trail.time = 0.32f;
                    break;
            }
            trail.colorGradient = g;
            trail.enabled = _effect != TrailEffect.Pixel;
            if (_particles != null)
            {
                var main = _particles.main;
                switch (_effect)
                {
                    case TrailEffect.Comet:
                        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.2f, 1f), new Color(1f, 0.3f, 0.05f, 1f));
                        break;
                    case TrailEffect.Pixel:
                        main.startColor = new ParticleSystem.MinMaxGradient(Ldr(_accent * 0.9f), Ldr(_accent2 * 0.9f)); // particle colours are 8-bit: keep hue
                        break;
                    default:
                        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Ldr(Color.Lerp(Color.white, _accent, 0.6f)));
                        break;
                }
            }
        }

        private void BuildEffect()
        {
            if (_fxRoot == null)
            {
                _fxRoot = new GameObject("BallTrailFX").transform;
                _fxRoot.position = transform.position;
            }
            if (_particles != null) _particles.gameObject.SetActive(false);
            if (_arc != null) _arc.gameObject.SetActive(false);
            if (_ghosts != null) foreach (var gr in _ghosts) gr.gameObject.SetActive(false);
            if (_assets == null) return;

            switch (_effect)
            {
                case TrailEffect.Comet:
                    MakeParticles(false, 0.45f, 0.08f, 0.22f, 14f, new Vector2(0.2f, 0.8f));
                    break;
                case TrailEffect.Sparkle:
                    MakeParticles(false, 0.7f, 0.05f, 0.16f, 12f, new Vector2(0.1f, 0.5f));
                    break;
                case TrailEffect.Pixel:
                    MakeParticles(true, 0.5f, 0.1f, 0.2f, 9f, Vector2.zero);
                    break;
                case TrailEffect.Electric:
                    if (_arc == null)
                    {
                        var go = new GameObject("Arc");
                        go.transform.SetParent(_fxRoot, false);
                        _arc = go.AddComponent<LineRenderer>();
                        _arc.sharedMaterial = _assets.softAdditive;
                        SoftLine(_arc);
                        _arc.useWorldSpace = true;
                        _arc.positionCount = _arcPoints.Length;
                        _arc.numCapVertices = 2;
                        _arc.alignment = LineAlignment.View;
                        _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    _arc.gameObject.SetActive(true);
                    break;
                case TrailEffect.Ghost:
                    if (_ghosts == null)
                    {
                        _ghosts = new MeshRenderer[5];
                        for (int i = 0; i < _ghosts.Length; i++)
                        {
                            var go = new GameObject("Ghost" + i);
                            go.transform.SetParent(_fxRoot, false);
                            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Quad();
                            _ghosts[i] = go.AddComponent<MeshRenderer>();
                            _ghosts[i].sharedMaterial = _assets.softAdditive;
                            _ghosts[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                    }
                    foreach (var gr in _ghosts) gr.gameObject.SetActive(true);
                    break;
            }
        }

        private void MakeParticles(bool squares, float life, float sizeMin, float sizeMax, float perUnit, Vector2 speed)
        {
            if (_particles == null)
            {
                var go = new GameObject("Particles");
                go.transform.SetParent(_fxRoot, false);
                _particles = go.AddComponent<ParticleSystem>();
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            var ps = _particles;
            ps.gameObject.SetActive(true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startRotation = squares ? new ParticleSystem.MinMaxCurve(0f) : new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.maxParticles = 300;
            main.gravityModifier = _effect == TrailEffect.Comet ? -0.05f : 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = perUnit;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                squares
                    ? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, squares ? AnimationCurve.Linear(0f, 1f, 1f, 0.6f) : AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (squares)
            {
                // Hard-edged squares: the 8-bit look.
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = ProceduralMeshes.Quad();
                r.alignment = ParticleSystemRenderSpace.View;
                r.sharedMaterial = _assets.additive;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sharedMaterial = _assets.softAdditive;
            }
            ps.Play();
        }

        // ------------------------------------------------------------------ per frame

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
            // Landing: a damped spring - squash, then rebound into a stretch (up to ~26%).
            float sdt = Time.time - _squashTime;
            float sq = sdt < 0.5f ? _squashAmount * Mathf.Exp(-sdt / 0.06f) * Mathf.Cos(2f * Mathf.PI * sdt / 0.16f) : 0f;
            if (squash != null)
            {
                squash.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                float along = stretch * (1f - 0.3f * sq);
                float across = (1f / Mathf.Sqrt(stretch)) * (1f + 0.22f * sq);
                squash.localScale = new Vector3(across, along, across) * (radius * 2f * _scaleMul);
            }

            // Sharp flash: bright on contact, gone in ~70 ms.
            float flash = Mathf.Exp(-Mathf.Max(0f, Time.time - _flashTime) / 0.07f);
            if (body != null)
            {
                body.transform.rotation = _spin;
                _mpb.Clear();
                _mpb.SetColor(AccentId, _accent);
                _mpb.SetColor(SeamId, Color.Lerp(_accent * 0.75f, _flash * 1.2f, flash));
                _mpb.SetColor(FlashId, _flash * (1.4f * flash));
                _mpb.SetFloat(SkinId, (float)_skin);
                _mpb.SetColor(BaseColorId, SkinTint(_skin));
                body.SetPropertyBlock(_mpb);
            }

            if (glow != null)
            {
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, Color.Lerp(_accent * 0.16f, _flash * 0.5f, flash));
                glow.SetPropertyBlock(_mpb);
                glow.transform.localScale = Vector3.one * (radius * 5f * _scaleMul * (1f + 0.4f * flash));
            }

            if (trail != null) trail.widthMultiplier = BaseTrailWidth * _trailWidth;
            TickEffect();
        }

        /// <summary>Metal tint (_BaseColor) of a skin; the non-metal skins ignore it.</summary>
        public static Color SkinTint(BallSkin skin)
        {
            switch (skin)
            {
                case BallSkin.Gold: return new Color(1f, 0.77f, 0.34f, 1f);
                case BallSkin.BlackChrome: return new Color(0.06f, 0.06f, 0.07f, 1f);
                case BallSkin.RoseGold: return new Color(0.98f, 0.68f, 0.6f, 1f);
                default: return new Color(0.93f, 0.94f, 0.98f, 1f);
            }
        }

        private void TickEffect()
        {
            if (_fxRoot == null) return;
            Vector3 pos = transform.position;
            _fxRoot.position = pos;

            float now = Time.time;
            _history.Add((now, pos));
            while (_history.Count > 2 && now - _history[0].time > 0.4f) _history.RemoveAt(0);

            if (_effect == TrailEffect.Electric && _arc != null)
            {
                // A jittering arc through the last 0.18 s of positions; the jitter re-rolls 25x a second.
                bool reroll = now - _arcJitterTime > 0.04f;
                if (reroll) _arcJitterTime = now;
                int n = _arcPoints.Length;
                for (int i = 0; i < n; i++)
                {
                    float age = 0.18f * i / (n - 1);
                    Vector3 p = PositionAgo(age);
                    if (reroll || _arcPoints[i] == Vector3.zero)
                    {
                        float amp = i == 0 ? 0f : 0.14f * Mathf.Sin(Mathf.PI * i / (n - 1)) + 0.03f;
                        _arcPoints[i] = new Vector3(Random.Range(-amp, amp), Random.Range(-amp, amp), 0f);
                    }
                    _arc.SetPosition(i, p + _arcPoints[i]);
                }
                _arc.widthMultiplier = 0.07f * _trailWidth;
                Color c = Color.Lerp(Color.white, _accent2.linear.gamma, 0.35f);
                _arc.startColor = new Color(c.r, c.g, c.b, 1f);
                _arc.endColor = new Color(c.r, c.g, c.b, 0f);
            }
            else if (_effect == TrailEffect.Ghost && _ghosts != null)
            {
                Camera cam = Camera.main;
                for (int i = 0; i < _ghosts.Length; i++)
                {
                    float age = 0.045f * (i + 1);
                    Transform g = _ghosts[i].transform;
                    g.position = PositionAgo(age);
                    if (cam != null) g.rotation = cam.transform.rotation;
                    g.localScale = Vector3.one * (radius * 2.6f * _scaleMul * (1f - 0.08f * i));
                    _mpb.Clear();
                    _mpb.SetColor(VisualAssets.ColorId, _accent * (0.22f * (1f - i / (float)_ghosts.Length)));
                    _ghosts[i].SetPropertyBlock(_mpb);
                }
            }
        }

        private Vector3 PositionAgo(float age)
        {
            float target = Time.time - age;
            for (int i = _history.Count - 1; i > 0; i--)
            {
                if (_history[i - 1].time <= target)
                {
                    var a = _history[i - 1];
                    var b = _history[i];
                    float u = Mathf.InverseLerp(a.time, b.time, target);
                    return Vector3.Lerp(a.pos, b.pos, u);
                }
            }
            return _history.Count > 0 ? _history[0].pos : transform.position;
        }
    }
}
