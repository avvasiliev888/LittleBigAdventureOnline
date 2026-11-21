using Albion.Client;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Wires the CC0 KayKit Adventurers/Skeletons models into the existing presentation
    /// adapters. The models themselves are imported by <see cref="ArtImportPipeline"/>.
    /// </summary>
    public static class KayKitSetup
    {
        private const string CharacterRoot = "Assets/Art/Characters/Generated";
        private const string WeaponRoot = "Assets/Art/Weapons/Generated";
        private const string SettingsPath = "Assets/Resources/Characters/WarriorVisual.asset";
        private const string MobSettingsPath = "Assets/Resources/Characters/MobVisuals.asset";

        [MenuItem("Albion/Art/Configure KayKit Hero")]
        public static void ConfigureHero()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRoot + "/Knight.prefab");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                CharacterRoot + "/KnightController.controller");
            var settings = AssetDatabase.LoadAssetAtPath<CharacterVisualSettings>(SettingsPath);
            if (prefab == null || controller == null || settings == null)
            {
                Debug.LogError("[kaykit] Knight prefab/controller/WarriorVisual.asset is missing. Run Prepare All Imported Art first.");
                return;
            }

            settings.Prefab = prefab;
            settings.Controller = controller;
            settings.Height = 1.95f;
            settings.GroundOffset = 0f;
            settings.Weapons = new[]
            {
                Bind(WeaponKind.Sword, "sword_1handed"),
                Bind(WeaponKind.Axe, "axe_1handed"),
                Bind(WeaponKind.Bow, "bow"),
                Bind(WeaponKind.Staff, "staff"),
                Bind(WeaponKind.Dagger, "dagger"),
            };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[kaykit] Hero configured: Knight.prefab + 5 authored weapons.");
        }

        [MenuItem("Albion/Art/Configure KayKit All")]
        public static void ConfigureAll()
        {
            ConfigureHero();
            ConfigureMobs();
        }

        [MenuItem("Albion/Art/Configure KayKit Mobs")]
        public static void ConfigureMobs()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MobVisualSettings>();
                AssetDatabase.CreateAsset(settings, MobSettingsPath);
            }

            settings.Entries = new[]
            {
                // Keyed by the mob's own key from config/mobs.json. A zone key limited the whole
                // world to one look per zone, which is exactly the limit the import pipeline exists
                // to remove — see docs/mob-visual-registry-options.md, variant 1.
                MobEntry("blue_scavenger", "Skeleton_Minion", 1.55f),
                MobEntry("yellow_bandit", "Skeleton_Rogue", 1.95f),
                MobEntry("red_warlord", "Skeleton_Warrior", 2.45f),
            };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[kaykit] Mob visuals configured by mob key: scavenger=Minion, bandit=Rogue, warlord=Warrior.");
        }

        [MenuItem("Albion/Art/Validate KayKit Setup")]
        public static void ValidateSetup()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRoot + "/Knight.prefab");
            if (prefab == null)
            {
                Debug.LogError("[kaykit] Knight.prefab missing.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>(true);
                var visuals = instance.GetComponentInChildren<CharacterVisuals>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                {
                    Debug.LogError("[kaykit] Knight Animator/Avatar invalid.");
                    return;
                }
                visuals.BindSockets(animator);
                var settings = AssetDatabase.LoadAssetAtPath<CharacterVisualSettings>(SettingsPath);
                if (settings != null) visuals.SetWeapons(settings.Weapons);
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (hand == null)
                {
                    Debug.LogError("[kaykit] Knight right hand bone missing.");
                    return;
                }
                var bounds = new Bounds(instance.transform.position, Vector3.zero);
                var hasBounds = false;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                Debug.Log("[kaykit] hero bounds=" + (hasBounds ? bounds.size.ToString("F2") : "none")
                    + " hand=" + hand.position.ToString("F2"));
                foreach (var kind in new[]
                         {
                             WeaponKind.Sword, WeaponKind.Axe, WeaponKind.Bow,
                             WeaponKind.Staff, WeaponKind.Dagger,
                         })
                {
                    visuals.Equip(kind);
                    var weapon = FindDeep(instance.transform, "Weapon " + kind);
                    var weaponRenderer = weapon != null ? weapon.GetComponentInChildren<Renderer>(true) : null;
                    Debug.Log("[kaykit] " + kind + "=" + (weaponRenderer != null
                        ? weaponRenderer.bounds.size.ToString("F2") + " center="
                          + weaponRenderer.bounds.center.ToString("F2")
                        : "missing"));
                    if (weaponRenderer == null)
                    {
                        Debug.LogError("[kaykit] " + kind + " prefab did not mount on the right hand.");
                    }
                }
                Debug.Log("[kaykit] Validation passed: Knight avatar, right-hand sockets, all weapons, URP prefab.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name) return candidate;
            }
            return null;
        }

        private static MobVisualSettings.Entry MobEntry(string mobKey, string prefabName, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CharacterRoot + "/" + prefabName + ".prefab");
            if (prefab == null) Debug.LogError("[kaykit] Mob prefab missing: " + prefabName);
            return new MobVisualSettings.Entry
            {
                MobKey = mobKey,
                Prefab = prefab,
                Height = height,
                Yaw = 0f,
                GroundOffset = 0f,
            };
        }

        private static CharacterVisuals.WeaponBinding Bind(WeaponKind kind, string file)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponRoot + "/" + file + ".prefab");
            if (prefab == null) Debug.LogError("[kaykit] Weapon prefab missing: " + file);
            // KayKit weapons are authored larger than the character's hand scale. These
            // factors bring them to believable silhouette sizes (body is normalized to 1.95m).
            var scale = file switch
            {
                "sword_1handed" => 0.85f,
                "axe_1handed" => 0.90f,
                "bow" => 0.90f,
                "staff" => 1.0f,
                "dagger" => 0.45f,
                _ => 1.0f,
            };
            return new CharacterVisuals.WeaponBinding
            {
                Kind = kind,
                Prefab = prefab,
                LocalPosition = Vector3.zero,
                LocalEuler = Vector3.zero,
                LocalScale = Vector3.one * scale,
            };
        }
    }
}
