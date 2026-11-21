using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Albion.Client;
using Shared;

namespace Albion.Editor
{
    /// <summary>
    /// Builds a mob body prefab and its animator controller from a named animation set.
    ///
    /// Why this exists: a controller used to be assembled from whatever clips happened to sit
    /// next to the model, with the attack slot scored by name and defaulting to
    /// "1H_Melee_Attack_Chop". That works for a knight and is wrong for everything else — the
    /// battle wizard would swing a staff like a sword. Here the controller is built from
    /// config/animation-sets.json, so the mob's weapon type decides the motion and a mage casts.
    ///
    /// Run:
    ///   Unity.exe -batchmode -quit -projectPath &lt;copy&gt; \
    ///     -executeMethod Albion.Editor.MobBodyImport.Run -logFile &lt;log&gt;
    /// </summary>
    public static class MobBodyImport
    {
        private const string ArtRoot = "Assets/Art";
        private const string CharactersRoot = "Assets/Art/Characters";
        private const string GeneratedFolder = CharactersRoot + "/Generated";
        /// <summary>
        /// Copied into the project, not read from config/. A relative path resolves against the
        /// Editor's working directory, which in batchmode is not the repository root — measured
        /// 21.11.2026: "Animation sets not found: config/animation-sets.json" while the file was
        /// plainly there. A project-relative path is the only one that is stable.
        /// </summary>
        private const string SetsPath = "Assets/StreamingAssets/Config/animation-sets.json";
        private const string MobVisualsPath = "Assets/Resources/Characters/MobVisuals.asset";

        /// <summary>
        /// One body, one set, one mob key. The body is copied into Assets/Art first, because the
        /// art pipeline only processes models under that root — a model left in its package folder
        /// is invisible to it and gets no prefab, no materials and no controller.
        ///
        /// PackageRoot is where the pack's authored materials live. It is needed because an FBX
        /// carries only its own embedded material, and the wizard's is a bare "lambert1" with no
        /// textures — the albedo sits in a separate .mat next to the pack. Measured 21.11.2026:
        /// without this the prefab validated as "no albedo texture".
        /// </summary>
        private static readonly (string MobKey, string SourceFbx, string PackageRoot, string BodyName, string Set, float Height)[] Jobs =
        {
            ("red_warlord", "Assets/WizardPBR/Mesh/WizardBodyMesh.fbx", "Assets/WizardPBR", "WizardBody", "magic", 2.45f),
        };

        [MenuItem("Albion/Mobs/Import Bodies From Sets")]
        public static void Run()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("########## MOB IMPORT START ##########");

