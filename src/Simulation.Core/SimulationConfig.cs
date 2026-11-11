using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Полная конфигурация мира (ADR-005): типы акторов, способности, зоны и предметы.
    /// Создаётся загрузчиком из JSON; ни одно геймплейное число в коде не дублируется.
    /// </summary>
    public sealed class SimulationConfig
    {
        private readonly Dictionary<uint, ActorConfig> _actors;
        private readonly Dictionary<uint, AbilityConfig> _abilities;
        private readonly Dictionary<uint, ItemDefinition> _items;
        private readonly List<ResourceNodeConfig> _resourceNodes;

        /// <summary>Создаёт конфигурацию мира из акторов, способностей, зон, предметов, дерева навыков и жил.</summary>
        /// <param name="actors">Типы акторов; идентификаторы уникальны.</param>
        /// <param name="abilities">Способности; идентификаторы уникальны, все ссылки акторов разрешаются.</param>
        /// <param name="zones">Зоны мира; покрывают мир без разрывов и пересечений.</param>
        /// <param name="items">Каталог предметов: сырьё и снаряжение.</param>
        /// <param name="skillTree">Дерево навыков; <c>null</c> — пустое дерево (тесты без прогрессии).</param>
        /// <param name="resourceNodes">Ресурсные жилы; <c>null</c> — жил нет.</param>
        /// <exception cref="ArgumentNullException">Если коллекция равна <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// При дубликатах идентификаторов, неразрешённых ссылках и противоречивых зонах.
        /// </exception>
        public SimulationConfig(
            IEnumerable<ActorConfig> actors,
            IEnumerable<AbilityConfig> abilities,
            IEnumerable<ZoneConfig> zones,
            IEnumerable<ItemDefinition> items,
            SkillTree? skillTree = null,
            IEnumerable<ResourceNodeConfig>? resourceNodes = null)
        {
            if (actors == null)
            {
                throw new ArgumentNullException(nameof(actors));
            }

            if (abilities == null)
            {
                throw new ArgumentNullException(nameof(abilities));
            }

            if (zones == null)
            {
                throw new ArgumentNullException(nameof(zones));
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            _abilities = new Dictionary<uint, AbilityConfig>();
            foreach (var ability in abilities)
            {
                if (!_abilities.TryAdd(ability.Id, ability))
                {
                    throw new ArgumentException("Дубликат Id способности: " + ability.Id + ".", nameof(abilities));
                }
            }

            _items = new Dictionary<uint, ItemDefinition>();
            foreach (var item in items)
            {
                if (!_items.TryAdd(item.Id, item))
                {
                    throw new ArgumentException("Дубликат Id предмета: " + item.Id + ".", nameof(items));
                }
            }

            Zones = new ZoneMap(zones);
            SkillTree = skillTree ?? SkillTree.Empty;

            ValidateItemArchetypes();
            ValidateSkillTreeAbilities();

            _resourceNodes = resourceNodes == null ? new List<ResourceNodeConfig>() : new List<ResourceNodeConfig>(resourceNodes);
            ValidateResourceNodes();

            _actors = new Dictionary<uint, ActorConfig>();
            foreach (var actor in actors)
            {
                if (!_actors.TryAdd(actor.Id, actor))
                {
                    throw new ArgumentException("Дубликат Id актора: " + actor.Id + ".", nameof(actors));
                }

                if (!_abilities.ContainsKey(actor.AbilityId))
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + " ссылается на отсутствующую способность " + actor.AbilityId + ".",
                        nameof(actors));
                }

                ValidateStarterLoadout(actor);
            }

            Actors = _actors;
            Abilities = _abilities;
        }

        /// <summary>Типы акторов по идентификатору.</summary>
        public IReadOnlyDictionary<uint, ActorConfig> Actors { get; }

        /// <summary>Способности по идентификатору.</summary>
        public IReadOnlyDictionary<uint, AbilityConfig> Abilities { get; }

        /// <summary>Каталог предметов по идентификатору.</summary>
        public IReadOnlyDictionary<uint, ItemDefinition> Items => _items;

        /// <summary>Карта зон мира с правилами PvP и смерти.</summary>
        public ZoneMap Zones { get; }

        /// <summary>Дерево навыков: ветки архетипов оружия (ADR-015).</summary>
        public SkillTree SkillTree { get; }

        /// <summary>Ресурсные жилы мира: источники сырья и Славы за сбор.</summary>
        public IReadOnlyList<ResourceNodeConfig> ResourceNodes => _resourceNodes;

        /// <summary>Возвращает тип актора по идентификатору.</summary>
        /// <param name="actorId">Идентификатор типа актора.</param>
        /// <exception cref="InvalidOperationException">Если такого актора нет в конфиге.</exception>
        public ActorConfig GetActor(uint actorId)
        {
            if (!_actors.TryGetValue(actorId, out var actor))
            {
                throw new InvalidOperationException("В конфигурации нет типа актора " + actorId + ".");
            }

            return actor;
        }

        /// <summary>Возвращает способность по идентификатору.</summary>
        /// <param name="abilityId">Идентификатор способности.</param>
        /// <exception cref="InvalidOperationException">Если такой способности нет в конфиге.</exception>
        public AbilityConfig GetAbility(uint abilityId)
        {
            if (!_abilities.TryGetValue(abilityId, out var ability))
            {
                throw new InvalidOperationException("В конфигурации нет способности " + abilityId + ".");
            }

            return ability;
        }

        /// <summary>Возвращает предмет по идентификатору.</summary>
        /// <param name="itemId">Идентификатор предмета.</param>
        /// <exception cref="InvalidOperationException">Если такого предмета нет в конфиге.</exception>
        public ItemDefinition GetItem(uint itemId)
        {
            if (!_items.TryGetValue(itemId, out var item))
            {
                throw new InvalidOperationException("В конфигурации нет предмета " + itemId + ".");
            }

            return item;
        }

        private void ValidateStarterLoadout(ActorConfig actor)
        {
            for (var i = 0; i < actor.StarterItems.Count; i++)
            {
                var starter = actor.StarterItems[i];
                if (!_items.ContainsKey(starter.ItemId))
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + " ссылается на отсутствующий стартовый предмет " + starter.ItemId + ".",
                        "actors");
                }
            }

            var usedSlots = new HashSet<EquipmentSlot>();
            for (var i = 0; i < actor.StarterEquipment.Count; i++)
            {
                var starter = actor.StarterEquipment[i];
                if (!_items.TryGetValue(starter.ItemId, out var item))
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + " ссылается на отсутствующее стартовое снаряжение " + starter.ItemId + ".",
                        "actors");
                }

                if (item.ItemKind != ItemKind.Equipment || !item.Slot.HasValue)
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + ": стартовый предмет " + item.Key + " не является снаряжением.",
                        "actors");
                }

                if (!usedSlots.Add(item.Slot.Value))
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + ": слот " + item.Slot.Value + " занят дважды в стартовом снаряжении.",
                        "actors");
                }
            }
        }

        private void ValidateItemArchetypes()
        {
            foreach (var pair in _items)
            {
                var item = pair.Value;
                if (item.Archetype != null && !SkillTree.Has(item.Archetype))
                {
                    throw new ArgumentException(
                        "Предмет '" + item.Key + "' ссылается на отсутствующую ветку дерева навыков '" + item.Archetype + "'.",
                        "items");
                }
            }
        }

        private void ValidateSkillTreeAbilities()
        {
            foreach (var archetype in SkillTree.Archetypes)
            {
                for (var i = 0; i < archetype.Nodes.Count; i++)
                {
                    var node = archetype.Nodes[i];
                    if (node.Kind == SkillNodeKind.Active && !_abilities.ContainsKey(node.AbilityId))
                    {
                        throw new ArgumentException(
                            "Узел " + node.Id + " ветки '" + archetype.Key + "' ссылается на отсутствующую способность "
                            + node.AbilityId + ".",
                            "skill-tree");
                    }
                }
            }
        }

        private void ValidateResourceNodes()
        {
            var ids = new HashSet<uint>();
            for (var i = 0; i < _resourceNodes.Count; i++)
            {
                var node = _resourceNodes[i];
                if (node == null)
                {
                    throw new ArgumentException("Список жил содержит null в позиции " + i + ".", "resourceNodes");
                }

                if (!ids.Add(node.Id))
                {
                    throw new ArgumentException("Дубликат Id жилы: " + node.Id + ".", "resourceNodes");
                }

                if (!_items.TryGetValue(node.ItemId, out var item))
                {
                    throw new ArgumentException(
                        "Жила '" + node.Key + "' ссылается на отсутствующий предмет " + node.ItemId + ".",
                        "resourceNodes");
                }

                if (item.ItemKind != ItemKind.Material)
                {
                    throw new ArgumentException(
                        "Жила '" + node.Key + "' ссылается на предмет '" + item.Key + "', который не является сырьём.",
                        "resourceNodes");
                }

                if (!Zones.TryGetZone(node.Position, out _))
                {
                    throw new ArgumentException("Жила '" + node.Key + "' находится вне зон мира.", "resourceNodes");
                }
            }
        }
    }
}
