using System;
using System.Collections.Generic;
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
        // Must cover the authored 1H_Melee_Attack_Chop clip (~1.07 s); 0.45 s cut the swing
        // almost immediately and looked like a twitch.
        [SerializeField] private float attackDuration = 1.0f;

        [Header("Weapon mount calibration")]
        [SerializeField] private Vector3 weaponLocalPosition;
        [SerializeField] private Vector3 weaponLocalEuler;
        [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

        [Header("Weapons (assign authored prefabs; pivot must be at the grip)")]
        [SerializeField] private WeaponBinding[] weapons = Array.Empty<WeaponBinding>();

        private readonly Dictionary<WeaponKind, GameObject> _weaponSlots = new();
        private bool _weaponSlotsBuilt;
        private bool _embeddedEquipmentHidden;
        private WeaponKind _equipped;
        private float _attackUntil = -10f;
        private float _hitUntil = -10f;
        private bool _dead;
        private bool _attackQueuedHit;
        private int _attackIndex;
        private string _lastAttackState = string.Empty;

        private static readonly string[] SwordCombo =
        {
            "1H_Melee_Attack_Chop",
            "1H_Melee_Attack_Slice_Diagonal",
            "1H_Melee_Attack_Slice_Horizontal",
            "1H_Melee_Attack_Stab",
        };

        private static readonly string[] AxeCombo =
        {
            "1H_Melee_Attack_Chop",
            "1H_Melee_Attack_Slice_Horizontal",
            "1H_Melee_Attack_Slice_Diagonal",
        };

        private static readonly string[] DaggerCombo =
        {
            "1H_Melee_Attack_Stab",
            "1H_Melee_Attack_Chop",
            "1H_Melee_Attack_Slice_Diagonal",
        };

        private static readonly string[] BowCombo =
        {
            "1H_Ranged_Shoot",
            "1H_Ranged_Shooting",
        };

        private static readonly string[] StaffCombo =
        {
            "Spellcast_Shoot",
            "Spellcast_Long",
        };

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

        /// <summary>Last horizontal speed passed by UnityView; diagnostics for the playtest.</summary>
        public float LastMotionSpeed { get; private set; }

        /// <summary>Last state requested by locomotion, and whether the controller has it.</summary>
        public string LastRequestedState { get; private set; } = string.Empty;
        public bool LastRequestedStateExists { get; private set; }

        public string WalkStateName => walkState;
        public string RunStateName => runState;
        public string AttackStateName => string.IsNullOrEmpty(_lastAttackState) ? attackState : _lastAttackState;

        /// <summary>Transform of the one active authored weapon slot, or null when unarmed.</summary>
        public Transform EquippedTransform
        {
            get
            {
                foreach (var pair in _weaponSlots)
                {
                    if (pair.Key == _equipped && pair.Value != null && pair.Value.activeSelf)
                        return pair.Value.transform;
                }
                return null;
            }
        }

        /// <summary>Exactly one slot may be active; used by the playtest and setup validation.</summary>
        public int ActiveWeaponSlotCount
        {
            get
            {
                var count = 0;
                foreach (var pair in _weaponSlots)
                {
                    if (pair.Value != null && pair.Value.activeSelf) count++;
                }
                return count;
            }
        }

        /// <summary>Default KayKit meshes that must never be visible next to a mounted weapon.</summary>
        public int VisibleEmbeddedEquipmentCount
        {
            get
            {
                var count = 0;
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                    if (IsMountedWeapon(renderer.transform)) continue;
                    if (IsEmbeddedEquipmentName(renderer.gameObject.name)) count++;
                }
                return count;
            }
        }

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
            HideEmbeddedEquipment();
            ResolveSockets();
        }

        /// <summary>Replaces the authored weapon loadout, typically from CharacterVisualSettings.</summary>
        public void SetWeapons(WeaponBinding[] bindings)
        {
            weapons = bindings ?? Array.Empty<WeaponBinding>();
            ClearWeaponSlots();
        }

        /// <summary>
        /// Keeps the serialized state names in sync with the authored KayKit clip names. The
        /// import pipeline calls this before saving the generated prefab; left null/empty for
        /// legacy prefabs that still use the canonical Idle/Walk/Run names.
        /// </summary>
        public void SetStateNames(string idle, string walk, string run, string attack,
            string hit, string death)
        {
            if (!string.IsNullOrEmpty(idle)) idleState = idle;
            if (!string.IsNullOrEmpty(walk)) walkState = walk;
            if (!string.IsNullOrEmpty(run)) runState = run;
            if (!string.IsNullOrEmpty(attack)) attackState = attack;
            if (!string.IsNullOrEmpty(hit)) hitState = hit;
            if (!string.IsNullOrEmpty(death)) deathState = death;
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
            HideEmbeddedEquipment();
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
            LastMotionSpeed = speed;

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

            if (wasDead)
            {
                // Respawn is the only reset that must cancel combat one-shots.
                _attackUntil = -10f;
                _attackQueuedHit = false;
                SetBool(DeadParameter, false);
            }

            // A teleport/respawn while already running must not queue Idle and Run in the
            // same Animator update: Unity applies only one cross-fade per frame, and Idle
            // wins, leaving the legs frozen while the root slides. Render catch-up also sets
            // reset every snapshot; it must never cancel an active swing.
            if ((wasDead || reset) && !IsAttackPlaying && speed <= 0.1f)
                PlayState(idleState, 0.08f, true);

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

            var state = NextAttackState();
            _lastAttackState = state;
            _attackUntil = Time.time + Mathf.Max(0.35f, ClipLength(state));
            _attackQueuedHit = false;
            // A pending Hit trigger from the same server tick can override the direct Play via
            // AnyState -> Hit_A. The mob must show its swing, not only the flinch.
            ResetTrigger(HitParameter);
            // Do NOT set the Attack trigger: AnyState -> 1H_Melee_Attack_Chop would fire next
            // update from a combo state and cancel the selected variant. PlayState is the
            // authoritative path; the parameter/AnyState graph remains a preview fallback.
            PlayState(state, 0.05f, true);
        }

        /// <summary>
        /// Picks the next authored attack clip for the equipped weapon. Swapping weapons resets
        /// the combo, so every equip starts with its strongest opening swing.
        /// </summary>
        private string NextAttackState()
        {
            string[] combo;
            switch (_equipped)
            {
                case WeaponKind.Bow: combo = BowCombo; break;
                case WeaponKind.Staff: combo = StaffCombo; break;
                case WeaponKind.Dagger: combo = DaggerCombo; break;
                case WeaponKind.Axe: combo = AxeCombo; break;
                default: combo = SwordCombo; break;
            }

            if (combo.Length == 0) return attackState;
            var state = combo[_attackIndex % combo.Length];
            _attackIndex++;
            return state;
        }

        private float ClipLength(string stateName)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                foreach (var clip in controller.animationClips)
                {
                    if (clip != null && clip.name == stateName) return clip.length;
                }
            }
            return attackDuration;
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

        /// <summary>
        /// Builds one inactive slot per authored binding, then enables exactly the selected kind.
        /// The imported KayKit body already contains sword/shield meshes inside the FBX; those are
        /// hidden separately so a snapshot can never show three swords at once.
        /// </summary>
        public void Equip(WeaponKind kind)
        {
            HideEmbeddedEquipment();
            BuildWeaponSlots();

            var hasBinding = FindBinding(kind) != null;
            var target = hasBinding ? kind : (WeaponKind?)null;
            foreach (var pair in _weaponSlots)
            {
                if (pair.Value == null) continue;
                pair.Value.SetActive(target.HasValue && pair.Key == target.Value);
            }
            _equipped = target ?? kind;
            _attackIndex = 0;
            _lastAttackState = string.Empty;
            if (!target.HasValue)
                Debug.LogWarning("CharacterVisuals: no authored binding for " + kind + "; character stays unarmed.", this);
        }

        private void BuildWeaponSlots()
        {
            if (_weaponSlotsBuilt) return;
            _weaponSlotsBuilt = true;
            if (weapons == null || weapons.Length == 0) return;

            var socket = rightHandSocket != null ? rightHandSocket : ResolveRightHand();
            if (socket == null)
            {
                Debug.LogWarning("CharacterVisuals: right hand socket not found; weapons not attached.", this);
                return;
            }

            foreach (var binding in weapons)
            {
                if (binding == null || binding.Prefab == null || _weaponSlots.ContainsKey(binding.Kind)) continue;
                var slot = Instantiate(binding.Prefab, socket, false);
                slot.name = "Weapon " + binding.Kind;
                var hasBindingCalibration = binding.LocalPosition != Vector3.zero
                    || binding.LocalEuler != Vector3.zero || binding.LocalScale != Vector3.one;
                slot.transform.localPosition = hasBindingCalibration
                    ? binding.LocalPosition : weaponLocalPosition;
                slot.transform.localRotation = Quaternion.Euler(
                    hasBindingCalibration ? binding.LocalEuler : weaponLocalEuler);
                slot.transform.localScale = hasBindingCalibration
                    ? binding.LocalScale : weaponLocalScale;
                foreach (var collider in slot.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var body in slot.GetComponentsInChildren<Rigidbody>(true))
                {
                    body.isKinematic = true;
                    body.detectCollisions = false;
                }
                slot.SetActive(false);
                _weaponSlots[binding.Kind] = slot;
            }
        }

        private void ClearWeaponSlots()
        {
            foreach (var pair in _weaponSlots)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }
            _weaponSlots.Clear();
            _weaponSlotsBuilt = false;
        }

        /// <summary>Hides the author's default swords/shields inside the imported body prefab.</summary>
        private void HideEmbeddedEquipment()
        {
            if (_embeddedEquipmentHidden) return;
            _embeddedEquipmentHidden = true;
            var hidden = 0;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                var target = renderer.gameObject;
                if (target == gameObject || IsMountedWeapon(renderer.transform)) continue;
                if (!IsEmbeddedEquipmentName(target.name)) continue;
                target.SetActive(false);
                hidden++;
            }
            if (hidden > 0)
                Debug.Log("[visuals] скрыто встроенное оружие/щит KayKit: " + hidden, this);
        }

        private static bool IsMountedWeapon(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
            {
                if (current.name.StartsWith("Weapon ", StringComparison.Ordinal)
                    || current.name.StartsWith("WeaponSlot_", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool IsEmbeddedEquipmentName(string objectName)
        {
            var name = (objectName ?? string.Empty).ToLowerInvariant();
            return name.Contains("sword") || name.Contains("shield")
                || name.Contains("axe") || name.Contains("staff")
                || name.Contains("crossbow") || name.Contains("dagger")
                || name.Contains("weapon");
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
            LastRequestedState = state;
            LastRequestedStateExists = animator.HasState(0, hash);
            if (!LastRequestedStateExists) return;
            // Play is deterministic even when an Animator was just rebound or is being driven by
            // a test the same frame; CrossFade could be ignored in those cases and leave the
            // previous (Idle) pose while the root moved. The state graph still owns the clip.
            if (restart || !animator.GetCurrentAnimatorStateInfo(0).IsName(state))
                animator.Play(hash, 0, 0f);
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

        private void ResetTrigger(int parameter)
        {
            if (HasParameter(parameter, AnimatorControllerParameterType.Trigger))
                animator.ResetTrigger(parameter);
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
