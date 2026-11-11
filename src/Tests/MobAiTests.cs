using System;
using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;
using CoreSimulation = Simulation.Core.Simulation;

namespace Tests
{
    /// <summary>
    /// Серверный автомат мобов (ADR-019): переходы Idle/Patrol/Chase/Attack/Return,
    /// правила зон (синяя passive, жёлтая и красная агрессивные), возврат домой с лечением.
    /// </summary>
    public sealed class MobAiTests
    {
        [Fact]
        public void YellowZoneMob_ChasesAndAttacksPlayerInsideAggroRange()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob() });
            var player = Player(config, 3f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation);

            Assert.Equal(MobAiState.Chase, mob.AiState);
            Assert.True(mob.Position.X > 0f);

            var ticks = 0;
            while (mob.AiState != MobAiState.Attack && ticks < 60)
            {
                TestWorld.Tick(simulation);
                ticks++;
            }

            Assert.Equal(MobAiState.Attack, mob.AiState);
            Assert.True(player.Hp < 100);
        }

        [Fact]
        public void Mob_SteersAroundRock_InsteadOfStickingToIt()
        {
            var rock = new WorldCollider("rock_steering", 1.5f, 0f, 0.7f, "rock");
            var config = TestWorld.Config(
                mobs: new[] { TestWorld.Mob(aggroRange: 20f, leashRange: 30f, attackRange: 1.6f, moveSpeed: 3f) },
                worldColliders: new[] { rock });
            var player = Player(config, 4f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            var attacked = false;
            var maxZ = 0f;
            for (var i = 0; i < 200 && !attacked; i++)
            {
                TestWorld.Tick(simulation);
                maxZ = MathF.Max(maxZ, MathF.Abs(mob.Position.Z));
                attacked = player.Hp < 100;
            }

            Assert.True(attacked, "Моб должен обойти камень и добраться до игрока.");
            Assert.True(maxZ > 0.4f, "Обход камня должен уводить моба в сторону, а не толкать в него.");
            Assert.True(Vector3.Distance(mob.Position, mob.SpawnPosition) < 5f);
        }

        [Fact]
        public void Attack_RespectsAttackIntervalFromConfig()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob(attackSeconds: 1f, damage: 5) });
            var player = Player(config, 1f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation);
            var afterFirstHit = player.Hp;
            Assert.Equal(95, afterFirstHit);
            Assert.Equal(MobAiState.Attack, mob.AiState);

            // 1 секунда интервала = 20 тиков: до истечения урона нет.
            TestWorld.TickMany(simulation, 5);
            Assert.Equal(afterFirstHit, player.Hp);

            TestWorld.TickMany(simulation, 20);
            Assert.True(player.Hp < afterFirstHit);
        }

        [Fact]
        public void BlueZoneMob_DoesNotAttackUnprovokedPlayer()
        {
            var zone = TestWorld.Zone(1, ZoneKind.Blue, -100f, 100f, pvpEnabled: false, hostileFlagSeconds: 0f);
            var config = TestWorld.Config(zones: new[] { zone }, mobs: new[] { TestWorld.Mob(zoneKey: "blue") });
            var player = Player(config, 3f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.TickMany(simulation, 60);

            Assert.Equal(100, player.Hp);
            Assert.NotEqual(MobAiState.Attack, mob.AiState);
            Assert.Equal(mob.SpawnPosition, mob.Position);
        }

        [Fact]
        public void BlueZoneMob_RetaliatesAfterPlayerAttack()
        {
            var zone = TestWorld.Zone(1, ZoneKind.Blue, -100f, 100f, pvpEnabled: false, hostileFlagSeconds: 0f);
            var config = TestWorld.Config(zones: new[] { zone }, mobs: new[] { TestWorld.Mob(zoneKey: "blue") });
            var player = Player(config, 3f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));
            Assert.Equal(player.Id, mob.AiTargetId);
            Assert.True(mob.Hp < mob.MaxHp);

            var ticks = 0;
            while (mob.AiState != MobAiState.Attack && ticks < 60)
            {
                TestWorld.Tick(simulation);
                ticks++;
            }

            Assert.Equal(MobAiState.Attack, mob.AiState);
            Assert.True(player.Hp < 100);
        }

        [Fact]
        public void Mob_ReturnsHomeWhenChaseDragsItBeyondLeashRange()
        {
            var config = TestWorld.Config(mobs: new[]
            {
                TestWorld.Mob(aggroRange: 4f, leashRange: 6f),
            });
            var player = Player(config, 3.5f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            var returned = false;
            for (var i = 0; i < 100 && !returned; i++)
            {
                // Игрок «убегает», оставаясь в пределах leash: моб тянется за ним и в итоге
                // оказывается слишком далеко от своего спавна.
                player.Position = new Vector3(mob.Position.X + 3.5f, 0f, 0f);
                TestWorld.Tick(simulation);
                returned = mob.AiState == MobAiState.Return;
            }

            Assert.True(returned);

            player.Position = new Vector3(500f, 0f, 0f);
            TestWorld.TickMany(simulation, 200);

            Assert.Equal(mob.SpawnPosition, mob.Position);
            Assert.True(mob.AiState == MobAiState.Idle || mob.AiState == MobAiState.Patrol);
        }

        [Fact]
        public void Mob_ReturnsHomeAndRestoresHpWhenTargetDisappears()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob() });
            var player = Player(config, 3f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));
            TestWorld.Tick(simulation);
            Assert.True(mob.Position.X > 0f);
            Assert.True(mob.Hp < mob.MaxHp);

            // Игрок «убежал» далеко: цель потеряна, моб обязан вернуться на спавн и полечиться.
            player.Position = new Vector3(200f, 0f, 0f);
            var ticks = 0;
            while (mob.Hp < mob.MaxHp && ticks < 200)
            {
                TestWorld.Tick(simulation);
                ticks++;
            }

            Assert.Equal(mob.MaxHp, mob.Hp);
            Assert.Equal(mob.SpawnPosition, mob.Position);
            Assert.NotEqual(MobAiState.Chase, mob.AiState);
            Assert.True(mob.AiState == MobAiState.Idle || mob.AiState == MobAiState.Patrol);
        }

        [Fact]
        public void Mob_PatrolsToPointThenPausesBeforeNextPoint()
        {
            var config = TestWorld.Config(mobs: new[]
            {
                TestWorld.Mob(
                    patrol: new[] { TestWorld.Point(2f, 0f), TestWorld.Point(0f, 2f) },
                    patrolPauseSeconds: 0.5f),
            });
            var simulation = TestWorld.Simulation(config);
            var mob = SpawnSingleMob(simulation);

            var ticks = 0;
            while (mob.AiState == MobAiState.Patrol && ticks < 100)
            {
                TestWorld.Tick(simulation);
                ticks++;
            }

            Assert.Equal(MobAiState.Idle, mob.AiState);
            Assert.Equal(2f, mob.Position.X, 3);

            TestWorld.TickMany(simulation, 5);
            Assert.Equal(MobAiState.Idle, mob.AiState);

            TestWorld.TickMany(simulation, 10);
            Assert.Equal(MobAiState.Patrol, mob.AiState);
        }

        [Fact]
        public void Mob_WithoutPatrolPoints_StaysIdleAtSpawn()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob() });
            var simulation = TestWorld.Simulation(config);
            var mob = SpawnSingleMob(simulation);

            TestWorld.TickMany(simulation, 100);

            Assert.Equal(MobAiState.Idle, mob.AiState);
            Assert.Equal(mob.SpawnPosition, mob.Position);
        }

        [Fact]
        public void Mob_KillsPlayerAndReturnsToPatrol()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob(damage: 50) });
            var player = Player(config, 1f, 0f);
            player.Hp = 40;
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation);

            Assert.True(player.IsDead);
            Assert.Equal(MobAiState.Attack, mob.AiState);

            // Цель умерла: моб больше не в Attack и не «залипает» в погоне.
            TestWorld.TickMany(simulation, 3);
            Assert.NotEqual(MobAiState.Attack, mob.AiState);
        }

        [Fact]
        public void Mob_RespawnsAtSpawnAfterDeath()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob(maxHp: 20, respawnSeconds: 1f) });
            var player = Player(config, 2f, 0f);
            var simulation = TestWorld.Simulation(config, player);
            var mob = SpawnSingleMob(simulation);

            TestWorld.Tick(simulation, TestWorld.Attack(player.Id, mob.Id));
            Assert.True(mob.IsDead);
            Assert.Single(simulation.State.Corpses);

            player.Position = new Vector3(200f, 0f, 0f);
            TestWorld.TickMany(simulation, 40);

            Assert.False(mob.IsDead);
            Assert.Equal(mob.MaxHp, mob.Hp);
            Assert.Equal(mob.SpawnPosition, mob.Position);
            Assert.True(mob.AiState == MobAiState.Idle || mob.AiState == MobAiState.Patrol);
        }

        [Fact]
        public void SpawnMobs_ReturnsZeroWithoutConfiguration()
        {
            var simulation = new CoreSimulation();

            Assert.Equal(0, simulation.SpawnMobs());
        }

        [Fact]
        public void AllocateEntityId_SkipsExistingEntitiesAndCorpses()
        {
            var config = TestWorld.Config();
            var entity = new EntityState(1, config.GetActor(1), Vector3.Zero);
            var corpse = new CorpseState(2, Vector3.Zero, 2f, 0, Array.Empty<ItemStack>(), 100);
            var state = new SimulationState(0, new[] { entity }, new[] { corpse });
            var simulation = new CoreSimulation(state, config);

            Assert.Equal(3u, simulation.AllocateEntityId());
        }

        [Fact]
        public void RestoreFromSnapshot_RestoresMobDefinition()
        {
            var config = TestWorld.Config(mobs: new[] { TestWorld.Mob() });
            var simulation = TestWorld.Simulation(config);
            SpawnSingleMob(simulation);

            var snapshot = simulation.CreateSnapshot();
            var restored = new CoreSimulation(config);
            restored.RestoreFromSnapshot(snapshot);

            var mob = restored.State.Entities.Single();
            Assert.NotNull(mob.Mob);
            Assert.Equal(MobAiState.Idle, mob.AiState);
            Assert.Equal(config.Mobs[1].ActorId, mob.ActorId);
        }

        private static EntityState Player(SimulationConfig config, float x, float z)
        {
            return new EntityState(10, config.GetActor(TestWorld.PlayerActorId), new Vector3(x, 0f, z));
        }

        private static EntityState SpawnSingleMob(CoreSimulation simulation)
        {
            Assert.Equal(1, simulation.SpawnMobs());
            return simulation.State.Entities.Single(entity => entity.Mob != null);
        }
    }
}
