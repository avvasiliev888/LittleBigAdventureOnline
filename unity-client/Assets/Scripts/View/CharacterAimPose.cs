using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Re-applies two pose corrections on top of the Animator every frame: the bow hand's wrist
    /// roll and the head turn toward the bow.
    ///
    /// Both values were calibrated in the editor preview window and cannot live on the clip,
    /// because the clip is a shared Mixamo asset. They also cannot be written once: the Animator
    /// overwrites every bone it animates on each evaluation, so anything applied earlier in the
    /// frame is gone. LateUpdate is the only window where a correction survives to rendering.
    ///
    /// Everything here is gated on the bow being the equipped weapon. An earlier version applied
    /// the head turn unconditionally, which left the warrior staring over his shoulder whenever he
    /// held a sword, and rolled the wrist of whichever hand was being corrected.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAimPose : MonoBehaviour
    {
        [Tooltip("Bone the aim corrections drive. Usually the character's Animator.")]
        [SerializeField] private Animator animator;

        [Header("Bow hand")]
        [Tooltip("Degrees the bow-holding wrist is rolled about the forearm axis.")]
        [SerializeField] private float bowHandRoll = 90f;

        [Header("Head")]
        [Tooltip("Degrees the head is turned toward the bow. Negative turns toward a left-hand bow.")]
        [SerializeField] private float headTurn = -82.16f;

        [Tooltip("Disable to keep the clip's own head direction.")]
        [SerializeField] private bool applyHeadTurn = true;

        private Transform _leftHand;
        private Transform _leftForearm;
        private Transform _head;
        private Transform _bowSlot;
        private CharacterVisuals _visuals;

        /// <summary>Last value this component wrote, used to detect a bone the clip does not drive.</summary>
        private bool _handWritten;
        private Quaternion _handDelta = Quaternion.identity;
        private Quaternion _handResult = Quaternion.identity;
        private bool _headWritten;
        private Quaternion _headDelta = Quaternion.identity;
        private Quaternion _headResult = Quaternion.identity;

        public void Configure(Animator target, Transform bowSlot, float wristRoll, float turn)
        {
            animator = target;
            _bowSlot = bowSlot;
            bowHandRoll = wristRoll;
            headTurn = turn;
            _handWritten = false;
            _headWritten = false;
            ResolveVisuals();
            ResolveBones();
        }

        private void Awake()
        {
            ResolveVisuals();
            ResolveBones();
        }

        private void ResolveVisuals()
        {
            if (_visuals != null) return;
            _visuals = GetComponent<CharacterVisuals>() ?? GetComponentInParent<CharacterVisuals>();
        }

        /// <summary>
        /// True only while a bow is the equipped weapon. The sword, axe, dagger and staff must
        /// never receive archery corrections.
        /// </summary>
        private bool IsBowEquipped()
        {
            ResolveVisuals();
            return _visuals != null && _visuals.Equipped == WeaponKind.Bow;
        }

        private void ResolveBones()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) return;

            _leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        /// <summary>
        /// Runs after the Animator has posed the rig, which is the only window where a correction
        /// survives to rendering.
        /// </summary>
        private void LateUpdate()
        {
            if (animator == null || !animator.isHuman) return;
            if (_leftHand == null) ResolveBones();

            // Archery corrections belong to the bow only.
            if (!IsBowEquipped())
            {
                _handWritten = false;
                _headWritten = false;
                return;
            }

            ApplyWristRoll();
            if (applyHeadTurn) ApplyHeadTurn();
        }

        /// <summary>
        /// Rolls the wrist about the forearm axis — pronation, the motion that lays the palm flat
        /// under a bow grip.
        ///
        /// The axis comes from bone POSITIONS: bone local axes are rolled differently on every rig
        /// and would bend the wrist sideways instead of turning it.
        ///
        /// An earlier version ALSO restored the bow's world position and rotation after rolling
        /// the hand. That pinned the bow to where it sat with the *unrolled* hand, so it hovered
        /// beside the palm and jumped as the hand moved — the visible flicker. The bow is rigidly
        /// parented to the hand; the hand is the only thing written here and the prop follows.
        /// </summary>
        private void ApplyWristRoll()
        {
            if (Mathf.Abs(bowHandRoll) < 0.01f) return;
            if (_leftHand == null || _leftForearm == null) return;

            var axis = _leftHand.position - _leftForearm.position;
            if (axis.sqrMagnitude < 1e-8f) return;

            var delta = Quaternion.AngleAxis(bowHandRoll, axis.normalized);
            _leftHand.rotation = delta * WithoutPreviousWrite(_leftHand, delta, ref _handWritten,
                ref _handDelta, ref _handResult);
        }

        /// <summary>
        /// Returns the hand's own (clip) rotation, discarding this component's previous write.
        ///
        /// The Animator overwrites every bone it animates each frame, so normally the value read
        /// in LateUpdate is already the clip's and nothing needs undoing. But a clip that carries
        /// no curve for this bone leaves the previous frame's value in place, and multiplying the
        /// roll onto it again would spin the wrist further every single frame. Detecting "the bone
        /// was not rewritten since we touched it" and undoing our own delta keeps the result
        /// idempotent in both cases.
        /// </summary>
        private static Quaternion WithoutPreviousWrite(Transform bone, Quaternion delta,
            ref bool written, ref Quaternion previousDelta, ref Quaternion previousResult)
        {
            var current = bone.rotation;
            if (written && Quaternion.Angle(current, previousResult) < 0.01f)
                current = Quaternion.Inverse(previousDelta) * current;

            var result = delta * current;
            written = true;
            previousDelta = delta;
            previousResult = result;
            return current;
        }

        /// <summary>
        /// Turns the head about world up. A head turn is a yaw; the bone's local axes would tilt
        /// the head on rigs whose head bone is rolled.
        /// </summary>
        private void ApplyHeadTurn()
        {
            if (Mathf.Abs(headTurn) < 0.01f) return;
            if (_head == null) return;
            var delta = Quaternion.AngleAxis(headTurn, Vector3.up);
            _head.rotation = delta * WithoutPreviousWrite(_head, delta, ref _headWritten,
                ref _headDelta, ref _headResult);
        }
    }
}
