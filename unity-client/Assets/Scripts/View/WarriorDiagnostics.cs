using System.Text;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Writes the facts about the warrior's pose into Player.log, so animation problems are read
    /// from real numbers instead of guessed at from a description.
    ///
    /// Every half second it logs: the Animator state actually playing, the clip's normalised time,
    /// the height of both feet / both hands / the head above the character root, how far the
    /// grounding anchor moved the body, and how many times the equipped weapon teleported between
    /// frames. That last counter is the "bow flickers between positions" symptom measured directly.
    ///
    /// Player.log is at %USERPROFILE%\AppData\LocalLow\DefaultCompany\Albion Prototype\
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WarriorDiagnostics : MonoBehaviour
    {
        /// <summary>Lowest mesh point seen by the grounding anchor this frame, in root space.</summary>
        public static float GroundMinY = float.NaN;

        /// <summary>
        /// Which renderer produced <see cref="GroundMinY"/>. The grounding anchor measures every
        /// mesh under the character, and a weapon hanging from the hand reaches below the feet.
        /// When that weapon is counted, the whole body is lifted off the floor — the "hovers with
        /// tucked legs" report. Naming the object is the only way to know which filter failed.
        /// </summary>
        public static string GroundMinSource = "-";

        private const float ReportInterval = 0.5f;
        /// <summary>A weapon moving further than this between frames is a teleport, not animation.</summary>
        private const float JumpThreshold = 0.08f;

        private static readonly string[] StateNames =
        {
            "Idle", "Locomotion", "Attack", "Hit", "Dead", "BowAim", "BowShoot", "BowIdle",
        };

        private Animator _animator;
        private CharacterVisuals _visuals;
        private Transform _calibration;
        private Transform _leftHand;
        private Transform _rightHand;
        private Transform _head;
        private Transform _leftFoot;
        private Transform _rightFoot;
        /// <summary>Hips bone — the root of the pose, so what it receives proves whether the Animator drove the rig.</summary>
        private Transform _hips;

        private float _nextReport;
        private float _startTime;
        private Vector3 _lastWeaponPosition;
        private bool _hasWeaponPosition;
        private int _weaponJumps;
        private float _lastJumpTime = -1f;
        private float _lastJumpDistance;
        private float _lastJumpDeltaY;
        private string _lastState = string.Empty;
        private int _stateChanges;
        /// <summary>Frames where the body yaw jumped more than 120 degrees: a visible spin.</summary>
        private int _yawFlips;
        /// <summary>Locomotion state switches; must stay flat during a straight run.</summary>
        private int _locomotionSwitches;
        private float _lastYaw = float.NaN;
        private string _lastLocomotionState = string.Empty;
        private bool _hasSpeedParameter;
        private bool _rigDumped;

        /// <summary>
        /// The pause flag is static because the visual is assembled across several frames: the
        /// weapon slot is parented into the hand after the body exists, and the pose sampled in
        /// that window is a half-built rig, not a rendering fault worth reporting.
        /// </summary>
        private static bool _paused;

        /// <summary>Stops the periodic pose rows while the visual is still being assembled.</summary>
        public static void Pause()
        {
            _paused = true;
        }

        /// <summary>Resumes reporting; the timer restarts so the next row is immediate.</summary>
        public static void Resume()
        {
            _paused = false;
        }

        public void Configure(Animator animator, Transform calibration)
        {
            _animator = animator;
            _calibration = calibration;
            Resolve();
        }

        private void Awake() => Resolve();

        private void Resolve()
        {
            _startTime = Time.time;
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_visuals == null) _visuals = GetComponent<CharacterVisuals>()
                ?? GetComponentInParent<CharacterVisuals>();
            if (_animator == null) return;

            _hasSpeedParameter = false;
            if (_animator.runtimeAnimatorController != null)
            {
                foreach (var parameter in _animator.parameters)
                {
                    if (parameter.name == "Speed") { _hasSpeedParameter = true; break; }
                }
            }

            if (!_animator.isHuman) return;
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            // Only once: Resolve also runs from LateUpdate while bones are still null, and each
            // extra call printed a duplicate rig dump that made the log look like several
            // characters were being created.
            if (!_rigDumped)
            {
                _rigDumped = true;
                DumpRig();
            }
        }

        private void LateUpdate()
        {
            if (_animator == null) { Resolve(); return; }
            if (_leftFoot == null) Resolve();

            TrackWeaponJump();
            TrackFacingAndLocomotion();

            var state = CurrentStateName();
            if (state != _lastState)
            {
                _lastState = state;
                _stateChanges++;
                Debug.Log("[diag] STATE -> " + state
                    + "  t=" + Elapsed().ToString("0.00")
                    + "  speed=" + Speed()
                    + "  eq=" + Equipped());
            }

            if (_paused) return;

            if (Time.time < _nextReport) return;
            _nextReport = Time.time + ReportInterval;
            Report(state);
        }

        /// <summary>
        /// Counts the two symptoms the customer reported, so the fix is proven rather than claimed.
        ///
        /// `flips`: the body yaw jumped more than 120 degrees in one frame. That is the "arrived at
        /// the clicked point and turned around" report - a spin is never animation, it is the facing
        /// root being rewritten. `loco`: the locomotion state changed. On a straight run it must not
        /// change at all; any growth is the "twitch in the middle of a run" report.
        /// </summary>
        private void TrackFacingAndLocomotion()
        {
            var root = _calibration != null ? _calibration : transform;
            var yaw = root.eulerAngles.y;
            if (!float.IsNaN(_lastYaw))
            {
                var delta = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
                if (delta > 120f) _yawFlips++;
            }

            _lastYaw = yaw;

            var state = CurrentStateName();
            if (!string.IsNullOrEmpty(_lastLocomotionState) && state != _lastLocomotionState
                && (state == "Locomotion" || _lastLocomotionState == "Locomotion"))
            {
                _locomotionSwitches++;
            }

            _lastLocomotionState = state;
        }

        private void Report(string state)
        {
            var builder = new StringBuilder("[diag] ");
            // Which object this row belongs to. Two diagnostics components were writing to the same
            // log and their rows were indistinguishable, which made one frozen character look like
            // one flickering character.
            builder.Append('[').Append(OwnerName()).Append("] ");
            builder.Append('t').Append(Elapsed().ToString("0.0"));
            builder.Append("  state=").Append(state);
            builder.Append("  norm=").Append(Normalized().ToString("0.00"));
            builder.Append("  speed=").Append(Speed());
            builder.Append("  eq=").Append(Equipped());

            // Heights relative to the character root. Healthy idle: feet ~0, head ~1.8.
            // Feet far above 0 means the body is lifted off the ground — the "hovering with
            // tucked legs" report.
            builder.Append("  |  footL=").Append(Height(_leftFoot));
            builder.Append(" footR=").Append(Height(_rightFoot));
            builder.Append(" handL=").Append(Height(_leftHand));
            builder.Append(" handR=").Append(Height(_rightHand));
            builder.Append(" head=").Append(Height(_head));

            if (_calibration != null)
                builder.Append("  anchorY=").Append(_calibration.localPosition.y.ToString("0.000"));
            if (!float.IsNaN(GroundMinY))
                builder.Append(" meshMinY=").Append(GroundMinY.ToString("0.000"))
                    .Append('(').Append(GroundMinSource).Append(')');

            builder.Append("  |  jumps=").Append(_weaponJumps);
            if (_lastJumpTime >= 0f)
            {
                builder.Append(" lastJump@").Append(_lastJumpTime.ToString("0.00"))
                    .Append(" dist=").Append(_lastJumpDistance.ToString("0.000"))
                    .Append(" dY=").Append(_lastJumpDeltaY.ToString("0.000"));
            }

            builder.Append("  changes=").Append(_stateChanges);

            // The two counters the facing and jitter fixes are judged by. `flips` counts frames
            // where the body yaw jumped more than 120 degrees - the "arrived at the point and
            // turned around" report measured directly, so it can be proven fixed instead of
            // argued about. `loco` counts locomotion state switches; during a straight run it
            // must not grow at all.
            builder.Append(" flips=").Append(_yawFlips);
            builder.Append(" loco=").Append(_locomotionSwitches);

            // The decisive block for "the clip plays but the body never moves".
            //  layer/animSpeed  whether the graph is actually advancing the layer at all
            //  hips pos/rot     what the Animator WROTE onto the bone this frame. If these are
            //                   frozen while norm advances, the Animator is not driving the rig
            //                   (the pose we see is a leftover); if they move while head/foot stay
            //                   put, something downstream (grounding/aim pose) cancels the pose.
            builder.Append("  |  ");
            if (_animator != null)
            {
                builder.Append("layer=").Append(_animator.GetLayerWeight(0).ToString("0.00"));
                builder.Append(" animSpeed=").Append(_animator.speed.ToString("0.00"));
                builder.Append(" upd=").Append(_animator.updateMode.ToString());
            }
            if (_hips != null)
            {
                var local = _hips.localPosition;
                var euler = _hips.localRotation.eulerAngles;
                builder.Append(" hips=").Append(local.x.ToString("0.000")).Append(',')
                    .Append(local.y.ToString("0.000")).Append(',')
                    .Append(local.z.ToString("0.000"));
                builder.Append(" hipsRot=").Append(euler.x.ToString("0.0")).Append(',')
                    .Append(euler.y.ToString("0.0")).Append(',')
                    .Append(euler.z.ToString("0.0"));
            }
            Debug.Log(builder.ToString());
        }

        /// <summary>
        /// One-shot audit of the rig wiring, logged when the character is created.
        ///
        /// A Humanoid Animator only drives bones it can map through the Avatar. A bone that was
        /// re-parented, renamed or duplicated after the Avatar was built silently stops receiving
        /// poses, and the visible body freezes in the bind pose while the state machine keeps
        /// advancing. This prints the mapping for the load-bearing bones plus the socket wiring, so
        /// a frozen pose is diagnosed from one line instead of a rebuild cycle per hypothesis.
        /// </summary>
        private void DumpRig()
        {
            if (_animator == null || !_animator.isHuman) return;
            var builder = new StringBuilder("[rig] avatar=")
                .Append(_animator.avatar != null ? _animator.avatar.name : "NULL")
                .Append(" valid=").Append(_animator.avatar != null && _animator.avatar.isValid)
                .Append(" human=").Append(_animator.isHuman)
                .Append(" enabled=").Append(_animator.enabled)
                .Append(" culling=").Append(_animator.cullingMode);

            foreach (var bone in new[]
                     {
                         HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
                         HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                         HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                     })
            {
                var transform = _animator.GetBoneTransform(bone);
                builder.Append("  ").Append(bone).Append('=')
                    .Append(transform != null ? transform.name : "NULL");
            }
            Debug.Log(builder.ToString());

            // Every transform whose name looks like an equipment socket, with its actual parent.
            // A socket parented under a hand is normal; a socket that ended up parented under the
            // HIPS would drag the whole skeleton when it is repositioned.
            foreach (var candidate in _animator.GetComponentsInChildren<Transform>(true))
            {
                if (!candidate.name.EndsWith("Socket", System.StringComparison.Ordinal)) continue;
                Debug.Log("[rig] socket " + candidate.name
                    + " parent=" + (candidate.parent != null ? candidate.parent.name : "NULL")
                    + " localScale=" + candidate.localScale.ToString("0.###")
                    + " active=" + candidate.gameObject.activeInHierarchy);
            }
        }

        private float Elapsed() => Time.time - _startTime;

        /// <summary>Identifies the owning object in every row; several characters log to one file.</summary>
        private string OwnerName()
        {
            var visual = _visuals != null ? _visuals.gameObject : gameObject;
            return visual.name + "#" + visual.GetInstanceID().ToString();
        }

        private string Equipped() => _visuals != null ? _visuals.Equipped.ToString() : "?";

        private string Speed()
        {
            if (_animator == null || !_hasSpeedParameter) return "-";
            return _animator.GetFloat("Speed").ToString("0.##");
        }

        private float Normalized()
        {
            if (_animator == null) return 0f;
            return _animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
        }

        private string CurrentStateName()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return "none";
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            foreach (var candidate in StateNames)
            {
                if (info.IsName(candidate)) return candidate;
            }

            // An unrecognised hash is worthless in a log. Report the CLIP that is actually playing
            // plus its length, which identifies the state even when the state name is unknown:
            // two hashes previously alternated ~25 times a second and could not be named at all.
            var clips = _animator.GetCurrentAnimatorClipInfo(0);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
            {
                return clips[0].clip.name + "(clip)"
                    + " len=" + clips[0].clip.length.ToString("0.###")
                    + " w=" + clips[0].weight.ToString("0.00");
            }

            return "h" + info.shortNameHash;
        }

        private string Height(Transform bone)
        {
            if (bone == null) return "-";
            return (bone.position.y - transform.position.y).ToString("0.000");
        }

        /// <summary>
        /// Counts frames where the equipped weapon teleports. The weapon is parented to a hand
        /// socket, so if it jumps while the hand does not, something is writing the weapon's
        /// transform directly — that is a real defect, not an animation.
        /// </summary>
        private void TrackWeaponJump()
        {
            var weapon = _visuals != null ? _visuals.EquippedTransform : null;
            if (weapon == null)
            {
                _hasWeaponPosition = false;
                return;
            }

            var position = weapon.position;
            if (_hasWeaponPosition)
            {
                var distance = Vector3.Distance(position, _lastWeaponPosition);
                if (distance > JumpThreshold)
                {
                    _weaponJumps++;
                    _lastJumpTime = Elapsed();
                    _lastJumpDistance = distance;
                    _lastJumpDeltaY = position.y - _lastWeaponPosition.y;
                }
            }

            _lastWeaponPosition = position;
            _hasWeaponPosition = true;
        }
    }
}
