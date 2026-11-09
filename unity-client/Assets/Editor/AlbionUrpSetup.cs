using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Albion.Editor
{
    /// <summary>
    /// M10: генерирует URP-ассеты двух профилей качества и профиль пост-обработки.
    /// Запуск: меню <c>Albion/Rendering/Setup URP</c> или
    /// <c>Unity.exe -batchmode -quit -projectPath unity-client -executeMethod Albion.Editor.AlbionUrpSetup.SetupUrp</c>.
    /// </summary>
    /// <remarks>
    /// Ассеты складываются в <c>Assets/Resources/Rendering</c>, потому что <c>QualityProfile</c> грузит их
    /// через <c>Resources.Load</c>: смена профиля не требует перезапуска и пересборки плеера.
    /// </remarks>
    public static class AlbionUrpSetup
    {
        /// <summary>Каталог с URP-ассетами (внутри Resources — для рантайма).</summary>
        public const string RenderingFolder = "Assets/Resources/Rendering";

        /// <summary>URP-ассет desktop-профиля.</summary>
        public const string DesktopAssetPath = RenderingFolder + "/UrpDesktop.asset";

        /// <summary>URP-ассет mobile-профиля.</summary>
        public const string MobileAssetPath = RenderingFolder + "/UrpMobile.asset";

        /// <summary>Renderer data desktop-профиля (deferred).</summary>
        public const string DesktopRendererPath = RenderingFolder + "/UniversalRendererDesktop.asset";

        /// <summary>Renderer data mobile-профиля (forward).</summary>
        public const string MobileRendererPath = RenderingFolder + "/UniversalRendererMobile.asset";

        /// <summary>Профиль пост-обработки (Bloom, Vignette, цветокоррекция).</summary>
        public const string VolumeProfilePath = RenderingFolder + "/AlbionPostProcess.asset";

        private const string PostProcessDataPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        /// <summary>Создаёт ассеты (или пересоздаёт поверх существующих) и назначает desktop по умолчанию.</summary>
        [MenuItem("Albion/Rendering/Setup URP")]
        public static void SetupUrp()
        {
            EnsureFolders();

            var postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            if (postProcessData == null)
            {
                Debug.LogError("URP: не найден PostProcessData по пути " + PostProcessDataPath);
            }

            var desktopRenderer = CreateRenderer(DesktopRendererPath, RenderingMode.Deferred, postProcessData);
            var mobileRenderer = CreateRenderer(MobileRendererPath, RenderingMode.Forward, postProcessData);

            var desktop = CreatePipeline(DesktopAssetPath, desktopRenderer);
            desktop.supportsHDR = true;
            desktop.msaaSampleCount = 4;
            desktop.renderScale = 1f;
            desktop.mainLightShadowmapResolution = 2048;
            desktop.shadowDistance = 60f;
            EnableGpuResidentDrawer(desktop, true);
            EditorUtility.SetDirty(desktop);

            var mobile = CreatePipeline(MobileAssetPath, mobileRenderer);
            mobile.supportsHDR = false;
            mobile.msaaSampleCount = 1;
            mobile.renderScale = 0.8f;
            mobile.mainLightShadowmapResolution = 1024;
            mobile.shadowDistance = 25f;
            EnableGpuResidentDrawer(mobile, false);
            DisableShadows(mobile);
            EditorUtility.SetDirty(mobile);

            CreateVolumeProfile();

            GraphicsSettings.defaultRenderPipeline = desktop;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("URP: ассеты профилей созданы (desktop deferred, mobile forward).");
        }

        /// <summary>
        /// Пакетное переключение профиля для валидации: печатает ключевые параметры обоих ассетов.
        /// </summary>
        [MenuItem("Albion/Rendering/Validate URP")]
        public static void ValidateUrp()
        {
            var desktop = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(DesktopAssetPath);
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileAssetPath);
            if (desktop == null || mobile == null)
            {
                throw new InvalidOperationException("URP: ассеты профилей не найдены. Сначала выполните Setup URP.");
            }

            var desktopRenderer = (UniversalRendererData)desktop.rendererDataList[0];
            var mobileRenderer = (UniversalRendererData)mobile.rendererDataList[0];

            Debug.Log("URP desktop: mode=" + desktopRenderer.renderingMode
                + " hdr=" + desktop.supportsHDR
                + " msaa=" + desktop.msaaSampleCount
                + " scale=" + desktop.renderScale.ToString("0.##")
                + " shadowRes=" + desktop.mainLightShadowmapResolution
                + " shadowDist=" + desktop.shadowDistance.ToString("0.##"));
            Debug.Log("URP mobile: mode=" + mobileRenderer.renderingMode
                + " hdr=" + mobile.supportsHDR
                + " msaa=" + mobile.msaaSampleCount
                + " scale=" + mobile.renderScale.ToString("0.##")
                + " shadowRes=" + mobile.mainLightShadowmapResolution
                + " shadowDist=" + mobile.shadowDistance.ToString("0.##"));

            if (desktopRenderer.renderingMode == mobileRenderer.renderingMode
                || desktop.supportsHDR == mobile.supportsHDR
                || Math.Abs(desktop.renderScale - mobile.renderScale) < 0.01f)
            {
                throw new InvalidOperationException("URP: профили desktop и mobile не различаются.");
            }

            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath) == null)
            {
                throw new InvalidOperationException("URP: профиль пост-обработки не найден.");
            }

            // Проверки, которые видит рантайм: Resources.Load, шейдеры, сцена в Build Settings.
            if (Resources.Load<UniversalRenderPipelineAsset>("Rendering/UrpDesktop") == null
                || Resources.Load<UniversalRenderPipelineAsset>("Rendering/UrpMobile") == null)
            {
                throw new InvalidOperationException("URP: ассеты профилей не грузятся через Resources.");
            }

            if (Resources.Load<VolumeProfile>("Rendering/AlbionPostProcess") == null)
            {
                throw new InvalidOperationException("URP: профиль пост-обработки не грузится через Resources.");
            }

            if (Shader.Find("Albion/Toon Lit") == null || Shader.Find("Albion/Toon Ground") == null)
            {
                throw new InvalidOperationException("M10: toon-шейдеры не найдены (Assets/Shaders).");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(AlbionWorldBuilder.ScenePath) == null)
            {
                throw new InvalidOperationException("M10: сцена арены не найдена — " + AlbionWorldBuilder.ScenePath);
            }

            var sceneInBuild = false;
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && scene.path == AlbionWorldBuilder.ScenePath)
                {
                    sceneInBuild = true;
                }
            }

            if (!sceneInBuild)
            {
                throw new InvalidOperationException("M10: сцена арены не включена в Build Settings.");
            }

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                throw new InvalidOperationException("M10: URP требует Linear color space.");
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Scenes/Arena/OcclusionCullingData.asset") == null)
            {
                Debug.LogWarning("M10: данные occlusion culling не найдены; запеките их в редакторе (Window/Rendering/Occlusion Culling).");
            }

            Debug.Log("URP: валидация профилей и контента пройдена.");
        }

        private static UniversalRendererData CreateRenderer(string path, RenderingMode mode, PostProcessData postProcessData)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(renderer, path);
            }

            renderer.renderingMode = mode;
            if (postProcessData != null)
            {
                renderer.postProcessData = postProcessData;
            }

            ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
            EditorUtility.SetDirty(renderer);
            return renderer;
        }

        private static UniversalRenderPipelineAsset CreatePipeline(string path, ScriptableRendererData renderer)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(asset, path);
            }
            else
            {
                var serialized = new SerializedObject(asset);
                var list = serialized.FindProperty("m_RendererDataList");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return asset;
        }

        private static void EnableGpuResidentDrawer(UniversalRenderPipelineAsset asset, bool enabled)
        {
            var serialized = new SerializedObject(asset);
            var mode = serialized.FindProperty("m_GPUResidentDrawerMode");
            if (mode == null)
            {
                Debug.LogWarning("URP: поле m_GPUResidentDrawerMode не найдено — GPU Resident Drawer не настроен.");
                return;
            }

            mode.intValue = enabled ? 1 : 0;
            var occlusion = serialized.FindProperty("m_GPUResidentDrawerEnableOcclusionCullingInCameras");
            if (occlusion != null)
            {
                occlusion.boolValue = enabled;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DisableShadows(UniversalRenderPipelineAsset asset)
        {
            var serialized = new SerializedObject(asset);
            var mainShadows = serialized.FindProperty("m_MainLightShadowsSupported");
            if (mainShadows != null)
            {
                mainShadows.boolValue = false;
            }

            var additionalShadows = serialized.FindProperty("m_AdditionalLightShadowsSupported");
            if (additionalShadows != null)
            {
                additionalShadows.boolValue = false;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void CreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            var bloom = profile.Has<Bloom>() ? profile.components.Find(component => component is Bloom) as Bloom : profile.Add<Bloom>(true);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);

            var vignette = profile.Has<Vignette>() ? profile.components.Find(component => component is Vignette) as Vignette : profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.4f);

            var color = profile.Has<ColorAdjustments>()
                ? profile.components.Find(component => component is ColorAdjustments) as ColorAdjustments
                : profile.Add<ColorAdjustments>(true);
            color.contrast.Override(12f);
            color.saturation.Override(18f);
            color.postExposure.Override(0.15f);

            EditorUtility.SetDirty(profile);
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Rendering");
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
