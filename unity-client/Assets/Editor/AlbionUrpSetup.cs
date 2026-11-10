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
            // Deferred does not support MSAA; desktop camera uses SMAA instead.
            desktop.msaaSampleCount = 1;
            desktop.renderScale = 1f;
            desktop.mainLightShadowmapResolution = 2048;
            desktop.shadowDistance = 60f;
            desktop.shadowCascadeCount = 2;
            desktop.cascade2Split = 0.35f;
            EnableDesktopShadows(desktop);
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
            // AddObjectToAsset requires a reimport before querying the imported subasset graph.
            AssetDatabase.ImportAsset(VolumeProfilePath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
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

            var desktopRenderers = desktop.rendererDataList;
            var mobileRenderers = mobile.rendererDataList;
            if (desktopRenderers == null || desktopRenderers.Length == 0
                || mobileRenderers == null || mobileRenderers.Length == 0
                || !(desktopRenderers[0] is UniversalRendererData)
                || !(mobileRenderers[0] is UniversalRendererData))
            {
                throw new InvalidOperationException("URP: профили не содержат UniversalRendererData.");
            }

            var desktopRenderer = (UniversalRendererData)desktopRenderers[0];
            var mobileRenderer = (UniversalRendererData)mobileRenderers[0];
            if (desktopRenderer.renderingMode != RenderingMode.Deferred
                || mobileRenderer.renderingMode != RenderingMode.Forward
                || desktopRenderer.postProcessData == null || mobileRenderer.postProcessData == null)
            {
                throw new InvalidOperationException("URP: неверный режим renderer или потерян PostProcessData.");
            }

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

            if (!desktop.supportsMainLightShadows || !desktop.supportsSoftShadows
                || desktop.shadowCascadeCount != 2 || desktop.shadowDistance <= 0f
                || desktop.msaaSampleCount != 1
                || mobile.supportsMainLightShadows || mobile.supportsAdditionalLightShadows)
            {
                throw new InvalidOperationException("URP: неверные настройки теней или MSAA профилей.");
            }

            ValidateVolumeProfile();

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

            ValidateShader("Albion/Toon Lit");
            ValidateShader("Albion/Toon Ground");

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

        private static void EnableDesktopShadows(UniversalRenderPipelineAsset asset)
        {
            // These properties have internal setters in URP 17; configure serialized fields.
            var serialized = new SerializedObject(asset);
            SetRequiredBool(serialized, "m_MainLightShadowsSupported", true);
            SetRequiredBool(serialized, "m_AnyShadowsSupported", true);
            SetRequiredBool(serialized, "m_SoftShadowsSupported", true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRequiredBool(SerializedObject serialized, string name, bool value)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("URP: обязательное поле не найдено: " + name);
            }

            property.boolValue = value;
        }

        private static void DisableShadows(UniversalRenderPipelineAsset asset)
        {
            var serialized = new SerializedObject(asset);
            SetRequiredBool(serialized, "m_MainLightShadowsSupported", false);
            SetRequiredBool(serialized, "m_AdditionalLightShadowsSupported", false);
            SetRequiredBool(serialized, "m_AnyShadowsSupported", false);
            SetRequiredBool(serialized, "m_SoftShadowsSupported", false);

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

            // Old generated profiles can contain missing references after an editor reload.
            profile.components.RemoveAll(component => component == null);

            var bloom = EnsureVolumeComponent<Bloom>(profile);
            bloom.intensity.Override(0.7f);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);

            var vignette = EnsureVolumeComponent<Vignette>(profile);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.4f);

            var color = EnsureVolumeComponent<ColorAdjustments>(profile);
            color.contrast.Override(6f);
            color.saturation.Override(10f);
            color.postExposure.Override(0.5f);

            var tonemapping = EnsureVolumeComponent<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(color);
            EditorUtility.SetDirty(tonemapping);
            profile.Reset();
            EditorUtility.SetDirty(profile);
        }

        private static T EnsureVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.components.Find(candidate => candidate is T) as T;
            if (component == null)
            {
                // Recover an existing subasset if only its profile-list reference was lost.
                foreach (var subasset in AssetDatabase.LoadAllAssetsAtPath(VolumeProfilePath))
                {
                    if (subasset is T existing)
                    {
                        component = existing;
                        profile.components.Add(component);
                        break;
                    }
                }
            }

            if (component == null)
            {
                component = profile.Add<T>(true);
            }

            if (!EditorUtility.IsPersistent(component))
            {
                // VolumeProfile.Add creates an in-memory object, not a saved subasset.
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            else if (AssetDatabase.GetAssetPath(component) != VolumeProfilePath)
            {
                throw new InvalidOperationException("URP: компонент volume принадлежит другому ассету: " + typeof(T).Name);
            }

            component.active = true;
            return component;
        }

        private static void ValidateVolumeProfile()
        {
            // Validate the imported on-disk graph, not merely newly-created in-memory components.
            // Do not SaveAssets here: validation must never make an unsaved repair pass.
            AssetDatabase.ImportAsset(VolumeProfilePath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null || profile.components.Count == 0
                || profile.components.Exists(component => component == null))
            {
                throw new InvalidOperationException("URP: профиль пост-обработки пуст или содержит потерянные компоненты.");
            }

            var bloom = ValidateVolumeComponent<Bloom>(profile);
            var vignette = ValidateVolumeComponent<Vignette>(profile);
            var color = ValidateVolumeComponent<ColorAdjustments>(profile);
            var tonemapping = ValidateVolumeComponent<Tonemapping>(profile);
            if (!bloom.intensity.overrideState || bloom.intensity.value <= 0f
                || !vignette.intensity.overrideState || vignette.intensity.value <= 0f
                || !color.contrast.overrideState || !color.saturation.overrideState
                || !color.postExposure.overrideState
                || !tonemapping.mode.overrideState || tonemapping.mode.value != TonemappingMode.ACES)
            {
                throw new InvalidOperationException("URP: профиль пост-обработки не содержит ожидаемые активные overrides.");
            }
        }

        private static T ValidateVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var matches = profile.components.FindAll(component => component is T);
            if (matches.Count != 1)
            {
                throw new InvalidOperationException("URP: ожидается один компонент " + typeof(T).Name
                    + "; profileMatches=" + matches.Count);
            }

            var component = matches[0];
            var active = component.active;
            var persistent = EditorUtility.IsPersistent(component);
            var subasset = AssetDatabase.IsSubAsset(component);
            var path = AssetDatabase.GetAssetPath(component);
            var hasIdentity = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string guid, out long localId);
            var expectedGuid = AssetDatabase.AssetPathToGUID(VolumeProfilePath);
            var importedMatches = 0;
            var importedAssets = AssetDatabase.LoadAllAssetsAtPath(VolumeProfilePath);
            foreach (var imported in importedAssets)
            {
                // Managed wrappers may change during reimport. Compare saved identity, not reference equality.
                if (imported is T && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(imported, out string importedGuid, out long importedId)
                    && importedGuid == guid && importedId == localId)
                {
                    importedMatches++;
                }
            }

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(profile, out string profileGuid, out long profileId);
            // IsSubAsset reports false for hidden VolumeComponent objects on Unity 6000.3,
            // even after a fresh-process disk load. The persisted GUID/localID plus the
            // independently imported child list proves containment without that unreliable flag.
            if (!active || !persistent || path != VolumeProfilePath
                || !hasIdentity || localId == 0 || guid != expectedGuid || guid != profileGuid
                || localId == profileId || importedMatches != 1)
            {
                throw new InvalidOperationException("URP: неверный сохранённый subasset " + typeof(T).Name
                    + "; active=" + active + "; persistent=" + persistent + "; isSubAsset=" + subasset
                    + "; path=" + path + "; hasIdentity=" + hasIdentity + "; guid=" + guid
                    + "; expectedGuid=" + expectedGuid + "; localId=" + localId
                    + "; importedIdentityMatches=" + importedMatches + "; importedAssetCount=" + importedAssets.Length);
            }

            return (T)component;
        }

        private static void ValidateShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            {
                throw new InvalidOperationException("M10: шейдер не найден, не поддерживается или имеет ошибки: " + name);
            }

            var material = new Material(shader);
            try
            {
                foreach (var pass in new[] { "ForwardLit", "ShadowCaster", "DepthOnly" })
                {
                    if (material.FindPass(pass) < 0)
                    {
                        throw new InvalidOperationException("M10: отсутствует проход " + pass + " у " + name);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
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
