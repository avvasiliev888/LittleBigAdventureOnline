using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Albion.Client
{
    /// <summary>
    /// Два профиля качества M10: <c>desktop</c> (deferred, HDR, MSAA 4x, тени 2048, пост-обработка)
    /// и <c>mobile</c> (forward, без HDR/MSAA/пост-обработки, render scale 0.8, тени 1024).
    /// </summary>
    /// <remarks>
    /// Профиль — это два готовых URP-ассета (ADR-012). Переключение не требует перезапуска плеера:
    /// выбирается переменной окружения <c>ALBION_QUALITY</c>, аргументом <c>-quality desktop|mobile</c>
    /// или дефолтом сборки (<c>ALBION_MOBILE_DEFAULT</c> для мобильной сборки).
    /// </remarks>
    public sealed class QualityProfile : MonoBehaviour
    {
        /// <summary>Имя профиля desktop.</summary>
        public const string DesktopName = "desktop";

        /// <summary>Имя профиля mobile.</summary>
        public const string MobileName = "mobile";

        private const string DesktopAssetPath = "Rendering/UrpDesktop";
        private const string MobileAssetPath = "Rendering/UrpMobile";
        private const string VolumeProfilePath = "Rendering/AlbionPostProcess";

        private Volume _volume;

        /// <summary>Текущий профиль.</summary>
        public Mode Current { get; private set; } = Mode.Desktop;

        /// <summary>Имя профиля для HUD и лога.</summary>
        public string ModeName => Current == Mode.Mobile ? MobileName : DesktopName;

        /// <summary>Профили качества.</summary>
        public enum Mode
        {
            /// <summary>Desktop: deferred, HDR, пост-обработка.</summary>
            Desktop,

            /// <summary>Mobile: forward, без пост-обработки.</summary>
            Mobile,
        }

        /// <summary>
        /// Применяет профиль из окружения/аргументов и обновляет камеру, свет, объём и HUD.
        /// </summary>
        /// <param name="volume">Глобальный volume с пост-обработкой (может быть <c>null</c>).</param>
        public void ApplyFromEnvironment(Volume volume = null)
        {
            _volume = volume;
            Apply(ResolveMode());
        }

        /// <summary>Применяет профиль: подменяет URP-ассет и настройки камеры/качества.</summary>
        /// <param name="mode">Профиль.</param>
        public void Apply(Mode mode)
        {
            Current = mode;

            var asset = Resources.Load<UniversalRenderPipelineAsset>(mode == Mode.Mobile ? MobileAssetPath : DesktopAssetPath);
            if (asset == null)
            {
                Debug.LogError("QualityProfile: не найден URP-ассет профиля " + mode
                    + ". Запустите Albion/Rendering/Setup URP или проверьте Assets/Resources/Rendering.");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = asset;

            var camera = Camera.main;
            if (camera != null)
            {
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = mode == Mode.Desktop;
                cameraData.antialiasing = mode == Mode.Desktop
                    ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
                    : AntialiasingMode.None;
                camera.farClipPlane = mode == Mode.Desktop ? 120f : 70f;
            }

            var postProcessProfile = Resources.Load<VolumeProfile>(VolumeProfilePath);
            if (_volume == null)
            {
                _volume = CreateGlobalVolume(postProcessProfile);
            }

            if (_volume != null)
            {
                if (postProcessProfile != null)
                {
                    _volume.sharedProfile = postProcessProfile;
                }

                _volume.weight = mode == Mode.Desktop ? 1f : 0f;
            }

            QualitySettings.shadowDistance = mode == Mode.Desktop ? 60f : 25f;
            QualitySettings.antiAliasing = 0;
            QualitySettings.vSyncCount = mode == Mode.Desktop ? 0 : 1;
            Application.targetFrameRate = mode == Mode.Desktop ? 120 : 30;

            Debug.Log("QualityProfile: " + ModeName
                + " (deferred=" + (mode == Mode.Desktop) + ", post=" + (mode == Mode.Desktop) + ")");
        }

        /// <summary>Включает/выключает пост-обработку без смены URP-ассета (для тестов и HUD).</summary>
        /// <param name="enabled">Включена ли пост-обработка.</param>
        public void SetPostProcessing(bool enabled)
        {
            var camera = Camera.main;
            if (camera != null)
            {
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = enabled;
            }

            if (_volume != null)
            {
                _volume.weight = enabled ? 1f : 0f;
            }
        }

        private static Volume CreateGlobalVolume(VolumeProfile profile)
        {
            if (profile == null)
            {
                return null;
            }

            var volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
            return volume;
        }

        private static Mode ResolveMode()
        {
            var value = Environment.GetEnvironmentVariable("ALBION_QUALITY");

            if (string.IsNullOrEmpty(value))
            {
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "-quality", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        value = args[i + 1];
                        break;
                    }

                    if (args[i].StartsWith("-quality=", StringComparison.OrdinalIgnoreCase))
                    {
                        value = args[i].Substring("-quality=".Length);
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(value))
            {
#if ALBION_MOBILE_DEFAULT
                return Mode.Mobile;
#else
                return Mode.Desktop;
#endif
            }

            return string.Equals(value, MobileName, StringComparison.OrdinalIgnoreCase) ? Mode.Mobile : Mode.Desktop;
        }
    }
}
