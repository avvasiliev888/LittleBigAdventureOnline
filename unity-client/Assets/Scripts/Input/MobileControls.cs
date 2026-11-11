using System;
using UnityEngine;

namespace Albion.Client
{
    /// <summary>
    /// Экранное управление для Android/iOS: виртуальный стик слева, кнопка удара справа
    /// и кнопки «Лут/Крафт/Навыки». Работает поверх IMGUI-HUD и не меняет серверный протокол:
    /// стик отдаёт направление в <see cref="UnityInputSource"/>, кнопка удара — удержание атаки.
    /// </summary>
    public sealed class MobileControls : MonoBehaviour
    {
        private const float DeadZone = 0.15f;
        private ClientHud? _hud;
        private Texture2D? _circle;
        private int _moveTouchId = -1;
        private int _attackTouchId = -1;
        private Vector2 _moveOrigin;
        private Vector2 _move;
        private bool _attackHeld;
        private int _labelFontSize;

        /// <summary>Направление стика в мировых осях X/Z; длина 0..1.</summary>
        public System.Numerics.Vector2 Move => new System.Numerics.Vector2(_move.x, _move.y);

        /// <summary>Удерживается ли экранная кнопка удара.</summary>
        public bool AttackHeld => _attackHeld;

        /// <summary>Подключает HUD и переводит экран в ландшафт.</summary>
        public void Initialize(ClientHud hud)
        {
            _hud = hud;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _labelFontSize = Mathf.Max(18, Mathf.RoundToInt(Screen.height / 32f));
        }

        private void Update()
        {
            if (_circle == null)
            {
                _circle = CreateCircle(64);
            }

            if (Input.touchCount == 0)
            {
                if (_moveTouchId != -1 || _attackTouchId != -1)
                {
                    ResetTouches();
                }

                return;
            }

            for (var i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                var position = touch.position;

                if (touch.phase == TouchPhase.Began)
                {
                    BeginTouch(touch.fingerId, position);
                }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    UpdateTouch(touch.fingerId, position);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndTouch(touch.fingerId);
                }
            }
        }

        private void BeginTouch(int fingerId, Vector2 position)
        {
            // Стик — левая нижняя зона, удар — правая нижняя. Кнопки HUD находятся сверху,
            // поэтому случайный тап по ним не запускает атаку.
            if (_moveTouchId == -1 && position.x < Screen.width * 0.45f && position.y < Screen.height * 0.35f)
            {
                _moveTouchId = fingerId;
                _moveOrigin = position;
                _move = Vector2.zero;
                return;
            }

            if (_attackTouchId == -1 && position.x > Screen.width * 0.55f && position.y < Screen.height * 0.35f)
            {
                _attackTouchId = fingerId;
                _attackHeld = true;
            }
        }

        private void UpdateTouch(int fingerId, Vector2 position)
        {
            if (fingerId != _moveTouchId)
            {
                return;
            }

            var delta = (position - _moveOrigin) / Mathf.Max(1f, Screen.height * 0.12f);
            var magnitude = delta.magnitude;
            if (magnitude < DeadZone)
            {
                _move = Vector2.zero;
                return;
            }

            _move = delta / Mathf.Max(1f, magnitude);
        }

        private void EndTouch(int fingerId)
        {
            if (fingerId == _moveTouchId)
            {
                _moveTouchId = -1;
                _move = Vector2.zero;
            }
            else if (fingerId == _attackTouchId)
            {
                _attackTouchId = -1;
                _attackHeld = false;
            }
        }

        private void ResetTouches()
        {
            _moveTouchId = -1;
            _attackTouchId = -1;
            _move = Vector2.zero;
            _attackHeld = false;
        }

        private void OnGUI()
        {
            if (_circle == null)
            {
                _circle = CreateCircle(64);
            }

            var scale = Mathf.Clamp(Screen.height / 720f, 1f, 3f);
            var radius = 82f * scale;
            var joystickCenter = new Vector2(radius * 1.35f, Screen.height - radius * 1.35f);
            DrawCircle(joystickCenter, radius, new Color(1f, 1f, 1f, 0.23f));
            var knob = joystickCenter + new Vector2(_move.x, -_move.y) * radius;
            DrawCircle(knob, radius * 0.48f, new Color(1f, 1f, 1f, 0.55f));

            var attackCenter = new Vector2(Screen.width - radius * 1.35f, Screen.height - radius * 1.35f);
            DrawCircle(attackCenter, radius, _attackHeld
                ? new Color(0.9f, 0.25f, 0.2f, 0.55f)
                : new Color(0.9f, 0.25f, 0.2f, 0.28f));
            DrawLabel(new Rect(attackCenter.x - radius, attackCenter.y - 16f, radius * 2f, 32f), "УДАР");

            var width = 112f * scale;
            var height = 42f * scale;
            var gap = 8f * scale;
            var totalWidth = (width * 3f) + (gap * 2f);
            var x = (Screen.width - totalWidth) * 0.5f;
            var y = 12f * scale;
            if (GUI.Button(new Rect(x, y, width, height), "Лут") && _hud != null)
            {
                _hud.RequestLoot();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, height), "Крафт") && _hud != null)
            {
                _hud.ToggleCraft();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, height), "Навыки") && _hud != null)
            {
                _hud.ToggleSkills();
            }
        }

        private void DrawCircle(Vector2 center, float radius, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), _circle);
            GUI.color = previous;
        }

        private void DrawLabel(Rect rect, string text)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = _labelFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            GUI.Label(rect, text, style);
        }

        private static Texture2D CreateCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            var center = (size - 1) * 0.5f;
            var radius = size * 0.5f;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
                    var alpha = Mathf.Clamp01(radius - distance);
                    pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
