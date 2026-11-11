using System;
using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Сквозной сценарий этапа полировки: вход в красную зону, сбор ресурса, бой с мобом,
    /// лут, крафт и смерть от другого игрока с полной потерей вещей.
    /// </summary>
    /// <remarks>
    /// Тест использует настоящие команды и серверную симуляцию, а не обходит правила напрямую.
    /// Мир компактный, чтобы путь через ворота занимал секунды, но правила зон, мобов, лута,
    /// крафта и смерти — те же, что в боевом конфиге.
    /// </remarks>
    public sealed class FullCycleIntegrationTests
    {
        [Fact]
        public void RedZoneCycle_GatherFightLootCraftThenDieAndLoseItems()
        {
            var blue = new ZoneConfig(
                1, "blue", ZoneKind.Blue, 0f, -20f, 20f, 20f,
                pvpEnabled: true, fullLootOnDeath: false, durabilityLossFraction: 0.1f,
                hostileFlagSeconds: 60f, resourceTierMin: 1, resourceTierMax: 4,
                gates: new[] { new ZoneGate(2, 20f, 0f, 10f) });
            var red = new ZoneConfig(
                2, "red", ZoneKind.Red, 20f, -20f, 40f, 20f,
                pvpEnabled: true, fullLootOnDeath: true, durabilityLossFraction: 0.1f,
                hostileFlagSeconds: 60f, resourceTierMin: 1, resourceTierMax: 4,
                gates: new[] { new ZoneGate(1, 20f, 0f, 10f) });

            var config = TestWorld.Config(
                zones: new[] { blue, red },
                abilities: new[] { new AbilityConfig(1, 120, 3f, 1f, 10) },
                maxHp: 100,
                moveSpeed: 20f,
                starterItems: Array.Empty<LoadoutItem>(),
                starterEquipment: Array.Empty<LoadoutEquipment>(),
                resourceNodes: new[]
                {
                    new ResourceNodeConfig(1, "red_wood", 1, 30f, 0f, 2, 0.1f, 1, 2f),
                },
                recipes: new[]
                {
                    TestWorld.Recipe(1, TestWorld.SwordItemId, 0, TestWorld.Material(1, 2)),
                },
                mobs: new[]
                {
                    TestWorld.Mob(
                        id: 1,
                        actorId: 101,
                        zoneKey: "red",
                        maxHp: 50,
                        damage: 2,
                        moveSpeed: 0.5f,
                        attackRange: 2f,
                        attackSeconds: 1f,
                        aggroRange: 2.5f,
                        leashRange: 10f,
                        spawns: new[] { TestWorld.Point(31f, 0f) },
                        loot: new[] { TestWorld.MobLoot(2, 1, 1) }),
                });

            var victim = TestWorld.Entity(1, config, 10f, 0f);
            var killer = TestWorld.Entity(2, config, 35f, 0f);
            var simulation = TestWorld.Simulation(config, victim, killer);
            Assert.Equal(1, simulation.SpawnMobs());
            var mob = simulation.State.Entities.Single(entity => entity.Mob != null);

            // 1. Вход в красную зону через ворота: обычное движение, без телепорта.
            for (var tick = 0; tick < 60; tick++)
            {
                TestWorld.Tick(simulation, TestWorld.Move(victim.Id, 1f, 0f, (uint)tick, (uint)tick));
            }

            Assert.Equal(ZoneKind.Red, config.Zones.GetZone(victim.Position).Kind);
            Assert.True(victim.Position.X > 20f);

            // 2. Сбор ресурса в красной зоне.
            TestWorld.Tick(simulation, TestWorld.Gather(victim.Id, 1));
            Assert.Equal(2, victim.Inventory.CountOf(1));

            // 3. Моб атакует, игрок отвечает и убивает его.
            Assert.True(mob.AiTargetId == victim.Id || mob.AiState == MobAiState.Chase);
            Assert.True(victim.Hp < 100, "моб должен был ударить жертву по пути к ресурсу");
            TestWorld.Tick(simulation, TestWorld.Attack(victim.Id, mob.Id));
            Assert.True(mob.IsDead);
            var corpse = Assert.Single(simulation.State.Corpses);

            // 4. Лут с трупа: ресурс попадает в инвентарь.
            TestWorld.Tick(simulation, TestWorld.Loot(victim.Id, corpse.Id));
            Assert.Equal(1, victim.Inventory.CountOf(2));
            Assert.Empty(simulation.State.Corpses);

            // 5. Крафт из собранного ресурса: дерево ушло, меч появился.
            TestWorld.Tick(simulation, TestWorld.Craft(victim.Id, 1));
            Assert.Equal(0, victim.Inventory.CountOf(1));
            Assert.Equal(1, victim.Inventory.CountOf(TestWorld.SwordItemId));

            // 6. Второй игрок добивает жертву в красной зоне: инвентарь и снаряжение теряются.
            for (var tick = 0; tick < 10; tick++)
            {
                TestWorld.Tick(simulation, TestWorld.Move(killer.Id, -1f, 0f, (uint)tick, (uint)tick));
            }

            TestWorld.Tick(simulation, TestWorld.Attack(killer.Id, victim.Id));

            Assert.True(victim.IsDead,
                "victim hp=" + victim.Hp + " pos=" + victim.Position + " killer pos=" + killer.Position
                + " distance=" + Vector3.Distance(victim.Position, killer.Position));
            Assert.True(victim.Inventory.IsEmpty);
            Assert.True(victim.Equipment.IsEmpty);
            Assert.True(killer.Inventory.CountOf(TestWorld.SwordItemId) == 0);
        }
    }
}
