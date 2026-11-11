using System;
using System.Collections.Generic;
using System.Numerics;

namespace Simulation.Core
{
    /// <summary>
    /// Тип PvE-моба из <c>config/mobs.json</c> (PLAN §5, ADR-019): характеристики, радиусы
    /// агрессии и возврата, точки спавна и патруля, награда и таблица лута.
    /// </summary>
    /// <remarks>
    /// Мобы не используют <see cref="AbilityConfig"/>: их удар задан полем <see cref="Damage"/>.
    /// <see cref="Actor"/> — синтезированный профиль актора для <see cref="EntityState"/> и снапшота;
    /// отдельной записи в <c>actors.json</c> у моба нет, чтобы характеристики не дублировались.
    /// </remarks>
    public sealed class MobDefinition
    {
        /// <summary>Радиус круга столкновения моба по умолчанию; значение приходит из JSON.</summary>
        public const float DefaultCollisionRadius = 0.6f;

        private readonly IReadOnlyList<Vector2> _spawns;
        private readonly IReadOnlyList<Vector2> _patrolPoints;
        private readonly List<MobLootEntry> _loot;

        /// <summary>Создаёт описание типа моба.</summary>
        /// <param name="id">Идентификатор типа; положительный и уникальный в конфиге.</param>
        /// <param name="key">Читаемый ключ для логов и UI (например, <c>blue_scavenger</c>).</param>
        /// <param name="name">Отображаемое имя.</param>
        /// <param name="actorId">Идентификатор синтезированного профиля актора в снапшоте; положительный.</param>
        /// <param name="zoneKey">Ключ зоны, в которой живёт моб.</param>
        /// <param name="abilityId">Идентификатор способности профиля актора; положительный.</param>
        /// <param name="maxHp">Максимальное здоровье; положительное.</param>
        /// <param name="damage">Урон одного удара; положительный.</param>
        /// <param name="moveSpeed">Скорость движения, ед/с; положительная.</param>
        /// <param name="attackRange">Радиус удара, ед. мира; положительный.</param>
        /// <param name="attackSeconds">Интервал между ударами, с; положительный.</param>
        /// <param name="aggroRange">Радиус агрессии, ед. мира; положительный.</param>
        /// <param name="leashRange">Радиус, после которого моб возвращается домой; не меньше радиуса агрессии.</param>
        /// <param name="respawnSeconds">Время респавна после смерти, с; положительное.</param>
        /// <param name="fameOnKill">Слава убийце; неотрицательная.</param>
        /// <param name="silverMin">Минимум серебра в трупе; неотрицательный.</param>
        /// <param name="silverMax">Максимум серебра в трупе; не меньше минимума.</param>
        /// <param name="lootRange">Радиус сбора лута из трупа, ед. мира; положительный.</param>
        /// <param name="corpseSeconds">Время жизни трупа, с; положительное.</param>
        /// <param name="patrolPauseSeconds">Пауза в точке патруля, с; неотрицательная.</param>
        /// <param name="patrolPoints">Смещения точек патруля от точки спавна; пусто — моб стоит на месте.</param>
        /// <param name="spawns">Точки спавна в мире; хотя бы одна.</param>
        /// <param name="loot">Таблица лута; пусто — только серебро.</param>
        /// <param name="collisionRadius">Радиус круга столкновения моба, мировые единицы; положительный.</param>
        /// <exception cref="ArgumentOutOfRangeException">Если числовое значение вне допустимого диапазона.</exception>
        /// <exception cref="ArgumentException">Если ключ, имя или зона пусты, либо точки патруля некорректны.</exception>
        public MobDefinition(
            uint id,
            string key,
            string name,
            uint actorId,
            string zoneKey,
            uint abilityId,
            int maxHp,
            int damage,
            float moveSpeed,
            float attackRange,
            float attackSeconds,
            float aggroRange,
            float leashRange,
            float respawnSeconds,
            int fameOnKill,
            int silverMin,
            int silverMax,
            float lootRange,
            float corpseSeconds,
            float patrolPauseSeconds,
            IReadOnlyList<Vector2>? patrolPoints,
            IReadOnlyList<Vector2>? spawns,
            IReadOnlyList<MobLootEntry>? loot,
            float collisionRadius = DefaultCollisionRadius)
        {
            if (id == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Id моба должен быть положительным.");
            }

            if (actorId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(actorId), actorId, "ActorId моба должен быть положительным.");
            }

            if (abilityId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(abilityId), abilityId, "AbilityId моба должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Ключ моба не может быть пустым.", nameof(key));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Имя моба не может быть пустым.", nameof(name));
            }

            if (string.IsNullOrWhiteSpace(zoneKey))
            {
                throw new ArgumentException("Ключ зоны моба не может быть пустым.", nameof(zoneKey));
            }

