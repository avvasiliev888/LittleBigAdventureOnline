using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Presentation-only prefab reference. The asset can point either at the legacy animated
    /// warrior or at any imported humanoid prefab that carries a <see cref="CharacterVisuals"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Albion/Character Visual Settings")]
    public sealed class CharacterVisualSettings : ScriptableObject
    {
        public const string ResourcePath = "Characters/WarriorVisual";
        public GameObject Prefab;
        public RuntimeAnimatorController Controller;
        [Min(0.1f)] public float Height = 1.95f;
        public float Yaw;
        public float GroundOffset;
        [Tooltip("Degrees the bow-holding wrist is rolled about the forearm axis (preview-calibrated).")]
        public float BowHandRoll = 90f;
        [Tooltip("Degrees the head is turned toward the bow (preview-calibrated).")]
        public float BowHeadTurn = -82.16f;
        [Tooltip("Extra yaw applied when a bow is equipped, so the left shoulder faces the target and the arrow leaves the bow, not the back.")]
        public float BowFacingOffsetDeg = 90f;
        [Tooltip("Seconds a combat turn toward the target takes. 0.2 reads as deliberate, not teleporting.")]
        [Min(0.01f)] public float TurnSeconds = 0.2f;
        [Tooltip("Metres kept inside the ability radius before striking, so a moving target stays in range.")]
        [Min(0f)] public float ApproachSlack = 0.6f;
        [Tooltip("Authored weapon prefabs, one entry per WeaponKind. Pivots must be at the grip.")]
        public CharacterVisuals.WeaponBinding[] Weapons = Array.Empty<CharacterVisuals.WeaponBinding>();

        public static CharacterVisualSettings Load()
        {
            var settings = Resources.Load<CharacterVisualSettings>(ResourcePath);
            if (settings == null)
                throw new InvalidOperationException("Character visual missing. Run Albion/Characters/Configure Imported Warrior.");
            if (settings.Prefab == null)
                throw new InvalidOperationException("Character visual prefab is not assigned.");

            var character = settings.Prefab.GetComponentInChildren<CharacterVisuals>(true);
            if (character == null && settings.Controller == null)
                throw new InvalidOperationException(
                    "Character prefab needs CharacterVisuals or an explicit RuntimeAnimatorController.");
            return settings;
        }

        /// <summary>Instantiate a presentation root under the network parent.</summary>
        public Transform Create(Transform parent, uint entityId)
        {
            if (!float.IsFinite(Height) || Height <= 0f
                || !float.IsFinite(Yaw) || !float.IsFinite(GroundOffset))
                throw new InvalidOperationException("Character visual requires positive finite height and finite offsets.");
            if (Prefab == null)
                throw new InvalidOperationException("Character visual prefab is not assigned.");

            return Prefab.GetComponentInChildren<CharacterVisuals>(true) != null
                ? CreateImported(parent, entityId)
                : CreateLegacyWarrior(parent, entityId);
        }

        private Transform CreateImported(Transform parent, uint entityId)
        {
            var root = new GameObject("Entity " + entityId).transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
            var facing = new GameObject("Character Facing").transform;
            facing.SetParent(root, false);
            var calibration = new GameObject("Character Calibration").transform;
            calibration.SetParent(facing, false);

            var visual = Instantiate(Prefab, calibration, false);
            visual.name = "Character Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, Yaw, 0f);
            DisablePhysics(visual);

            var animator = visual.GetComponentInChildren<Animator>(true);
            if (animator == null)
                throw new InvalidOperationException("Imported character prefab has no Animator.");
            // A professional prefab owns its controller (state names/parameters are authored for
            // its skeleton). The legacy settings controller is only a fallback for prefabs that
            // do not ship one; overriding a generated prefab's controller with the old warrior
            // controller would retarget the wrong state graph.
            if (animator.runtimeAnimatorController == null && Controller != null)
                animator.runtimeAnimatorController = Controller;
            if (animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("Imported character has no RuntimeAnimatorController.");
            animator.applyRootMotion = false;
            animator.enabled = false;

            root.gameObject.SetActive(true);

            var bounds = Measure(visual, root);
            var nativeHeight = bounds.max.y - bounds.min.y;
            if (!float.IsFinite(nativeHeight) || nativeHeight < 0.001f)
                throw new InvalidOperationException("Imported character has invalid rest bounds.");
            var scale = Height / nativeHeight;
            calibration.localScale = Vector3.one * scale;
            calibration.localPosition = new Vector3(
                -(bounds.min.x + bounds.max.x) * 0.5f * scale,
                -bounds.min.y * scale + GroundOffset,
                -(bounds.min.z + bounds.max.z) * 0.5f * scale);
            // Grounding is re-solved every frame: one-shot clips move the hips, and the offset
            // computed here from the rest pose would leave the body sunk into the floor.
            var anchor = calibration.gameObject.AddComponent<GroundAnchor>();
            anchor.Configure(visual.transform, calibration, animator, GroundOffset, Height, scale);
            // Solve once immediately so the first drawn frame is already grounded instead of
            // popping down on the next LateUpdate.
            anchor.Ground();

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            // Weapon slots are parented into the hand during the next frames. The pose is only
            // meaningful once that assembly settles, so the pose log stays quiet until then.
            WarriorDiagnostics.Pause();
            animator.enabled = true;
            var character = visual.GetComponent<CharacterVisuals>()
                ?? visual.GetComponentInChildren<CharacterVisuals>(true)
                ?? visual.AddComponent<CharacterVisuals>();
            character.Initialize(facing, animator, animator.runtimeAnimatorController);
            character.SetWeapons(Weapons);
            character.TurnSeconds = TurnSeconds;
            character.BowYawOffsetDeg = BowFacingOffsetDeg;
            AttachAimPose(visual, animator, character);
            AttachDiagnostics(visual, animator, calibration);
            HealthBar.Attach(root, Height + GroundOffset);
            // Resume on the next frame, after the animator has produced a real pose.
            root.gameObject.AddComponent<DiagnosticsResume>();
            return root;
        }

        private Transform CreateLegacyWarrior(Transform parent, uint entityId)
        {
            if (Controller == null)
                throw new InvalidOperationException("Legacy warrior visual requires a RuntimeAnimatorController.");

            var root = new GameObject("Entity " + entityId).transform;
            root.SetParent(parent, false);
            root.gameObject.SetActive(false);
            // Facing rotates about the grounded centre, not the asymmetric imported mesh pivot.
            var facing = new GameObject("Warrior Facing").transform;
            facing.SetParent(root, false);
            // Calibration is OUTSIDE Animator's hierarchy, so animation cannot overwrite height/grounding.
            var calibration = new GameObject("Warrior Calibration").transform;
            calibration.SetParent(facing, false);
            var visual = Instantiate(Prefab, calibration, false);
            visual.name = "Warrior Visual";
            // The vendor prefab ships sword and axe under the same hand socket; hide the axe
            // before the character is shown (M10.11).
            WeaponVisuals.Attach(visual.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, Yaw, 0f);
            DisablePhysics(visual);
            root.gameObject.SetActive(true);
            var bounds = Measure(visual, root);
            var nativeHeight = bounds.max.y - bounds.min.y;
            if (!float.IsFinite(nativeHeight) || nativeHeight < 0.001f)
                throw new InvalidOperationException("Warrior body has invalid rest bounds.");
            var scale = Height / nativeHeight;
            calibration.localScale = Vector3.one * scale;
            calibration.localPosition = new Vector3(
                -(bounds.min.x + bounds.max.x) * 0.5f * scale,
                -bounds.min.y * scale + GroundOffset,
                -(bounds.min.z + bounds.max.z) * 0.5f * scale);
            var anchor = calibration.gameObject.AddComponent<GroundAnchor>();
            anchor.Configure(visual.transform, calibration, visual.GetComponent<Animator>(),
                GroundOffset, Height, scale);
            anchor.Ground();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            var animator = visual.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Warrior requires a valid Humanoid Animator/Avatar on the prefab root.");
            // Vendor sockets are offset from forearms in the rest pose. Bind them to actual hands
            // before retargeting; otherwise wrist/forearm rotations make equipment float away.
            BindHandSocket(animator, "RightHandSocket", HumanBodyBones.RightHand);
            BindHandSocket(animator, "LeftHandSocket", HumanBodyBones.LeftHand);
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = Controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.keepAnimatorStateOnDisable = true;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
            root.gameObject.AddComponent<WarriorVisual>().Initialize(facing, animator);
            HealthBar.Attach(root, Height + GroundOffset);
            return root;
        }

        /// <summary>
        /// Installs the per-frame aim corrections (wrist roll + head turn) that the preview window
        /// calibrated. Skipped entirely when both values are zero, so characters that never aim pay
        /// nothing for it.
        /// </summary>
        private void AttachAimPose(GameObject visual, Animator animator, CharacterVisuals character)
        {
            if (Mathf.Abs(BowHandRoll) < 0.01f && Mathf.Abs(BowHeadTurn) < 0.01f) return;
            var pose = visual.GetComponent<CharacterAimPose>() ?? visual.AddComponent<CharacterAimPose>();
            // The bow slot is located lazily each frame, because Equip() may not have run yet here
            // and the slot is rebuilt whenever the loadout changes.
            pose.Configure(animator, null, BowHandRoll, BowHeadTurn);
        }

        /// <summary>
        /// Turns on the pose logger: one line every half second in Player.log with the active
        /// Animator state, bone heights and any weapon teleports. Animation bugs on an imported
        /// rig cannot be judged from the pivot alone — bone positions are the only evidence.
        /// </summary>
        private static void AttachDiagnostics(GameObject visual, Animator animator, Transform calibration)
        {
            var diagnostics = visual.GetComponent<WarriorDiagnostics>()
                ?? visual.AddComponent<WarriorDiagnostics>();
            diagnostics.Configure(animator, calibration);
        }

        private static void DisablePhysics(GameObject visual)        {
            foreach (var item in visual.GetComponentsInChildren<Animator>(true))
            {
                item.applyRootMotion = false;
                item.enabled = false;
            }
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        private static (Vector3 min, Vector3 max) Measure(GameObject visual, Transform root)
        {
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var lowest = float.PositiveInfinity;
            var lowestSource = "-";
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            try
            {
                foreach (var skin in skins)
                {
                    if (skin.sharedMesh == null) continue;
                    // A mounted weapon is parented to the hand, so it counts as "geometry" and a
                    // resting bow hangs BELOW the feet. Grounding on that lifted the whole body
                    // off the floor — the character appeared to hover with tucked legs.
                    if (IsMountedEquipment(skin.transform)) continue;
                    skin.enabled = true;

                    // Bake WITH scale. The third argument is not cosmetic: measured 21.11.2026 on
                    // the Battle Wizard, whose FBX imports at lossyScale 0,01 because it was
                    // authored in centimetres.
                    //   BakeMesh(mesh, false) -> nativeHeight 0,0197 for a 2,25 m body, so the
                    //     game applied scale 124: the model filled the screen with stretched
                    //     polygons and the body itself became invisible.
                    //   BakeMesh(mesh, true)  -> nativeHeight 1,97, and the knight is bit-identical
                    //     at 2,3145 both ways because its lossyScale is 1.
                    // Verified across four variants; only this one is correct for both packs.
                    skin.BakeMesh(mesh, true);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = root.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                        if (point.y < lowest)
                        {
                            lowest = point.y;
                            lowestSource = Path(skin.transform, visual.transform);
                        }
                    }
                    mesh.Clear();
                }
            }
            finally { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer || renderer.bounds.size == Vector3.zero) continue;
                if (IsMountedEquipment(renderer.transform)) continue;
                var b = renderer.bounds;
                foreach (var corner in Corners(b))
                {
                    var point = root.InverseTransformPoint(corner);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    if (point.y < lowest)
                    {
                        lowest = point.y;
                        lowestSource = Path(renderer.transform, visual.transform) + " [static]";
                    }
                }
            }

            if (!float.IsFinite(min.y) || !float.IsFinite(max.y))
                throw new InvalidOperationException("Character has no measurable geometry; check the prefab.");

            LowestSource = lowestSource;
            return (min, max);
        }

        /// <summary>Which object the last <see cref="Measure"/> call found lowest.</summary>
        private static string LowestSource = "-";

        /// <summary>Hierarchy path of a transform, so the log says exactly which object was lowest.</summary>
        private static string Path(Transform item, Transform stopAt)
        {
            var builder = new System.Text.StringBuilder(item.name);
            for (var current = item.parent; current != null && current != stopAt; current = current.parent)
            {
                builder.Insert(0, current.name + "/");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Re-anchors the visual to the ground on the pose that is CURRENTLY displayed.
        ///
        /// The startup offset is computed once, from the rest pose. One-shot clips (attack, hit,
        /// death) lower the hips or shift the body, and the Animator writes those bones straight
        /// onto the hierarchy — so the model sinks through the floor while the clip plays and stays
        /// there afterwards. Recomputing every frame keeps the feet on the ground regardless of
        /// what the clip does.
        /// </summary>
        /// <summary>
        /// Re-enables the pose log one frame after the character was built, once the animator has
        /// written a real pose. Sampling earlier reported the half-assembled rig.
        /// </summary>
        private sealed class DiagnosticsResume : MonoBehaviour
        {
            private int _frames;

            private void LateUpdate()
            {
                if (++_frames < 2) return;
                WarriorDiagnostics.Resume();
                Destroy(this);
            }
        }

        private sealed class GroundAnchor : MonoBehaviour
        {
            private Transform _visual;
            private Transform _calibration;
            private Animator _animator;
            private Transform _leftFoot;
            private Transform _rightFoot;
            /// <summary>Distance from the foot bone down to the sole, in calibration local units.</summary>
            private float _soleDrop;
            private float _offset;
            private float _height;
            private float _scale;

            public void Configure(Transform visual, Transform calibration, Animator animator,
                float offset, float height, float scale)
            {
                _visual = visual;
                _calibration = calibration;
                _animator = animator;
                _offset = offset;
                _height = height;
                _scale = scale;

                // The sole offset is measured ONCE, from the rest pose, by comparing the foot bone
                // against the lowest body vertex. Thereafter grounding only looks at the foot
                // bones, so a hanging sword, a bow or a cape can never lift the character.
                if (_animator != null && _animator.isHuman)
                {
                    _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
                }

                if (_leftFoot == null || _rightFoot == null || _calibration == null) return;
                var bounds = Measure(visual.gameObject, _calibration);
                var restFoot = LowestFootY();
                if (!float.IsNaN(restFoot)) _soleDrop = restFoot - bounds.min.y;
            }

            private void LateUpdate()
            {
                // Deliberately does NOT call _animator.Update(0): forcing the animator through a
                // zero-length step from LateUpdate left it pinned on the bind pose as soon as a
                // weapon slot was parented into the hierarchy, which is exactly the "hovers with
                // tucked legs, identical numbers every frame" pose the player saw.
                Ground();
            }

            /// <summary>Re-solves the vertical anchor from the pose currently on the bones.</summary>
            public void Ground()
            {
                if (_visual == null || _calibration == null) return;

                var basePosition = _calibration.localPosition;

                if (_leftFoot != null && _rightFoot != null)
                {
                    var foot = LowestFootY();
                    if (!float.IsNaN(foot))
                    {
                        // Feet, not geometry: the lowest sole is placed on the ground.
                        basePosition.y = -((foot - _soleDrop) * _scale) + _offset;
                        _calibration.localPosition = basePosition;
                        WarriorDiagnostics.GroundMinY = foot - _soleDrop;
                        WarriorDiagnostics.GroundMinSource = "footBones";
                        return;
                    }
                }

                // Fallback for rigs without usable foot bones: measure the body geometry, with
                // mounted weapons excluded.
                var bounds = Measure(_visual.gameObject, _calibration);
                // Only the vertical anchor moves; X/Z centring was correct at startup.
                basePosition.y = -bounds.min.y * _scale + _offset;
                _calibration.localPosition = basePosition;
                // Published so the pose log can show whether grounding, not the clip, is what
                // lifts the body off the floor.
                WarriorDiagnostics.GroundMinY = bounds.min.y;
                WarriorDiagnostics.GroundMinSource = LowestSource;            }

            /// <summary>Height of the lowest foot bone in calibration-local units.</summary>
            private float LowestFootY()
            {
                if (_calibration == null) return float.NaN;
                var left = float.PositiveInfinity;
                var right = float.PositiveInfinity;
                if (_leftFoot != null)
                    left = _calibration.InverseTransformPoint(_leftFoot.position).y;
                if (_rightFoot != null)
                    right = _calibration.InverseTransformPoint(_rightFoot.position).y;
                var lowest = Mathf.Min(left, right);
                return float.IsNaN(lowest) || float.IsInfinity(lowest) ? float.NaN : lowest;
            }
        }

        /// <summary>
        /// True for a transform that belongs to a mounted weapon rather than the body. These must
        /// never influence grounding: a hanging bow or a lowered blade reaches below the feet and
        /// would lift the whole character off the floor.
        /// </summary>
        private static bool IsMountedEquipment(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (current.name.StartsWith("Weapon ", StringComparison.Ordinal)
                    || current.name.StartsWith("WeaponSlot_", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector3[] Corners(Bounds bounds)
        {
            var center = bounds.center;
            var extents = bounds.extents;
            return new[]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, extents.z),
            };
        }

        private static void BindHandSocket(Animator animator, string name, HumanBodyBones bone)
        {
            var hand = animator.GetBoneTransform(bone);
            if (hand == null) throw new InvalidOperationException("Warrior hand bone missing: " + bone);
            foreach (var socket in animator.GetComponentsInChildren<Transform>(true))
            {
                if (socket.name != name) continue;
                socket.SetParent(hand, true); // Preserve the artist's rest-pose grip/orientation.
                return;
            }
            throw new InvalidOperationException("Warrior equipment socket missing: " + name);
        }
    }
}
