using System;
using System.Collections.Generic;
using Client.Network;
using Simulation.Core;
using UnityEngine;
using UnityVector2 = UnityEngine.Vector2;
using UnityVector3 = UnityEngine.Vector3;

namespace Albion.Client
{
    /// <summary>
    /// HUD и базовые экраны Stage 5 (ADR-020): статы, зона, флаг враждебности, крафт,
    /// торговля, дерево навыков и сбор лута. Всё рисуется IMGUI, потому что у проекта нет
    /// UI-префабов: так экран работает и в fallback-сцене, и в собранном клиенте.
    /// </summary>
    public sealed class ClientHud : MonoBehaviour
    {
        private const float PanelWidth = 360f;
        private const float SmallPanelWidth = 330f;
        private const float MinimapPanelWidth = 300f;
        private const float MinimapPanelHeight = 150f;
        private const float MinimapMargin = 12f;
        private const float HintsBarHeight = 52f;
        private static Texture2D? _minimapArrow;

        private ClientPipeline? _pipeline;
        private ClientContent? _content;
        private UnityView? _view;
        private UnityInputSource? _input;
        private bool _showCraft;
        private bool _showSkills;
        private bool _showInventory;
        private uint _pendingLootCorpseId;
        private string _message = string.Empty;
        private float _messageUntil;
        private uint _pendingCommandSequence;
        private string _pendingCommandLabel = string.Empty;
        private uint _shownCommandResultSequence;
        private UnityVector3 _craftScroll;
        private UnityVector3 _skillScroll;
        private UnityVector3 _tradeScroll;

        /// <summary>Подключает HUD к конвейеру и контенту.</summary>
        /// <param name="pipeline">Клиентский конвейер.</param>
        /// <param name="content">Контент из JSON.</param>
        public void Initialize(ClientPipeline pipeline, ClientContent content, UnityView? view = null,
            UnityInputSource? input = null)
        {
            _pipeline = pipeline;
            _content = content;
            _view = view;
            _input = input;
        }

        private void Update()
        {
            if (_pipeline == null || _content == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                ToggleCraft();
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                ToggleSkills();
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                RequestLoot();
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                RequestGather();
            }

            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventory();
            }

            TryPendingLoot();
        }

        /// <summary>Открывает/закрывает панель крафта (клавиша C или мобильная кнопка).</summary>
        public void ToggleCraft()
        {
            _showCraft = !_showCraft;
            _showSkills = false;
            _showInventory = false;
            _input?.CancelPointerDestination();
        }

        /// <summary>Открывает/закрывает дерево навыков (клавиша K или мобильная кнопка).</summary>
        public void ToggleSkills()
        {
            _showSkills = !_showSkills;
            _showCraft = false;
            _showInventory = false;
            _input?.CancelPointerDestination();
        }

        /// <summary>Открывает/закрывает инвентарь (клавиша I).</summary>
        public void ToggleInventory()
        {
            _showInventory = !_showInventory;
            _showCraft = false;
            _showSkills = false;
            _input?.CancelPointerDestination();
        }

        /// <summary>Подбирает лут ближайшего трупа (клавиша E или мобильная кнопка).</summary>
        public void RequestLoot()
        {
            TryLoot();
        }

        /// <summary>Собирает ресурс из ближайшей жилы (клавиша F или мобильная кнопка).</summary>
        public void RequestGather()
        {
            TryGather();
        }

        private void TryGather()
        {
            if (_pipeline == null || _content == null || !_pipeline.HasSession)
            {
                return;
            }

            var position = OwnPosition();
            ResourceNodeConfig? nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var node in _content.Config.ResourceNodes)
            {
                var nodePosition = new UnityVector3(node.Position.X, node.Position.Y, node.Position.Z);
                var distance = UnityVector3.Distance(position, nodePosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = node;
                }
            }

            if (nearest == null || nearestDistance > nearest.Range + 0.5f)
            {
                ShowMessage("Рядом нет ресурсной жилы — подойдите ближе.");
                return;
            }

