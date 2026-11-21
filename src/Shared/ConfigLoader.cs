using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
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

        /// <summary>Имя файла с зонами мира (ADR-013).</summary>
        public const string ZonesFileName = "zones.json";

        /// <summary>Имя файла с каталогом предметов (ADR-013).</summary>
        public const string ItemsFileName = "items.json";

        /// <summary>Имя файла с деревом навыков (ADR-015).</summary>
        public const string SkillTreeFileName = "skill-tree.json";

        /// <summary>Имя файла с ресурсными жилами (ADR-015).</summary>
        public const string ResourcesFileName = "resources.json";

        /// <summary>Имя файла с рецептами крафта (ADR-017).</summary>
        public const string RecipesFileName = "recipes.json";

        /// <summary>Имя файла с NPC-торговцами (ADR-017).</summary>
        public const string TradersFileName = "npc-traders.json";

        /// <summary>Имя файла с PvE-мобами (ADR-019).</summary>
        public const string MobsFileName = "mobs.json";

        /// <summary>Имя файла со статическими препятствиями процедурного мира.</summary>
        public const string WorldCollidersFileName = "world-colliders.json";

        /// <summary>Пустое дерево навыков: используется тестами и мирами без прогрессии.</summary>
        public const string EmptySkillTreeJson = "{\"archetypes\": []}";

        /// <summary>Пустой список жил: используется тестами и мирами без добычи.</summary>
        public const string EmptyResourcesJson = "[]";

        /// <summary>Пустой список рецептов: используется тестами и мирами без крафта.</summary>
        public const string EmptyRecipesJson = "[]";

        /// <summary>Пустой список торговцев: используется тестами и мирами без торговли.</summary>
        public const string EmptyTradersJson = "[]";

        /// <summary>Пустой список мобов: используется тестами и мирами без PvE.</summary>
        public const string EmptyMobsJson = "[]";

        /// <summary>Пустой список статических препятствий: тестовые миры без декора.</summary>
        public const string EmptyWorldCollidersJson = "[]";

        /// <summary>Загружает конфигурацию из каталога.</summary>
        /// <param name="directory">
        /// Каталог с <see cref="ActorsFileName"/>, <see cref="AbilitiesFileName"/>,
        /// <see cref="ZonesFileName"/>, <see cref="ItemsFileName"/>, <see cref="SkillTreeFileName"/>,
        /// <see cref="ResourcesFileName"/>, <see cref="RecipesFileName"/>, <see cref="TradersFileName"/>,
        /// <see cref="MobsFileName"/> и <see cref="WorldCollidersFileName"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если обязательные файлы отсутствуют или невалидны.</exception>
        public static SimulationConfig LoadFromDirectory(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            return Load(
                Path.Combine(directory, ActorsFileName),
                Path.Combine(directory, AbilitiesFileName),
                Path.Combine(directory, ZonesFileName),
                Path.Combine(directory, ItemsFileName),
                Path.Combine(directory, SkillTreeFileName),
                Path.Combine(directory, ResourcesFileName),
                Path.Combine(directory, RecipesFileName),
                Path.Combine(directory, TradersFileName),
                Path.Combine(directory, MobsFileName),
                Path.Combine(directory, WorldCollidersFileName));
        }

        /// <summary>Загружает конфигурацию из десяти файлов.</summary>
        /// <param name="actorsPath">Путь к <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesPath">Путь к <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesPath">Путь к <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsPath">Путь к <see cref="ItemsFileName"/>.</param>
        /// <param name="skillTreePath">Путь к <see cref="SkillTreeFileName"/>.</param>
        /// <param name="resourcesPath">Путь к <see cref="ResourcesFileName"/>.</param>
        /// <param name="recipesPath">Путь к <see cref="RecipesFileName"/>.</param>
        /// <param name="tradersPath">Путь к <see cref="TradersFileName"/>.</param>
        /// <param name="mobsPath">Путь к <see cref="MobsFileName"/>.</param>
        /// <param name="worldCollidersPath">
        /// Путь к <see cref="WorldCollidersFileName"/>; <c>null</c> или отсутствующий файл — мир без
        /// статических препятствий (тесты и старые конфиги).
        /// </param>
        /// <exception cref="ConfigValidationException">Если обязательные файлы отсутствуют или невалидны.</exception>
        public static SimulationConfig Load(
            string actorsPath,
            string abilitiesPath,
            string zonesPath,
            string itemsPath,
            string skillTreePath,
            string resourcesPath,
            string recipesPath,
            string tradersPath,
            string mobsPath,
            string? worldCollidersPath = null)
        {
            if (actorsPath == null)
            {
                throw new ArgumentNullException(nameof(actorsPath));
            }

            if (abilitiesPath == null)
            {
                throw new ArgumentNullException(nameof(abilitiesPath));
            }

            if (zonesPath == null)
            {
                throw new ArgumentNullException(nameof(zonesPath));
            }

            if (itemsPath == null)
            {
                throw new ArgumentNullException(nameof(itemsPath));
            }

            if (skillTreePath == null)
            {
                throw new ArgumentNullException(nameof(skillTreePath));
            }

            if (resourcesPath == null)
            {
                throw new ArgumentNullException(nameof(resourcesPath));
            }

            if (recipesPath == null)
            {
                throw new ArgumentNullException(nameof(recipesPath));
            }

            if (tradersPath == null)
            {
                throw new ArgumentNullException(nameof(tradersPath));
            }

            if (mobsPath == null)
            {
                throw new ArgumentNullException(nameof(mobsPath));
            }

            var actorsJson = ReadFile(actorsPath);
            var abilitiesJson = ReadFile(abilitiesPath);
            var zonesJson = ReadFile(zonesPath);
            var itemsJson = ReadFile(itemsPath);
            var skillTreeJson = ReadFile(skillTreePath);
            var resourcesJson = ReadFile(resourcesPath);
            var recipesJson = ReadFile(recipesPath);
            var tradersJson = ReadFile(tradersPath);
            var mobsJson = ReadFile(mobsPath);
            var worldCollidersJson = ReadOptionalFile(worldCollidersPath);

            return LoadFromJson(
                actorsJson,
                abilitiesJson,
                zonesJson,
                itemsJson,
                actorsPath,
                skillTreeJson,
                resourcesJson,
                recipesJson,
                tradersJson,
                mobsJson,
                worldCollidersJson);
        }

        /// <summary>Загружает конфигурацию из строк JSON.</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesJson">Содержимое <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsJson">Содержимое <see cref="ItemsFileName"/>.</param>
        /// <param name="source">Метка источника для сообщений об ошибках.</param>
        /// <param name="skillTreeJson">Содержимое <see cref="SkillTreeFileName"/>; пустое дерево по умолчанию.</param>
        /// <param name="resourcesJson">Содержимое <see cref="ResourcesFileName"/>; пустой список по умолчанию.</param>
        /// <param name="recipesJson">Содержимое <see cref="RecipesFileName"/>; пустой список по умолчанию.</param>
        /// <param name="tradersJson">Содержимое <see cref="TradersFileName"/>; пустой список по умолчанию.</param>
        /// <param name="mobsJson">Содержимое <see cref="MobsFileName"/>; пустой список по умолчанию.</param>
        /// <param name="worldCollidersJson">Содержимое <see cref="WorldCollidersFileName"/>; пустой список по умолчанию.</param>
        /// <exception cref="ConfigValidationException">Если JSON невалиден.</exception>
        public static SimulationConfig LoadFromJson(
            string actorsJson,
            string abilitiesJson,
            string zonesJson,
            string itemsJson,
            string source = "config",
            string skillTreeJson = EmptySkillTreeJson,
            string resourcesJson = EmptyResourcesJson,
            string recipesJson = EmptyRecipesJson,
            string tradersJson = EmptyTradersJson,
            string mobsJson = EmptyMobsJson,
            string worldCollidersJson = EmptyWorldCollidersJson)
        {
            if (actorsJson == null)
            {
                throw new ArgumentNullException(nameof(actorsJson));
            }

            if (abilitiesJson == null)
            {
                throw new ArgumentNullException(nameof(abilitiesJson));
            }

            if (zonesJson == null)
            {
                throw new ArgumentNullException(nameof(zonesJson));
            }

            if (itemsJson == null)
            {
                throw new ArgumentNullException(nameof(itemsJson));
            }

            if (skillTreeJson == null)
            {
                throw new ArgumentNullException(nameof(skillTreeJson));
            }

            if (resourcesJson == null)
            {
                throw new ArgumentNullException(nameof(resourcesJson));
            }

            if (recipesJson == null)
            {
                throw new ArgumentNullException(nameof(recipesJson));
            }

            if (tradersJson == null)
            {
                throw new ArgumentNullException(nameof(tradersJson));
            }

            if (mobsJson == null)
            {
                throw new ArgumentNullException(nameof(mobsJson));
            }

            if (worldCollidersJson == null)
            {
                throw new ArgumentNullException(nameof(worldCollidersJson));
            }

            var actorDtos = ParseArray(actorsJson, "actors", source);
            var abilityDtos = ParseArray(abilitiesJson, "abilities", source);
            var zoneDtos = ParseArray(zonesJson, "zones", source);
            var itemDtos = ParseArray(itemsJson, "items", source);
            var resourceDtos = ParseArray(resourcesJson, "resources", source);
            var recipeDtos = ParseArray(recipesJson, "recipes", source);
            var traderDtos = ParseArray(tradersJson, "npc-traders", source);
            var mobDtos = ParseArray(mobsJson, "mobs", source);
            var colliderDtos = ParseArray(worldCollidersJson, "world-colliders", source);
            var skillTree = ToSkillTree(skillTreeJson, source);

            var items = new List<ItemDefinition>(itemDtos.Count);
            for (var i = 0; i < itemDtos.Count; i++)
            {
                items.Add(ToItem(itemDtos[i], source, i));
            }

            var zones = new List<ZoneConfig>(zoneDtos.Count);
            for (var i = 0; i < zoneDtos.Count; i++)
            {
                zones.Add(ToZone(zoneDtos[i], source, i));
            }

            var abilities = new List<AbilityConfig>(abilityDtos.Count);
            for (var i = 0; i < abilityDtos.Count; i++)
            {
                abilities.Add(ToAbility(abilityDtos[i], source, i));
            }

            var actors = new List<ActorConfig>(actorDtos.Count);
            for (var i = 0; i < actorDtos.Count; i++)
            {
                actors.Add(ToActor(actorDtos[i], source, i));
            }

            var nodes = new List<ResourceNodeConfig>(resourceDtos.Count);
            for (var i = 0; i < resourceDtos.Count; i++)
            {
                nodes.Add(ToResourceNode(resourceDtos[i], source, i));
            }

            var recipes = new List<Recipe>(recipeDtos.Count);
            for (var i = 0; i < recipeDtos.Count; i++)
            {
                recipes.Add(ToRecipe(recipeDtos[i], source, i));
            }

            var traders = new List<NpcTrader>(traderDtos.Count);
            for (var i = 0; i < traderDtos.Count; i++)
            {
                traders.Add(ToTrader(traderDtos[i], source, i));
            }

            var mobs = new List<MobDefinition>(mobDtos.Count);
            for (var i = 0; i < mobDtos.Count; i++)
            {
                mobs.Add(ToMob(mobDtos[i], source, i));
            }

            var colliders = new List<WorldCollider>(colliderDtos.Count);
            for (var i = 0; i < colliderDtos.Count; i++)
            {
                colliders.Add(ToWorldCollider(colliderDtos[i], source, i));
            }

            try
            {
                return new SimulationConfig(
                    actors,
                    abilities,
                    zones,
                    items,
                    skillTree,
                    nodes,
                    recipes,
                    traders,
                    mobs,
                    colliders);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(source + ": " + exception.Message, exception);
            }
        }

        /// <summary>
        /// Считает SHA-256 от содержимого десяти файлов конфигурации: одинаковый хеш означает,
        /// что сервер и клиент читают одни и те же файлы (M9, Auto-Gate).
        /// </summary>
        /// <param name="directory">
        /// Каталог с <see cref="ActorsFileName"/>, <see cref="AbilitiesFileName"/>,
        /// <see cref="ZonesFileName"/>, <see cref="ItemsFileName"/>, <see cref="SkillTreeFileName"/>,
        /// <see cref="ResourcesFileName"/>, <see cref="RecipesFileName"/>, <see cref="TradersFileName"/>,
        /// <see cref="MobsFileName"/> и <see cref="WorldCollidersFileName"/>.
        /// </param>
        /// <returns>Хеш в нижнем hex-регистре.</returns>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если обязательные файлы отсутствуют.</exception>
        public static string ComputeHash(string directory)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            return ComputeHashFromJson(
                ReadFile(Path.Combine(directory, ActorsFileName)),
                ReadFile(Path.Combine(directory, AbilitiesFileName)),
                ReadFile(Path.Combine(directory, ZonesFileName)),
                ReadFile(Path.Combine(directory, ItemsFileName)),
                ReadFile(Path.Combine(directory, SkillTreeFileName)),
                ReadFile(Path.Combine(directory, ResourcesFileName)),
                ReadFile(Path.Combine(directory, RecipesFileName)),
                ReadFile(Path.Combine(directory, TradersFileName)),
                ReadFile(Path.Combine(directory, MobsFileName)),
                ReadOptionalFile(Path.Combine(directory, WorldCollidersFileName)));
        }

        /// <summary>Считает хеш от строк JSON всех десяти файлов конфигурации.</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesJson">Содержимое <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsJson">Содержимое <see cref="ItemsFileName"/>.</param>
        /// <param name="skillTreeJson">Содержимое <see cref="SkillTreeFileName"/>.</param>
        /// <param name="resourcesJson">Содержимое <see cref="ResourcesFileName"/>.</param>
        /// <param name="recipesJson">Содержимое <see cref="RecipesFileName"/>.</param>
        /// <param name="tradersJson">Содержимое <see cref="TradersFileName"/>.</param>
        /// <param name="mobsJson">Содержимое <see cref="MobsFileName"/>.</param>
        /// <param name="worldCollidersJson">Содержимое <see cref="WorldCollidersFileName"/>; пустой список по умолчанию.</param>
        /// <returns>Хеш в нижнем hex-регистре.</returns>
        /// <exception cref="ArgumentNullException">Если строка равна <c>null</c>.</exception>
        public static string ComputeHashFromJson(
            string actorsJson,
            string abilitiesJson,
            string zonesJson,
            string itemsJson,
            string skillTreeJson,
            string resourcesJson,
            string recipesJson,
            string tradersJson,
            string mobsJson,
            string worldCollidersJson = EmptyWorldCollidersJson)
        {
            if (actorsJson == null)
            {
                throw new ArgumentNullException(nameof(actorsJson));
            }

            if (abilitiesJson == null)
            {
                throw new ArgumentNullException(nameof(abilitiesJson));
            }

            if (zonesJson == null)
            {
                throw new ArgumentNullException(nameof(zonesJson));
            }

            if (itemsJson == null)
            {
                throw new ArgumentNullException(nameof(itemsJson));
            }

            if (skillTreeJson == null)
            {
                throw new ArgumentNullException(nameof(skillTreeJson));
            }

            if (resourcesJson == null)
            {
                throw new ArgumentNullException(nameof(resourcesJson));
            }

            if (recipesJson == null)
            {
                throw new ArgumentNullException(nameof(recipesJson));
            }

            if (tradersJson == null)
            {
                throw new ArgumentNullException(nameof(tradersJson));
            }

            if (mobsJson == null)
            {
                throw new ArgumentNullException(nameof(mobsJson));
            }

            if (worldCollidersJson == null)
            {
                throw new ArgumentNullException(nameof(worldCollidersJson));
            }

            using var sha = SHA256.Create();
            using var stream = new MemoryStream();
            AppendSection(stream, Encoding.UTF8.GetBytes(actorsJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(abilitiesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(zonesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(itemsJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(skillTreeJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(resourcesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(recipesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(tradersJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(mobsJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(worldCollidersJson));

            var hash = sha.ComputeHash(stream.ToArray());
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
            {
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static void AppendSection(MemoryStream stream, byte[] section)
        {
            stream.Write(section, 0, section.Length);
            stream.WriteByte(0x1F);
        }

        private static string ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new ConfigValidationException("Файл конфигурации не найден: " + path);
            }

            return File.ReadAllText(path);
        }

        /// <summary>
        /// Читает необязательный файл. Отсутствие <c>world-colliders.json</c> допустимо для старых
        /// тестовых каталогов и означает мир без статических препятствий, а не тихий сброс боевых данных.
        /// </summary>
        private static string ReadOptionalFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return EmptyWorldCollidersJson;
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

        private static Dictionary<string, object?> ParseObject(string json, string section, string source)
        {
            object? root;
            try
            {
                root = MiniJson.Parse(json);
            }
            catch (JsonParseException exception)
            {
                throw new ConfigValidationException(
                    source + ": невалидный JSON в " + section + " (ожидается JSON-объект): " + exception.Message + ".",
                    exception);
            }

            if (root is not Dictionary<string, object?> dto)
            {
                throw new ConfigValidationException(source + ": " + section + " должен быть JSON-объектом.");
            }

            return dto;
        }

        private static SkillTree ToSkillTree(string json, string source)
        {
            var root = ParseObject(json, "skill-tree", source);
            var entries = OptionalObjectArray(root, source + ": skill-tree", "archetypes");
            var archetypes = new List<SkillArchetype>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                archetypes.Add(ToSkillArchetype(entries[i], source, i));
            }

            try
            {
                return new SkillTree(archetypes);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(source + ": " + exception.Message, exception);
            }
        }

        private static SkillArchetype ToSkillArchetype(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": skill-tree.archetypes[" + index + "]";
            var key = RequireString(dto, label, "key");
            var name = RequireString(dto, label, "name");
            var nodeDtos = OptionalObjectArray(dto, label, "nodes");
            var nodes = new List<SkillNode>(nodeDtos.Count);
            for (var i = 0; i < nodeDtos.Count; i++)
            {
                nodes.Add(ToSkillNode(nodeDtos[i], label, i));
            }

            try
            {
                return new SkillArchetype(key, name, nodes);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        private static SkillNode ToSkillNode(Dictionary<string, object?> dto, string archetypeLabel, int index)
        {
            var label = archetypeLabel + ".nodes[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var name = RequireString(dto, label, "name");
            var fameThreshold = RequireInt(dto, label, "fameThreshold");
            var kind = ToSkillNodeKind(RequireString(dto, label, "kind"), label);
            var abilityId = kind == SkillNodeKind.Active ? RequireUint(dto, label, "abilityId") : 0u;
            var damageBonus = OptionalFloat(dto, label, "damageBonus", 0f);
            var maxHpBonus = OptionalInt(dto, label, "maxHpBonus", 0);
            var moveSpeedBonus = OptionalFloat(dto, label, "moveSpeedBonus", 0f);

            try
            {
                return new SkillNode(id, name, fameThreshold, kind, abilityId, damageBonus, maxHpBonus, moveSpeedBonus);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static ResourceNodeConfig ToResourceNode(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": resources[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var itemId = RequireUint(dto, label, "itemId");
            var x = RequireFloat(dto, label, "x");
            var z = RequireFloat(dto, label, "z");
            var amount = RequireInt(dto, label, "amount");
            var gatherSeconds = RequireFloat(dto, label, "gatherSeconds");
            var famePerGather = OptionalInt(dto, label, "famePerGather", 0);
            var range = RequireFloat(dto, label, "range");

            try
            {
                return new ResourceNodeConfig(id, key, itemId, x, z, amount, gatherSeconds, famePerGather, range);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        private static WorldCollider ToWorldCollider(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": world-colliders[" + index + "]";
            var id = RequireString(dto, label, "id");
            var kind = OptionalString(dto, label, "kind") ?? "obstacle";
            var x = RequireFloat(dto, label, "x");
            var z = RequireFloat(dto, label, "z");
            var radius = RequireFloat(dto, label, "radius");

            try
            {
                return new WorldCollider(id, x, z, radius, kind);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
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
            var resourceRegenPerSecond = RequireFloat(dto, label, "resourceRegenPerSecond");
            var kind = ToActorKind(RequireString(dto, label, "kind"), label);
            var starterItems = ToStarterItems(dto, label);
            var starterEquipment = ToStarterEquipment(dto, label);
            var fameOnKill = OptionalInt(dto, label, "fameOnKill", 0);
            var inventorySlots = OptionalInt(dto, label, "inventorySlots", Inventory.DefaultCapacity);
            var startingSilver = OptionalInt(dto, label, "startingSilver", 0);
            var collisionRadius = OptionalFloat(dto, label, "collisionRadius", ActorConfig.DefaultCollisionRadius);

            try
            {
                return new ActorConfig(
                    id,
                    maxHp,
                    moveSpeed,
                    maxResource,
                    respawnSeconds,
                    abilityId,
                    resourceRegenPerSecond,
                    kind,
                    starterItems,
                    starterEquipment,
                    fameOnKill,
                    inventorySlots,
                    startingSilver,
                    collisionRadius);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
        }

        private static Recipe ToRecipe(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": recipes[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var outputItemId = RequireUint(dto, label, "outputItemId");
            var outputQuantity = OptionalInt(dto, label, "outputQuantity", 1);
            var famePerCraft = OptionalInt(dto, label, "famePerCraft", 0);
            var materialDtos = RequireObjectArray(dto, label, "materials");
            var materials = new List<RecipeMaterial>(materialDtos.Count);
            for (var i = 0; i < materialDtos.Count; i++)
            {
                var materialLabel = label + ".materials[" + i + "]";
                var itemId = RequireUint(materialDtos[i], materialLabel, "itemId");
                var quantity = RequireInt(materialDtos[i], materialLabel, "quantity");

                try
                {
                    materials.Add(new RecipeMaterial(itemId, quantity));
                }
                catch (ArgumentException exception)
                {
                    throw new ConfigValidationException(materialLabel + ": " + exception.Message, exception);
                }
            }

            try
            {
                return new Recipe(id, key, outputItemId, outputQuantity, materials, famePerCraft);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        private static NpcTrader ToTrader(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": npc-traders[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var zoneKey = RequireString(dto, label, "zoneKey");
            var x = RequireFloat(dto, label, "x");
            var z = RequireFloat(dto, label, "z");
            var range = RequireFloat(dto, label, "range");
            var priceDtos = RequireObjectArray(dto, label, "prices");
            var prices = new List<TraderPrice>(priceDtos.Count);
            for (var i = 0; i < priceDtos.Count; i++)
            {
                var priceLabel = label + ".prices[" + i + "]";
                var itemId = RequireUint(priceDtos[i], priceLabel, "itemId");
                var buyPrice = RequireInt(priceDtos[i], priceLabel, "buyPrice");
                var sellPrice = RequireInt(priceDtos[i], priceLabel, "sellPrice");

                try
                {
                    prices.Add(new TraderPrice(itemId, buyPrice, sellPrice));
                }
                catch (ArgumentException exception)
                {
                    throw new ConfigValidationException(priceLabel + ": " + exception.Message, exception);
                }
            }

            try
            {
                return new NpcTrader(id, key, zoneKey, x, z, range, prices);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        private static MobDefinition ToMob(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": mobs[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var name = RequireString(dto, label, "name");
            var actorId = RequireUint(dto, label, "actorId");
            var zoneKey = RequireString(dto, label, "zoneKey");
            var abilityId = RequireUint(dto, label, "abilityId");
            var maxHp = RequireInt(dto, label, "maxHp");
            var damage = RequireInt(dto, label, "damage");
            var moveSpeed = RequireFloat(dto, label, "moveSpeed");
            var attackRange = RequireFloat(dto, label, "attackRange");
            var attackSeconds = RequireFloat(dto, label, "attackSeconds");
            var aggroRange = RequireFloat(dto, label, "aggroRange");
            var leashRange = RequireFloat(dto, label, "leashRange");
            var respawnSeconds = RequireFloat(dto, label, "respawnSeconds");
            var fameOnKill = RequireInt(dto, label, "fameOnKill");
            var silverMin = RequireInt(dto, label, "silverMin");
            var silverMax = RequireInt(dto, label, "silverMax");
            var lootRange = RequireFloat(dto, label, "lootRange");
            var corpseSeconds = RequireFloat(dto, label, "corpseSeconds");
            var patrolPauseSeconds = OptionalFloat(dto, label, "patrolPauseSeconds", 0f);
            var collisionRadius = OptionalFloat(dto, label, "collisionRadius", MobDefinition.DefaultCollisionRadius);
            var keepDistance = OptionalFloat(dto, label, "keepDistance", 0f);
            var weaponType = OptionalString(dto, label, "weaponType", MobDefinition.DefaultWeaponType);

            var spawnDtos = RequireObjectArray(dto, label, "spawns");
            var spawns = ToPoints(spawnDtos, label + ".spawns");
            var patrolDtos = OptionalObjectArray(dto, label, "patrol");
            var patrol = ToPoints(patrolDtos, label + ".patrol");

            var lootDtos = OptionalObjectArray(dto, label, "loot");
            var loot = new List<MobLootEntry>(lootDtos.Count);
            for (var i = 0; i < lootDtos.Count; i++)
            {
                var entryLabel = label + ".loot[" + i + "]";
                var itemId = RequireUint(lootDtos[i], entryLabel, "itemId");
                var minQuantity = RequireInt(lootDtos[i], entryLabel, "minQuantity");
                var maxQuantity = RequireInt(lootDtos[i], entryLabel, "maxQuantity");
                var chance = OptionalFloat(lootDtos[i], entryLabel, "chance", 1f);

                try
                {
                    loot.Add(new MobLootEntry(itemId, minQuantity, maxQuantity, chance));
                }
                catch (ArgumentException exception)
                {
                    throw new ConfigValidationException(entryLabel + ": " + exception.Message, exception);
                }
            }

            try
            {
                return new MobDefinition(
                    id,
                    key,
                    name,
                    actorId,
                    zoneKey,
                    abilityId,
                    maxHp,
                    damage,
                    moveSpeed,
                    attackRange,
                    attackSeconds,
                    aggroRange,
                    leashRange,
                    respawnSeconds,
                    fameOnKill,
                    silverMin,
                    silverMax,
                    lootRange,
                    corpseSeconds,
                    patrolPauseSeconds,
                    patrol,
                    spawns,
                    loot,
                    collisionRadius,
                    keepDistance,
                    weaponType);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        /// <summary>Читает массив точек <c>{x, z}</c> в координаты плоскости X/Z.</summary>
        private static List<Vector2> ToPoints(List<Dictionary<string, object?>> dtos, string label)
        {
            var points = new List<Vector2>(dtos.Count);
            for (var i = 0; i < dtos.Count; i++)
            {
                var pointLabel = label + "[" + i + "]";
                points.Add(new Vector2(RequireFloat(dtos[i], pointLabel, "x"), RequireFloat(dtos[i], pointLabel, "z")));
            }

            return points;
        }

        /// <summary>Читает стартовые предметы актора; отсутствующее поле — пустой набор.</summary>
        private static IReadOnlyList<LoadoutItem> ToStarterItems(Dictionary<string, object?> dto, string label)
        {
            var entries = OptionalObjectArray(dto, label, "starterItems");
            var result = new List<LoadoutItem>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entryLabel = label + ".starterItems[" + i + "]";
                var itemId = RequireUint(entries[i], entryLabel, "itemId");
                var quantity = RequireInt(entries[i], entryLabel, "quantity");

                try
                {
                    result.Add(new LoadoutItem(itemId, quantity));
                }
                catch (ArgumentException exception)
                {
                    throw new ConfigValidationException(entryLabel + ": " + exception.Message, exception);
                }
            }

            return result;
        }

        /// <summary>Читает стартовое снаряжение актора; отсутствующее поле — пустой набор.</summary>
        private static IReadOnlyList<LoadoutEquipment> ToStarterEquipment(Dictionary<string, object?> dto, string label)
        {
            var entries = OptionalObjectArray(dto, label, "starterEquipment");
            var result = new List<LoadoutEquipment>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var entryLabel = label + ".starterEquipment[" + i + "]";
                var itemId = RequireUint(entries[i], entryLabel, "itemId");

                try
                {
                    result.Add(new LoadoutEquipment(itemId));
                }
                catch (ArgumentException exception)
                {
                    throw new ConfigValidationException(entryLabel + ": " + exception.Message, exception);
                }
            }

            return result;
        }

        private static ZoneConfig ToZone(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": zones[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var kind = ToZoneKind(RequireString(dto, label, "kind"), label);
            var minX = RequireFloat(dto, label, "minX");
            var minZ = RequireFloat(dto, label, "minZ");
            var maxX = RequireFloat(dto, label, "maxX");
            var maxZ = RequireFloat(dto, label, "maxZ");
            var pvpEnabled = RequireBool(dto, label, "pvpEnabled");
            var fullLootOnDeath = RequireBool(dto, label, "fullLootOnDeath");
            var durabilityLossFraction = RequireFloat(dto, label, "durabilityLossFraction");
            var hostileFlagSeconds = RequireFloat(dto, label, "hostileFlagSeconds");
            var resourceTierMin = RequireInt(dto, label, "resourceTierMin");
            var resourceTierMax = RequireInt(dto, label, "resourceTierMax");
            var gates = ToZoneGates(dto, label);

            try
            {
                return new ZoneConfig(
                    id,
                    key,
                    kind,
                    minX,
                    minZ,
                    maxX,
                    maxZ,
                    pvpEnabled,
                    fullLootOnDeath,
                    durabilityLossFraction,
                    hostileFlagSeconds,
                    resourceTierMin,
                    resourceTierMax,
                    gates);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        /// <summary>Читает необязательные проходы зоны; отсутствие поля — граница без контроля.</summary>
        private static List<ZoneGate> ToZoneGates(Dictionary<string, object?> dto, string label)
        {
            var entries = OptionalObjectArray(dto, label, "gates");
            var gates = new List<ZoneGate>(entries.Count);
            for (var i = 0; i < entries.Count; i++)
            {
                var gateLabel = label + ".gates[" + i + "]";
                var toZoneId = RequireUint(entries[i], gateLabel, "toZoneId");
                var x = RequireFloat(entries[i], gateLabel, "x");
                var z = RequireFloat(entries[i], gateLabel, "z");
                var halfWidth = RequireFloat(entries[i], gateLabel, "halfWidth");
                try
                {
                    gates.Add(new ZoneGate(toZoneId, x, z, halfWidth));
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new ConfigValidationException(gateLabel + ": " + exception.Message, exception);
                }
            }

            return gates;
        }

        private static ItemDefinition ToItem(Dictionary<string, object?> dto, string source, int index)
        {
            var label = source + ": items[" + index + "]";
            var id = RequireUint(dto, label, "id");
            var key = RequireString(dto, label, "key");
            var kind = ToItemKind(RequireString(dto, label, "kind"), label);
            var tier = RequireInt(dto, label, "tier");
            var maxDurability = RequireInt(dto, label, "maxDurability");
            EquipmentSlot? slot = null;
            if (dto.TryGetValue("slot", out var slotValue) && slotValue != null)
            {
                if (slotValue is not string slotText)
                {
                    throw new ConfigValidationException(label + ": поле 'slot' должно быть строкой.");
                }

                slot = ToEquipmentSlot(slotText, label);
            }

            var archetype = OptionalString(dto, label, "archetype");
            var damageBonus = OptionalInt(dto, label, "damageBonus", 0);
            var maxHpBonus = OptionalInt(dto, label, "maxHpBonus", 0);
            var moveSpeedBonus = OptionalFloat(dto, label, "moveSpeedBonus", 0f);

            try
            {
                return new ItemDefinition(id, key, kind, tier, maxDurability, slot, archetype, damageBonus, maxHpBonus, moveSpeedBonus);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
        }

        private static ActorKind ToActorKind(string text, string label)
        {
            switch (text)
            {
                case "player":
                    return ActorKind.Player;
                case "mob":
                    return ActorKind.Mob;
                default:
                    throw new ConfigValidationException(
                        label + ": поле 'kind' должно быть 'player' или 'mob', получено '" + text + "'.");
            }
        }

        private static ZoneKind ToZoneKind(string text, string label)
        {
            switch (text)
            {
                case "blue":
                    return ZoneKind.Blue;
                case "yellow":
                    return ZoneKind.Yellow;
                case "red":
                    return ZoneKind.Red;
                default:
                    throw new ConfigValidationException(
                        label + ": поле 'kind' должно быть 'blue', 'yellow' или 'red', получено '" + text + "'.");
            }
        }

        private static ItemKind ToItemKind(string text, string label)
        {
            switch (text)
            {
                case "material":
                    return ItemKind.Material;
                case "equipment":
                    return ItemKind.Equipment;
                default:
                    throw new ConfigValidationException(
                        label + ": поле 'kind' должно быть 'material' или 'equipment', получено '" + text + "'.");
            }
        }

        private static SkillNodeKind ToSkillNodeKind(string text, string label)
        {
            switch (text)
            {
                case "passive":
                    return SkillNodeKind.Passive;
                case "active":
                    return SkillNodeKind.Active;
                default:
                    throw new ConfigValidationException(
                        label + ": поле 'kind' должно быть 'passive' или 'active', получено '" + text + "'.");
            }
        }

        private static EquipmentSlot ToEquipmentSlot(string text, string label)
        {
            switch (text)
            {
                case "mainHand":
                    return EquipmentSlot.MainHand;
                case "chest":
                    return EquipmentSlot.Chest;
                case "legs":
                    return EquipmentSlot.Legs;
                case "feet":
                    return EquipmentSlot.Feet;
                default:
                    throw new ConfigValidationException(
                        label + ": поле 'slot' должно быть 'mainHand', 'chest', 'legs' или 'feet', получено '"
                        + text + "'.");
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

        private static string RequireString(Dictionary<string, object?> dto, string label, string field)
        {
            var value = RequireValue(dto, label, field);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            throw new ConfigValidationException(
                label + ": поле '" + field + "' должно быть непустой строкой.");
        }

        private static string OptionalString(
            Dictionary<string, object?> dto, string label, string field, string fallback)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return fallback;
            }

            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            throw new ConfigValidationException(
                label + ": поле '" + field + "' должно быть непустой строкой.");
        }

        private static bool RequireBool(Dictionary<string, object?> dto, string label, string field)
        {
            var value = RequireValue(dto, label, field);
            if (value is bool flag)
            {
                return flag;
            }

            throw new ConfigValidationException(
                label + ": поле '" + field + "' должно быть true или false.");
        }

        private static int OptionalInt(Dictionary<string, object?> dto, string label, string field, int fallback)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return fallback;
            }

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

        private static float OptionalFloat(Dictionary<string, object?> dto, string label, string field, float fallback)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return fallback;
            }

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

        private static string? OptionalString(Dictionary<string, object?> dto, string label, string field)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return null;
            }

            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            throw new ConfigValidationException(label + ": поле '" + field + "' должно быть непустой строкой.");
        }

        /// <summary>Читает необязательный массив JSON-объектов; отсутствующее поле — пустой список.</summary>
        private static List<Dictionary<string, object?>> OptionalObjectArray(
            Dictionary<string, object?> dto,
            string label,
            string field)
        {
            var entries = OptionalObjectArrayOrNull(dto, label, field);
            if (entries == null)
            {
                return new List<Dictionary<string, object?>>();
            }

            return entries;
        }

        /// <summary>Читает обязательный массив JSON-объектов; отсутствующее поле — ошибка.</summary>
        private static List<Dictionary<string, object?>> RequireObjectArray(
            Dictionary<string, object?> dto,
            string label,
            string field)
        {
            var entries = OptionalObjectArrayOrNull(dto, label, field);
            if (entries == null)
            {
                throw new ConfigValidationException(label + ": поле '" + field + "' обязательно и должно быть JSON-массивом.");
            }

            return entries;
        }

        private static List<Dictionary<string, object?>>? OptionalObjectArrayOrNull(
            Dictionary<string, object?> dto,
            string label,
            string field)
        {
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return null;
            }

            if (value is not List<object?> list)
            {
                throw new ConfigValidationException(
                    label + ": поле '" + field + "' должно быть JSON-массивом объектов.");
            }

            var result = new List<Dictionary<string, object?>>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object?> entry)
                {
                    throw new ConfigValidationException(
                        label + ": поле '" + field + "[" + i + "]' должно быть JSON-объектом.");
                }

                result.Add(entry);
            }

            return result;
        }
    }
}
