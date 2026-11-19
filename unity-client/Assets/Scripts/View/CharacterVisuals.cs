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
        // The controller has ONE locomotion state, driven by a Walk/Run blend tree; there is no
        // separate "Walk" or "Run" state. Asking for those hashed to nothing, HasState returned
        // false, and the legs stayed in the idle pose while the root slid across the map.
        [SerializeField] private string walkState = "Locomotion";
        [SerializeField] private string runState = "Locomotion";
        [SerializeField] private string attackState = "Attack";
        [SerializeField] private string hitState = "Hit";
        [SerializeField] private string deathState = "Dead";
        [SerializeField] private float runSpeedThreshold = 3.2f;
        // Must cover the authored 1H_Melee_Attack_Chop clip (~1.07 s); 0.45 s cut the swing
        // almost immediately and looked like a twitch.
        [SerializeField] private float attackDuration = 1.0f;

        // How long the pose watch keeps re-entering the current state after the weapon hierarchy
        // changes. Slots are instantiated and toggled over a few frames, and each change drops the
        // rig to the bind pose; a second covers that window with margin without costing anything
        // once it removes itself.
        private const float PoseWatchSeconds = 1.0f;

        // Locomotion hysteresis. The measured speed is a per-frame displacement divided by frame
        // time, so it jitters; separate start/stop thresholds plus a short dwell stop Idle and
        // Locomotion from trading places every frame.
        private const float StartSpeed = 0.35f;
        private const float StopSpeed = 0.1f;
        private const float LocomotionDwellSeconds = 0.18f;

        private bool _locomotionMoving;
        private float _locomotionChangedAt = -1f;

        // Combat turn. The facing root is normally rewritten from the velocity vector every
        // frame (see SetMotion), which silently undoes any instant correction made by Face().
        // A turn therefore has to be an explicit, timed task that blocks the velocity-facing
        // until it settles, otherwise "turn to the target" works only while standing still.
        private bool _turning;
        private float _turnRemaining;
        private float _turnSeconds = 0.2f;
        private float _turnFromYaw;
        private float _turnToYaw;
        private float _turnOffsetDeg;
        private float _yawOffsetDeg;
        private float _baseYaw;
        private float _bowYawOffsetDeg = 90f;

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

        /// <summary>
        /// How long the stagger reaction owns the character. Must cover the clip, or a second
        /// damage event restarts it and the flinch visibly repeats while the player keeps moving.
        /// </summary>
        private const float HitReactionSeconds = 0.6f;

        // Every melee combo resolves to the ONE attack state the controller actually has.
        // These arrays previously held KayKit clip names ("1H_Melee_Attack_Chop" and friends)
        // that exist nowhere in WarriorLocomotion.controller. PlayState checks HasState, found
        // nothing, and returned — so pressing attack did literally nothing: no clip, no error,
        // just the character frozen in whatever pose it was already in.
        private static readonly string[] SwordCombo = { "Attack" };

        private static readonly string[] AxeCombo = { "Attack" };

        private static readonly string[] DaggerCombo = { "Attack" };

        // Must match the state names AlbionBowAnimationSetup writes into the controller. The old
        // entries ("1H_Ranged_Shoot" / "1H_Ranged_Shooting") were leftover KayKit names that exist
        // nowhere in the project, so the Animator silently failed to find them and the bow never
        // fired. Named as literals, not shared constants: that setup script is editor-only and
        // runtime code cannot reference it.
        private static readonly string[] BowCombo =
        {
            "BowShoot",
        };

        // The controller has no spellcast states either; staff falls back to the melee swing
        // rather than asking for a state that does not exist.
        private static readonly string[] StaffCombo = { "Attack" };

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

        /// <summary>Resolved left-hand mount used by the two-handed bow.</summary>
        public Transform LeftHandSocket
        {
            get
            {
                if (leftHandSocket == null) ResolveSockets();
                return leftHandSocket;
            }
        }

        /// <summary>True while the attack one-shot is still in its active phase.</summary>
        public bool IsAttackPlaying => Time.time < _attackUntil;

        /// <summary>True while an explicit combat turn is still settling.</summary>
        public bool IsTurning => _turning;

        /// <summary>
        /// Extra yaw applied on top of every facing rotation. The bow is held in the left hand,
        /// so a character facing its target chest-first points the bow sideways; this offset lets
        /// the model be rotated to a left-shoulder stance without touching the network root.
        /// </summary>
        public float YawOffsetDeg
        {
            get => _yawOffsetDeg;
            set
            {
                if (!float.IsFinite(value)) return;
                if (Mathf.Approximately(_yawOffsetDeg, value)) return;
                _yawOffsetDeg = value;
                // The offset changes the meaning of the current rotation, so re-derive it from
                // the stored base yaw instead of leaving the model visibly rotated by the delta.
                ApplyFacingQuaternion(_baseYaw);
            }
        }

        /// <summary>Seconds an explicit combat turn takes. Clamped to a sane range.</summary>
        public float TurnSeconds
        {
            get => _turnSeconds;
            set => _turnSeconds = Mathf.Clamp(float.IsFinite(value) ? value : 0.2f, 0.01f, 2f);
        }

        /// <summary>Yaw applied when a bow is equipped; calibrated in the preview.</summary>
        public float BowYawOffsetDeg
        {
            get => _bowYawOffsetDeg;
            set
            {
                if (!float.IsFinite(value)) return;
                _bowYawOffsetDeg = value;
                YawOffsetDeg = _equipped == WeaponKind.Bow ? value : 0f;
            }
        }

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

            // A pending combat turn owns the facing root while it settles: velocity facing would
            // otherwise rewrite the rotation every frame and the target-facing would never appear.
            if (_turning && !dead)
            {
                _turnRemaining -= deltaTime;
                var t = _turnSeconds <= 0f ? 1f : Mathf.Clamp01(1f - _turnRemaining / _turnSeconds);
                // Ease-out: fast at the start, settles without overshoot.
                t = 1f - (1f - t) * (1f - t);
                var yaw = Mathf.LerpAngle(_turnFromYaw, _turnToYaw, t) + _turnOffsetDeg;
                ApplyFacingQuaternion(yaw, true);
                if (_turnRemaining <= 0f) _turning = false;
            }
            else if (!dead && speed > 0f && facing != null)
            {
                var yaw = Quaternion.LookRotation(velocity / speed, Vector3.up).eulerAngles.y;
                ApplyFacingQuaternion(yaw, true);
            }

            var wasDead = _dead;
            _dead = dead;
            if (dead) _turning = false;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            if (dead)
            {
                if (!wasDead)
                {
                    // PlayState is the only path. Setting the Dead bool as well would fire the
                    // AnyState -> Dead transition on top of the direct Play, and the two entry
                    // points restarted the clip against each other.
                    PlayState(deathState, 0.1f, true);
                }
                return;
            }

            if (wasDead)
            {
                // Respawn is the only reset that must cancel combat one-shots.
                _attackUntil = -10f;
                _attackQueuedHit = false;
                _turning = false;
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
                _hitUntil = Time.time + HitReactionSeconds;
                SetTrigger(HitParameter);
                return;
            }

            PlayLocomotionWithHysteresis(speed);
        }

        /// <summary>
        /// Chooses the locomotion state with hysteresis, so a speed hovering at the threshold cannot
        /// alternate states every frame.
        ///
        /// The old test was a bare "speed <= 0.1f", and the speed handed in is a per-frame
        /// displacement divided by the frame time — it naturally jitters around any fixed cutoff.
        /// The log caught Idle and Locomotion trading places 747 times in one session (about 25 per
        /// second, i.e. almost every frame), which restarted the clip constantly: the legs never
        /// advanced, the body appeared to slide without striding, and everything parented to a bone
        /// (weapon slots included) visibly flickered between poses.
        ///
        /// Start and stop therefore use different thresholds, plus a minimum time in state. Once
        /// moving, the character keeps moving until it is clearly stopped; once stopped, it needs
        /// clearly more than noise before it walks again.
        /// </summary>
        private void PlayLocomotionWithHysteresis(float speed)
        {
            var moving = _locomotionMoving;
            if (moving)
            {
                if (speed < StopSpeed) moving = false;
            }
            else if (speed > StartSpeed)
            {
                moving = true;
            }

            // A state that was just entered is held briefly. Without this the very next frame can
            // undo the switch, and the cross-fade never completes — the pose is stuck between two
            // clips, which reads as a body that is not animating at all.
            if (moving != _locomotionMoving
                && Time.time - _locomotionChangedAt < LocomotionDwellSeconds)
            {
                moving = _locomotionMoving;
            }

            if (moving != _locomotionMoving)
            {
                _locomotionMoving = moving;
                _locomotionChangedAt = Time.time;
            }

            PlayState(moving ? walkState : idleState, 0.12f, false);
        }

        /// <summary>Immediately turns the visual to a horizontal direction (combat facing).</summary>
        public void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.z)) return;
            if (direction.sqrMagnitude <= 0.0001f) return;
            if (facing == null) facing = transform;
            _turning = false;
            // Поворот якорем, а не телом: сетевой корень позиции не меняется.
            ApplyFacingQuaternion(Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y, true);
        }

        /// <summary>
        /// Starts a smooth turn toward a horizontal direction. Blocks velocity facing until it
        /// settles, so the model actually arrives at the target before the swing is played.
        /// </summary>
        /// <param name="yawOffsetDeg">
        /// Extra yaw added only while this turn holds. The bow is held in the left hand, so a
        /// chest-first stance points it sideways; the shot therefore turns the model to a
        /// left-shoulder stance. Running and walking pass 0 and stay forward-facing.
        /// </param>
        public void TurnTo(Vector3 direction, float yawOffsetDeg = 0f)
        {
            direction.y = 0f;
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.z)) return;
            if (direction.sqrMagnitude <= 0.0001f) return;
            if (facing == null) facing = transform;
            if (!float.IsFinite(yawOffsetDeg)) yawOffsetDeg = 0f;
            _turnFromYaw = _baseYaw;
            _turnToYaw = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y;
            _turnOffsetDeg = yawOffsetDeg;
            // A turn already in flight is restarted from the current pose: retargeting mid-turn is
            // cheaper than finishing a turn toward a target that has already moved.
            _turnSeconds = Mathf.Clamp(_turnSeconds, 0.01f, 2f);
            _turnRemaining = _turnSeconds;
            _turning = true;
        }

        /// <summary>Writes the facing root from a base yaw, adding the calibration offset.</summary>
        private void ApplyFacingQuaternion(float baseYaw, bool remember = false)
        {
            if (facing == null) facing = transform;
            if (remember) _baseYaw = baseYaw;
            facing.rotation = Quaternion.Euler(0f, baseYaw + _yawOffsetDeg, 0f);
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
                var clips = controller.animationClips;
                foreach (var clip in clips)
                {
                    if (clip != null && clip.name == stateName) return clip.length;
                }

                // Clip names differ from state names: the "Attack" state plays a clip called
                // "1HAttack", "Dead" plays "1HDeathA". Falling back to a flat attackDuration
                // kept the character locked in the swing (or never released) for every state.
                foreach (var clip in clips)
                {
                    if (clip == null) continue;
                    if (clip.name.IndexOf(stateName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return clip.length;
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
            // The ANYSTATE -> Hit transition is the only entry point. Firing the trigger AND
            // calling Play restarted the clip against itself, which is why one hit looked like
            // two very fast reactions. While the reaction plays it must not be re-entered at
            // all: repeated HP deltas used to restart it on every snapshot while moving.
            if (Time.time < _hitUntil) return;
            _hitUntil = Time.time + HitReactionSeconds;
            SetTrigger(HitParameter);
        }

        /// <summary>
        /// Builds one inactive slot per authored binding, then enables exactly the selected kind.
        /// The imported KayKit body already contains sword/shield meshes inside the FBX; those are
        /// hidden separately so a snapshot can never show three swords at once.
        /// </summary>
        public void Equip(WeaponKind kind)
        {
            // UnityView calls this on EVERY snapshot (ApplyWeapon), several times per second.
            // Without this guard the slot SetActive calls — and the pose restore that follows them
            // — ran continuously, so the rig was re-entered and re-bound constantly instead of
            // once. WeaponVisuals.Equip has had the same guard for a while.
            if (_equipped == kind && _weaponSlotsBuilt) return;

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
            else
            {
                RestorePose();
                WatchPose();
            }
        }

        /// <summary>
        /// Re-applies the current state after a weapon slot was parented into the rig.
        ///
        /// Adding a transform under an animated hierarchy makes Unity fall back to the bind pose,
        /// and the Animator keeps advancing its own clock without rewriting the bones.
        ///
        /// This must NOT call Animator.Play. Play enters a state directly, bypassing transitions,
        /// so replaying the state that happened to be current restarted a one-shot forever: with a
        /// sword equipped the character sat in Attack at normalizedTime 0,01 for the whole session
        /// (59 consecutive log rows) because Equip runs on every snapshot and re-entered the swing
        /// before it could reach its exit transition. Rebinding instead re-evaluates the graph from
        /// its own parameters, which is what the state machine needs.
        /// </summary>
        private void RestorePose()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return;
            if (!animator.isActiveAndEnabled) return;
            animator.Rebind();
            animator.Update(0f);
        }

        /// <summary>
        /// Keeps the rig posed for a few frames after the weapon hierarchy changed.
        ///
        /// Restoring the pose once, at the end of Equip, was not enough: the slots are built and
        /// toggled over several frames (BuildWeaponSlots instantiates them, then Equip calls
        /// SetActive on each), and EVERY such change under an animated hierarchy drops the rig back
        /// to the bind pose. The in-game log caught the result — hips frozen at -0,083 with every
        /// rotation exactly zero while normalizedTime kept running.
        ///
        /// The watch runs in LateUpdate, after the Animator has evaluated, so a bind pose written
        /// this frame is repaired before rendering. Once the deadline passes it removes itself, so
        /// steady-state cost is zero.
        /// </summary>
        private sealed class PoseWatch : MonoBehaviour
        {
            private CharacterVisuals _owner;
            private float _until;

            public void Configure(CharacterVisuals owner, float seconds)
            {
                _owner = owner;
                _until = Time.time + seconds;
            }

            private void LateUpdate()
            {
                if (_owner == null)
                {
                    Destroy(this);
                    return;
                }

                _owner.RestorePose();

                if (Time.time < _until) return;
                Destroy(this);
            }
        }

        /// <summary>Arms the pose watch for the frames in which weapon slots are being wired.</summary>
        private void WatchPose()
        {
            if (animator == null) return;
            var existing = animator.GetComponent<PoseWatch>();
            if (existing != null)
            {
                existing.Configure(this, PoseWatchSeconds);
                return;
            }
            animator.gameObject.AddComponent<PoseWatch>().Configure(this, PoseWatchSeconds);
        }

        private void BuildWeaponSlots()
        {
            if (_weaponSlotsBuilt) return;
            _weaponSlotsBuilt = true;
            if (weapons == null || weapons.Length == 0) return;

            var right = rightHandSocket != null ? rightHandSocket : ResolveRightHand();
            if (leftHandSocket == null)
            {
                leftHandSocket = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            }

            if (right == null)
            {
                Debug.LogWarning("CharacterVisuals: right hand socket not found; weapons not attached.", this);
                return;
            }

            foreach (var binding in weapons)
            {
                if (binding == null || binding.Prefab == null || _weaponSlots.ContainsKey(binding.Kind)) continue;
                // Лук держится левой рукой (как в 2H_Ranged-анимациях), посох и клинки — правой.
                var socket = binding.Kind == WeaponKind.Bow && leftHandSocket != null ? leftHandSocket : right;
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
                if (binding.Kind == WeaponKind.Bow && !hasBindingCalibration)
                {
                    // Fallback only: the authored binding above already carries the full
                    // calibration (it includes the 90 deg string line). Adding this on top of
                    // authored values would double the turn and break the grip.
                    slot.transform.localRotation *= Quaternion.Euler(0f, 90f, 0f);
                }

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
