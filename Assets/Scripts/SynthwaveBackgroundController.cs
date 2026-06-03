using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class SynthwaveBackgroundController : MonoBehaviour
{
    [Header("References")]
    public AudioSource audioSource;
    public Volume globalVolume;
    public Camera targetCamera;

    [Header("Tempo")]
    [Min(30f)] public float bpm = 128f;
    public bool useAudioClock = false;

    [Header("Grid")]
    [Min(4)] public int gridColumns = 20;
    [Min(4)] public int floorGridRows = 28;
    [Min(4)] public int backGridRows = 16;
    [Min(0.25f)] public float gridSquareSize = 1.2f;
    public float floorNearZ = 0f;
    public float backWallZ = 60f;
    public Gradient gridColorGradient;

    [Header("Sky Shapes (2D)")]
    [Min(4)] public int skyShapeCount = 24;
    public Vector2 skyXRange = new Vector2(-22f, 22f);
    public Vector2 skyYRange = new Vector2(6f, 20f);
    public Vector2 skyScaleRange = new Vector2(0.6f, 1.8f);
    [Range(0.1f, 8f)] public float driftSpeed = 1.1f;

    [Header("Music Reactive")]
    [Range(1f, 4f)] public float quarterBeatBrightnessMultiplier = 2f;
    [Range(0.01f, 0.3f)] public float pulseDuration = 0.08f;
    [Range(0f, 1f)] public float bassDropThreshold = 0.22f;
    [Min(1)] public int bassDropBeats = 4;
    [Range(0.05f, 2f)] public float extrusionHeight = 6f;

    [Header("Oscilloscope")]
    [Min(2)] public int waveSamples = 64;
    [Range(0.1f, 5f)] public float waveHeight = 1.4f;
    [Range(0.1f, 12f)] public float waveScrollSpeed = 3.2f;
    [Min(1)] public int waveLineCount = 4;

    [Header("Glow")]
    [ColorUsage(true, true)] public Color cyanGlow = new Color(0f, 3f, 4f, 1f);
    [ColorUsage(true, true)] public Color purpleGlow = new Color(1.2f, 0f, 2.5f, 1f);
    [ColorUsage(true, true)] public Color pinkGlow = new Color(5f, 0.5f, 2.5f, 1f);

    readonly List<LineRenderer> _gridLines = new List<LineRenderer>();
    readonly List<Transform> _skyShapes = new List<Transform>();
    readonly List<Transform> _shapePillars = new List<Transform>();
    readonly List<LineRenderer> _waveLines = new List<LineRenderer>();

    Transform _gridRoot;
    Transform _skyRoot;
    Transform _waveRoot;

    float _nextBeatTime;
    float _pulseTimer;
    bool _inBassDrop;
    float _bassDropEndTime;
    float _time;

    float[] _samples = new float[512];
    float _instantAmplitude;

    void Reset()
    {
        if (gridColorGradient.colorKeys == null || gridColorGradient.colorKeys.Length == 0)
        {
            gridColorGradient = new Gradient();
            gridColorGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0f, 1f, 1f), 0f),
                    new GradientColorKey(new Color(0.9f, 0f, 1f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
        }
    }

    void OnEnable()
    {
        Build();
    }

    void OnValidate()
    {
        gridColumns = Mathf.Max(4, gridColumns);
        floorGridRows = Mathf.Max(4, floorGridRows);
        backGridRows = Mathf.Max(4, backGridRows);
        waveSamples = Mathf.Max(2, waveSamples);
        waveLineCount = Mathf.Max(1, waveLineCount);
        skyShapeCount = Mathf.Max(4, skyShapeCount);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= DeferredBuild;
        UnityEditor.EditorApplication.delayCall += DeferredBuild;
#else
        Build();
#endif
    }

#if UNITY_EDITOR
    void DeferredBuild()
    {
        if (this == null) return;
        Build();
    }
#endif

    void Build()
    {
        EnsureRoots();
        ClearChildren(_gridRoot);
        ClearChildren(_skyRoot);
        ClearChildren(_waveRoot);

        _gridLines.Clear();
        _skyShapes.Clear();
        _shapePillars.Clear();
        _waveLines.Clear();

        BuildGrid();
        BuildSkyShapes();
        BuildWaveLines();

        _nextBeatTime = TimeNow() + BeatInterval();
        _pulseTimer = 0f;
        _inBassDrop = false;
    }

    void Update()
    {
        _time += Application.isPlaying ? Time.deltaTime : 0.016f;
        UpdateAudio();
        UpdateBeatPulse();
        UpdateSkyShapes();
        UpdateBassDropState();
        UpdateWaveLines();
        ApplyGlow();
    }

    void EnsureRoots()
    {
        _gridRoot = GetOrCreateChild("GridRoot");
        _skyRoot = GetOrCreateChild("Sky2DRoot");
        _waveRoot = GetOrCreateChild("WaveRoot");
    }

    Transform GetOrCreateChild(string name)
    {
        Transform t = transform.Find(name);
        if (t != null) return t;

        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform c = root.GetChild(i);
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }
    }

    LineRenderer CreateLine(string name, Transform parent, float width, Color a, Color b, bool useWorldSpace = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = useWorldSpace;
        lr.loop = false;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.widthMultiplier = width;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;

        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", a);
        lr.sharedMaterial = mat;
        lr.startColor = a;
        lr.endColor = b;

        return lr;
    }

    void BuildGrid()
    {
        float halfWidth = (gridColumns * gridSquareSize) * 0.5f;

        // Floor grid on XZ plane (normal +Y): perfect squares.
        for (int i = 0; i <= gridColumns; i++)
        {
            float x = -halfWidth + i * gridSquareSize;
            float t = i / Mathf.Max(1f, gridColumns);
            Color hdr = Color.Lerp(cyanGlow, purpleGlow, t);
            LineRenderer lr = CreateLine($"Floor_V_{i}", _gridRoot, 0.04f, hdr, hdr);
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(x, 0f, floorNearZ));
            lr.SetPosition(1, new Vector3(x, 0f, floorNearZ + floorGridRows * gridSquareSize));
            _gridLines.Add(lr);
        }

        for (int j = 0; j <= floorGridRows; j++)
        {
            float z = floorNearZ + j * gridSquareSize;
            float t = j / Mathf.Max(1f, floorGridRows);
            Color hdr = Color.Lerp(cyanGlow, purpleGlow, t * 0.9f);
            LineRenderer lr = CreateLine($"Floor_H_{j}", _gridRoot, 0.04f, hdr, hdr);
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(-halfWidth, 0f, z));
            lr.SetPosition(1, new Vector3(halfWidth, 0f, z));
            _gridLines.Add(lr);
        }

        // Back wall grid on XY plane at fixed Z (normal +Z): perfect squares.
        float wallHeight = backGridRows * gridSquareSize;
        for (int i = 0; i <= gridColumns; i++)
        {
            float x = -halfWidth + i * gridSquareSize;
            float t = i / Mathf.Max(1f, gridColumns);
            Color hdr = Color.Lerp(cyanGlow, purpleGlow, t);
            LineRenderer lr = CreateLine($"Wall_V_{i}", _gridRoot, 0.04f, hdr, hdr);
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(x, 0f, backWallZ));
            lr.SetPosition(1, new Vector3(x, wallHeight, backWallZ));
            _gridLines.Add(lr);
        }

        for (int j = 0; j <= backGridRows; j++)
        {
            float y = j * gridSquareSize;
            float t = j / Mathf.Max(1f, backGridRows);
            Color hdr = Color.Lerp(cyanGlow, purpleGlow, t * 0.9f);
            LineRenderer lr = CreateLine($"Wall_H_{j}", _gridRoot, 0.04f, hdr, hdr);
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(-halfWidth, y, backWallZ));
            lr.SetPosition(1, new Vector3(halfWidth, y, backWallZ));
            _gridLines.Add(lr);
        }
    }

    void BuildSkyShapes()
    {
        for (int i = 0; i < skyShapeCount; i++)
        {
            GameObject go = new GameObject($"SkyShape_{i}");
            go.transform.SetParent(_skyRoot, false);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            bool oct = i % 3 == 0;
            bool tri = i % 3 == 1;
            mf.sharedMesh = oct ? BuildPolygonMesh(8) : tri ? BuildPolygonMesh(3) : BuildCircuitQuadMesh();

            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", Color.Lerp(cyanGlow, purpleGlow, Random.value));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Vector3 p = new Vector3(
                Random.Range(skyXRange.x, skyXRange.y),
                Random.Range(skyYRange.x, skyYRange.y),
                Random.Range(40f, 70f)
            );
            float s = Random.Range(skyScaleRange.x, skyScaleRange.y);
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * s;

            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = go.name + "_Pillar";
            pillar.transform.SetParent(go.transform, false);
            pillar.transform.localPosition = Vector3.zero;
            pillar.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f);

            var col = pillar.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            MeshRenderer pillarRenderer = pillar.GetComponent<MeshRenderer>();
            Material pillarMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            pillarMat.SetColor("_BaseColor", new Color(0f, 3f, 4f, 1f));
            pillarRenderer.sharedMaterial = pillarMat;
            pillarRenderer.enabled = false;
            pillarRenderer.shadowCastingMode = ShadowCastingMode.Off;
            pillarRenderer.receiveShadows = false;

            _skyShapes.Add(go.transform);
            _shapePillars.Add(pillar.transform);
        }
    }

    Mesh BuildPolygonMesh(int sides)
    {
        Mesh m = new Mesh();
        List<Vector3> v = new List<Vector3>();
        List<int> t = new List<int>();

        v.Add(Vector3.zero);
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        }

        for (int i = 1; i <= sides; i++)
        {
            int next = i == sides ? 1 : i + 1;
            t.Add(0);
            t.Add(i);
            t.Add(next);
        }

        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        return m;
    }

    Mesh BuildCircuitQuadMesh()
    {
        Mesh m = new Mesh();
        Vector3[] v =
        {
            new Vector3(-1f, -0.25f, 0f),
            new Vector3( 1f, -0.25f, 0f),
            new Vector3( 1f,  0.25f, 0f),
            new Vector3(-1f,  0.25f, 0f),
            new Vector3(-0.2f, -0.9f, 0f),
            new Vector3( 0.2f, -0.9f, 0f),
            new Vector3( 0.2f, -0.25f, 0f),
            new Vector3(-0.2f, -0.25f, 0f)
        };

        int[] tri =
        {
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7
        };

        m.vertices = v;
        m.triangles = tri;
        m.RecalculateNormals();
        return m;
    }

    void BuildWaveLines()
    {
        for (int i = 0; i < waveLineCount; i++)
        {
            LineRenderer lr = CreateLine($"Wave_{i}", _waveRoot, 0.05f, pinkGlow, pinkGlow);
            lr.positionCount = waveSamples;
            _waveLines.Add(lr);
        }
    }

    void UpdateAudio()
    {
        _instantAmplitude = 0f;
        if (audioSource == null) return;

        audioSource.GetOutputData(_samples, 0);
        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++)
        {
            sum += Mathf.Abs(_samples[i]);
        }
        _instantAmplitude = sum / _samples.Length;
    }

    void UpdateBeatPulse()
    {
        float now = TimeNow();
        if (now >= _nextBeatTime)
        {
            _nextBeatTime += BeatInterval();
            _pulseTimer = pulseDuration;

            if (_instantAmplitude > bassDropThreshold)
            {
                _inBassDrop = true;
                _bassDropEndTime = now + BeatInterval() * bassDropBeats;
            }
        }

        if (_pulseTimer > 0f)
        {
            _pulseTimer -= Application.isPlaying ? Time.deltaTime : 0.016f;
        }
    }

    void UpdateBassDropState()
    {
        if (_inBassDrop && TimeNow() >= _bassDropEndTime)
        {
            _inBassDrop = false;
        }

        for (int i = 0; i < _shapePillars.Count; i++)
        {
            if (_shapePillars[i] == null || _skyShapes[i] == null) continue;

            MeshRenderer shapeRenderer = _skyShapes[i].GetComponent<MeshRenderer>();
            MeshRenderer pillarRenderer = _shapePillars[i].GetComponent<MeshRenderer>();

            if (_inBassDrop)
            {
                if (shapeRenderer != null) shapeRenderer.enabled = false;
                if (pillarRenderer != null) pillarRenderer.enabled = true;

                Vector3 s = _shapePillars[i].localScale;
                s.y = Mathf.Lerp(s.y, extrusionHeight, 0.2f);
                _shapePillars[i].localScale = s;
            }
            else
            {
                if (shapeRenderer != null) shapeRenderer.enabled = true;
                if (pillarRenderer != null) pillarRenderer.enabled = false;

                Vector3 s = _shapePillars[i].localScale;
                s.y = Mathf.Lerp(s.y, 0.01f, 0.3f);
                _shapePillars[i].localScale = s;
            }
        }
    }

    void UpdateSkyShapes()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        for (int i = 0; i < _skyShapes.Count; i++)
        {
            if (_skyShapes[i] == null) continue;

            Vector3 p = _skyShapes[i].localPosition;
            p.x += Mathf.Sin(_time * 0.7f + i * 0.91f) * driftSpeed * dt;
            p.y += Mathf.Cos(_time * 0.4f + i * 1.31f) * driftSpeed * 0.35f * dt;

            if (p.x > skyXRange.y) p.x = skyXRange.x;
            if (p.x < skyXRange.x) p.x = skyXRange.y;
            if (p.y > skyYRange.y) p.y = skyYRange.x;
            if (p.y < skyYRange.x) p.y = skyYRange.y;

            _skyShapes[i].localPosition = p;

            if (targetCamera != null)
            {
                Vector3 forward = targetCamera.transform.forward;
                _skyShapes[i].forward = -forward;
            }
        }
    }

    void UpdateWaveLines()
    {
        float amp = Mathf.Clamp01(_instantAmplitude * 14f);
        float baseHeight = waveHeight * Mathf.Lerp(0.15f, 1f, amp);

        for (int line = 0; line < _waveLines.Count; line++)
        {
            LineRenderer lr = _waveLines[line];
            if (lr == null) continue;

            float zOffset = Mathf.Lerp(
                floorNearZ + 2f * gridSquareSize,
                floorNearZ + floorGridRows * gridSquareSize * 0.85f,
                line / Mathf.Max(1f, _waveLines.Count - 1f)
            );
            float lineWidth = gridColumns * gridSquareSize * Mathf.Lerp(
                0.9f,
                0.35f,
                line / Mathf.Max(1f, _waveLines.Count - 1f)
            );

            for (int i = 0; i < waveSamples; i++)
            {
                float t = i / (float)(waveSamples - 1);
                float x = Mathf.Lerp(-lineWidth * 0.5f, lineWidth * 0.5f, t);
                float phase = _time * waveScrollSpeed + t * 22f + line * 1.7f;
                float y = Mathf.Sin(phase) * baseHeight;
                float z = zOffset + Mathf.Sin(t * 5f + _time * 0.4f + line) * 0.5f;

                lr.SetPosition(i, new Vector3(x, 0.06f + y, z));
            }
        }
    }

    void ApplyGlow()
    {
        float pulse = _pulseTimer > 0f ? quarterBeatBrightnessMultiplier : 1f;

        for (int i = 0; i < _gridLines.Count; i++)
        {
            if (_gridLines[i] == null) continue;
            Color c = i % 2 == 0 ? cyanGlow : purpleGlow;
            c *= pulse;
            _gridLines[i].startColor = c;
            _gridLines[i].endColor = c;
            if (_gridLines[i].sharedMaterial != null)
                _gridLines[i].sharedMaterial.SetColor("_BaseColor", c);
        }

        for (int i = 0; i < _skyShapes.Count; i++)
        {
            if (_skyShapes[i] == null) continue;
            MeshRenderer r = _skyShapes[i].GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial != null)
            {
                Color baseColor = Color.Lerp(cyanGlow, purpleGlow, (i * 0.37f) % 1f);
                r.sharedMaterial.SetColor("_BaseColor", baseColor * pulse);
            }

            MeshRenderer pr = _shapePillars[i].GetComponent<MeshRenderer>();
            if (pr != null && pr.sharedMaterial != null)
            {
                pr.sharedMaterial.SetColor("_BaseColor", cyanGlow * pulse * 1.5f);
            }
        }

        for (int i = 0; i < _waveLines.Count; i++)
        {
            if (_waveLines[i] == null) continue;
            Color c = pinkGlow * pulse;
            _waveLines[i].startColor = c;
            _waveLines[i].endColor = c;
            if (_waveLines[i].sharedMaterial != null)
                _waveLines[i].sharedMaterial.SetColor("_BaseColor", c);
        }

        if (globalVolume != null && globalVolume.sharedProfile != null && globalVolume.sharedProfile.TryGet(out Bloom bloom))
        {
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.threshold.overrideState = true;
            bloom.intensity.value = _pulseTimer > 0f ? 2.8f : 1.6f;
            bloom.threshold.value = _pulseTimer > 0f ? 0.6f : 0.95f;
        }
    }

    float BeatInterval()
    {
        return 60f / Mathf.Max(1f, bpm);
    }

    float TimeNow()
    {
        if (useAudioClock && audioSource != null)
            return audioSource.time;
        return Application.isPlaying ? Time.time : _time;
    }
}
