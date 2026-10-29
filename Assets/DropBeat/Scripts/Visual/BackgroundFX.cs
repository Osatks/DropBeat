using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Backdrop, world-space grid wall, floating wireframe cubes, star field, edge spectrum bars and downbeat
    /// shockwaves. The grid and cubes are fixed in the world and the sky turns with the camera, so camera
    /// perspective changes read as real movement through depth.
    /// Pulses are driven from the song's beat grid on the song clock, so they flash exactly on the beat.
    /// </summary>
    public sealed class BackgroundFX : MonoBehaviour
    {
        private const int Bands = 18;
        private const float GridSpacing = 2.5f;
        private const float GridDepth = 30f;
        private const int Cubes = 18;
        // Square and large (about +-60 degrees at 85 units) so the turned sky still fills the tunnel view.
        private const float BackdropSize = 300f;
        private static readonly int QuadSizeId = Shader.PropertyToID("_QuadSize");
        private static readonly int SunIntensityId = Shader.PropertyToID("_SunIntensity");
        private float _sunIntensity = -1f;
        private const float CubeSpan = 56f;

        private VisualAssets _assets;
        private CameraRig _rig;
        private AudioSource _music;
        private MaterialPropertyBlock _mpb;

        private Transform _backdrop;
        private MeshRenderer _backdropRenderer;
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int ScrollId = Shader.PropertyToID("_Scroll");
        private static readonly int SunCenterId = Shader.PropertyToID("_SunCenter");
        private static readonly int SunRadiusId = Shader.PropertyToID("_SunRadius");
        private Transform _grid;
        private MeshRenderer _gridRenderer;
        private ParticleSystem _stars;
        private Transform[] _cubes;
        private MeshRenderer[] _cubeRenderers;
        private Vector3[] _cubeSpin;
        private Transform[] _barsL;
        private Transform[] _barsR;
        private MeshRenderer[] _barRenderers;
        private float[] _levels;
        private float[] _spectrum;
        private Transform[] _waves;
        private MeshRenderer[] _waveRenderers;
        private float[] _waveAge;
        private int _nextWave;

        private Color _accent = new Color(0f, 2.2f, 2.6f, 1f);
        private int _lastBeat = int.MinValue;
        private float _pulse;
        private float _hitFlash;

        /// <summary>Raised once per beat crossing: (beat index, is downbeat).</summary>
        public event System.Action<int, bool> Beat;

        public void Init(VisualAssets assets, CameraRig rig, AudioSource music)
        {
            _assets = assets;
            _rig = rig;
            _music = music;
            _mpb = new MaterialPropertyBlock();

            if (assets.backdrop != null)
            {
                _backdrop = MakeRenderer("Backdrop", ProceduralMeshes.Quad(), assets.backdrop, out _backdropRenderer);
            }
            else
            {
                _backdrop = MakeRenderer("Backdrop", ProceduralMeshes.GradientQuad(new Color(0.02f, 0.01f, 0.06f), new Color(0.09f, 0.02f, 0.16f)), assets.additive, out _);
            }
            _backdrop.localScale = new Vector3(BackdropSize, BackdropSize, 1f);

            _grid = MakeRenderer("Grid", ProceduralMeshes.GridLines(60f, 60f, GridSpacing, 0.07f), assets.additive, out _gridRenderer);

            // Wireframe cubes floating at different depths around the path (recycled vertically as the ball climbs).
            _cubes = new Transform[Cubes];
            _cubeRenderers = new MeshRenderer[Cubes];
            _cubeSpin = new Vector3[Cubes];
            var rng = new System.Random(7);
            for (int i = 0; i < Cubes; i++)
            {
                _cubes[i] = MakeRenderer("Cube" + i, ProceduralMeshes.WireCube(0.035f), assets.additive, out _cubeRenderers[i]);
                float side = (i & 1) == 0 ? -1f : 1f;
                float x = side * (5f + (float)rng.NextDouble() * 12f);
                float z = 2f + (float)rng.NextDouble() * 22f;
                float y = (i + (float)rng.NextDouble()) / Cubes * CubeSpan - CubeSpan * 0.5f;
                _cubes[i].position = new Vector3(x, y, z);
                _cubes[i].rotation = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, 0f);
                _cubes[i].localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 2.2f);
                _cubeSpin[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 40f;
            }

            _stars = MakeStars(assets.softAdditive);

            _levels = new float[Bands];
            _spectrum = new float[512];
            _barsL = new Transform[Bands];
            _barsR = new Transform[Bands];
            _barRenderers = new MeshRenderer[Bands * 2];
            for (int i = 0; i < Bands; i++)
            {
                _barsL[i] = MakeRenderer("BarL" + i, ProceduralMeshes.Quad(), assets.additive, out _barRenderers[i * 2]);
                _barsR[i] = MakeRenderer("BarR" + i, ProceduralMeshes.Quad(), assets.additive, out _barRenderers[i * 2 + 1]);
            }

            _waves = new Transform[4];
            _waveRenderers = new MeshRenderer[4];
            _waveAge = new float[4];
            for (int i = 0; i < _waves.Length; i++)
            {
                _waves[i] = MakeRenderer("Wave" + i, ProceduralMeshes.Torus(1f, 0.02f, 96, 4), assets.additive, out _waveRenderers[i]);
                _waves[i].gameObject.SetActive(false);
                _waveAge[i] = 99f;
            }
        }

        public void SetAccent(Color accent) => _accent = accent;

        public void ResetBeat() => _lastBeat = int.MinValue;

        public void HitFlash(float amount) => _hitFlash = Mathf.Max(_hitFlash, amount);

        /// <summary>0..1: overdrive shifts the world toward gold/magenta and makes everything pulse harder.</summary>
        public void SetOverdrive(float level) => _overdrive = Mathf.Clamp01(level);

        private float _overdrive;
        private static readonly Color OverdriveGold = new Color(2.6f, 1.5f, 0.3f, 1f);
        private Color Accent => Color.Lerp(_accent, OverdriveGold, _overdrive * 0.8f);

        private Transform MakeRenderer(string name, Mesh mesh, Material mat, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private ParticleSystem MakeStars(Material mat)
        {
            var go = new GameObject("Stars");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.75f, 1f, 0.55f), new Color(1f, 0.55f, 0.95f, 0.55f));
            main.maxParticles = 260;
            var emission = ps.emission;
            emission.rateOverTime = 20f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            vel.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 44f, 24f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// <param name="songTime">Heard song time, or NaN when no song is running.</param>
        public void Tick(SongData song, double songTime, Vector3 focus, float fever)
        {
            if (_rig == null) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 cam = _rig.transform.position;
            Quaternion rot = _rig.transform.rotation;

            // The sky is "at infinity" (follows the camera position) but turns only part of the way with it,
            // so angled and tunnel views visibly swing the sun and nebula while still filling the screen.
            Quaternion sky = Quaternion.Slerp(_rig.RestRotation, rot, 0.65f);
            _backdrop.SetPositionAndRotation(cam + sky * new Vector3(0f, 0f, 85f), sky);

            // Grid wall fixed in the world behind the path (snapped by whole cells so it never slides).
            float gx = Mathf.Round(focus.x / GridSpacing) * GridSpacing;
            float gy = Mathf.Round(focus.y / GridSpacing) * GridSpacing;
            _grid.SetPositionAndRotation(new Vector3(gx, gy, GridDepth), Quaternion.identity);
            _stars.transform.position = cam + rot * new Vector3(0f, 4f, 22f);
            TickCubes(dt, focus);

            // Beat pulse from the grid (exact) rather than from audio levels (late and noisy).
            bool down = false;
            if (song != null && !double.IsNaN(songTime))
            {
                int k = song.BeatIndexAt(songTime);
                double since = songTime - song.BeatTime(k);
                down = song.IsDownbeat(k);
                _pulse = Mathf.Exp(-(float)since * 7f) * (down ? 1f : 0.6f);
                if (k != _lastBeat)
                {
                    bool fresh = _lastBeat != int.MinValue && k == _lastBeat + 1 && since < 0.1;
                    _lastBeat = k;
                    if (fresh)
                    {
                        if (down) SpawnWave(focus);
                        Beat?.Invoke(k, down);
                    }
                }
            }
            else
            {
                _pulse = Mathf.MoveTowards(_pulse, 0f, dt * 3f);
            }

            _hitFlash = Mathf.MoveTowards(_hitFlash, 0f, dt * 3.5f);
            float gridI = (0.08f + 0.22f * _pulse + 0.2f * _hitFlash + 0.08f * fever) * (1f + 0.55f * _overdrive);
            SetColor(_gridRenderer, Color.Lerp(Accent, new Color(1.6f, 0.4f, 2.2f), 0.35f) * gridI);

            if (_backdropRenderer != null)
            {
                _mpb.Clear();
                _mpb.SetColor(AccentId, Accent);
                _mpb.SetVector(QuadSizeId, new Vector4(BackdropSize, BackdropSize, 0f, 0f));
                // Looking up the tunnel the sun swings behind the rings: dim it so the rings stay readable.
                if (_sunIntensity < 0f) _sunIntensity = _backdropRenderer.sharedMaterial.GetFloat(SunIntensityId);
                float lookUp = Mathf.InverseLerp(0.1f, 0.7f, (rot * Vector3.forward).y);
                _mpb.SetFloat(SunIntensityId, _sunIntensity * (1f - 0.65f * lookUp));
                _mpb.SetFloat(PulseId, Mathf.Clamp01(_pulse * (0.8f + 0.6f * _overdrive) + _hitFlash * 0.3f));
                _mpb.SetVector(ScrollId, new Vector4(cam.x * 0.3f, cam.y * 0.22f, 0f, 0f));
                // Narrow phone views get a smaller sun so it frames the path instead of filling it.
                bool portrait = _rig.Cam.aspect < 1f;
                _mpb.SetVector(SunCenterId, portrait ? new Vector4(4f, 15f, 0f, 0f) : new Vector4(8f, 11f, 0f, 0f));
                _mpb.SetFloat(SunRadiusId, portrait ? 9.5f : 13f);
                _backdropRenderer.SetPropertyBlock(_mpb);
            }

            TickSpectrum(dt, cam, rot, fever);
            TickWaves(dt);
        }

        private void TickSpectrum(float dt, Vector3 cam, Quaternion rot, float fever)
        {
            bool playing = _music != null && _music.isPlaying;
            if (playing) _music.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

            Camera c = _rig.Cam;
            const float dist = 12f;
            float halfH = dist * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * c.aspect;
            float barH = halfH * 2f / Bands * 0.3f;
            for (int i = 0; i < Bands; i++)
            {
                float level = 0f;
                if (playing)
                {
                    int a = Mathf.FloorToInt(Mathf.Pow(2f, i * 8.6f / Bands));
                    int b = Mathf.Max(a + 1, Mathf.FloorToInt(Mathf.Pow(2f, (i + 1) * 8.6f / Bands)));
                    float sum = 0f;
                    for (int j = a; j < b && j < _spectrum.Length; j++) sum += _spectrum[j];
                    level = Mathf.Clamp01(Mathf.Sqrt(sum) * (1.6f + i * 0.25f));
                }

                _levels[i] = level > _levels[i] ? Mathf.Lerp(_levels[i], level, 0.6f) : Mathf.MoveTowards(_levels[i], level, dt * 1.8f);
                float len = 0.08f + _levels[i] * halfW * 0.16f;
                float y = -halfH * 0.9f + (i + 0.5f) * (halfH * 1.8f / Bands);
                _barsL[i].SetPositionAndRotation(cam + rot * new Vector3(-halfW + len * 0.5f, y, dist), rot);
                _barsR[i].SetPositionAndRotation(cam + rot * new Vector3(halfW - len * 0.5f, y, dist), rot);
                _barsL[i].localScale = new Vector3(len, barH, 1f);
                _barsR[i].localScale = new Vector3(len, barH, 1f);
                Color col = Color.Lerp(Accent, new Color(2.6f, 0.35f, 1.9f), i / (float)Bands) * (0.035f + 0.22f * _levels[i] + 0.05f * fever) * (1f + 0.5f * _overdrive);
                SetColor(_barRenderers[i * 2], col);
                SetColor(_barRenderers[i * 2 + 1], col);
            }
        }

        private void TickCubes(float dt, Vector3 focus)
        {
            Color c = Color.Lerp(Accent, new Color(1.6f, 0.4f, 2.2f), 0.5f);
            for (int i = 0; i < Cubes; i++)
            {
                Transform t = _cubes[i];
                Vector3 p = t.position;
                if (p.y < focus.y - CubeSpan * 0.5f) p.y += CubeSpan;
                else if (p.y > focus.y + CubeSpan * 0.5f) p.y -= CubeSpan;
                t.position = p;
                t.Rotate(_cubeSpin[i] * dt, Space.Self);
                // Nearer cubes slightly brighter; all of them pulse on the beat.
                float near = Mathf.InverseLerp(26f, 2f, p.z);
                SetColor(_cubeRenderers[i], c * ((0.06f + 0.06f * near) * (1f + 1.5f * _pulse + _hitFlash) * (1f + 0.6f * _overdrive)));
            }
        }

        private void SpawnWave(Vector3 focus)
        {
            int i = _nextWave;
            _nextWave = (_nextWave + 1) % _waves.Length;
            _waveAge[i] = 0f;
            _waves[i].gameObject.SetActive(true);
            Quaternion rot = _rig.transform.rotation;
            _waves[i].SetPositionAndRotation(focus + rot * new Vector3(0f, 0f, 14f), rot * Quaternion.Euler(90f, 0f, 0f));
        }

        private void TickWaves(float dt)
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                if (_waveAge[i] > 1.2f)
                {
                    if (_waves[i].gameObject.activeSelf) _waves[i].gameObject.SetActive(false);
                    continue;
                }

                _waveAge[i] += dt;
                float a = Mathf.Clamp01(_waveAge[i] / 1.2f);
                float r = Mathf.Lerp(1.5f, 13f, 1f - (1f - a) * (1f - a));
                _waves[i].localScale = new Vector3(r, r, r);
                SetColor(_waveRenderers[i], Accent * (0.3f * (1f - a) * (1f - a)) * (1f + _overdrive));
            }
        }

        private void SetColor(Renderer r, Color c)
        {
            _mpb.Clear();
            _mpb.SetColor(VisualAssets.ColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
