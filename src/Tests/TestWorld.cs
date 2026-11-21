using System;
using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>Сборка конфигов и миров для тестов M3.</summary>
    internal static class TestWorld
    {
        public const uint PlayerActorId = 1;
        public const uint PlayerAbilityId = 1;
        public const uint DummyActorId = 2;
        public const uint SwordItemId = 10;
        public const uint BowItemId = 14;

        /// <summary>Каталог предметов по умолчанию: сырьё, оружие двух архетипов и доспехи.</summary>
        public static readonly IReadOnlyList<ItemDefinition> DefaultItems = new[]
        {
            new ItemDefinition(1, "wood", ItemKind.Material, 1, 0),
            new ItemDefinition(2, "ore", ItemKind.Material, 1, 0),
            new ItemDefinition(SwordItemId, "sword", ItemKind.Equipment, 1, 100, EquipmentSlot.MainHand, "warrior", 4),
            new ItemDefinition(11, "chest", ItemKind.Equipment, 1, 100, EquipmentSlot.Chest, maxHpBonus: 20),
            new ItemDefinition(12, "legs", ItemKind.Equipment, 1, 80, EquipmentSlot.Legs, maxHpBonus: 10),
            new ItemDefinition(13, "feet", ItemKind.Equipment, 1, 60, EquipmentSlot.Feet, moveSpeedBonus: 0.05f),
            new ItemDefinition(BowItemId, "bow", ItemKind.Equipment, 1, 90, EquipmentSlot.MainHand, "archer", 3),
        };

        /// <summary>
        /// Дерево навыков по умолчанию: две ветки с базовой способностью 1, пассивом на урон
        /// и пассивом на здоровье. Тесты прогрессии задают своё дерево и свои способности.
        /// </summary>
        public static readonly SkillTree DefaultSkillTree = new SkillTree(new[]
        {
            new SkillArchetype("warrior", "Воин", new[]
            {
                new SkillNode(1, "Рассечение", 0, SkillNodeKind.Active, PlayerAbilityId),
                new SkillNode(2, "Сила удара", 10, SkillNodeKind.Passive, damageBonus: 0.10f),
                new SkillNode(3, "Крепкий доспех", 20, SkillNodeKind.Passive, maxHpBonus: 20),
            }),
            new SkillArchetype("archer", "Лучник", new[]
            {
                new SkillNode(4, "Выстрел", 0, SkillNodeKind.Active, PlayerAbilityId),
                new SkillNode(5, "Острый глаз", 10, SkillNodeKind.Passive, damageBonus: 0.05f),
            }),
        });

        /// <summary>Дерево по умолчанию в виде JSON — для тестов загрузчика.</summary>
        public const string SkillTreeJson = @"{
  ""archetypes"": [
    { ""key"": ""warrior"", ""name"": ""Воин"", ""nodes"": [
      { ""id"": 1, ""name"": ""Рассечение"", ""fameThreshold"": 0, ""kind"": ""active"", ""abilityId"": 1 },
      { ""id"": 2, ""name"": ""Сила удара"", ""fameThreshold"": 10, ""kind"": ""passive"", ""damageBonus"": 0.10 },
      { ""id"": 3, ""name"": ""Крепкий доспех"", ""fameThreshold"": 20, ""kind"": ""passive"", ""maxHpBonus"": 20 }
    ] }
  ]
}";

        /// <summary>Пустой список жил в виде JSON.</summary>
        public const string ResourcesJson = "[]";

        /// <summary>Одна жила внутри тестового мира в виде JSON.</summary>
        public const string WoodResourceJson = @"[
  { ""id"": 7, ""key"": ""ash_tree"", ""itemId"": 1, ""x"": 2.0, ""z"": 0.0, ""amount"": 2, ""gatherSeconds"": 1.5, ""famePerGather"": 3, ""range"": 3.0 }
]";

        /// <summary>
        /// Зона по умолчанию для старых тестов: один большой мир с включённым PvP, чтобы движение,
        /// способности и бой двух игроков работали как раньше. Тесты зон задают свои зоны явно.
        /// </summary>
        public static readonly IReadOnlyList<ZoneConfig> DefaultZones = new[]
        {
            new ZoneConfig(1, "test", ZoneKind.Yellow, -1000f, -1000f, 1000f, 1000f, true, false, 0.1f, 60f, 1, 4),
        };

        /// <summary>Тот же мир в виде JSON — для тестов загрузчика и клиентского контента.</summary>
        public const string ZonesJson = @"[
  { ""id"": 1, ""key"": ""test"", ""kind"": ""yellow"", ""minX"": -1000.0, ""minZ"": -1000.0, ""maxX"": 1000.0, ""maxZ"": 1000.0, ""pvpEnabled"": true, ""fullLootOnDeath"": false, ""durabilityLossFraction"": 0.1, ""hostileFlagSeconds"": 60.0, ""resourceTierMin"": 1, ""resourceTierMax"": 4 }
]";

        /// <summary>Каталог предметов по умолчанию в виде JSON — для тестов загрузчика.</summary>
        public const string ItemsJson = @"[
  { ""id"": 1, ""key"": ""wood"", ""kind"": ""material"", ""tier"": 1, ""maxDurability"": 0 }
]";

        /// <summary>Загружает конфиг из строк JSON с минимальными актором, способностью и зонами.</summary>
        public static SimulationConfig LoadFromJson(
            string skillTreeJson = Shared.ConfigLoader.EmptySkillTreeJson,
            string resourcesJson = "[]",
            string recipesJson = Shared.ConfigLoader.EmptyRecipesJson,
            string tradersJson = Shared.ConfigLoader.EmptyTradersJson,
            string? itemsJson = null,
            string mobsJson = Shared.ConfigLoader.EmptyMobsJson)
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";
            const string abilities = @"[
  { ""id"": 1, ""damage"": 25, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

            return Shared.ConfigLoader.LoadFromJson(
                actors,
                abilities,
                ZonesJson,
                itemsJson ?? ItemsJson,
                "test",
                skillTreeJson,
                resourcesJson,
                recipesJson,
                tradersJson,
                mobsJson);
        }

        public static SimulationConfig Config(
            int damage = 25,
            float range = 3f,
            float cooldownSeconds = 2f,
            int resourceCost = 10,
            int maxHp = 100,
            float moveSpeed = 4f,
            float respawnSeconds = 5f,
            int maxResource = 100,
            float resourceRegenPerSecond = 0f,
            IReadOnlyList<ZoneConfig>? zones = null,
            IReadOnlyList<ItemDefinition>? items = null,
            IReadOnlyList<LoadoutItem>? starterItems = null,
            IReadOnlyList<LoadoutEquipment>? starterEquipment = null,
            ActorKind kind = ActorKind.Player,
            SkillTree? skillTree = null,
            IReadOnlyList<ResourceNodeConfig>? resourceNodes = null,
            int fameOnKill = 0,
            IReadOnlyList<AbilityConfig>? abilities = null,
            IReadOnlyList<Recipe>? recipes = null,
            IReadOnlyList<NpcTrader>? traders = null,
            int inventorySlots = Inventory.DefaultCapacity,
            int startingSilver = 0,
            IReadOnlyList<MobDefinition>? mobs = null,
            IReadOnlyList<WorldCollider>? worldColliders = null)
        {
            var ability = new AbilityConfig(PlayerAbilityId, damage, range, cooldownSeconds, resourceCost);
            var actor = new ActorConfig(
                PlayerActorId,
                maxHp,
                moveSpeed,
                maxResource,
                respawnSeconds,
                PlayerAbilityId,
                resourceRegenPerSecond,
                kind,
                starterItems,
                starterEquipment,
                fameOnKill,
                inventorySlots,
                startingSilver);

            var abilityList = abilities ?? new[] { ability };
            var tree = skillTree ?? DefaultSkillTree;
            var itemList = items ?? DefaultItems;

            return new SimulationConfig(
                new[] { actor },
                abilityList,
                zones ?? DefaultZones,
                itemList,
                tree,
                resourceNodes,
                recipes,
                traders,
                mobs,
                worldColliders);
        }

        /// <summary>Конфиг с оружием в руках: меч воина или лук лучника (для смены архетипа).</summary>
        public static SimulationConfig LoadoutConfig(
            IReadOnlyList<AbilityConfig> abilities,
            SkillTree skillTree,
            uint starterWeaponId,
            IReadOnlyList<ResourceNodeConfig>? resourceNodes = null,
            int fameOnKill = 0)
        {
            return Config(
                abilities: abilities,
                skillTree: skillTree,
                starterEquipment: new[] { new LoadoutEquipment(starterWeaponId) },
                resourceNodes: resourceNodes,
                fameOnKill: fameOnKill);
        }

        /// <summary>Зона для тестов зон; границы задаются явно.</summary>
        public static ZoneConfig Zone(
            uint id,
            ZoneKind kind,
            float minX,
            float maxX,
            bool pvpEnabled,
            bool fullLootOnDeath = false,
            float durabilityLossFraction = 0.1f,
            float hostileFlagSeconds = 60f,
            float minZ = -1000f,
            float maxZ = 1000f)
        {
            return new ZoneConfig(
                id,
                kind.ToString().ToLowerInvariant(),
                kind,
                minX,
                minZ,
                maxX,
                maxZ,
                pvpEnabled,
                fullLootOnDeath,
                durabilityLossFraction,
                hostileFlagSeconds,
                1,
                4);
        }

        public static EntityState Entity(
            uint id,
            SimulationConfig config,
            float x = 0f,
            float z = 0f,
            uint actorId = PlayerActorId)
        {
            var entity = new EntityState(id, config.GetActor(actorId), new Vector3(x, 0f, z));
            entity.GrantStarterLoadout(config);
            return entity;
        }

        /// <summary>Player actor 1 plus training dummy actor 2 for server/dummy tests (M10.7).</summary>
        public static SimulationConfig ServerConfig(float dummyMoveSpeed = 1f, float range = 3f)
        {
            var ability = new AbilityConfig(PlayerAbilityId, 25, range, 2f, 10);
            var player = new ActorConfig(PlayerActorId, 100, 4f, 100, 5f, PlayerAbilityId);
            var dummy = new ActorConfig(DummyActorId, 100, dummyMoveSpeed, 100, 5f, PlayerAbilityId, 0f, ActorKind.Mob);
            return new SimulationConfig(
                new[] { player, dummy },
                new[] { ability },
                DefaultZones,
                DefaultItems,
                DefaultSkillTree);
        }

        public static CoreSimulation Simulation(SimulationConfig config, params EntityState[] entities)
        {
            return new CoreSimulation(new SimulationState(0, entities), config);
        }

        public static CommandEnvelope Move(uint entityId, float x, float z, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new MoveCommand(new Vector2(x, z), clientTick, sequence));
        }

        public static CommandEnvelope Attack(uint entityId, uint targetId, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new AbilityCommand(targetId, clientTick, sequence));
        }

        public static CommandEnvelope Equip(uint entityId, uint itemId, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new EquipCommand(itemId, clientTick, sequence));
        }

        public static CommandEnvelope Gather(uint entityId, uint nodeId, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new GatherCommand(nodeId, clientTick, sequence));
        }

        public static CommandEnvelope Craft(uint entityId, uint recipeId, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new CraftCommand(recipeId, clientTick, sequence));
        }

        public static CommandEnvelope Buy(
            uint entityId,
            uint traderId,
            uint itemId,
            int quantity,
            uint clientTick = 0,
            uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new BuyCommand(traderId, itemId, quantity, clientTick, sequence));
        }

        public static CommandEnvelope Sell(
            uint entityId,
            uint traderId,
            uint itemId,
            int quantity,
            uint clientTick = 0,
            uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new SellCommand(traderId, itemId, quantity, clientTick, sequence));
        }

        public static CommandEnvelope Loot(uint entityId, uint corpseId, uint clientTick = 0, uint sequence = 0)
        {
            return new CommandEnvelope(entityId, new LootCommand(corpseId, clientTick, sequence));
        }

        public static Recipe Recipe(
            uint id,
            uint outputItemId,
            int famePerCraft,
            params RecipeMaterial[] materials)
        {
            return new Recipe(id, "recipe_" + id, outputItemId, 1, materials, famePerCraft);
        }

        public static RecipeMaterial Material(uint itemId, int quantity) => new RecipeMaterial(itemId, quantity);

        public static NpcTrader Trader(
            uint id,
            string zoneKey,
            float x,
            float z,
            float range,
            params TraderPrice[] prices)
        {
            return new NpcTrader(id, "trader_" + id, zoneKey, x, z, range, prices);
        }

        public static TraderPrice Price(uint itemId, int buyPrice, int sellPrice)
        {
            return new TraderPrice(itemId, buyPrice, sellPrice);
        }

        public static MobDefinition Mob(
            uint id = 1,
            uint actorId = 101,
            string zoneKey = "test",
            int maxHp = 60,
            int damage = 5,
            float moveSpeed = 2f,
            float attackRange = 1.5f,
            float attackSeconds = 1f,
            float aggroRange = 5f,
            float leashRange = 12f,
            float respawnSeconds = 5f,
            int fameOnKill = 4,
            int silverMin = 0,
            int silverMax = 0,
            float lootRange = 2f,
            float corpseSeconds = 10f,
            float patrolPauseSeconds = 0f,
            Vector2[]? patrol = null,
            Vector2[]? spawns = null,
            MobLootEntry[]? loot = null,
            uint abilityId = PlayerAbilityId,
            float keepDistance = 0f,
            string weaponType = MobDefinition.DefaultWeaponType)
        {
            return new MobDefinition(
                id,
                "mob_" + id,
                "Mob " + id,
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
                patrol ?? Array.Empty<Vector2>(),
                spawns ?? new[] { new Vector2(0f, 0f) },
                loot,
                MobDefinition.DefaultCollisionRadius,
                keepDistance,
                weaponType);
        }

        public static MobLootEntry MobLoot(uint itemId, int minQuantity, int maxQuantity, float chance = 1f)
        {
            return new MobLootEntry(itemId, minQuantity, maxQuantity, chance);
        }

        public static Vector2 Point(float x, float z) => new Vector2(x, z);

        public static ItemStack Stack(uint itemId, int quantity) => new ItemStack(itemId, quantity);

        public static void Tick(CoreSimulation simulation, params CommandEnvelope[] commands)
        {
            simulation.Tick(SimulationConstants.FixedDeltaTime, commands);
        }

        public static void TickMany(CoreSimulation simulation, int count)
        {
            for (var i = 0; i < count; i++)
            {
                simulation.Tick(SimulationConstants.FixedDeltaTime, Array.Empty<CommandEnvelope>());
            }
        }
    }
}
