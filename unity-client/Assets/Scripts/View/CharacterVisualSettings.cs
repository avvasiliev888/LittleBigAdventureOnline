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

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            animator.enabled = true;
            var character = visual.GetComponent<CharacterVisuals>()
                ?? visual.GetComponentInChildren<CharacterVisuals>(true)
                ?? visual.AddComponent<CharacterVisuals>();
            character.Initialize(facing, animator, animator.runtimeAnimatorController);
            character.SetWeapons(Weapons);
            HealthBar.Attach(root, Height + GroundOffset);
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

        private static void DisablePhysics(GameObject visual)
        {
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
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            try
            {
                foreach (var skin in skins)
                {
                    skin.enabled = true;
                    skin.BakeMesh(mesh, false);
                    foreach (var vertex in mesh.vertices)
                    {
                        var point = root.InverseTransformPoint(skin.transform.TransformPoint(vertex));
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }
                    mesh.Clear();
                }
            }
            finally { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer || renderer.bounds.size == Vector3.zero) continue;
                var b = renderer.bounds;
                foreach (var corner in Corners(b))
                {
                    var point = root.InverseTransformPoint(corner);
                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                }
            }

            return (min, max);
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
