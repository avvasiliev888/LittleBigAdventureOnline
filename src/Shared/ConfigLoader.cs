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

        /// <summary>Имя файла с зонами мира (ADR-013).</summary>
        public const string ZonesFileName = "zones.json";

        /// <summary>Имя файла с каталогом предметов (ADR-013).</summary>
        public const string ItemsFileName = "items.json";

        /// <summary>Имя файла с деревом навыков (ADR-015).</summary>
        public const string SkillTreeFileName = "skill-tree.json";

        /// <summary>Имя файла с ресурсными жилами (ADR-015).</summary>
        public const string ResourcesFileName = "resources.json";

        /// <summary>Пустое дерево навыков: используется тестами и мирами без прогрессии.</summary>
        public const string EmptySkillTreeJson = "{\"archetypes\": []}";

        /// <summary>Пустой список жил: используется тестами и мирами без добычи.</summary>
        public const string EmptyResourcesJson = "[]";

        /// <summary>Загружает конфигурацию из каталога.</summary>
        /// <param name="directory">
        /// Каталог с <see cref="ActorsFileName"/>, <see cref="AbilitiesFileName"/>,
        /// <see cref="ZonesFileName"/>, <see cref="ItemsFileName"/>, <see cref="SkillTreeFileName"/>
        /// и <see cref="ResourcesFileName"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">Если <paramref name="directory"/> равен <c>null</c>.</exception>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют или невалидны.</exception>
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
                Path.Combine(directory, ResourcesFileName));
        }

        /// <summary>Загружает конфигурацию из шести файлов.</summary>
        /// <param name="actorsPath">Путь к <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesPath">Путь к <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesPath">Путь к <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsPath">Путь к <see cref="ItemsFileName"/>.</param>
        /// <param name="skillTreePath">Путь к <see cref="SkillTreeFileName"/>.</param>
        /// <param name="resourcesPath">Путь к <see cref="ResourcesFileName"/>.</param>
        /// <exception cref="ConfigValidationException">Если файлы отсутствуют или невалидны.</exception>
        public static SimulationConfig Load(
            string actorsPath,
            string abilitiesPath,
            string zonesPath,
            string itemsPath,
            string skillTreePath,
            string resourcesPath)
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

            var actorsJson = ReadFile(actorsPath);
            var abilitiesJson = ReadFile(abilitiesPath);
            var zonesJson = ReadFile(zonesPath);
            var itemsJson = ReadFile(itemsPath);
            var skillTreeJson = ReadFile(skillTreePath);
            var resourcesJson = ReadFile(resourcesPath);

            return LoadFromJson(
                actorsJson,
                abilitiesJson,
                zonesJson,
                itemsJson,
                actorsPath,
                skillTreeJson,
                resourcesJson);
        }

        /// <summary>Загружает конфигурацию из строк JSON.</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesJson">Содержимое <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsJson">Содержимое <see cref="ItemsFileName"/>.</param>
        /// <param name="source">Метка источника для сообщений об ошибках.</param>
        /// <param name="skillTreeJson">Содержимое <see cref="SkillTreeFileName"/>; пустое дерево по умолчанию.</param>
        /// <param name="resourcesJson">Содержимое <see cref="ResourcesFileName"/>; пустой список по умолчанию.</param>
        /// <exception cref="ConfigValidationException">Если JSON невалиден.</exception>
        public static SimulationConfig LoadFromJson(
            string actorsJson,
            string abilitiesJson,
            string zonesJson,
            string itemsJson,
            string source = "config",
            string skillTreeJson = EmptySkillTreeJson,
            string resourcesJson = EmptyResourcesJson)
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

            var actorDtos = ParseArray(actorsJson, "actors", source);
            var abilityDtos = ParseArray(abilitiesJson, "abilities", source);
            var zoneDtos = ParseArray(zonesJson, "zones", source);
            var itemDtos = ParseArray(itemsJson, "items", source);
            var resourceDtos = ParseArray(resourcesJson, "resources", source);
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

            try
            {
                return new SimulationConfig(actors, abilities, zones, items, skillTree, nodes);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(source + ": " + exception.Message, exception);
            }
        }

        /// <summary>
        /// Считает SHA-256 от содержимого четырёх файлов конфигурации: одинаковый хеш означает,
        /// что сервер и клиент читают один и тот же файл (M9, Auto-Gate).
        /// </summary>
        /// <param name="directory">
        /// Каталог с <see cref="ActorsFileName"/>, <see cref="AbilitiesFileName"/>,
        /// <see cref="ZonesFileName"/>, <see cref="ItemsFileName"/>, <see cref="SkillTreeFileName"/>
        /// и <see cref="ResourcesFileName"/>.
        /// </param>
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
                ReadFile(Path.Combine(directory, AbilitiesFileName)),
                ReadFile(Path.Combine(directory, ZonesFileName)),
                ReadFile(Path.Combine(directory, ItemsFileName)),
                ReadFile(Path.Combine(directory, SkillTreeFileName)),
                ReadFile(Path.Combine(directory, ResourcesFileName)));
        }

        /// <summary>Считает хеш от строк JSON всех шести файлов конфигурации.</summary>
        /// <param name="actorsJson">Содержимое <see cref="ActorsFileName"/>.</param>
        /// <param name="abilitiesJson">Содержимое <see cref="AbilitiesFileName"/>.</param>
        /// <param name="zonesJson">Содержимое <see cref="ZonesFileName"/>.</param>
        /// <param name="itemsJson">Содержимое <see cref="ItemsFileName"/>.</param>
        /// <param name="skillTreeJson">Содержимое <see cref="SkillTreeFileName"/>.</param>
        /// <param name="resourcesJson">Содержимое <see cref="ResourcesFileName"/>.</param>
        /// <returns>Хеш в нижнем hex-регистре.</returns>
        /// <exception cref="ArgumentNullException">Если строка равна <c>null</c>.</exception>
        public static string ComputeHashFromJson(
            string actorsJson,
            string abilitiesJson,
            string zonesJson,
            string itemsJson,
            string skillTreeJson,
            string resourcesJson)
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

            using var sha = SHA256.Create();
            using var stream = new MemoryStream();
            AppendSection(stream, Encoding.UTF8.GetBytes(actorsJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(abilitiesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(zonesJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(itemsJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(skillTreeJson));
            AppendSection(stream, Encoding.UTF8.GetBytes(resourcesJson));

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
                    fameOnKill);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (id=" + id + "): " + exception.Message, exception);
            }
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
                    resourceTierMax);
            }
            catch (ArgumentException exception)
            {
                throw new ConfigValidationException(label + " (key=" + key + "): " + exception.Message, exception);
            }
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
            if (!dto.TryGetValue(field, out var value) || value == null)
            {
                return new List<Dictionary<string, object?>>();
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
