using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Rebuilds the transition graph of the player controller.
    ///
    /// Every state existed, but only Idle and Locomotion were wired — each by a single one-way
    /// transition. Attack, Hit, Dead, BowAim and BowShoot had NO transitions at all, so once the
    /// runtime faded into one of them nothing could ever leave: the character froze mid-swing, the
    /// hit reaction held forever, death never played and the bow flickered between states.
    ///
    /// The graph is written through the AnimatorController API rather than by hand-editing the
    /// .controller YAML, because a wrong fileID there fails silently — the Animator just shows an
    /// empty pose, with no error to chase.
    /// </summary>
    public static class WarriorControllerFix
    {
        private const string ControllerPath = AlbionAnimationSetup.ControllerPath;

        private const string Idle = "Idle";
        private const string Locomotion = "Locomotion";
        private const string Attack = "Attack";
        private const string Hit = "Hit";
        private const string Dead = "Dead";
        private const string BowAim = "BowAim";
        private const string BowShoot = "BowShoot";
        private const string BowIdle = "BowIdle";

        private const string SpeedParameter = "Speed";

        /// <summary>
        /// Where each state's clip really lives. Assigning through the API is the only reliable
        /// way: a hand-written .controller reference is a raw fileID, and Idle and Dead had one
        /// that does not exist inside their FBX (1827226128182048838 instead of 7400000). Unity
        /// resolves a bad fileID to null without any error, so both states played NOTHING — the
        /// character kept whatever pose it was already in. That is exactly what "freezes instead
        /// of playing" and "hovers with tucked legs" looked like.
        /// </summary>
        private static readonly (string State, string AssetPath, string ClipName)[] Motions =
        {
            (Idle, "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HIdle.fbx", "1HIdle"),
            (Dead, "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HDeathA.fbx", "1HDeathA"),
            (Attack, "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HAttack.fbx", "1HAttack"),
            (Hit, "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HPain.fbx", "1HPain"),
            (BowAim, "Assets/AllStarCharacterLibrary/Models/Standing Aim Idle 01.fbx", "BowAim"),
            (BowShoot, "Assets/AllStarCharacterLibrary/Models/Standing Aim Recoil.fbx", "BowShoot"),
            (BowIdle, "Assets/AllStarCharacterLibrary/Models/Standing Idle 01.fbx", "BowIdle"),
        };

        [MenuItem("Albion/Characters/Fix Warrior Animation Graph")]
        public static void Fix()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new InvalidOperationException("Controller missing: " + ControllerPath);

            var machine = controller.layers[0].stateMachine;
            var states = machine.states.Select(entry => entry.state).ToArray();

            AnimatorState Find(string name)
            {
                var state = states.FirstOrDefault(item => item.name == name);
                if (state == null)
                {
                    throw new InvalidOperationException(
                        "State '" + name + "' not found in " + ControllerPath
                        + ". Found: " + string.Join(", ", states.Select(item => item.name)));
                }
                return state;
            }

            var idle = Find(Idle);
            var locomotion = Find(Locomotion);
            var attack = Find(Attack);
            var hit = Find(Hit);
            var dead = Find(Dead);
            var bowAim = Find(BowAim);
            var bowShoot = Find(BowShoot);
            // BowIdle is still a valid state the runtime may play; validated here so a rename
            // fails loudly instead of leaving the animation graph silently inconsistent.
            Find(BowIdle);

            EnsureParameters(controller);
            BindMotions(controller, Find);

            machine.defaultState = idle;

            // Start from a clean graph so repeated runs cannot stack duplicate transitions.
            foreach (var state in states) state.transitions = Array.Empty<AnimatorStateTransition>();
            foreach (var transition in machine.anyStateTransitions.ToArray())
                machine.RemoveAnyStateTransition(transition);

            // ── One-shots interrupt anything ─────────────────────────────────────────────
            // Death is evaluated first so it wins over a hit or a swing landing on the same frame.
            AddAnyState(machine, dead, "Dead", 0.1f, true);
            AddAnyState(machine, hit, "Hit", 0.05f, false);
            AddAnyState(machine, attack, "Attack", 0.05f, false);

            // ── One-shots must hand control back, or they hold the character forever ──────
            AddExit(attack, locomotion, 0.9f);
            AddExit(hit, locomotion, 0.9f);
            // Death deliberately has no exit: it holds its final frame.

            // ── Locomotion ───────────────────────────────────────────────────────────────
            AddSpeedTransition(idle, locomotion, AnimatorConditionMode.Greater, 0.1f, 0.1f);
            AddSpeedTransition(locomotion, idle, AnimatorConditionMode.Less, 0.100001f, 0.1f);

            // ── Bow ──────────────────────────────────────────────────────────────────────
            // BowShoot hands control back to the aim pose; the runtime selects the bow states.
            //
            // There used to be a BowAim -> BowIdle and a BowIdle -> BowAim pair as well. Both had
            // hasExitTime 0.99 and no conditions, so they took turns firing every time a clip
            // finished and the character visibly flickered between two poses forever. Aim and idle
            // are chosen by the game, never by the graph, so those two edges are gone.
            AddExit(bowShoot, bowAim, 0.9f);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log("[warrior-graph] Rebuilt. AnyState: " + machine.anyStateTransitions.Length
                + ", default: " + machine.defaultState.name);
            foreach (var state in states)
            {
                var motion = state.motion != null ? state.motion.name : "*** NO MOTION ***";
                Debug.Log("[warrior-graph] " + state.name + " (" + motion + ") -> "
                    + string.Join(", ", state.transitions.Select(t =>
                        (t.destinationState != null ? t.destinationState.name : "exit")
                        + (t.hasExitTime ? " @exit" + t.exitTime : " [cond]"))));
            }
        }

        /// <summary>
        /// Loads each state's clip out of its FBX and assigns it. Any state whose clip cannot be
        /// found is reported loudly instead of silently playing nothing.
        /// </summary>
        private static void BindMotions(AnimatorController controller, Func<string, AnimatorState> find)
        {
            foreach (var entry in Motions)
            {
                var state = find(entry.State);
                var clip = LoadClip(entry.AssetPath, entry.ClipName);
                if (clip == null)
                {
                    Debug.LogError("[warrior-graph] clip '" + entry.ClipName + "' not found in "
                        + entry.AssetPath + " — state '" + entry.State + "' will play nothing.");
                    continue;
                }

                if (state.motion == clip) continue;
                state.motion = clip;
                Debug.Log("[warrior-graph] bound " + entry.State + " <- " + clip.name
                    + " (" + clip.length.ToString("0.###") + " s, human=" + clip.isHumanMotion + ")");
            }
        }

        private static AnimationClip LoadClip(string assetPath, string clipName)
        {            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0) return null;

            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }

            // Fall back to the first real clip in the file rather than returning null.
            foreach (var asset in assets)
            {
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            }

            return null;
        }

        /// <summary>
        /// Files whose clips must repeat. Listed by FBX path; the importer decides the clip list.
        /// </summary>
        private static readonly string[] LoopingClips =
        {
            "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HIdle.fbx",
            "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HCombatIdle.fbx",
            "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HWalkF.fbx",
            "Assets/AllStarCharacterLibrary/Models/RootAnimsMale/BaseMale@1HCombatRunF.fbx",
            "Assets/AllStarCharacterLibrary/Models/Standing Aim Idle 01.fbx",
            "Assets/AllStarCharacterLibrary/Models/Standing Idle 01.fbx",
        };

        /// <summary>
        /// Makes the resting and locomotion clips repeat. Looping is a CLIP property, so it has to
        /// be written through the ModelImporter: editing AnimationClip settings on a generated clip
        /// is discarded on the next reimport, and hand-editing the .meta rewrote the whole file and
        /// destroyed the avatar mapping.
        ///
        /// BaseMale@1HIdle ships with an empty clipAnimations list, which means "import the default
        /// take" — and the default take is not looping. Idle therefore played once (2.3 s) and then
        /// held its final pose, which is the statue the character became while standing still.
        /// Walk and Run already loop; they are listed only to repair them if they ever regress.
        /// </summary>
        /// <summary>
        /// Reports what each resting clip actually animates.
        ///
        /// The in-game pose log showed Idle sitting in a pose identical to three decimal places at
        /// t=0, t=7 s and t=22 s while the clip's normalised time ran 0 -> 0.98 and wrapped. A clip
        /// that plays but never moves the bones is empty or drives the wrong paths, and no amount
        /// of state-graph wiring can fix that — so this counts the curves and samples the hips.
        /// </summary>
        [MenuItem("Albion/Characters/Inspect Warrior Clip Curves")]
        public static void InspectCurves()
        {
            foreach (var path in LoopingClips)
            {
                var clip = LoadClip(path, System.IO.Path.GetFileNameWithoutExtension(path)
                    .Replace("BaseMale@", string.Empty).Replace(".fbx", string.Empty));
                if (clip == null)
                {
                    Debug.LogWarning("[curves] no clip at " + path);
                    continue;
                }

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var hips = 0;
                var firstBone = string.Empty;
                foreach (var binding in bindings)
                {
                    if (binding.path.EndsWith("Hips")) hips++;
                    if (firstBone.Length == 0) firstBone = binding.path + "." + binding.propertyName;
                }

                // Sample one driven property at the start and the middle: equal values mean the
                // clip holds a still pose even though its length and loop flag look healthy.
                var report = "[curves] " + clip.name + " len=" + clip.length.ToString("0.###")
                    + " curves=" + bindings.Length + " loop=" + AnimationUtility.GetAnimationClipSettings(clip).loopTime;

                // What KIND of curves the clip carries. A humanoid clip that only has root-motion
                // curves ("MotionT"/"RootT") and no muscle curves cannot move the body at all, and
                // would look exactly like the frozen Idle pose in the in-game log.
                var groups = new System.Collections.Generic.Dictionary<string, int>();
                var bones = new System.Collections.Generic.HashSet<string>();
                foreach (var binding in bindings)
                {
                    var dot = binding.propertyName.IndexOf('.');
                    var group = dot > 0 ? binding.propertyName.Substring(0, dot) : binding.propertyName;
                    groups.TryGetValue(group, out var count);
                    groups[group] = count + 1;
                    if (!string.IsNullOrEmpty(binding.path)) bones.Add(binding.path);
                }

                var summary = new System.Text.StringBuilder();
                foreach (var pair in groups) summary.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
                report += "  groups[" + summary.ToString().Trim() + "] bonePaths=" + bones.Count;

                if (bindings.Length > 0)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
                    if (curve != null && curve.length > 0)
                    {
                        var atStart = curve.Evaluate(0f);
                        var atMid = curve.Evaluate(clip.length * 0.5f);
                        report += "  " + firstBone + " delta=" + Mathf.Abs(atMid - atStart).ToString("0.0000");
                    }
                }

                Debug.Log(report);
            }
        }

        [MenuItem("Albion/Characters/Loop Warrior Idle And Locomotion Clips")]
        public static void LoopClips()
        {
            foreach (var path in LoopingClips)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[warrior-loop] no ModelImporter at " + path);
                    continue;
                }

                // Start from whatever the importer currently uses: the explicit list when the file
                // has one, otherwise the generated defaults.
                var clips = importer.clipAnimations.Length > 0
                    ? importer.clipAnimations
                    : importer.defaultClipAnimations;
                if (clips.Length == 0)
                {
                    Debug.LogWarning("[warrior-loop] no clips to configure at " + path);
                    continue;
                }

                var changed = false;
                for (var i = 0; i < clips.Length; i++)
                {
                    if (clips[i].loopTime) continue;
                    clips[i].loopTime = true;
                    changed = true;
                }

                if (!changed) continue;

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                Debug.Log("[warrior-loop] " + path + " -> looping (" + clips.Length + " take(s))");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Adds the parameters the runtime drives, leaving existing ones untouched.</summary>
        private static void EnsureParameters(AnimatorController controller)
        {
            var existing = controller.parameters.Select(parameter => parameter.name).ToArray();

            void Ensure(string name, AnimatorControllerParameterType type)
            {
                if (existing.Contains(name)) return;
                controller.AddParameter(name, type);
            }

            Ensure(SpeedParameter, AnimatorControllerParameterType.Float);
            Ensure("Attack", AnimatorControllerParameterType.Trigger);
            Ensure("Hit", AnimatorControllerParameterType.Trigger);
            Ensure("Dead", AnimatorControllerParameterType.Bool);
            Ensure("Bow", AnimatorControllerParameterType.Bool);
        }

        private static void AddAnyState(AnimatorStateMachine machine, AnimatorState destination,
            string parameter, float duration, bool _)
        {
            var transition = machine.AddAnyStateTransition(destination);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
        }

        /// <summary>
        /// Returns to locomotion once the one-shot has played through. Exit time is a fraction of
        /// the clip, so 0.9 hands control back just before the final frame and avoids a dead pose.
        /// </summary>
        private static void AddExit(AnimatorState from, AnimatorState destination, float exitTime)
        {
            var transition = from.AddTransition(destination);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = 0.1f;
            transition.hasFixedDuration = true;
            transition.canTransitionToSelf = false;
        }

        private static void AddSpeedTransition(AnimatorState from, AnimatorState destination,
            AnimatorConditionMode mode, float threshold, float duration)
        {
            var transition = from.AddTransition(destination);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.AddCondition(mode, threshold, SpeedParameter);
        }
    }
}
