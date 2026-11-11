using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Snapshot-driven HP/mana meters projected above the character. Screen-space drawing keeps
    /// the fill legible on a phone and independent of world lighting, shader stripping and depth.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HealthBar : MonoBehaviour
    {
        private static readonly Color HpColor = new Color(0.15f, 0.78f, 0.3f, 1f);
        private static readonly Color ManaColor = new Color(0.12f, 0.49f, 1f, 1f);
        private Camera _camera;
        private bool _hasHp;
        private bool _hasResource;
        private float _hpLoss;
        private float _resourceLoss;
        private float _hpLossUntil;
        private float _resourceLossUntil;

        /// <summary>Actual snapshot HP fraction, not a locally simulated health value.</summary>
        public float HpFraction { get; private set; }

        /// <summary>Actual snapshot resource fraction; also changes during authoritative regeneration.</summary>
        public float ResourceFraction { get; private set; }

        /// <summary>Creates meters above the entity root at <paramref name="height"/> metres.</summary>
        public static HealthBar Attach(Transform parent, float height)
        {
            var holder = new GameObject("Health Bar");
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = new Vector3(0f, height + 0.24f, 0f);
            return holder.AddComponent<HealthBar>();
        }

        /// <summary>Updates HP only from the authoritative value supplied by the view.</summary>
        public void SetHp(int hp, int maxHp)
        {
            var fraction = Fraction(hp, maxHp);
            if (_hasHp && fraction < HpFraction)
            {
                _hpLoss = HpFraction;
                _hpLossUntil = Time.unscaledTime + 1.2f;
            }
            HpFraction = fraction;
            _hasHp = true;
        }

        /// <summary>Updates mana only from the authoritative value supplied by the view.</summary>
        public void SetResource(int resource, int maxResource)
        {
            if (maxResource <= 0)
            {
                // У мобов нет маны: не рисуем под HP пустую синюю полоску.
                ResourceFraction = 0f;
                _hasResource = false;
                return;
            }

            var fraction = Fraction(resource, maxResource);
            if (_hasResource && fraction < ResourceFraction)
            {
                _resourceLoss = ResourceFraction;
                _resourceLossUntil = Time.unscaledTime + 1.2f;
            }
            ResourceFraction = fraction;
            _hasResource = true;
        }

        /// <summary>Clamped presentation ratio; an unavailable maximum is empty, never full.</summary>
        public static float Fraction(int value, int maximum)
        {
            return maximum > 0 ? Mathf.Clamp01((float)value / maximum) : 0f;
        }

        /// <summary>Pixel dimensions stay readable at landscape phone resolutions.</summary>
        public static Vector2 MeterSize(float screenHeight)
        {
            var scale = Mathf.Clamp(screenHeight / 720f, 1f, 2f);
            return new Vector2(88f * scale, 10f * scale);
        }

        /// <summary>Geometry shared by world and screen HUD; keeps the fill's left edge pinned.</summary>
        public static Rect FillRect(Rect meter, float fraction)
        {
            return new Rect(meter.x + 2f, meter.y + 2f,
                Mathf.Max(0f, meter.width - 4f) * Mathf.Clamp01(fraction), Mathf.Max(0f, meter.height - 4f));
        }

        /// <summary>Draws an opaque, bordered meter without scene materials or GUI.skin dependencies.</summary>
        public static void DrawMeter(Rect meter, float fraction, Color color, float recentLoss = 0f)
        {
            var previous = GUI.color;
            GUI.color = new Color(0.015f, 0.02f, 0.03f, 1f);
            GUI.DrawTexture(meter, Texture2D.whiteTexture);
            GUI.color = new Color(0.12f, 0.14f, 0.19f, 1f);
            GUI.DrawTexture(FillRect(meter, 1f), Texture2D.whiteTexture);
            if (recentLoss > fraction)
            {
                GUI.color = new Color(1f, 0.8f, 0.28f, 1f);
                GUI.DrawTexture(FillRect(meter, recentLoss), Texture2D.whiteTexture);
            }
            GUI.color = color;
            GUI.DrawTexture(FillRect(meter, fraction), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !_hasHp) return;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            var point = _camera.WorldToScreenPoint(transform.position);
            if (point.z <= 0f || point.x < 0f || point.x > Screen.width
                || point.y < 0f || point.y > Screen.height) return;

            var previousDepth = GUI.depth;
            GUI.depth = 20; // Main HUD panels remain in front of overhead meters.
            var size = MeterSize(Screen.height);
            var x = point.x - size.x * 0.5f;
            var y = Screen.height - point.y;
            DrawMeter(new Rect(x, y, size.x, size.y), HpFraction, HpColor,
                Time.unscaledTime < _hpLossUntil ? _hpLoss : 0f);
            if (_hasResource)
            {
                DrawMeter(new Rect(x, y + size.y + 3f, size.x, size.y), ResourceFraction, ManaColor,
                    Time.unscaledTime < _resourceLossUntil ? _resourceLoss : 0f);
            }

            GUI.depth = previousDepth;
        }
    }
}
