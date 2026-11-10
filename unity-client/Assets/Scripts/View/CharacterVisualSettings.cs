using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>Presentation-only prefab/controller references; Resources includes dependencies in both builds.</summary>
    [CreateAssetMenu(menuName = "Albion/Character Visual Settings")]
    public sealed class CharacterVisualSettings : ScriptableObject
    {
        public const string ResourcePath = "Characters/WarriorVisual";
        public GameObject Prefab;
        public RuntimeAnimatorController Controller;
        [Min(0.1f)] public float Height = 1.85f;
        public float Yaw;
        public float GroundOffset;

        public static CharacterVisualSettings Load()
        {
            var settings = Resources.Load<CharacterVisualSettings>(ResourcePath);
            if (settings == null || settings.Prefab == null || settings.Controller == null)
                throw new InvalidOperationException("Warrior visual missing. Run Albion/Characters/Configure Warrior Visual.");
            return settings;
        }

        public Transform Create(Transform parent, uint entityId)
        {
            if (Prefab == null || Controller == null || !float.IsFinite(Height) || Height <= 0f
                || !float.IsFinite(Yaw) || !float.IsFinite(GroundOffset))
                throw new InvalidOperationException("Character visual requires prefab/controller, positive finite height and finite offsets.");
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
            root.gameObject.SetActive(true);
            var skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skins.Length == 0) throw new InvalidOperationException("Warrior requires a skinned body mesh.");
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
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
            var nativeHeight = max.y - min.y;
            if (!float.IsFinite(nativeHeight) || nativeHeight < 0.001f)
                throw new InvalidOperationException("Warrior body has invalid rest bounds.");
            var scale = Height / nativeHeight;
            calibration.localScale = Vector3.one * scale;
            calibration.localPosition = new Vector3(-(min.x + max.x) * 0.5f * scale,
                -min.y * scale + GroundOffset, -(min.z + max.z) * 0.5f * scale);
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
