using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// One ring: a solid 3D band (DropBeat/Ring3D) with a DropBeat/Ring quad behind it for the glow. While approaching, its segments light up clockwise and
    /// complete exactly on the beat, and a thin approach circle closes onto it at the same moment.
    /// Types: tap (segmented), hold (dashed ring + rail + end cap), swipe (chevron arrow), dual (two rings
    /// joined by a beam), companion (the companion ball's colour). Moving rings sway in rhythm.
    /// A hit shatters it outward; a miss drops it.
    /// </summary>
    public sealed class RingView : MonoBehaviour
    {
        /// <summary>Tilt toward the camera: reads as a ring the ball drops into, without getting too thin.</summary>
        public static readonly Quaternion Tilt = Quaternion.Euler(-40f, 0f, 0f);
        /// <summary>World size of the quad; the ring radius is 0.6 of its half-size (about 0.8 units).</summary>
        public const float QuadSize = 2.7f;

        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        public static readonly int FillId = Shader.PropertyToID("_Fill");
        public static readonly int BurstId = Shader.PropertyToID("_Burst");
        public static readonly int ZoneId = Shader.PropertyToID("_Zone");
        public static readonly int SegmentsId = Shader.PropertyToID("_Segments");
        public static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        public static readonly int SpinId = Shader.PropertyToID("_Spin");
        public static readonly int ArrowId = Shader.PropertyToID("_Arrow");
        public static readonly int BandScaleId = Shader.PropertyToID("_BandScale");
        /// <summary>Depth of the 3D band in quad units (the quad is 1 wide): about 0.25 world units.</summary>
        private const float BandHalfDepth = 0.045f;

        private MeshRenderer _body, _band, _approach, _cap, _rail, _railFill, _beam;
        private MeshFilter _bandFilter;
        private Transform _bodyT, _bandT, _approachT, _capT, _railT, _railFillT, _beamT;
        private MaterialPropertyBlock _mpb;
        private VisualAssets _assets;

        private PlayNote _note;
        private Color _base;
        private float _segments;
        private float _thickness;
        private float _spinDir;
        private Vector4 _arrow;
        private enum State { Idle, Live, Holding, Hit, Missed }
        private State _state;
        private double _stateTime;
        private Grade _grade;

        public PlayNote Note => _note;
        public bool Free => _state == State.Idle;

        public static RingView Create(Transform parent, VisualAssets assets)
        {
            var root = new GameObject("Ring");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<RingView>();
            view._assets = assets;
            view._mpb = new MaterialPropertyBlock();

            view._bodyT = MakeQuad(root.transform, "Body", assets.ring, Tilt, out view._body);
            view._bandT = MakeQuad(root.transform, "Band", assets.ring3d != null ? assets.ring3d : assets.ring, Tilt, out view._band);
            view._bandFilter = view._bandT.GetComponent<MeshFilter>();
            view._band.enabled = assets.ring3d != null;
            view._approachT = MakeQuad(root.transform, "Approach", assets.ring, Tilt, out view._approach);
            view._capT = MakeQuad(root.transform, "Cap", assets.ring, Tilt, out view._cap);
            view._railT = MakeQuad(root.transform, "Rail", assets.additive, Quaternion.identity, out view._rail);
            view._railFillT = MakeQuad(root.transform, "RailFill", assets.additive, Quaternion.identity, out view._railFill);
            view._beamT = MakeQuad(root.transform, "Beam", assets.additive, Quaternion.identity, out view._beam);
            root.SetActive(false);
            return view;
        }

        private static Transform MakeQuad(Transform parent, string name, Material mat, Quaternion rotation, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one * QuadSize;
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Quad();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        public void Bind(PlayNote note, Color accent)
        {
            _note = note;
            note.view = this;
            _base = note.kind == NoteKind.Offbeat ? _assets.offbeat : accent;
            _segments = note.kind == NoteKind.Downbeat ? 12f : note.kind == NoteKind.Offbeat ? 6f : 8f;
            _thickness = note.kind == NoteKind.Downbeat ? 0.085f : note.kind == NoteKind.Offbeat ? 0.06f : 0.072f;
            _arrow = Vector4.zero;
            if (note.impact)
            {
                // Big moment: white-hot gold ring with more, finer segments.
                _base = Color.Lerp(accent, _assets.perfect, 0.55f) * 1.25f;
                _segments = 16f;
                _thickness = 0.095f;
            }
            switch (note.type)
            {
                case NoteType.Hold:
                    _base = _assets.perfect * 0.75f;
                    _segments = 20f;
                    break;
                case NoteType.Swipe:
                    _base = _assets.offbeat;
                    _segments = 4f;
                    _thickness = 0.08f;
                    float angle = note.swipe == SwipeDir.Left ? -Mathf.PI * 0.5f : note.swipe == SwipeDir.Right ? Mathf.PI * 0.5f : 0f;
                    _arrow = new Vector4(1f, angle, 0f, 0f);
                    break;
                case NoteType.Dual:
                    _base = new Color(1.6f, 0.6f, 2.6f, 1f);
                    break;
            }
            _spinDir = (note.index & 1) == 0 ? 1f : -1f;
            _state = State.Live;
            transform.localPosition = note.position;
            transform.localScale = Vector3.one * note.scale;
            _bodyT.localPosition = Vector3.zero;
            _bodyT.localScale = Vector3.one * QuadSize;
            // The ring (3D band + glow) faces the ball's launch; the approach circle always faces the camera
            // so the timing cue stays readable, and swipe arrows face the camera too.
            _bandT.localRotation = note.facing;
            _bodyT.localRotation = note.type == NoteType.Swipe ? Quaternion.identity : note.facing;
            _approachT.localRotation = Quaternion.identity;
            _bandT.localPosition = Vector3.zero;
            // Radius 0.3 and half width = thickness / 2 match the quad's SDF ring (radius 0.6, in -1..1 space).
            _bandFilter.sharedMesh = ProceduralMeshes.RingBand(0.3f, _thickness * 0.5f, BandHalfDepth);

            bool hold = note.type == NoteType.Hold;
            _rail.enabled = hold;
            _railFill.enabled = false;
            _cap.enabled = hold;
            bool beam = note.type == NoteType.Dual && note.side == TapSide.Left && note.partner != null;
            _beam.enabled = beam;
            if (hold) LayoutRail();
            gameObject.SetActive(true);
        }

        public void Release()
        {
            if (_note != null && _note.view == this) _note.view = null;
            _note = null;
            _state = State.Idle;
            gameObject.SetActive(false);
        }

        public void OnJudged(Grade grade, double songTime)
        {
            _grade = grade;
            _stateTime = songTime;
            if (grade == Grade.Miss) _state = State.Missed;
            else _state = _note != null && _note.type == NoteType.Hold && !_note.tailJudged ? State.Holding : State.Hit;
        }

        /// <param name="isTarget">True while the ring is inside its approach window.</param>
        public void Tick(double songTime, double approachTime, bool isTarget, float fever, Vector3 mainBall)
        {
            if (_note == null) return;
            if (_state == State.Live) transform.localPosition = _note.PositionAt(songTime);
            switch (_state)
            {
                case State.Live: TickLive(songTime, approachTime, isTarget, fever); break;
                case State.Holding: TickHolding(songTime, mainBall); break;
                case State.Hit: TickHit(songTime); break;
                case State.Missed: TickMiss(songTime); break;
            }
        }

        private void TickLive(double t, double approachTime, bool isTarget, float fever)
        {
            float u = (float)((_note.time - t) / approachTime); // 1 -> 0 over the approach, < 0 after the beat
            float closeness = 1f - Mathf.Clamp01(u);
            float spin = _note.type == NoteType.Swipe ? 0f : (float)t * 0.04f * _spinDir;

            Color body = _base;
            // Rings further ahead fade into the distance: a depth cue that also keeps the next ring obvious.
            body.a = isTarget ? 1f + 0.25f * fever : 0.45f * Mathf.Clamp01(1.6f - u * 0.35f);
            Color fillColor = isTarget ? Color.Lerp(_base * 1.3f, _assets.perfect, closeness * closeness) : _base;
            SetRing(_body, body, fillColor, isTarget ? closeness : 0f, 0f, isTarget ? 0.6f + 0.4f * closeness : 0f, _segments, _thickness, spin, _arrow);

            if (isTarget && u < 1f && u > -0.25f)
            {
                _approach.enabled = true;
                float s = 1f + 1.6f * Mathf.Max(0f, u);
                _approachT.localScale = Vector3.one * (QuadSize * s);
                float a = Mathf.Clamp01(closeness * 1.5f) * (u < 0f ? 1f + u * 4f : 1f) * 0.8f;
                Color ac = Color.Lerp(_base, Color.white * 1.6f, closeness);
                ac.a = a;
                SetRing(_approach, ac, ac, 1f, 0f, 0f, 0f, 0.012f / s, 0f, Vector4.zero);
            }
            else
            {
                _approach.enabled = false;
            }

            if (_note.type == NoteType.Hold) TickRail(0f, isTarget ? 0.5f + 0.5f * closeness : 0.35f);
            if (_beam.enabled) TickBeam(t, isTarget ? 0.4f + 0.6f * closeness : 0.25f);
        }

        private void TickHolding(double t, Vector3 ball)
        {
            if (_note.tailJudged)
            {
                _stateTime = t;
                _grade = _note.tailGrade;
                _state = _note.tailGrade == Grade.Miss ? State.Missed : State.Hit;
                _rail.enabled = _railFill.enabled = _cap.enabled = false;
                if (_state == State.Hit) transform.localPosition = _note.railEnd;
                return;
            }

            float p = Mathf.Clamp01((float)((t - _note.time) / System.Math.Max(0.05, _note.endTime - _note.time)));
            Color gold = _assets.perfect;
            Color body = gold;
            body.a = 1.2f;
            SetRing(_body, body, gold * 1.2f, 1f, 0f, 0.8f, _segments, _thickness, (float)t * 0.3f, Vector4.zero);
            _approach.enabled = false;
            TickRail(p, 1f);
        }

        private void LayoutRail()
        {
            Vector3 a = _note.position;
            Vector3 b = _note.railEnd;
            Vector3 d = b - a;
            float len = d.magnitude;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float s = 1f / Mathf.Max(0.01f, transform.localScale.x);
            _railT.localPosition = d * 0.5f * s;
            _railT.localRotation = Quaternion.Euler(0f, 0f, angle);
            _railT.localScale = new Vector3(len * s, 0.13f * s, 1f);
            _capT.localPosition = d * s;
            _capT.localScale = Vector3.one * (QuadSize * 0.55f);
        }

        private void TickRail(float progress, float intensity)
        {
            Vector3 a = _note.position;
            Vector3 d = _note.railEnd - a;
            float s = 1f / Mathf.Max(0.01f, transform.localScale.x);
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, _assets.perfect * (0.25f * intensity));
            _rail.SetPropertyBlock(_mpb);

            _railFill.enabled = progress > 0f;
            if (progress > 0f)
            {
                _railFillT.localPosition = d * (0.5f * progress) * s;
                _railFillT.localRotation = Quaternion.Euler(0f, 0f, angle);
                _railFillT.localScale = new Vector3(d.magnitude * progress * s, 0.2f * s, 1f);
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, _assets.perfect * 1.1f);
                _railFill.SetPropertyBlock(_mpb);
            }

            Color cap = _assets.perfect;
            cap.a = 0.5f + 0.5f * intensity;
            SetRing(_cap, cap, cap * 1.3f, progress, 0f, 0f, 0f, 0.05f, 0f, Vector4.zero);
        }

        private void TickBeam(double t, float intensity)
        {
            PlayNote other = _note.partner;
            Vector3 d = other.PositionAt(t) - transform.localPosition;
            float s = 1f / Mathf.Max(0.01f, transform.localScale.x);
            _beamT.localPosition = d * 0.5f * s;
            _beamT.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            float flicker = 0.85f + 0.15f * Mathf.Sin((float)t * 40f);
            _beamT.localScale = new Vector3(Mathf.Max(0.01f, d.magnitude - 1.6f) * s, 0.07f * s, 1f);
            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, _base * (0.5f * intensity * flicker));
            _beam.SetPropertyBlock(_mpb);
        }

        private void TickHit(double t)
        {
            float a = Mathf.Clamp01((float)(t - _stateTime) / 0.4f);
            float ease = 1f - (1f - a) * (1f - a);
            Color c = _assets.GradeColor(_grade);
            Color body = c;
            body.a = 1.3f;
            SetRing(_body, body, c * 1.2f, 1f, ease, 0.4f * (1f - a), _segments, _thickness, (float)t * 0.04f * _spinDir + ease * 0.08f * _spinDir, _arrow);
            _approach.enabled = false;
            _beam.enabled = false;
            _rail.enabled = _railFill.enabled = _cap.enabled = false;
            if (a >= 1f) Release();
        }

        private void TickMiss(double t)
        {
            float a = Mathf.Clamp01((float)(t - _stateTime) / 0.5f);
            Color c = _assets.miss;
            c.a = 1f - a;
            _bodyT.localPosition = new Vector3(0f, -0.8f * a * a, 0f);
            _bandT.localPosition = _bodyT.localPosition;
            SetRing(_body, c, c, 0f, 0f, 0f, _segments, _thickness, 0f, _arrow);
            _approach.enabled = false;
            _beam.enabled = false;
            _rail.enabled = _railFill.enabled = _cap.enabled = false;
            if (a >= 1f) Release();
        }

        private void SetRing(Renderer r, Color color, Color fill, float charge, float burst, float zone, float segments, float thickness, float spin, Vector4 arrow)
        {
            _mpb.Clear();
            _mpb.SetColor(ColorId, color);
            _mpb.SetColor(FillColorId, fill);
            _mpb.SetFloat(FillId, charge);
            _mpb.SetFloat(BurstId, burst);
            _mpb.SetFloat(ZoneId, zone);
            _mpb.SetFloat(SegmentsId, segments);
            _mpb.SetFloat(ThicknessId, thickness);
            _mpb.SetFloat(SpinId, spin);
            _mpb.SetVector(ArrowId, arrow);
            bool body = r == _body && _band.enabled;
            if (body) _band.SetPropertyBlock(_mpb);
            _mpb.SetFloat(BandScaleId, body ? (_note != null && _note.type == NoteType.Swipe ? 0f : 0.2f) : 1f);
            r.SetPropertyBlock(_mpb);
        }
    }
}
