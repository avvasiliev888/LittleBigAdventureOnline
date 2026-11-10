using System;
using System.IO;
using Albion.Client;
using Simulation.Core;
using UnityEditor;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Albion.Editor
{
    /// <summary>Explicit setup and regression checks; never modifies vendor prefabs or open scenes on import.</summary>
    public static class AlbionCharacterSetup
    {
        private const string AssetPath = "Assets/Resources/Characters/WarriorVisual.asset";
        public const string PrefabPath = "Assets/dRealWarriorFantasyCharacter/Prefabs/Warrior.prefab";

        [MenuItem("Albion/Characters/Configure Warrior Visual")]
        public static void Configure()
        {
            Directory.CreateDirectory("Assets/Resources/Characters");
            AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<CharacterVisualSettings>(AssetPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CharacterVisualSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
            }
            // Humanoid retargeting normalizes forward to +Z, unlike the old frozen rest mesh.
            settings.Yaw = 0f;
            settings.Controller = AlbionAnimationSetup.Configure();
            settings.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Require(settings.Prefab != null, "Imported Warrior prefab missing: " + PrefabPath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Validate();
        }

        [MenuItem("Albion/Characters/Validate Warrior Visual")]
        public static void Validate()
        {
            var root = new GameObject("Character regression (transient)");
            try
            {
                var settings = CharacterVisualSettings.Load();
                Require(AssetDatabase.GetAssetPath(settings.Prefab) == PrefabPath, "Wrong character prefab.");
                var view = new UnityView(root.transform) { PlayerEntityId = 1 };
                view.ApplySnapshot(Snapshot(1, false));
                view.Render(0f);
                Require(view.Player != null, "Snapshot did not spawn player.");
                var player = view.Player;
                Require(root.transform.childCount == 2 && root.transform.GetChild(1).position == new Vector3(1f, 0f, 8f),
                    "Remote entity did not follow snapshot position.");
                Require(player.position == new Vector3(3f, 0f, 4f), "Authoritative position changed.");
                Require(player.localScale == Vector3.one, "Network root must retain unit scale.");
                var materials = Array.FindAll(player.GetComponentsInChildren<Renderer>(),
                    renderer => renderer.GetComponentInParent<HealthBar>() == null);
                Require(materials.Length >= 2, "Expected imported body and equipment, not primitives.");
                var healthBar = player.GetComponentInChildren<HealthBar>();
                Require(healthBar != null, "Health bar is missing.");
                if (healthBar != null)
                {
                    Require(healthBar.transform.Find("Mana Back") != null, "Mana backing bar is missing.");
                    Require(healthBar.transform.Find("Mana Fill") != null, "Mana fill bar is missing.");
                }
                foreach (var renderer in materials)
                foreach (var material in renderer.sharedMaterials)
                {
                    Require(material != null && material.shader != null && material.shader.isSupported
                        && material.shader.name == "Universal Render Pipeline/Lit"
                        && !ShaderUtil.ShaderHasError(material.shader), "Warrior has missing/unsupported URP material.");
                    Require(material.GetTexture("_BaseMap") != null, "Warrior albedo missing.");
                }
                foreach (var animator in player.GetComponentsInChildren<Animator>(true))
                    Require(animator.enabled && !animator.applyRootMotion && animator.runtimeAnimatorController == settings.Controller,
                        "Animator must animate bones with root motion OFF and configured controller.");
                foreach (var collider in player.GetComponentsInChildren<Collider>(true))
                    Require(!collider.enabled, "Client physics collider enabled.");
                var body = player.GetComponentInChildren<SkinnedMeshRenderer>();
                Require(body != null, "No imported skinned body.");
                Require(body.enabled && body.GetComponent<MeshRenderer>() == null,
                    "Visible geometry must remain skinned, not a frozen duplicate.");
                ValidateAnimation(view, body, settings);
                ValidateWeaponAnimation(view);
                ValidateIdleWalk(root.transform);
                ValidateCombat(root.transform);
                ValidateRemoteMotion(root.transform);
                var originalMaterial = body.sharedMaterial;
                // Two-tick buffer delay: leap sufficiently far to exercise death and respawn.
                view.ApplySnapshot(Snapshot(20, true));
                view.ApplySnapshot(Snapshot(23, true));
                view.Render(1f);
                var deathMotion = player.GetComponent<WarriorVisual>();
                Require(player.GetChild(0).gameObject.activeSelf && deathMotion != null,
                    "Death must keep the visual alive for the one-shot Death state.");
                deathMotion.Animator.Update(0.2f);
                Require(deathMotion.Animator.GetCurrentAnimatorStateInfo(0).IsName("Dead"),
                    "HP=0 did not enter the Death state.");
                RequireDominantClip(deathMotion.Animator, "1HDeathA");
                view.ApplySnapshot(Snapshot(40, false));
                view.ApplySnapshot(Snapshot(43, false));
                view.Render(1f);
                deathMotion.Animator.Update(0.2f);
                Require(player.GetChild(0).gameObject.activeSelf && body.sharedMaterial == originalMaterial,
                    "Respawn did not preserve imported materials.");
                Require(deathMotion.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                    "Respawn did not revive the Animator from Death.");
                var respawnMotion = deathMotion;
                Require(respawnMotion.Animator.enabled && respawnMotion.Animator.gameObject.activeInHierarchy
                    && !respawnMotion.Animator.applyRootMotion,
                    "Respawn must recover an active Animator with root motion OFF.");
                // Exercise the recovered controller without adding prediction samples to the lifecycle test.
                respawnMotion.SetMotion(Vector3.forward * 1.5f, false);
                respawnMotion.Animator.Update(0.01f);
                respawnMotion.Animator.Update(0.2f);
                ValidateClipCycle(respawnMotion, "1HWalkF", true);
                respawnMotion.SetMotion(Vector3.zero, false);
                view.SetPredictedPlayerPosition(40, new NVector3(6f, 0f, 7f), NVector3.Zero, false);
                view.Render(0f);
                Require(player.position == new Vector3(6f, 0f, 7f), "Predicted movement disconnected from visual root.");
                Debug.Log("Warrior regression checks passed: prefab, URP, grounding, scale, snapshot/prediction, death/respawn.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ValidateWeaponAnimation(UnityView view)
        {
            var player = view.Player;
            var motion = player.GetComponent<WarriorVisual>();
            var animator = motion.Animator;
            var weapons = player.GetComponentInChildren<WeaponVisuals>(true);
            Require(weapons != null, "Weapon switcher is missing.");
            if (weapons == null)
            {
                return;
            }

            Require(weapons.Equipped == WeaponKind.Sword
                && weapons.SwordObject != null && weapons.SwordObject.activeSelf
                && weapons.AxeObject != null && !weapons.AxeObject.activeSelf,
                "Spawn must show only the sword.");
            motion.SetMotion(Vector3.zero, false);
            animator.Update(0.2f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Weapon check must start Idle.");

            foreach (var kind in new[] { WeaponKind.Sword, WeaponKind.Axe })
            {
                weapons.Equip(kind);
                Require(weapons.Equipped == kind
                    && weapons.SwordObject.activeSelf == (kind == WeaponKind.Sword)
                    && weapons.AxeObject.activeSelf == (kind == WeaponKind.Axe),
                    "Equip(" + kind + ") did not show exactly one weapon.");
                var equipped = weapons.EquippedTransform;
                Require(equipped != null && equipped.gameObject.activeInHierarchy, "Equipped weapon is inactive.");
                var before = equipped.position;
                motion.PlayAttack();
                animator.Update(0.15f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"),
                    "Equipped " + kind + " did not enter Attack.");
                Require((equipped.position - before).sqrMagnitude > 0.000001f,
                    "Equipped " + kind + " did not follow the attack animation.");
                animator.Update(2f);
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                    "Attack with " + kind + " did not return to Idle.");
            }

            weapons.Equip(WeaponKind.Sword);
            Require(weapons.Equipped == WeaponKind.Sword
                && weapons.SwordObject.activeSelf && !weapons.AxeObject.activeSelf,
                "Weapon validation must restore the sword.");
            Debug.Log("Warrior weapon checks passed: sword by default, 1/2 toggles one mesh, "
                + "equipped weapon follows Attack.");
        }

        private static void ValidateCombat(Transform parent)
        {
            var group = new GameObject("Combat regression");
            group.transform.SetParent(parent, false);
            try
            {
                var view = new UnityView(group.transform) { PlayerEntityId = 2 };
                view.ApplySnapshot(RemoteSnapshot(1, 0f));
                view.Render(0f);
                var motion = view.Player.GetComponent<WarriorVisual>();
                var animator = motion.Animator;
                var root = motion.transform;
                var rootPosition = root.position;
                var rootRotation = root.rotation;
                var rootScale = root.localScale;
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Combat regression must start Idle.");

                // HP-drop path through UnityView (not just a direct PlayHit call).
                view.ApplySnapshot(RemoteSnapshot(4, 0f, 75));
                view.ApplySnapshot(RemoteSnapshot(6, 0f, 75));
                view.ApplySnapshot(RemoteSnapshot(9, 0f, 75));
                view.Render(0.2f);
                animator.Update(0.15f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"), "Server HP loss did not enter Hit.");
                RequireDominantClip(animator, "1HPain");
                animator.Update(2f);
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "HP Hit did not return to Idle.");

                motion.PlayAttack();
                animator.Update(0.15f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), "Space/ability did not enter Attack.");
                RequireDominantClip(animator, "1HAttack");
                var attackProgress = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                motion.PlayAttack();
                animator.Update(0f);
                Require(motion.IsAttackPlaying
                    && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= attackProgress,
                    "Spamming Space restarted the active Attack from frame 0.");
                // Render also runs every frame while the swing is active; locomotion must not
                // override the pending/ongoing one-shot before it reaches its exit phase.
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.05f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), "Locomotion overrode an active Attack.");
                animator.Update(2f);
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Attack did not return to Idle.");

                motion.PlayHit();
                animator.Update(0.15f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"), "HP loss did not enter Hit.");
                RequireDominantClip(animator, "1HPain");
                animator.Update(2f);
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Hit did not return to Idle.");

                // A swing must interrupt locomotion, then resume it without moving the network root.
                motion.SetMotion(Vector3.forward * 4f, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Combat must start from Locomotion.");
                motion.PlayAttack();
                animator.Update(0.15f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), "Attack did not interrupt locomotion.");
                animator.Update(2f);
                motion.SetMotion(Vector3.forward * 4f, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Attack did not resume locomotion.");

                // Death has priority over movement and plays once; respawn returns to Idle.
                motion.SetMotion(Vector3.forward * 4f, true);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Dead"), "Death did not interrupt locomotion.");
                RequireDominantClip(animator, "1HDeathA");
                motion.SetMotion(Vector3.zero, false);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Respawn did not recover Idle.");
                Require(root.position == rootPosition && root.rotation == rootRotation && root.localScale == rootScale
                    && !animator.applyRootMotion, "Combat animation moved the network root or enabled root motion.");
                Debug.Log("Warrior combat checks passed: Attack interrupts locomotion, Hit staggers, Death plays once and respawns Idle.");
            }
            finally { UnityEngine.Object.DestroyImmediate(group); }
        }

        private static void RequireDominantClip(Animator animator, string expected)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Require(clips.Length > 0, expected + " has no active clip.");
            var dominant = clips[0];
            foreach (var clip in clips)
                if (clip.weight > dominant.weight) dominant = clip;
            Require(dominant.clip != null && dominant.clip.name.IndexOf(expected, StringComparison.Ordinal) >= 0
                && dominant.weight > 0.5f, "Expected dominant clip " + expected + ", got "
                + (dominant.clip == null ? "null" : dominant.clip.name) + " at weight " + dominant.weight);
        }

        private static void ValidateRemoteMotion(Transform parent)
        {
            var group = new GameObject("Remote motion regression");
            group.transform.SetParent(parent, false);
            try
            {
                foreach (var dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
                {
                    var view = new UnityView(group.transform) { PlayerEntityId = 2 };
                    view.ApplySnapshot(RemoteSnapshot(1, 0f));
                    view.Render(0f);
                    var motion = view.Player.GetComponent<WarriorVisual>();
                    Require(motion.Speed == 0f, "Remote spawn must be Idle.");
                    view.ApplySnapshot(RemoteSnapshot(5, 0.8f));
                    view.Render(dt);
                    Require(Mathf.Abs(motion.Speed - 4f) < 0.002f
                        && Vector3.Dot(motion.Facing.forward, Vector3.forward) > 0.999f,
                        "Remote interpolation speed/facing depends on framerate: " + dt + ", speed=" + motion.Speed);
                    for (var frame = 0; frame < 40; frame++) view.Render(1f / 30f);
                    Require(motion.Speed == 0f, "Frozen remote snapshots must return to Idle.");
                    view.Render(float.NaN);
                    view.Render(0f);
                    Require(motion.Speed == 0f, "Invalid/zero delta produced remote motion.");
                    view.ApplySnapshot(RemoteSnapshot(100, 50f));
                    view.ApplySnapshot(RemoteSnapshot(103, 50f));
                    view.Render(dt);
                    Require(motion.Speed == 0f, "Catchup/teleport must reset remote velocity.");
                    UnityEngine.Object.DestroyImmediate(view.Player.gameObject);

                    // A normal consecutive-tick correction must not become a stride merely because
                    // interpolation divides its 4 m jump into sub-2 m rendered frames at high FPS.
                    // Use an independent view so the freeze/catchup assertions above keep their history.
                    var correctionView = new UnityView(group.transform) { PlayerEntityId = 2 };
                    correctionView.ApplySnapshot(RemoteSnapshot(1, 0f));
                    correctionView.Render(0f);
                    var correctionMotion = correctionView.Player.GetComponent<WarriorVisual>();
                    Require(correctionMotion.Speed == 0f, "Correction baseline must start Idle.");
                    correctionView.ApplySnapshot(RemoteSnapshot(2, 4f));
                    correctionView.ApplySnapshot(RemoteSnapshot(3, 4f));
                    correctionView.ApplySnapshot(RemoteSnapshot(4, 4f));
                    correctionView.Render(dt);
                    Require(correctionMotion.Speed == 0f && correctionMotion.Animator.GetFloat("Speed") == 0f,
                        "Consecutive-tick 4 m correction must stay Idle at FPS=" + (1f / dt)
                        + ", speed=" + correctionMotion.Speed);
                    Require(Mathf.Abs(correctionView.Player.position.z - 4f * Mathf.Min(dt * 20f, 1f)) < 0.0001f,
                        "Animation discontinuity filtering must not change interpolated position.");
                    UnityEngine.Object.DestroyImmediate(correctionView.Player.gameObject);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(group); }
            Debug.Log("Remote locomotion checks passed: 30/60/144 FPS, spawn, freeze, zero/NaN delta, catchup/teleport, consecutive-tick correction.");
        }

        private static SimulationSnapshot RemoteSnapshot(uint tick, float z, int hp = 100) => new SimulationSnapshot(tick,
            new[] { new EntitySnapshot(2, 1, new NVector3(0f, 0f, z), NVector3.Zero, hp, 100, 100, 100, 0, 0, false) });

        private static void ValidateAnimation(UnityView view, SkinnedMeshRenderer body, CharacterVisualSettings settings)
        {
            var player = view.Player;
            var motion = player.GetComponent<WarriorVisual>();
            Require(motion != null && motion.Animator != null, "Missing locomotion driver.");
            var animator = motion.Animator;
            var origin = player.position;
            var scale = animator.transform.parent.localScale;
            var calibration = animator.transform.parent.localPosition;
            var importedYaw = animator.transform.localRotation;
            var leg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Require(leg != null, "Humanoid leg missing.");
            animator.Update(0.2f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Spawn must enter Idle.");
            var idlePose = leg.localRotation;
            var mesh = new Mesh();
            try
            {
                body.BakeMesh(mesh, false);
                var min = float.PositiveInfinity;
                var max = float.NegativeInfinity;
                foreach (var vertex in mesh.vertices)
                {
                    var y = player.InverseTransformPoint(body.transform.TransformPoint(vertex)).y;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
                Debug.Log("Animated Warrior idle: feet=" + min + ", height=" + (max - min)
                    + ", calibrationScale=" + scale + ", offset=" + calibration);
                Require(Mathf.Abs(min - settings.GroundOffset) < 0.08f, "Idle feet are not grounded: " + min);
                Require(max - min > settings.Height * 0.8f && max - min < settings.Height * 1.08f,
                    "Animated body scale incorrect: " + (max - min));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }

            // Supplied prediction velocity, not reconciliation displacement, controls locomotion.
            view.SetPredictedPlayerPosition(2, new NVector3(3f, 0f, 4f), new NVector3(4f, 0f, 0f), false);
            view.Render(0.016f);
            Require(Mathf.Abs(motion.Speed - 4f) < 0.001f, "Prediction did not select locomotion speed.");
            Require(Vector3.Dot(motion.Facing.forward, Vector3.right) > 0.999f, "D must face right, not strafe.");
            animator.Update(0.01f);
            animator.Update(0.2f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Speed>0.1 must enter locomotion.");
            var movingPose = leg.localRotation;
            var minLeg = float.PositiveInfinity;
            var maxLeg = float.NegativeInfinity;
            for (var sample = 0; sample < 60; sample++)
            {
                animator.Update(1f / 60f);
                var angle = Quaternion.Angle(idlePose, leg.localRotation);
                minLeg = Mathf.Min(minLeg, angle);
                maxLeg = Mathf.Max(maxLeg, angle);
            }
            Debug.Log("Retargeted lower-leg stride range=" + (maxLeg - minLeg));
            Require(maxLeg - minLeg > 15f, "Run has no substantial leg stride; check source Avatar reference pose.");
            animator.Update(0.17f);
            Require(Quaternion.Angle(movingPose, leg.localRotation) > 1f, "Run pose does not advance.");
            Require(Quaternion.Angle(idlePose, leg.localRotation) > 1f, "Run did not change Idle pose.");
            var beforeAnimation = player.position;
            var visualPosition = animator.transform.localPosition;
            for (var frame = 0; frame < 120; frame++) animator.Update(1f / 60f);
            Require(player.position == beforeAnimation && player.localScale == Vector3.one
                && player.rotation == Quaternion.identity, "Animator moved/rotated/scaled the network root.");
            Require((animator.transform.localPosition - visualPosition).sqrMagnitude < 0.000001f,
                "Root translation leaked into the imported visual.");
            Require(animator.transform.parent.localScale == scale && animator.transform.parent.localPosition == calibration
                && Quaternion.Angle(animator.transform.localRotation, importedYaw) < 0.01f,
                "Animation overwrote calibration or model yaw.");

            view.SetPredictedPlayerPosition(3, new NVector3(3f, 0f, 4f), new NVector3(-4f, 0f, 0f), true);
            view.Render(0.016f);
            Require(Vector3.Dot(motion.Facing.forward, Vector3.left) > 0.999f, "A must turn left.");
            var heading = motion.Facing.rotation;
            view.SetPredictedPlayerPosition(4, new NVector3(-2f, 0f, 1f), NVector3.Zero, true);
            view.Render(0.016f);
            Require(motion.Speed == 0f && animator.GetFloat("Speed") == 0f && motion.Facing.rotation == heading,
                "Stopped reconciliation must stay Idle and retain heading.");
            animator.Update(0.01f);
            animator.Update(0.2f);
            Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Zero speed did not return to Idle.");
            motion.SetMotion(new Vector3(0.1f, 0f, 0f), false);
            Require(motion.Speed == 0f, "Threshold 0.1 must stay Idle.");
            motion.SetMotion(new Vector3(0.11f, 0f, 0f), false);
            Require(motion.Speed > 0.1f, "Threshold above 0.1 must move.");
            motion.SetMotion(new Vector3(float.NaN, 0f, 0f), false);
            Require(motion.Speed == 0f, "Invalid velocity reached Animator.");
            motion.SetMotion(Vector3.forward * 4f, true);
            Require(motion.Speed == 0f, "Dead visual must be idle despite input.");
            Require(!animator.applyRootMotion && animator.enabled, "Root motion must remain OFF.");
            // Restore baseline prediction for the remaining snapshot/lifecycle assertions.
            view.SetPredictedPlayerPosition(5, new NVector3(origin.x, origin.y, origin.z), NVector3.Zero, false);
            view.Render(0f);
            Debug.Log("Warrior animation checks passed: advancing Idle/Walk/Run, clip selection, prediction speed, A/D facing, threshold, stop/correction, root motion OFF.");
        }

        private static void ValidateIdleWalk(Transform parent)
        {
            // Keep sampled clip phases and motion inputs independent of the existing Run/lifecycle
            // regression: those assertions intentionally retain their original Idle pose and history.
            var group = new GameObject("Idle Walk regression");
            group.transform.SetParent(parent, false);
            try
            {
                var view = new UnityView(group.transform) { PlayerEntityId = 2 };
                view.ApplySnapshot(RemoteSnapshot(1, 0f));
                view.Render(0f);
                var motion = view.Player.GetComponent<WarriorVisual>();
                var animator = motion.Animator;
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Clip regression must start Idle.");
                ValidateClipCycle(motion, "1HIdle", false);
                // Exact walk threshold distinguishes a correctly wired blend tree from Run-only motion.
                motion.SetMotion(Vector3.forward * 1.5f, false);
                animator.Update(0.01f);
                animator.Update(0.2f);
                Require(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Walk speed must enter locomotion.");
                ValidateClipCycle(motion, "1HWalkF", true);
            }
            finally { UnityEngine.Object.DestroyImmediate(group); }
        }

        private static void ValidateClipCycle(WarriorVisual motion, string expectedClip, bool walking)
        {
            var animator = motion.Animator;
            Require(animator.enabled && !animator.applyRootMotion && !animator.IsInTransition(0),
                expectedClip + " must be sampled on an enabled, settled Animator with root motion OFF.");
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            Require(clips.Length > 0, expectedClip + " has no active clip.");
            var dominant = clips[0];
            foreach (var clip in clips)
                if (clip.weight > dominant.weight) dominant = clip;
            Require(dominant.clip != null && dominant.clip.name.IndexOf(expectedClip, StringComparison.Ordinal) >= 0
                && dominant.weight > 0.5f, "Expected dominant clip " + expectedClip + ", got "
                + (dominant.clip == null ? "null" : dominant.clip.name) + " at weight " + dominant.weight);

            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            var rightLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Require(spine != null && leftLeg != null && rightLeg != null, "Humanoid spine/legs missing.");
            var root = motion.transform;
            var rootPosition = root.position;
            var rootRotation = root.rotation;
            var rootScale = root.localScale;
            var modelPosition = animator.transform.localPosition;
            var modelRotation = animator.transform.localRotation;
            var calibrationPosition = animator.transform.parent.localPosition;
            var calibrationScale = animator.transform.parent.localScale;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var startTime = state.normalizedTime;
            Require(state.length > 0f && float.IsFinite(state.length), expectedClip + " has invalid cycle length.");
            // Derive sample spacing from this state's actual length, rather than assuming Idle and
            // Walk share a one-second cycle. Compare all sampled pairs to avoid phase-dependent bounds.
            const int maxSamples = 721;
            var spinePoses = new Quaternion[maxSamples];
            var leftPoses = new Quaternion[maxSamples];
            var rightPoses = new Quaternion[maxSamples];
            var spineRange = 0f;
            var legRange = 0f;
            var cycleComplete = false;
            for (var sample = 0; sample < maxSamples; sample++)
            {
                spinePoses[sample] = spine.localRotation;
                leftPoses[sample] = leftLeg.localRotation;
                rightPoses[sample] = rightLeg.localRotation;
                for (var previous = 0; previous < sample; previous++)
                {
                    spineRange = Mathf.Max(spineRange, Quaternion.Angle(spinePoses[previous], spinePoses[sample]));
                    legRange = Mathf.Max(legRange, Quaternion.Angle(leftPoses[previous], leftPoses[sample]),
                        Quaternion.Angle(rightPoses[previous], rightPoses[sample]));
                }
                Require(root.position == rootPosition && root.rotation == rootRotation && root.localScale == rootScale
                    && (animator.transform.localPosition - modelPosition).sqrMagnitude < 0.000001f
                    && Quaternion.Angle(animator.transform.localRotation, modelRotation) < 0.01f
                    && animator.transform.parent.localPosition == calibrationPosition
                    && animator.transform.parent.localScale == calibrationScale,
                    expectedClip + " moved the network/model root or calibration while sampling bones.");
                if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime - startTime >= 1f)
                {
                    cycleComplete = true;
                    break;
                }
                animator.Update(state.length / 180f);
            }
            Require(cycleComplete, expectedClip + " did not advance through a full animation cycle.");
            Require(walking ? legRange > 5f : spineRange > 0.1f,
                expectedClip + " has no meaningful bone progression: spine=" + spineRange + ", legs=" + legRange);
            Debug.Log(expectedClip + " full-cycle progression: spine=" + spineRange + ", legs=" + legRange);
        }

        private static SimulationSnapshot Snapshot(uint tick, bool dead) => new SimulationSnapshot(tick,
            new[] { new EntitySnapshot(1, 1, new NVector3(3f, 0f, 4f), NVector3.Zero,
                dead ? 0 : 100, 100, 100, 100, 0, dead ? 100u : 0u, dead),
                new EntitySnapshot(2, 1, new NVector3(1f, 0f, 8f), NVector3.Zero, 100, 100, 100, 100, 0, 0, false) });

        public static void ConfigureAndVerify()
        {
            Configure();
            AlbionBatch.VerifyRendering();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
