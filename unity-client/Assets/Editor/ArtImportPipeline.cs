using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Albion.Client;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Albion.Editor
{
    /// <summary>
    /// Import pipeline for professional FBX characters/weapons/environment:
    /// Humanoid avatar setup, URP Lit material conversion, socket binding and prefab wrappers.
    /// It never creates meshes or textures; everything visible comes from the artist's FBX.
    /// </summary>
    public static class ArtImportPipeline
    {
        public const string ArtRoot = "Assets/Art";
        public const string CharactersRoot = ArtRoot + "/Characters";
        public const string WeaponsRoot = ArtRoot + "/Weapons";
        public const string EnvironmentRoot = ArtRoot + "/Environment";

        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static readonly HashSet<string> Extracted = new HashSet<string>();

        [MenuItem("Albion/Art/Prepare All Imported Art")]
        public static void PrepareAll()
        {
            EnsureFolders();
            var changed = 0;
            foreach (var path in FindModels())
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                ConfigureImporter(importer, path);
                importer.SaveAndReimport();
                PrepareModel(path);
                changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[art] Prepared " + changed + " imported model(s).");
        }

        [MenuItem("Albion/Art/Rebuild KayKit Character Prefabs")]
        public static void RebuildKayKitCharacters()
        {
            var models = new[]
            {
                CharactersRoot + "/KayKit/Knight.fbx",
                CharactersRoot + "/KayKit/Skeleton_Minion.fbx",
                CharactersRoot + "/KayKit/Skeleton_Rogue.fbx",
                CharactersRoot + "/KayKit/Skeleton_Warrior.fbx",
            };
            foreach (var path in models)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    Debug.LogError("[art] KayKit model missing: " + path);
                    continue;
                }
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null)
                {
                    ConfigureImporter(importer, path);
                    importer.SaveAndReimport();
                }
                PrepareModel(path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[art] Rebuilt KayKit character prefabs and controllers.");
        }

        [MenuItem("Albion/Art/Create Locomotion Controller From Selected Model")]
        public static void CreateControllerFromSelection()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(CharactersRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Albion Art",
                    "Select an FBX inside Assets/Art/Characters first.", "OK");
                return;
            }
            var controller = CreateController(path);
            if (controller != null) Selection.activeObject = controller;
        }

        [MenuItem("Albion/Art/Validate Imported Art")]
        public static void Validate()
        {
            EnsureFolders();
            var problems = 0;
            foreach (var prefabPath in FindGeneratedPrefabs())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;
                var isCharacter = prefabPath.StartsWith(CharactersRoot + "/Generated/",
                    StringComparison.OrdinalIgnoreCase);
                if (isCharacter)
                {
                    var animator = prefab.GetComponentInChildren<Animator>(true);
                    if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                    {
                        Debug.LogError("[art] " + prefabPath + ": Animator/Avatar missing or invalid.", prefab);
                        problems++;
                    }
                    else if (animator.runtimeAnimatorController == null)
                    {
                        Debug.LogError("[art] " + prefabPath + ": RuntimeAnimatorController is missing.", prefab);
                        problems++;
                    }
                    var visuals = prefab.GetComponentInChildren<CharacterVisuals>(true);
                    if (visuals == null)
                    {
                        Debug.LogError("[art] " + prefabPath + ": CharacterVisuals missing.", prefab);
                        problems++;
                    }
                    else
                    {
                        visuals.BindSockets(animator);
                        if (visuals.RightHandSocket == null)
                        {
                            Debug.LogError("[art] " + prefabPath + ": right-hand socket/bone missing.", prefab);
                            problems++;
                        }
                    }
                }
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material == null || material.shader == null
                            || material.shader.name != "Universal Render Pipeline/Lit")
                        {
                            Debug.LogError("[art] " + prefabPath + ": material is not URP Lit.", prefab);
                            problems++;
                        }
                        else if (material.name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) < 0
                                 && material.GetTexture("_BaseMap") == null)
                        {
                            Debug.LogError("[art] " + prefabPath + ": albedo texture is missing.", prefab);
                            problems++;
                        }
                    }
                }
            }
            Debug.Log(problems == 0
                ? "[art] Validation passed: avatars/CharacterVisuals, URP Lit and albedo textures are present."
                : "[art] Validation found " + problems + " problem(s).");
        }

        internal static void ConfigureImporter(ModelImporter importer, string path)
        {
            var isCharacter = path.StartsWith(CharactersRoot + "/", StringComparison.OrdinalIgnoreCase);
            var isEnvironment = path.StartsWith(EnvironmentRoot + "/", StringComparison.OrdinalIgnoreCase);

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importConstraints = false;
            importer.importVisibility = true;
            importer.importBlendShapes = isCharacter;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;

            if (isCharacter)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.animationCompression = ModelImporterAnimationCompression.Optimal;
                importer.animationRotationError = 0.5f;
                importer.animationPositionError = 0.5f;
                importer.animationScaleError = 0.5f;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }

            if (isEnvironment)
            {
                importer.importBlendShapes = false;
                importer.optimizeMeshPolygons = true;
                importer.optimizeMeshVertices = true;
            }

            if (isCharacter)
            {
                // KayKit takes are authored as one-shot clips by the FBX importer. Without an
                // explicit loop flag Animator plays Walking_A/Running_A once (~1 s) and then
                // holds the last pose while the root keeps moving: exactly the "steps then
                // glide" bug. Idle/locomotion must loop; attack/hit/death must not.
                var takes = importer.clipAnimations.Length > 0
                    ? importer.clipAnimations
                    : importer.defaultClipAnimations;
                if (takes.Length > 0)
                {
                    var changed = false;
                    foreach (var take in takes)
                    {
                        var canonical = CanonicalState(take.name);
                        var loop = canonical == "Idle" || canonical == "Walk" || canonical == "Run";
                        if (take.loopTime != loop)
                        {
                            take.loopTime = loop;
                            changed = true;
                        }
                    }
                    if (changed) importer.clipAnimations = takes;
                }
            }
        }

        internal static void Schedule(string path)
        {
            if (!Pending.Add(path)) return;
            EditorApplication.delayCall += () =>
            {
                Pending.Remove(path);
                try
                {
                    PrepareModel(path);
                }
                catch (Exception exception)
                {
                    Debug.LogError("[art] Failed to prepare " + path + ": " + exception.Message);
                }
            };
        }

        /// <summary>Animation FBX has no body prefab; refresh the body once all clips are imported.</summary>
        internal static void ScheduleFolder(string animationPath)
        {
            var folder = (Path.GetDirectoryName(animationPath) ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) return;
            foreach (var path in FindModels())
            {
                var candidateFolder = (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');
                if (string.Equals(candidateFolder, folder, StringComparison.OrdinalIgnoreCase)
                    && !IsAnimationOnly(path))
                    Schedule(path);
            }
        }

        internal static void PrepareModel(string path)
        {
            if (!IsModel(path) || IsAnimationOnly(path)) return;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) return;

            var category = CategoryFolder(path);
            var generatedFolder = category + "/Generated";
            ArtMaterialUtility.EnsureFolder(generatedFolder);
            var modelFolder = category + "/" + Sanitize(Path.GetFileNameWithoutExtension(path));
            var materialFolder = modelFolder + "/Materials";
            var textureFolder = modelFolder + "/Textures";

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = Path.GetFileNameWithoutExtension(path);
                TryExtractEmbeddedTextures(path, textureFolder);
                ArtMaterialUtility.AssignFallbackAlbedo(instance, ArtRoot);
                ArtMaterialUtility.ConvertRenderers(instance, materialFolder);
                StripEmbeddedEquipment(instance);

                var animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null && category == CharactersRoot)
                {
                    AnimatorController controller = null;
                    if (animator.runtimeAnimatorController == null
                        || IsGeneratedController(animator.runtimeAnimatorController))
                    {
                        controller = CreateController(path);
                        if (controller != null) animator.runtimeAnimatorController = controller;
                    }
                    animator.applyRootMotion = false;
                    animator.enabled = true;
                    var visuals = instance.GetComponent<CharacterVisuals>()
                        ?? instance.GetComponentInChildren<CharacterVisuals>(true)
                        ?? instance.AddComponent<CharacterVisuals>();
                    visuals.BindSockets(animator);
                    if (controller != null) ApplyStateNames(visuals, controller);
                }

                // InstantiatePrefab keeps a nested FBX prefab root. Unity 6 then stores the
                // generated asset as a Prefab object, which cannot be assigned to a GameObject
                // field (CharacterVisualSettings.Prefab). Unpack to a real GameObject root.
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance,
                        PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                var prefabPath = generatedFolder + "/" + Sanitize(instance.name) + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                    AssetDatabase.DeleteAsset(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                Debug.Log("[art] Prepared prefab: " + prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// KayKit bodies ship with several sword and shield meshes baked into the FBX. They are
        /// not equipment slots: the runtime mounts authored weapon prefabs on hand sockets. Delete
        /// the baked copies so one snapshot weapon can never render as three swords plus a shield.
        /// </summary>
        private static void StripEmbeddedEquipment(GameObject instance)
        {
            var removed = 0;
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform == instance.transform
                    || transform.GetComponent<Renderer>() == null) continue;
                var name = transform.name.ToLowerInvariant();
                if (!(name.Contains("sword") || name.Contains("shield") || name.Contains("axe")
                      || name.Contains("staff") || name.Contains("crossbow") || name.Contains("dagger")))
                    continue;
                Object.DestroyImmediate(transform.gameObject);
                removed++;
            }
            if (removed > 0)
                Debug.Log("[art] Removed " + removed + " baked weapon/shield meshes from " + instance.name + ".");
        }

        private static void TryExtractEmbeddedTextures(string path, string textureFolder)
        {
            if (!Extracted.Add(path)) return;
            ArtMaterialUtility.EnsureFolder(textureFolder);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            try
            {
                importer.ExtractTextures(textureFolder);
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[art] No embedded textures extracted from " + path + ": " + exception.Message);
            }
        }

        private static bool IsGeneratedController(RuntimeAnimatorController controller)
        {
            var path = AssetDatabase.GetAssetPath(controller);
            return !string.IsNullOrEmpty(path)
                && path.StartsWith(CharactersRoot + "/Generated/", StringComparison.OrdinalIgnoreCase);
        }

        private static AnimatorController CreateController(string characterPath)
        {
            // Only this model's own clips belong to its controller. Several KayKit characters
            // share one folder; scanning the folder would put skeleton clips on the knight and
            // produce an Animator/avatar mismatch.
            var clips = new List<AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(characterPath))
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    clips.Add(clip);
            }
            if (clips.Count == 0) return null;

            var controllerFolder = CharactersRoot + "/Generated";
            ArtMaterialUtility.EnsureFolder(controllerFolder);
            var controllerPath = controllerFolder + "/" + Sanitize(Path.GetFileNameWithoutExtension(characterPath)) + "Controller.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
                AssetDatabase.DeleteAsset(controllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            // The base layer defaults to weight 0 when a controller is created through the API.
            // At weight 0 Unity evaluates the state machine but writes no pose, which looks like
            // a character gliding in bind pose. Force the authored layer weight to 1.
            var layers = controller.layers;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[0].stateMachine;

            // One state per canonical slot. CharacterVisuals still drives one-shots directly;
            // the graph below is the parameter-driven fallback (and documents exit timing).
            var order = new[] { "Idle", "Walk", "Run", "Attack", "Hit", "Dead" };
            var chosen = new Dictionary<string, AnimationClip>();
            foreach (var clip in clips)
            {
                var stateName = CanonicalState(clip.name);
                if (Array.IndexOf(order, stateName) < 0) continue;
                if (!chosen.TryGetValue(stateName, out var current)
                    || IsBetterClip(stateName, clip.name, current.name))
                {
                    chosen[stateName] = clip;
                }
            }

            var states = new Dictionary<string, AnimatorState>();
            foreach (var stateName in order)
            {
                if (!chosen.TryGetValue(stateName, out var clip)) continue;
                // Name the state after the authored clip (Walking_A, 1H_Melee_Attack_Chop),
                // not the internal canonical slot. This is what the acceptance test observes.
                var state = machine.AddState(clip.name);
                state.motion = clip;
                states[stateName] = state;
            }
            if (states.TryGetValue("Idle", out var idle)) machine.defaultState = idle;

            AddLocomotion(machine, states, "Idle", "Walk", 0.1f, true);
            AddLocomotion(machine, states, "Walk", "Idle", 0.1f, false);
            AddLocomotion(machine, states, "Walk", "Run", 3.2f, true);
            AddLocomotion(machine, states, "Run", "Walk", 3.2f, false);
            AddLocomotion(machine, states, "Run", "Idle", 0.1f, false);
            // CharacterVisuals starts Attack/Hit with a deterministic Animator.Play. Keeping
            // AnyState -> Hit here lets a pending Hit trigger override a mob attack in the same
            // server tick (observed as MOB ATTACK state=Hit_A), so the trigger graph is not
            // used for those two one-shots. Dead stays parameter-driven.
            AddAnyStateBool(machine, states, "Dead", "Dead");
            AddExitToIdle(states, "Attack", 0.9f);
            AddExitToIdle(states, "Hit", 0.8f);

            // Extra one-shot states for the per-weapon attack variants. CharacterVisuals picks a
            // clip with Animator.Play, so the states need no AnyState transition; the exit to
            // Idle keeps the graph valid for manual preview and parameter-driven fallback.
            var extraAttacks = new[]
            {
                "1H_Melee_Attack_Slice_Diagonal",
                "1H_Melee_Attack_Slice_Horizontal",
                "1H_Melee_Attack_Stab",
                "2H_Melee_Attack_Spin",
                "1H_Ranged_Shoot",
                "1H_Ranged_Shooting",
                "Spellcast_Shoot",
                "Spellcast_Long",
            };
            foreach (var clip in clips)
            {
                if (Array.IndexOf(extraAttacks, clip.name) < 0) continue;
                if (states.ContainsKey(clip.name)) continue;
                var extra = machine.AddState(clip.name);
                extra.motion = clip;
                AddExitToIdle(machine, extra, 0.9f);
            }

            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void AddLocomotion(AnimatorStateMachine machine,
            IReadOnlyDictionary<string, AnimatorState> states,
            string from, string to, float threshold, bool greater)
        {
            if (!states.TryGetValue(from, out var source) || !states.TryGetValue(to, out var target)) return;
            var transition = source.AddTransition(target);
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.duration = 0.1f;
            transition.hasFixedDuration = true;
            transition.AddCondition(greater ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
                threshold, "Speed");
        }

        private static void AddAnyStateTrigger(AnimatorStateMachine machine,
            IReadOnlyDictionary<string, AnimatorState> states, string state, string parameter, float duration)
        {
            if (!states.TryGetValue(state, out var target)) return;
            var transition = machine.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.duration = duration;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
        }

        private static void AddAnyStateBool(AnimatorStateMachine machine,
            IReadOnlyDictionary<string, AnimatorState> states, string state, string parameter)
        {
            if (!states.TryGetValue(state, out var target)) return;
            var transition = machine.AddAnyStateTransition(target);
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.duration = 0.08f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
        }

        private static void AddExitToIdle(IReadOnlyDictionary<string, AnimatorState> states,
            string from, float exitTime)
        {
            if (!states.TryGetValue(from, out var source) || !states.TryGetValue("Idle", out var idle)) return;
            AddExitToState(source, idle, exitTime);
        }

        private static void AddExitToIdle(AnimatorStateMachine machine, AnimatorState source, float exitTime)
        {
            AnimatorState idle = null;
            foreach (var child in machine.states)
            {
                if (child.state.name == "Idle")
                {
                    idle = child.state;
                    break;
                }
            }
            if (idle != null) AddExitToState(source, idle, exitTime);
        }

        private static void AddExitToState(AnimatorState source, AnimatorState idle, float exitTime)
        {
            var transition = source.AddTransition(idle);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = 0.08f;
            transition.hasFixedDuration = true;
        }

        private static void ApplyStateNames(CharacterVisuals visuals, AnimatorController controller)
        {
            var names = new Dictionary<string, string>();
            foreach (var state in controller.layers[0].stateMachine.states)
            {
                var canonical = CanonicalState(state.state.name);
                // Keep the primary Attack state (1H_Melee_Attack_Chop) as the default; the
                // additional attack variants are selected by CharacterVisuals directly.
                if (!string.IsNullOrEmpty(canonical) && !names.ContainsKey(canonical))
                    names[canonical] = state.state.name;
            }
            visuals.SetStateNames(
                names.TryGetValue("Idle", out var idle) ? idle : null,
                names.TryGetValue("Walk", out var walk) ? walk : null,
                names.TryGetValue("Run", out var run) ? run : null,
                names.TryGetValue("Attack", out var attack) ? attack : null,
                names.TryGetValue("Hit", out var hit) ? hit : null,
                names.TryGetValue("Dead", out var dead) ? dead : null);
        }

        private static string CanonicalState(string clipName)
        {
            var name = clipName.ToLowerInvariant();
            if (name.Contains("idle")) return "Idle";
            if (name.Contains("run")) return "Run";
            if (name.Contains("walk")) return "Walk";
            if (name.Contains("attack") || name.Contains("slash") || name.Contains("swing")) return "Attack";
            if (name.Contains("pain") || name.Contains("hit") || name.Contains("damage")) return "Hit";
            if (name.Contains("death") || name.Contains("die")) return "Dead";
            return string.Empty;
        }

        /// <summary>Prefer the canonical simple clip over sit/lie/strafe/2H variants.</summary>
        private static bool IsBetterClip(string stateName, string candidate, string current)
        {
            return ScoreClip(stateName, candidate) > ScoreClip(stateName, current);
        }

        private static int ScoreClip(string stateName, string clipName)
        {
            var name = (clipName ?? string.Empty).ToLowerInvariant();
            switch (stateName)
            {
                case "Idle":
                    if (name == "idle") return 100;
                    if (name == "unarmed_idle") return 90;
                    if (name.EndsWith("_idle") && name.Contains("melee")) return 70;
                    if (name.Contains("idle")) return 50;
                    return 0;
                case "Walk":
                    if (name == "walking_a" || name == "walk") return 100;
                    if (name.StartsWith("walking") && !name.Contains("back")) return 70;
                    if (name.Contains("walk")) return 40;
                    return 0;
                case "Run":
                    if (name == "running_a" || name == "run") return 100;
                    if (name.StartsWith("running") && !name.Contains("strafe")) return 70;
                    if (name.Contains("run") && !name.Contains("strafe")) return 40;
                    return 0;
                case "Attack":
                    if (name == "1h_melee_attack_chop") return 100;
                    if (name.StartsWith("1h_melee_attack")) return 80;
                    if (name.Contains("attack")) return 40;
                    return 0;
                case "Hit":
                    if (name == "hit_a") return 100;
                    if (name.StartsWith("hit_")) return 70;
                    if (name.Contains("hit")) return 30;
                    return 0;
                case "Dead":
                    if (name == "death_a") return 100;
                    if (name.StartsWith("death_") && !name.Contains("pose")) return 70;
                    if (name.Contains("death")) return 30;
                    return 0;
                default:
                    return 0;
            }
        }

        private static IEnumerable<string> FindModels()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot)) return Array.Empty<string>();
            return AssetDatabase.FindAssets("t:Model", new[] { ArtRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsModel)
                .Distinct();
        }

        private static IEnumerable<string> FindGeneratedPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot)) return Array.Empty<string>();
            return Directory.GetFiles(ArtRoot, "*.prefab", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => path.Contains("/Generated/", StringComparison.OrdinalIgnoreCase));
        }

        private static string CategoryFolder(string path)
        {
            if (path.StartsWith(WeaponsRoot + "/", StringComparison.OrdinalIgnoreCase)) return WeaponsRoot;
            if (path.StartsWith(EnvironmentRoot + "/", StringComparison.OrdinalIgnoreCase)) return EnvironmentRoot;
            return CharactersRoot;
        }

        private static bool IsModel(string path)
        {
            return path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAnimationOnly(string path)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            return file.Contains("@", StringComparison.Ordinal)
                || path.IndexOf("/Animations/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Sanitize(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (var character in name)
            {
                builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character : '_');
            }
            return builder.Length == 0 ? "Imported" : builder.ToString();
        }

        private static void EnsureFolders()
        {
            ArtMaterialUtility.EnsureFolder(CharactersRoot);
            ArtMaterialUtility.EnsureFolder(WeaponsRoot);
            ArtMaterialUtility.EnsureFolder(EnvironmentRoot);
            ArtMaterialUtility.EnsureFolder(CharactersRoot + "/Generated");
            ArtMaterialUtility.EnsureFolder(WeaponsRoot + "/Generated");
        }
    }

    internal sealed class ArtModelPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtImportPipeline.ArtRoot + "/", StringComparison.OrdinalIgnoreCase)) return;
            var importer = assetImporter as ModelImporter;
            if (importer != null) ArtImportPipeline.ConfigureImporter(importer, assetPath);
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ArtImportPipeline.ArtRoot + "/", StringComparison.OrdinalIgnoreCase)) return;
            if (ArtImportPipeline.IsAnimationOnly(assetPath)) ArtImportPipeline.ScheduleFolder(assetPath);
            else ArtImportPipeline.Schedule(assetPath);
        }
    }

    internal sealed class ArtTexturePostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtImportPipeline.ArtRoot + "/", StringComparison.OrdinalIgnoreCase)) return;
            var importer = assetImporter as TextureImporter;
            if (importer != null) ArtMaterialUtility.ConfigureTexture(importer, assetPath);
        }
    }
}
