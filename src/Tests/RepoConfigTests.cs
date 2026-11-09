using System.IO;
using Shared.Config;
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
            Assert.Equal(5f, actor.RespawnSeconds);
        }

        [Fact]
        public void RepoConfig_DamageFromJson_IsAppliedInSimulation()
        {
            var config = LoadRepoConfig();
            var expectedHp = config.GetActor(1).MaxHp - config.GetAbility(1).Damage;

            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));

            Assert.Equal(expectedHp, simulation.State.Entities[1].Hp);
        }

        [Fact]
        public void ChangedJsonDamage_ChangesSimulationResult()
        {
            const string actors = @"[
  { ""id"": 1, ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";
            const string abilities = @"[
  { ""id"": 1, ""damage"": 40, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

            var config = ConfigLoader.LoadFromJson(actors, abilities);
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
