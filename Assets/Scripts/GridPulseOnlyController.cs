using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class GridPulseOnlyController : MonoBehaviour
{
    enum GridPlaneMode
    {
        XY,
        XZ
    }

    enum RotationSpeedBehavior
    {
        Linear,
        Exponential,
        SmoothStep,
        EaseInQuad,
        EaseOutQuad,
        EaseInOutCubic
    }

    [Header("References")]
    public Camera targetCamera;
    public Volume globalVolume;

    [Header("Single Plane Grid (XY at fixed Z)")]
    [Min(4)] public int gridColumns = 28;
    [Min(4)] public int gridRows = 18;
    [Min(0.25f)] public float gridSquareSize = 1.0f;
    public float planeZ = 42f;
    public float planeY = 0f;

    [Header("Plane Transition")]
    [SerializeField] bool enablePlaneToggleInput = true;
    [SerializeField] GridPlaneMode startPlane = GridPlaneMode.XY;
    [Range(0.05f, 2f)] public float planeTransitionDuration = 0.35f;
    public KeyCode togglePlaneKey = KeyCode.Space;
    [SerializeField] RotationSpeedBehavior rotationSpeedBehavior = RotationSpeedBehavior.Exponential;
    [Range(1.2f, 8f)] public float exponentialSharpness = 3f;

    [Header("Neon Colors")]
    [ColorUsage(true, true)] public Color cyanGlow = new Color(0f, 3f, 4f, 1f);
    [ColorUsage(true, true)] public Color purpleGlow = new Color(1.2f, 0f, 2.5f, 1f);

    [Header("Mouse Ripple")]
    public bool useCircularWaveEffect = true;
    public bool useRowColumnLineEffect = false;
    [Range(2f, 200f)] public float waveSpeed = 24f;
    [Range(0.05f, 8f)] public float waveBandWidth = 0.65f;
    [Range(0.5f, 6f)] public float waveIntensity = 2.4f;
    [Range(0.2f, 20f)] public float waveLifetime = 4.5f;
    [Range(0.2f, 8f)] public float ringFrequency = 2.2f;

    [Header("Bloom")]
    [Range(0f, 4f)] public float bloomIdleIntensity = 1.0f;
    [Range(0f, 4f)] public float bloomWaveIntensity = 2.8f;
    [Range(0f, 2f)] public float bloomIdleThreshold = 1.0f;
    [Range(0f, 2f)] public float bloomWaveThreshold = 0.62f;

    [Header("Procedural Motion")]
    public bool enableProceduralMotion = true;
    [Range(0f, 10f)] public float xyUpwardSpeed = 1.2f;
    [Range(0f, 2f)] public float xzInwardAmount = 0.28f;
    [Range(0f, 10f)] public float xzInwardSpeed = 1.4f;

    [Header("Performance")]
    [Range(0.5f, 4f)] public float rippleCullBandMultiplier = 2.5f;
    [Min(1)] public int maxActiveWaves = 8;
    [Range(0.1f, 12f)] public float meshEmissionStrength = 4f;

    class WavePulse
    {
        public Vector3 originLocal;
        public float age;
    }

    struct Segment
    {
        public Vector3 start;
        public Vector3 end;
        public Vector3 center;
        public Color baseColor;
    }

    readonly List<WavePulse> _waves = new List<WavePulse>();

    Transform _gridRoot;
    Transform _meshHost;
    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;

    Mesh _mesh;
    Material _sharedLineMaterial;

    Segment[] _segments = System.Array.Empty<Segment>();
    Vector3[] _vertices = System.Array.Empty<Vector3>();
    Color[] _colors = System.Array.Empty<Color>();
    int[] _triangles = System.Array.Empty<int>();
    int _lastColumns = -1;
    int _lastRows = -1;
    float _lastSquareSize = -1f;
    float _lastPlaneZ = float.MinValue;
    float _lastPlaneY = float.MinValue;

    GridPlaneMode _currentPlane;
    GridPlaneMode _targetPlane;
    float _planeLerpT = 1f;
    Quaternion _rotXY;
    Quaternion _rotXZ;
    Vector3 _transitionPivotPoint;
    Quaternion _transitionFromRot;
    Quaternion _transitionToRot;
    float _halfHeight;

    const float BaseLineWidth = 0.04f;
    const float RippleWidthBoost = 0.03f;

    void OnEnable()
    {
        if (ShouldSkipEditorBuildForPrefabAsset())
            return;

        bool hadGridRootBeforeBuild = transform.Find("GridRoot") != null;
        _currentPlane = startPlane;
        _targetPlane = startPlane;
        Build();

        // Do not force-reset transform on play start if the grid already exists in scene.
        if (!hadGridRootBeforeBuild)
        {
            ApplyPlaneImmediate(_currentPlane);
        }
    }

    void OnDisable()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
            _mesh = null;
        }

        if (_sharedLineMaterial != null)
        {
            if (Application.isPlaying) Destroy(_sharedLineMaterial);
            else DestroyImmediate(_sharedLineMaterial);
            _sharedLineMaterial = null;
        }

    }

    void OnValidate()
    {
        gridColumns = Mathf.Max(4, gridColumns);
        gridRows = Mathf.Max(4, gridRows);

#if UNITY_EDITOR
        EditorApplication.delayCall -= DeferredBuild;
        EditorApplication.delayCall += DeferredBuild;
#else
        Build();
#endif
    }

