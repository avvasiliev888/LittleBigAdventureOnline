using System;
using System.Collections.Generic;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using UnityEngine;
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

        private ClientPipeline? _pipeline;
        private ClientContent? _content;
        private bool _showCraft;
        private bool _showSkills;
        private string _message = string.Empty;
        private float _messageUntil;
        private UnityVector3 _craftScroll;
        private UnityVector3 _skillScroll;
        private UnityVector3 _tradeScroll;

        /// <summary>Подключает HUD к конвейеру и контенту.</summary>
        /// <param name="pipeline">Клиентский конвейер.</param>
        /// <param name="content">Контент из JSON.</param>
        public void Initialize(ClientPipeline pipeline, ClientContent content)
        {
            _pipeline = pipeline;
            _content = content;
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
        }

        /// <summary>Открывает/закрывает панель крафта (клавиша C или мобильная кнопка).</summary>
        public void ToggleCraft()
        {
            _showCraft = !_showCraft;
            _showSkills = false;
        }

        /// <summary>Открывает/закрывает дерево навыков (клавиша K или мобильная кнопка).</summary>
        public void ToggleSkills()
        {
            _showSkills = !_showSkills;
            _showCraft = false;
        }

        /// <summary>Подбирает лут ближайшего трупа (клавиша E или мобильная кнопка).</summary>
        public void RequestLoot()
        {
            TryLoot();
        }

        private void OnGUI()
        {
            if (_pipeline == null || _content == null)
            {
                return;
            }

            DrawStats();
            DrawHints();

            if (_showCraft)
            {
                DrawCraft();
            }

            if (_showSkills)
            {
                DrawSkills();
            }

            DrawTrade();

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
                zoneText = zone.Key + " (PvP: " + (zone.PvpEnabled ? "да" : "нет") + ")";
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
                own.HasValue, "HP", new Color(0.05f, 0.42f, 0.15f, 1f), fillLabel);
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
                "WASD/ПКМ — движение, Space/ЛКМ — атака, E — лут, C — крафт, K — навыки. "
                + "Торговец открывается рядом.");
        }

        private void DrawCraft()
        {
            var rect = new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 96f);
            Backdrop(rect, 0.82f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Крафт (C — закрыть)");
            _craftScroll = GUILayout.BeginScrollView(_craftScroll);

            foreach (var recipe in _content!.Config.Recipes.Values)
            {
                var output = _content.Config.GetItem(recipe.OutputItemId);
                GUILayout.Label("#" + recipe.Id + " " + output.Key + " x" + recipe.OutputQuantity
                    + "  (+" + recipe.FamePerCraft + " славы)");
                var materials = string.Empty;
                foreach (var material in recipe.Materials)
                {
                    if (materials.Length > 0)
                    {
                        materials += ", ";
                    }

                    materials += _content.Config.GetItem(material.ItemId).Key + " x" + material.Quantity;
                }

                GUILayout.Label("   нужно: " + (materials.Length == 0 ? "ничего" : materials));
                if (GUILayout.Button("Скрафтить #" + recipe.Id))
                {
                    if (_pipeline!.SendCraft(recipe.Id))
                    {
                        ShowMessage("Крафт отправлен: " + output.Key);
                    }
                }

                GUILayout.Space(4f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSkills()
        {
            var own = Own();
            var rect = new Rect((Screen.width - SmallPanelWidth) * 0.5f, 80f, SmallPanelWidth, Screen.height - 200f);
            Backdrop(rect, 0.82f);
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
                GUILayout.Label("В конфиге нет ветки '" + archetypeKey + "'.");
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

            var rect = new Rect(12f, Screen.height - 300f, PanelWidth, 240f);
            Backdrop(rect, 0.82f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("Торговец " + trader.Key + " (" + trader.ZoneKey + ")");
            _tradeScroll = GUILayout.BeginScrollView(_tradeScroll);
            foreach (var price in trader.Prices)
            {
                var item = _content.Config.GetItem(price.ItemId);
                GUILayout.Label(item.Key + ": купить " + price.BuyPrice + ", продать " + price.SellPrice);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Купить") && _pipeline.SendBuy(trader.Id, price.ItemId, 1))
                {
                    ShowMessage("Покупка отправлена: " + item.Key);
                }

                if (GUILayout.Button("Продать") && _pipeline.SendSell(trader.Id, price.ItemId, 1))
                {
                    ShowMessage("Продажа отправлена: " + item.Key);
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

            if (_pipeline.SendLoot(nearest.Id))
            {
                ShowMessage("Лут отправлен с трупа " + nearest.Id + ".");
            }
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
    }
}
