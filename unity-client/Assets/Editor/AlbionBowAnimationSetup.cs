using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Imports the Mixamo bow clips and wires them into WarriorLocomotion.controller.
    ///
    /// Unlike the All Star set, the Mixamo exports carry an extra wrapper in the hierarchy
    /// (BaseMale(Clone)/Origin/ROOT/Hips/...), so copying an Avatar from BaseMale.fbx fails with
    /// "Transform 'Hips' for human bone 'Hips' not found". These files therefore build their own
    /// Humanoid Avatar from their own skeleton; retargeting onto the warrior still works because
    /// both sides are Humanoid and the clip stores human-muscle data, not raw bone curves.
    ///
    /// Never runs on import; explicit menu only.
    /// </summary>
    public static class AlbionBowAnimationSetup
    {
        public const string ControllerPath = AlbionAnimationSetup.ControllerPath;

        private const string AimClipPath = "Assets/AllStarCharacterLibrary/Models/Standing Aim Idle 01.fbx";
        private const string ShootClipPath = "Assets/AllStarCharacterLibrary/Models/Standing Aim Recoil.fbx";
        private const string IdleClipPath = "Assets/AllStarCharacterLibrary/Models/Standing Idle 01.fbx";

        /// <summary>Animator state names the runtime driver crossfades to.</summary>
        public const string AimState = "BowAim";
        public const string ShootState = "BowShoot";

        [MenuItem("Albion/Animation/Configure Bow Animations")]
        public static void Configure()
        {
            var aim = ImportClip(AimClipPath, loop: true, "BowAim");
            var shoot = ImportClip(ShootClipPath, loop: false, "BowShoot");
            var idle = ImportClip(IdleClipPath, loop: true, "BowIdle");

            if (aim == null || shoot == null)
            {
                throw new InvalidOperationException(
                    "Bow clips did not import as Humanoid. Check the Rig tab of:\n" + AimClipPath + "\n" + ShootClipPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new InvalidOperationException(
                    "Controller missing: " + ControllerPath
                    + ". Run Albion/Characters/Configure Imported Warrior first.");

            EnsureStates(controller, aim, shoot, idle);
            AssetDatabase.SaveAssets();
            Report(controller);
        }

        /// <summary>
        /// Imports one Mixamo FBX as a self-contained Humanoid animation source.
        /// The Avatar is generated from the file's own skeleton, which is what makes the extra
        /// wrapper nodes harmless.
        /// </summary>
        private static AnimationClip ImportClip(string path, bool loop, string clipName)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }
            if (importer == null)
            {
                Debug.LogWarning("[bow] Unimportable model: " + path);
                return null;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            // Mixamo rigs bake scale into nodes; keeping file scale avoids a 100x mismatch.
            importer.useFileScale = true;

            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            for (var i = 0; i < clips.Length; i++)
            {
                clips[i].name = i == 0 ? clipName : clipName + "_" + i;
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                // Same policy as the melee set: bake rotation and bob, keep XZ out of the pose.
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = false;
                clips[i].keepOriginalOrientation = false;
                clips[i].keepOriginalPositionY = false;
                clips[i].keepOriginalPositionXZ = false;
                clips[i].heightFromFeet = true;
                clips[i].events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>()
                .FirstOrDefault(item => item.isValid && item.isHuman);
            if (avatar == null)
            {
                Debug.LogError("[bow] " + path + " produced no valid Humanoid Avatar. "
                    + "Open the file's Rig tab and check the bone mapping.");
                return null;
            }

            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview", StringComparison.Ordinal));
        }

        private static void EnsureStates(AnimatorController controller, AnimationClip aim, AnimationClip shoot, AnimationClip idle)
        {
            if (controller.layers.Length == 0) throw new InvalidOperationException("Controller has no layers.");
            var machine = controller.layers[0].stateMachine;

            var aimState = UpsertState(machine, AimState, aim);
            var shootState = UpsertState(machine, ShootState, shoot);
            UpsertState(machine, "BowIdle", idle);

            // The runtime crossfades by state name; loops live on the clips themselves.
            aimState.writeDefaultValues = false;
            shootState.writeDefaultValues = false;
            EditorUtility.SetDirty(controller);
        }

        private static AnimatorState UpsertState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var existing = machine.states
                .Select(entry => entry.state)
                .FirstOrDefault(state => state.name == name);

            if (existing != null)
            {
                existing.motion = clip;
                return existing;
            }

            var state = machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = false;
            return state;
        }

        private static void Report(AnimatorController controller)
        {
            Debug.Log("[bow] States: " +
                string.Join(", ", controller.layers[0].stateMachine.states.Select(entry => entry.state.name)));
            foreach (var clip in controller.animationClips)
            {
                if (clip == null) continue;
                Debug.Log("[bow] Clip " + clip.name
                    + ": length=" + clip.length.ToString("0.###")
                    + ", human=" + clip.humanMotion
                    + ", loop=" + clip.isLooping);
            }
        }
    }
}
