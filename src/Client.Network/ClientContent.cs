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
        /// <param name="directory">Каталог с <c>actors.json</c> и <c>abilities.json</c>.</param>
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
        /// <param name="source">Метка источника для сообщений об ошибках.</param>
        /// <exception cref="ConfigValidationException">Если JSON невалиден.</exception>
        public static ClientContent LoadFromJson(string actorsJson, string abilitiesJson, string source = "client-config")
        {
            return new ClientContent(
                ConfigLoader.LoadFromJson(actorsJson, abilitiesJson, source),
                ConfigLoader.ComputeHashFromJson(actorsJson, abilitiesJson));
        }

        /// <summary>Тип актора по идентификатору.</summary>
        /// <param name="actorId">Идентификатор типа актора.</param>
        /// <exception cref="InvalidOperationException">Если такого актора нет.</exception>
        public ActorConfig GetActor(uint actorId) => Config.GetActor(actorId);

        /// <summary>Способность по идентификатору.</summary>
        /// <param name="abilityId">Идентификатор способности.</param>
        /// <exception cref="InvalidOperationException">Если такой способности нет.</exception>
        public AbilityConfig GetAbility(uint abilityId) => Config.GetAbility(abilityId);
    }
}
