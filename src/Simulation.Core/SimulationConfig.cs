using System;
using System.Collections.Generic;
using System.Numerics;

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
        private readonly Dictionary<uint, Recipe> _recipes;
        private readonly List<ResourceNodeConfig> _resourceNodes;
        private readonly List<NpcTrader> _traders;
        private readonly Dictionary<uint, MobDefinition> _mobs;
        private readonly Dictionary<uint, MobDefinition> _mobsByActorId;
        private readonly List<MobDefinition> _mobTypes;

        /// <summary>Создаёт конфигурацию мира из акторов, способностей, зон, предметов, дерева навыков, жил, рецептов, торговцев и мобов.</summary>
        /// <param name="actors">Типы акторов; идентификаторы уникальны.</param>
        /// <param name="abilities">Способности; идентификаторы уникальны, все ссылки акторов разрешаются.</param>
        /// <param name="zones">Зоны мира; покрывают мир без разрывов и пересечений.</param>
        /// <param name="items">Каталог предметов: сырьё и снаряжение.</param>
        /// <param name="skillTree">Дерево навыков; <c>null</c> — пустое дерево (тесты без прогрессии).</param>
        /// <param name="resourceNodes">Ресурсные жилы; <c>null</c> — жил нет.</param>
        /// <param name="recipes">Рецепты крафта; <c>null</c> — крафта нет.</param>
        /// <param name="traders">NPC-торговцы; <c>null</c> — торговли нет.</param>
        /// <param name="mobs">Типы PvE-мобов; <c>null</c> — мобов нет (тесты и миры без PvE).</param>
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
            IEnumerable<ResourceNodeConfig>? resourceNodes = null,
            IEnumerable<Recipe>? recipes = null,
            IEnumerable<NpcTrader>? traders = null,
            IEnumerable<MobDefinition>? mobs = null)
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

            _recipes = new Dictionary<uint, Recipe>();
            if (recipes != null)
            {
                foreach (var recipe in recipes)
                {
                    if (recipe == null)
                    {
                        throw new ArgumentException("Список рецептов содержит null.", nameof(recipes));
                    }

                    if (!_recipes.TryAdd(recipe.Id, recipe))
                    {
                        throw new ArgumentException("Дубликат Id рецепта: " + recipe.Id + ".", nameof(recipes));
                    }
                }
            }

            ValidateRecipes();

            _traders = traders == null ? new List<NpcTrader>() : new List<NpcTrader>(traders);
            ValidateTraders();

            _mobs = new Dictionary<uint, MobDefinition>();
            _mobsByActorId = new Dictionary<uint, MobDefinition>();
            _mobTypes = new List<MobDefinition>();
            if (mobs != null)
            {
                foreach (var mob in mobs)
                {
                    if (mob == null)
                    {
                        throw new ArgumentException("Список мобов содержит null.", nameof(mobs));
                    }

                    if (!_mobs.TryAdd(mob.Id, mob))
                    {
                        throw new ArgumentException("Дубликат Id моба: " + mob.Id + ".", nameof(mobs));
                    }

                    if (_mobsByActorId.ContainsKey(mob.ActorId))
                    {
                        throw new ArgumentException(
                            "Дубликат ActorId моба: " + mob.ActorId + ".",
                            nameof(mobs));
                    }

                    _mobsByActorId.Add(mob.ActorId, mob);
                    _mobTypes.Add(mob);
                }
            }

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

            ValidateMobs();

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

        /// <summary>Рецепты крафта по идентификатору (ADR-017).</summary>
        public IReadOnlyDictionary<uint, Recipe> Recipes => _recipes;

        /// <summary>NPC-торговцы мира (ADR-017).</summary>
        public IReadOnlyList<NpcTrader> Traders => _traders;

        /// <summary>Типы PvE-мобов по идентификатору (ADR-019).</summary>
        public IReadOnlyDictionary<uint, MobDefinition> Mobs => _mobs;

        /// <summary>Типы мобов в порядке объявления: детерминированный порядок спавна.</summary>
        public IReadOnlyList<MobDefinition> MobTypes => _mobTypes;

        /// <summary>Возвращает тип моба по идентификатору.</summary>
        /// <param name="mobId">Идентификатор типа моба.</param>
        /// <exception cref="InvalidOperationException">Если такого моба нет в конфиге.</exception>
        public MobDefinition GetMob(uint mobId)
        {
            if (!_mobs.TryGetValue(mobId, out var mob))
            {
                throw new InvalidOperationException("Моба с Id " + mobId + " нет в конфиге.");
            }

            return mob;
        }

        /// <summary>Пытается найти тип моба по идентификатору синтезированного актора.</summary>
        /// <param name="actorId">Идентификатор актора из снапшота.</param>
        /// <param name="mob">Найденный моб или <c>null</c>.</param>
        /// <returns><c>true</c>, если это актор моба.</returns>
        public bool TryGetMobByActorId(uint actorId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MobDefinition? mob)
        {
            return _mobsByActorId.TryGetValue(actorId, out mob);
        }

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

        /// <summary>Возвращает рецепт по идентификатору.</summary>
        /// <param name="recipeId">Идентификатор рецепта.</param>
        /// <exception cref="InvalidOperationException">Если такого рецепта нет в конфиге.</exception>
        public Recipe GetRecipe(uint recipeId)
        {
            if (!_recipes.TryGetValue(recipeId, out var recipe))
            {
                throw new InvalidOperationException("В конфигурации нет рецепта " + recipeId + ".");
            }

            return recipe;
        }

        /// <summary>Возвращает торговца по идентификатору.</summary>
        /// <param name="traderId">Идентификатор торговца.</param>
        /// <exception cref="InvalidOperationException">Если такого торговца нет в конфиге.</exception>
        public NpcTrader GetTrader(uint traderId)
        {
            for (var i = 0; i < _traders.Count; i++)
            {
                if (_traders[i].Id == traderId)
                {
                    return _traders[i];
                }
            }

            throw new InvalidOperationException("В конфигурации нет торговца " + traderId + ".");
        }

        private void ValidateRecipes()
        {
            foreach (var pair in _recipes)
            {
                var recipe = pair.Value;
                if (!_items.TryGetValue(recipe.OutputItemId, out var output))
                {
                    throw new ArgumentException(
                        "Рецепт '" + recipe.Key + "' ссылается на отсутствующий предмет " + recipe.OutputItemId + ".",
                        "recipes");
                }

                if (output.ItemKind != ItemKind.Equipment)
                {
                    throw new ArgumentException(
                        "Рецепт '" + recipe.Key + "' создаёт '" + output.Key + "', который не является снаряжением.",
                        "recipes");
                }

                for (var i = 0; i < recipe.Materials.Count; i++)
                {
                    var material = recipe.Materials[i];
                    if (!_items.TryGetValue(material.ItemId, out var item))
                    {
                        throw new ArgumentException(
                            "Рецепт '" + recipe.Key + "' ссылается на отсутствующее сырьё " + material.ItemId + ".",
                            "recipes");
                    }

                    if (item.ItemKind != ItemKind.Material)
                    {
                        throw new ArgumentException(
                            "Рецепт '" + recipe.Key + "' использует '" + item.Key + "', который не является сырьём.",
                            "recipes");
                    }
                }
            }
        }

        private void ValidateTraders()
        {
            var ids = new HashSet<uint>();
            for (var i = 0; i < _traders.Count; i++)
            {
                var trader = _traders[i];
                if (trader == null)
                {
                    throw new ArgumentException("Список торговцев содержит null в позиции " + i + ".", "traders");
                }

                if (!ids.Add(trader.Id))
                {
                    throw new ArgumentException("Дубликат Id торговца: " + trader.Id + ".", "traders");
                }

                if (!Zones.TryGetZoneByKey(trader.ZoneKey, out var zone))
                {
                    throw new ArgumentException(
                        "Торговец '" + trader.Key + "' ссылается на отсутствующую зону '" + trader.ZoneKey + "'.",
                        "traders");
                }

                if (!Zones.TryGetZone(trader.Position, out var atZone)
                    || atZone == null
                    || !string.Equals(atZone.Key, zone!.Key, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Торговец '" + trader.Key + "' стоит вне своей зоны '" + trader.ZoneKey + "'.",
                        "traders");
                }

                for (var p = 0; p < trader.Prices.Count; p++)
                {
                    var price = trader.Prices[p];
                    if (!_items.TryGetValue(price.ItemId, out var item))
                    {
                        throw new ArgumentException(
                            "Торговец '" + trader.Key + "' торгует отсутствующим предметом " + price.ItemId + ".",
                            "traders");
                    }

                    if (item.ItemKind != ItemKind.Material && item.ItemKind != ItemKind.Equipment)
                    {
                        throw new ArgumentException(
                            "Торговец '" + trader.Key + "' торгует '" + item.Key + "' неизвестного вида.",
                            "traders");
                    }
                }
            }
        }

        private void ValidateMobs()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _mobTypes.Count; i++)
            {
                var mob = _mobTypes[i];
                if (!keys.Add(mob.Key))
                {
                    throw new ArgumentException("Дубликат ключа моба: '" + mob.Key + "'.", "mobs");
                }

                if (!Zones.TryGetZoneByKey(mob.ZoneKey, out var zone) || zone == null)
                {
                    throw new ArgumentException(
                        "Моб '" + mob.Key + "' ссылается на отсутствующую зону '" + mob.ZoneKey + "'.",
                        "mobs");
                }

                if (_actors.ContainsKey(mob.ActorId))
                {
                    throw new ArgumentException(
                        "ActorId моба '" + mob.Key + "' (" + mob.ActorId + ") совпадает с актором из actors.json.",
                        "mobs");
                }

                if (!_abilities.ContainsKey(mob.Actor.AbilityId))
                {
                    throw new ArgumentException(
                        "Моб '" + mob.Key + "' ссылается на отсутствующую способность " + mob.Actor.AbilityId + ".",
                        "mobs");
                }

                for (var s = 0; s < mob.Spawns.Count; s++)
                {
                    var spawn = mob.Spawns[s];
                    var position = new Vector3(spawn.X, 0f, spawn.Y);
                    if (!zone.Contains(position))
                    {
                        throw new ArgumentException(
                            "Точка спавна " + s + " моба '" + mob.Key + "' вне зоны '" + mob.ZoneKey + "'.",
                            "mobs");
                    }

                    // Патруль задаётся смещениями от спавна: каждая точка обязана остаться в зоне,
                    // иначе моб на законном маршруте окажется в чужой зоне с другими правилами.
                    for (var p = 0; p < mob.PatrolPoints.Count; p++)
                    {
                        var patrol = mob.PatrolPoints[p];
                        var patrolPosition = new Vector3(spawn.X + patrol.X, 0f, spawn.Y + patrol.Y);
                        if (!zone.Contains(patrolPosition))
                        {
                            throw new ArgumentException(
                                "Точка патруля " + p + " моба '" + mob.Key + "' выходит за зону '"
                                + mob.ZoneKey + "'.",
                                "mobs");
                        }
                    }
                }

                for (var l = 0; l < mob.Loot.Count; l++)
                {
                    var entry = mob.Loot[l];
                    if (!_items.TryGetValue(entry.ItemId, out var item))
                    {
                        throw new ArgumentException(
                            "Моб '" + mob.Key + "' ссылается на отсутствующий предмет лута " + entry.ItemId + ".",
                            "mobs");
                    }

                    if (item.ItemKind != ItemKind.Material && item.ItemKind != ItemKind.Equipment)
                    {
                        throw new ArgumentException(
                            "Моб '" + mob.Key + "' роняет '" + item.Key + "' неизвестного вида.",
                            "mobs");
                    }
                }
            }
        }

        private void ValidateStarterLoadout(ActorConfig actor)
        {
            var starterItemIds = new HashSet<uint>();
            for (var i = 0; i < actor.StarterItems.Count; i++)
            {
                var starter = actor.StarterItems[i];
                if (!_items.ContainsKey(starter.ItemId))
                {
                    throw new ArgumentException(
                        "Актор " + actor.Id + " ссылается на отсутствующий стартовый предмет " + starter.ItemId + ".",
                        "actors");
                }

                starterItemIds.Add(starter.ItemId);
            }

            if (starterItemIds.Count > actor.InventorySlots)
            {
                throw new ArgumentException(
                    "Актор " + actor.Id + ": стартовых стопок " + starterItemIds.Count
                    + " больше вместимости инвентаря " + actor.InventorySlots + ".",
                    "actors");
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
