using System.Collections.Generic;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Правила PvP по зонам (ADR-013): в синей урон между игроками невозможен, в жёлтой и красной
    /// удар наносит урон и поднимает флаг враждебности; мобы бьются в любой зоне.
    /// </summary>
    public sealed class ZoneCombatTests
    {
        [Fact]
        public void BlueZone_PlayerDamage_IsImpossible_AndAbilityIsNotActivated()
        {
            var config = ZoneFixture.PlayerConfig();
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.BlueX),
                TestWorld.Entity(2, config, x: ZoneFixture.BlueX + 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            var attacker = simulation.State.Entities[0];
            var victim = simulation.State.Entities[1];

            Assert.Equal(100, victim.Hp);
            Assert.Equal(100, attacker.Resource);
            Assert.Equal(0u, attacker.CooldownUntilTick);
            Assert.Equal(0u, attacker.HostileUntilTick);
        }

        [Fact]
        public void BlueZone_MobCombat_IsAllowed()
        {
            var config = ZoneFixture.PlayerAndMobConfig();
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.BlueX),
                TestWorld.Entity(100, config, x: ZoneFixture.BlueX + 1f, actorId: 100));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 100));

            Assert.Equal(75, simulation.State.Entities[1].Hp);
            Assert.Equal(90, simulation.State.Entities[0].Resource);
        }

        [Fact]
        public void YellowZone_PlayerDamage_AppliesAndFlagsAttacker()
        {
            var config = ZoneFixture.PlayerConfig(damage: 25);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            var attacker = simulation.State.Entities[0];
            var victim = simulation.State.Entities[1];
            var yellow = config.Zones.GetZone(attacker.Position);

            Assert.Equal(75, victim.Hp);
            Assert.True(attacker.IsHostile(simulation.State.Tick));
            Assert.Equal((uint)(yellow.HostileFlagSeconds * SimulationConstants.TickRate), attacker.HostileUntilTick);
            Assert.False(victim.IsHostile(simulation.State.Tick));
        }

        [Fact]
        public void RedZone_PlayerDamage_AppliesAndFlagsAttacker()
        {
            var config = ZoneFixture.PlayerConfig(damage: 10);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.RedX),
                TestWorld.Entity(2, config, x: ZoneFixture.RedX + 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            var attacker = simulation.State.Entities[0];
            var red = config.Zones.GetZone(attacker.Position);

            Assert.Equal(90, simulation.State.Entities[1].Hp);
            Assert.Equal((uint)(red.HostileFlagSeconds * SimulationConstants.TickRate), attacker.HostileUntilTick);
        }

        [Fact]
        public void HostilityFlag_ExpiresAfterZoneDuration()
        {
            var config = ZoneFixture.PlayerConfig(damage: 10);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            var attacker = simulation.State.Entities[0];
            var flagTicks = (uint)(config.Zones.GetZone(attacker.Position).HostileFlagSeconds * SimulationConstants.TickRate);

            Assert.True(attacker.IsHostile(simulation.State.Tick));

            TestWorld.TickMany(simulation, (int)flagTicks - 2);
            Assert.True(attacker.IsHostile(simulation.State.Tick));

            TestWorld.TickMany(simulation, 2);
            Assert.False(attacker.IsHostile(simulation.State.Tick));
        }

        [Fact]
        public void SafeVictim_CannotBeHitFromAnotherZone()
        {
            // Безопасность определяется зоной жертвы: игрок, стоящий в синей зоне, неуязвим,
            // даже если атакующий находится в жёлтой в пределах дальности удара.
            var config = ZoneFixture.PlayerConfig();
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: 0.5f),
                TestWorld.Entity(2, config, x: -0.5f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(100, simulation.State.Entities[1].Hp);
            Assert.Equal(100, simulation.State.Entities[0].Resource);
        }

        [Fact]
        public void Movement_IsClampedToWorldBounds()
        {
            var config = ZoneFixture.PlayerConfig();
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config, x: 19f));

            for (var i = 0; i < 20; i++)
            {
                TestWorld.Tick(simulation, TestWorld.Move(1, 1f, 0f));
            }

            Assert.Equal(config.Zones.MaxX, simulation.State.Entities[0].Position.X);
        }

        [Fact]
        public void AttackingMob_DoesNotSetHostilityFlag()
        {
            var config = ZoneFixture.PlayerAndMobConfig();
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(100, config, x: ZoneFixture.YellowX + 1f, actorId: 100));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 100));

            Assert.Equal(0u, simulation.State.Entities[0].HostileUntilTick);
        }

        [Fact]
        public void OutOfRangePlayerAttack_InPvpZone_ActivatesButMisses()
        {
            var config = ZoneFixture.PlayerConfig(damage: 25, cooldownSeconds: 2f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 5f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(100, simulation.State.Entities[1].Hp);
            Assert.Equal(90, simulation.State.Entities[0].Resource);
            Assert.True(simulation.State.Entities[0].CooldownUntilTick > 0);
        }
    }
}
