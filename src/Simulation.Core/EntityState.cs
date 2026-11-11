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

        /// <summary>Максимальное здоровье.</summary>
        public int MaxHp { get; }

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

        /// <summary>Инвентарь сущности; у мобов пуст.</summary>
        public Inventory Inventory { get; } = new Inventory();

        /// <summary>Надетое снаряжение сущности; у мобов пусто.</summary>
        public Equipment Equipment { get; } = new Equipment();

        /// <summary>Мёртвая сущность (HP ≤ 0).</summary>
        public bool IsDead => Hp <= 0;

        /// <summary>Действует ли флаг враждебности на указанном тике.</summary>
        /// <param name="currentTick">Текущий тик симуляции.</param>
        /// <returns><c>true</c>, если игрок ещё враждебен.</returns>
        public bool IsHostile(uint currentTick) => HostileUntilTick > currentTick;

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
        }
    }
}
