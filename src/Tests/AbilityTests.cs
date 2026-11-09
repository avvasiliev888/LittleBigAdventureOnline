using System;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Способность (M3): дистанция, кулдаун, ресурс, урон берутся из конфига.
    /// </summary>
    public sealed class AbilityTests
    {
        [Fact]
        public void Ability_InRange_DealsConfiguredDamage()
        {
            var config = TestWorld.Config(damage: 25, range: 3f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(75, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_OutOfRange_DealsNoDamage()
        {
            var config = TestWorld.Config(damage: 25, range: 3f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 3.5f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(100, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_OnCooldown_IsBlocked()
        {
            var config = TestWorld.Config(damage: 25, cooldownSeconds: 2f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, sequence: 0));
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, clientTick: 1, sequence: 0));

            // Второе применение заблокировано кулдауном: урон нанесён один раз.
            Assert.Equal(75, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_AfterCooldown_AppliesAgain()
        {
            var config = TestWorld.Config(damage: 25, cooldownSeconds: 1f, maxResource: 1000);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, clientTick: 0));
            TestWorld.TickMany(simulation, 20);
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, clientTick: 21));

            Assert.Equal(50, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_WithoutEnoughResource_DealsNoDamage()
        {
            var config = TestWorld.Config(damage: 25, resourceCost: 10, maxResource: 5);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(100, simulation.State.Entities[1].Hp);
            Assert.Equal(5, simulation.State.Entities[0].Resource);
        }

        [Fact]
        public void Ability_SpendsResource_AndSetsCooldown()
        {
            var config = TestWorld.Config(resourceCost: 30, cooldownSeconds: 2f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            var caster = simulation.State.Entities[0];
            Assert.Equal(70, caster.Resource);
            Assert.Equal(40u, caster.CooldownUntilTick);
        }

        [Fact]
        public void Ability_WithSubTickCooldown_UsesAtLeastOneTick()
        {
            var config = TestWorld.Config(cooldownSeconds: 0.01f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(1u, simulation.State.Entities[0].CooldownUntilTick);
        }

        [Fact]
        public void Ability_KillsTarget_OnLethalDamage()
        {
            var config = TestWorld.Config(damage: 100, respawnSeconds: 5f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            var target = simulation.State.Entities[1];
            Assert.Equal(0, target.Hp);
            Assert.True(target.IsDead);
            Assert.Equal(100u, target.RespawnAtTick);
        }

        [Fact]
        public void Ability_FromDeadCaster_IsIgnored()
        {
            var config = TestWorld.Config(damage: 25);
            var caster = TestWorld.Entity(1, config);
            caster.Hp = 0;
            var simulation = TestWorld.Simulation(config, caster, TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(100, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_AgainstDeadTarget_IsIgnored()
        {
            var config = TestWorld.Config(damage: 25);
            var target = TestWorld.Entity(2, config, x: 1f);
            target.Hp = 0;
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config), target);

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(0, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void Ability_ForUnknownTarget_IsIgnored()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 999));

            Assert.Equal(1u, simulation.State.Tick);
        }

        [Theory]
        [InlineData(10, 90)]
        [InlineData(25, 75)]
        [InlineData(50, 50)]
        public void Ability_DamageFromConfig_ChangesResult(int damage, int expectedHp)
        {
            var config = TestWorld.Config(damage: damage);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(expectedHp, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void AbilityCommand_WithZeroTarget_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityCommand(0, 0, 0));
        }
    }
}
