using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Procedural meshes for the house. Boxes get UVs in metres divided by a
    // texel scale, so a 7 m wall and a 0.5 m drawer show the same texture
    // density instead of one stretched image per face. Meshes are cached by
    // their exact parameters for the process lifetime; the set is bounded by
    // the authored layout, and scene reloads reuse them.
    public static class MeshKit
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        // Axis-aligned box centred on the origin. uvMetres: metres per
        // texture repeat.
        public static Mesh Box(Vector3 size, float uvMetres = 2f)
        {
            string key = "box" + size.x.ToString("F3") + "_" + size.y.ToString("F3") + "_" + size.z.ToString("F3") + "_" + uvMetres.ToString("F2");
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tangents = new List<Vector4>(24);
            var triangles = new List<int>(36);
            Vector3 h = size * 0.5f;

            // Each face: (normal, u axis, v axis) with u x v = normal, which is
            // Unity's front-face winding for the triangles added in Face().
            Face(vertices, normals, uvs, tangents, triangles, Vector3.right, Vector3.back, Vector3.up, h, size.z, size.y, uvMetres);
            Face(vertices, normals, uvs, tangents, triangles, Vector3.left, Vector3.forward, Vector3.up, h, size.z, size.y, uvMetres);
            Face(vertices, normals, uvs, tangents, triangles, Vector3.up, Vector3.forward, Vector3.right, h, size.z, size.x, uvMetres);
            Face(vertices, normals, uvs, tangents, triangles, Vector3.down, Vector3.right, Vector3.forward, h, size.x, size.z, uvMetres);
            Face(vertices, normals, uvs, tangents, triangles, Vector3.forward, Vector3.right, Vector3.up, h, size.x, size.y, uvMetres);
            Face(vertices, normals, uvs, tangents, triangles, Vector3.back, Vector3.left, Vector3.up, h, size.x, size.y, uvMetres);

            var mesh = new Mesh { name = "PHYSXR_" + key, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        private static void Face(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<Vector4> t, List<int> tri,
            Vector3 normal, Vector3 uAxis, Vector3 vAxis, Vector3 half, float uSize, float vSize, float uvMetres)
        {
            int start = v.Count;
            Vector3 centre = Vector3.Scale(normal, half);
            Vector3 du = uAxis * (uSize * 0.5f);
            Vector3 dv = vAxis * (vSize * 0.5f);

            float us = uSize / uvMetres, vs = vSize / uvMetres;
            v.Add(centre - du - dv); uv.Add(new Vector2(0f, 0f));
            v.Add(centre + du - dv); uv.Add(new Vector2(us, 0f));
            v.Add(centre + du + dv); uv.Add(new Vector2(us, vs));
            v.Add(centre - du + dv); uv.Add(new Vector2(0f, vs));
            for (int i = 0; i < 4; i++)
            {
                n.Add(normal);
                t.Add(new Vector4(uAxis.x, uAxis.y, uAxis.z, 1f));
            }

            tri.Add(start); tri.Add(start + 1); tri.Add(start + 2);
            tri.Add(start); tri.Add(start + 2); tri.Add(start + 3);
        }

        // A closed cylinder along Y, centred on the origin.
        public static Mesh Cylinder(float radius, float height, int segments = 16, float topRadius = -1f)
        {
            if (topRadius < 0f)
                topRadius = radius;
            string key = "cyl" + radius.ToString("F3") + "_" + height.ToString("F3") + "_" + segments + "_" + topRadius.ToString("F3");
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float y0 = -height * 0.5f, y1 = height * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                vertices.Add(new Vector3(c * radius, y0, s * radius));
                vertices.Add(new Vector3(c * topRadius, y1, s * topRadius));
                uvs.Add(new Vector2((float)i / segments * 2f, 0f));
                uvs.Add(new Vector2((float)i / segments * 2f, height));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 3);
                triangles.Add(a); triangles.Add(a + 3); triangles.Add(a + 2);
            }

            AddCap(vertices, uvs, triangles, y1, topRadius, segments, true);
            AddCap(vertices, uvs, triangles, y0, radius, segments, false);

            var mesh = new Mesh { name = "PHYSXR_" + key, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }

        private static void AddCap(List<Vector3> v, List<Vector2> uv, List<int> tri, float y, float r, int segments, bool up)
        {
            int centre = v.Count;
            v.Add(new Vector3(0f, y, 0f));
            uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                uv.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int i = 0; i < segments; i++)
            {
                if (up)
                {
                    tri.Add(centre); tri.Add(centre + i + 2); tri.Add(centre + i + 1);
                }
                else
                {
                    tri.Add(centre); tri.Add(centre + i + 1); tri.Add(centre + i + 2);
                }
            }
        }

        // A unit-ish quad (1x1, facing -Z) for flat decals such as paintings.
        public static Mesh Quad(float width, float height)
        {
            string key = "quad" + width.ToString("F3") + "_" + height.ToString("F3");
            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
                return cached;

            var mesh = new Mesh { name = "PHYSXR_" + key, hideFlags = HideFlags.DontSave };
            float w = width * 0.5f, h = height * 0.5f;
            mesh.SetVertices(new List<Vector3> { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Cache[key] = mesh;
            return mesh;
        }
    }
}
