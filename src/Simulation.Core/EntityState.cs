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

        /// <summary>Мёртвая сущность (HP ≤ 0).</summary>
        public bool IsDead => Hp <= 0;
    }
}
