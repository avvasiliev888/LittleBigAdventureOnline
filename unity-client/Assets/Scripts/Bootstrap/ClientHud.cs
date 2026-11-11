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
        private uint _zoneId;
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
                _showCraft = !_showCraft;
                _showSkills = false;
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                _showSkills = !_showSkills;
                _showCraft = false;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                TryLoot();
            }

            ApplyZoneVisuals();
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
                GUI.Label(new Rect(12f, Screen.height - 70f, 900f, 24f), _message);
            }
        }

        private EntitySnapshot? Own()
        {
            var snapshot = _pipeline?.LastSnapshot;
            if (snapshot == null || _pipeline == null)
            {
                return null;
            }

            for (var i = 0; i < snapshot.Entities.Count; i++)
            {
                if (snapshot.Entities[i].Id == _pipeline.EntityId)
                {
                    return snapshot.Entities[i];
                }
            }

            return null;
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
            var hp = own.HasValue ? own.Value.Hp + "/" + own.Value.MaxHp : "-";
            var resource = own.HasValue ? own.Value.Resource + "/" + own.Value.MaxResource : "-";
            var cooldown = _pipeline?.Prediction?.CooldownRemainingTicks ?? 0;

            GUI.Box(new Rect(12f, 12f, PanelWidth, 154f), "Персонаж");
            GUI.Label(new Rect(24f, 38f, PanelWidth - 24f, 22f), "HP: " + hp + "    Ресурс: " + resource);
            GUI.Label(new Rect(24f, 60f, PanelWidth - 24f, 22f), "Слава: " + fame + "    Серебро: " + silver);
            GUI.Label(new Rect(24f, 82f, PanelWidth - 24f, 22f), "Зона: " + zoneText);
            GUI.Label(new Rect(24f, 104f, PanelWidth - 24f, 22f),
                "Флаг: " + (hostile ? "ВРАЖДЕБЕН" : "мирный") + "    Кулдаун: " + cooldown);
            GUI.Label(new Rect(24f, 126f, PanelWidth - 24f, 22f),
                "Снапшоты: " + _pipeline!.SnapshotsApplied + "    Дельта: " + _pipeline.DeltasApplied);
        }

        private void DrawHints()
        {
            GUI.Label(
                new Rect(12f, Screen.height - 46f, Screen.width - 24f, 22f),
                "WASD/ПКМ — движение, Space/ЛКМ — атака, E — лут, C — крафт, K — навыки. "
                + "Торговец открывается рядом.");
        }

        private void DrawCraft()
        {
            var rect = new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 96f);
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

        private void ApplyZoneVisuals()
        {
            var own = Own();
            if (!own.HasValue || _content == null || own.Value.ZoneId == _zoneId)
            {
                return;
            }

            _zoneId = own.Value.ZoneId;
            if (!_content.Config.Zones.TryGetZoneById(_zoneId, out var zone) || zone == null)
            {
                return;
            }

            switch (zone.Key)
            {
                case "blue":
                    ApplyPalette(new Color(0.30f, 0.42f, 0.62f), new Color(0.34f, 0.46f, 0.66f), 0.028f,
                        new Color(0.85f, 0.9f, 1f));
                    break;
                case "yellow":
                    ApplyPalette(new Color(0.62f, 0.52f, 0.32f), new Color(0.68f, 0.58f, 0.38f), 0.034f,
                        new Color(1f, 0.9f, 0.7f));
                    break;
                case "red":
                    ApplyPalette(new Color(0.42f, 0.24f, 0.24f), new Color(0.48f, 0.26f, 0.24f), 0.04f,
                        new Color(1f, 0.72f, 0.62f));
                    break;
            }
        }

        private static void ApplyPalette(Color ambient, Color fog, float density, Color sun)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambient;
            RenderSettings.ambientEquatorColor = ambient * 0.7f;
            RenderSettings.ambientGroundColor = ambient * 0.4f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = density;

            var light = RenderSettings.sun;
            if (light != null)
            {
                light.color = sun;
                light.intensity = 1.15f;
            }
        }

        private void ShowMessage(string message)
        {
            _message = message;
            _messageUntil = Time.unscaledTime + 4f;
        }
    }
}
