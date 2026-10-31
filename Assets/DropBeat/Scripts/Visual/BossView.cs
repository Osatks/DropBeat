using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// The 8-Bit Hero boss: a crowned pixel demon built from glowing voxels, floating behind the finale's
    /// ring circle. Three halos of pixel dots turn round it (the middle one is the track the rings ride),
    /// every hit fires a pixel laser from the ring into the boss, and when its health runs out it bursts
    /// into voxels. Everything is driven by song time, so pausing freezes it.
    /// </summary>
    public sealed class BossView : MonoBehaviour
    {
        // Pixel art, top row first: C crown, H horns, B body, E eyes, D nose, T teeth.
        private static readonly string[] Art =
        {
            "..C....C....C..",
            "..CC..CCC..CC..",
            "..CCCCCCCCCCC..",
            ".H.BBBBBBBBB.H.",
            "HH.BBBBBBBBB.HH",
            "H.BBBBBBBBBBB.H",
            "..BEEBBBBBEEB..",
            "..BEEBBBBBEEB..",
            "..BBBBBDBBBBB..",
            "...BBBBBBBBB...",
            "...BTBTBTBTB...",
            "....BBBBBBB....",
            "..B..B...B..B.."
        };

        private const string Parts = "CHBEDT";
        private static readonly Color[] PartColor =
        {
            new Color(2.4f, 1.6f, 0.25f, 1f),  // crown
            new Color(1.3f, 1.2f, 1.5f, 1f),   // horns
            new Color(0.32f, 0.07f, 0.55f, 1f),// body
            new Color(3.2f, 0.35f, 0.2f, 1f),  // eyes
            new Color(0.08f, 0.02f, 0.16f, 1f),// nose
            new Color(1.9f, 1.9f, 2.0f, 1f)    // teeth
        };
        private static readonly Color[] PartRim =
        {
            new Color(1.2f, 0.9f, 0.3f, 1f),
            new Color(0.6f, 0.6f, 0.9f, 1f),
            new Color(1.4f, 0.3f, 1.6f, 1f),
            new Color(1.5f, 0.6f, 0.4f, 1f),
            new Color(0.4f, 0.1f, 0.6f, 1f),
            new Color(0.6f, 0.6f, 0.8f, 1f)
        };
        private const int Eyes = 3;
        private const float Pixel = 0.24f;
        /// <summary>The boss floats this far behind the ring circle, so the rings always pass in front.</summary>
        private const float Behind = 0.9f;
        private const float InnerRadius = 2.15f;

        private static readonly Color[] HaloPalette =
        {
            new Color(2.8f, 0.45f, 0.3f, 1f),
            new Color(2.8f, 1.8f, 0.25f, 1f),
            new Color(0.35f, 2.2f, 2.8f, 1f),
            new Color(2.4f, 0.45f, 2.6f, 1f),
            new Color(0.6f, 2.7f, 0.6f, 1f)
        };

        private Transform _body;
        private MeshRenderer[] _parts;
        private Transform _haloTrack, _haloInner, _haloOuter;
        private MeshRenderer _rTrack, _rInner, _rOuter;
        private MaterialPropertyBlock _mpb;

        private struct Laser
        {
            public Transform t;
            public MeshRenderer r;
            public Vector3 from;
            public Color color;
            public double born;
        }
        private Laser[] _lasers;
        private int _nextLaser;

        private struct Bit
        {
            public Transform t;
            public MeshRenderer r;
            public Vector3 home, vel, spin;
            public int part;
        }
        private Bit[] _bits;

        private Vector3 _center;
        private float _orbitPerSec;
        private double _appear, _end, _spb;
        private double _hitAt = double.NegativeInfinity, _defeatAt = double.PositiveInfinity;
        private double _fireAt = double.NegativeInfinity, _roarAt = double.NegativeInfinity;
        private Vector3 _recoil;
        private float _hitStrength;
        private bool _active;

        public static BossView Create(Transform parent, VisualAssets assets)
        {
            var go = new GameObject("Boss");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BossView>();
            b.Build(assets);
            go.SetActive(false);
            return b;
        }

        private void Build(VisualAssets assets)
        {
            _mpb = new MaterialPropertyBlock();
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);

            // One merged voxel mesh per colour (the Neon shader has one colour per renderer).
            _parts = new MeshRenderer[Parts.Length];
            int rows = Art.Length, cols = Art[0].Length;
            Mesh box = ProceduralMeshes.Box();
            var bitList = new System.Collections.Generic.List<Bit>();
            for (int p = 0; p < Parts.Length; p++)
            {
                var combine = new System.Collections.Generic.List<CombineInstance>();
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (Art[r][c] != Parts[p]) continue;
                        Vector3 pos = CellPos(r, c, rows, cols);
                        combine.Add(new CombineInstance
                        {
                            mesh = box,
                            transform = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(Pixel, Pixel, Pixel * 1.6f) * 0.96f)
                        });
                        if (((r + c) & 1) == 0) bitList.Add(new Bit { home = pos, part = p });
                    }
                }
                var mesh = new Mesh { name = "Boss" + Parts[p] };
                mesh.CombineMeshes(combine.ToArray(), true, true);
                var part = new GameObject("Part" + Parts[p]);
                part.transform.SetParent(_body, false);
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                _parts[p] = part.AddComponent<MeshRenderer>();
                _parts[p].sharedMaterial = assets.neon;
                _parts[p].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _parts[p].receiveShadows = false;
            }

            // Pixel-dot halos in the ring plane (z = 0), each dot a colour of the 8-bit palette.
            _haloTrack = Halo("Track", assets.additive, 44, LevelLayout.BossRadius, 0.16f, out _rTrack);
            _haloInner = Halo("Inner", assets.additive, 26, InnerRadius, 0.13f, out _rInner);
            _haloOuter = Halo("Outer", assets.additive, 60, LevelLayout.BossRadius + 1.05f, 0.1f, out _rOuter);

            _lasers = new Laser[4];
            for (int i = 0; i < _lasers.Length; i++)
            {
                var lg = new GameObject("Laser" + i);
                lg.transform.SetParent(transform, false);
                lg.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Quad();
                var lr = lg.AddComponent<MeshRenderer>();
                lr.sharedMaterial = assets.additive;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.enabled = false;
                _lasers[i] = new Laser { t = lg.transform, r = lr, born = double.NegativeInfinity };
            }

            // Voxels for the explosion (every other cell), made up front so defeat costs nothing.
            _bits = bitList.ToArray();
            for (int i = 0; i < _bits.Length; i++)
            {
                var bg = new GameObject("Bit" + i);
                bg.transform.SetParent(transform, false);
                bg.AddComponent<MeshFilter>().sharedMesh = box;
                var br = bg.AddComponent<MeshRenderer>();
                br.sharedMaterial = assets.neon;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                br.receiveShadows = false;
                bg.SetActive(false);
                Vector3 h = _bits[i].home;
                // Deterministic burst: outward from the centre, faster for the outer cells, a hop upward.
                Vector3 dir = h.sqrMagnitude > 1e-4f ? h.normalized : Vector3.up;
                float speed = 3.5f + 2.5f * Mathf.Repeat(i * 0.618f, 1f);
                _bits[i].t = bg.transform;
                _bits[i].r = br;
                _bits[i].vel = dir * speed + new Vector3(0f, 2.5f, -1.5f * Mathf.Repeat(i * 0.37f, 1f));
                _bits[i].spin = new Vector3(Mathf.Repeat(i * 0.73f, 1f) - 0.5f, Mathf.Repeat(i * 0.41f, 1f) - 0.5f, 0.3f) * 900f;
            }
        }

        private static Vector3 CellPos(int r, int c, int rows, int cols) =>
            new Vector3((c - (cols - 1) * 0.5f) * Pixel, ((rows - 1) * 0.5f - r) * Pixel, 0f);

        private Transform Halo(string name, Material mat, int dots, float radius, float size, out MeshRenderer renderer)
        {
            var verts = new Vector3[dots * 4];
            var cols = new Color[dots * 4];
            var tris = new int[dots * 6];
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
                float h = size * 0.5f;
                verts[i * 4] = c + new Vector3(-h, -h, 0f);
                verts[i * 4 + 1] = c + new Vector3(h, -h, 0f);
                verts[i * 4 + 2] = c + new Vector3(-h, h, 0f);
                verts[i * 4 + 3] = c + new Vector3(h, h, 0f);
                // Runs of the palette round the circle, every third dot dimmer: a twinkling pixel chain.
                Color col = HaloPalette[(i / 2) % HaloPalette.Length] * (i % 3 == 2 ? 0.45f : 1f);
                col.a = 1f;
                for (int k = 0; k < 4; k++) cols[i * 4 + k] = col;
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4 + 2; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 1;
            }
            var mesh = new Mesh { name = "Halo" + name, vertices = verts, colors = cols, triangles = tris };
            mesh.RecalculateBounds();
            var go = new GameObject("Halo" + name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>Sets up the fight: the circle's centre, the carousel speed, when the boss appears and when the arena ends.</summary>
        public void Begin(Vector3 center, float orbitPerBeat, double appear, double end, double spb)
        {
            _center = center;
            _spb = spb;
            _orbitPerSec = orbitPerBeat / (float)spb;
            _appear = appear;
            _end = end;
            _hitAt = double.NegativeInfinity;
            _defeatAt = double.PositiveInfinity;
            _fireAt = _roarAt = double.NegativeInfinity;
            _active = true;
            for (int i = 0; i < _lasers.Length; i++)
            {
                _lasers[i].born = double.NegativeInfinity;
                _lasers[i].r.enabled = false;
            }
            for (int i = 0; i < _bits.Length; i++) _bits[i].t.gameObject.SetActive(false);
            _body.gameObject.SetActive(true);
            gameObject.SetActive(false);
        }

        public void Clear()
        {
            _active = false;
            gameObject.SetActive(false);
        }

        /// <summary>A ring was hit: a pixel laser from the ring into the boss, a white flash and a recoil.</summary>
        public void Hit(Vector3 ringPos, Color color, float strength, double t)
        {
            if (!_active || t >= _defeatAt) return;
            ref Laser l = ref _lasers[_nextLaser];
            _nextLaser = (_nextLaser + 1) % _lasers.Length;
            l.from = ringPos;
            l.color = color;
            l.born = t;
            _hitAt = t;
            _hitStrength = strength;
            Vector3 d = _center - ringPos;
            d.z = 0f;
            _recoil = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up;
        }

        /// <summary>The boss fires a ring (barrage): its eyes flare and it lurches forward.</summary>
        public void Fire(double t) => _fireAt = t;

        /// <summary>A new attack begins: a bigger flare and swell.</summary>
        public void Roar(double t) => _roarAt = t;

        public void Defeat(double t)
        {
            if (!_active) return;
            _defeatAt = t;
        }

        /// <param name="beatPos">Beat position (beat index + fraction); <paramref name="downbeat"/>: is the current beat the bar's first.</param>
        public void Tick(double t, float beatPos, bool downbeat, float hp01)
        {
            if (!_active) return;
            bool visible = t >= _appear - _spb * 4.0;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible) return;

            float frac = beatPos - Mathf.Floor(beatPos);
            float beatPulse = Mathf.Exp(-frac * 5f);
            // Entrance: grows in with an overshoot over the four beats before the fight.
            float enter = Mathf.Clamp01((float)((t - (_appear - _spb * 4.0)) / (_spb * 3.0)));
            float grow = enter >= 1f ? 1f : 1f + 2.70158f * Mathf.Pow(enter - 1f, 3f) + 1.70158f * Mathf.Pow(enter - 1f, 2f);

            bool dead = t >= _defeatAt;
            bool escaped = !dead && t > _end;
            float flash = (float)System.Math.Exp(-(t - _hitAt) / 0.09);
            if (t < _hitAt) flash = 0f;
            float fire = t >= _fireAt ? (float)System.Math.Exp(-(t - _fireAt) / 0.12) : 0f;
            float roar = t >= _roarAt ? (float)System.Math.Exp(-(t - _roarAt) / 0.35) : 0f;

            // ---- body
            if (dead)
            {
                if (_body.gameObject.activeSelf) _body.gameObject.SetActive(false);
            }
            else
            {
                float bob = 0.16f * Mathf.Sin(beatPos * Mathf.PI);
                float stomp = downbeat ? 0.08f * Mathf.Exp(-frac * 6f) : 0f;
                Vector3 pos = _center + new Vector3(0f, bob, Behind) + _recoil * (0.22f * flash * _hitStrength);
                float scale = grow * (1f + stomp + 0.06f * fire + 0.15f * roar);
                if (escaped)
                {
                    float e = (float)((t - _end) / 1.2);
                    pos += new Vector3(0f, 7f * e * e, 0f);
                    scale *= Mathf.Max(0f, 1f - e * 0.6f);
                }
                _body.localPosition = pos;
                _body.localRotation = Quaternion.Euler(0f, 14f * Mathf.Sin(beatPos * Mathf.PI * 0.25f), 3f * Mathf.Sin(beatPos * Mathf.PI * 0.5f));
                _body.localScale = Vector3.one * Mathf.Max(0.001f, scale);

                // Low health: the body flickers red. Hits flash it white-hot.
                float hurt = hp01 < 0.3f ? 0.5f + 0.5f * Mathf.Sin((float)t * 22f) : 0f;
                for (int p = 0; p < _parts.Length; p++)
                {
                    Color c = PartColor[p];
                    if (p == Eyes) c *= 0.8f + 0.6f * beatPulse + 1.2f * fire + 1.5f * roar;
                    else c = Color.Lerp(c, new Color(1.8f, 0.2f, 0.25f, 1f), hurt * 0.35f);
                    // A hot flash, not a white-out: hits come every beat, so keep the pixels readable.
                    c = Color.Lerp(c, Color.white * 1.6f, flash * 0.4f * _hitStrength);
                    _mpb.Clear();
                    _mpb.SetColor(VisualAssets.ColorId, c);
                    _mpb.SetColor(VisualAssets.RimId, PartRim[p] * (1f + beatPulse * 0.5f));
                    _parts[p].SetPropertyBlock(_mpb);
                }
            }

            // ---- halos: the track turns with the ring carousel, the others the other way.
            float spin = (float)t * _orbitPerSec;
            float burstOut = dead ? Mathf.Clamp01((float)((t - _defeatAt) / 1.4)) : 0f;
            float fade = dead ? 1f - burstOut : escaped ? Mathf.Clamp01(1f - (float)((t - _end) / 0.8)) : 1f;
            float glow = (0.55f + 0.55f * beatPulse + 0.8f * flash) * fade * Mathf.Clamp01(enter * 1.5f);
            SetHalo(_haloTrack, _rTrack, grow * (1f + burstOut * 1.5f), -spin, glow);
            SetHalo(_haloInner, _rInner, grow * (1f + burstOut * 2.5f), spin * 1.6f, glow * 0.7f);
            SetHalo(_haloOuter, _rOuter, grow * (1f + 0.04f * beatPulse + burstOut), spin * 0.5f, glow * 0.45f);

            // ---- lasers
            for (int i = 0; i < _lasers.Length; i++)
            {
                ref Laser l = ref _lasers[i];
                float age = (float)(t - l.born);
                bool on = age >= 0f && age < 0.18f && !dead;
                if (l.r.enabled != on) l.r.enabled = on;
                if (!on) continue;
                Vector3 to = _body.localPosition;
                Vector3 d = to - l.from;
                l.t.localPosition = l.from + d * 0.5f;
                l.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                float k = 1f - age / 0.18f;
                // Pixel beam: thick and chunky, thinning as it fades.
                l.t.localScale = new Vector3(d.magnitude, 0.22f * k + 0.04f, 1f);
                _mpb.Clear();
                _mpb.SetColor(VisualAssets.ColorId, Color.Lerp(l.color, Color.white * 2f, 0.35f) * k);
                l.r.SetPropertyBlock(_mpb);
            }

            // ---- explosion
            if (dead)
            {
                float age = (float)(t - _defeatAt);
                bool live = age < 1.8f;
                for (int i = 0; i < _bits.Length; i++)
                {
                    ref Bit b = ref _bits[i];
                    if (b.t.gameObject.activeSelf != live) b.t.gameObject.SetActive(live);
                    if (!live) continue;
                    b.t.localPosition = _center + new Vector3(0f, 0f, Behind) + b.home + b.vel * age + new Vector3(0f, -4.5f * age * age, 0f);
                    b.t.localRotation = Quaternion.Euler(b.spin * age);
                    float s = Pixel * 1.1f * Mathf.Clamp01(1f - age / 1.8f);
                    b.t.localScale = Vector3.one * Mathf.Max(0.001f, s);
                    Color c = Color.Lerp(Color.white * 3f, PartColor[b.part] * 1.6f, Mathf.Clamp01(age * 4f));
                    _mpb.Clear();
                    _mpb.SetColor(VisualAssets.ColorId, c);
                    _mpb.SetColor(VisualAssets.RimId, PartRim[b.part]);
                    b.r.SetPropertyBlock(_mpb);
                }
            }
        }

        private void SetHalo(Transform halo, MeshRenderer r, float scale, float angle, float glow)
        {
            bool on = glow > 0.01f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            halo.localPosition = _center;
            halo.localRotation = Quaternion.Euler(0f, 0f, angle);
            halo.localScale = new Vector3(scale, scale, 1f);
            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, new Color(glow, glow, glow, 1f));
            r.SetPropertyBlock(_mpb);
        }
    }
}