            try
            {
                var sets = LoadSets();
                report.AppendLine("sets: " + string.Join(", ", sets.Keys));

                var settings = AssetDatabase.LoadAssetAtPath<MobVisualSettings>(MobVisualsPath);
                if (settings == null)
                {
                    throw new InvalidOperationException("MobVisuals.asset not found: " + MobVisualsPath);
                }

                var entries = settings.Entries != null
                    ? settings.Entries.ToList()
                    : new List<MobVisualSettings.Entry>();

                foreach (var job in Jobs)
                {
                    report.AppendLine("--- " + job.MobKey + " <- " + job.SourceFbx + " set=" + job.Set);

                    if (!sets.TryGetValue(job.Set, out var set))
                    {
                        throw new InvalidOperationException("Animation set not found: " + job.Set);
                    }

                    var artFbx = CopyIntoArt(job.SourceFbx, job.BodyName, report);
                    if (artFbx == null)
                    {
                        report.AppendLine("SKIP body not found: " + job.SourceFbx);
                        continue;
                    }

                    var clips = CollectSetClips(set, artFbx, report);
                    var prefab = BuildPrefab(artFbx, job.PackageRoot, job.BodyName, clips, job.MobKey, report);
                    if (prefab == null)
                    {
                        report.AppendLine("SKIP prefab failed for " + job.MobKey);
                        continue;
                    }

                    UpsertEntry(entries, job.MobKey, prefab, job.Height);
                    report.AppendLine("entry set: " + job.MobKey + " -> " + prefab.name + " h=" + job.Height);
                }

                settings.Entries = entries.ToArray();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                // The validator is the only thing that catches a white emission or a missing
                // albedo, and it used to be reachable only from a manual menu — which is the same
                // as not having it. Run it here, while the prefab is fresh and the failure is cheap.
                settings.ValidateMaterials();
                report.AppendLine("ValidateMaterials: OK");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                report.AppendLine("FAILED: " + exception.GetType().Name + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                report.AppendLine("########## MOB IMPORT END ##########");
                Debug.Log(report.ToString());
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                return;
            }

            report.AppendLine("########## MOB IMPORT END ##########");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Reads config/animation-sets.json. Parsed with the same MiniJson the game uses, so a
        /// malformed file fails here with the same rules instead of drifting from the runtime.
        /// </summary>
        private static Dictionary<string, Dictionary<string, object?>> LoadSets()
        {
            if (!File.Exists(SetsPath))
            {
                throw new FileNotFoundException("Animation sets not found: " + SetsPath);
            }

            var parsed = MiniJson.Parse(File.ReadAllText(SetsPath)) as Dictionary<string, object?>;
            if (parsed == null || !(parsed["sets"] is List<object?> list))
            {
                throw new InvalidOperationException("Animation sets file has no 'sets' array.");
            }

            var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (item is Dictionary<string, object?> entry
                    && entry.TryGetValue("key", out var key)
                    && key is string name)
                {
                    result[name] = entry;
                }
            }

            return result;
        }

        /// <summary>
        /// Copies the body FBX under Assets/Art so the art pipeline owns it. The source stays where
        /// the package put it: deleting the original would break the customer's ability to re-import.
        /// </summary>
        private static string CopyIntoArt(string sourceFbx, string bodyName, System.Text.StringBuilder report)
        {
            if (!File.Exists(sourceFbx))
            {
                return null;
            }

            var folder = CharactersRoot + "/" + bodyName;
            var target = folder + "/" + bodyName + ".fbx";
            if (File.Exists(target))
            {
                report.AppendLine("body already in Art: " + target);
                return target;
            }

            ArtMaterialUtility.EnsureFolder(folder);
            AssetDatabase.CopyAsset(sourceFbx, target);
            AssetDatabase.Refresh();
            report.AppendLine("body copied to Art: " + target);
            return File.Exists(target) ? target : null;
        }

        /// <summary>
        /// Resolves each role of the set to an actual clip. The set names clips, but a clip lives
        /// inside an FBX somewhere; this finds it by name across the whole project, which is safe
        /// because the set's own validator already proved each name exists in its source folder.
        /// </summary>
        private static Dictionary<string, AnimationClip> CollectSetClips(
            Dictionary<string, object?> set,
            string fallbackFbx,
            System.Text.StringBuilder report)
        {
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in new[] { "idle", "walk", "run", "attack", "death", "hit" })
            {
                if (set.TryGetValue(role, out var value) && value is string clipName)
                {
                    wanted[role] = clipName;
                }
            }

            var resolved = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            var index = BuildClipIndex();

            foreach (var pair in wanted)
            {
                if (index.TryGetValue(pair.Value, out var clip))
                {
                    resolved[pair.Key] = clip;
                    continue;
                }

                report.AppendLine("MISSING clip for role " + pair.Key + ": " + pair.Value);
            }

