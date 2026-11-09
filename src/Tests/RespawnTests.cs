using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Смерть и респавн (M3): HP ≤ 0 → мёртв, через N секунд из конфига — полное HP в точке появления.</summary>
    public sealed class RespawnTests
    {
        [Fact]
        public void Death_AtZeroHp_IsMarkedDead()
        {
            var config = TestWorld.Config(damage: 100);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.True(simulation.State.Entities[1].IsDead);
        }

        [Fact]
        public void Respawn_HappensExactlyAfterConfiguredSeconds()
        {
            var config = TestWorld.Config(damage: 100, respawnSeconds: 5f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            TestWorld.TickMany(simulation, 98);

            Assert.True(simulation.State.Entities[1].IsDead);

            TestWorld.TickMany(simulation, 1);

            Assert.False(simulation.State.Entities[1].IsDead);
        }

        [Fact]
        public void Respawn_RestoresFullHpAndResource()
        {
            var config = TestWorld.Config(damage: 100, respawnSeconds: 5f, maxResource: 100);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            TestWorld.TickMany(simulation, 100);

            var respawned = simulation.State.Entities[1];
            Assert.Equal(100, respawned.Hp);
            Assert.Equal(100, respawned.Resource);
            Assert.Equal(0u, respawned.RespawnAtTick);
            Assert.Equal(0u, respawned.CooldownUntilTick);
        }

        [Fact]
        public void Respawn_ReturnsEntityToSpawnPosition()
        {
            var config = TestWorld.Config(damage: 100, respawnSeconds: 1f);
            var target = TestWorld.Entity(2, config, x: 1f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config), target);

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            simulation.State.Entities[1].Position = new Vector3(9f, 0f, 9f);
            TestWorld.TickMany(simulation, 20);

            SnapshotAssert.Equal(new Vector3(1f, 0f, 0f), simulation.State.Entities[1].Position);
        }

        [Fact]
        public void Respawn_IsConfigurable_ShorterValueRespawnsEarlier()
        {
            var fastConfig = TestWorld.Config(damage: 100, respawnSeconds: 1f);
            var slowConfig = TestWorld.Config(damage: 100, respawnSeconds: 3f);

            var fast = TestWorld.Simulation(fastConfig, TestWorld.Entity(1, fastConfig), TestWorld.Entity(2, fastConfig, x: 1f));
            var slow = TestWorld.Simulation(slowConfig, TestWorld.Entity(1, slowConfig), TestWorld.Entity(2, slowConfig, x: 1f));

            TestWorld.Tick(fast, TestWorld.Attack(1, 2));
            TestWorld.Tick(slow, TestWorld.Attack(1, 2));

            TestWorld.TickMany(fast, 20);
            TestWorld.TickMany(slow, 20);

            Assert.False(fast.State.Entities[1].IsDead);
            Assert.True(slow.State.Entities[1].IsDead);
        }
    }
}
