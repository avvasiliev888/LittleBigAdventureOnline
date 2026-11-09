using System;
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

        public static SimulationConfig Config(
            int damage = 25,
            float range = 3f,
            float cooldownSeconds = 2f,
            int resourceCost = 10,
            int maxHp = 100,
            float moveSpeed = 4f,
            float respawnSeconds = 5f,
            int maxResource = 100)
        {
            var ability = new AbilityConfig(PlayerAbilityId, damage, range, cooldownSeconds, resourceCost);
            var actor = new ActorConfig(PlayerActorId, maxHp, moveSpeed, maxResource, respawnSeconds, PlayerAbilityId);

            return new SimulationConfig(new[] { actor }, new[] { ability });
        }

        public static EntityState Entity(
            uint id,
            SimulationConfig config,
            float x = 0f,
            float z = 0f,
            uint actorId = PlayerActorId)
        {
            return new EntityState(id, config.GetActor(actorId), new Vector3(x, 0f, z));
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
