using System.IO;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Тесты на реальном конфиге репозитория (`config/`): «изменил JSON → изменилось поведение».
    /// Если оператор поменяет числа в `config/abilities.json` или `config/actors.json`,
    /// этот тест упадёт с конкретным ожидаемым значением — то есть правка конфига видна.
    /// </summary>
    public sealed class RepoConfigTests
    {
        [Fact]
        public void RepoConfig_Ability_HasDocumentedBalance()
        {
            var config = LoadRepoConfig();
            var ability = config.GetAbility(1);

            Assert.Equal(25, ability.Damage);
            Assert.Equal(3f, ability.Range);
            Assert.Equal(2f, ability.CooldownSeconds);
            Assert.Equal(10, ability.ResourceCost);
        }

        [Fact]
        public void RepoConfig_Actor_HasDocumentedBalance()
        {
            var config = LoadRepoConfig();
            var actor = config.GetActor(1);

            Assert.Equal(100, actor.MaxHp);
            Assert.Equal(4f, actor.MoveSpeed);
            Assert.Equal(100, actor.MaxResource);
            Assert.Equal(5f, actor.ResourceRegenPerSecond);
            Assert.Equal(5f, actor.RespawnSeconds);
        }

        [Fact]
        public void RepoConfig_Zones_HaveDocumentedRules()
        {
            var config = LoadRepoConfig();

            var blue = config.Zones.GetZone(new System.Numerics.Vector3(0f, 0f, 0f));
            var yellow = config.Zones.GetZone(new System.Numerics.Vector3(18f, 0f, 0f));
            var red = config.Zones.GetZone(new System.Numerics.Vector3(33f, 0f, 0f));

            Assert.Equal(ZoneKind.Blue, blue.Kind);
            Assert.False(blue.PvpEnabled);
            Assert.False(blue.FullLootOnDeath);

            Assert.Equal(ZoneKind.Yellow, yellow.Kind);
            Assert.True(yellow.PvpEnabled);
            Assert.False(yellow.FullLootOnDeath);

            Assert.Equal(ZoneKind.Red, red.Kind);
            Assert.True(red.PvpEnabled);
            Assert.True(red.FullLootOnDeath);
        }

        [Fact]
        public void RepoConfig_Player_StartsWithInsuranceLoadout()
        {
            var config = LoadRepoConfig();
            var actor = config.GetActor(1);

            Assert.Equal(ActorKind.Player, actor.Kind);
            Assert.NotEmpty(actor.StarterItems);
            Assert.Equal(4, actor.StarterEquipment.Count);
        }

        [Fact]
        public void RepoConfig_DamageFromJson_IsAppliedInSimulation()
        {
            var config = LoadRepoConfig();
            var expectedHp = config.GetActor(1).MaxHp - config.GetAbility(1).Damage;

            // Бой возможен только в зоне с PvP: репозиторный конфиг держит спавн в синей зоне.
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config, x: 18f),
                TestWorld.Entity(2, config, x: 19f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(expectedHp, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void ChangedJsonDamage_ChangesSimulationResult()
        {
            const string actors = @"[
  { ""id"": 1, ""kind"": ""player"", ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";
            const string abilities = @"[
  { ""id"": 1, ""damage"": 40, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

            var config = ConfigLoader.LoadFromJson(actors, abilities, TestWorld.ZonesJson, TestWorld.ItemsJson);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(60, simulation.State.Entities[1].Hp);
        }

        private static SimulationConfig LoadRepoConfig()
        {
            var directory = Path.Combine(System.AppContext.BaseDirectory, "config");
            return ConfigLoader.LoadFromDirectory(directory);
        }
    }
}
