using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Albion.Client
{
    /// <summary>
    /// Presentation-only fantasy meshes/materials. Never moves the network root. Meshes and
    /// materials are shared; all arrays/material conversions are allocated only when spawning.
    /// </summary>
    public sealed class FantasyVisuals : MonoBehaviour
    {
        private const string OutlineName = "Toon Outline";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadeColor = Shader.PropertyToID("_ShadeColor");
        private static readonly Dictionary<Material, Material> Converted = new Dictionary<Material, Material>();
        private static readonly Dictionary<int, Material> Palette = new Dictionary<int, Material>();
        private static Material _outline;
        private static Mesh _rock;
        private static Mesh _box;
        private static Mesh _cone;

        private Transform _facing;
        private Transform _body;
        private Transform[] _limbs;
        private Quaternion[] _limbRest;
        private Vector3 _bodyRest;
        private bool _golem;
        private float _phase;

        /// <summary>Shared low-poly loot sack instead of a capsule corpse marker.</summary>
        public static GameObject CreateCorpse(Transform parent, uint corpseId)
        {
            var root = new GameObject("Corpse " + corpseId);
            root.transform.SetParent(parent, false);
            Part(root.transform, "Loot sack", Rock, new Vector3(0f, 0.22f, 0f), new Vector3(0.65f, 0.42f, 0.58f), MaterialFor(3));
            Part(root.transform, "Sack tie", Box, new Vector3(0f, 0.37f, 0f), new Vector3(0.25f, 0.1f, 0.25f), MaterialFor(11), false);
            return root;
        }

        /// <summary>Creates a zone-specific procedural wolf or horned stone golem (no colliders).</summary>
        public static Transform CreateMob(Transform parent, uint actorId, string zone)
        {
            var root = new GameObject("Mob " + actorId + " " + (zone == "red" ? "Stone Golem" : "Wolf")).transform;
            root.SetParent(parent, false);
            var controller = root.gameObject.AddComponent<FantasyVisuals>();
            controller._golem = zone == "red";
            controller._facing = new GameObject("Visual Facing").transform;
            controller._facing.SetParent(root, false);
            controller._body = new GameObject("Animated Body").transform;
            controller._body.SetParent(controller._facing, false);
            controller._phase = (actorId % 7) * 0.73f;
            if (controller._golem) controller.BuildGolem();
            else controller.BuildWolf(zone == "yellow");
            controller._bodyRest = controller._body.localPosition;
            controller._limbRest = new Quaternion[controller._limbs.Length];
            for (var i = 0; i < controller._limbs.Length; i++) controller._limbRest[i] = controller._limbs[i].localRotation;
            return root;
        }

        /// <summary>Call after applying the entity position; velocity drives only visual facing/gait.</summary>
        public void SetMotion(Vector3 velocity, bool dead, bool reset, float deltaTime)
        {
            if (_facing == null) return;
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) deltaTime = 0f;
            velocity.y = 0f;
            var speed = velocity.magnitude;
            if (dead || reset || !float.IsFinite(speed) || speed <= 0.08f) speed = 0f;
            if (speed > 0f)
                _facing.rotation = Quaternion.Slerp(_facing.rotation, Quaternion.LookRotation(velocity / speed),
                    1f - Mathf.Exp(-14f * deltaTime));
            var amount = Mathf.Clamp01(speed / (_golem ? 2.5f : 2.2f));
            _phase = Mathf.Repeat(_phase + deltaTime * Mathf.Lerp(2f, _golem ? 7f : 13f, amount), Mathf.PI * 2f);
            var stride = Mathf.Sin(_phase) * amount;
            _body.localPosition = _bodyRest + Vector3.up * (Mathf.Abs(Mathf.Sin(_phase * 2f)) * amount * (_golem ? 0.055f : 0.035f));
            for (var i = 0; i < _limbs.Length; i++)
            {
                var sign = _golem ? (i % 2 == 0 ? 1f : -1f) : (i == 0 || i == 3 ? 1f : -1f);
                _limbs[i].localRotation = _limbRest[i] * Quaternion.Euler(stride * sign * (_golem ? 19f : 27f), 0f, 0f);
            }
        }

        private void BuildWolf(bool yellow)
        {
            // Long muzzle, pointed ears, four jointed legs and a swept tail read as a wolf even
            // at the gameplay camera distance; warm alpha wolves are larger than slate scavengers.
            var fur = MaterialFor(yellow ? 1 : 0);
            var mane = MaterialFor(yellow ? 3 : 2);
            var cream = MaterialFor(4);
            var dark = MaterialFor(5);
            var eye = MaterialFor(yellow ? 7 : 6);
            _facing.localScale = Vector3.one * (yellow ? 1.16f : 1f);
            Part(_body, "Long ribcage", Rock, new Vector3(0f, 0.78f, -0.05f), new Vector3(0.62f, 0.65f, 1.35f), fur);
            Part(_body, "Shoulder mane", Rock, new Vector3(0f, 0.87f, 0.43f), new Vector3(0.76f, 0.85f, 0.7f), mane);
            Part(_body, "Cream throat", Rock, new Vector3(0f, 0.9f, 0.7f), new Vector3(0.42f, 0.6f, 0.32f), cream);
            Part(_body, "Angular head", Rock, new Vector3(0f, 1.19f, 0.71f), new Vector3(0.52f, 0.55f, 0.62f), fur);
            Part(_body, "Long muzzle", Box, new Vector3(0f, 1.1f, 1.05f), new Vector3(0.28f, 0.22f, 0.5f), cream);
            Part(_body, "Nose", Box, new Vector3(0f, 1.13f, 1.31f), new Vector3(0.22f, 0.14f, 0.1f), dark);
            for (var side = -1; side <= 1; side += 2)
            {
                var ear = Part(_body, "Pointed ear", Cone, new Vector3(side * 0.2f, 1.36f, 0.57f), new Vector3(0.28f, 0.43f, 0.25f), mane);
                ear.localRotation = Quaternion.Euler(-12f, 0f, side * -14f);
                Part(_body, "Golden eye", Rock, new Vector3(side * 0.225f, 1.25f, 0.91f), new Vector3(0.075f, 0.085f, 0.1f), eye, false);
            }
            var tail = Part(_body, "Swept tail", Cone, new Vector3(0f, 0.83f, -0.64f), new Vector3(0.34f, 0.95f, 0.34f), mane);
            tail.localRotation = Quaternion.Euler(-118f, 0f, 0f);
            _limbs = new Transform[4];
            for (var i = 0; i < 4; i++)
            {
                var leg = new GameObject(i < 2 ? "Foreleg" : "Hindleg").transform;
                leg.SetParent(_body, false);
                leg.localPosition = new Vector3(i % 2 == 0 ? -0.23f : 0.23f, 0.7f, i < 2 ? 0.44f : -0.48f);
                Part(leg, "Leg", Rock, new Vector3(0f, -0.26f, 0f), new Vector3(0.22f, 0.61f, 0.26f), fur);
                Part(leg, "Paw", Box, new Vector3(0f, -0.61f, 0.055f), new Vector3(0.24f, 0.16f, 0.34f), dark);
                _limbs[i] = leg;
            }
        }

        private void BuildGolem()
        {
            var stone = MaterialFor(8);
            var dark = MaterialFor(9);
            var ember = MaterialFor(10);
            var horn = MaterialFor(11);
            Part(_body, "Granite torso", Rock, new Vector3(0f, 1.35f, 0f), new Vector3(1.08f, 1.35f, 0.76f), stone);
            Part(_body, "Chest ember core", Rock, new Vector3(0f, 1.52f, 0.4f), new Vector3(0.25f, 0.4f, 0.12f), ember, false);
            Part(_body, "Belt slab", Rock, new Vector3(0f, 0.91f, 0f), new Vector3(0.9f, 0.47f, 0.68f), dark);
            Part(_body, "Stone head", Rock, new Vector3(0f, 2.08f, 0.02f), new Vector3(0.7f, 0.72f, 0.63f), stone);
            Part(_body, "Heavy brow", Box, new Vector3(0f, 2.16f, 0.32f), new Vector3(0.59f, 0.13f, 0.16f), dark);
            for (var side = -1; side <= 1; side += 2)
            {
                Part(_body, "Ember eye", Box, new Vector3(side * 0.17f, 2.06f, 0.337f), new Vector3(0.15f, 0.085f, 0.05f), ember, false);
                var spike = Part(_body, "Crown horn", Cone, new Vector3(side * 0.25f, 2.3f, -0.03f), new Vector3(0.24f, 0.65f, 0.24f), horn);
                spike.localRotation = Quaternion.Euler(-10f, 0f, -side * 30f);
                Part(_body, "Shoulder boulder", Rock, new Vector3(side * 0.7f, 1.75f, -0.02f), new Vector3(0.73f, 0.73f, 0.8f), stone);
            }
            _limbs = new Transform[4];
            for (var i = 0; i < 4; i++)
            {
                var arm = i < 2;
                var side = i % 2 == 0 ? -1f : 1f;
                var limb = new GameObject(arm ? "Rock arm" : "Rock leg").transform;
                limb.SetParent(_body, false);
                limb.localPosition = new Vector3(side * (arm ? 0.83f : 0.31f), arm ? 1.65f : 0.87f, 0f);
                Part(limb, "Jointed stone", Rock, new Vector3(0f, arm ? -0.33f : -0.29f, 0f), new Vector3(arm ? 0.48f : 0.4f, 0.7f, 0.48f), stone);
                Part(limb, arm ? "Heavy fist" : "Stone foot", Rock, new Vector3(0f, -0.7f, arm ? 0.05f : 0.1f), new Vector3(0.5f, 0.35f, arm ? 0.5f : 0.63f), dark);
                _limbs[i] = limb;
            }
        }

        private static Transform Part(Transform parent, string name, Mesh mesh, Vector3 position, Vector3 scale, Material material, bool outline = true)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (outline)
            {
                var hull = new GameObject(OutlineName);
                hull.transform.SetParent(obj.transform, false);
                hull.AddComponent<MeshFilter>().sharedMesh = mesh;
                var hullRenderer = hull.AddComponent<MeshRenderer>();
                hullRenderer.sharedMaterial = OutlineMaterial;
                hullRenderer.shadowCastingMode = ShadowCastingMode.Off;
                hullRenderer.receiveShadows = false;
                hullRenderer.lightProbeUsage = LightProbeUsage.Off;
                hullRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return obj.transform;
        }

        private static Shader RequiredShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new InvalidOperationException("Required fantasy visual shader missing: " + name);
            return shader;
        }

        private static Material ToonMaterial(Material source)
        {
            if (source.shader != null && source.shader.name == "Albion/Toon Lit") return source;
            if (Converted.TryGetValue(source, out var cached) && cached != null) return cached;
            var result = new Material(RequiredShader("Albion/Toon Lit")) { name = source.name + " Toon", enableInstancing = true };
            var color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            result.SetColor(BaseColor, color);
            result.SetColor(ShadeColor, Shade(color));
            var textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
            if (textureProperty != null && result.HasProperty("_BaseMap"))
            {
                result.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                result.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                result.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }
            if (source.HasProperty("_EmissionColor")) result.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            Converted[source] = result;
            return result;
        }

        private static Color Shade(Color color) => new Color(color.r * 0.46f, color.g * 0.5f, color.b * 0.61f, color.a);

        private static Material OutlineMaterial
        {
            get
            {
                if (_outline != null) return _outline;
                _outline = new Material(RequiredShader("Albion/Toon Outline")) { name = "Shared Fantasy Outline", enableInstancing = true };
                _outline.SetColor("_OutlineColor", new Color(0.055f, 0.07f, 0.075f, 1f));
                _outline.SetFloat("_OutlineWidth", 0.014f);
                return _outline;
            }
        }

        private static Material MaterialFor(int id)
        {
            if (Palette.TryGetValue(id, out var cached) && cached != null) return cached;
            Color color;
            switch (id)
            {
                case 0: color = new Color(0.45f, 0.56f, 0.62f); break;
                case 1: color = new Color(0.6f, 0.34f, 0.17f); break;
                case 2: color = new Color(0.23f, 0.32f, 0.39f); break;
                case 3: color = new Color(0.33f, 0.2f, 0.12f); break;
                case 4: color = new Color(0.82f, 0.79f, 0.66f); break;
                case 5: color = new Color(0.1f, 0.12f, 0.14f); break;
                case 6: color = new Color(0.6f, 0.9f, 0.95f); break;
                case 7: color = new Color(1f, 0.76f, 0.16f); break;
                case 8: color = new Color(0.36f, 0.4f, 0.42f); break;
                case 9: color = new Color(0.22f, 0.24f, 0.27f); break;
                case 10: color = new Color(1f, 0.29f, 0.06f); break;
                default: color = new Color(0.73f, 0.65f, 0.45f); break;
            }
            var material = new Material(RequiredShader("Albion/Toon Lit")) { name = "Fantasy Palette " + id, enableInstancing = true };
            material.SetColor(BaseColor, color);
            material.SetColor(ShadeColor, Shade(color));
            material.SetFloat("_RimStrength", 0.12f);
            if (id == 6 || id == 7 || id == 10) material.SetColor("_EmissionColor", color * 0.3f);
            Palette[id] = material;
            return material;
        }

        private static Mesh Rock => _rock != null ? _rock : _rock = MakeRock();
        private static Mesh Box => _box != null ? _box : _box = MakeBox();
        private static Mesh Cone => _cone != null ? _cone : _cone = MakeCone();

        private static void Triangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            var start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        }

        private static Mesh FinishMesh(string name, List<Vector3> vertices, List<int> indices)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Mesh MakeRock()
        {
            const int sides = 8;
            const int rings = 4;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var ring = 0; ring < rings; ring++)
            {
                var a = Mathf.PI * ring / rings;
                var b = Mathf.PI * (ring + 1) / rings;
                for (var side = 0; side < sides; side++)
                {
                    var p = Mathf.PI * 2f * side / sides;
                    var q = Mathf.PI * 2f * (side + 1) / sides;
                    var v0 = RockPoint(a, p);
                    var v1 = RockPoint(a, q);
                    var v2 = RockPoint(b, q);
                    var v3 = RockPoint(b, p);
                    if (ring > 0) Triangle(vertices, indices, v0, v1, v2);
                    if (ring < rings - 1) Triangle(vertices, indices, v0, v2, v3);
                }
            }
            return FinishMesh("Shared faceted rock 48 triangles", vertices, indices);
        }

        private static Vector3 RockPoint(float latitude, float longitude) =>
            new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude)) * 0.5f;

        private static Mesh MakeBox()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var normals = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var normal in normals)
            {
                var up = Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up;
                var right = Vector3.Cross(up, normal);
                var a = (normal - right - up) * 0.5f;
                var b = (normal + right - up) * 0.5f;
                var c = (normal + right + up) * 0.5f;
                var d = (normal - right + up) * 0.5f;
                Triangle(vertices, indices, a, b, c);
                Triangle(vertices, indices, a, c, d);
            }
            return FinishMesh("Shared fantasy box", vertices, indices);
        }

        private static Mesh MakeCone()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (var side = 0; side < 6; side++)
            {
                var a = side * Mathf.PI / 3f;
                var b = (side + 1) * Mathf.PI / 3f;
                var p = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                var q = new Vector3(Mathf.Cos(b) * 0.5f, 0f, Mathf.Sin(b) * 0.5f);
                Triangle(vertices, indices, p, Vector3.up, q);
                Triangle(vertices, indices, Vector3.zero, p, q);
            }
            return FinishMesh("Shared fantasy horn", vertices, indices);
        }
    }
}
