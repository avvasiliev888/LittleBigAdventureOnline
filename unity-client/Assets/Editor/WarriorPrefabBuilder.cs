using System;
using System.IO;
using Albion.Client;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Rebuilds <c>dRealWarriorFantasyCharacter/Prefabs/Warrior.prefab</c> from its FBX source.
    ///
    /// The vendor prefab ships with only two empty objects (the hand sockets) — no mesh, no
    /// skeleton, no Animator. <see cref="CharacterVisualSettings.Create"/> picks its code path by
    /// looking for <see cref="CharacterVisuals"/> on the prefab, so a prefab without it silently
    /// falls through to <c>CreateLegacyWarrior</c>, which builds a placeholder knight out of
    /// primitives. That is why the game showed a chibi helmet instead of the imported warrior.
    ///
    /// This script rebuilds the prefab properly: instantiate the FBX, keep the vendor sockets,
    /// attach an Animator with the shared locomotion controller, and add CharacterVisuals so the
    /// runtime can take the imported path.
    /// </summary>
    public static class WarriorPrefabBuilder
    {
        private const string ModelPath = "Assets/dRealWarriorFantasyCharacter/Meshes/Warrior.fbx";
        private const string PrefabPath = AlbionCharacterSetup.PrefabPath;
        private const string ControllerPath = AlbionAnimationSetup.ControllerPath;
        private const string SettingsPath = "Assets/Resources/Characters/WarriorVisual.asset";

        [MenuItem("Albion/Characters/Rebuild Warrior Prefab From FBX")]
        public static void Rebuild()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Require(model != null, "FBX missing: " + ModelPath);

            // The sockets must survive the rebuild: the runtime mounts weapons on them and their
            // vendor offsets are calibrated, so re-deriving them from bones would move the sword.
            var sockets = CaptureSockets();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = "Warrior";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                var animator = instance.GetComponentInChildren<Animator>(true);
                Require(animator != null, "Warrior.fbx imported with no Animator. "
                    + "Check its Rig tab is Animation Type = Humanoid.");

                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                Require(controller != null, "Controller missing: " + ControllerPath
                    + ". Run Albion/Characters/Configure Imported Warrior first.");

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;

                RestoreSockets(instance.transform, animator, sockets);

                var visuals = instance.GetComponent<CharacterVisuals>()
                    ?? instance.AddComponent<CharacterVisuals>();
                visuals.BindSockets(animator);

                // The FBX root prefab nests under the GameObject we just edited; saving requires a
                // plain hierarchy the settings asset can reference as a GameObject field.
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance,
                        PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (existing != null) AssetDatabase.DeleteAsset(PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(saved != null, "Prefab did not save: " + PrefabPath);
            Verify(saved);
            PointSettingsAt(saved);

            Debug.Log("[warrior] Prefab rebuilt from FBX: " + PrefabPath);
        }

        private struct SocketSnapshot
        {
            public string Name;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public bool Found;
        }

        /// <summary>Reads the vendor socket transforms before the old prefab is replaced.</summary>
        private static SocketSnapshot[] CaptureSockets()
        {
            var names = new[] { "RightHandSocket", "LeftHandSocket" };
            var result = new SocketSnapshot[names.Length];
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            for (var i = 0; i < names.Length; i++)
            {
                result[i].Name = names[i];
                if (old == null) continue;
                foreach (var child in old.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name != names[i]) continue;
                    result[i].LocalPosition = child.localPosition;
                    result[i].LocalRotation = child.localRotation;
                    result[i].Found = true;
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Puts the sockets back on the new hand bones with their vendor offset intact. If the FXB
        /// already carries sockets, those win — they are authored against this exact skeleton.
        /// </summary>
        private static void RestoreSockets(Transform root, Animator animator, SocketSnapshot[] sockets)
        {
            var bones = new[]
            {
                HumanBodyBones.RightHand,
                HumanBodyBones.LeftHand,
            };
            var existingBonesFound = new bool[sockets.Length];

            for (var i = 0; i < sockets.Length; i++)
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == sockets[i].Name)
                    {
                        existingBonesFound[i] = true;
                        break;
                    }
                }
            }

            for (var i = 0; i < sockets.Length; i++)
            {
                if (existingBonesFound[i] || !sockets[i].Found) continue;
                var bone = animator.GetBoneTransform(bones[i]);
                if (bone == null) continue;

                var socket = new GameObject(sockets[i].Name).transform;
                socket.SetParent(bone, false);
                socket.localPosition = sockets[i].LocalPosition;
                socket.localRotation = sockets[i].LocalRotation;
                socket.localScale = Vector3.one;
            }
        }

        /// <summary>Fails loudly if the rebuild did not actually attach a body.</summary>
        private static void Verify(GameObject prefab)
        {
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Require(renderers.Length > 0, "Rebuilt prefab has no SkinnedMeshRenderer — the body mesh "
                + "did not survive. Check the FBX import settings.");

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Require(animator != null, "Rebuilt prefab has no Animator.");
            Require(animator.avatar != null && animator.avatar.isHuman,
                "Rebuilt prefab has no valid Humanoid Avatar — retargeting would not work.");
            Require(animator.runtimeAnimatorController != null,
                "Rebuilt prefab has no Animator Controller.");

            var visuals = prefab.GetComponentInChildren<CharacterVisuals>(true);
            Require(visuals != null, "Rebuilt prefab has no CharacterVisuals, so the runtime would "
                + "still fall back to the legacy placeholder.");
        }

        /// <summary>
        /// Points the settings asset at the rebuilt prefab. Without this the runtime keeps whatever
        /// it had, and the rebuild would appear to do nothing.
        /// </summary>
        private static void PointSettingsAt(GameObject prefab)
        {
            var settings = AssetDatabase.LoadAssetAtPath<CharacterVisualSettings>(SettingsPath);
            if (settings == null)
            {
                Debug.LogWarning("[warrior] Settings asset missing: " + SettingsPath
                    + ". Run Albion/Characters/Configure Imported Warrior.");
                return;
            }

            settings.Prefab = prefab;
            settings.Yaw = 0f;
            settings.Height = 1.95f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[warrior] WarriorVisual.asset now points at the rebuilt prefab.");
        }

        private static void Require(bool condition, string message)
        {
            if (condition) return;
            throw new InvalidOperationException("[warrior] " + message);
        }
    }
}
