using System.Collections.Generic;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// World-space floating damage numbers. Values come only from authoritative snapshot HP
    /// deltas, so reconciliation or interpolation can never invent a hit; the label is drawn in
    /// screen space above the entity and fades upward, which stays legible on a phone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageNumbers : MonoBehaviour
    {
        private const float Lifetime = 0.9f;
        private const float Rise = 1.4f;
        private static readonly Color DamageColor = new Color(1f, 0.34f, 0.25f, 1f);
        private static readonly Color HealColor = new Color(0.3f, 1f, 0.45f, 1f);
        private static readonly Color OutlineColor = new Color(0.02f, 0.02f, 0.04f, 1f);

        private struct Entry
        {
            public Vector3 World;
            public int Amount;
            public bool Healing;
            public float Born;
        }

        private readonly List<Entry> _entries = new List<Entry>(16);
        private GUIStyle _style;
        private Camera _camera;

        /// <summary>Creates the overlay under the world root; one instance per view.</summary>
        public static DamageNumbers Attach(Transform parent)
        {
            var holder = new GameObject("Damage Numbers");
            holder.transform.SetParent(parent, false);
            return holder.AddComponent<DamageNumbers>();
        }

        /// <summary>Adds a number above a world position. Non-positive values are ignored.</summary>
        public void Show(Vector3 world, int amount, bool healing = false)
        {
            if (amount <= 0)
            {
                return;
            }

            _entries.Add(new Entry
            {
                World = world,
                Amount = amount,
                Healing = healing,
                Born = Time.unscaledTime,
            });
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _entries.Count == 0)
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 40f), 14, 30),
                };
            }

            var previousDepth = GUI.depth;
            GUI.depth = 20; // Same layer as overhead bars; the main HUD stays in front.
            var now = Time.unscaledTime;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                var age = now - entry.Born;
                if (age < 0f || age >= Lifetime)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                var position = entry.World + (Vector3.up * (Rise * (age / Lifetime)));
                var screen = _camera.WorldToScreenPoint(position);
                if (screen.z <= 0f)
                {
                    continue;
                }

                var alpha = 1f - (age / Lifetime);
                var color = entry.Healing ? HealColor : DamageColor;
                var text = entry.Healing ? "+" + entry.Amount : entry.Amount.ToString();
                var rect = new Rect(screen.x - 60f, Screen.height - screen.y - 20f, 120f, 36f);

                _style.normal.textColor = new Color(OutlineColor.r, OutlineColor.g, OutlineColor.b, alpha);
                GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), text, _style);
                GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), text, _style);
                GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), text, _style);
                GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, _style);

                _style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
                GUI.Label(rect, text, _style);
            }

            GUI.depth = previousDepth;
        }
    }
}
