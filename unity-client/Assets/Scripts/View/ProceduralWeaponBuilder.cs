using UnityEngine;

namespace Albion.Client
{
    /// <summary>Builds the five visible weapons from the same low-poly primitive library.</summary>
    internal static class ProceduralWeaponBuilder
    {
        private static readonly Color Wood = new Color(0.32f, 0.2f, 0.11f);
        private static readonly Color Leather = new Color(0.22f, 0.14f, 0.1f);
        private static readonly Color Steel = new Color(0.68f, 0.74f, 0.78f);
        private static readonly Color Gold = new Color(0.94f, 0.72f, 0.26f);
        private static readonly Color Arcane = new Color(0.42f, 0.82f, 0.95f);

        /// <summary>Creates a weapon root under a hand socket. The root is left inactive.</summary>
        public static Transform Create(WeaponKind kind, Transform socket)
        {
            var root = new GameObject("Weapon " + kind).transform;
            root.SetParent(socket, false);
            root.localPosition = new Vector3(0f, -0.02f, 0.03f);
            switch (kind)
            {
                case WeaponKind.Axe:
                    BuildAxe(root);
                    break;
                case WeaponKind.Bow:
                    BuildBow(root);
                    break;
                case WeaponKind.Staff:
                    BuildStaff(root);
                    break;
                case WeaponKind.Dagger:
                    BuildDagger(root);
                    break;
                default:
                    BuildSword(root);
                    break;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        private static void BuildSword(Transform root)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, -12f);
            Part(root, "Sword Grip", ProceduralMeshLibrary.Cylinder, Leather,
                new Vector3(0f, 0.04f, 0f), new Vector3(0.055f, 0.28f, 0.055f));
            Part(root, "Sword Pommel", ProceduralMeshLibrary.Sphere, Gold,
                new Vector3(0f, -0.12f, 0f), new Vector3(0.1f, 0.1f, 0.1f), outline: false);
            Part(root, "Sword Guard", ProceduralMeshLibrary.Box, Gold,
                new Vector3(0f, 0.2f, 0f), new Vector3(0.24f, 0.045f, 0.07f));
            Part(root, "Sword Blade", ProceduralMeshLibrary.Box, Steel,
                new Vector3(0f, 0.62f, 0f), new Vector3(0.075f, 0.8f, 0.028f));
            Part(root, "Sword Tip", ProceduralMeshLibrary.Cone, Steel,
                new Vector3(0f, 1.03f, 0f), new Vector3(0.075f, 0.16f, 0.028f), outline: false);
        }

        private static void BuildDagger(Transform root)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, -18f);
            Part(root, "Dagger Grip", ProceduralMeshLibrary.Cylinder, Leather,
                new Vector3(0f, 0.02f, 0f), new Vector3(0.05f, 0.18f, 0.05f));
            Part(root, "Dagger Guard", ProceduralMeshLibrary.Box, Gold,
                new Vector3(0f, 0.12f, 0f), new Vector3(0.16f, 0.04f, 0.06f));
            Part(root, "Dagger Blade", ProceduralMeshLibrary.Box, Steel,
                new Vector3(0f, 0.32f, 0f), new Vector3(0.06f, 0.38f, 0.025f));
            Part(root, "Dagger Tip", ProceduralMeshLibrary.Cone, Steel,
                new Vector3(0f, 0.53f, 0f), new Vector3(0.06f, 0.12f, 0.025f), outline: false);
        }

        private static void BuildAxe(Transform root)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, -8f);
            Part(root, "Axe Haft", ProceduralMeshLibrary.Cylinder, Wood,
                new Vector3(0f, 0.28f, 0f), new Vector3(0.065f, 1.1f, 0.065f));
            Part(root, "Axe Head", ProceduralMeshLibrary.Box, Steel,
                new Vector3(0.16f, 0.7f, 0f), new Vector3(0.44f, 0.26f, 0.09f));
            Part(root, "Axe Edge", ProceduralMeshLibrary.Cone, Steel,
                new Vector3(0.38f, 0.7f, 0f), new Vector3(0.2f, 0.26f, 0.09f));
            Part(root, "Axe Collar", ProceduralMeshLibrary.Box, Gold,
                new Vector3(0f, 0.68f, 0f), new Vector3(0.12f, 0.08f, 0.12f), outline: false);
        }

        private static void BuildBow(Transform root)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, -8f);
            Part(root, "Bow Grip", ProceduralMeshLibrary.Cylinder, Leather,
                new Vector3(0f, 0f, 0f), new Vector3(0.07f, 0.3f, 0.07f));
            Part(root, "Bow Upper", ProceduralMeshLibrary.Box, Wood,
                new Vector3(-0.1f, 0.42f, 0f), new Vector3(0.07f, 0.7f, 0.07f)).localRotation =
                Quaternion.Euler(0f, 0f, 24f);
            Part(root, "Bow Lower", ProceduralMeshLibrary.Box, Wood,
                new Vector3(-0.1f, -0.42f, 0f), new Vector3(0.07f, 0.7f, 0.07f)).localRotation =
                Quaternion.Euler(0f, 0f, -24f);
            Part(root, "Bow String", ProceduralMeshLibrary.Box, new Color(0.85f, 0.8f, 0.65f),
                new Vector3(0.18f, 0f, 0f), new Vector3(0.012f, 1.15f, 0.012f), outline: false);
        }

        private static void BuildStaff(Transform root)
        {
            root.localRotation = Quaternion.Euler(0f, 0f, -5f);
            Part(root, "Staff Haft", ProceduralMeshLibrary.Cylinder, Wood,
                new Vector3(0f, 0.35f, 0f), new Vector3(0.07f, 1.45f, 0.07f));
            Part(root, "Staff Collar", ProceduralMeshLibrary.Cylinder, Gold,
                new Vector3(0f, 0.95f, 0f), new Vector3(0.15f, 0.08f, 0.15f), outline: false);
            Part(root, "Staff Core", ProceduralMeshLibrary.Sphere, Arcane,
                new Vector3(0f, 1.18f, 0f), new Vector3(0.24f, 0.24f, 0.24f));
            Part(root, "Staff Flame", ProceduralMeshLibrary.Cone, Arcane,
                new Vector3(0f, 1.4f, 0f), new Vector3(0.2f, 0.3f, 0.2f), outline: false);
        }

        private static Transform Part(
            Transform root,
            string name,
            Mesh mesh,
            Color color,
            Vector3 position,
            Vector3 scale,
            bool outline = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ProceduralMaterials.Lit(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            if (outline)
            {
                var hull = new GameObject("Outline");
                hull.transform.SetParent(go.transform, false);
                hull.AddComponent<MeshFilter>().sharedMesh = mesh;
                var hullRenderer = hull.AddComponent<MeshRenderer>();
                hullRenderer.sharedMaterial = ProceduralMaterials.Outline;
                hullRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hullRenderer.receiveShadows = false;
            }
            return go.transform;
        }
    }
}
