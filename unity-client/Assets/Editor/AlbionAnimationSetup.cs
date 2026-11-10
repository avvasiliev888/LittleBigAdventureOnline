using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Explicit, reproducible Humanoid locomotion/combat setup; never runs on import.
    /// All clips are retargeted from the downloaded All Star Characters male library.
    /// </summary>
    public static class AlbionAnimationSetup
    {
        public const string ModelPath = "Assets/dRealWarriorFantasyCharacter/Meshes/Warrior.fbx";
        public const string ControllerPath = "Assets/Resources/Characters/WarriorLocomotion.controller";
        public const string AnimationFolder = "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/";
        private const string IdleSource = "BaseMale@1HIdle.fbx";
        private const string WalkSource = "BaseMale@1HWalkF.fbx";
        private const string RunSource = "BaseMale@1HCombatRunF.fbx";
        private const string AttackSource = "BaseMale@1HAttack.fbx";
        private const string HitSource = "BaseMale@1HPain.fbx";
        private const string DeathSource = "BaseMale@1HDeathA.fbx";

        public static RuntimeAnimatorController Configure()
        {
            var avatarPath = AnimationFolder + "BaseMale.fbx";
            var avatarImporter = (ModelImporter)AssetImporter.GetAtPath(avatarPath);
            if (avatarImporter == null) throw new InvalidOperationException("Missing source Avatar: " + avatarPath);
            avatarImporter.animationType = ModelImporterAnimationType.Human;
            avatarImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            avatarImporter.importAnimation = false;
            avatarImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            avatarImporter.SaveAndReimport();
            var sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(avatarPath).OfType<Avatar>().FirstOrDefault();
            if (sourceAvatar == null || !sourceAvatar.isValid || !sourceAvatar.isHuman)
                throw new InvalidOperationException("Invalid source Humanoid Avatar.");
            foreach (var source in new[] { IdleSource, WalkSource, RunSource, AttackSource, HitSource, DeathSource })
            {
                var loop = source == IdleSource || source == WalkSource || source == RunSource;
                ConfigureImporter(AnimationFolder + source, sourceAvatar, loop);
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            EnsureParameters(controller);
            BuildStates(controller);
            AssetDatabase.SaveAssets();
            Inventory();
            return controller;
        }

        private static void ConfigureImporter(string path, Avatar sourceAvatar, bool loop)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing downloaded animation source: " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = sourceAvatar;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.clipAnimations;
            if (clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = loop;
                clip.loopPose = loop;
                // Bake rotation and vertical bob into pose; extract XZ translation but never apply it.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = false;
                clip.keepOriginalOrientation = false;
                clip.keepOriginalPositionY = false;
                clip.keepOriginalPositionXZ = false;
                clip.heightFromFeet = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static void EnsureParameters(AnimatorController controller)
        {
            var names = controller.parameters.Select(parameter => parameter.name).ToArray();
            if (!names.Contains("Speed")) controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            if (!names.Contains("Attack")) controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            if (!names.Contains("Hit")) controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            if (!names.Contains("Dead")) controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        }

        private static void BuildStates(AnimatorController controller)
        {
            var machine = controller.layers[0].stateMachine;
            foreach (var state in machine.states.Select(item => item.state).ToArray()) machine.RemoveState(state);
            foreach (var transition in machine.anyStateTransitions.ToArray()) machine.RemoveAnyStateTransition(transition);

            var idle = machine.AddState("Idle");
            idle.motion = Clip(IdleSource);
            var locomotion = machine.AddState("Locomotion");
            var blend = AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>()
                .FirstOrDefault(tree => tree.name == "Walk Run (metres per second)");
            if (blend == null)
            {
                blend = new BlendTree { name = "Walk Run (metres per second)", blendType = BlendTreeType.Simple1D,
                    blendParameter = "Speed", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(blend, controller);
            }
            blend.blendType = BlendTreeType.Simple1D;
            blend.blendParameter = "Speed";
            blend.useAutomaticThresholds = false;
            var walkClip = Clip(WalkSource);
            var runClip = Clip(RunSource);
            blend.children = new[]
            {
                new ChildMotion { motion = walkClip, threshold = 1.5f, timeScale = 1.5f / Mathf.Max(0.1f, walkClip.averageSpeed.magnitude) },
                new ChildMotion { motion = runClip, threshold = 4f, timeScale = 4f / Mathf.Max(0.1f, runClip.averageSpeed.magnitude) },
            };
            locomotion.motion = blend;
            var attack = machine.AddState("Attack");
            attack.motion = Clip(AttackSource);
            var hit = machine.AddState("Hit");
            hit.motion = Clip(HitSource);
            var dead = machine.AddState("Dead");
            dead.motion = Clip(DeathSource);
            machine.defaultState = idle;

            Transition(idle, locomotion, false, 0f, 0.1f, ("Speed", AnimatorConditionMode.Greater, 0.1f), ("Dead", AnimatorConditionMode.IfNot, 0f));
            Transition(locomotion, idle, false, 0f, 0.1f, ("Speed", AnimatorConditionMode.Less, 0.100001f), ("Dead", AnimatorConditionMode.IfNot, 0f));

            // Attack/Hit/Dead are explicit presentation states driven by WarriorVisual. They have
            // no automatic outgoing transitions: the driver returns to Idle/Locomotion only after
            // the one-shot reaches its exit phase, so a render frame cannot cancel a swing early.
            EditorUtility.SetDirty(controller);
        }

        private static void Transition(AnimatorState from, AnimatorState to, bool exitTime, float exit,
            float duration, params (string Name, AnimatorConditionMode Mode, float Threshold)[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = exitTime;
            transition.exitTime = exit;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            foreach (var condition in conditions)
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Name);
        }

        private static AnimationClip Clip(string source)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(AnimationFolder + source).OfType<AnimationClip>()
                .FirstOrDefault(item => !item.name.StartsWith("__preview", StringComparison.Ordinal));
            if (clip == null || !clip.humanMotion)
                throw new InvalidOperationException("Expected Humanoid animation: " + source);
            return clip;
        }

        public static void Inventory()
        {
            var paths = new[] { ModelPath, IdleSource, WalkSource, RunSource, AttackSource, HitSource, DeathSource }
                .Select(source => source == ModelPath ? source : AnimationFolder + source);
            foreach (var path in paths)
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Debug.Log("Warrior animation importer: " + path + ", type=" + importer.animationType + ", import=" + importer.importAnimation);
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    Debug.Log("Warrior clip: " + clip.name + ", length=" + clip.length + ", human=" + clip.humanMotion
                        + ", loop=" + clip.isLooping + ", averageSpeed=" + clip.averageSpeed);
                foreach (var avatar in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>())
                    Debug.Log("Warrior avatar: " + avatar.name + ", valid=" + avatar.isValid + ", human=" + avatar.isHuman);
            }
        }
    }
}
