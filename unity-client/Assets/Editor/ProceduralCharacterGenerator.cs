using Albion.Client;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Stage 8 generator entry point. The actual meshes/materials are built by the runtime
    /// <see cref="ProceduralHumanoidVisual"/>, so the same code path is exercised in the Editor,
    /// in Play Mode, in the smoke test and in player builds. This menu configures the asset and
    /// generates/validates all four archetypes (hero, blue/yellow/red mobs).
    /// </summary>
    public static class ProceduralCharacterGenerator
    {
        public const string MenuPath = "Albion/Characters/Generate Procedural Humanoids";
        public const string SettingsPath = "Assets/Resources/Characters/WarriorVisual.asset";

        /// <summary>Configures the procedural mode and validates every generated archetype.</summary>
        [MenuItem(MenuPath)]
        public static void Generate()
        {
            var settings = AlbionCharacterSetup.LoadSettings();
            settings.Procedural = true;
            settings.Profile = ProceduralCharacterProfile.Hero;
            settings.Height = 1.95f;
            settings.Yaw = 0f;
            settings.GroundOffset = 0f;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            ProceduralCharacterChecks.ValidateAllProfiles();
            ProceduralCharacterChecks.Validate(settings);
            Debug.Log("Stage 8: процедурные гуманоиды сгенерированы и провалидированы (hero, blue, yellow, red).");
        }

        /// <summary>Batch/legacy compatibility path used by <see cref="AlbionCharacterSetup.Configure"/>.</summary>
        public static void Configure()
        {
            Generate();
        }

        /// <summary>Creates a transient gallery for an operator to inspect in the Scene view.</summary>
        [MenuItem("Albion/Characters/Preview Procedural Gallery")]
        public static void PreviewGallery()
        {
            var root = new GameObject("Procedural Gallery (transient)");
            var profiles = new[]
            {
                ProceduralCharacterProfile.Hero,
                ProceduralCharacterProfile.BlueMob,
                ProceduralCharacterProfile.YellowMob,
                ProceduralCharacterProfile.RedMob,
            };
            for (var i = 0; i < profiles.Length; i++)
            {
                var visual = ProceduralHumanoidVisual.CreatePlayer(
                    root.transform, (uint)(i + 1), profiles[i] == ProceduralCharacterProfile.RedMob ? 2.55f
                        : profiles[i] == ProceduralCharacterProfile.YellowMob ? 2.15f
                        : profiles[i] == ProceduralCharacterProfile.BlueMob ? 1.95f : 1.95f,
                    profiles[i]);
                visual.transform.position = new Vector3((i - 1.5f) * 1.8f, 0f, 0f);
                visual.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            }
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Stage 8: временная галерея процедурных моделей создана. Не сохраняйте сцену; удалите объект после осмотра.");
        }
    }
}
