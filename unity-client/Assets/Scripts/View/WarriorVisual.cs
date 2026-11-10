using UnityEngine;

namespace Albion.Client
{
    /// <summary>Presentation-only locomotion. The network root is never moved or rotated here.</summary>
    public sealed class WarriorVisual : MonoBehaviour
    {
        private enum MotionState { Idle, Locomotion, Attack, Hit, Dead }

        private static readonly int SpeedParameter = UnityEngine.Animator.StringToHash("Speed");
        private MotionState _state = MotionState.Idle;
        private bool _dead;

        public Animator Animator { get; private set; }
        public Transform Facing { get; private set; }
        public float Speed { get; private set; }

        public void Initialize(Transform facing, Animator animator)
        {
            Facing = facing;
            Animator = animator;
            SetMotion(Vector3.zero, false, true);
        }

        public void SetMotion(Vector3 velocity, bool dead, bool reset = false)
        {
            velocity.y = 0f;
            var speed = velocity.magnitude;
            Speed = dead || reset || !float.IsFinite(speed) || speed <= 0.1f ? 0f : speed;
            var wasDead = _dead;
            _dead = dead;
            if (Speed > 0f && Facing != null)
                Facing.rotation = Quaternion.LookRotation(velocity / Speed, Vector3.up);

            if (Animator == null) return;
            Animator.applyRootMotion = false;
            if (Animator.runtimeAnimatorController == null) return;
            Animator.SetFloat(SpeedParameter, Speed);

            // Presentation state is driven explicitly so ability/death feedback never depends on
            // parameter-consumption timing. The controller still owns the actual clip/blend graph.
            if (dead)
            {
                if (!wasDead)
                {
                    _state = MotionState.Dead;
                    Animator.CrossFadeInFixedTime("Dead", 0.08f, 0);
                }
                return;
            }

            if (wasDead || reset)
            {
                _state = MotionState.Idle;
                Animator.CrossFadeInFixedTime("Idle", 0.1f, 0);
            }

            if (_state == MotionState.Attack || _state == MotionState.Hit)
            {
                var action = Animator.GetCurrentAnimatorStateInfo(0);
                var expected = _state == MotionState.Attack ? "Attack" : "Hit";
                // Wait for the crossfade to enter the one-shot, then hold it through its exit phase.
                if (!action.IsName(expected) || action.normalizedTime < 0.9f) return;
            }

            var target = Speed > 0.1f ? MotionState.Locomotion : MotionState.Idle;
            if (_state != target)
            {
                _state = target;
                Animator.CrossFadeInFixedTime(target == MotionState.Locomotion ? "Locomotion" : "Idle", 0.1f, 0);
            }
        }

        /// <summary>One-shot swing triggered only when the local ability actually starts.</summary>
        public void PlayAttack()
        {
            if (_dead || Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return;
            // Spamming Space must not restart the swing from frame 0. The prediction cooldown
            // normally blocks this too, but the visual guard is authoritative for the clip.
            if (IsAttackPlaying) return;
            _state = MotionState.Attack;
            Animator.CrossFadeInFixedTime("Attack", 0.05f, 0);
        }

        /// <summary>True until the current Attack one-shot reaches its exit phase.</summary>
        public bool IsAttackPlaying
        {
            get
            {
                if (_state != MotionState.Attack || Animator == null || !Animator.isActiveAndEnabled)
                {
                    return false;
                }

                var action = Animator.GetCurrentAnimatorStateInfo(0);
                // During the crossfade into Attack the state is still the previous clip; treat
                // that window as playing too. Once Attack reaches 0.9 it may return to locomotion.
                return !action.IsName("Attack") || action.normalizedTime < 0.9f;
            }
        }

        /// <summary>One-shot stagger triggered by a server-side HP decrease.</summary>
        public void PlayHit()
        {
            if (_dead || Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return;
            _state = MotionState.Hit;
            Animator.CrossFadeInFixedTime("Hit", 0.05f, 0);
        }
    }
}
