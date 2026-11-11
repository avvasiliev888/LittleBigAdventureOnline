using System;
using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Интеграционный сценарий этапа 4 (PLAN §5): моб патрулирует, замечает игрока, догоняет,
    /// наносит урон, умирает от ответных ударов и оставляет труп, который игрок собирает.
    /// </summary>
    public sealed class MobIntegrationTests
    {
        [Fact]
        public void Mob_PatrolsAggroesAttacksDiesAndLeavesLoot()
        {
            var config = TestWorld.Config(
                damage: 25,
                cooldownSeconds: 1f,
                starterEquipment: new[] { new LoadoutEquipment(TestWorld.SwordItemId) },
                mobs: new[]
                {
                    TestWorld.Mob(
                        maxHp: 50,
                        damage: 5,
                        moveSpeed: 3f,
                        attackSeconds: 1f,
                        aggroRange: 5f,
                        leashRange: 15f,
                        fameOnKill: 6,
                        silverMin: 4,
                        silverMax: 4,
                        loot: new[] { TestWorld.MobLoot(1, 2, 2, 1f) },
                        patrol: new[] { TestWorld.Point(2f, 0f) },
                        patrolPauseSeconds: 0.5f),
                });
            var player = TestWorld.Entity(10, config, 8f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            Assert.Equal(1, simulation.SpawnMobs());
            var mob = simulation.State.Entities.Single(entity => entity.Mob != null);

            // 1. Патруль: моб идёт к точке (2,0), игрок вне радиуса агрессии (8 > 5).
            TestWorld.TickMany(simulation, 30);
            Assert.Equal(2f, mob.Position.X, 3);
            Assert.Equal(100, player.Hp);

            // 2. Игрок входит в радиус агрессии: моб бросает патруль и догоняет.
            player.Position = new Vector3(4f, 0f, 0f);
            TestWorld.Tick(simulation);
            Assert.Equal(MobAiState.Chase, mob.AiState);

            var ticks = 0;
            while (mob.AiState != MobAiState.Attack && ticks < 60)
            {
                TestWorld.Tick(simulation);
                ticks++;
            }

            Assert.Equal(MobAiState.Attack, mob.AiState);
            var hpBeforeMobHit = player.Hp;
            TestWorld.TickMany(simulation, 25);
            Assert.True(player.Hp < hpBeforeMobHit);

            // 3. Ответные удары: 50 HP моба и 25 урона — две атаки с интервалом 1 с.
            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));
            Assert.False(mob.IsDead);
            TestWorld.TickMany(simulation, 20);
            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));

            Assert.True(mob.IsDead);
            var corpse = Assert.Single(simulation.State.Corpses);
            Assert.Equal(4, corpse.Silver);
            Assert.Equal(6, player.Fame.Get("warrior"));

            // 4. Игрок стоит рядом, поэтому сразу собирает лут.
            TestWorld.Tick(simulation, TestWorld.Loot(player.Id, corpse.Id));
            Assert.Equal(2, player.Inventory.CountOf(1));
            Assert.Equal(4, player.Silver);
            Assert.Empty(simulation.State.Corpses);
        }
    }
}