#if UNITY_EDITOR
    void DeferredBuild()
    {
        if (this == null) return;
        if (ShouldSkipEditorBuildForPrefabAsset()) return;
        Build();
    }
#endif

    void Update()
    {
        if (NeedsRebuild())
        {
            Build();
        }

        HandlePlaneToggleInput();
        UpdatePlaneTransition(Time.deltaTime);

        if (Application.isPlaying)
        {
            HandleMouseClick();
            UpdateWaves(Time.deltaTime);
        }
        else
        {
            UpdateWaves(0.016f);
        }

        ApplyGlowAndMesh();
    }

    [ContextMenu("Build Grid")]
    public void Build()
    {
        if (ShouldSkipEditorBuildForPrefabAsset())
            return;

        Vector3 prevLocalPos = Vector3.zero;
        Quaternion prevLocalRot = Quaternion.identity;
        bool hadGridRoot = _gridRoot != null;
        if (hadGridRoot)
        {
            prevLocalPos = _gridRoot.localPosition;
            prevLocalRot = _gridRoot.localRotation;
        }

        EnsureRoot();
        EnsureRenderObjects();
        EnsureSharedLineMaterial();

        _waves.Clear();

        float halfWidth = (gridColumns * gridSquareSize) * 0.5f;
        float halfHeight = (gridRows * gridSquareSize) * 0.5f;
        _halfHeight = halfHeight;

        var segs = new List<Segment>((gridColumns + 1) * gridRows + (gridRows + 1) * gridColumns);

        for (int i = 0; i <= gridColumns; i++)
        {
            float x = -halfWidth + i * gridSquareSize;
            float t = i / Mathf.Max(1f, gridColumns);
            Color c = Color.Lerp(cyanGlow, purpleGlow, t);

            for (int j = 0; j < gridRows; j++)
            {
                float y0 = -halfHeight + j * gridSquareSize;
                float y1 = y0 + gridSquareSize;
                Vector3 a = new Vector3(x, y0, planeZ);
                Vector3 b = new Vector3(x, y1, planeZ);
                segs.Add(new Segment { start = a, end = b, center = (a + b) * 0.5f, baseColor = c });
            }
        }

        for (int j = 0; j <= gridRows; j++)
        {
            float y = -halfHeight + j * gridSquareSize;
            float t = j / Mathf.Max(1f, gridRows);
            Color c = Color.Lerp(cyanGlow, purpleGlow, t * 0.9f);

            for (int i = 0; i < gridColumns; i++)
            {
                float x0 = -halfWidth + i * gridSquareSize;
                float x1 = x0 + gridSquareSize;
                Vector3 a = new Vector3(x0, y, planeZ);
                Vector3 b = new Vector3(x1, y, planeZ);
                segs.Add(new Segment { start = a, end = b, center = (a + b) * 0.5f, baseColor = c });
            }
        }

        _segments = segs.ToArray();
        CachePlaneRotations();
        BuildMeshTopology();
        UpdateMeshVisuals();

        // Preserve user/authored placement during rebuilds (especially in XZ mode).
        if (hadGridRoot)
        {
            _gridRoot.localPosition = prevLocalPos;
            _gridRoot.localRotation = prevLocalRot;
        }
        else
        {
            // First-time build only: place according to start plane.
            ApplyPlaneImmediate(_currentPlane);
        }

        CacheBuildInputs();
    }

    bool ShouldSkipEditorBuildForPrefabAsset()
    {
#if UNITY_EDITOR
        return PrefabUtility.IsPartOfPrefabAsset(gameObject);
#else
        return false;
#endif
    }

    void EnsureRoot()
    {
        _gridRoot = transform.Find("GridRoot");
        if (_gridRoot == null)
        {
            var root = new GameObject("GridRoot");
            root.transform.SetParent(transform, false);
            _gridRoot = root.transform;
        }
    }

    void EnsureRenderObjects()
    {
        // Remove legacy children from old LineRenderer-based builds.
        for (int i = _gridRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = _gridRoot.GetChild(i);
            if (child.name == "GridMesh") continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        _meshHost = _gridRoot.Find("GridMesh");
        if (_meshHost == null)
        {
            var host = new GameObject("GridMesh");
            host.transform.SetParent(_gridRoot, false);
            _meshHost = host.transform;
        }

        _meshFilter = _meshHost.GetComponent<MeshFilter>();
        if (_meshFilter == null) _meshFilter = _meshHost.gameObject.AddComponent<MeshFilter>();

        _meshRenderer = _meshHost.GetComponent<MeshRenderer>();
        if (_meshRenderer == null) _meshRenderer = _meshHost.gameObject.AddComponent<MeshRenderer>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "GridPulseMesh" };
            _mesh.indexFormat = IndexFormat.UInt32;
            _mesh.MarkDynamic();
        }

        _meshFilter.sharedMesh = _mesh;
    }

    void EnsureSharedLineMaterial()
    {
        Shader unlit = Shader.Find("DropBeat/GridVertexColorUnlit");
        if (unlit == null)
        {
            unlit = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (unlit == null)
        {
            Debug.LogWarning("GridPulseOnlyController: Compatible unlit shader not found.", this);
            return;
        }

        bool needNewMaterial = _sharedLineMaterial == null || _sharedLineMaterial.shader != unlit;
        if (needNewMaterial)
        {
            if (_sharedLineMaterial != null)
            {
                if (Application.isPlaying) Destroy(_sharedLineMaterial);
                else DestroyImmediate(_sharedLineMaterial);
            }

            _sharedLineMaterial = new Material(unlit)
            {
                name = "GridPulse_SharedLineMat"
            };
        }

        _sharedLineMaterial.SetColor("_BaseColor", Color.white);
        _sharedLineMaterial.SetFloat("_EmissionStrength", meshEmissionStrength);
        _sharedLineMaterial.SetFloat("_Alpha", 1f);

        if (_meshRenderer != null)
        {
            _meshRenderer.sharedMaterial = _sharedLineMaterial;
        }
    }

    bool NeedsRebuild()
    {
        return _segments.Length == 0 ||
               _lastColumns != gridColumns ||
               _lastRows != gridRows ||
               !Mathf.Approximately(_lastSquareSize, gridSquareSize) ||
               !Mathf.Approximately(_lastPlaneZ, planeZ) ||
               !Mathf.Approximately(_lastPlaneY, planeY);
    }

    void CacheBuildInputs()
    {
        _lastColumns = gridColumns;
        _lastRows = gridRows;
        _lastSquareSize = gridSquareSize;
        _lastPlaneZ = planeZ;
        _lastPlaneY = planeY;
    }

    void BuildMeshTopology()
    {
        int segCount = _segments.Length;
        _vertices = new Vector3[segCount * 4];
        _colors = new Color[segCount * 4];
        _triangles = new int[segCount * 6];

        int v = 0;
        int t = 0;
        for (int i = 0; i < segCount; i++)
        {
            _triangles[t++] = v + 0;
            _triangles[t++] = v + 2;
            _triangles[t++] = v + 1;
            _triangles[t++] = v + 1;
            _triangles[t++] = v + 2;
            _triangles[t++] = v + 3;
            v += 4;
        }

        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.triangles = _triangles;
        _mesh.RecalculateBounds();
    }

    void HandleMouseClick()
    {
        if (!MousePressedThisFrame()) return;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || _gridRoot == null) return;

        Vector2 screenPos = GetMouseScreenPosition();
        Ray ray = cam.ScreenPointToRay(screenPos);

        Plane plane = GetActiveWorldPlane();
        if (!plane.Raycast(ray, out float enter)) return;

        Vector3 worldPoint = ray.GetPoint(enter);
        Vector3 localPoint = _gridRoot.InverseTransformPoint(worldPoint);
        AddWaveLocal(localPoint);
    }

    public void TriggerPulseAtWorldPoint(Vector3 worldPoint)
    {
        if (_gridRoot == null)
        {
            Build();
        }

        if (_gridRoot == null)
        {
            return;
        }

        Vector3 localPoint = _gridRoot.InverseTransformPoint(worldPoint);
        AddWaveLocal(localPoint);
    }

    public void TriggerWaveformAtWorldPoint(Vector3 worldPoint, float spacing)
    {
        if (_gridRoot == null)
        {
            Build();
        }

        if (_gridRoot == null)
        {
            return;
        }

        Vector3 localCenter = _gridRoot.InverseTransformPoint(worldPoint);
        float laneSpacing = Mathf.Max(0.05f, spacing);
        AddWaveLocal(localCenter);
        AddWaveLocal(localCenter + new Vector3(-laneSpacing, 0f, 0f));
        AddWaveLocal(localCenter + new Vector3(laneSpacing, 0f, 0f));
    }

    public bool TryProjectWorldPointToGrid(Vector3 sourceWorldPoint, out Vector3 gridWorldPoint)
    {
        if (_gridRoot == null)
        {
            Build();
        }

        Plane plane = GetActiveWorldPlane();
        Camera cam = ActiveCamera;
        if (cam != null)
        {
            Vector3 screenPoint = cam.WorldToScreenPoint(sourceWorldPoint);
            Ray ray = cam.ScreenPointToRay(screenPoint);
            if (plane.Raycast(ray, out float enter))
            {
                gridWorldPoint = ray.GetPoint(enter);
                return true;
            }
        }

        gridWorldPoint = sourceWorldPoint - (plane.normal * plane.GetDistanceToPoint(sourceWorldPoint));
        return _gridRoot != null;
    }

    void UpdateWaves(float dt)
    {
        for (int i = _waves.Count - 1; i >= 0; i--)
        {
            _waves[i].age += dt;
            if (_waves[i].age > waveLifetime)
                _waves.RemoveAt(i);
        }
    }

    void AddWaveLocal(Vector3 localPoint)
    {
        _waves.Add(new WavePulse { originLocal = localPoint, age = 0f });
        if (_waves.Count > maxActiveWaves)
        {
            _waves.RemoveRange(0, _waves.Count - maxActiveWaves);
        }
    }

    void HandlePlaneToggleInput()
    {
        if (!enablePlaneToggleInput) return;
        if (!Application.isPlaying || _gridRoot == null) return;
        if (!TogglePressedThisFrame()) return;

        _targetPlane = _targetPlane == GridPlaneMode.XY ? GridPlaneMode.XZ : GridPlaneMode.XY;
        _transitionPivotPoint = GetBottomEdgePivotPointLocal();
        _transitionFromRot = _gridRoot.localRotation;
        _transitionToRot = _targetPlane == GridPlaneMode.XY ? _rotXY : _rotXZ;
        _planeLerpT = 0f;
    }

    bool TogglePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        if (togglePlaneKey == KeyCode.Space) return Keyboard.current.spaceKey.wasPressedThisFrame;
        if (togglePlaneKey == KeyCode.Return) return Keyboard.current.enterKey.wasPressedThisFrame;
        return false;
