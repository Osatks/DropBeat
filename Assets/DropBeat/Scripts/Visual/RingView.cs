using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// One ring. Colour and the shrinking approach circle are driven by song time, so the circle
    /// closes onto the ring exactly when the ball lands, which is exactly the beat.
    /// </summary>
    public sealed class RingView : MonoBehaviour
    {
        public static readonly Quaternion Tilt = Quaternion.Euler(-58f, 0f, 0f);

        private MeshRenderer _body;
        private MeshRenderer _approach;
        private MeshRenderer _halo;
        private Transform _bodyT;
        private Transform _approachT;
        private Transform _haloT;
        private MaterialPropertyBlock _mpb;
        private VisualAssets _assets;

        private PlayNote _note;
        private Color _base;
        private enum State { Idle, Live, Hit, Missed }
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

            view._bodyT = MakePart(root.transform, "Body", ProceduralMeshes.Torus(0.78f, 0.075f), assets.neon, out view._body);
            view._bodyT.localRotation = Tilt;
            view._approachT = MakePart(root.transform, "Approach", ProceduralMeshes.Torus(0.78f, 0.03f, 64, 6), assets.additive, out view._approach);
            view._approachT.localRotation = Tilt;
            view._haloT = MakePart(root.transform, "Halo", ProceduralMeshes.Quad(), assets.softAdditive, out view._halo);
            view._haloT.localPosition = new Vector3(0f, 0f, 0.35f);
            root.SetActive(false);
            return view;
        }

        private static Transform MakePart(Transform parent, string name, Mesh mesh, Material mat, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
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
            _state = State.Live;
            transform.localPosition = note.position;
            transform.localScale = Vector3.one * note.scale;
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
            _state = grade == Grade.Miss ? State.Missed : State.Hit;
        }

        /// <param name="isTarget">True for the next note the player has to hit.</param>
        public void Tick(double songTime, double approachTime, bool isTarget, float fever)
        {
            if (_note == null) return;
            switch (_state)
            {
                case State.Live: TickLive(songTime, approachTime, isTarget, fever); break;
                case State.Hit: TickHit(songTime); break;
                case State.Missed: TickMiss(songTime); break;
            }
        }

        private void TickLive(double t, double approachTime, bool isTarget, float fever)
        {
            float u = (float)((_note.time - t) / approachTime); // 1 -> 0 over the approach, <0 after the beat
            float closeness = 1f - Mathf.Clamp01(u);

            float intensity = isTarget ? Mathf.Lerp(0.55f, 1.35f, closeness) : 0.32f;
            intensity *= 1f + fever * 0.25f;
            Color body = _base * intensity;
            if (isTarget && closeness > 0.8f)
            {
                body = Color.Lerp(body, _assets.perfect * 0.9f, (closeness - 0.8f) / 0.2f);
            }

            SetColor(_body, body, _base * 0.6f);

            if (u < 1f && u > -0.25f)
            {
                _approach.enabled = true;
                float s = 1f + 2.2f * Mathf.Max(0f, u);
                _approachT.localScale = new Vector3(s, s, s);
                float a = Mathf.Clamp01(closeness * 1.4f) * (u < 0f ? 1f + u * 4f : 1f);
                SetColor(_approach, Color.Lerp(_base, Color.white * 2f, closeness) * a * (isTarget ? 1f : 0.45f), Color.black);
            }
            else
            {
                _approach.enabled = false;
            }

            float halo = isTarget ? 0.18f + 0.35f * closeness * closeness : 0.06f;
            _haloT.localScale = Vector3.one * 2.8f;
            SetColor(_halo, _base * halo, Color.black);
            _bodyT.localScale = Vector3.one;
            _bodyT.localPosition = Vector3.zero;
        }

        private void TickHit(double t)
        {
            float a = Mathf.Clamp01((float)(t - _stateTime) / 0.35f);
            float ease = 1f - (1f - a) * (1f - a);
            Color c = _assets.GradeColor(_grade);
            _bodyT.localScale = Vector3.one * Mathf.Lerp(1f, _grade == Grade.Perfect ? 1.9f : 1.5f, ease);
            SetColor(_body, c * (1f - a) * 1.1f, c * 0.6f * (1f - a));
            _approach.enabled = false;
            _haloT.localScale = Vector3.one * Mathf.Lerp(2.6f, 4.2f, ease);
            SetColor(_halo, c * 0.28f * (1f - a) * (1f - a), Color.black);
            if (a >= 1f) Release();
        }

        private void TickMiss(double t)
        {
            float a = Mathf.Clamp01((float)(t - _stateTime) / 0.5f);
            Color c = _assets.miss;
            _bodyT.localPosition = new Vector3(0f, -0.7f * a * a, 0f);
            _bodyT.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, a);
            SetColor(_body, c * (1f - a), c * 0.5f * (1f - a));
            _approach.enabled = false;
            SetColor(_halo, c * 0.25f * (1f - a), Color.black);
            if (a >= 1f) Release();
        }

        private void SetColor(Renderer r, Color c, Color rim)
        {
            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, c);
            _mpb.SetColor(VisualAssets.RimId, rim);
            r.SetPropertyBlock(_mpb);
        }
    }
}