            if (_pipeline.SendGather(nearest.Id))
            {
                BeginPendingCommand("Сбор", ItemKey(nearest.ItemId) + " ×" + nearest.Amount);
                ShowMessage("Сбор запрошен: " + ItemKey(nearest.ItemId) + " ×" + nearest.Amount + ".");
                var nodePosition = new UnityVector3(nearest.Position.X, nearest.Position.Y, nearest.Position.Z);
                _view?.TriggerGather(nodePosition);
            }
        }

        /// <summary>ЛКМ по земле: если под точкой труп — отправить лут, иначе просто бежать туда.</summary>
        public void OnGroundClick(UnityVector3 point)
        {
            // Новый клик — новая команда: старый авто-подбор больше не актуален.
            _pendingLootCorpseId = 0;
            var snapshot = _pipeline?.LastSnapshot;
            if (snapshot == null)
            {
                return;
            }

            CorpseSnapshot? clicked = null;
            var clickedDistance = float.MaxValue;
            foreach (var corpse in snapshot.Corpses)
            {
                var dx = corpse.Position.X - point.x;
                var dz = corpse.Position.Z - point.z;
                var distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance <= 1.25f && distance < clickedDistance)
                {
                    clickedDistance = distance;
                    clicked = corpse;
                }
            }

            if (clicked == null)
            {
                return;
            }

            var player = OwnPosition();
            var px = clicked.Position.X - player.x;
            var pz = clicked.Position.Z - player.z;
            var playerDistance = MathF.Sqrt((px * px) + (pz * pz));
            if (playerDistance > clicked.LootRange + 0.35f)
            {
                _pendingLootCorpseId = clicked.Id;
                ShowMessage("Бегу к трупу " + clicked.Id + " — лут подберётся автоматически.");
                return;
            }

            LootCorpse(clicked);
        }

        private void TryPendingLoot()
        {
            if (_pendingLootCorpseId == 0 || _pipeline?.LastSnapshot == null)
            {
                return;
            }

            CorpseSnapshot? target = null;
            foreach (var corpse in _pipeline.LastSnapshot.Corpses)
            {
                if (corpse.Id == _pendingLootCorpseId)
                {
                    target = corpse;
                    break;
                }
            }

            if (target == null)
            {
                _pendingLootCorpseId = 0;
                return;
            }

            var player = OwnPosition();
            var dx = target.Position.X - player.x;
            var dz = target.Position.Z - player.z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            if (distance <= target.LootRange + 0.2f)
            {
                _pendingLootCorpseId = 0;
                LootCorpse(target);
            }
        }

        /// <summary>
        /// Screen-space rectangle of the world minimap. Public so the input guard and HUD
        /// regression use exactly the same geometry as the rendered panel.
        /// </summary>
        public static Rect MinimapRect(float screenWidth, float screenHeight)
        {
            var scale = Mathf.Clamp(screenWidth / 1280f, 0.78f, 1.25f);
            var width = Mathf.Round(MinimapPanelWidth * scale);
            var height = Mathf.Round(MinimapPanelHeight * scale);
            return new Rect(
                screenWidth - width - MinimapMargin,
                screenHeight - HintsBarHeight - MinimapMargin - height,
                width,
                height);
        }

        /// <summary>
        /// Map content rectangle inside the minimap panel. World aspect ratio is preserved
        /// (960x320) and the map is letterboxed inside the panel instead of stretched.
        /// </summary>
        public static Rect MinimapMapRect(float screenWidth, float screenHeight, float worldWidth, float worldDepth)
        {
            var panel = MinimapRect(screenWidth, screenHeight);
            var content = new Rect(panel.x + 8f, panel.y + 25f, panel.width - 16f, panel.height - 45f);
            if (!(worldWidth > 0f) || !(worldDepth > 0f))
            {
                return content;
            }

            var aspect = worldWidth / worldDepth;
            var width = content.width;
            var height = width / aspect;
            if (height > content.height)
            {
                height = content.height;
                width = height * aspect;
            }

            return new Rect(
                content.x + ((content.width - width) * 0.5f),
                content.y + ((content.height - height) * 0.5f),
                width,
                height);
        }

        /// <summary>Projects world XZ into the minimap; +Z is north (top of the map).</summary>
        public static UnityVector2 WorldToMinimap(UnityVector2 world, Rect map,
            float minX, float maxX, float minZ, float maxZ)
        {
            var fx = Mathf.InverseLerp(minX, maxX, world.x);
            var fz = Mathf.InverseLerp(minZ, maxZ, world.y);
            return new UnityVector2(map.x + (fx * map.width), map.y + ((1f - fz) * map.height));
        }

        /// <summary>Projects a minimap point back into world XZ; result is clamped to the world.</summary>
        public static UnityVector2 MinimapToWorld(UnityVector2 point, Rect map,
            float minX, float maxX, float minZ, float maxZ)
        {
            var fx = map.width <= 0f ? 0f : Mathf.Clamp01((point.x - map.x) / map.width);
            var fz = map.height <= 0f ? 0f : Mathf.Clamp01(1f - ((point.y - map.y) / map.height));
            return new UnityVector2(
                Mathf.Lerp(minX, maxX, fx),
                Mathf.Lerp(minZ, maxZ, fz));
        }

        /// <summary>
        /// Screen-space rectangle of the craft window. It stops above the minimap so the
        /// two HUD elements never overlap; the recipe list keeps its own scroll view.
        /// </summary>
        public static Rect CraftPanelRect(float screenWidth, float screenHeight)
        {
            if (Application.isMobilePlatform)
            {
                // The minimap is desktop-only, so mobile keeps the full-height craft window.
                return new Rect(screenWidth - PanelWidth - 12f, 12f, PanelWidth, screenHeight - 96f);
            }

            var minimap = MinimapRect(screenWidth, screenHeight);
            var height = Mathf.Max(180f, minimap.y - 12f - 12f);
            return new Rect(screenWidth - PanelWidth - 12f, 12f, PanelWidth, height);
        }

        private static Rect InventoryPanelRect(float screenWidth, float screenHeight)
        {
            return new Rect((screenWidth - SmallPanelWidth) * 0.5f, 80f,
                SmallPanelWidth, Mathf.Min(440f, screenHeight - 150f));
        }

        private static Rect SkillsPanelRect(float screenWidth, float screenHeight)
        {
            return new Rect((screenWidth - SmallPanelWidth) * 0.5f, 80f,
                SmallPanelWidth, screenHeight - 200f);
        }

        private static Rect TradePanelRect(float screenWidth, float screenHeight)
        {
            return new Rect(12f, screenHeight - 300f, PanelWidth, 240f);
        }

        private static Rect HintsPanelRect(float screenWidth, float screenHeight)
        {
            return new Rect(6f, screenHeight - 52f, screenWidth - 12f, 30f);
        }

        /// <summary>True while the mouse is over any visible HUD window or its backdrop.</summary>
        public bool IsPointerOverUi
        {
            get
            {
                if (_pipeline == null || _content == null || !_pipeline.HasSession
                    || Application.isMobilePlatform)
                {
                    return false;
                }

                // Input.mousePosition has its origin at the bottom-left; IMGUI rects use
                // top-left coordinates, hence the vertical flip.
                var point = new UnityVector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return IsPointOverPanel(point, Screen.width, Screen.height,
                    _showCraft, _showSkills, _showInventory, FindNearbyTrader() != null);
            }
        }

        /// <summary>
        /// Shared hit-test for HUD windows. Kept pure and public so the editor regression can
        /// verify that the craft button area blocks world clicks at every supported resolution.
        /// </summary>
        public static bool IsPointOverPanel(UnityVector2 point, float screenWidth, float screenHeight,
            bool craftOpen, bool skillsOpen, bool inventoryOpen, bool tradeOpen)
        {
            if (VitalsPanelRect(screenWidth, screenHeight, false).Contains(point)
                || HintsPanelRect(screenWidth, screenHeight).Contains(point)
                || MinimapRect(screenWidth, screenHeight).Contains(point))
            {
                return true;
            }

            if (craftOpen && CraftPanelRect(screenWidth, screenHeight).Contains(point))
            {
                return true;
            }

            if (inventoryOpen && InventoryPanelRect(screenWidth, screenHeight).Contains(point))
            {
                return true;
            }

            if (skillsOpen && SkillsPanelRect(screenWidth, screenHeight).Contains(point))
            {
                return true;
            }

            return tradeOpen && TradePanelRect(screenWidth, screenHeight).Contains(point);
        }

        private void OnGUI()
        {
            if (_pipeline == null || _content == null)
            {
                return;
            }

            ConsumeCommandResult();

            // Draw world-facing HUD first, then modal windows. IMGUI hit-testing and painting
            // both follow call order, so craft stays on top of the trader window and the
            // stats/hints underneath it.
            DrawStats();
            DrawHints();
            DrawTrade();
            DrawMinimap();

            if (_showCraft)
            {
                DrawCraft();
            }

            if (_showSkills)
            {
                DrawSkills();
            }

            if (_showInventory)
            {
                DrawInventory();
            }

            if (!string.IsNullOrEmpty(_message) && Time.unscaledTime < _messageUntil)
            {
                Backdrop(new Rect(6f, Screen.height - 78f, Mathf.Min(940f, Screen.width - 12f), 30f), 0.62f);
                GUI.Label(new Rect(12f, Screen.height - 70f, 900f, 24f), _message);
            }
        }

        /// <summary>Semi-transparent black plate behind HUD text so pale skies cannot wash it out.</summary>
        public static void Backdrop(Rect rect, float alpha)
        {
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private EntitySnapshot? Own()
        {
            return _pipeline != null && TryGetOwnVitals(_pipeline.LastSnapshot, _pipeline.EntityId, out var own)
                ? own : (EntitySnapshot?)null;
        }

        /// <summary>Finds the local entity in the newest authoritative snapshot, never a nearby mob.</summary>
        public static bool TryGetOwnVitals(SimulationSnapshot? snapshot, uint entityId, out EntitySnapshot own)
        {
            if (snapshot != null && entityId != 0)
            {
                for (var i = 0; i < snapshot.Entities.Count; i++)
                {
                    if (snapshot.Entities[i].Id == entityId)
                    {
                        own = snapshot.Entities[i];
                        return true;
                    }
                }
            }
            own = default;
            return false;
        }

        /// <summary>Readable stats panel dimensions; mobile top-centre controls remain unobstructed.</summary>
        public static Rect VitalsPanelRect(float screenWidth, float screenHeight, bool mobile)
        {
            var scale = mobile ? Mathf.Clamp(screenHeight / 720f, 1f, 1.5f) : 1f;
            var width = mobile ? Mathf.Min(360f * scale, screenWidth * 0.3f) : PanelWidth;
            return new Rect(12f, 12f, width, 228f * scale);
        }

        private UnityVector3 OwnPosition()
        {
            var prediction = _pipeline?.Prediction;
            if (prediction == null)
            {
                return UnityVector3.zero;
            }

            var position = prediction.Position;
            return new UnityVector3(position.X, position.Y, position.Z);
        }

        private void DrawStats()
        {
            var own = Own();
            var zoneText = "-";
            if (own.HasValue && _content != null && _content.Config.Zones.TryGetZoneById(own.Value.ZoneId, out var zone) && zone != null)
            {
                zoneText = ClientText.Zone(zone.Key) + " (бой с игроками: "
                    + (zone.PvpEnabled ? "да" : "нет") + ")";
            }

            var hostile = own.HasValue && _pipeline != null && _pipeline.LastSnapshot != null
                && own.Value.HostileUntilTick > _pipeline.LastSnapshot.Tick;
            var fame = own.HasValue ? own.Value.FameTotal : 0;
            var silver = own.HasValue ? own.Value.Silver : 0;
            var rect = VitalsPanelRect(Screen.width, Screen.height, Application.isMobilePlatform);
            var scale = rect.height / 228f;
            var label = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(17f * scale),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
            };
            var fillLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            var previous = GUI.color;
            GUI.color = new Color(0.025f, 0.035f, 0.055f, 0.94f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            var inner = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 24f * scale);
            GUI.Label(inner, "Персонаж — данные сервера", label);
            var hp = new Rect(inner.x, rect.y + 36f * scale, inner.width, 30f * scale);
            var mana = new Rect(inner.x, rect.y + 72f * scale, inner.width, 30f * scale);
            DrawVital(hp, own.HasValue ? own.Value.Hp : 0, own.HasValue ? own.Value.MaxHp : 0,
                own.HasValue, "Здоровье", new Color(0.05f, 0.42f, 0.15f, 1f), fillLabel);
            DrawVital(mana, own.HasValue ? own.Value.Resource : 0, own.HasValue ? own.Value.MaxResource : 0,
                own.HasValue, "Мана", new Color(0.04f, 0.25f, 0.7f, 1f), fillLabel);
            inner.y = rect.y + 108f * scale;
            GUI.Label(inner, "Слава: " + fame + "    Серебро: " + silver, label);
            inner.y += 25f * scale;
            GUI.Label(inner, "Зона: " + zoneText, label);
            inner.y += 25f * scale;
            GUI.Label(inner, "Флаг: " + (hostile ? "ВРАЖДЕБЕН" : "мирный"), label);
            inner.y += 25f * scale;
            GUI.Label(inner, "Снапшоты: " + _pipeline!.SnapshotsApplied + " / Δ " + _pipeline.DeltasApplied, label);
        }

        private static void DrawVital(Rect rect, int value, int maximum, bool available, string name, Color color, GUIStyle label)
        {
            HealthBar.DrawMeter(rect, HealthBar.Fraction(value, maximum), color);
            GUI.Label(rect, available ? name + "  " + value + " / " + maximum : name + "  —", label);
        }

        private void DrawHints()
        {
            if (Application.isMobilePlatform) return;
            var rect = new Rect(6f, Screen.height - 52f, Screen.width - 12f, 30f);
            Backdrop(rect, 0.55f);
            GUI.Label(
                new Rect(12f, Screen.height - 46f, Screen.width - 24f, 22f),
                "WASD — ходьба, ЛКМ/ПКМ — бег к курсору, клик — метка и лут с трупа, Пробел — атака, "
                + "I — инвентарь, F — сбор, C — крафт, K — навыки, 1–4 — оружие.");
        }

        /// <summary>
        /// Compact world map: three zones, gates, traders, resource nodes, mobs, corpses and
        /// the player arrow. ALT+LMB sends a debug teleport to the clicked world point.
        /// </summary>
        private void DrawMinimap()
        {
            if (_pipeline == null || _content == null || !_pipeline.HasSession || Application.isMobilePlatform)
            {
                return;
            }

            var zones = _content.Config.Zones;
            var panel = MinimapRect(Screen.width, Screen.height);
            if (panel.width < 120f || panel.height < 80f)
            {
                return;
            }

            var map = MinimapMapRect(Screen.width, Screen.height, zones.MaxX - zones.MinX, zones.MaxZ - zones.MinZ);
            var scale = Mathf.Clamp(panel.width / MinimapPanelWidth, 0.8f, 1.3f);
            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(11f * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.96f, 0.91f, 0.76f) },
            };
            var hintStyle = new GUIStyle(titleStyle)
            {
                fontSize = Mathf.RoundToInt(9f * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.8f, 0.3f) },
            };
            var mapStyle = new GUIStyle(titleStyle)
            {
                fontSize = Mathf.RoundToInt(8.5f * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.72f) },
            };

            Backdrop(panel, 0.92f);
            FillRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.72f, 0.58f, 0.28f, 1f));
            OutlineRect(panel, new Color(0.55f, 0.47f, 0.3f, 0.65f));

            GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, panel.width - 120f, 18f * scale), "КАРТА МИРА", titleStyle);
            GUI.Label(new Rect(panel.x + panel.width - 142f, panel.y + 4f, 134f, 18f * scale),
                AltHeld() ? "ALT+ЛКМ: ТЕЛЕПОРТ" : "ALT+ЛКМ: телепорт", hintStyle);

            FillRect(map, new Color(0.035f, 0.055f, 0.065f, 1f));
            OutlineRect(map, new Color(0.5f, 0.46f, 0.35f, 0.8f));
            GUI.Label(new Rect(map.x + map.width - 17f, map.y + 1f, 14f, 14f), "С", mapStyle);

            foreach (var zone in zones.Zones)
            {
                var min = WorldToMinimap(new UnityVector2(zone.MinX, zone.MinZ), map,
                    zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                var max = WorldToMinimap(new UnityVector2(zone.MaxX, zone.MaxZ), map,
                    zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                var zoneRect = Rect.MinMaxRect(min.x, max.y, max.x, min.y);
                FillRect(zoneRect, ZoneMapColor(zone.Key));
                OutlineRect(zoneRect, new Color(0.72f, 0.6f, 0.34f, 0.28f));

                var label = ClientText.Zone(zone.Key).ToUpperInvariant();
                if (label.EndsWith(" ЗОНА", StringComparison.Ordinal))
                {
                    label = label.Substring(0, label.Length - 5);
                }

                if (zoneRect.width >= 62f)
                {
                    GUI.Label(new Rect(zoneRect.x, zoneRect.y + (zoneRect.height * 0.5f) - 8f,
                        zoneRect.width, 16f), label, mapStyle);
                }
            }

            // Light grid at 80 m: enough orientation without turning the map into graph paper.
            for (var i = 1; i < 12; i++)
            {
                var x = map.x + (map.width * i / 12f);
                FillRect(new Rect(x, map.y, 1f, map.height), new Color(1f, 1f, 1f, 0.045f));
            }

            for (var i = 1; i < 4; i++)
            {
                var y = map.y + (map.height * i / 4f);
                FillRect(new Rect(map.x, y, map.width, 1f), new Color(1f, 1f, 1f, 0.045f));
            }

            // Gates are the only legal crossings, so they deserve a visible notch.
            foreach (var zone in zones.Zones)
            {
                if (zone.Gates == null)
                {
                    continue;
                }

                foreach (var gate in zone.Gates)
                {
                    var gatePoint = WorldToMinimap(new UnityVector2(gate.X, gate.Z), map,
                        zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                    FillRect(new Rect(gatePoint.x - 2f, gatePoint.y - 3f, 4f, 6f),
                        new Color(0.95f, 0.78f, 0.34f, 0.9f));
                }
            }

            if (_content.Config.ResourceNodes != null)
            {
                foreach (var node in _content.Config.ResourceNodes)
                {
                    var point = WorldToMinimap(new UnityVector2(node.Position.X, node.Position.Z), map,
                        zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                    if (!map.Contains(point))
                    {
                        continue;
                    }

                    FillRect(new Rect(point.x - 1f, point.y - 1f, 2f, 2f), ResourceColor(node.ItemId));
                }
            }

            if (_content.Config.Traders != null)
            {
                foreach (var trader in _content.Config.Traders)
                {
                    var point = WorldToMinimap(new UnityVector2(trader.Position.X, trader.Position.Z), map,
                        zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                    DrawDiamond(point, 4f, new Color(1f, 0.8f, 0.28f, 1f));
                }
            }

            var snapshot = _pipeline.LastSnapshot;
            if (snapshot != null)
            {
                foreach (var entity in snapshot.Entities)
                {
                    if (entity.IsDead || _content.FindMobByActorId(entity.ActorId) == null)
                    {
                        continue;
                    }

                    var point = WorldToMinimap(new UnityVector2(entity.Position.X, entity.Position.Z), map,
                        zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                    if (map.Contains(point))
                    {
                        FillRect(new Rect(point.x - 1.5f, point.y - 1.5f, 3f, 3f),
                            new Color(0.96f, 0.26f, 0.2f, 0.95f));
                    }
                }

                foreach (var corpse in snapshot.Corpses)
                {
                    var point = WorldToMinimap(new UnityVector2(corpse.Position.X, corpse.Position.Z), map,
                        zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                    if (map.Contains(point))
                    {
                        FillRect(new Rect(point.x - 1.5f, point.y - 1.5f, 3f, 3f),
                            new Color(0.95f, 0.72f, 0.2f, 0.95f));
                    }
                }
            }

            var own = OwnPosition();
            var playerPoint = WorldToMinimap(new UnityVector2(own.x, own.z), map,
                zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
            if (map.Contains(playerPoint))
            {
                FillRect(new Rect(playerPoint.x - 6f, playerPoint.y - 6f, 12f, 12f), new Color(0f, 0f, 0f, 0.75f));
                FillRect(new Rect(playerPoint.x - 4f, playerPoint.y - 4f, 8f, 8f), new Color(1f, 0.82f, 0.25f, 1f));
                var forward = _view != null ? _view.PlayerForward : UnityVector3.forward;
                var angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                var oldMatrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, playerPoint);
                var previous = GUI.color;
                GUI.color = new Color(0.06f, 0.06f, 0.05f, 1f);
                GUI.DrawTexture(new Rect(playerPoint.x - 8f, playerPoint.y - 9f, 16f, 18f), MinimapArrowTexture());
                GUI.color = previous;
                GUI.matrix = oldMatrix;
            }

            var mouse = Event.current != null ? Event.current.mousePosition : UnityVector2.zero;
            if (map.Contains(mouse))
            {
                var world = MinimapToWorld(mouse, map, zones.MinX, zones.MaxX, zones.MinZ, zones.MaxZ);
                var zoneName = zones.TryGetZone(new System.Numerics.Vector3(world.x, 0f, world.y), out var hoveredZone)
                    ? ClientText.Zone(hoveredZone.Key) : "вне зоны";
                FillRect(new Rect(mouse.x - 0.5f, mouse.y - 0.5f, 1f, 1f), Color.white);
                GUI.Label(new Rect(panel.x + 8f, panel.y + panel.height - 18f, panel.width - 16f, 14f),
                    "X " + world.x.ToString("0") + "  Z " + world.y.ToString("0") + "  " + zoneName, hintStyle);

                var current = Event.current;
                if (current != null && current.type == EventType.MouseDown && current.button == 0
                    && (current.alt || AltHeld()))
                {
                    _input?.CancelPointerDestination();
                    if (_pipeline.SendTeleport(world.x, world.y))
                    {
                        ShowMessage("Телепорт: " + zoneName + ", X " + world.x.ToString("0") + ", Z " + world.y.ToString("0"));
                    }
                    else
                    {
                        ShowMessage("Телепорт недоступен: нет соединения с сервером.");
                    }

                    current.Use();
                }
            }
            else
            {
                GUI.Label(new Rect(panel.x + 8f, panel.y + panel.height - 18f, panel.width - 16f, 14f),
                    "▲ вы   ◆ торговец   ● моб   • жила", mapStyle);
            }
        }

        private static Color ZoneMapColor(string key)
        {
            switch (key)
            {
                case "yellow": return new Color(0.42f, 0.33f, 0.14f, 0.9f);
                case "red": return new Color(0.42f, 0.15f, 0.15f, 0.9f);
                case "blue":
                default: return new Color(0.12f, 0.29f, 0.43f, 0.9f);
            }
        }

        private Color ResourceColor(uint itemId)
        {
            if (_content == null)
            {
                return new Color(0.6f, 0.8f, 0.5f, 1f);
            }

            var tier = _content.Config.GetItem(itemId).Tier;
            switch (tier)
            {
                case 1: return new Color(0.64f, 0.72f, 0.56f, 1f);
                case 2: return new Color(0.45f, 0.7f, 0.95f, 1f);
                case 3: return new Color(0.95f, 0.72f, 0.3f, 1f);
                default: return new Color(0.95f, 0.38f, 0.28f, 1f);
            }
        }

        private static void FillRect(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void OutlineRect(Rect rect, Color color)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            FillRect(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), color);
            FillRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            FillRect(new Rect(rect.x + rect.width - 1f, rect.y, 1f, rect.height), color);
        }

        private static void DrawDiamond(UnityVector2 center, float radius, Color color)
        {
            var previous = GUI.color;
            var matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(45f, center);
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
                Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = previous;
        }

        private static Texture2D MinimapArrowTexture()
        {
            if (_minimapArrow != null)
            {
                return _minimapArrow;
            }

            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Minimap player arrow",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = clear;
            }

            for (var y = 2; y < 14; y++)
            {
                var halfWidth = (int)((y - 2) * 0.42f);
                for (var x = (size / 2) - halfWidth; x <= (size / 2) + halfWidth; x++)
                {
                    pixels[(y * size) + x] = new Color32(255, 255, 255, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _minimapArrow = texture;
            return _minimapArrow;
        }

        private static bool AltHeld()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        private void DrawCraft()
        {
            var rect = CraftPanelRect(Screen.width, Screen.height);
            Backdrop(rect, 0.96f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Крафт (C — закрыть)");
            _craftScroll = GUILayout.BeginScrollView(_craftScroll);

            foreach (var recipe in _content!.Config.Recipes.Values)
            {
                var output = _content.Config.GetItem(recipe.OutputItemId);
                GUILayout.Label("#" + recipe.Id + " " + ClientText.Item(output.Key) + " ×" + recipe.OutputQuantity
                    + "  (+" + recipe.FamePerCraft + " славы)");
                var materials = string.Empty;
                foreach (var material in recipe.Materials)
                {
                    if (materials.Length > 0)
                    {
                        materials += ", ";
                    }

                    materials += ClientText.Item(_content.Config.GetItem(material.ItemId).Key)
                        + " ×" + material.Quantity;
                }

                GUILayout.Label("   нужно: " + (materials.Length == 0 ? "ничего" : materials));
                if (GUILayout.Button("Скрафтить #" + recipe.Id))
                {
                    if (_pipeline!.SendCraft(recipe.Id))
                    {
                        BeginPendingCommand("Крафт", ClientText.Item(output.Key));
                        ShowMessage("Крафт запрошен: " + ClientText.Item(output.Key) + ".");
                    }
                }

                GUILayout.Space(4f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawInventory()
        {
            var rect = InventoryPanelRect(Screen.width, Screen.height);
            Backdrop(rect, 0.94f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Инвентарь (I — закрыть)");
            var own = Own();
            if (own.HasValue)
            {
                GUILayout.Label("Серебро: " + own.Value.Silver + "   Слава: " + own.Value.FameTotal);
                GUILayout.Space(4f);
                GUILayout.Label("Снаряжение:");
                DrawEquipmentLine("Оружие", own.Value.WeaponItemId);
                DrawEquipmentLine("Нагрудник", own.Value.ChestItemId);
                DrawEquipmentLine("Штаны", own.Value.LegsItemId);
                DrawEquipmentLine("Сапоги", own.Value.FeetItemId);
            }
            else
            {
                GUILayout.Label("Нет данных о персонаже.");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Инвентарь (данные сервера):");
            if (_pipeline == null || !_pipeline.HasInventory)
            {
                GUILayout.Label("Ожидаем инвентарь от сервера…");
            }
            else if (_pipeline.Inventory.Count == 0)
            {
                GUILayout.Label("Пусто.");
            }
            else
            {
                foreach (var stack in _pipeline.Inventory)
                {
                    GUILayout.Label("• " + ItemKey(stack.ItemId) + " ×" + stack.Quantity);
                }
            }

            GUILayout.EndArea();
        }

        private void DrawEquipmentLine(string slot, uint itemId)
        {
            GUILayout.Label("  " + slot + ": " + (itemId == 0 ? "—" : ItemKey(itemId)));
        }

        private string ItemKey(uint itemId)
        {
            return _content != null && _content.Config.Items.TryGetValue(itemId, out var item)
                ? ClientText.Item(item.Key)
                : "предмет #" + itemId;
        }

        private void DrawSkills()
        {
            var own = Own();
            var rect = SkillsPanelRect(Screen.width, Screen.height);
            Backdrop(rect, 0.94f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Дерево навыков (K — закрыть)");

            if (!own.HasValue)
            {
                GUILayout.Label("Нет снапшота персонажа.");
                GUILayout.EndArea();
                return;
            }

            var archetypeKey = WeaponArchetype(own.Value.WeaponItemId);
            if (archetypeKey == null)
            {
                GUILayout.Label("Возьмите оружие: ветка навыков определяется оружием в руке.");
                GUILayout.Label("Всего Славы: " + own.Value.FameTotal);
                GUILayout.EndArea();
                return;
            }

            if (!_content!.Config.SkillTree.TryGet(archetypeKey, out var archetype) || archetype == null)
            {
                GUILayout.Label("В конфиге нет ветки навыков для этого оружия.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(archetype.Name + "  |  Слава ветки: " + own.Value.BranchFame);
            _skillScroll = GUILayout.BeginScrollView(_skillScroll);
            foreach (var node in archetype.Nodes)
            {
                var unlocked = own.Value.BranchFame >= node.FameThreshold;
                GUILayout.Label((unlocked ? "[открыт] " : "[закрыт] ") + node.Name
                    + "  (" + node.FameThreshold + " славы)");
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTrade()
        {
            if (_content == null || _pipeline == null || !_pipeline.HasSession)
            {
                return;
            }

            var trader = FindNearbyTrader();
            if (trader == null)
            {
                return;
            }

            var rect = TradePanelRect(Screen.width, Screen.height);
            Backdrop(rect, 0.94f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label(ClientText.Trader(trader.Key) + " (" + ClientText.Zone(trader.ZoneKey) + ")");
            _tradeScroll = GUILayout.BeginScrollView(_tradeScroll);
            foreach (var price in trader.Prices)
            {
                var item = _content.Config.GetItem(price.ItemId);
                var itemName = ClientText.Item(item.Key);
                GUILayout.Label(itemName + ": купить " + price.BuyPrice + ", продать " + price.SellPrice);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Купить") && _pipeline.SendBuy(trader.Id, price.ItemId, 1))
                {
                    BeginPendingCommand("Покупка", itemName);
                    ShowMessage("Покупка запрошена: " + itemName + ".");
                }

                if (GUILayout.Button("Продать") && _pipeline.SendSell(trader.Id, price.ItemId, 1))
                {
                    BeginPendingCommand("Продажа", itemName);
                    ShowMessage("Продажа запрошена: " + itemName + ".");
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private NpcTrader? FindNearbyTrader()
        {
            var position = OwnPosition();
            NpcTrader? nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var trader in _content!.Config.Traders)
            {
                var dx = trader.Position.X - position.x;
                var dz = trader.Position.Z - position.z;
                var distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance <= trader.Range && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = trader;
                }
            }

            return nearest;
        }

        private void TryLoot()
        {
            var snapshot = _pipeline?.LastSnapshot;
            if (snapshot == null || _pipeline == null)
            {
                return;
            }

            var position = OwnPosition();
            CorpseSnapshot? nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var corpse in snapshot.Corpses)
            {
                var dx = corpse.Position.X - position.x;
                var dz = corpse.Position.Z - position.z;
                var distance = MathF.Sqrt((dx * dx) + (dz * dz));
                if (distance <= corpse.LootRange && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = corpse;
                }
            }

            if (nearest == null)
            {
                ShowMessage("Рядом нет трупа с лутом.");
                return;
            }

            LootCorpse(nearest);
        }

        private void LootCorpse(CorpseSnapshot corpse)
        {
            if (_pipeline == null || !_pipeline.SendLoot(corpse.Id))
            {
                return;
            }

            var contents = DescribeLoot(corpse);
            BeginPendingCommand("Лут", contents);
            ShowMessage("Лут запрошен: " + contents + ".");
        }


        private string DescribeLoot(CorpseSnapshot corpse)
        {
            var parts = new List<string>();
            if (corpse.Silver > 0)
            {
                parts.Add(corpse.Silver + " серебра");
            }

            for (var i = 0; i < corpse.Loot.Count; i++)
            {
                parts.Add(ItemKey(corpse.Loot[i].ItemId) + " ×" + corpse.Loot[i].Quantity);
            }

            return parts.Count == 0 ? "пусто" : string.Join(", ", parts);
        }

        private string? WeaponArchetype(uint weaponItemId)
        {
            if (weaponItemId == 0 || _content == null)
            {
                return null;
            }

            try
            {
                return _content.Config.GetItem(weaponItemId).Archetype;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = Time.unscaledTime + 4f;
        }

        private void BeginPendingCommand(string kind, string subject)
        {
            if (_pipeline == null)
            {
                return;
            }

            _pendingCommandSequence = _pipeline.LastUiSequence;
            _pendingCommandLabel = kind + ": " + subject;
        }

        private void ConsumeCommandResult()
        {
            var result = _pipeline?.LastCommandResult;
            if (!result.HasValue)
            {
                return;
            }

            var value = result.Value;
            if (value.Sequence == _shownCommandResultSequence)
            {
                return;
            }

            _shownCommandResultSequence = value.Sequence;
            if (value.Sequence == _pendingCommandSequence)
            {
                ShowMessage(value.Accepted
                    ? _pendingCommandLabel + " — готово."
                    : _pendingCommandLabel + " — отказ: " + value.Detail);
                _pendingCommandSequence = 0;
                return;
            }

            // Результат для команды без локального ожидания (например, экипировка):
            // успех не шумит, а причину отказа важно показать.
            if (!value.Accepted)
            {
                ShowMessage("Сервер отклонил команду: " + value.Detail);
            }
        }
    }
}
