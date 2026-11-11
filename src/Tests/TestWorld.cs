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

        /// <summary>Каталог предметов по умолчанию: сырьё и четыре слота снаряжения.</summary>
        public static readonly IReadOnlyList<ItemDefinition> DefaultItems = new[]
        {
            new ItemDefinition(1, "wood", ItemKind.Material, 1, 0),
            new ItemDefinition(2, "ore", ItemKind.Material, 1, 0),
            new ItemDefinition(10, "sword", ItemKind.Equipment, 1, 100, EquipmentSlot.MainHand),
            new ItemDefinition(11, "chest", ItemKind.Equipment, 1, 100, EquipmentSlot.Chest),
            new ItemDefinition(12, "legs", ItemKind.Equipment, 1, 80, EquipmentSlot.Legs),
            new ItemDefinition(13, "feet", ItemKind.Equipment, 1, 60, EquipmentSlot.Feet),
        };

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
            ActorKind kind = ActorKind.Player)
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
                starterEquipment);

            return new SimulationConfig(
                new[] { actor },
                new[] { ability },
                zones ?? DefaultZones,
                items ?? DefaultItems);
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
                DefaultItems);
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
