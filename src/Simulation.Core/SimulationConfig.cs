using System;
using System.Collections.Generic;

namespace Simulation.Core
{
    /// <summary>
    /// Полная конфигурация мира (M3, ADR-005): типы акторов и способности.
    /// Создаётся загрузчиком из JSON; ни одно геймплейное число в коде не дублируется.
    /// </summary>
    public sealed class SimulationConfig
    {
        private readonly Dictionary<uint, ActorConfig> _actors;
        private readonly Dictionary<uint, AbilityConfig> _abilities;

        /// <summary>Создаёт конфигурацию из типов акторов и способностей.</summary>
        /// <param name="actors">Типы акторов; идентификаторы уникальны.</param>
        /// <param name="abilities">Способности; идентификаторы уникальны, все ссылки акторов разрешаются.</param>
        /// <exception cref="ArgumentNullException">Если коллекция равна <c>null</c>.</exception>
        /// <exception cref="ArgumentException">При дубликатах идентификаторов или неразрешённой ссылке на способность.</exception>
        public SimulationConfig(IEnumerable<ActorConfig> actors, IEnumerable<AbilityConfig> abilities)
        {
            if (actors == null)
            {
                throw new ArgumentNullException(nameof(actors));
            }

            if (abilities == null)
            {
                throw new ArgumentNullException(nameof(abilities));
            }

            _abilities = new Dictionary<uint, AbilityConfig>();
            foreach (var ability in abilities)
            {
                if (!_abilities.TryAdd(ability.Id, ability))
                {
                    throw new ArgumentException("Дубликат Id способности: " + ability.Id + ".", nameof(abilities));
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
            }

            Actors = _actors;
            Abilities = _abilities;
        }

        /// <summary>Типы акторов по идентификатору.</summary>
        public IReadOnlyDictionary<uint, ActorConfig> Actors { get; }

        /// <summary>Способности по идентификатору.</summary>
        public IReadOnlyDictionary<uint, AbilityConfig> Abilities { get; }

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
    }
}
