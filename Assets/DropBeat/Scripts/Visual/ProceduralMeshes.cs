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

        /// <summary>
        /// Solid ring band in the XY plane (same orientation as the ring quad): a box cross-section swept round
        /// <paramref name="radius"/>. UV.x = angle clockwise from +Y (0..1, like the Ring shader), UV.y = face
        /// (0 front, 1 outer wall, 2 back, 3 inner wall).
        /// </summary>
        public static Mesh RingBand(float radius, float halfWidth, float halfDepth, int segments = 96)
        {
            string key = "band" + radius + "_" + halfWidth + "_" + halfDepth + "_" + segments;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            float rIn = radius - halfWidth, rOut = radius + halfWidth;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int f = 0; f < 4; f++)
            {
                int start = verts.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float u = i / (float)segments;
                    float a = u * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                    Vector3 p0, p1, n;
                    switch (f)
                    {
                        case 0: p0 = dir * rIn + Vector3.back * halfDepth; p1 = dir * rOut + Vector3.back * halfDepth; n = Vector3.back; break;
                        case 1: p0 = dir * rOut + Vector3.back * halfDepth; p1 = dir * rOut + Vector3.forward * halfDepth; n = dir; break;
                        case 2: p0 = dir * rOut + Vector3.forward * halfDepth; p1 = dir * rIn + Vector3.forward * halfDepth; n = Vector3.forward; break;
                        default: p0 = dir * rIn + Vector3.forward * halfDepth; p1 = dir * rIn + Vector3.back * halfDepth; n = -dir; break;
                    }
                    verts.Add(p0); verts.Add(p1);
                    normals.Add(n); normals.Add(n);
                    uvs.Add(new Vector2(u, f)); uvs.Add(new Vector2(u, f));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = start + i * 2, b = a + 2;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
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

        /// <summary>Wireframe cube of size 1: twelve thin square bars along its edges.</summary>
        public static Mesh WireCube(float bar)
        {
            string key = "wirecube" + bar;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var combine = new List<CombineInstance>();
            Mesh box = Box();
            float h = 0.5f;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int s = 0; s < 4; s++)
                {
                    float a = (s & 1) == 0 ? -h : h;
                    float b = (s & 2) == 0 ? -h : h;
                    Vector3 pos = axis == 0 ? new Vector3(0, a, b) : axis == 1 ? new Vector3(a, 0, b) : new Vector3(a, b, 0);
                    Vector3 scale = axis == 0 ? new Vector3(1f + bar, bar, bar) : axis == 1 ? new Vector3(bar, 1f + bar, bar) : new Vector3(bar, bar, 1f + bar);
                    combine.Add(new CombineInstance { mesh = box, transform = Matrix4x4.TRS(pos, Quaternion.identity, scale) });
                }
            }
            var mesh = new Mesh { name = key };
            mesh.CombineMeshes(combine.ToArray(), true, true);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Unit cube centred on the origin, flat normals, white vertex colour.</summary>
        public static Mesh Box()
        {
            const string key = "box";
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 f in n)
            {
                Vector3 u = f.y != 0f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(f, u);
                int i = verts.Count;
                verts.Add((f - u - v) * 0.5f); verts.Add((f + u - v) * 0.5f);
                verts.Add((f + u + v) * 0.5f); verts.Add((f - u + v) * 0.5f);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            var colors = new List<Color>();
            for (int i = 0; i < verts.Count; i++) colors.Add(Color.white);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Smooth UV sphere of diameter 1 (enough segments for clean chrome highlights).</summary>
        public static Mesh Sphere(int segments = 64, int rings = 40)
        {
            string key = "sphere" + segments + "_" + rings;
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>((segments + 1) * (rings + 1));
            var normals = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(segments * rings * 6);
            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float theta = v * Mathf.PI;
                for (int s = 0; s <= segments; s++)
                {
                    float u = s / (float)segments;
                    float phi = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    verts.Add(n * 0.5f);
                    normals.Add(n);
                    uvs.Add(new Vector2(u, 1f - v));
                }
            }

            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s;
                    int b = a + row;
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
