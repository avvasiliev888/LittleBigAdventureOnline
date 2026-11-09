using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Simulation.Core;

namespace Shared
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
    /// Загрузчик JSON-конфигов (ADR-005, ADR-011): единственное место, которое знает про JSON.
    /// Возвращает готовый <see cref="SimulationConfig"/> или бросает <see cref="ConfigValidationException"/>.
    /// Значений по умолчанию нет: отсутствующий файл или поле — ошибка, а не тихий фолбэк.
    /// </summary>
    /// <remarks>
    /// С M9 загрузчик живёт в <c>Shared</c> и собирается под netstandard2.1 — его использует и сервер,
    /// и Unity-клиент, читая один и тот же файл. System.Text.Json не используется: сборка
    /// <c>Shared.dll</c> грузится в Unity, где этого пакета нет (Discovered Issues).
    /// </remarks>
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

            var actorDtos = ParseArray(actorsJson, "actors", source);
            var abilityDtos = ParseArray(abilitiesJson, "abilities", source);

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

        /// <summary>
        /// Считает SHA-256 от содержимого двух файлов конфигурации: одинаковый хеш означает,
        /// что сервер и клиент читают один и тот же файл (M9, Auto-Gate).
        /// </summary>
        /// <param name="directory">Каталог с <see cref="ActorsFileName"/> и <see cref="AbilitiesFileName"/>.</param>
        /// <returns>Хеш в нижнем hex-регистре.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют.</exception>
        public static string ComputeHash(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            return ComputeHashFromJson(
                ReadFile(Path.Combine(directory, ActorsFileName)),
                ReadFile(Path.Combine(directory, AbilitiesFileName)));
        }

        /// <summary>Считает хеш от двух строк JSON (для тестов и сравнения версий конфига).</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <returns>Хеш в нижнем hex-регистре.</returns>
        /// <exception cref="ArgumentNullException">Если строка равна <c>null</c>.</exception>
        public static string ComputeHashFromJson(string actorsJson, string abilitiesJson)
        {
            if (actorsJson == null)
            {
                throw new ArgumentNullException(nameof(actorsJson));
            }

            if (abilitiesJson == null)
            {
                throw new ArgumentNullException(nameof(abilitiesJson));
            }

            using var sha = SHA256.Create();
            var actorBytes = Encoding.UTF8.GetBytes(actorsJson);
            var abilityBytes = Encoding.UTF8.GetBytes(abilitiesJson);
            var buffer = new byte[actorBytes.Length + 1 + abilityBytes.Length];
            actorBytes.CopyTo(buffer, 0);
            buffer[actorBytes.Length] = 0x1F;
            abilityBytes.CopyTo(buffer, actorBytes.Length + 1);

            var hash = sha.ComputeHash(buffer);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ConfigValidationException("Файл конфигурации не найден: " + path);
            }

            return File.ReadAllText(path);
        }

        private static List<Dictionary<string, object?>> ParseArray(string json, string section, string source)
        {
            object? root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (JsonParseException exception)
            {
                throw new ConfigValidationException(
                    source + ": невалидный JSON в " + section + " (ожидается JSON-массив объектов): " + exception.Message + ".",
                    exception);
            }

            if (root is not List<object?> list)
            {
                throw new ConfigValidationException(source + ": " + section + " должен быть JSON-массивом.");
            }

            var result = new List<Dictionary<string, object?>>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object?> dto)
                {
                    throw new ConfigValidationException(source + ": " + section + "[" + i + "] должен быть JSON-объектом.");
                }

                result.Add(dto);
            }

            return result;
        }

        private static ActorConfig ToActor(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": actors[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var maxHp = RequireInt(dto, label, "maxHp");
            var moveSpeed = RequireFloat(dto, label, "moveSpeed");
            var maxResource = RequireInt(dto, label, "maxResource");
            var respawnSeconds = RequireFloat(dto, label, "respawnSeconds");
            var abilityId = RequireUint(dto, label, "abilityId");

            try
            {
                return new ActorConfig(id, maxHp, moveSpeed, maxResource, respawnSeconds, abilityId);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static AbilityConfig ToAbility(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": abilities[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var damage = RequireInt(dto, label, "damage");
            var range = RequireFloat(dto, label, "range");
            var cooldownSeconds = RequireFloat(dto, label, "cooldownSeconds");
            var resourceCost = RequireInt(dto, label, "resourceCost");

            try
            {
                return new AbilityConfig(id, damage, range, cooldownSeconds, resourceCost);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static object RequireValue(Dictionary<string, object?> dto, string label, string field)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                throw new ConfigValidationException(label + ": отсутствует обязательное поле '" + field + "'.");
            }

            return value;
        }

        private static uint RequireUint(Dictionary<string, object?> dto, string label, string field)
        {
            var value = RequireValue(dto, label, field);
            if (value is double number
                && number >= 0
                && number <= uint.MaxValue
                && Math.Floor(number) == number)
            {
                return (uint)number;
            }

            throw new ConfigValidationException(
                label + ": поле '" + field + "' должно быть целым числом 0.." + uint.MaxValue + ".");
        }

        private static int RequireInt(Dictionary<string, object?> dto, string label, string field)
        {
            var value = RequireValue(dto, label, field);
            if (value is double number
                && number >= int.MinValue
                && number <= int.MaxValue
                && Math.Floor(number) == number)
            {
                return (int)number;
            }

            throw new ConfigValidationException(
                label + ": поле '" + field + "' должно быть целым числом " + int.MinValue + ".." + int.MaxValue + ".");
        }

        private static float RequireFloat(Dictionary<string, object?> dto, string label, string field)
        {
            var value = RequireValue(dto, label, field);
            if (value is double number && !double.IsNaN(number) && !double.IsInfinity(number))
            {
                var result = (float)number;
                if (!float.IsNaN(result) && !float.IsInfinity(result))
                {
                    return result;
                }
            }

            throw new ConfigValidationException(label + ": поле '" + field + "' должно быть числом.");
        }
    }
}
