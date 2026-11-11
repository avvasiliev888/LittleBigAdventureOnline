using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Presentation adapter for an imported humanoid prefab. It does not own movement,
    /// combat or inventory state: it only turns snapshots into Animator parameters and
    /// mounts authored weapon prefabs on hand bones. Put it on any character prefab
    /// (or let <see cref="CharacterVisualSettings"/> add it at runtime).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterVisuals : MonoBehaviour
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int DeadParameter = Animator.StringToHash("Dead");
        private static readonly int AttackParameter = Animator.StringToHash("Attack");
        private static readonly int HitParameter = Animator.StringToHash("Hit");

        [Header("Rig")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform facing;
        [SerializeField] private Transform rightHandSocket;
        [SerializeField] private Transform leftHandSocket;

        [Header("Animator states (leave empty if the controller uses parameters only)")]
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string walkState = "Walk";
        [SerializeField] private string runState = "Run";
        [SerializeField] private string attackState = "Attack";
        [SerializeField] private string hitState = "Hit";
        [SerializeField] private string deathState = "Dead";
        [SerializeField] private float runSpeedThreshold = 3.2f;
        [SerializeField] private float attackDuration = 0.45f;

        [Header("Weapon mount calibration")]
        [SerializeField] private Vector3 weaponLocalPosition;
        [SerializeField] private Vector3 weaponLocalEuler;
        [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

        [Header("Weapons (assign authored prefabs; pivot must be at the grip)")]
        [SerializeField] private WeaponBinding[] weapons = Array.Empty<WeaponBinding>();

        private GameObject _weaponInstance;
        private WeaponKind _equipped;
        private float _attackUntil = -10f;
        private float _hitUntil = -10f;
        private bool _dead;
        private bool _attackQueuedHit;

        [Serializable]
        public sealed class WeaponBinding
        {
            public WeaponKind Kind;
            public GameObject Prefab;
            public Vector3 LocalPosition;
            public Vector3 LocalEuler;
            public Vector3 LocalScale = Vector3.one;
        }

        /// <summary>Animator driven by this adapter; never enables root motion.</summary>
        public Animator Animator => animator;

        /// <summary>Root that turns toward movement without moving the network root.</summary>
        public Transform Facing => facing != null ? facing : transform;

        /// <summary>Currently equipped presentation weapon, if any.</summary>
        public WeaponKind Equipped => _equipped;

        /// <summary>Resolved right-hand mount; humanoid bone fallback if no socket object exists.</summary>
        public Transform RightHandSocket
        {
            get
            {
                if (rightHandSocket == null) ResolveSockets();
                return rightHandSocket;
            }
        }

        /// <summary>True while the attack one-shot is still in its active phase.</summary>
        public bool IsAttackPlaying => Time.time < _attackUntil;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (facing == null) facing = transform;
            ResolveSockets();
        }

        /// <summary>Binds runtime-created prefabs and starts the idle pose.</summary>
        public void Initialize(Transform facingRoot, Animator targetAnimator,
            RuntimeAnimatorController controller = null)
        {
            facing = facingRoot != null ? facingRoot : transform;
            animator = targetAnimator;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.keepAnimatorStateOnDisable = true;
                if (controller != null) animator.runtimeAnimatorController = controller;
                animator.enabled = true;
                animator.Rebind();
                animator.Update(0f);
            }
            ResolveSockets();
            PlayState(idleState, 0f, true);
        }

        /// <summary>Finds the hand sockets by author-friendly names, then by humanoid bone.</summary>
        public void BindSockets(Animator targetAnimator)
        {
            if (targetAnimator != null) animator = targetAnimator;
            ResolveSockets();
        }

        /// <summary>Drives locomotion/death from server snapshots. Never moves the network root.</summary>
        public void SetMotion(Vector3 velocity, bool dead, bool reset, float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) deltaTime = 0f;
            velocity.y = 0f;
            var speed = velocity.magnitude;
            if (!float.IsFinite(speed) || speed <= 0.1f) speed = 0f;

            if (!dead && speed > 0f && facing != null)
            {
                facing.rotation = Quaternion.Slerp(facing.rotation,
                    Quaternion.LookRotation(velocity / speed, Vector3.up),
                    1f - Mathf.Exp(-18f * deltaTime));
            }

            var wasDead = _dead;
            _dead = dead;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            if (dead)
            {
                if (!wasDead)
                {
                    SetBool(DeadParameter, true);
                    PlayState(deathState, 0.1f, true);
                }
                return;
            }

            if (wasDead) SetBool(DeadParameter, false);
            if (wasDead || reset)
            {
                _attackUntil = -10f;
                _attackQueuedHit = false;
                PlayState(idleState, 0.08f, true);
            }

            SetFloat(SpeedParameter, speed);
            if (IsAttackPlaying) return;

            if (Time.time < _hitUntil) return;

            // A hit received mid-swing is played after the swing, not over it.
            if (_attackQueuedHit)
            {
                _attackQueuedHit = false;
                _hitUntil = Time.time + 0.22f;
                SetTrigger(HitParameter);
                PlayState(hitState, 0.06f, true);
                return;
            }

            if (speed <= 0.1f) PlayState(idleState, 0.12f, false);
            else PlayState(speed >= runSpeedThreshold ? runState : walkState, 0.12f, false);
        }

        /// <summary>One-shot swing; a hit received during it is queued, not allowed to cancel it.</summary>
        public void PlayAttack()
        {
            if (_dead || animator == null || !animator.isActiveAndEnabled
                || animator.runtimeAnimatorController == null || IsAttackPlaying) return;
            _attackUntil = Time.time + Mathf.Max(0.1f, attackDuration);
            _attackQueuedHit = false;
            SetTrigger(AttackParameter);
            PlayState(attackState, 0.05f, true);
        }

        /// <summary>Stagger reaction. Queued behind an active attack to preserve the swing.</summary>
        public void PlayHit()
        {
            if (_dead || animator == null || !animator.isActiveAndEnabled
                || animator.runtimeAnimatorController == null) return;
            if (IsAttackPlaying)
            {
                _attackQueuedHit = true;
                return;
            }
            _hitUntil = Time.time + 0.22f;
            SetTrigger(HitParameter);
            PlayState(hitState, 0.06f, true);
        }

        /// <summary>Equips one authored weapon prefab by kind.</summary>
        public void Equip(WeaponKind kind)
        {
            var binding = FindBinding(kind);
            if (_equipped == kind
                && (_weaponInstance != null || binding == null || binding.Prefab == null))
                return;

            _equipped = kind;
            if (_weaponInstance != null)
            {
                Destroy(_weaponInstance);
                _weaponInstance = null;
            }

            if (binding == null || binding.Prefab == null)
            {
                return;
            }

            var socket = rightHandSocket != null ? rightHandSocket : ResolveRightHand();
            if (socket == null)
            {
                Debug.LogWarning("CharacterVisuals: right hand socket not found; " + kind + " not attached.", this);
                return;
            }

            _weaponInstance = Instantiate(binding.Prefab, socket, false);
            _weaponInstance.name = "Weapon " + kind;
            var hasBindingCalibration = binding.LocalPosition != Vector3.zero
                || binding.LocalEuler != Vector3.zero || binding.LocalScale != Vector3.one;
            _weaponInstance.transform.localPosition = hasBindingCalibration
                ? binding.LocalPosition : weaponLocalPosition;
            _weaponInstance.transform.localRotation = Quaternion.Euler(
                hasBindingCalibration ? binding.LocalEuler : weaponLocalEuler);
            _weaponInstance.transform.localScale = hasBindingCalibration
                ? binding.LocalScale : weaponLocalScale;
            foreach (var collider in _weaponInstance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in _weaponInstance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
        }

        /// <summary>Maps the server item archetype to one of the authored weapon prefabs.</summary>
        public void EquipFromArchetype(string archetype)
        {
            var value = archetype ?? string.Empty;
            if (value.IndexOf("bow", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("archer", StringComparison.OrdinalIgnoreCase) >= 0)
                Equip(WeaponKind.Bow);
            else if (value.IndexOf("staff", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mage", StringComparison.OrdinalIgnoreCase) >= 0)
                Equip(WeaponKind.Staff);
            else if (value.IndexOf("dagger", StringComparison.OrdinalIgnoreCase) >= 0)
                Equip(WeaponKind.Dagger);
            else if (value.IndexOf("axe", StringComparison.OrdinalIgnoreCase) >= 0)
                Equip(WeaponKind.Axe);
            else
                Equip(WeaponKind.Sword);
        }

        private WeaponBinding FindBinding(WeaponKind kind)
        {
            if (weapons == null) return null;
            for (var i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] != null && weapons[i].Kind == kind) return weapons[i];
            }
            return null;
        }

        private void ResolveSockets()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (rightHandSocket == null) rightHandSocket = FindSocket("RightHandSocket", "WeaponSocket.R",
                "RightHand", "hand_r", "mixamorig:RightHand");
            if (leftHandSocket == null) leftHandSocket = FindSocket("LeftHandSocket", "WeaponSocket.L",
                "LeftHand", "hand_l", "mixamorig:LeftHand");
            if (rightHandSocket == null) rightHandSocket = ResolveRightHand();
            if (leftHandSocket == null && animator != null)
                leftHandSocket = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        private Transform ResolveRightHand()
        {
            if (rightHandSocket != null) return rightHandSocket;
            if (animator == null) return null;
            rightHandSocket = animator.GetBoneTransform(HumanBodyBones.RightHand);
            return rightHandSocket;
        }

        private Transform FindSocket(params string[] names)
        {
            if (animator == null) return null;
            foreach (var candidate in animator.GetComponentsInChildren<Transform>(true))
            {
                for (var i = 0; i < names.Length; i++)
                {
                    if (string.Equals(candidate.name, names[i], StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            return null;
        }

        private void PlayState(string state, float fade, bool restart)
        {
            if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(state))
                return;
            var hash = Animator.StringToHash(state);
            if (!animator.HasState(0, hash)) return;
            if (restart) animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
            else if (!animator.GetCurrentAnimatorStateInfo(0).IsName(state))
                animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
        }

        private void SetFloat(int parameter, float value)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Float))
                animator.SetFloat(parameter, value);
        }

        private void SetBool(int parameter, bool value)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Bool))
                animator.SetBool(parameter, value);
        }

        private void SetTrigger(int parameter)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Trigger))
                animator.SetTrigger(parameter);
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == hash && parameter.type == type) return true;
            }
            return false;
        }
    }
}
