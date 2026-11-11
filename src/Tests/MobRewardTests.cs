using System;
using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Награды за PvE и сбор лута (ADR-019): Слава, детерминированный труп, серебро,
    /// частичный сбор при полном инвентаре и истечение трупа.
    /// </summary>
    public sealed class MobRewardTests
    {
        [Fact]
        public void KillingMob_AwardsFameAndLeavesCorpseWithSilverAndLoot()
        {
            var config = TestWorld.Config(
                starterEquipment: new[] { new LoadoutEquipment(TestWorld.SwordItemId) },
                mobs: new[]
                {
                    TestWorld.Mob(
                        maxHp: 20,
                        fameOnKill: 4,
                        silverMin: 7,
                        silverMax: 7,
                        loot: new[]
                        {
                            TestWorld.MobLoot(1, 2, 2, 1f),
                            TestWorld.MobLoot(2, 1, 1, 0f),
                            TestWorld.MobLoot(1, 1, 1, 1f),
                        }),
                });
            var player = TestWorld.Entity(10, config, 2f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));

            Assert.True(mob.IsDead);
            Assert.Equal(4, player.Fame.Get("warrior"));

            var corpse = Assert.Single(simulation.State.Corpses);
            Assert.Equal(7, corpse.Silver);
            var stack = Assert.Single(corpse.Loot);
            Assert.Equal(1u, stack.ItemId);
            Assert.Equal(3, stack.Quantity);
            Assert.True(corpse.ExpireAtTick > simulation.State.Tick);
        }

        [Fact]
        public void Loot_TransfersItemsAndSilverAndRemovesCorpse()
        {
            var config = TestWorld.Config(
                mobs: new[]
                {
                    TestWorld.Mob(
                        maxHp: 20,
                        silverMin: 5,
                        silverMax: 5,
                        loot: new[] { TestWorld.MobLoot(1, 2, 2, 1f) }),
                });
            var player = TestWorld.Entity(10, config, 2f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);
            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));
            var corpse = Assert.Single(simulation.State.Corpses);

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, corpse.Id));

            Assert.Equal(2, player.Inventory.CountOf(1));
            Assert.Equal(5, player.Silver);
            Assert.Empty(simulation.State.Corpses);
        }

        [Fact]
        public void Loot_OutOfRange_DoesNotTransferAnything()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob(maxHp: 20, silverMin: 5, silverMax: 5) });
            var player = TestWorld.Entity(10, config, 20f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var corpse = new CorpseState(100, Vector3.Zero, 2f, 5, null, 100);
            simulation.State.AddCorpse(corpse);

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, corpse.Id));

            Assert.Equal(0, player.Silver);
            Assert.Single(simulation.State.Corpses);
        }

        [Fact]
        public void Loot_UnknownCorpse_IsIgnored()
        {
            var config = TestWorld.Config();
            var player = TestWorld.Entity(10, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, 999u));

            Assert.Equal(0, player.Silver);
            Assert.Empty(simulation.State.Corpses);
        }

        [Fact]
        public void Loot_DeadPlayer_DoesNotCollect()
        {
            var config = TestWorld.Config();
            var player = TestWorld.Entity(10, config);
            player.Hp = 0;
            var simulation = TestWorld.Simulation(config, player);
            simulation.State.AddCorpse(new CorpseState(100, Vector3.Zero, 2f, 5, null, 100));

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, 100u));

            Assert.Equal(0, player.Silver);
            Assert.Single(simulation.State.Corpses);
        }

        [Fact]
        public void Loot_FullInventory_KeepsItemsButTakesSilver()
        {
            var config = TestWorld.Config(inventorySlots: 1, starterItems: new[] { new LoadoutItem(2, 1) });
            var player = TestWorld.Entity(10, config, 0f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var corpse = new CorpseState(100, Vector3.Zero, 2f, 5, new[] { new ItemStack(1, 3) }, 100);
            simulation.State.AddCorpse(corpse);

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, 100u));

            Assert.Equal(0, player.Inventory.CountOf(1));
            Assert.Equal(5, player.Silver);
            Assert.Single(simulation.State.Corpses);
            Assert.Single(corpse.Loot);
        }

        [Fact]
        public void Loot_SilverOverflow_LeavesSilverInCorpse()
        {
            var config = TestWorld.Config();
            var player = TestWorld.Entity(10, config);
            player.Silver = int.MaxValue;
            var simulation = TestWorld.Simulation(config, player);
            simulation.State.AddCorpse(new CorpseState(100, Vector3.Zero, 2f, 5, null, 100));

            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, 100u));

            Assert.Equal(int.MaxValue, player.Silver);
            Assert.Single(simulation.State.Corpses);
        }

        [Fact]
        public void Corpse_ExpiresAfterConfiguredSeconds()
        {
            var config = TestWorld.Config();
            var simulation = TestWorld.Simulation(config);
            simulation.State.AddCorpse(new CorpseState(100, Vector3.Zero, 2f, 5, null, 10));

            TestWorld.TickMany(simulation, 12);

            Assert.Empty(simulation.State.Corpses);
        }

        [Fact]
        public void Corpse_WithoutLoot_DropsOnlySilverInRange()
        {
            var config = TestWorld.Config(
                mobs: new[] { TestWorld.Mob(maxHp: 20, silverMin: 3, silverMax: 3) });
            var player = TestWorld.Entity(10, config, 2f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));

            var corpse = Assert.Single(simulation.State.Corpses);
            Assert.Equal(3, corpse.Silver);
            Assert.Empty(corpse.Loot);
        }

        [Fact]
        public void Loot_QuantityRange_StaysWithinConfiguredBounds()
        {
            var config = TestWorld.Config(
                starterEquipment: new[] { new LoadoutEquipment(TestWorld.SwordItemId) },
                mobs: new[]
                {
                    TestWorld.Mob(
                        maxHp: 20,
                        silverMin: 1,
                        silverMax: 5,
                        loot: new[]
                        {
                            TestWorld.MobLoot(1, 1, 3, 1f),
                            TestWorld.MobLoot(2, 1, 1, 0.5f),
                        }),
                });
            var player = TestWorld.Entity(10, config, 2f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));

            var corpse = Assert.Single(simulation.State.Corpses);
            Assert.InRange(corpse.Silver, 1, 5);
            var wood = Assert.Single(corpse.Loot, stack => stack.ItemId == 1);
            Assert.InRange(wood.Quantity, 1, 3);
        }

        private static EntityState SpawnSingleMob(CoreSimulation simulation)
        {
            Assert.Equal(1, simulation.SpawnMobs());
            return simulation.State.Entities.Single(entity => entity.Mob != null);
        }
    }
}
