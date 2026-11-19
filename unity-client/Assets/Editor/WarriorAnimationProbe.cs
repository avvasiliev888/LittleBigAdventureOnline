using System.Text;
using Albion.Client;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Answers, in ONE run, every question the warrior animation bugs depend on:
    /// which clips actually exist inside each FBX, what the importer thinks it configured,
    /// which controller states have no motion, and what the prefab's avatar binds to.
    /// Read the log, then act. No guessing, no rebuild-per-hypothesis.
    /// </summary>
    public static class WarriorAnimationProbe
    {
        private const string AnimDir = "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/";
        private const string ControllerPath = "Assets/Resources/Characters/WarriorLocomotion.controller";
        private const string SettingsPath = "Assets/Resources/Characters/WarriorVisual.asset";
        private const string PrefabPath = "Assets/dRealWarriorFantasyCharacter/Prefabs/Warrior.prefab";

        [MenuItem("Albion/Characters/Probe Warrior Animation")]
        public static void Probe()
        {
            var r = new StringBuilder();
            r.AppendLine("########## PROBE START ##########");
            ProbeFbxClips(r);
            ProbeImporterConfig(r);
            ProbeController(r);
            ProbePrefab(r);
            ProbeSettings(r);
            r.AppendLine("########## PROBE END ##########");
            Debug.Log(r.ToString());
        }

        /// <summary>What clips live inside every FBX the graph references.</summary>
        private static void ProbeFbxClips(StringBuilder r)
        {
            r.AppendLine("=== 1. CLIPS INSIDE EACH FBX ===");
            var files = new[]
            {
                "BaseMale", "BaseMale@1HAttack", "BaseMale@1HDeathA", "BaseMale@1HPain",
                "BaseMale@1HIdle", "BaseMale@1HWalkF", "BaseMale@1HCombatRunF",
                "BaseMale@1HCombatIdle", "BaseMale@1HDeathB", "BaseMale@1HDeathC",
            };
            foreach (var file in files)
            {
                var path = AnimDir + file + ".fbx";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    r.AppendLine($"  {file}: FILE MISSING at {path}");
                    continue;
                }

                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var clips = 0;
                var avatar = "none";
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        clips++;
                        r.AppendLine($"  {file}: clip '{clip.name}' len={clip.length:0.###} " +
                                     $"human={clip.isHumanMotion} empty={clip.empty}");
                    }
                    else if (asset is Avatar a)
                    {
                        avatar = $"{a.name}(valid={a.isValid},human={a.isHuman})";
                    }
                }
                if (clips == 0) r.AppendLine($"  {file}: *** NO CLIPS IMPORTED *** avatar={avatar}");
            }

            var standalone = new[]
            {
                "Assets/AllStarCharacterLibrary/Models/Standing Aim Idle 01.fbx",
                "Assets/AllStarCharacterLibrary/Models/Standing Aim Recoil.fbx",
                "Assets/AllStarCharacterLibrary/Models/Standing Idle 01.fbx",
            };
            foreach (var path in standalone)
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                var found = 0;
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        found++;
                        r.AppendLine($"  {System.IO.Path.GetFileName(path)}: clip '{clip.name}' " +
                                     $"len={clip.length:0.###} human={clip.isHumanMotion}");
                    }
                }
                if (found == 0) r.AppendLine($"  {System.IO.Path.GetFileName(path)}: *** NO CLIPS ***");
            }
        }

        /// <summary>What the .meta importer says it configured, read through the API.</summary>
        private static void ProbeImporterConfig(StringBuilder r)
        {
            r.AppendLine("=== 2. IMPORTER CONFIG ===");
            foreach (var file in new[] { "BaseMale@1HIdle", "BaseMale@1HDeathA", "BaseMale@1HAttack", "BaseMale@1HPain" })
            {
                var path = AnimDir + file + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    r.AppendLine($"  {file}: no ModelImporter");
                    continue;
                }

                r.AppendLine($"  {file}: animationType={importer.animationType} " +
                             $"importAnimation={importer.importAnimation} " +
                             $"avatarSetup={importer.avatarSetup} " +
                             $"clipAnimations={importer.clipAnimations.Length} " +
                             $"defaultClipAnimations={importer.defaultClipAnimations.Length}");
                var src = importer.sourceAvatar;
                r.AppendLine($"      sourceAvatar={(src == null ? "NULL" : src.name)}");
                foreach (var c in importer.defaultClipAnimations)
                {
                    r.AppendLine($"      default take '{c.takeName}' frames {c.firstFrame}..{c.lastFrame}");
                }
            }
        }

        private static void ProbeController(StringBuilder r)
        {
            r.AppendLine("=== 3. CONTROLLER ===");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                r.AppendLine("  CONTROLLER NOT FOUND");
                return;
            }

            foreach (var layer in controller.layers)
            {
                r.AppendLine($"  layer '{layer.name}' weight={layer.defaultWeight} " +
                             $"mask={(layer.avatarMask == null ? "none" : layer.avatarMask.name)}");
                if (layer.stateMachine == null) continue;
                foreach (var child in layer.stateMachine.states)
                {
                    var state = child.state;
                    var motion = state.motion;
                    var verdict = motion == null ? "*** NO MOTION ***" : motion.GetType().Name;
                    var length = motion is AnimationClip clip ? clip.length.ToString("0.###") : "-";
                    r.AppendLine($"    '{state.name}' motion={verdict} len={length} " +
                                 $"transitions={state.transitions.Length}");
                    if (motion is AnimationClip c2)
                    {
                        r.AppendLine($"        isHuman={c2.isHumanMotion} empty={c2.empty}");
                    }
                }
            }
        }

        private static void ProbePrefab(StringBuilder r)
        {
            r.AppendLine("=== 4. PREFAB ===");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                r.AppendLine("  PREFAB NOT FOUND");
                return;
            }

            var animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                r.AppendLine("  *** NO ANIMATOR ON PREFAB ***");
            }
            else
            {
                var avatar = animator.avatar;
                r.AppendLine($"  animator={animator.name} avatar={(avatar == null ? "NULL" : avatar.name)} " +
                             $"valid={(avatar != null && avatar.isValid)} human={(avatar != null && avatar.isHuman)}");
                r.AppendLine($"  controller={(animator.runtimeAnimatorController == null ? "NULL" : animator.runtimeAnimatorController.name)}");
                if (avatar != null && avatar.isHuman)
                {
                    var lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    var rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    r.AppendLine($"  bones: hips={(hips == null ? "NULL" : hips.name)} " +
                                 $"lhand={(lh == null ? "NULL" : lh.name)} rhand={(rh == null ? "NULL" : rh.name)}");
                }
            }

            var visuals = prefab.GetComponentInChildren<CharacterVisuals>(true);
            r.AppendLine($"  CharacterVisuals={(visuals == null ? "NULL" : visuals.name)}");

            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf("Socket", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    r.AppendLine($"  socket '{t.name}' parent={(t.parent == null ? "-" : t.parent.name)}");
            }

            var skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            r.AppendLine($"  skinnedMeshes={skins.Length}");
            for (var i = 0; i < skins.Length && i < 10; i++)
            {
                r.AppendLine($"    smr[{i}] '{skins[i].name}' mesh={(skins[i].sharedMesh == null ? "NULL" : skins[i].sharedMesh.name)} " +
                             $"bounds={skins[i].localBounds.size}");
            }
        }

        private static void ProbeSettings(StringBuilder r)
        {
            r.AppendLine("=== 5. SETTINGS ASSET ===");
            var asset = AssetDatabase.LoadAssetAtPath<CharacterVisualSettings>(SettingsPath);
            if (asset == null)
            {
                r.AppendLine("  SETTINGS NOT FOUND (or not a CharacterVisualSettings)");
                return;
            }

            var so = new SerializedObject(asset);
            var prefabProp = so.FindProperty("prefab");
            r.AppendLine($"  prefab={(prefabProp == null || prefabProp.objectReferenceValue == null ? "NULL" : prefabProp.objectReferenceValue.name)}");
            var weaponProp = so.FindProperty("weapons");
            if (weaponProp != null)
            {
                r.AppendLine($"  weapons={weaponProp.arraySize}");
                for (var i = 0; i < weaponProp.arraySize; i++)
                {
                    var item = weaponProp.GetArrayElementAtIndex(i);
                    var kind = item.FindPropertyRelative("kind");
                    var p = item.FindPropertyRelative("prefab");
                    var lp = item.FindPropertyRelative("localPosition");
                    var le = item.FindPropertyRelative("localEuler");
                    r.AppendLine($"    [{i}] kind={(kind == null ? "?" : kind.enumValueIndex)} " +
                                 $"prefab={(p == null || p.objectReferenceValue == null ? "NULL" : p.objectReferenceValue.name)} " +
                                 $"pos={(lp == null ? "?" : lp.vector3Value.ToString("0.###"))} " +
                                 $"euler={(le == null ? "?" : le.vector3Value.ToString("0.###"))}");
                }
            }
            foreach (var f in new[] { "bowHandRoll", "bowHeadTurn", "height", "groundOffset" })
            {
                var prop = so.FindProperty(f);
                if (prop != null) r.AppendLine($"  {f}={prop.floatValue:0.###}");
            }
        }
    }
}
