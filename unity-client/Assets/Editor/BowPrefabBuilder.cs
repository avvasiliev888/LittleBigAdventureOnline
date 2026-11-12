using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Builds a small authored bow prefab for the hero. The KayKit weapon set shipped with
    /// the project has a crossbow but no bow, and a crossbow silhouette cannot read as a bow.
    /// Everything is low-poly and uses URP Lit, matching the imported weapon prefabs.
    /// </summary>
    public static class BowPrefabBuilder
    {
        private const string PrefabPath = "Assets/Art/Weapons/Generated/bow.prefab";
        private const string MeshPath = "Assets/Art/Weapons/Generated/BowLimbs.asset";
        private const string WoodPath = "Assets/Art/Weapons/Generated/M_BowWood.mat";
        private const string StringPath = "Assets/Art/Weapons/Generated/M_BowString.mat";

        [MenuItem("Albion/Art/Create Bow Prefab")]
        public static void CreateBow()
        {
            var wood = GetOrCreateMaterial(WoodPath, new Color(0.23f, 0.12f, 0.055f), 0.28f);
            var stringMaterial = GetOrCreateMaterial(StringPath, new Color(0.86f, 0.82f, 0.68f), 0.15f);

            var root = new GameObject("bow");
            var limbs = new GameObject("Bow Limbs");
            limbs.transform.SetParent(root.transform, false);
            var filter = limbs.AddComponent<MeshFilter>();
            var renderer = limbs.AddComponent<MeshRenderer>();
            // A Mesh created at edit time is not serialized into a prefab unless it is an asset.
            // Without this the bow prefab instantiates with a missing mesh (zero bounds).
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
            var mesh = BuildLimbs();
            AssetDatabase.CreateAsset(mesh, MeshPath);
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = wood;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grip.name = "Grip";
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, 0.025f);
            grip.transform.localScale = new Vector3(0.075f, 0.24f, 0.075f);
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            grip.GetComponent<MeshRenderer>().sharedMaterial = wood;

            var stringObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stringObject.name = "Bow String";
            stringObject.transform.SetParent(root.transform, false);
            stringObject.transform.localPosition = new Vector3(0f, 0f, 0.18f);
            stringObject.transform.localScale = new Vector3(0.008f, 1.42f, 0.008f);
            Object.DestroyImmediate(stringObject.GetComponent<Collider>());
            stringObject.GetComponent<MeshRenderer>().sharedMaterial = stringMaterial;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[art] Created bow prefab: " + PrefabPath);
        }

        private static Material GetOrCreateMaterial(string path, Color color, float smoothness)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh BuildLimbs()
        {
            const float halfHeight = 0.72f;
            const float bend = 0.18f;
            const float halfWidth = 0.035f;
            const float halfDepth = 0.028f;
            const int segments = 20;

            var vertices = new List<Vector3>((segments + 1) * 4);
            var triangles = new List<int>(segments * 24);
            for (var i = 0; i <= segments; i++)
            {
                var t = -1f + (2f * i / segments);
                var y = t * halfHeight;
                var z = bend * t * t;
                var dz = 2f * bend * t / halfHeight;
                var tangent = new Vector3(0f, 1f, dz).normalized;
                var normal = new Vector3(0f, -tangent.z, tangent.y).normalized;
                var center = new Vector3(0f, y, z);
                vertices.Add(center - Vector3.right * halfWidth - normal * halfDepth);
                vertices.Add(center + Vector3.right * halfWidth - normal * halfDepth);
                vertices.Add(center + Vector3.right * halfWidth + normal * halfDepth);
                vertices.Add(center - Vector3.right * halfWidth + normal * halfDepth);
            }

            for (var i = 0; i < segments; i++)
            {
                var current = i * 4;
                var next = (i + 1) * 4;
                for (var side = 0; side < 4; side++)
                {
                    var a = current + side;
                    var b = current + ((side + 1) % 4);
                    var c = next + side;
                    var d = next + ((side + 1) % 4);
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }

            // Caps: the bow tips should be closed, not hollow.
            triangles.Add(0); triangles.Add(2); triangles.Add(1);
            triangles.Add(0); triangles.Add(3); triangles.Add(2);
            var last = segments * 4;
            triangles.Add(last); triangles.Add(last + 1); triangles.Add(last + 2);
            triangles.Add(last); triangles.Add(last + 2); triangles.Add(last + 3);

            var mesh = new Mesh { name = "Bow Limbs" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
