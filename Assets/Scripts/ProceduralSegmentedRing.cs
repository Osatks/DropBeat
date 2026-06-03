using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralSegmentedRing : MonoBehaviour
{
    [Header("Ring Shape")]
    [Min(0.001f)] public float innerRadius = 1.2f;
    [Min(0.001f)] public float ringWidth = 0.28f;
    [Min(0.001f)] public float extrusionDepth = 0.16f;

    [Header("Segmentation")]
    [Min(1)] public int segmentCount = 2;
    [Range(0f, 180f)] public float gapSizeDegrees = 24f;
    [Range(0f, 360f)] public float startAngleOffsetDegrees = 0f;
    [Min(1)] public int arcResolutionPerSegment = 48;

    [Header("Generation")]
    public bool regenerateEveryFrameInEditor = false;

    MeshFilter _meshFilter;
    Mesh _mesh;

    void OnEnable()
    {
        EnsureMesh();
        Rebuild();
    }

    void OnValidate()
    {
        ClampValues();
        EnsureMesh();
        Rebuild();
    }

    void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && regenerateEveryFrameInEditor)
        {
            Rebuild();
        }
#endif
    }

    [ContextMenu("Rebuild Ring")]
    public void Rebuild()
    {
        ClampValues();
        if (_mesh == null) EnsureMesh();

        _mesh.Clear();

        float outerRadius = innerRadius + ringWidth;
        float anglePerSegment = 360f / segmentCount;
        float usableArc = Mathf.Max(0.5f, anglePerSegment - gapSizeDegrees);

        var vertices = new List<Vector3>(segmentCount * arcResolutionPerSegment * 8);
        var normals = new List<Vector3>(segmentCount * arcResolutionPerSegment * 8);
        var uvs = new List<Vector2>(segmentCount * arcResolutionPerSegment * 8);
        var triangles = new List<int>(segmentCount * arcResolutionPerSegment * 36);

        float halfDepth = extrusionDepth * 0.5f;

        for (int s = 0; s < segmentCount; s++)
        {
            float centerStart = s * anglePerSegment + startAngleOffsetDegrees;
            float a0 = centerStart + (gapSizeDegrees * 0.5f);
            float a1 = a0 + usableArc;
            BuildArcPrism(
                a0,
                a1,
                innerRadius,
                outerRadius,
                halfDepth,
                arcResolutionPerSegment,
                vertices,
                normals,
                uvs,
                triangles
            );
        }

        _mesh.SetVertices(vertices);
        _mesh.SetNormals(normals);
        _mesh.SetUVs(0, uvs);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateBounds();
    }

    void ClampValues()
    {
        innerRadius = Mathf.Max(0.001f, innerRadius);
        ringWidth = Mathf.Max(0.001f, ringWidth);
        extrusionDepth = Mathf.Max(0.001f, extrusionDepth);
        segmentCount = Mathf.Max(1, segmentCount);
        arcResolutionPerSegment = Mathf.Max(1, arcResolutionPerSegment);
    }

    void EnsureMesh()
    {
        if (_meshFilter == null)
            _meshFilter = GetComponent<MeshFilter>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "ProceduralSegmentedRingMesh" };
            _mesh.MarkDynamic();
        }

        if (_meshFilter.sharedMesh != _mesh)
            _meshFilter.sharedMesh = _mesh;
    }

    static Vector2 Polar(float radius, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
    }

    static void AddQuad(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal
    )
    {
        int i = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(0f, 1f));

        triangles.Add(i + 0);
        triangles.Add(i + 1);
        triangles.Add(i + 2);
        triangles.Add(i + 0);
        triangles.Add(i + 2);
        triangles.Add(i + 3);
    }

    static void BuildArcPrism(
        float startDeg,
        float endDeg,
        float innerRadius,
        float outerRadius,
        float halfDepth,
        int steps,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles
    )
    {
        float arcLen = Mathf.Max(0.001f, endDeg - startDeg);

        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            float a0 = startDeg + arcLen * t0;
            float a1 = startDeg + arcLen * t1;

            Vector2 i0 = Polar(innerRadius, a0);
            Vector2 o0 = Polar(outerRadius, a0);
            Vector2 i1 = Polar(innerRadius, a1);
            Vector2 o1 = Polar(outerRadius, a1);

            Vector3 fi0 = new Vector3(i0.x, i0.y, +halfDepth);
            Vector3 fo0 = new Vector3(o0.x, o0.y, +halfDepth);
            Vector3 fi1 = new Vector3(i1.x, i1.y, +halfDepth);
            Vector3 fo1 = new Vector3(o1.x, o1.y, +halfDepth);

            Vector3 bi0 = new Vector3(i0.x, i0.y, -halfDepth);
            Vector3 bo0 = new Vector3(o0.x, o0.y, -halfDepth);
            Vector3 bi1 = new Vector3(i1.x, i1.y, -halfDepth);
            Vector3 bo1 = new Vector3(o1.x, o1.y, -halfDepth);

            AddQuad(vertices, normals, uvs, triangles, fi0, fo0, fo1, fi1, Vector3.forward);
            AddQuad(vertices, normals, uvs, triangles, bo1, bo0, bi0, bi1, Vector3.back);

            Vector3 nOuter = new Vector3((o0.x + o1.x) * 0.5f, (o0.y + o1.y) * 0.5f, 0f).normalized;
            Vector3 nInner = -new Vector3((i0.x + i1.x) * 0.5f, (i0.y + i1.y) * 0.5f, 0f).normalized;

            AddQuad(vertices, normals, uvs, triangles, fo0, bo0, bo1, fo1, nOuter);
            AddQuad(vertices, normals, uvs, triangles, bi0, fi0, fi1, bi1, nInner);

            if (i == 0)
            {
                Vector3 nCap0 = new Vector3(Mathf.Sin(a0 * Mathf.Deg2Rad), -Mathf.Cos(a0 * Mathf.Deg2Rad), 0f).normalized;
                AddQuad(vertices, normals, uvs, triangles, fi0, bi0, bo0, fo0, nCap0);
            }

            if (i == steps - 1)
            {
                Vector3 nCap1 = new Vector3(-Mathf.Sin(a1 * Mathf.Deg2Rad), Mathf.Cos(a1 * Mathf.Deg2Rad), 0f).normalized;
                AddQuad(vertices, normals, uvs, triangles, fo1, bo1, bi1, fi1, nCap1);
            }
        }
    }
}
