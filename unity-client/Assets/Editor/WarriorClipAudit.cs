using System;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Measures how much each animation clip actually moves the body.
    ///
    /// The in-game pose log showed the warrior standing in a pose that was symmetric
    /// (left foot == right foot, left hand == right hand, to three decimals) and constant for
    /// twenty seconds while the clip's normalised time ran 0 -> 1 and wrapped. A symmetric,
    /// unchanging pose is the bind pose: the clip is being played, but nothing drives the bones.
    ///
    /// This samples every muscle curve of every clip and reports its value range, which separates
    /// the two possible causes:
    ///   * ranges near zero   -> the CLIP is a still pose (bad import / wrong take);
    ///   * ranges are healthy -> the clip is fine and the failure is in retargeting at runtime.
    /// </summary>
    public static class WarriorClipAudit
    {
        private const int Samples = 24;

        /// <summary>Muscle curves worth printing by name; they define standing vs crouching.</summary>
        private static readonly string[] Spotlight =
        {
            "Left Upper Leg Front-Back",
            "Right Upper Leg Front-Back",
            "Left Lower Leg Stretch",
            "Right Lower Leg Stretch",
            "Spine Front-Back",
            "Left Arm Down-Up",
            "Right Arm Down-Up",
            "Head Turn Left-Right",
            "RootT.y",
        };

        [MenuItem("Albion/Characters/Audit Animation Clips")]
        public static void Audit()
        {
            var guids = AssetDatabase.FindAssets("t:AnimationClip", new[]
            {
                "Assets/AllStarCharacterLibrary/Models",
            });

            Debug.Log("########## CLIP AUDIT START ##########");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".FBX", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(asset is AnimationClip clip)) continue;
                    if (clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
                    Audit(clip, path);
                }
            }
            Debug.Log("########## CLIP AUDIT END ##########");
        }

        private static void Audit(AnimationClip clip, string path)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var animated = 0;
            var biggestRange = 0f;
            var biggestCurve = string.Empty;

            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;

                var range = Range(curve, clip.length);
                if (range > 0.02f) animated++;
                if (range > biggestRange)
                {
                    biggestRange = range;
                    biggestCurve = binding.propertyName;
                }
            }

            var line = "[clip] " + clip.name.PadRight(18)
                + " len=" + clip.length.ToString("0.###").PadLeft(6)
                + " human=" + (clip.isHumanMotion ? "Y" : "N")
                + " curves=" + bindings.Length.ToString().PadLeft(4)
                + " moving=" + animated.ToString().PadLeft(4)
                + " biggest=" + biggestRange.ToString("0.000")
                + " (" + biggestCurve + ")";
            Debug.Log(line);

            foreach (var name in Spotlight)
            {
                foreach (var binding in bindings)
                {
                    if (!string.Equals(binding.propertyName, name, StringComparison.Ordinal)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null) break;
                    var range = Range(curve, clip.length);
                    Debug.Log("[clip]    " + name.PadRight(28)
                        + " range=" + range.ToString("0.000")
                        + "  0%=" + curve.Evaluate(0f).ToString("0.000")
                        + "  50%=" + curve.Evaluate(clip.length * 0.5f).ToString("0.000"));
                    break;
                }
            }
        }

        private static float Range(AnimationCurve curve, float length)
        {
            if (length <= 0f) return 0f;
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            for (var i = 0; i < Samples; i++)
            {
                var value = curve.Evaluate(length * i / (Samples - 1f));
                if (value < min) min = value;
                if (value > max) max = value;
            }
            return max - min;
        }
    }
}
