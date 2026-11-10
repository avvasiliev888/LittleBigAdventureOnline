using System;
using System.IO;
using Shared;
using Xunit;

namespace Tests
{
    /// <summary>Загрузчик и валидация конфигов (M3, ADR-005): нет дефолтов, ошибки объяснимы.</summary>
    public sealed class ConfigTests
    {
        private const string ValidActors = @"[
  { ""id"": 1, ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";

        private const string ValidAbilities = @"[
  { ""id"": 1, ""damage"": 25, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

        [Fact]
        public void LoadFromJson_ValidConfig_BuildsSimulationConfig()
        {
            var config = ConfigLoader.LoadFromJson(ValidActors, ValidAbilities);

            Assert.Single(config.Actors);
            Assert.Single(config.Abilities);
            Assert.Equal(4f, config.GetActor(1).MoveSpeed);
            Assert.Equal(25, config.GetAbility(1).Damage);
        }

        [Fact]
        public void LoadFromJson_ActorWithoutResourceRegen_ThrowsWithFieldName()
        {
            const string actors = @"[
  { ""id"": 1, ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""respawnSeconds"": 5.0, ""abilityId"": 1 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(actors, ValidAbilities));

            Assert.Contains("resourceRegenPerSecond", exception.Message);
        }

        [Fact]
        public void LoadFromJson_CommentsAndTrailingCommas_AreAllowed()
        {
            const string actors = @"[
  // комментарий допустим
  { ""id"": 1, ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 1, },
]";

            var config = ConfigLoader.LoadFromJson(actors, ValidAbilities);

            Assert.Single(config.Actors);
        }

        [Fact]
        public void LoadFromJson_MissingDamage_ThrowsWithFieldName()
        {
            const string abilities = @"[ { ""id"": 1, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 } ]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, abilities));

            Assert.Contains("damage", exception.Message);
        }

        [Fact]
        public void LoadFromJson_DuplicateAbilityId_Throws()
        {
            const string abilities = @"[
  { ""id"": 1, ""damage"": 25, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 },
  { ""id"": 1, ""damage"": 30, ""range"": 3.0, ""cooldownSeconds"": 2.0, ""resourceCost"": 10 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(ValidActors, abilities));

            Assert.Contains("Дубликат", exception.Message);
        }

        [Fact]
        public void LoadFromJson_MissingAbilityReference_Throws()
        {
            const string actors = @"[
  { ""id"": 1, ""maxHp"": 100, ""moveSpeed"": 4.0, ""maxResource"": 100, ""resourceRegenPerSecond"": 0.0, ""respawnSeconds"": 5.0, ""abilityId"": 99 }
]";

            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(actors, ValidAbilities));

            Assert.Contains("99", exception.Message);
        }

        [Fact]
        public void LoadFromJson_InvalidJson_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson("{ not json", ValidAbilities));

            Assert.Contains("невалидный JSON", exception.Message);
        }

        [Fact]
        public void LoadFromJson_NonArray_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => ConfigLoader.LoadFromJson(@"{ ""actors"": [] }", ValidAbilities));

            Assert.Contains("JSON-массив", exception.Message);
        }

        [Fact]
        public void Load_MissingFile_Throws()
        {
            var missing = Path.Combine(Path.GetTempPath(), "albion-missing-" + Guid.NewGuid().ToString("N"), "actors.json");

            var exception = Assert.Throws<ConfigValidationException>(() => ConfigLoader.Load(missing, missing));

            Assert.Contains("не найден", exception.Message);
        }

        [Fact]
        public void LoadFromDirectory_MissingFile_Throws()
        {
            var missing = Path.Combine(Path.GetTempPath(), "albion-missing-" + Guid.NewGuid().ToString("N"));

            var exception = Assert.Throws<ConfigValidationException>(() => ConfigLoader.LoadFromDirectory(missing));

            Assert.Contains("не найден", exception.Message);
        }

        [Fact]
        public void LoadFromDirectory_RepoConfig_Loads()
        {
            var config = ConfigLoader.LoadFromDirectory(RepoConfigDirectory());

            Assert.NotEmpty(config.Actors);
            Assert.NotEmpty(config.Abilities);
        }

        [Fact]
        public void ActorConfig_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(0, 100, 4f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 0, 4f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 0f, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, float.NaN, 100, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, -1, 5f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 0f, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 0));
        }

        [Fact]
        public void ActorConfig_RejectsInvalidResourceRegen()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, 1, float.PositiveInfinity));
        }

        [Fact]
        public void AbilityConfig_RejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(0, 25, 3f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, -1, 3f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, -1f, 2f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, 3f, -1f, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Simulation.Core.AbilityConfig(1, 25, 3f, 2f, -1));
        }

        [Fact]
        public void SimulationConfig_RejectsMissingReference()
        {
            var actor = new Simulation.Core.ActorConfig(1, 100, 4f, 100, 5f, abilityId: 7);
            var ability = new Simulation.Core.AbilityConfig(1, 25, 3f, 2f, 10);

            var exception = Assert.Throws<ArgumentException>(
                () => new Simulation.Core.SimulationConfig(new[] { actor }, new[] { ability }));

            Assert.Contains("7", exception.Message);
        }

        private static string RepoConfigDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "config");
        }
    }
}
