using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Реализация <see cref="ISimulation"/>: фиксированный тик, движение и способности по конфигу,
    /// смерть, респавн, полные снапшоты и восстановление из них.
    /// </summary>
    public sealed class Simulation : ISimulation
    {
        private readonly SimulationConfig? _config;
        private SimulationState _state;
        private uint _nextEntityId;

        /// <summary>
        /// Создаёт пустой мир без конфигурации. Допустим только до появления команд:
        /// первая же команда потребует <see cref="SimulationConfig"/>.
        /// </summary>
        public Simulation()
            : this(new SimulationState(0, Array.Empty<EntityState>()), null)
        {
        }

        /// <summary>Создаёт симуляцию над готовым состоянием мира без конфигурации.</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        public Simulation(SimulationState initialState)
            : this(initialState, null)
        {
        }

        /// <summary>Создаёт пустой мир с конфигурацией.</summary>
        /// <param name="config">Типы акторов и способности; в M4 приходит из JSON.</param>
        public Simulation(SimulationConfig config)
            : this(new SimulationState(0, Array.Empty<EntityState>()), config)
        {
        }

        /// <summary>Создаёт симуляцию над состоянием мира с конфигурацией.</summary>
        /// <param name="initialState">Начальное состояние мира.</param>
        /// <param name="config">Конфигурация; <c>null</c> допустим только для мира без команд.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="initialState"/> равен <c>null</c>.</exception>
        public Simulation(SimulationState initialState, SimulationConfig? config)
        {
            _state = initialState ?? throw new ArgumentNullException(nameof(initialState));
            _config = config;

            _nextEntityId = 1;
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                if (_state.Entities[i].Id >= _nextEntityId)
                {
                    _nextEntityId = _state.Entities[i].Id + 1;
                }
            }

            for (var i = 0; i < _state.Corpses.Count; i++)
            {
                if (_state.Corpses[i].Id >= _nextEntityId)
                {
                    _nextEntityId = _state.Corpses[i].Id + 1;
                }
            }

            // Сущности могли получить снаряжение и Славу до создания симуляции (спавн сервера,
            // тестовые миры): производные характеристики обязаны учитывать их с первого тика.
            if (_config != null)
            {
                for (var i = 0; i < _state.Entities.Count; i++)
                {
                    _state.Entities[i].RecalculateStats(_config);
                }
            }
        }

        /// <inheritdoc />
        public SimulationState State => _state;

        /// <summary>
        /// Выдаёт свободный идентификатор сущности или трупа. Единый счётчик не даёт спавну мобов
        /// и подключению игроков выдать один и тот же Id (ADR-019).
        /// </summary>
        /// <returns>Положительный идентификатор, свободный в текущем мире.</returns>
        public uint AllocateEntityId()
        {
            while (true)
            {
                var id = _nextEntityId++;
                if (!_state.TryGetEntity(id, out _) && !_state.TryGetCorpse(id, out _))
                {
                    return id;
                }
            }
        }

        /// <summary>
        /// Спавнит всех мобов из конфига: по одной сущности на каждую точку спавна (ADR-019).
        /// Повторный вызов добавит мобов ещё раз — сервер вызывает метод один раз при старте.
        /// </summary>
        /// <returns>Сколько мобов заспавнено.</returns>
        public int SpawnMobs()
        {
            if (_config == null)
            {
                return 0;
            }

            var spawned = 0;
            for (var i = 0; i < _config.MobTypes.Count; i++)
            {
                var mob = _config.MobTypes[i];
                for (var j = 0; j < mob.Spawns.Count; j++)
                {
                    var spawn = new Vector3(mob.Spawns[j].X, 0f, mob.Spawns[j].Y);
                    _state.AddEntity(new EntityState(AllocateEntityId(), mob, spawn));
                    spawned++;
                }
            }

            return spawned;
        }

        /// <inheritdoc />
        public void Tick(float deltaTime, IReadOnlyList<CommandEnvelope> commands)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTime),
                    deltaTime,
                    "deltaTime должен быть конечным положительным числом (NaN и Infinity запрещены).");
            }

            if (commands.Count > 0 && _config == null)
            {
                throw new InvalidOperationException(
                    "Команды требуют SimulationConfig: геймплейные параметры не могут быть захардкожены (ADR-005).");
            }

            // Регенерация идёт до способностей: на тике, где ресурс добрал до стоимости,
            // удар уже возможен. Мёртвые не регенерируют; респавн ниже восстанавливает полный запас.
            RegenerateResources(deltaTime);

            for (var i = 0; i < commands.Count; i++)
            {
                ApplyCommand(commands[i], deltaTime);
            }

            // Мобы ходят и бьют после команд игроков: удар по мобу на этом же тике уже поднимает
            // его агрессию, а не запаздывает на тик. Истёкшие трупы убираются до респавна.
            UpdateMobs(deltaTime);
            _state.RemoveExpiredCorpses(_state.Tick);

            ProcessRespawns();

            // Страховка от пересечений на спавне/респавне: даже если два актора появились
            // в одной точке, сервер мягко разводит их до следующего шага движения.
            MovementCollision.ResolveOverlaps(_state.Entities, _config?.Zones, _config?.WorldColliders);

            _state.Tick++;
        }

        /// <inheritdoc />
        public SimulationSnapshot CreateSnapshot()
        {
            var entities = new EntitySnapshot[_state.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = _state.Entities[i];
                var weapon = entity.Equipment.Weapon;
                var archetype = weapon?.Definition.Archetype;
                var branchFame = archetype == null ? 0 : entity.Fame.Get(archetype);
                entities[i] = new EntitySnapshot(
                    entity.Id,
                    entity.ActorId,
                    entity.Position,
                    entity.SpawnPosition,
                    entity.Hp,
                    entity.MaxHp,
                    entity.Resource,
                    entity.MaxResource,
                    entity.CooldownUntilTick,
                    entity.RespawnAtTick,
                    entity.IsDead,
                    GetZoneId(entity.Position),
                    entity.AbilityId,
                    entity.MoveSpeed,
                    entity.Silver,
                    entity.HostileUntilTick,
                    weapon?.Definition.Id ?? 0u,
                    entity.Equipment.Get(EquipmentSlot.Chest)?.Definition.Id ?? 0u,
                    entity.Equipment.Get(EquipmentSlot.Legs)?.Definition.Id ?? 0u,
                    entity.Equipment.Get(EquipmentSlot.Feet)?.Definition.Id ?? 0u,
                    entity.Fame.Total,
                    branchFame);
            }

            var corpses = new CorpseSnapshot[_state.Corpses.Count];
            for (var i = 0; i < corpses.Length; i++)
            {
                var corpse = _state.Corpses[i];
                corpses[i] = new CorpseSnapshot(
                    corpse.Id,
                    corpse.Position,
                    corpse.LootRange,
                    corpse.Silver,
                    corpse.Loot,
                    corpse.ExpireAtTick);
            }

            return new SimulationSnapshot(_state.Tick, entities, corpses);
        }

        private uint GetZoneId(Vector3 position)
        {
            if (_config == null)
            {
                return 0u;
            }

            return _config.Zones.TryGetZone(position, out var zone) && zone != null ? zone.Id : 0u;
        }

        /// <inheritdoc />
        public void RestoreFromSnapshot(SimulationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (_config == null)
            {
                throw new InvalidOperationException(
                    "Восстановление снапшота требует SimulationConfig: тип актора берётся из конфига.");
            }

            var entities = new EntityState[snapshot.Entities.Count];
            for (var i = 0; i < entities.Length; i++)
            {
                var saved = snapshot.Entities[i];
                EntityState entity;
                if (_config.TryGetMobByActorId(saved.ActorId, out var mob))
                {
                    entity = new EntityState(saved.Id, mob, saved.SpawnPosition);
                }
                else
                {
                    entity = new EntityState(saved.Id, _config.GetActor(saved.ActorId), saved.SpawnPosition);
                }

                entity.Position = saved.Position;
                entity.Hp = saved.Hp;
                entity.Resource = saved.Resource;
                entity.CooldownUntilTick = saved.CooldownUntilTick;
                entity.RespawnAtTick = saved.RespawnAtTick;
                entities[i] = entity;
            }

            _state = new SimulationState(snapshot.Tick, entities);
        }

        private void ApplyCommand(CommandEnvelope envelope, float deltaTime)
        {
            switch (envelope.Command)
            {
                case MoveCommand move:
                    // Команда для сущности, которой уже нет в мире, игнорируется: решение о логировании принимает сервер.
                    if (_state.TryGetEntity(envelope.EntityId, out var mover))
                    {
                        ApplyMove(mover, move, deltaTime);
                    }

                    return;

                case AbilityCommand ability:
                    if (_state.TryGetEntity(envelope.EntityId, out var caster))
                    {
                        ApplyAbility(caster, ability);
                    }

                    return;

                case EquipCommand equip:
                    if (_state.TryGetEntity(envelope.EntityId, out var equipper))
                    {
                        ApplyEquip(equipper, equip);
                    }

                    return;

                case GatherCommand gather:
                    if (_state.TryGetEntity(envelope.EntityId, out var gatherer))
                    {
                        ApplyGather(gatherer, gather);
                    }

                    return;

                case CraftCommand craft:
                    if (_state.TryGetEntity(envelope.EntityId, out var crafter))
                    {
                        ApplyCraft(crafter, craft);
                    }

                    return;

                case BuyCommand buy:
                    if (_state.TryGetEntity(envelope.EntityId, out var buyer))
                    {
                        ApplyBuy(buyer, buy);
                    }

                    return;

                case SellCommand sell:
                    if (_state.TryGetEntity(envelope.EntityId, out var seller))
                    {
                        ApplySell(seller, sell);
                    }

                    return;

                case LootCommand loot:
                    if (_state.TryGetEntity(envelope.EntityId, out var looter))
                    {
                        ApplyLoot(looter, loot);
                    }

                    return;

                default:
                    throw new NotSupportedException(
                        "Тип команды " + envelope.Command.GetType().FullName + " не поддерживается симуляцией.");
            }
        }

        private void ApplyMove(EntityState entity, MoveCommand command, float deltaTime)
        {
            if (entity.IsDead)
            {
                return;
            }

            var direction = new Vector3(command.Direction.X, 0f, command.Direction.Y);
            var desired = entity.Position + (direction * (entity.MoveSpeed * deltaTime));

            // Граница мира, проходы и препятствия — правило симуляции, а не вежливость:
            // из мира нельзя выйти и «спрятаться» вне зон, сквозь дерево/камень/столб и
            // сквозь другого актора нельзя пройти (иначе клиент и сервер разойдутся).
            entity.Position = MovementCollision.Resolve(
                entity.Position,
                desired,
                entity.Actor.CollisionRadius,
                _config!.Zones,
                _config.WorldColliders,
                BuildActorCircles(entity.Id),
                entity.Id);
        }

        private void ApplyAbility(EntityState caster, AbilityCommand command)
        {
            if (caster.IsDead)
            {
                return;
            }

            var ability = _config!.GetAbility(caster.AbilityId);

            // Self-target is never a valid hit. It is also the one target error that does not
            // activate the ability, so a direct simulation caller cannot spend the cooldown on it.
            if (command.TargetId == caster.Id)
            {
                return;
            }

            if (caster.CooldownUntilTick > _state.Tick)
            {
                return;
            }

            if (caster.Resource < ability.ResourceCost)
            {
                return;
            }

            EntityState? target = null;
            ZoneConfig? targetZone = null;

            if (command.TargetId != 0
                && _state.TryGetEntity(command.TargetId, out var found)
                && !found.IsDead)
            {
                target = found;

                if (target.Actor.Kind == ActorKind.Player)
                {
                    targetZone = _config.Zones.GetZone(target.Position);

                    // Безопасная зона: ударить игрока нельзя, и способность не активируется —
                    // иначе клиент тратил бы ресурс и кулдаун на действие, которое сервер всё равно отменит.
                    if (!targetZone.PvpEnabled)
                    {
                        return;
                    }
                }
            }

            // Activation is charged first: a miss (no target, dead target, out of range) still
            // spends the resource and starts the cooldown, exactly like the client prediction.
            caster.Resource -= ability.ResourceCost;
            caster.CooldownUntilTick = _state.Tick + ToTicks(ability.CooldownSeconds);

            if (target == null)
            {
                return;
            }

            if (HorizontalDistance(caster.Position, target.Position) > ability.Range)
            {
                return;
            }

            // Урон = база способности + бонус оружия, умноженный на пассивные узлы дерева.
            // Округление от нуля: 2.5 урона не «теряются» на ветке с +5%.
            var damage = (int)MathF.Round(
                (ability.Damage + caster.Equipment.DamageBonus) * caster.DamageMultiplier,
                MidpointRounding.AwayFromZero);
            target.Hp -= damage;

            // Ответный удар: моб запоминает обидчика. В синей зоне это единственный способ
            // поднять его агрессию — пассивные мобы не нападают первыми (ADR-019).
            if (target.Mob != null)
            {
                target.AiTargetId = caster.Id;
            }

            if (targetZone != null)
            {
                // Флаг враждебности: напавший на игрока сам становится законной целью на время из конфига.
                caster.HostileUntilTick = _state.Tick + ToTicks(targetZone.HostileFlagSeconds);
            }

            if (target.Hp <= 0)
            {
                Kill(target, caster);
            }
        }

        /// <summary>
        /// Смена оружия: предмет берётся из инвентаря, вытесненная вещь возвращается в инвентарь.
        /// Смена архетипа оружия меняет активную способность, но Слава копится по веткам и не сбрасывается.
        /// </summary>
        /// <param name="entity">Сущность, меняющая снаряжение.</param>
        /// <param name="command">Команда с идентификатором предмета.</param>
        private void ApplyEquip(EntityState entity, EquipCommand command)
        {
            if (entity.IsDead || !_config!.Items.TryGetValue(command.ItemId, out var item))
            {
                return;
            }

            if (item.ItemKind != ItemKind.Equipment || !item.Slot.HasValue)
            {
                return;
            }

            var slot = item.Slot.Value;
            var previous = entity.Equipment.Get(slot);
            if (previous != null && !entity.Inventory.CanAddAfterRemoving(item.Id, 1, previous.Definition.Id, 1))
            {
                // Вытесненную вещь некуда положить — отменяем смену до списания предмета.
                return;
            }

            if (!entity.Inventory.Remove(item.Id, 1))
            {
                return;
            }

            entity.Equip(_config, item);

            if (previous != null)
            {
                entity.Inventory.Add(previous.Definition, 1);
            }
        }

        /// <summary>
        /// Сбор ресурсов из жилы: сырьё в инвентарь, Слава в ветку оружия в руке, кулдаун сбора.
        /// Жила статична: истощения и респавна нет, ограничение — персональный кулдаун сбора.
        /// </summary>
        /// <param name="entity">Сущность, собирающая ресурс.</param>
        /// <param name="command">Команда с идентификатором жилы.</param>
        private void ApplyGather(EntityState entity, GatherCommand command)
        {
            if (entity.IsDead)
            {
                return;
            }

            var node = FindNode(command.NodeId);
            if (node == null || entity.GatherCooldownUntilTick > _state.Tick)
            {
                return;
            }

            if (HorizontalDistance(entity.Position, node.Position) > node.Range)
            {
                return;
            }

            if (!entity.Inventory.CanAdd(node.ItemId, node.Amount))
            {
                return;
            }

            entity.Inventory.Add(_config!.GetItem(node.ItemId), node.Amount);
            entity.GatherCooldownUntilTick = _state.Tick + ToTicks(node.GatherSeconds);
            entity.AwardFame(_config, node.FamePerGather);
        }

        /// <summary>
        /// Крафт по рецепту (ADR-017): сырьё списывается из инвентаря, предмет создаётся,
        /// Слава начисляется в ветку оружия в руке. Транзакция атомарна: при нехватке места
        /// сырьё возвращается, предмет не создаётся.
        /// </summary>
        /// <param name="entity">Сущность, крафтящая предмет.</param>
        /// <param name="command">Команда с идентификатором рецепта.</param>
        private void ApplyCraft(EntityState entity, CraftCommand command)
        {
            if (entity.IsDead)
            {
                return;
            }

            var recipe = FindRecipe(command.RecipeId);
            if (recipe == null)
            {
                return;
            }

            for (var i = 0; i < recipe.Materials.Count; i++)
            {
                var material = recipe.Materials[i];
                if (entity.Inventory.CountOf(material.ItemId) < material.Quantity)
                {
                    return;
                }
            }

            if (!CanCraftOutput(entity.Inventory, recipe))
            {
                return;
            }

            for (var i = 0; i < recipe.Materials.Count; i++)
            {
                var material = recipe.Materials[i];
                entity.Inventory.Remove(material.ItemId, material.Quantity);
            }

            var output = _config!.GetItem(recipe.OutputItemId);

            // CanCraftOutput гарантирует: после списания сырья место под результат есть,
            // поэтому Add здесь не может не поместиться и откат не нужен.
            entity.Inventory.Add(output, recipe.OutputQuantity);
            entity.AwardFame(_config, recipe.FamePerCraft);
        }

        /// <summary>
        /// Покупка у NPC (ADR-017): проверка радиуса, цены, серебра и места в инвентаре.
        /// Отказ не списывает серебро и не создаёт предмет.
        /// </summary>
        /// <param name="entity">Покупатель.</param>
        /// <param name="command">Команда покупки.</param>
        private void ApplyBuy(EntityState entity, BuyCommand command)
        {
            if (entity.IsDead)
            {
                return;
            }

            var trader = FindTrader(command.TraderId);
            if (trader == null || !IsNear(entity.Position, trader.Position, trader.Range))
            {
                return;
            }

            var price = trader.FindPrice(command.ItemId);
            if (price == null)
            {
                return;
            }

            var cost = (long)price.BuyPrice * command.Quantity;
            if (cost > int.MaxValue || entity.Silver < cost)
            {
                return;
            }

            var item = _config!.GetItem(command.ItemId);
            if (!entity.Inventory.Add(item, command.Quantity))
            {
                return;
            }

            entity.Silver -= (int)cost;
        }

        /// <summary>
        /// Продажа NPC (ADR-017): предмет списывается, серебро начисляется по цене продажи.
        /// Проверка радиуса и наличия предмета — до изменения состояния.
        /// </summary>
        /// <param name="entity">Продавец.</param>
        /// <param name="command">Команда продажи.</param>
        private void ApplySell(EntityState entity, SellCommand command)
        {
            if (entity.IsDead)
            {
                return;
            }

            var trader = FindTrader(command.TraderId);
            if (trader == null || !IsNear(entity.Position, trader.Position, trader.Range))
            {
                return;
            }

            var price = trader.FindPrice(command.ItemId);
            if (price == null)
            {
                return;
            }

            var revenue = (long)price.SellPrice * command.Quantity;
            if (revenue > int.MaxValue || entity.Silver > int.MaxValue - revenue)
            {
                return;
            }

            if (entity.Inventory.CountOf(command.ItemId) < command.Quantity)
            {
                return;
            }

            entity.Inventory.Remove(command.ItemId, command.Quantity);
            entity.Silver += (int)revenue;
        }

        /// <summary>
        /// Сбор лута из трупа (ADR-019): предметы переносятся, пока помещаются в инвентарь,
        /// серебро — целиком (с защитой от переполнения). Частично собранный труп остаётся в мире
        /// до истечения срока; пустой удаляется сразу.
        /// </summary>
        /// <param name="looter">Сущность, собирающая лут.</param>
        /// <param name="command">Команда с идентификатором трупа.</param>
        private void ApplyLoot(EntityState looter, LootCommand command)
        {
            if (looter.IsDead || !_state.TryGetCorpse(command.CorpseId, out var corpse))
            {
                return;
            }

            if (!IsNear(looter.Position, corpse.Position, corpse.LootRange))
            {
                return;
            }

            var loot = corpse.MutableLoot;
            for (var i = loot.Count - 1; i >= 0; i--)
            {
                var stack = loot[i];
                var item = _config!.GetItem(stack.ItemId);
                if (looter.Inventory.Add(item, stack.Quantity))
                {
                    loot.RemoveAt(i);
                }
            }

            if (corpse.Silver > 0 && looter.Silver <= int.MaxValue - corpse.Silver)
            {
                looter.Silver += corpse.Silver;
                corpse.Silver = 0;
            }

            if (corpse.IsEmpty)
            {
                _state.RemoveCorpse(corpse.Id);
            }
        }

        private Recipe? FindRecipe(uint recipeId)
        {
            return _config!.Recipes.TryGetValue(recipeId, out var recipe) ? recipe : null;
        }

        private NpcTrader? FindTrader(uint traderId)
        {
            for (var i = 0; i < _config!.Traders.Count; i++)
            {
                if (_config.Traders[i].Id == traderId)
                {
                    return _config.Traders[i];
                }
            }

            return null;
        }

        /// <summary>Поместится ли результат крафта после списания сырья (учитывает освобождение стопок).</summary>
        private static bool CanCraftOutput(Inventory inventory, Recipe recipe)
        {
            if (inventory.CountOf(recipe.OutputItemId) > 0)
            {
                return true;
            }

            var freeingStacks = 0;
            for (var i = 0; i < recipe.Materials.Count; i++)
            {
                var material = recipe.Materials[i];
                if (inventory.CountOf(material.ItemId) == material.Quantity)
                {
                    freeingStacks++;
                }
            }

            return inventory.FreeSlots + freeingStacks > 0;
        }

        private ResourceNodeConfig? FindNode(uint nodeId)
        {
            for (var i = 0; i < _config!.ResourceNodes.Count; i++)
            {
                if (_config.ResourceNodes[i].Id == nodeId)
                {
                    return _config.ResourceNodes[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Один шаг серверного автомата мобов (ADR-019): агрессия по зоне, патруль, преследование,
        /// удар и возврат домой с лечением.
        /// </summary>
        /// <param name="deltaTime">Длительность тика, с.</param>
        private void UpdateMobs(float deltaTime)
        {
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (entity.Mob == null)
                {
                    continue;
                }

                if (entity.IsDead)
                {
                    ResetMobAi(entity);
                    continue;
                }

                UpdateMob(entity, entity.Mob, deltaTime);
            }
        }

        private void UpdateMob(EntityState mob, MobDefinition definition, float deltaTime)
        {
            if (mob.AiState == MobAiState.Return)
            {
                if (MoveMobTowards(mob, mob.SpawnPosition, deltaTime))
                {
                    ArriveHome(mob, definition);
                }

                return;
            }

            // Синяя зона пассивна: моб отвечает только тому, кто ударил его первым (AiTargetId).
            // В жёлтой и красной зоне (PvP включён) он сам берёт ближайшую цель в радиусе агрессии.
            var zone = _config!.Zones.GetZone(mob.Position);
            var target = FindMobTarget(mob, zone, out var targetDistance);

            if (target != null)
            {
                mob.AiTargetId = target.Id;

                if (HorizontalDistance(mob.Position, mob.SpawnPosition) > definition.LeashRange)
                {
                    mob.AiTargetId = 0;
                    mob.AiState = MobAiState.Return;
                    return;
                }

                if (targetDistance <= definition.AttackRange)
                {
                    mob.AiState = MobAiState.Attack;
                    if (_state.Tick >= mob.NextAttackTick)
                    {
                        mob.NextAttackTick = _state.Tick + ToTicks(definition.AttackSeconds);
                        target.Hp -= definition.Damage;
                        if (target.Hp <= 0)
                        {
                            Kill(target, mob);
                        }
                    }

                    return;
                }

                mob.AiState = MobAiState.Chase;
                MoveMobTowards(mob, target.Position, deltaTime);
                return;
            }

            mob.AiTargetId = 0;

            // Погоня без цели (цель умерла или вышла за leash) — домой. Патрулирующий моб не
            // «возвращается» на каждом шаге: его смещение от спавна — это и есть маршрут.
            if (mob.AiState == MobAiState.Chase || mob.AiState == MobAiState.Attack)
            {
                mob.AiState = MobAiState.Return;
                if (MoveMobTowards(mob, mob.SpawnPosition, deltaTime))
                {
                    ArriveHome(mob, definition);
                }

                return;
            }

            if (definition.PatrolPoints.Count == 0)
            {
                mob.AiState = MobAiState.Idle;
                return;
            }

            if (mob.AiState == MobAiState.Idle)
            {
                if (_state.Tick < mob.AiWaitUntilTick)
                {
                    return;
                }

                mob.AiState = MobAiState.Patrol;
            }

            var index = (int)(mob.PatrolIndex % (uint)definition.PatrolPoints.Count);
            var point = definition.PatrolPoints[index];
            var destination = new Vector3(
                mob.SpawnPosition.X + point.X,
                0f,
                mob.SpawnPosition.Z + point.Y);
            if (MoveMobTowards(mob, destination, deltaTime))
            {
                mob.PatrolIndex = (uint)((index + 1) % definition.PatrolPoints.Count);
                mob.AiState = MobAiState.Idle;
                mob.AiWaitUntilTick = _state.Tick + ToTicks(definition.PatrolPauseSeconds);
            }
        }

        private void ArriveHome(EntityState mob, MobDefinition definition)
        {
            mob.Position = mob.SpawnPosition;
            mob.Hp = mob.MaxHp;
            mob.AiState = MobAiState.Idle;
            mob.AiWaitUntilTick = _state.Tick + ToTicks(definition.PatrolPauseSeconds);
        }

        private EntityState? FindMobTarget(EntityState mob, ZoneConfig zone, out float distance)
        {
            // Ответная агрессия: обидчик остаётся целью, пока моб не выйдет за радиус возврата.
            if (mob.AiTargetId != 0
                && _state.TryGetEntity(mob.AiTargetId, out var attacker)
                && !attacker.IsDead
                && attacker.Actor.Kind == ActorKind.Player
                && HorizontalDistance(mob.Position, attacker.Position) <= mob.Mob!.LeashRange)
            {
                distance = HorizontalDistance(mob.Position, attacker.Position);
                return attacker;
            }

            distance = float.MaxValue;
            if (!zone.PvpEnabled)
            {
                return null;
            }

            EntityState? best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var candidate = _state.Entities[i];
                if (candidate.Actor.Kind != ActorKind.Player || candidate.IsDead)
                {
                    continue;
                }

                var candidateDistance = HorizontalDistance(mob.Position, candidate.Position);
                if (candidateDistance <= mob.Mob!.AggroRange && candidateDistance < bestDistance)
                {
                    best = candidate;
                    bestDistance = candidateDistance;
                }
            }

            if (best != null)
            {
                distance = bestDistance;
            }

            return best;
        }

        private bool MoveMobTowards(EntityState mob, Vector3 destination, float deltaTime)
        {
            var dx = destination.X - mob.Position.X;
            var dz = destination.Z - mob.Position.Z;
            var distance = MathF.Sqrt((dx * dx) + (dz * dz));
            var step = mob.MoveSpeed * deltaTime;

            if (distance <= step || distance <= 0.0001f)
            {
                mob.Position = MovementCollision.Resolve(
                    mob.Position,
                    new Vector3(destination.X, 0f, destination.Z),
                    mob.Actor.CollisionRadius,
                    _config!.Zones,
                    _config.WorldColliders,
                    BuildActorCircles(mob.Id),
                    mob.Id);
                return true;
            }

            var scale = step / distance;
            mob.Position = MovementCollision.Resolve(
                mob.Position,
                new Vector3(mob.Position.X + (dx * scale), 0f, mob.Position.Z + (dz * scale)),
                mob.Actor.CollisionRadius,
                _config!.Zones,
                _config.WorldColliders,
                BuildActorCircles(mob.Id),
                mob.Id);
            return false;
        }

        /// <summary>
        /// Круги живых акторов для проверки динамических столкновений; движущийся актор исключён.
        /// </summary>
        private List<CollisionCircle> BuildActorCircles(uint selfId)
        {
            var circles = new List<CollisionCircle>(_state.Entities.Count);
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (entity.Id != selfId && !entity.IsDead)
                {
                    circles.Add(CollisionCircle.FromEntity(entity));
                }
            }

            return circles;
        }

        private static void ResetMobAi(EntityState mob)
        {
            mob.AiState = MobAiState.Idle;
            mob.AiTargetId = 0;
            mob.NextAttackTick = 0;
            mob.AiWaitUntilTick = 0;
            mob.PatrolIndex = 0;
        }

        /// <summary>
        /// Создаёт труп убитого моба: детерминированные серебро и лут из таблицы (ADR-003, ADR-019).
        /// Детерминизм важен для replay-тестов и одинакового результата боя на сервере и в тестах.
        /// </summary>
        /// <param name="victim">Погибший моб.</param>
        private void CreateCorpse(EntityState victim)
        {
            var mob = victim.Mob!;
            var silverSpan = mob.SilverMax - mob.SilverMin;
            var silver = mob.SilverMin;
            if (silverSpan > 0)
            {
                silver += (int)(LootHash(victim.Id, 1u, _state.Tick) % (uint)(silverSpan + 1));
            }

            var loot = new List<ItemStack>();
            for (var i = 0; i < mob.Loot.Count; i++)
            {
                var entry = mob.Loot[i];
                if (entry.Chance <= 0f)
                {
                    continue;
                }

                if (entry.Chance < 1f)
                {
                    var roll = (LootHash(victim.Id, entry.ItemId, _state.Tick) % 10000u) / 10000f;
                    if (roll >= entry.Chance)
                    {
                        continue;
                    }
                }

                var quantitySpan = entry.MaxQuantity - entry.MinQuantity;
                var quantity = entry.MinQuantity;
                if (quantitySpan > 0)
                {
                    quantity += (int)(LootHash(victim.Id, entry.ItemId + 1u, _state.Tick) % (uint)(quantitySpan + 1));
                }

                var merged = false;
                for (var j = 0; j < loot.Count; j++)
                {
                    if (loot[j].ItemId == entry.ItemId)
                    {
                        loot[j].Quantity += quantity;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    loot.Add(new ItemStack(entry.ItemId, quantity));
                }
            }

            _state.AddCorpse(new CorpseState(
                AllocateEntityId(),
                victim.Position,
                mob.LootRange,
                silver,
                loot,
                _state.Tick + ToTicks(mob.CorpseSeconds)));
        }

        private static uint LootHash(uint entityId, uint salt, uint tick)
        {
            unchecked
            {
                var hash = entityId * 2654435761u;
                hash ^= salt * 2246822519u;
                hash ^= tick * 3266489917u;
                hash ^= hash >> 13;
                hash *= 0x5BD1E995u;
                hash ^= hash >> 15;
                return hash;
            }
        }

        /// <summary>
        /// Применяет смерть по правилам зоны жертвы (ADR-013): в красной зоне игрок теряет снаряжение
        /// и инвентарь, в остальных зонах — только прочность; мобы оставляют труп с лутом (ADR-019).
        /// Убийца-игрок получает Славу в ветку оружия в руке (ADR-015).
        /// </summary>
        /// <param name="victim">Погибшая сущность.</param>
        /// <param name="killer">Нанесшая смертельный удар сущность; совпадает с жертвой при «самоубийстве».</param>
        private void Kill(EntityState victim, EntityState killer)
        {
            victim.Hp = 0;
            victim.CooldownUntilTick = 0;
            victim.HostileUntilTick = 0;
            victim.RespawnAtTick = _state.Tick + ToTicks(victim.Actor.RespawnSeconds);

            // Слава убийцы начисляется до правил лута: прогресс не зависит от зоны гибели жертвы.
            if (killer.Id != victim.Id && killer.Actor.Kind == ActorKind.Player)
            {
                killer.AwardFame(_config!, victim.Actor.FameOnKill);
            }

            // Моб не игрок: вместо потери снаряжения он оставляет труп, который игрок собирает
            // командой LootCommand (ADR-019). Труп переживает моба и исчезает по таймеру.
            if (victim.Mob != null)
            {
                ResetMobAi(victim);
                CreateCorpse(victim);
                return;
            }

            if (victim.Actor.Kind != ActorKind.Player)
            {
                return;
            }

            var zone = _config!.Zones.GetZone(victim.Position);
            if (zone.FullLootOnDeath)
            {
                victim.Inventory.Clear();
                victim.Equipment.Clear();
            }
            else
            {
                victim.Equipment.ApplyDurabilityLoss(zone.DurabilityLossFraction);
            }

            // Лут и износ меняют характеристики: без пересчёта оружие после полного лута
            // продолжало бы давать способность, а MaxHp — бонусы снятых вещей.
            victim.RecalculateStats(_config);
        }

        private void RegenerateResources(float deltaTime)
        {
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (entity.IsDead || entity.Resource >= entity.MaxResource)
                {
                    entity.ResourceRegenRemainder = 0f;
                    continue;
                }

                var regen = entity.Actor.ResourceRegenPerSecond;
                if (!(regen > 0f))
                {
                    entity.ResourceRegenRemainder = 0f;
                    continue;
                }

                entity.ResourceRegenRemainder += regen * deltaTime;
                if (entity.ResourceRegenRemainder < 1f)
                {
                    continue;
                }

                // Floor, а не Round: дробный остаток копится, целые единицы начисляются по мере
                // накопления. При достижении максимума остаток обнуляется, чтобы не «переполнить»
                // ресурс и не тратить накопленное за впустую.
                var gain = (int)MathF.Floor(entity.ResourceRegenRemainder);
                var room = entity.MaxResource - entity.Resource;
                if (gain >= room)
                {
                    entity.Resource = entity.MaxResource;
                    entity.ResourceRegenRemainder = 0f;
                }
                else
                {
                    entity.Resource += gain;
                    entity.ResourceRegenRemainder -= gain;
                }
            }
        }

        private void ProcessRespawns()
        {
            for (var i = 0; i < _state.Entities.Count; i++)
            {
                var entity = _state.Entities[i];
                if (!entity.IsDead || entity.RespawnAtTick == 0 || _state.Tick + 1 < entity.RespawnAtTick)
                {
                    continue;
                }

                entity.Hp = entity.MaxHp;
                entity.Resource = entity.MaxResource;
                entity.ResourceRegenRemainder = 0f;
                entity.Position = _config == null ? entity.SpawnPosition : _config.Zones.ClampToBounds(entity.SpawnPosition);
                entity.CooldownUntilTick = 0;
                entity.HostileUntilTick = 0;
                entity.RespawnAtTick = 0;

                // Моб после респавна снова патрулирует от точки спавна, а не продолжает погоню.
                if (entity.Mob != null)
                {
                    entity.AiState = entity.Mob.PatrolPoints.Count > 0 ? MobAiState.Patrol : MobAiState.Idle;
                    entity.AiTargetId = 0;
                    entity.NextAttackTick = 0;
                    entity.AiWaitUntilTick = 0;
                    entity.PatrolIndex = 0;
                }

                // Страховочный набор: игрок не возрождается голым, если терять было нечего
                // (полный лут в красной зоне). Если предметы уцелели, набор не дублируется.
                if (_config != null
                    && entity.Actor.Kind == ActorKind.Player
                    && entity.Inventory.IsEmpty
                    && entity.Equipment.IsEmpty)
                {
                    entity.GrantStarterLoadout(_config);
                }
            }
        }

        private static uint ToTicks(float seconds)
        {
            var ticks = (uint)MathF.Round(seconds * SimulationConstants.TickRate);
            return ticks == 0 ? 1u : ticks;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            var dx = left.X - right.X;
            var dz = left.Z - right.Z;
            return MathF.Sqrt((dx * dx) + (dz * dz));
        }

        private static bool IsNear(Vector3 left, Vector3 right, float range)
        {
            return HorizontalDistance(left, right) <= range;
        }
    }
}
