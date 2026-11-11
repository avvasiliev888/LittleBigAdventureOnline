using System;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Состояние одной сущности в мире. Все геймплейные параметры берутся из <see cref="ActorConfig"/>.
    /// </summary>
    public sealed class EntityState
    {
        /// <summary>Создаёт сущность в точке появления с полным здоровьем и ресурсом.</summary>
        /// <param name="id">Идентификатор сущности в мире.</param>
        /// <param name="actor">Тип актора из конфига.</param>
        /// <param name="spawnPosition">Точка появления и респавна.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="actor"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если идентификатор сущности равен нулю.</exception>
        public EntityState(uint id, ActorConfig actor, Vector3 spawnPosition)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id сущности должен быть положительным.");
            }

            Actor = actor ?? throw new ArgumentNullException(nameof(actor));

            Id = id;
            ActorId = actor.Id;
            MaxHp = actor.MaxHp;
            MaxResource = actor.MaxResource;
            SpawnPosition = spawnPosition;
            Position = spawnPosition;
            Hp = actor.MaxHp;
            Resource = actor.MaxResource;
            MoveSpeed = actor.MoveSpeed;
            AbilityId = actor.AbilityId;
            DamageMultiplier = 1f;
            Silver = actor.StartingSilver;
            Inventory = new Inventory(actor.InventorySlots);
        }

        /// <summary>Идентификатор сущности в мире.</summary>
        public uint Id { get; }

        /// <summary>Идентификатор типа актора.</summary>
        public uint ActorId { get; }

        /// <summary>Тип актора из конфига.</summary>
        public ActorConfig Actor { get; }

        /// <summary>Точка появления и респавна.</summary>
        public Vector3 SpawnPosition { get; }

        /// <summary>Текущая позиция.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Текущее здоровье.</summary>
        public int Hp { get; set; }

        /// <summary>Максимальное здоровье с учётом снаряжения и открытых пассивных узлов.</summary>
        public int MaxHp { get; private set; }

        /// <summary>Скорость движения с учётом снаряжения и открытых пассивных узлов.</summary>
        public float MoveSpeed { get; private set; }

        /// <summary>Действующая способность: оружие в руке определяет ветку дерева навыков.</summary>
        public uint AbilityId { get; private set; }

        /// <summary>Множитель урона от открытых пассивных узлов дерева (1 — без бонуса).</summary>
        public float DamageMultiplier { get; private set; }

        /// <summary>Текущий ресурс.</summary>
        public int Resource { get; set; }

        /// <summary>
        /// Накопленная дробная часть регенерации ресурса (серверная деталь; в снапшот не входит).
        /// Нужна, чтобы при 5 ед/с и тике 0.05 с не терять 0.25 ресурса каждый тик.
        /// </summary>
        public float ResourceRegenRemainder { get; set; }

        /// <summary>Максимальный ресурс.</summary>
        public int MaxResource { get; }

        /// <summary>Тик, до которого способность на кулдауне (включительно).</summary>
        public uint CooldownUntilTick { get; set; }

        /// <summary>Тик, на котором произойдёт респавн (0 — сущность жива).</summary>
        public uint RespawnAtTick { get; set; }

        /// <summary>Тик, до которого действует флаг враждебности (0 — игрок «мирный»).</summary>
        /// <remarks>
        /// Флаг ставится сервером при атаке на игрока в зоне с PvP (ADR-013). В снапшот пока не входит:
        /// поле появится вместе с клиентским UI на этапе интеграции (Protocol v5).
        /// </remarks>
        public uint HostileUntilTick { get; set; }

        /// <summary>Тик, до которого игрок не может собирать ресурсы (0 — можно).</summary>
        public uint GatherCooldownUntilTick { get; set; }

        /// <summary>Слава по архетипам оружия (ADR-015); смена оружия прогресс не сбрасывает.</summary>
        public FameProgress Fame { get; } = new FameProgress();

        /// <summary>
        /// Серебро персонажа (ADR-017): плата за крафт и покупки у NPC, выручка с продаж.
        /// В снапшот пока не входит — поле появится с клиентским UI на этапе интеграции.
        /// </summary>
        public int Silver { get; set; }

        /// <summary>Инвентарь сущности; у мобов пуст.</summary>
        public Inventory Inventory { get; }

        /// <summary>Надетое снаряжение сущности; у мобов пусто.</summary>
        public Equipment Equipment { get; } = new Equipment();

        /// <summary>Мёртвая сущность (HP ≤ 0).</summary>
        public bool IsDead => Hp <= 0;

        /// <summary>Действует ли флаг враждебности на указанном тике.</summary>
        /// <param name="currentTick">Текущий тик симуляции.</param>
        /// <returns><c>true</c>, если игрок ещё враждебен.</returns>
        public bool IsHostile(uint currentTick) => HostileUntilTick > currentTick;

        /// <summary>
        /// Надевает предмет из инвентаря и пересчитывает характеристики. Правила замены слота —
        /// в <see cref="Equipment"/>: старая вещь в этом слоте вытесняется (возврат в инвентарь —
        /// обязанность вызывающего кода, <see cref="Simulation.ApplyEquip"/>).
        /// </summary>
        /// <param name="config">Конфигурация мира с деревом навыков.</param>
        /// <param name="item">Определение снаряжения из конфига.</param>
        /// <exception cref="ArgumentNullException">Если аргумент равен <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Если предмет не является снаряжением.</exception>
        public void Equip(SimulationConfig config, ItemDefinition item)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (item.ItemKind != ItemKind.Equipment || !item.Slot.HasValue)
            {
                throw new ArgumentException(
                    "Надеть можно только снаряжение '" + item.Key + "'.", nameof(item));
            }

            Equipment.Equip(item);
            RecalculateStats(config);
        }

        /// <summary>
        /// Начисляет Славу в ветку оружия в руке и пересчитывает характеристики.
        /// Без оружия (или если ветки нет в дереве) Слава не начисляется: прогресс привязан к оружию.
        /// </summary>
        /// <param name="config">Конфигурация мира с деревом навыков.</param>
        /// <param name="amount">Сколько Славы начислить; неотрицательное.</param>
        /// <returns>Начисленная Слава; 0, если начислять некуда.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="config"/> равен <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Если количество отрицательное.</exception>
        public int AwardFame(SimulationConfig config, int amount)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "Слава не может быть отрицательной.");
            }

            if (amount == 0)
            {
                return 0;
            }

            var archetypeKey = Equipment.Weapon?.Definition.Archetype;
            if (archetypeKey == null || !config.SkillTree.Has(archetypeKey))
            {
                return 0;
            }

            Fame.Add(archetypeKey, amount);
            RecalculateStats(config);
            return amount;
        }

        /// <summary>
        /// Пересчитывает производные характеристики (<see cref="MaxHp"/>, <see cref="MoveSpeed"/>,
        /// <see cref="AbilityId"/>, <see cref="DamageMultiplier"/>) по актору, снаряжению и Славе
        /// в ветке оружия в руке. Вызывается после смены снаряжения, начисления Славы и смерти.
        /// </summary>
        /// <param name="config">Конфигурация мира с деревом навыков.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="config"/> равен <c>null</c>.</exception>
        public void RecalculateStats(SimulationConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var archetypeKey = Equipment.Weapon?.Definition.Archetype;
            var branch = archetypeKey != null && config.SkillTree.TryGet(archetypeKey, out var found) ? found : null;
            var fame = branch == null ? 0 : Fame.Get(branch.Key);

            MaxHp = Actor.MaxHp + Equipment.MaxHpBonus + (branch == null ? 0 : branch.GetMaxHpBonus(fame));
            MoveSpeed = Actor.MoveSpeed
                * (1f + Equipment.MoveSpeedBonus + (branch == null ? 0f : branch.GetMoveSpeedBonus(fame)));
            DamageMultiplier = 1f + (branch == null ? 0f : branch.GetDamageBonus(fame));
            AbilityId = branch == null ? Actor.AbilityId : branch.ResolveAbilityId(fame);

            if (Hp > MaxHp)
            {
                Hp = MaxHp;
            }
        }

        /// <summary>
        /// Выдаёт страховочный набор из конфига актора: предметы в инвентарь, снаряжение — с полной прочностью.
        /// Вызывается при первом появлении игрока и при респавне, если терять было нечего.
        /// </summary>
        /// <param name="config">Конфигурация мира с каталогом предметов.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="config"/> равен <c>null</c>.</exception>
        public void GrantStarterLoadout(SimulationConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            for (var i = 0; i < Actor.StarterItems.Count; i++)
            {
                var starter = Actor.StarterItems[i];
                Inventory.Add(config.GetItem(starter.ItemId), starter.Quantity);
            }

            for (var i = 0; i < Actor.StarterEquipment.Count; i++)
            {
                Equipment.Equip(config.GetItem(Actor.StarterEquipment[i].ItemId));
            }

            // Набор выдаётся на спавне/респавне: после пересчёта бонусов доспехов игрок
            // обязан начать с полным здоровьем, а не с базовым MaxHp актора.
            RecalculateStats(config);
            Hp = MaxHp;
        }
    }
}
