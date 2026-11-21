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

        /// <summary>
        /// Плоский гранёный диск в плоскости XY. Морозное пятно на земле: у него неровный
        /// край, поэтому он не читается как нарисованный круг.
        /// </summary>
        public static Mesh Disc => Get("disc", () =>
        {
            const int sides = 14;
            var vertices = new List<Vector3> { Vector3.zero };
            var normals = new List<Vector3> { Vector3.back };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var indices = new List<int>();

            var random = new System.Random(4242);
            for (var i = 0; i < sides; i++)
            {
                var angle = (i / (float)sides) * Mathf.PI * 2f;
                // Неровный радиус: край пятна не должен быть идеальной окружностью.
                var radius = 0.5f * (0.82f + (float)random.NextDouble() * 0.18f);
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
                normals.Add(Vector3.back);
                uv.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            for (var i = 0; i < sides; i++)
            {
                indices.Add(0);
                indices.Add(1 + i);
                indices.Add(1 + ((i + 1) % sides));
            }

            var mesh = new Mesh { vertices = vertices.ToArray() };
            mesh.normals = normals.ToArray();
            mesh.uv = uv.ToArray();
            mesh.triangles = indices.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        });

        /// <summary>
        /// Гранёный кристалл: две пирамиды на общей грани, плоское затенение, неровные грани.
        /// Заменяет круглые примитивы в ледяной магии — гладкий шар и тор читаются как
        /// «кружок», а гранёный камень читается как лёд.
        /// </summary>
        public static Mesh IceCrystal => Get("ice-crystal", () => MakeCrystal(7, 3));

        /// <summary>
        /// Ледяной шип, растущий из земли: кристалл, растянутый по Y с наклоном.
        /// Основание сдвинуто к y=0, поэтому при масштабировании шип растёт ВВЕРХ от земли,
        /// а не в обе стороны от своего центра — иначе он висит в воздухе.
        /// </summary>
        public static Mesh IceSpike => Get("ice-spike", () => MakeCrystal(6, 2, shiftY: 0.62f));

        /// <summary>
        /// Координаты вершин кристалла для разлёта: каждый осколок строится с новым seed,
        /// поэтому два осколка в одном взрыве не выглядят копиями.
        /// </summary>
        public static Mesh CrystalVariant(int seed)
        {
            var key = "ice-crystal-" + seed;
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var mesh = MakeCrystal(6 + (seed % 4), 2 + (seed % 3), seed);
            mesh.name = "Procedural " + key;
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Кристалл из двух пирамид. <paramref name="sides"/> задаёт число граней,
        /// <paramref name="jitter"/> — разброс вершин, чтобы грани были неровными.
        /// <paramref name="shiftY"/> поднимает меш так, чтобы его основание стояло на y=0:
        /// нужно шипам, которые растут из земли. Нормали не сглаживаются.
        /// </summary>
        private static Mesh MakeCrystal(int sides, int jitter, int seed = 0, float shiftY = 0f)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var indices = new List<int>();

            var random = new System.Random(seed == 0 ? 12345 : seed * 7919);
            float Drift() => (float)((random.NextDouble() - 0.5) * 0.06 * jitter);

            // Пояс: вершины на неровном радиусе вокруг оси Y.
            var ring = new List<Vector3>(sides);
            for (var i = 0; i < sides; i++)
            {
                var angle = (i / (float)sides) * Mathf.PI * 2f;
                var radius = 0.34f + Drift();
                ring.Add(new Vector3(
                    Mathf.Cos(angle) * radius,
                    Drift() * 0.5f,
                    Mathf.Sin(angle) * radius));
            }

            // Вершина сверху вытянута: кристалл должен быть острым, а не шарообразным.
            var top = new Vector3(Drift() * 0.4f, 0.95f + Drift(), Drift() * 0.4f);
            var bottom = new Vector3(Drift() * 0.4f, -0.62f + Drift(), Drift() * 0.4f);

            for (var i = 0; i < sides; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % sides];

                AddTriangle(vertices, normals, uv, indices, top, a, b);
                AddTriangle(vertices, normals, uv, indices, bottom, b, a);
            }

            // Сдвиг основания к нулю: без него масштабирование растягивает шип в обе стороны
            // от центра, и он висит над землёй вместо того, чтобы расти из неё.
            if (shiftY != 0f)
            {
                for (var i = 0; i < vertices.Count; i++)
                {
                    vertices[i] += Vector3.up * shiftY;
                }
            }

            var mesh = new Mesh { vertices = vertices.ToArray() };
            mesh.normals = normals.ToArray();
            mesh.uv = uv.ToArray();
            mesh.triangles = indices.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Треугольник с нормалью от грани: плоское затенение без сглаживания.</summary>
        private static void AddTriangle(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uv,
            List<int> indices,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

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
