using System.Collections.Generic;
using UnityEngine;

namespace DropBeat
{
    /// <summary>Shared runtime meshes. Built once and reused by every ring/ball/effect (nothing is saved into the scene).</summary>
    public static class ProceduralMeshes
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Torus lying in the XZ plane (normal = +Y).</summary>
        public static Mesh Torus(float major, float minor, int segments = 64, int sides = 12)
        {
            string key = "torus" + major + "_" + minor + "_" + segments + "_" + sides;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>((segments + 1) * (sides + 1));
            var normals = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(segments * sides * 6);
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(u) * major, 0f, Mathf.Sin(u) * major);
                var outward = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                for (int j = 0; j <= sides; j++)
                {
                    float v = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = outward * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                    verts.Add(center + n * minor);
                    normals.Add(n);
                    uvs.Add(new Vector2(i / (float)segments, j / (float)sides));
                }
            }

            int row = sides + 1;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * row + j;
                    int b = (i + 1) * row + j;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            }

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Unit quad in the XY plane centred on the origin, facing -Z.</summary>
        public static Mesh Quad()
        {
            const string key = "quad";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var mesh = new Mesh { name = key };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Vertical gradient quad (backdrop), bottom/top colours baked into vertex colours.</summary>
        public static Mesh GradientQuad(Color bottom, Color top)
        {
            var mesh = new Mesh { name = "gradient" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.colors = new[] { bottom, bottom, top, top };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Grid of thin line quads in the XY plane; lines fade toward the edges via vertex alpha.</summary>
        public static Mesh GridLines(float halfWidth, float halfHeight, float spacing, float thickness)
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float alphaA, float alphaB)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                colors.Add(new Color(1, 1, 1, alphaA)); colors.Add(new Color(1, 1, 1, alphaA));
                colors.Add(new Color(1, 1, 1, alphaB)); colors.Add(new Color(1, 1, 1, alphaB));
                uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 1);
            }

            float t = thickness * 0.5f;
            int nx = Mathf.CeilToInt(halfWidth / spacing);
            for (int i = -nx; i <= nx; i++)
            {
                float x = i * spacing;
                float edge = 1f - Mathf.Abs(x) / (halfWidth + spacing);
                // Vertical line split in two halves so it fades toward top and bottom.
                AddQuad(new Vector3(x - t, -halfHeight, 0), new Vector3(x + t, -halfHeight, 0), new Vector3(x - t, 0, 0), new Vector3(x + t, 0, 0), 0f, edge);
                AddQuad(new Vector3(x - t, 0, 0), new Vector3(x + t, 0, 0), new Vector3(x - t, halfHeight, 0), new Vector3(x + t, halfHeight, 0), edge, 0f);
            }

            int ny = Mathf.CeilToInt(halfHeight / spacing);
            for (int j = -ny; j <= ny; j++)
            {
                float y = j * spacing;
                float edge = 1f - Mathf.Abs(y) / (halfHeight + spacing);
                int i = verts.Count;
                // Horizontal line: three vertices across so it fades at both sides.
                verts.Add(new Vector3(-halfWidth, y - t, 0)); verts.Add(new Vector3(-halfWidth, y + t, 0));
                verts.Add(new Vector3(0, y - t, 0)); verts.Add(new Vector3(0, y + t, 0));
                verts.Add(new Vector3(halfWidth, y - t, 0)); verts.Add(new Vector3(halfWidth, y + t, 0));
                colors.Add(new Color(1, 1, 1, 0)); colors.Add(new Color(1, 1, 1, 0));
                colors.Add(new Color(1, 1, 1, edge)); colors.Add(new Color(1, 1, 1, edge));
                colors.Add(new Color(1, 1, 1, 0)); colors.Add(new Color(1, 1, 1, 0));
                for (int k = 0; k < 6; k++) uvs.Add(Vector2.zero);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i + 3); tris.Add(i + 2);
                tris.Add(i + 2); tris.Add(i + 3); tris.Add(i + 4); tris.Add(i + 3); tris.Add(i + 5); tris.Add(i + 4);
            }

            var mesh = new Mesh { name = "grid" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