#else
        return Input.GetKeyDown(togglePlaneKey);
#endif
    }

    bool MousePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    Vector2 GetMouseScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
        return Vector2.zero;
#else
        return Input.mousePosition;
#endif
    }

    void UpdatePlaneTransition(float dt)
    {
        if (_gridRoot == null || _planeLerpT >= 1f) return;

        _planeLerpT += dt / Mathf.Max(0.0001f, planeTransitionDuration);
        float t = EvaluateRotationT(Mathf.Clamp01(_planeLerpT));
        Quaternion rot = Quaternion.Slerp(_transitionFromRot, _transitionToRot, t);

        SetTransformAroundBottomPivot(rot, _transitionPivotPoint);

        if (_planeLerpT >= 1f)
        {
            _currentPlane = _targetPlane;
            SetTransformAroundBottomPivot(_transitionToRot, _transitionPivotPoint);
            _planeLerpT = 1f;
        }
    }

    void CachePlaneRotations()
    {
        _rotXY = Quaternion.identity;
        _rotXZ = Quaternion.Euler(90f, 0f, 0f);
    }

    void ApplyPlaneImmediate(GridPlaneMode mode)
    {
        if (_gridRoot == null) return;
        CachePlaneRotations();

        _gridRoot.localRotation = mode == GridPlaneMode.XY ? _rotXY : _rotXZ;
        _gridRoot.localPosition = mode == GridPlaneMode.XY
            ? new Vector3(0f, _halfHeight, 0f)
            : new Vector3(0f, planeY, -planeZ);

        _planeLerpT = 1f;
    }

    Plane GetActiveWorldPlane()
    {
        if (_gridRoot == null)
            return new Plane(Vector3.forward, new Vector3(0f, 0f, transform.position.z + planeZ));

        Vector3 normal = _gridRoot.forward;
        Vector3 point = _gridRoot.TransformPoint(new Vector3(0f, 0f, planeZ));
        return new Plane(normal, point);
    }

    Vector3 GetBottomEdgePivotPointLocal()
    {
        if (_gridRoot == null) return Vector3.zero;
        Vector3 localBottomPivot = new Vector3(0f, -_halfHeight, planeZ);
        return _gridRoot.localPosition + _gridRoot.localRotation * localBottomPivot;
    }

    void SetTransformAroundBottomPivot(Quaternion targetRotation, Vector3 pivotPointLocal)
    {
        if (_gridRoot == null) return;
        Vector3 localBottomPivot = new Vector3(0f, -_halfHeight, planeZ);
        _gridRoot.localRotation = targetRotation;
        _gridRoot.localPosition = pivotPointLocal - (targetRotation * localBottomPivot);
    }

    void ApplyGlowAndMesh()
    {
        if (_segments.Length == 0 || _mesh == null)
            return;

        bool hasWave = _waves.Count > 0;
        UpdateMeshVisuals();

        if (globalVolume != null && globalVolume.sharedProfile != null && globalVolume.sharedProfile.TryGet(out Bloom bloom))
        {
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.threshold.overrideState = true;
            bloom.intensity.value = hasWave ? bloomWaveIntensity : bloomIdleIntensity;
            bloom.threshold.value = hasWave ? bloomWaveThreshold : bloomIdleThreshold;
        }

    }

    void UpdateMeshVisuals()
    {
        Vector3 planeForward = _gridRoot != null ? _gridRoot.InverseTransformDirection(targetCamera != null ? targetCamera.transform.forward : Vector3.forward) : Vector3.forward;
        Vector3 normal = planeForward.sqrMagnitude > 0.0001f ? -planeForward.normalized : Vector3.back;
        float tTime = Application.isPlaying ? Time.time : (float)EditorApplication.timeSinceStartup;

        for (int i = 0; i < _segments.Length; i++)
        {
            Segment seg = _segments[i];
            Vector3 segStart = seg.start;
            Vector3 segEnd = seg.end;
            Vector3 segCenter = seg.center;

            if (enableProceduralMotion)
            {
                if (_currentPlane == GridPlaneMode.XY)
                {
                    // Upward flow on XY plane.
                    float yOffset = Mathf.Repeat(tTime * xyUpwardSpeed, Mathf.Max(0.0001f, gridSquareSize));
                    segStart.y -= yOffset;
                    segEnd.y -= yOffset;
                    segCenter.y -= yOffset;
                }
                else
                {
                    // Inward flow on XZ plane: stable depth-lane flow toward horizon.
                    segStart = ApplyInwardFlowXZ(segStart, tTime);
                    segEnd = ApplyInwardFlowXZ(segEnd, tTime);
                    segCenter = ApplyInwardFlowXZ(segCenter, tTime);
                }
            }

            float ripple = 0f;

            for (int w = 0; w < _waves.Count; w++)
            {
                float radius = _waves[w].age * waveSpeed;
                ripple += EvaluateWaveContribution(_waves[w].originLocal, segCenter, radius);
            }

            float width = BaseLineWidth + Mathf.Clamp01(ripple) * RippleWidthBoost;
            Color c = seg.baseColor * (1f + ripple * waveIntensity);

            Vector3 dir = segEnd - segStart;
            float len = dir.magnitude;
            Vector3 tangent = len > 0.0001f ? (dir / len) : Vector3.up;
            Vector3 right = Vector3.Cross(normal, tangent).normalized;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            Vector3 offset = right * (width * 0.5f);

            int v = i * 4;
            _vertices[v + 0] = segStart - offset;
            _vertices[v + 1] = segStart + offset;
            _vertices[v + 2] = segEnd - offset;
            _vertices[v + 3] = segEnd + offset;

            _colors[v + 0] = c;
            _colors[v + 1] = c;
            _colors[v + 2] = c;
            _colors[v + 3] = c;
        }

        _mesh.vertices = _vertices;
        _mesh.colors = _colors;
        _mesh.RecalculateBounds();

        if (_sharedLineMaterial != null)
        {
            _sharedLineMaterial.SetFloat("_EmissionStrength", meshEmissionStrength);
        }
    }

    float EvaluateWaveContribution(Vector3 origin, Vector3 point, float radius)
    {
        float dPlane = DistanceOnGridPlane(origin, point);
        float dLine = AxisLineDistanceOnGridPlane(origin, point);
        float contribution = 0f;
        int activeModes = 0;

        if (useCircularWaveEffect)
        {
            contribution += EvaluateBand(dPlane, radius);
            activeModes++;
        }

        if (useRowColumnLineEffect)
        {
            contribution += EvaluateBand(dLine, radius);
            activeModes++;
        }

        if (activeModes == 0)
            return EvaluateBand(dPlane, radius);

        return contribution;
    }

    float EvaluateBand(float distance, float radius)
    {
        float dr = distance - radius;
        float cull = waveBandWidth * Mathf.Max(0.5f, rippleCullBandMultiplier);
        if (dr < -cull || dr > cull)
            return 0f;

        float envelope = Mathf.Exp(-(dr * dr) / Mathf.Max(0.0001f, 2f * waveBandWidth * waveBandWidth));
        float oscillation = 0.5f + 0.5f * Mathf.Cos(dr * ringFrequency * Mathf.PI * 2f);
        return envelope * oscillation;
    }

    static float DistanceOnGridPlane(Vector3 a, Vector3 b)
    {
        float dx = b.x - a.x;
        float dy = b.y - a.y;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    static float AxisLineDistanceOnGridPlane(Vector3 a, Vector3 b)
    {
        float dx = Mathf.Abs(b.x - a.x);
        float dy = Mathf.Abs(b.y - a.y);
        return Mathf.Min(dx, dy);
    }

    float EvaluateRotationT(float x)
    {
        x = Mathf.Clamp01(x);
        if (rotationSpeedBehavior == RotationSpeedBehavior.Linear)
        {
            return x;
        }

        if (rotationSpeedBehavior == RotationSpeedBehavior.Exponential)
        {
            // Exponential ease-out: fast start, smooth settle.
            float k = Mathf.Max(1.0001f, exponentialSharpness);
            return (1f - Mathf.Exp(-k * x)) / (1f - Mathf.Exp(-k));
        }

        if (rotationSpeedBehavior == RotationSpeedBehavior.SmoothStep)
        {
            return Mathf.SmoothStep(0f, 1f, x);
        }

        if (rotationSpeedBehavior == RotationSpeedBehavior.EaseInQuad)
        {
            return x * x;
        }

        if (rotationSpeedBehavior == RotationSpeedBehavior.EaseOutQuad)
        {
            return 1f - (1f - x) * (1f - x);
        }

        // EaseInOutCubic
        return x < 0.5f
            ? 4f * x * x * x
            : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
    }

    Vector3 ApplyInwardFlowXZ(Vector3 p, float tTime)
    {
        // Local mesh Y is depth before GridRoot rotation to XZ.
        // Scroll depth lanes toward horizon with wrap, then apply smooth perspective squeeze.
        float span = Mathf.Max(0.001f, _halfHeight * 2f);
        float shift = tTime * xzInwardSpeed * gridSquareSize;

        float yMin = -_halfHeight;
        float yWrapped = yMin + Mathf.Repeat((p.y - yMin) - shift, span);
        p.y = yWrapped;

        float depth01 = Mathf.InverseLerp(-_halfHeight, _halfHeight, p.y);
        float squeeze = Mathf.Lerp(1f, Mathf.Max(0.05f, 1f - xzInwardAmount), depth01);
        p.x *= squeeze;
        return p;
    }

    public Camera ActiveCamera => targetCamera != null ? targetCamera : Camera.main;

    public bool IsXZPlaneActive => _currentPlane == GridPlaneMode.XZ;

    public float GridEmissionStrength => meshEmissionStrength;

    public void SetPlaneToggleInputEnabled(bool enabled)
    {
        enablePlaneToggleInput = enabled;
    }
}
