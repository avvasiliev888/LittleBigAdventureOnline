using System;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Снаряжение, Слава и дерево навыков (ADR-015): бонусы предметов входят в производные
    /// характеристики, Слава копится по ветке оружия в руке, узлы открываются по порогам.
    /// </summary>
    public sealed class ProgressionTests
    {
        [Fact]
        public void Equipment_AffectsMaxHpDamageAndMoveSpeed()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);

            Assert.Equal(120, player.MaxHp); // 100 актора + 20 доспеха
            Assert.Equal(120, player.Hp);
            Assert.Equal(4f, player.MoveSpeed);

            var simulation = TestWorld.Simulation(config, player, target);
            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            // (50 способности + 4 оружия) * 1.0 ветки = 54.
            Assert.Equal(120 - 54, target.Hp);

            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.BootsItemId));

            Assert.Equal(4.2f, player.MoveSpeed, 4);
            Assert.Equal(120, player.MaxHp);
        }

        [Fact]
        public void Kill_AwardsFameToEquippedWeaponArchetype()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            Assert.Equal(0, player.Fame.Total);

            Kill(simulation, target);

            Assert.Equal(30, player.Fame.Get("warrior"));
            Assert.Equal(0, player.Fame.Get("archer"));
            Assert.Equal(30, player.Fame.Total);
        }

        [Fact]
        public void Fame_UnlocksPassiveNodes_AtThreshold()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            Kill(simulation, target);
            Assert.Equal(30, player.Fame.Get("warrior"));
            Assert.Equal(1f, player.DamageMultiplier, 5);
            Assert.Equal(120, player.MaxHp);

            RespawnTick(simulation);
            Kill(simulation, target);

            Assert.Equal(60, player.Fame.Get("warrior"));
            Assert.Equal(1.10f, player.DamageMultiplier, 5);
            Assert.Equal(130, player.MaxHp); // +10 пассивного узла
            Assert.Equal(ProgressionFixture.WarriorAbilityId, player.AbilityId);
        }

        [Fact]
        public void Fame_AtEighty_ReplacesActiveAbility()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            for (var i = 0; i < 3; i++)
            {
                Kill(simulation, target);
                RespawnTick(simulation);
            }

            Assert.Equal(90, player.Fame.Get("warrior"));
            Assert.Equal(ProgressionFixture.WarriorHeavyAbilityId, player.AbilityId);
        }

        [Fact]
        public void WeaponSwap_KeepsArchetypeProgress_AndChangesAbility()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            Kill(simulation, target);
            RespawnTick(simulation);
            Kill(simulation, target);
            Assert.Equal(60, player.Fame.Get("warrior"));
            Assert.Equal(1.10f, player.DamageMultiplier, 5);

            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.BowItemId));

            Assert.Equal(ProgressionFixture.BowItemId, player.Equipment.Weapon!.Definition.Id);
            Assert.Equal(ProgressionFixture.ArcherAbilityId, player.AbilityId);
            Assert.Equal(60, player.Fame.Get("warrior"));
            Assert.Equal(0, player.Fame.Get("archer"));
            Assert.Equal(1f, player.DamageMultiplier, 5);
            Assert.Equal(120, player.MaxHp); // доспех без пассивного узла воина
            Assert.Equal(1, player.Inventory.CountOf(ProgressionFixture.SwordItemId));

            RespawnTick(simulation);
            Kill(simulation, target);
            Assert.Equal(30, player.Fame.Get("archer"));
            Assert.Equal(60, player.Fame.Get("warrior"));

            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.SwordItemId));

            Assert.Equal(ProgressionFixture.WarriorAbilityId, player.AbilityId);
            Assert.Equal(1.10f, player.DamageMultiplier, 5);
            Assert.Equal(130, player.MaxHp);
        }

        [Fact]
        public void Equip_DisplacedItem_ReturnsToInventory()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            Assert.Equal(0, player.Inventory.CountOf(ProgressionFixture.SwordItemId));

            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.BowItemId));

            Assert.Equal(1, player.Inventory.CountOf(ProgressionFixture.SwordItemId));
            Assert.Equal(0, player.Inventory.CountOf(ProgressionFixture.BowItemId));
        }

        [Fact]
        public void Equip_MaterialOrUnknownOrMissingItem_IsIgnored()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            // Сырьё сначала попадает в инвентарь сбором, но надеть его всё равно нельзя.
            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));
            Assert.Equal(2, player.Inventory.CountOf(1));

            TestWorld.Tick(simulation, TestWorld.Equip(1, 1));
            TestWorld.Tick(simulation, TestWorld.Equip(1, 999));
            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.BowItemId));

            // Лук надет, а повторная команда без предмета в инвентаре ничего не меняет.
            TestWorld.Tick(simulation, TestWorld.Equip(1, ProgressionFixture.BowItemId));

            Assert.Equal(ProgressionFixture.BowItemId, player.Equipment.Weapon!.Definition.Id);
            Assert.Equal(2, player.Inventory.CountOf(1));
        }

        [Fact]
        public void Kill_WithoutWeapon_AwardsNoFame()
        {
            var config = ProgressionFixture.Config(withWeapon: false);
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            Assert.True(player.Equipment.IsEmpty);
            Assert.Equal(ProgressionFixture.UnarmedAbilityId, player.AbilityId);

            Kill(simulation, target);

            Assert.True(player.Fame.IsEmpty);
            Assert.Equal(ProgressionFixture.UnarmedAbilityId, player.AbilityId);
        }

        [Fact]
        public void Death_DoesNotResetFame()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var target = ProgressionFixture.Target(config);
            var simulation = TestWorld.Simulation(config, player, target);

            Kill(simulation, target);
            Assert.Equal(30, player.Fame.Get("warrior"));

            RespawnTick(simulation);

            player.Hp = 1;
            TestWorld.Tick(simulation, TestWorld.Attack(2, 1));
            Assert.True(player.IsDead);

            RespawnTick(simulation);
            Assert.False(player.IsDead);
            Assert.Equal(30, player.Fame.Get("warrior"));
        }

        [Fact]
        public void Gather_AddsItemsAwardsFame_AndRespectsCooldown()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));

            Assert.Equal(2, player.Inventory.CountOf(1));
            Assert.Equal(5, player.Fame.Get("warrior"));
            Assert.True(player.GatherCooldownUntilTick > 0);

            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));
            Assert.Equal(2, player.Inventory.CountOf(1));

            TestWorld.TickMany(simulation, 12);
            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));

            Assert.Equal(4, player.Inventory.CountOf(1));
            Assert.Equal(10, player.Fame.Get("warrior"));
        }

        [Fact]
        public void Gather_OutOfRangeOrUnknownNode_DoesNothing()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.FarNodeId));
            TestWorld.Tick(simulation, TestWorld.Gather(1, 99));

            Assert.Equal(0, player.Inventory.CountOf(1));
            Assert.Equal(0, player.Inventory.CountOf(2));
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void Gather_WithoutWeapon_AddsItemsButNoFame()
        {
            var config = ProgressionFixture.Config(withWeapon: false);
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));

            Assert.Equal(2, player.Inventory.CountOf(1));
            Assert.True(player.Fame.IsEmpty);
        }

        [Fact]
        public void DeadEntity_CannotGather()
        {
            var config = ProgressionFixture.Config();
            var player = ProgressionFixture.Player(config);
            var simulation = TestWorld.Simulation(config, player);

            player.Hp = 0;
            TestWorld.Tick(simulation, TestWorld.Gather(1, ProgressionFixture.WoodNodeId));

            Assert.Equal(0, player.Inventory.CountOf(1));
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void Gather_WhenInventoryFull_DoesNotConsumeCooldownOrAwardFame()
        {
            var config = TestWorld.Config(
                resourceNodes: new[]
                {
                    new ResourceNodeConfig(7, "ore_vein", 2, 2f, 0f, 1, 1.5f, 3, 3f),
                },
                starterItems: new[] { new LoadoutItem(1, 5) },
                starterEquipment: new[] { new LoadoutEquipment(TestWorld.SwordItemId) },
                inventorySlots: 1);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Gather(1, 7));

            Assert.Equal(0, player.Inventory.CountOf(2));
            Assert.Equal(0u, player.GatherCooldownUntilTick);
            Assert.Equal(0, player.Fame.Total);
        }

        [Fact]
        public void Equip_WhenDisplacedItemHasNoRoom_IsRejected()
        {
            // Инвентарь заполнен двумя стопками; у меча в стопке 2 штуки, поэтому слот не освободится.
            var config = TestWorld.Config(
                starterItems: new[]
                {
                    new LoadoutItem(TestWorld.SwordItemId, 2),
                    new LoadoutItem(1, 1),
                },
                starterEquipment: new[] { new LoadoutEquipment(TestWorld.BowItemId) },
                inventorySlots: 2);
            var player = TestWorld.Entity(1, config);
            var simulation = TestWorld.Simulation(config, player);

            TestWorld.Tick(simulation, TestWorld.Equip(1, TestWorld.SwordItemId));

            Assert.Equal(TestWorld.BowItemId, player.Equipment.Weapon?.Definition.Id);
            Assert.Equal(2, player.Inventory.CountOf(TestWorld.SwordItemId));
        }

        private static void Kill(CoreSimulation simulation, EntityState victim)
        {
            victim.Hp = 1;
            TestWorld.Tick(simulation, TestWorld.Attack(1, victim.Id));
        }

        /// <summary>Ждёт респавн цели и спад кулдауна способности (0.5 с + 0.2 с запаса).</summary>
        private static void RespawnTick(CoreSimulation simulation)
        {
            TestWorld.TickMany(simulation, 16);
        }
    }
}
