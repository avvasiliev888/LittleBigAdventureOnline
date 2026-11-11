using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Последствия смерти по зонам (ADR-013, product-vision §6.1): в жёлтой зоне игрок теряет только
    /// прочность, в красной — снаряжение и инвентарь; при респавне выдаётся страховочный набор,
    /// если терять больше нечего.
    /// </summary>
    public sealed class DeathRulesTests
    {
        [Fact]
        public void YellowDeath_KeepsItems_AndWearsDurability()
        {
            var config = ZoneFixture.PlayerConfig(damage: 100, withLoadout: true);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 1f));

            var victim = simulation.State.Entities[1];
            var yellow = config.Zones.GetZone(victim.Position);
            var expectedDurability = victim.Equipment.Get(EquipmentSlot.MainHand)!.MaxDurability
                - (int)System.Math.Round(
                    victim.Equipment.Get(EquipmentSlot.MainHand)!.MaxDurability * (double)yellow.DurabilityLossFraction,
                    System.MidpointRounding.AwayFromZero);

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.True(victim.IsDead);
            Assert.Equal(5, victim.Inventory.CountOf(1));
            Assert.NotNull(victim.Equipment.Get(EquipmentSlot.MainHand));
            Assert.Equal(expectedDurability, victim.Equipment.Get(EquipmentSlot.MainHand)!.Durability);
            Assert.True(victim.RespawnAtTick > 0);
        }

        [Fact]
        public void RedDeath_ClearsInventoryAndEquipment()
        {
            var config = ZoneFixture.PlayerConfig(damage: 100, withLoadout: true);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.RedX),
                TestWorld.Entity(2, config, x: ZoneFixture.RedX + 1f));

            var victim = simulation.State.Entities[1];

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.True(victim.IsDead);
            Assert.True(victim.Inventory.IsEmpty);
            Assert.True(victim.Equipment.IsEmpty);
        }

        [Fact]
        public void YellowDeath_RespawnKeepsSurvivingItems_WithoutDuplicatingStarterKit()
        {
            var config = ZoneFixture.PlayerConfig(damage: 100, withLoadout: true);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 1f));

            var victim = simulation.State.Entities[1];
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            var wornDurability = victim.Equipment.Get(EquipmentSlot.MainHand)!.Durability;

            TestWorld.TickMany(simulation, (int)(victim.Actor.RespawnSeconds * SimulationConstants.TickRate));

            Assert.False(victim.IsDead);
            Assert.Equal(5, victim.Inventory.CountOf(1));
            Assert.Equal(wornDurability, victim.Equipment.Get(EquipmentSlot.MainHand)!.Durability);
        }

        [Fact]
        public void RedDeath_RespawnGrantsStarterKit()
        {
            var config = ZoneFixture.PlayerConfig(damage: 100, withLoadout: true);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.RedX),
                TestWorld.Entity(2, config, x: ZoneFixture.RedX + 1f));

            var victim = simulation.State.Entities[1];
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            Assert.True(victim.Inventory.IsEmpty);

            TestWorld.TickMany(simulation, (int)(victim.Actor.RespawnSeconds * SimulationConstants.TickRate));

            Assert.False(victim.IsDead);
            Assert.Equal(100, victim.Hp);
            Assert.Equal(5, victim.Inventory.CountOf(1));
            Assert.NotNull(victim.Equipment.Get(EquipmentSlot.MainHand));
            Assert.Equal(100, victim.Equipment.Get(EquipmentSlot.MainHand)!.Durability);
            Assert.Equal(victim.SpawnPosition, victim.Position);
        }

        [Fact]
        public void MobDeath_IsNotSubjectToPlayerLootRules()
        {
            var config = ZoneFixture.PlayerAndMobConfig(damage: 100);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.RedX),
                TestWorld.Entity(100, config, x: ZoneFixture.RedX + 1f, actorId: 100));

            var mob = simulation.State.Entities[1];

            TestWorld.Tick(simulation, TestWorld.Attack(1, 100));

            // Моб умирает и респавнится, но интерфейс игрока (инвентарь/снаряжение) у него пуст:
            // правила лута к нему не применяются и исключений не вызывают.
            Assert.True(mob.IsDead);
            Assert.True(mob.Inventory.IsEmpty);
            Assert.True(mob.Equipment.IsEmpty);

            TestWorld.TickMany(simulation, (int)(mob.Actor.RespawnSeconds * SimulationConstants.TickRate));

            Assert.False(mob.IsDead);
            Assert.True(mob.Inventory.IsEmpty);
        }

        [Fact]
        public void Death_ClearsHostilityFlag()
        {
            var config = ZoneFixture.PlayerConfig(damage: 100, withLoadout: true);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: ZoneFixture.YellowX),
                TestWorld.Entity(2, config, x: ZoneFixture.YellowX + 1f));

            var attacker = simulation.State.Entities[0];
            var victim = simulation.State.Entities[1];

            TestWorld.TickMany(simulation, 1);
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.True(attacker.IsHostile(simulation.State.Tick));
            Assert.Equal(0u, victim.HostileUntilTick);
        }
    }
}
