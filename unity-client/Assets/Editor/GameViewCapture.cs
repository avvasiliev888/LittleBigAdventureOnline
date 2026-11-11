#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Albion.Editor
{
    /// <summary>
    /// Reads the Editor's composited GameView, including real runtime IMGUI. Camera.Render does
    /// not include IMGUI. Unity exposes no public GameView texture API, so the two reflected names
    /// are deliberately isolated here and absence is reported rather than producing a world-only PNG.
    /// </summary>
    public static class GameViewCapture
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Call before the capture gate to allow normal GameView repaint/player-loop work.</summary>
        public static EditorWindow Prepare()
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            var window = EditorWindow.GetWindow(type);
            window.Show();
            // The hidden batch GameView starts portrait; a landscape frame is what the phone build uses.
            if (window.position.width < 1200f || window.position.height > window.position.width)
            {
                window.position = new Rect(40f, 40f, 1600f, 900f);
            }

            window.Focus();
            window.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
            return window;
        }

        /// <summary>
        /// Attempts a synchronous readback after a genuine GameView repaint. Retry from subsequent
        /// Editor update ticks if not ready. In batchmode the window may never paint; then use a
        /// nonbatch Editor run, not Camera.Render or a fabricated HUD overlay.
        /// </summary>
        public static bool TryCapture(string path, out string error)
        {
            Texture2D image = null;
            var oldActive = RenderTexture.active;
            try
            {
                if (!EditorApplication.isPlaying)
                    throw new InvalidOperationException("GameView capture requires Play mode.");
                var window = Prepare();
                // SendEvent uses the actual HostView GUI context. Calling a private OnGUI method
                // directly, or setting Event.current in EditorApplication.update, is not equivalent.
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                var field = window.GetType().GetField("m_RenderTexture", InstanceMembers);
                if (field == null)
                    throw new MissingFieldException("UnityEditor.GameView", "m_RenderTexture");
                var texture = field.GetValue(window) as RenderTexture;
                if (texture == null || !texture.IsCreated() || texture.width < 64 || texture.height < 64)
                    throw new InvalidOperationException("GameView has not produced its composited render texture. Retry after repaint; use nonbatch if unavailable.");
                RenderTexture.active = texture;
                image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0, false);
                image.Apply(false, false);
                var bytes = image.EncodeToPNG();
                if (bytes == null || bytes.Length < 128)
                    throw new InvalidOperationException("GameView PNG encoding returned no usable image.");
                var destination = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, bytes);
                Debug.Log("[agent-smoke] composited GameView capture " + texture.width + "x" + texture.height + ": " + destination);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
            finally
            {
                RenderTexture.active = oldActive;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
#endif
