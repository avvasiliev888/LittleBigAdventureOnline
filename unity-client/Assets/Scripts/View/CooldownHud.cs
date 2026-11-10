using Client.Network;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Bottom-centre ability indicator. The sword is coloured while the ability is ready and
    /// pale grey during cooldown; a coloured copy fills bottom-up as the cooldown recovers.
    /// Cooldown values are read from the same prediction/reconciliation state as the HUD.
    /// </summary>
    public sealed class CooldownHud : MonoBehaviour
    {
        private const float IconSize = 72f;
        private const float BottomMargin = 18f;
        private static readonly Color ReadyTint = new Color(0.98f, 0.78f, 0.26f, 1f);
        private static readonly Color CooldownTint = new Color(0.36f, 0.36f, 0.4f, 1f);
        private static readonly Color FillTint = new Color(0.95f, 0.72f, 0.22f, 1f);

        private ClientPipeline _pipeline;
        private Texture2D _sword;
        private Texture2D _circle;

        /// <summary>Connects the HUD to the session pipeline used by the render loop.</summary>
        public void Initialize(ClientPipeline pipeline)
        {
            _pipeline = pipeline;
            _sword = CreateSwordTexture(72);
            _circle = CreateCircleTexture(72);
        }

        private void OnDestroy()
        {
            if (_sword != null)
            {
                Destroy(_sword);
            }

            if (_circle != null)
            {
                Destroy(_circle);
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _sword == null || _circle == null)
            {
                return;
            }

            var progress = CooldownProgress();
            var ready = progress >= 0.999f;
            var rect = new Rect(
                (Screen.width - IconSize) * 0.5f,
                Screen.height - IconSize - BottomMargin,
                IconSize,
                IconSize);

            GUI.color = Color.white;
            GUI.DrawTexture(rect, _circle, ScaleMode.ScaleToFit, true);
            GUI.color = ready ? ReadyTint : CooldownTint;
            GUI.DrawTexture(rect, _sword, ScaleMode.ScaleToFit, true);

            if (!ready && progress > 0f)
            {
                // Clip a coloured copy to the bottom fraction of the icon. The bright part grows
                // upwards as the cooldown drains, matching Image.Type=Filled/Vertical.
                var filledHeight = IconSize * progress;
                GUI.BeginGroup(new Rect(rect.x, rect.y + IconSize - filledHeight, IconSize, filledHeight));
                GUI.color = FillTint;
                GUI.DrawTexture(new Rect(0f, filledHeight - IconSize, IconSize, IconSize),
                    _sword, ScaleMode.ScaleToFit, true);
                GUI.EndGroup();
            }

            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x - 24f, rect.y - 20f, IconSize + 72f, 18f), "SPACE / LMB");
        }

        private float CooldownProgress()
        {
            var prediction = _pipeline?.Prediction;
            if (prediction == null || prediction.AbilityCooldownTicks <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01(
                1f - (float)prediction.CooldownRemainingTicks / prediction.AbilityCooldownTicks);
        }

        private static Texture2D CreateSwordTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Cooldown Sword",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (var index = 0; index < pixels.Length; index++)
            {
                pixels[index] = clear;
            }

            var steel = new Color32(235, 240, 255, 255);
            var hilt = new Color32(210, 170, 90, 255);
            DrawLine(pixels, size, 20, 20, 50, 50, 3, steel);
            DrawLine(pixels, size, 13, 27, 27, 13, 3, hilt);
            DrawLine(pixels, size, 20, 20, 10, 10, 3, hilt);
            DrawDisc(pixels, size, 9, 9, 4, hilt);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Cooldown Backdrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            var fill = new Color32(18, 18, 24, 220);
            var rim = new Color32(130, 130, 145, 220);
            var radius = size * 0.5f - 1f;
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                pixels[(y * size) + x] = distance > radius
                    ? clear
                    : distance > radius - 2f ? rim : fill;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, int radius, Color32 color)
        {
            var steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (var step = 0; step <= steps; step++)
            {
                var t = steps == 0 ? 0f : (float)step / steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                var y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                DrawDisc(pixels, size, x, y, radius, color);
            }
        }

        private static void DrawDisc(Color32[] pixels, int size, int cx, int cy, int radius, Color32 color)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= size || y >= size)
                {
                    continue;
                }

                var dx = x - cx;
                var dy = y - cy;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    pixels[(y * size) + x] = color;
                }
            }
        }
    }
}
