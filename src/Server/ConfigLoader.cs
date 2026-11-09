using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Simulation.Core;

namespace Server
{
    /// <summary>
    /// Исключение конфигурации: невалидный JSON, пропущенное поле, дубликат или неразрешённая ссылка.
    /// Сообщение обязано объяснять, что именно и где не так (ADR-005).
    /// </summary>
    public sealed class ConfigValidationException : Exception
    {
        /// <summary>Создаёт исключение с сообщением.</summary>
        /// <param name="message">Понятное человеку описание проблемы.</param>
        public ConfigValidationException(string message)
            : base(message)
        {
        }

        /// <summary>Создаёт исключение с сообщением и внутренней причиной.</summary>
        /// <param name="message">Понятное человеку описание проблемы.</param>
        /// <param name="inner">Исходное исключение.</param>
        public ConfigValidationException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Загрузчик JSON-конфигов (ADR-005): единственное место, которое знает про JSON.
    /// Возвращает готовый <see cref="SimulationConfig"/> или бросает <see cref="ConfigValidationException"/>.
    /// Значений по умолчанию нет: отсутствующий файл или поле — ошибка, а не тихий фолбэк.
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>Имя файла с типами акторов.</summary>
        public const string ActorsFileName = "actors.json";

        /// <summary>Имя файла со способностями.</summary>
        public const string AbilitiesFileName = "abilities.json";

        /// <summary>Загружает конфигурацию из каталога.</summary>
        /// <param name="directory">Каталог с <see cref="ActorsFileName"/> и <see cref="AbilitiesFileName"/>.</param>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют или невалидны.</exception>
        public static SimulationConfig LoadFromDirectory(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            return Load(Path.Combine(directory, ActorsFileName), Path.Combine(directory, AbilitiesFileName));
        }

        /// <summary>Загружает конфигурацию из двух файлов.</summary>
        /// <param name="actorsPath">Путь к <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesPath">Путь к <see cref="AbilitiesFileName"/>.</param>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют или невалидны.</exception>
        public static SimulationConfig Load(string actorsPath, string abilitiesPath)
        {
            if (actorsPath == null)
            {
                throw new ArgumentNullException(nameof(actorsPath));
            }

            if (abilitiesPath == null)
            {
                throw new ArgumentNullException(nameof(abilitiesPath));
            }

            var actorsJson = ReadFile(actorsPath);
            var abilitiesJson = ReadFile(abilitiesPath);

            return LoadFromJson(actorsJson, abilitiesJson, actorsPath);
        }

        /// <summary>Загружает конфигурацию из строк JSON.</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <param name="source">Метка источника для сообщений об ошибках.</param>
        /// <exception cref="ConfigValidationException">Если JSON невалиден.</exception>
        public static SimulationConfig LoadFromJson(string actorsJson, string abilitiesJson, string source = "config")
        {
            if (actorsJson == null)
            {
                throw new ArgumentNullException(nameof(actorsJson));
            }

            if (abilitiesJson == null)
            {
                throw new ArgumentNullException(nameof(abilitiesJson));
            }

            var actorDtos = Deserialize<ActorDto>(actorsJson, ActorDto.RootName, source);
            var abilityDtos = Deserialize<AbilityDto>(abilitiesJson, AbilityDto.RootName, source);

            var actors = new List<ActorConfig>(actorDtos.Count);
            for (var i = 0; i < actorDtos.Count; i++)
            {
                actors.Add(ToActor(actorDtos[i], source, i));
            }

            var abilities = new List<AbilityConfig>(abilityDtos.Count);
            for (var i = 0; i < abilityDtos.Count; i++)
            {
                abilities.Add(ToAbility(abilityDtos[i], source, i));
            }

            try
            {
                return new SimulationConfig(actors, abilities);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(source + ": " + exception.Message, exception);
            }
        }

        private static string ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ConfigValidationException("Файл конфигурации не найден: " + path);
            }

            return File.ReadAllText(path);
        }

        private static List<T> Deserialize<T>(string json, string section, string source)
        {
            try
            {
                var result = JsonSerializer.Deserialize<List<T>>(json, Options);
                if (result == null)
                {
                    throw new ConfigValidationException(source + ": " + section + " должен быть JSON-массивом.");
                }

                return result;
            }
            catch (JsonException exception)
            {
                throw new ConfigValidationException(
                    source + ": невалидный JSON в " + section + " (ожидается JSON-массив объектов): " + exception.Message + ".",
                    exception);
            }
        }

        private static ActorConfig ToActor(ActorDto dto, string source, int index)
        {
            var label = source + ": " + ActorDto.RootName + "[" + index + "]";
            var id = Require(dto.Id, label, "id");
            var maxHp = Require(dto.MaxHp, label, "maxHp");
            var moveSpeed = Require(dto.MoveSpeed, label, "moveSpeed");
            var maxResource = Require(dto.MaxResource, label, "maxResource");
            var respawnSeconds = Require(dto.RespawnSeconds, label, "respawnSeconds");
            var abilityId = Require(dto.AbilityId, label, "abilityId");

            try
            {
                return new ActorConfig(id, maxHp, moveSpeed, maxResource, respawnSeconds, abilityId);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static AbilityConfig ToAbility(AbilityDto dto, string source, int index)
        {
            var label = source + ": " + AbilityDto.RootName + "[" + index + "]";
            var id = Require(dto.Id, label, "id");
            var damage = Require(dto.Damage, label, "damage");
            var range = Require(dto.Range, label, "range");
            var cooldownSeconds = Require(dto.CooldownSeconds, label, "cooldownSeconds");
            var resourceCost = Require(dto.ResourceCost, label, "resourceCost");

            try
            {
                return new AbilityConfig(id, damage, range, cooldownSeconds, resourceCost);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static T Require<T>(T? value, string label, string field)
            where T : struct
        {
            if (!value.HasValue)
            {
                throw new ConfigValidationException(label + ": отсутствует обязательное поле '" + field + "'.");
            }

            return value.Value;
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private sealed class ActorDto
        {
            public const string RootName = "actors";

            public uint? Id { get; set; }

            public int? MaxHp { get; set; }

            public float? MoveSpeed { get; set; }

            public int? MaxResource { get; set; }

            public float? RespawnSeconds { get; set; }

            public uint? AbilityId { get; set; }
        }

        private sealed class AbilityDto
        {
            public const string RootName = "abilities";

            public uint? Id { get; set; }

            public int? Damage { get; set; }

            public float? Range { get; set; }

            public float? CooldownSeconds { get; set; }

            public int? ResourceCost { get; set; }
        }
    }
}
