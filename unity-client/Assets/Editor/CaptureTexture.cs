#if UNITY_EDITOR
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>RenderTexture readback stores rows bottom-up; PNGs must be flipped once.</summary>
    public static class CaptureTexture
    {
        public static void FlipVertical(Texture2D texture)
        {
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels();
            var flipped = new Color[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                System.Array.Copy(pixels, (height - 1 - y) * width, flipped, y * width, width);
            }

            texture.SetPixels(flipped);
            texture.Apply(false, false);
        }
    }
}
#endif
