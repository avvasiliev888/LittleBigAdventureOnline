using System;
using System.Collections.Generic;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Small, shared low-poly meshes used by the procedural characters and weapons.
    /// Everything is generated once per play session; primitives are never imported or
    /// instantiated through <see cref="GameObject.CreatePrimitive"/> (which would add colliders).
    /// </summary>
    public static class ProceduralMeshLibrary
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Axis-aligned cube with flat-shaded faces.</summary>
        public static Mesh Box => Get("box", MakeBox);

        /// <summary>Low-poly sphere used for heads, hands and decorative joints.</summary>
        public static Mesh Sphere => Get("sphere", MakeSphere);

        /// <summary>Low-poly cylinder used for arms, legs and hafts.</summary>
        public static Mesh Cylinder => Get("cylinder", MakeCylinder);

        /// <summary>Low-poly cone used for hoods, horns and staff tips.</summary>
        public static Mesh Cone => Get("cone", MakeCone);

        /// <summary>Flat crescent used for the attack slash flash.</summary>
        public static Mesh SlashArc => Get("slash", MakeSlashArc);

        /// <summary>Flat double-sided ring used by the shrinking click marker.</summary>
        public static Mesh Ring => Get("ring", MakeRing);

        private static Mesh Get(string key, Func<Mesh> factory)
        {
            if (Cache.TryGetValue(key, out var mesh) && mesh != null)
            {
                return mesh;
            }

            mesh = factory();
            mesh.name = "Procedural " + key;
            Cache[key] = mesh;
            return mesh;
        }

        private static Mesh MakeBox()
        {
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var indices = new List<int>(36);

            AddFace(vertices, normals, uv, indices, Vector3.left, Vector3.forward, Vector3.up);
            AddFace(vertices, normals, uv, indices, Vector3.right, Vector3.back, Vector3.up);
            AddFace(vertices, normals, uv, indices, Vector3.down, Vector3.right, Vector3.forward);
            AddFace(vertices, normals, uv, indices, Vector3.up, Vector3.right, Vector3.back);
            AddFace(vertices, normals, uv, indices, Vector3.back, Vector3.left, Vector3.up);
            AddFace(vertices, normals, uv, indices, Vector3.forward, Vector3.right, Vector3.up);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddFace(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv,
            List<int> indices,
            Vector3 normal,
            Vector3 tangent,
            Vector3 bitangent)
        {
            var start = vertices.Count;
            var center = normal * 0.5f;
            var a = center - (tangent * 0.5f) - (bitangent * 0.5f);
            var b = center + (tangent * 0.5f) - (bitangent * 0.5f);
            var c = center + (tangent * 0.5f) + (bitangent * 0.5f);
            var d = center - (tangent * 0.5f) + (bitangent * 0.5f);
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            for (var i = 0; i < 4; i++) normals.Add(normal);
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(0f, 1f));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);
        }

        private static Mesh MakeSphere()
        {
            const int segments = 8;
            const int rings = 5;
            var vertices = new List<Vector3>();
            var indices = new List<int>();

            vertices.Add(Vector3.down * 0.5f);
            for (var ring = 1; ring < rings; ring++)
            {
                var latitude = Mathf.PI * ring / rings;
                var radius = Mathf.Sin(latitude) * 0.5f;
                var y = Mathf.Cos(latitude) * 0.5f;
                for (var segment = 0; segment < segments; segment++)
                {
                    var longitude = Mathf.PI * 2f * segment / segments;
                    vertices.Add(new Vector3(Mathf.Cos(longitude) * radius, y, Mathf.Sin(longitude) * radius));
                }
            }
            vertices.Add(Vector3.up * 0.5f);

            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                indices.Add(0);
                indices.Add(1 + next);
                indices.Add(1 + segment);
            }

            for (var ring = 0; ring < rings - 2; ring++)
            {
                var row = 1 + (ring * segments);
                var nextRow = row + segments;
                for (var segment = 0; segment < segments; segment++)
                {
                    var next = (segment + 1) % segments;
                    indices.Add(row + segment);
                    indices.Add(row + next);
                    indices.Add(nextRow + next);
                    indices.Add(row + segment);
                    indices.Add(nextRow + next);
                    indices.Add(nextRow + segment);
                }
            }

            var top = vertices.Count - 1;
            var lastRow = 1 + ((rings - 2) * segments);
            for (var segment = 0; segment < segments; segment++)
            {
                var next = (segment + 1) % segments;
                indices.Add(top);
                indices.Add(lastRow + segment);
                indices.Add(lastRow + next);
            }

            return Finish(vertices, indices);
        }

        private static Mesh MakeCylinder()
        {
            const int sides = 8;
            var vertices = new List<Vector3>();
            var indices = new List<int>();

            for (var side = 0; side < sides; side++)
            {
                var a = Mathf.PI * 2f * side / sides;
                var next = Mathf.PI * 2f * (side + 1) / sides;
                var lower = new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f);
                var lowerNext = new Vector3(Mathf.Cos(next) * 0.5f, -0.5f, Mathf.Sin(next) * 0.5f);
                var upper = new Vector3(lower.x, 0.5f, lower.z);
                var upperNext = new Vector3(lowerNext.x, 0.5f, lowerNext.z);
                var start = vertices.Count;
                vertices.Add(lower);
                vertices.Add(lowerNext);
                vertices.Add(upperNext);
                vertices.Add(upper);
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 3);
            }

            for (var side = 0; side < sides; side++)
            {
                var a = Mathf.PI * 2f * side / sides;
                var next = Mathf.PI * 2f * (side + 1) / sides;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0.5f, Mathf.Sin(a) * 0.5f);
                var q = new Vector3(Mathf.Cos(next) * 0.5f, 0.5f, Mathf.Sin(next) * 0.5f);
                Triangle(vertices, indices, Vector3.up * 0.5f, p, q);
                Triangle(vertices, indices, Vector3.down * 0.5f, q, p);
            }

            return Finish(vertices, indices);
        }

        private static Mesh MakeCone()
        {
            const int sides = 8;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var side = 0; side < sides; side++)
            {
                var a = Mathf.PI * 2f * side / sides;
                var next = Mathf.PI * 2f * (side + 1) / sides;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f);
                var q = new Vector3(Mathf.Cos(next) * 0.5f, -0.5f, Mathf.Sin(next) * 0.5f);
                Triangle(vertices, indices, Vector3.up * 0.5f, p, q);
                Triangle(vertices, indices, Vector3.down * 0.5f, q, p);
            }
            return Finish(vertices, indices);
        }

        private static Mesh MakeSlashArc()
        {
            const int segments = 12;
            const float inner = 0.35f;
            const float outer = 1.05f;
            const float sweep = 1.7f;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var angle = -sweep * 0.5f + (sweep * i / segments);
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices.Add(direction * inner);
                vertices.Add(direction * outer);
                if (i == 0) continue;
                var start = (i - 1) * 2;
                indices.Add(start);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start + 3);
            }

            // Double-sided flash: the slash must read from either shoulder camera.
            var count = vertices.Count;
            for (var i = 0; i < count; i += 2)
            {
                var start = i;
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start + 3);
                indices.Add(start + 2);
            }

            return Finish(vertices, indices);
        }

        private static Mesh MakeRing()
        {
            const int segments = 24;
            const float inner = 0.44f;
            const float outer = 0.55f;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.PI * 2f * i / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(direction * inner);
                vertices.Add(direction * outer);
                if (i == 0) continue;
                var start = (i - 1) * 2;
                indices.Add(start);
                indices.Add(start + 1);
                indices.Add(start + 2);
                indices.Add(start + 1);
                indices.Add(start + 3);
                indices.Add(start + 2);
            }

            var count = vertices.Count;
            for (var i = 0; i < count - 2; i += 2)
            {
                indices.Add(i);
                indices.Add(i + 2);
                indices.Add(i + 1);
                indices.Add(i + 1);
                indices.Add(i + 2);
                indices.Add(i + 3);
            }

            return Finish(vertices, indices);
        }

        private static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        private static Mesh Finish(List<Vector3> vertices, List<int> indices)
        {
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
