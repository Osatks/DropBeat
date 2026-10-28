using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Backdrop, parallax grid, star field, edge spectrum bars and downbeat shockwaves.
    /// Pulses are driven from the song's beat grid on the song clock, so they flash exactly on the beat.
    /// </summary>
    public sealed class BackgroundFX : MonoBehaviour
    {
        private const int Bands = 18;
        private const float GridSpacing = 2.5f;

        private VisualAssets _assets;
        private CameraRig _rig;
        private AudioSource _music;
        private MaterialPropertyBlock _mpb;

        private Transform _backdrop;
        private Transform _grid;
        private MeshRenderer _gridRenderer;
        private ParticleSystem _stars;
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

            _backdrop = MakeRenderer("Backdrop", ProceduralMeshes.GradientQuad(new Color(0.02f, 0.01f, 0.06f), new Color(0.09f, 0.02f, 0.16f)), assets.additive, out _);
            _backdrop.localScale = new Vector3(260f, 160f, 1f);

            _grid = MakeRenderer("Grid", ProceduralMeshes.GridLines(46f, 34f, GridSpacing, 0.05f), assets.additive, out _gridRenderer);

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
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.8f, 1f, 0.8f), new Color(1f, 0.6f, 1f, 0.8f));
            main.maxParticles = 600;
            var emission = ps.emission;
            emission.rateOverTime = 45f;
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

            _backdrop.position = new Vector3(cam.x, cam.y, 70f);

            float scroll = Mathf.Repeat(cam.y * 0.55f, GridSpacing);
            _grid.position = new Vector3(cam.x * 0.5f, cam.y - scroll, 24f);
            _stars.transform.position = new Vector3(cam.x, cam.y + 4f, 20f);

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
            float gridI = 0.12f + 0.28f * _pulse + 0.25f * _hitFlash + 0.1f * fever;
            SetColor(_gridRenderer, Color.Lerp(_accent, new Color(1.6f, 0.4f, 2.2f), 0.35f) * gridI);

            TickSpectrum(dt, cam, fever);
            TickWaves(dt);
        }

        private void TickSpectrum(float dt, Vector3 cam, float fever)
        {
            bool playing = _music != null && _music.isPlaying;
            if (playing) _music.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);

            Camera c = _rig.Cam;
            float z = 10f;
            float dist = z - cam.z;
            float halfH = dist * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * c.aspect;
            float barH = halfH * 2f / Bands * 0.42f;
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
                float y = cam.y - halfH * 0.9f + (i + 0.5f) * (halfH * 1.8f / Bands);
                _barsL[i].position = new Vector3(cam.x - halfW + len * 0.5f, y, z);
                _barsR[i].position = new Vector3(cam.x + halfW - len * 0.5f, y, z);
                _barsL[i].localScale = new Vector3(len, barH, 1f);
                _barsR[i].localScale = new Vector3(len, barH, 1f);
                Color col = Color.Lerp(_accent, new Color(2.6f, 0.35f, 1.9f), i / (float)Bands) * (0.05f + 0.28f * _levels[i] + 0.06f * fever);
                SetColor(_barRenderers[i * 2], col);
                SetColor(_barRenderers[i * 2 + 1], col);
            }
        }

        private void SpawnWave(Vector3 focus)
        {
            int i = _nextWave;
            _nextWave = (_nextWave + 1) % _waves.Length;
            _waveAge[i] = 0f;
            _waves[i].gameObject.SetActive(true);
            _waves[i].position = new Vector3(focus.x, focus.y, 14f);
            _waves[i].rotation = Quaternion.Euler(90f, 0f, 0f);
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
                float r = Mathf.Lerp(1.5f, 22f, 1f - (1f - a) * (1f - a));
                _waves[i].localScale = new Vector3(r, r, r);
                SetColor(_waveRenderers[i], _accent * (0.9f * (1f - a)));
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
