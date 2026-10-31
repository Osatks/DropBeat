using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// The Perfect glint: a 4-point star flash that sits just in front of the hit, faces the camera,
    /// flares in 25 ms and is gone by 140 ms. A small pool, driven by song time.
    /// </summary>
    public sealed class ImpactFX : MonoBehaviour
    {
        private const int PoolSize = 6;
        private const double Life = 0.14;

        private struct Glint
        {
            public Transform root, a, b;
            public MeshRenderer ra, rb;
            public Vector3 pos;
            public double born;
            public float size;
        }

        private Glint[] _pool;
        private int _next;
        private MaterialPropertyBlock _mpb;

        public static ImpactFX Create(Transform parent, Material softAdditive)
        {
            var go = new GameObject("ImpactFX");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<ImpactFX>();
            fx._mpb = new MaterialPropertyBlock();
            fx._pool = new Glint[PoolSize];
            Mesh quad = ProceduralMeshes.Quad();
            for (int i = 0; i < PoolSize; i++)
            {
                var root = new GameObject("Glint" + i).transform;
                root.SetParent(go.transform, false);
                fx._pool[i] = new Glint
                {
                    root = root,
                    a = Bar(root, "H", quad, softAdditive, out MeshRenderer ra),
                    b = Bar(root, "V", quad, softAdditive, out MeshRenderer rb),
                    born = double.NegativeInfinity
                };
                fx._pool[i].ra = ra;
                fx._pool[i].rb = rb;
                root.gameObject.SetActive(false);
            }
            return fx;
        }

        private static Transform Bar(Transform parent, string name, Mesh mesh, Material mat, out MeshRenderer r)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Starts a glint at <paramref name="pos"/> (song time <paramref name="now"/>); size 1 = normal.</summary>
        public void Play(Vector3 pos, double now, float size = 1f)
        {
            ref Glint g = ref _pool[_next];
            _next = (_next + 1) % PoolSize;
            g.pos = pos;
            g.born = now;
            g.size = size;
            g.root.gameObject.SetActive(true);
        }

        public void Tick(double t, Transform cam)
        {
            if (_pool == null || cam == null) return;
            for (int i = 0; i < PoolSize; i++)
            {
                ref Glint g = ref _pool[i];
                if (!g.root.gameObject.activeSelf) continue;
                double age = t - g.born;
                if (age < 0.0 || age > Life)
                {
                    g.root.gameObject.SetActive(false);
                    continue;
                }
                float u = (float)(age / Life);
                // Flare in over 25 ms, fall away by the end.
                float e = age < 0.025 ? (float)(age / 0.025) : 1f - Mathf.InverseLerp(0.025f, (float)Life, (float)age);
                e = Mathf.Clamp01(e);
                Vector3 toCam = cam.position - g.pos;
                g.root.position = g.pos + (toCam.sqrMagnitude > 1e-6f ? toCam.normalized * 0.55f : Vector3.zero);
                g.root.rotation = cam.rotation * Quaternion.Euler(0f, 0f, 25f * u);
                float len = 2.2f * e * g.size;
                g.a.localScale = new Vector3(Mathf.Max(0.001f, len), 0.09f * g.size, 1f);
                g.b.localScale = new Vector3(0.09f * g.size, Mathf.Max(0.001f, len), 1f);
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, Color.Lerp(Color.white * 2.2f, new Color(1.6f, 1.25f, 0.5f, 1f), u));
                g.ra.SetPropertyBlock(_mpb);
                g.rb.SetPropertyBlock(_mpb);
            }
        }

        public void Clear()
        {
            if (_pool == null) return;
            for (int i = 0; i < PoolSize; i++) _pool[i].root.gameObject.SetActive(false);
        }
    }
}