            if (maxHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "MaxHp моба должен быть положительным.");
            }

            if (damage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damage), damage, "Урон моба должен быть положительным.");
            }

            if (!IsFinite(moveSpeed) || moveSpeed <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "MoveSpeed моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(attackRange) || attackRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(attackRange), attackRange, "AttackRange моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(attackSeconds) || attackSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(attackSeconds), attackSeconds, "AttackSeconds моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(aggroRange) || aggroRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(aggroRange), aggroRange, "AggroRange моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(leashRange) || leashRange < aggroRange)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(leashRange),
                    leashRange,
                    "LeashRange моба должен быть конечным и не меньше AggroRange.");
            }

            if (!IsFinite(respawnSeconds) || respawnSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(respawnSeconds), respawnSeconds, "RespawnSeconds моба должен быть конечным положительным числом.");
            }

            if (fameOnKill < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fameOnKill), fameOnKill, "Слава за убийство моба не может быть отрицательной.");
            }

            if (silverMin < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(silverMin), silverMin, "Минимум серебра не может быть отрицательным.");
            }

            if (silverMax < silverMin)
            {
                throw new ArgumentOutOfRangeException(nameof(silverMax), silverMax, "Максимум серебра не может быть меньше минимума.");
            }

            if (!IsFinite(lootRange) || lootRange <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(lootRange), lootRange, "LootRange моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(corpseSeconds) || corpseSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(corpseSeconds), corpseSeconds, "CorpseSeconds моба должен быть конечным положительным числом.");
            }

            if (!IsFinite(patrolPauseSeconds) || patrolPauseSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patrolPauseSeconds),
                    patrolPauseSeconds,
                    "PatrolPauseSeconds моба должен быть конечным неотрицательным числом.");
            }

            if (!IsFinite(collisionRadius) || collisionRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(collisionRadius),
                    collisionRadius,
                    "CollisionRadius моба должен быть конечным положительным числом.");
            }

            if (spawns == null || spawns.Count == 0)
            {
                throw new ArgumentException("У моба должна быть хотя бы одна точка спавна.", nameof(spawns));
            }

            _spawns = CopyPoints(spawns, nameof(spawns), requireFinite: true);
            _patrolPoints = CopyPoints(patrolPoints, nameof(patrolPoints), requireFinite: true);

            _loot = new List<MobLootEntry>();
            if (loot != null)
            {
                for (var i = 0; i < loot.Count; i++)
                {
                    if (loot[i] == null)
                    {
                        throw new ArgumentException("Таблица лута содержит null в позиции " + i + ".", nameof(loot));
                    }

                    _loot.Add(loot[i]);
                }
            }

            Id = id;
            Key = key;
            Name = name;
            ActorId = actorId;
            ZoneKey = zoneKey;
            MaxHp = maxHp;
            Damage = damage;
            MoveSpeed = moveSpeed;
            AttackRange = attackRange;
            AttackSeconds = attackSeconds;
            AggroRange = aggroRange;
            LeashRange = leashRange;
            RespawnSeconds = respawnSeconds;
            FameOnKill = fameOnKill;
            SilverMin = silverMin;
            SilverMax = silverMax;
            LootRange = lootRange;
            CorpseSeconds = corpseSeconds;
            PatrolPauseSeconds = patrolPauseSeconds;

            // Профиль актора нужен EntityState и снапшоту: у моба нет ресурса, инвентаря и способности
            // в геймплее, поэтому значения-заглушки минимальны и не влияют на баланс.
            Actor = new ActorConfig(
                actorId,
                maxHp,
                moveSpeed,
                maxResource: 0,
                respawnSeconds,
                abilityId,
                kind: ActorKind.Mob,
                fameOnKill: fameOnKill,
                inventorySlots: 1,
                collisionRadius: collisionRadius);
        }

        /// <summary>Идентификатор типа моба.</summary>
        public uint Id { get; }

        /// <summary>Читаемый ключ моба.</summary>
        public string Key { get; }

        /// <summary>Отображаемое имя моба.</summary>
        public string Name { get; }

        /// <summary>Идентификатор синтезированного профиля актора (для снапшота и восстановления).</summary>
        public uint ActorId { get; }

        /// <summary>Ключ зоны обитания.</summary>
        public string ZoneKey { get; }

        /// <summary>Максимальное здоровье.</summary>
        public int MaxHp { get; }

        /// <summary>Урон одного удара.</summary>
        public int Damage { get; }

        /// <summary>Скорость движения, ед/с.</summary>
        public float MoveSpeed { get; }

        /// <summary>Радиус удара.</summary>
        public float AttackRange { get; }

        /// <summary>Интервал между ударами, с.</summary>
        public float AttackSeconds { get; }

        /// <summary>Радиус агрессии.</summary>
        public float AggroRange { get; }

        /// <summary>Радиус возврата домой.</summary>
        public float LeashRange { get; }

        /// <summary>Время респавна, с.</summary>
        public float RespawnSeconds { get; }

        /// <summary>Слава убийце.</summary>
        public int FameOnKill { get; }

        /// <summary>Минимум серебра в трупе.</summary>
        public int SilverMin { get; }

        /// <summary>Максимум серебра в трупе.</summary>
        public int SilverMax { get; }

        /// <summary>Радиус сбора лута из трупа.</summary>
        public float LootRange { get; }

        /// <summary>Время жизни трупа, с.</summary>
        public float CorpseSeconds { get; }

        /// <summary>Пауза в точке патруля, с.</summary>
        public float PatrolPauseSeconds { get; }

        /// <summary>Смещения точек патруля от точки спавна.</summary>
        public IReadOnlyList<Vector2> PatrolPoints => _patrolPoints;

        /// <summary>Точки спавна в мире.</summary>
        public IReadOnlyList<Vector2> Spawns => _spawns;

        /// <summary>Таблица лута.</summary>
        public IReadOnlyList<MobLootEntry> Loot => _loot;

        /// <summary>Профиль актора для <see cref="EntityState"/> и снапшота.</summary>
        public ActorConfig Actor { get; }

        private static IReadOnlyList<Vector2> CopyPoints(IReadOnlyList<Vector2>? source, string parameterName, bool requireFinite)
        {
            var copy = new List<Vector2>();
            if (source == null)
            {
                return copy;
            }

            for (var i = 0; i < source.Count; i++)
            {
                if (requireFinite && (!IsFinite(source[i].X) || !IsFinite(source[i].Y)))
                {
                    throw new ArgumentException("Точка в позиции " + i + " должна быть конечной.", parameterName);
                }

                copy.Add(source[i]);
            }

            return copy.AsReadOnly();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
