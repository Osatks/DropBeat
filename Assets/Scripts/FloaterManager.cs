using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class FloaterManager : MonoBehaviour
{
    [Header("References")]
    public GridPulseOnlyController gridController;

    [Header("XZ Floating Objects")]
    public GameObject floaterPrefab;
    public GameObject[] floaterPrefabs;
    public bool enableXZFloaters = true;
    [Range(0, 128)] public int floaterCount = 28;
    [Range(0.02f, 0.5f)] public float floaterMinSize = 0.07f;
    [Range(0.05f, 1.2f)] public float floaterMaxSize = 0.22f;
    [Range(0.1f, 8f)] public float floaterAverageSpeed = 1.6f;
    [Range(0f, 1f)] public float floaterSpeedVariance = 0.35f;
    [Range(0.05f, 1f)] public float floaterMinSpeedFactor = 0.45f;
    [Range(0.1f, 8f)] public float floaterMinAbsoluteSpeed = 0.85f;
    [Range(2f, 80f)] public float floaterNearDepth = 8f;
    [Range(4f, 120f)] public float floaterFarDepth = 30f;
    [Range(0.2f, 4f)] public float floaterGlowMultiplier = 1.4f;
    [Range(0.05f, 1.2f)] public float floaterFadeInDuration = 0.28f;
    [Range(0f, 90f)] public float floaterSpinSpeed = 16f;
    [Range(0.05f, 1f)] public float spawnMinViewportY = 0.35f;
    [Range(0.1f, 1f)] public float spawnMaxViewportY = 0.95f;
    [Range(0.05f, 1.2f)] public float planeFadeDuration = 0.35f;

    Transform _floaterRoot;
    Material _sharedFloaterMaterial;
    Mesh _triangleMesh;
    MaterialPropertyBlock _mpb;

    readonly List<Transform> _floaters = new List<Transform>();
    readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
    readonly List<Vector3> _velDir = new List<Vector3>();
    readonly List<float> _speedMul = new List<float>();
    readonly List<Color> _baseColor = new List<Color>();
    readonly List<float> _depth = new List<float>();
    readonly List<float> _fade = new List<float>();
    readonly List<Vector3> _baseScale = new List<Vector3>();
    float _visibility = 0f;

    void OnEnable()
    {
        ResolveController();
        _visibility = 0f;
    }

    void OnDisable()
    {
        if (_sharedFloaterMaterial != null)
        {
            if (Application.isPlaying) Destroy(_sharedFloaterMaterial);
            else DestroyImmediate(_sharedFloaterMaterial);
            _sharedFloaterMaterial = null;
        }
        DestroySpawnedFloaters();
    }

    void Update()
    {
        ResolveController();
        bool shouldShow = enableXZFloaters && gridController != null && gridController.IsXZPlaneActive;
        if (shouldShow)
        {
            EnsureRoot();
            EnsureMaterial();
            EnsurePool();
        }

        UpdateFloaters(shouldShow);
    }

    void ResolveController()
    {
        if (gridController == null)
        {
            gridController = FindAnyObjectByType<GridPulseOnlyController>();
        }
    }

    void EnsureRoot()
    {
        if (_floaterRoot != null) return;
        _floaterRoot = transform.Find("Floaters");
        if (_floaterRoot == null)
        {
            var go = new GameObject("Floaters");
            go.transform.SetParent(transform, false);
            _floaterRoot = go.transform;
        }
    }

    void EnsureMaterial()
    {
        if (_sharedFloaterMaterial != null) return;

        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        if (s == null) return;

        _sharedFloaterMaterial = new Material(s) { name = "FloaterManager_Mat" };
        _sharedFloaterMaterial.SetColor("_BaseColor", Color.white);
        if (_sharedFloaterMaterial.HasProperty("_Surface")) _sharedFloaterMaterial.SetFloat("_Surface", 1f);
        if (_sharedFloaterMaterial.HasProperty("_Cull")) _sharedFloaterMaterial.SetFloat("_Cull", 0f);
        if (_sharedFloaterMaterial.HasProperty("_ZWrite")) _sharedFloaterMaterial.SetFloat("_ZWrite", 0f);
        _sharedFloaterMaterial.renderQueue = 3000;
    }

    void EnsurePool()
    {
        if (_floaterRoot == null) return;

        while (_floaters.Count < floaterCount)
        {
            GameObject sourcePrefab = PickFloaterPrefab();
            GameObject g = sourcePrefab != null ? Instantiate(sourcePrefab) : GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "Floater";
            g.transform.SetParent(_floaterRoot, false);

            var col = g.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var mr = g.GetComponent<MeshRenderer>();
            if (mr != null && _sharedFloaterMaterial != null) mr.sharedMaterial = _sharedFloaterMaterial;

            if (sourcePrefab == null)
            {
                int shape = Random.Range(1, 3); // 1=bar, 2=triangle (no confetti dot)
                ApplyShape(g, shape);
            }

            _floaters.Add(g.transform);
            _renderers.Add(mr);
            _velDir.Add(RandomDirection());
            _speedMul.Add(RandomSpeedMultiplier());
            _baseColor.Add(RandomColor());
            _depth.Add(0f);
            _fade.Add(0f);
            _baseScale.Add(g.transform.localScale);
            Respawn(_floaters.Count - 1, true);
        }

        while (_floaters.Count > floaterCount)
        {
            int i = _floaters.Count - 1;
            if (_floaters[i] != null)
            {
                if (Application.isPlaying) Destroy(_floaters[i].gameObject);
                else DestroyImmediate(_floaters[i].gameObject);
            }
            _floaters.RemoveAt(i);
            _renderers.RemoveAt(i);
            _velDir.RemoveAt(i);
            _speedMul.RemoveAt(i);
            _baseColor.RemoveAt(i);
            _depth.RemoveAt(i);
            _fade.RemoveAt(i);
            _baseScale.RemoveAt(i);
        }
    }

    void UpdateFloaters(bool shouldShow)
    {
        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        _visibility = Mathf.MoveTowards(_visibility, shouldShow ? 1f : 0f, dt / Mathf.Max(0.01f, planeFadeDuration));

        if (!shouldShow && _visibility <= 0.001f)
        {
            DestroySpawnedFloaters();
            return;
        }

        if (_floaterRoot == null) return;
        if (gridController == null) return;

        if (!_floaterRoot.gameObject.activeSelf) _floaterRoot.gameObject.SetActive(true);

        Camera cam = gridController.ActiveCamera;
        if (cam == null) return;

        float emission = gridController.GridEmissionStrength;

        for (int i = 0; i < _floaters.Count; i++)
        {
            Transform f = _floaters[i];
            if (f == null) continue;

            Vector3 world = transform.TransformPoint(f.localPosition);
            float depthT = Mathf.InverseLerp(floaterNearDepth, Mathf.Max(floaterNearDepth + 0.01f, floaterFarDepth), _depth[i]);
            float parallax = Mathf.Lerp(1.25f, 0.65f, depthT);
            float speed = Mathf.Max(floaterMinAbsoluteSpeed, floaterAverageSpeed * _speedMul[i] * parallax);

            _fade[i] = Mathf.MoveTowards(_fade[i], 1f, dt / Mathf.Max(0.01f, floaterFadeInDuration));
            float fade = Mathf.SmoothStep(0f, 1f, _fade[i]);
            float motionRamp = Mathf.Lerp(0.65f, 1f, fade);

            world += (cam.transform.right * _velDir[i].x + cam.transform.up * _velDir[i].y) * (speed * motionRamp * dt);
            f.localPosition = transform.InverseTransformPoint(world);
            f.localRotation *= Quaternion.Euler(0f, 0f, floaterSpinSpeed * dt * motionRamp);
            float groupFade = Mathf.SmoothStep(0f, 1f, _visibility);
            f.localScale = _baseScale[i] * Mathf.Lerp(0.2f, 1f, fade) * Mathf.Lerp(0.6f, 1f, groupFade);

            Vector3 vp = cam.WorldToViewportPoint(world);
            bool outOfView = vp.z < 0.1f || vp.x < -0.1f || vp.x > 1.1f || vp.y > 1.1f;
            if (outOfView)
            {
                Respawn(i, false);
            }

            MeshRenderer mr = _renderers[i];
            if (mr != null)
            {
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                Color c = _baseColor[i] * (emission * floaterGlowMultiplier);
                c.a = fade * groupFade;
                _mpb.Clear();
                _mpb.SetColor("_BaseColor", c);
                mr.SetPropertyBlock(_mpb);
            }
        }
    }

    void Respawn(int index, bool upperBand)
    {
        if (index < 0 || index >= _floaters.Count) return;
        Transform t = _floaters[index];
        if (t == null) return;
        if (gridController == null) return;

        Camera cam = gridController.ActiveCamera;
        if (cam == null) return;

        float yMin = Mathf.Clamp01(Mathf.Min(spawnMinViewportY, spawnMaxViewportY));
        float yMax = Mathf.Clamp01(Mathf.Max(spawnMinViewportY + 0.01f, spawnMaxViewportY));
        float y = upperBand ? Random.Range(yMin, yMax) : Random.Range(Mathf.Max(0.05f, yMin - 0.25f), yMin);
        float x = Random.Range(0.05f, 0.95f);
        float near = Mathf.Min(floaterNearDepth, floaterFarDepth);
        float far = Mathf.Max(floaterNearDepth + 0.01f, floaterFarDepth);
        float depth = Random.Range(Mathf.Max(3f, near), Mathf.Max(near + 0.01f, far));

        _velDir[index] = RandomDirection();
        _speedMul[index] = RandomSpeedMultiplier();
        _depth[index] = depth;
        _fade[index] = 0f;

        Vector3 world = cam.ViewportToWorldPoint(new Vector3(x, y, depth));
        t.localPosition = transform.InverseTransformPoint(world);
        t.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    }

    Vector3 RandomDirection()
    {
        Vector2 d = new Vector2(Random.Range(-0.7f, 0.7f), Random.Range(0.45f, 1f)).normalized;
        return new Vector3(d.x, d.y, 0f);
    }

    float RandomSpeedMultiplier()
    {
        float min = Mathf.Max(0.01f, 1f - floaterSpeedVariance);
        float max = 1f + floaterSpeedVariance;
        float s = Random.Range(min, max);
        return Mathf.Max(floaterMinSpeedFactor, s);
    }

    Color RandomColor()
    {
        return Random.value < 0.5f
            ? new Color(0.3f, 1f, 1f, 1f)
            : new Color(1f, 0.35f, 0.95f, 1f);
    }

    void ApplyShape(GameObject g, int shape)
    {
        if (g == null) return;
        float size = Random.Range(floaterMinSize, floaterMaxSize);
        var mf = g.GetComponent<MeshFilter>();
        if (mf == null) return;

        if (shape == 1)
        {
            mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            g.transform.localScale = new Vector3(size * 2.4f, size * 0.75f, size);
        }
        else
        {
            if (_triangleMesh == null) _triangleMesh = CreateTriangleMesh();
            mf.sharedMesh = _triangleMesh;
            g.transform.localScale = new Vector3(size * 1.7f, size * 1.7f, size);
        }
    }

    Mesh CreateTriangleMesh()
    {
        var m = new Mesh { name = "FloaterTriangle" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.4f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 0.4f, 0f)
        };
        m.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0.5f),
            new Vector2(0f, 1f)
        };
        m.triangles = new[] { 0, 1, 2 };
        m.RecalculateNormals();
        return m;
    }

    GameObject PickFloaterPrefab()
    {
        if (floaterPrefabs != null && floaterPrefabs.Length > 0)
        {
            int count = floaterPrefabs.Length;
            int start = Random.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                var p = floaterPrefabs[(start + i) % count];
                if (p != null) return p;
            }
        }

        return floaterPrefab;
    }

    void DestroySpawnedFloaters()
    {
        if (_floaterRoot != null)
        {
            if (Application.isPlaying) Destroy(_floaterRoot.gameObject);
            else DestroyImmediate(_floaterRoot.gameObject);
            _floaterRoot = null;
        }

        _floaters.Clear();
        _renderers.Clear();
        _velDir.Clear();
        _speedMul.Clear();
        _baseColor.Clear();
        _depth.Clear();
        _fade.Clear();
        _baseScale.Clear();
    }
}
