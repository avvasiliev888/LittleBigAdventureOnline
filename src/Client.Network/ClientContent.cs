using System;
using Shared;
using Simulation.Core;

namespace Client.Network
{
    /// <summary>
    /// Data-driven контент клиента (M9, ADR-011): тот же JSON, что у сервера, тем же загрузчиком
    /// из <c>Shared</c>. Клиент использует его для предсказания движения/кулдауна и для UI,
    /// поэтому конфиг не дублируется в клиентском коде.
    /// </summary>
    public sealed class ClientContent
    {
        private ClientContent(SimulationConfig config, string hash)
        {
            Config = config;
            Hash = hash;
        }

        /// <summary>Загруженная конфигурация мира.</summary>
        public SimulationConfig Config { get; }

        /// <summary>SHA-256 содержимого конфигов: совпадает у сервера и клиента при одном файле.</summary>
        public string Hash { get; }

        /// <summary>Загружает конфиг из каталога (в Unity — <c>StreamingAssets/Config</c>).</summary>
        /// <param name="directory">
        /// Каталог с <c>actors.json</c>, <c>abilities.json</c>, <c>zones.json</c>, <c>items.json</c>,
        /// <c>skill-tree.json</c>, <c>resources.json</c>, <c>recipes.json</c>, <c>npc-traders.json</c>
        /// и <c>mobs.json</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют или невалидны.</exception>
        public static ClientContent LoadFromDirectory(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            return new ClientContent(ConfigLoader.LoadFromDirectory(directory), ConfigLoader.ComputeHash(directory));
        }

        /// <summary>Загружает конфиг из строк JSON (тесты, встроенные сценарии).</summary>
        /// <param name="actorsJson">Содержимое <c>actors.json</c>.</param>
        /// <param name="abilitiesJson">Содержимое <c>abilities.json</c>.</param>
        /// <param name="zonesJson">Содержимое <c>zones.json</c>.</param>
        /// <param name="itemsJson">Содержимое <c>items.json</c>.</param>
        /// <param name="source">Метка источника для сообщений об ошибках.</param>
        /// <param name="skillTreeJson">Содержимое <c>skill-tree.json</c>; пустое дерево по умолчанию.</param>
        /// <param name="resourcesJson">Содержимое <c>resources.json</c>; пустой список по умолчанию.</param>
        /// <param name="recipesJson">Содержимое <c>recipes.json</c>; пустой список по умолчанию.</param>
        /// <param name="tradersJson">Содержимое <c>npc-traders.json</c>; пустой список по умолчанию.</param>
        /// <param name="mobsJson">Содержимое <c>mobs.json</c>; пустой список по умолчанию.</param>
        /// <exception cref="ConfigValidationException">Если JSON невалиден.</exception>
        public static ClientContent LoadFromJson(
            string actorsJson,
            string abilitiesJson,
            string zonesJson,
            string itemsJson,
            string source = "client-config",
            string skillTreeJson = ConfigLoader.EmptySkillTreeJson,
            string resourcesJson = ConfigLoader.EmptyResourcesJson,
            string recipesJson = ConfigLoader.EmptyRecipesJson,
            string tradersJson = ConfigLoader.EmptyTradersJson,
            string mobsJson = ConfigLoader.EmptyMobsJson)
        {
            return new ClientContent(
                ConfigLoader.LoadFromJson(
                    actorsJson,
                    abilitiesJson,
                    zonesJson,
                    itemsJson,
                    source,
                    skillTreeJson,
                    resourcesJson,
                    recipesJson,
                    tradersJson,
                    mobsJson),
                ConfigLoader.ComputeHashFromJson(
                    actorsJson,
                    abilitiesJson,
                    zonesJson,
                    itemsJson,
                    skillTreeJson,
                    resourcesJson,
                    recipesJson,
                    tradersJson,
                    mobsJson));
        }

        /// <summary>Тип актора по идентификатору.</summary>
        /// <param name="actorId">Идентификатор типа актора.</param>
        /// <exception cref="InvalidOperationException">Если такого актора нет.</exception>
        public ActorConfig GetActor(uint actorId) => Config.GetActor(actorId);

        /// <summary>Способность по идентификатору.</summary>
        /// <param name="abilityId">Идентификатор способности.</param>
        /// <exception cref="InvalidOperationException">Если такой способности нет.</exception>
        public AbilityConfig GetAbility(uint abilityId) => Config.GetAbility(abilityId);

        /// <summary>Пытается определить тип актора по идентификатору (ADR-019, ADR-020).</summary>
        /// <param name="actorId">Идентификатор актора из снапшота.</param>
        /// <param name="kind">Найденный тип или <c>null</c>, если актор неизвестен.</param>
        /// <returns><c>true</c>, если актор объявлен в <c>actors.json</c> или <c>mobs.json</c>.</returns>
        public bool TryGetActorKind(uint actorId, out ActorKind? kind)
        {
            try
            {
                kind = Config.GetActor(actorId).Kind;
                return true;
            }
            catch (InvalidOperationException)
            {
                // Синтетические акторы мобов не лежат в actors.json: их тип задаёт mobs.json.
            }

            if (Config.TryGetMobByActorId(actorId, out _))
            {
                kind = ActorKind.Mob;
                return true;
            }

            kind = null;
            return false;
        }

        /// <summary>Моб по actorId; <c>null</c>, если actorId не моб.</summary>
        /// <param name="actorId">Идентификатор актора.</param>
        public MobDefinition? FindMobByActorId(uint actorId)
        {
            return Config.TryGetMobByActorId(actorId, out var mob) ? mob : null;
        }
    }
}