            // The body's own file is the last resort: a single-FBX package carries its clips inside
            // the same model, so a name that the project-wide index missed is still worth one look.
            if (resolved.Count < wanted.Count)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fallbackFbx))
                {
                    if (!(asset is AnimationClip clip)) continue;
                    foreach (var pair in wanted)
                    {
                        if (!resolved.ContainsKey(pair.Key)
                            && string.Equals(clip.name, pair.Value, StringComparison.OrdinalIgnoreCase))
                        {
                            resolved[pair.Key] = clip;
                        }
                    }
                }
            }

            return resolved;
        }

        /// <summary>Clip name -> clip, built once. Name collisions keep the first, which is stable.</summary>
        private static Dictionary<string, AnimationClip> BuildClipIndex()
        {
            var index = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip
                        && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)
                        && !index.ContainsKey(clip.name))
                    {
                        index[clip.name] = clip;
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// Saves the body as a prefab with a controller built from the set. Materials are converted
        /// to the project pipeline first: the package ships Built-in materials, which render pink
        /// under URP — the exact defect the body gallery reported for three other models.
        /// </summary>
        private static GameObject BuildPrefab(
            string artFbx,
            string packageRoot,
            string bodyName,
            Dictionary<string, AnimationClip> clips,
            string mobKey,
            System.Text.StringBuilder report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(artFbx);
            if (model == null)
            {
                report.AppendLine("model did not load: " + artFbx);
                return null;
            }

            ArtMaterialUtility.EnsureFolder(GeneratedFolder);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.name = bodyName;

                var materialFolder = CharactersRoot + "/" + bodyName + "/Materials";
                ArtMaterialUtility.EnsureFolder(materialFolder);
                AssignPackageAlbedo(instance, packageRoot, bodyName, report);
                ArtMaterialUtility.AssignFallbackAlbedo(instance, ArtRoot);
                ArtMaterialUtility.ConvertRenderers(instance, materialFolder);

                // Only the body mesh belongs in a mob prefab. Staffs and stage props sit in the
                // same package and would otherwise ride along as floating geometry.
                StripProps(instance, report);

                var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.enabled = true;

                var controller = BuildController(bodyName, clips, report);
                if (controller != null)
                {
                    animator.runtimeAnimatorController = controller;
                }

                var visuals = instance.GetComponent<CharacterVisuals>()
                    ?? instance.AddComponent<CharacterVisuals>();
                visuals.BindSockets(animator);

                // The state names must match THIS controller. CharacterVisuals defaults to
                // "Idle"/"Locomotion"/"Attack", which do not exist in a controller built from a
                // set, so Animator.Play resolves to nothing and the mob stands in bind pose. Found
                // 21.11.2026 on the wizard prefab: idleState=Idle while the controller has Idle01.
                if (controller != null)
                {
                    ApplyStateNames(visuals, clips, report);
                }

                var prefabPath = GeneratedFolder + "/" + bodyName + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                {
                    AssetDatabase.DeleteAsset(prefabPath);
                }

                // InstantiatePrefab keeps a nested FBX prefab root, and Unity 6 then stores the
                // saved asset as a Prefab variant: the file holds only overrides and no
                // SkinnedMeshRenderer at all, which is unloadable as a plain GameObject. The art
                // pipeline unpacks for the same reason; without it the mob prefab is 122 lines of
                // references and the body renders as nothing.
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance,
                        PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                report.AppendLine("prefab saved: " + prefabPath);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Writes the controller's own clip names into CharacterVisuals.
        ///
        /// CharacterVisuals resolves an animator state by name at runtime, so the prefab must
        /// carry the names of the clips it actually owns. Its defaults ("Idle", "Locomotion",
        /// "Attack") belong to the warrior's hand-built controller and resolve to nothing here —
        /// Animator.Play on a missing state is silent, and the mob stands in bind pose forever.
        /// </summary>
        private static void ApplyStateNames(
            CharacterVisuals visuals,
            Dictionary<string, AnimationClip> clips,
            System.Text.StringBuilder report)
        {
            string Name(string role) =>
                clips.TryGetValue(role, out var clip) ? clip.name : null;

            visuals.SetStateNames(Name("idle"), Name("walk"), Name("run"),
                Name("attack"), Name("hit"), Name("death"));

            report.AppendLine("state names: idle=" + (Name("idle") ?? "-")
                + " walk=" + (Name("walk") ?? "-")
                + " run=" + (Name("run") ?? "-")
                + " attack=" + (Name("attack") ?? "-")
                + " hit=" + (Name("hit") ?? "-")
                + " death=" + (Name("death") ?? "-"));
        }

        /// <summary>
        /// Points each body renderer at the pack's own authored material.
        ///
        /// Why: an FBX only carries the material stored inside it. The wizard's is a bare
        /// "lambert1" with no textures at all — the albedo, normal and mask live in
        /// WizardPBR/Materials/WizardBody.mat, which is a separate asset the model never
        /// references. Converting the embedded material therefore produces a correct-looking
        /// URP material with no texture, and the prefab renders flat grey. Measured 21.11.2026.
        ///
        /// The match is by name, and only replaces a material that has no albedo, so a pack whose
        /// FBX already carries textured materials is left untouched.
        /// </summary>
        private static void AssignPackageAlbedo(
            GameObject instance,
            string packageRoot,
            string bodyName,
            System.Text.StringBuilder report)
        {
            if (string.IsNullOrEmpty(packageRoot) || !AssetDatabase.IsValidFolder(packageRoot))
            {
                return;
            }

            var candidates = new List<Material>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { packageRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null) candidates.Add(material);
            }

            if (candidates.Count == 0)
            {
                report.AppendLine("package has no materials: " + packageRoot);
                return;
            }

            // Prefer the material named after the body; fall back to any material that actually
            // has a texture, because that is the only thing distinguishing a body from a prop.
            var authored = candidates.FirstOrDefault(m =>
                    string.Equals(m.name, bodyName, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(m => m.name.StartsWith(bodyName, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(m => HasAlbedo(m) && m.name.IndexOf("staff", StringComparison.OrdinalIgnoreCase) < 0);

            if (authored == null)
            {
                report.AppendLine("no authored body material found in " + packageRoot);
                return;
            }

            report.AppendLine("authored material: " + authored.name + " albedo=" + HasAlbedo(authored));

            var assigned = 0;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer)) continue;

                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null) continue;
                    if (HasAlbedo(materials[i])) continue;

                    materials[i] = authored;
                    assigned++;
                }

                renderer.sharedMaterials = materials;
            }

            report.AppendLine("renderers given authored material: " + assigned);
        }

        private static bool HasAlbedo(Material material)
        {
            if (material == null) return false;
            return material.GetTexture("_BaseMap") != null
                || material.GetTexture("_MainTex") != null
                || material.GetTexture("_BaseColorMap") != null;
        }

        /// <summary>
        /// Removes children that are not part of the body. The wizard package ships three staffs
        /// and a stage in the same folder; a mob carrying them would look like a walking prop shop.
        /// </summary>
        private static void StripProps(GameObject instance, System.Text.StringBuilder report)
        {
            var keep = new[] { "WizardBody", "Body", "Mesh", "Character" };
            var removed = 0;

            foreach (var child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child == instance.transform) continue;

                var name = child.name;
                if (keep.Any(k => name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                // Only strip a subtree that has geometry of its own; a bare transform node often
                // carries the skeleton, and removing it would break the rig.
                if (child.GetComponent<Renderer>() == null
                    && child.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    continue;
                }

                report.AppendLine("stripped prop: " + name);
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                removed++;
            }

            report.AppendLine("props stripped: " + removed);
        }

        /// <summary>
        /// Builds the controller from the set's six roles. States are named after the clip, because
        /// CharacterVisuals resolves a state by name and an invented name would play nothing.
        /// </summary>
        private static AnimatorController BuildController(
            string bodyName,
            Dictionary<string, AnimationClip> clips,
            System.Text.StringBuilder report)
        {
            var controllerPath = GeneratedFolder + "/" + bodyName + "Controller.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath) != null)
            {
                AssetDatabase.DeleteAsset(controllerPath);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            // The base layer defaults to weight 0 through the API, which evaluates the machine but
            // writes no pose — the character glides in bind pose. Force weight 1.
            var layers = controller.layers;
            layers[0].defaultWeight = 1f;
            controller.layers = layers;
            var machine = controller.layers[0].stateMachine;

            var states = new Dictionary<string, AnimatorState>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in new[] { "idle", "walk", "run", "attack", "hit", "death" })
            {
                if (!clips.TryGetValue(role, out var clip))
                {
                    report.AppendLine("no clip for role, state skipped: " + role);
                    continue;
                }

                var state = machine.AddState(clip.name);
                state.motion = clip;
                states[role] = state;
            }

            if (states.TryGetValue("idle", out var idle))
            {
                machine.defaultState = idle;
            }

            // Переходы строятся ТОЛЬКО из конкретных состояний. Раньше они шли из AnyState,
            // и тогда смена скорости выбивала моба из атаки: Speed пишется каждый кадр, а
            // AnyState-переход срабатывает и из Attack — жест обрывался на первом же кадре.
            //
            // Пороги walk/run разнесены с зазором. Прежние 3,2/3,2 при moveSpeed 3,2 стояли
            // ровно на скорости моба: она колеблется вокруг порога, оба перехода срабатывают
            // по очереди, и каждый перезапускает клип с нуля — это и было «шаг начинается
            // заново, не закончившись».
            AddLocomotion(machine, states, "idle", "walk", 0.15f, true);
            AddLocomotion(machine, states, "walk", "idle", 0.10f, false);
            AddLocomotion(machine, states, "walk", "run", WalkToRunSpeed, true);
            AddLocomotion(machine, states, "run", "walk", RunToWalkSpeed, false);
            AddLocomotion(machine, states, "idle", "run", WalkToRunSpeed, true);
            AddLocomotion(machine, states, "run", "idle", 0.10f, false);

            AddExit(states, "attack", "idle", 0.9f);
            AddExit(states, "hit", "idle", 0.8f);

            AssetDatabase.SaveAssets();
            report.AppendLine("controller saved: " + controllerPath + " states=" + states.Count);
            return controller;
        }

        /// <summary>
        /// Скорость, выше которой начинается бег (3,6), и ниже которой снова шаг (3,2).
        /// Зазор в 0,4 м/с — это и есть гистерезис: прежние пороги 3,2/3,2 стояли ровно на
        /// moveSpeed моба, и скорость, колеблясь вокруг порога, переключала состояния каждый
        /// кадр. Каждое переключение перезапускало клип с нуля — «шаг начинается заново».
        /// </summary>
        private const float WalkToRunSpeed = 3.6f;
        private const float RunToWalkSpeed = 3.2f;

        private static void AddLocomotion(
            AnimatorStateMachine machine,
            Dictionary<string, AnimatorState> states,
            string from,
            string to,
            float threshold,
            bool greater)
        {
            if (!states.TryGetValue(from, out var source) || !states.TryGetValue(to, out var target))
            {
                return;
            }

            // Переход из конкретного состояния, а не из AnyState: AnyState-переход выбивает
            // моба из атаки и из реакции на удар, потому что Speed пишется каждый кадр.
            var transition = source.AddTransition(target);
            transition.hasExitTime = false;
            transition.duration = 0.14f;
            transition.hasFixedDuration = true;
            transition.AddCondition(greater
                ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, threshold, "Speed");
        }

        private static void AddExit(
            Dictionary<string, AnimatorState> states,
            string from,
            string to,
            float exitTime)
        {
            if (!states.TryGetValue(from, out var source) || !states.TryGetValue(to, out var target))
            {
                return;
            }

            var transition = source.AddTransition(target);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = 0.1f;
        }

        /// <summary>Replaces the entry for a mob, or appends it. Duplicates are what the validator rejects.</summary>
        private static void UpsertEntry(
            List<MobVisualSettings.Entry> entries,
            string mobKey,
            GameObject prefab,
            float height)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].MobKey, mobKey, StringComparison.OrdinalIgnoreCase))
                {
                    entries[i].Prefab = prefab;
                    entries[i].Height = height;
                    return;
                }
            }

            entries.Add(new MobVisualSettings.Entry
            {
                MobKey = mobKey,
                Prefab = prefab,
                Height = height,
                Yaw = 0f,
                GroundOffset = 0f,
            });
        }
    }
}
