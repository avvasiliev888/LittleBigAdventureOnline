using System;
using System.IO;
using System.Numerics;
using Client.Network;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M9: клиент использует тот же JSON-конфиг, что и сервер (ADR-011).
    /// «Изменил JSON → изменилось поведение клиента» проверяется без перекомпиляции:
    /// тест подаёт другой JSON и смотрит на шаг предсказания и кулдаун.
    /// </summary>
    public sealed class ClientContentTests
    {
        private const string ActorsTemplate = @"[
  {{ ""id"": 1, ""maxHp"": {0}, ""moveSpeed"": {1}, ""maxResource"": 100, ""respawnSeconds"": 5.0, ""abilityId"": 1 }}
]";

        private const string AbilitiesTemplate = @"[
  {{ ""id"": 1, ""damage"": 25, ""range"": {0}, ""cooldownSeconds"": {1}, ""resourceCost"": 10 }}
]";

        [Fact]
        public void LoadFromDirectory_RepoConfig_LoadsAndHashes()
        {
            var content = ClientContent.LoadFromDirectory(RepoConfigDirectory());

            Assert.NotEmpty(content.Config.Actors);
            Assert.NotEmpty(content.Config.Abilities);
            Assert.Equal(64, content.Hash.Length);
            Assert.Equal(ConfigLoader.ComputeHash(RepoConfigDirectory()), content.Hash);
        }

        [Fact]
        public void ClientAndServerReadSameFile_SameHash()
        {
            // Сервер и клиент идут через один и тот же ConfigLoader из Shared; хеш — доказательство.
            var clientHash = ClientContent.LoadFromDirectory(RepoConfigDirectory()).Hash;
            var serverHash = ConfigLoader.ComputeHash(RepoConfigDirectory());

            Assert.Equal(serverHash, clientHash);
        }

        [Fact]
        public void ChangedJsonFile_ChangesHash()
        {
            var original = ConfigLoader.ComputeHash(RepoConfigDirectory());

            var temp = Path.Combine(Path.GetTempPath(), "albion-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                File.Copy(Path.Combine(RepoConfigDirectory(), ConfigLoader.ActorsFileName), Path.Combine(temp, ConfigLoader.ActorsFileName));
                File.WriteAllText(Path.Combine(temp, ConfigLoader.AbilitiesFileName), Abilities(range: 3f, cooldown: 2f));

                var modified = ConfigLoader.ComputeHash(temp);

                Assert.NotEqual(original, modified);
            }
            finally
            {
                Directory.Delete(temp, recursive: true);
            }
        }

        [Fact]
        public void ChangedMoveSpeedJson_ChangesPredictionStep_WithoutRecompile()
        {
            var defaultContent = ClientContent.LoadFromDirectory(RepoConfigDirectory());
            var defaultPrediction = Predict(ClientContent.LoadFromJson(
                Actors(maxHp: 100, moveSpeed: 4f),
                Abilities(range: 3f, cooldown: 2f)));

            var changedPrediction = Predict(ClientContent.LoadFromJson(
                Actors(maxHp: 100, moveSpeed: 8f),
                Abilities(range: 3f, cooldown: 2f)));

            Assert.Equal(0.2f, defaultPrediction.Position.X, 3);
            Assert.Equal(0.4f, changedPrediction.Position.X, 3);
            Assert.Equal(defaultContent.GetActor(1).MoveSpeed, defaultPrediction.MoveSpeed);
        }

        [Fact]
        public void ChangedCooldownJson_ChangesPredictedCooldown_WithoutRecompile()
        {
            var fast = new ClientPrediction(1, ClientContent.LoadFromJson(Actors(100, 4f), Abilities(3f, 2f)).GetActor(1), ClientContent.LoadFromJson(Actors(100, 4f), Abilities(3f, 2f)).GetAbility(1));
            var slowContent = ClientContent.LoadFromJson(Actors(100, 4f), Abilities(3f, 5f));
            var slow = new ClientPrediction(1, slowContent.GetActor(1), slowContent.GetAbility(1));

            Assert.Equal(40, fast.AbilityCooldownTicks);
            Assert.Equal(100, slow.AbilityCooldownTicks);

            fast.PredictAbility(0, 1);
            slow.PredictAbility(0, 1);

            Assert.Equal(40u, fast.CooldownUntilTick);
            Assert.Equal(100u, slow.CooldownUntilTick);
        }

        [Fact]
        public void Configuration_UsesRangeAndMaxHpFromJson()
        {
            var content = ClientContent.LoadFromJson(Actors(maxHp: 250, moveSpeed: 4f), Abilities(range: 7.5f, cooldown: 2f));
            var prediction = new ClientPrediction(1, content.GetActor(1), content.GetAbility(1));

            Assert.Equal(250, content.GetActor(1).MaxHp);
            Assert.Equal(7.5f, prediction.AbilityRange);
        }

        [Fact]
        public void PredictAbility_ReconciliationUsesServerCooldownAfterAck()
        {
            var content = ClientContent.LoadFromJson(Actors(100, 4f), Abilities(3f, 2f));
            var prediction = new ClientPrediction(7, content.GetActor(1), content.GetAbility(1));
            prediction.Reset(Vector3.Zero);

            prediction.PredictAbility(10, 1);
            Assert.Equal(50u, prediction.CooldownUntilTick);

            // Сервер ещё не подтвердил команду — предсказанный кулдаун сохраняется.
            prediction.Reconcile(Snapshot(cooldownUntilTick: 0), acknowledgedSequence: 0);
            Assert.Equal(50u, prediction.CooldownUntilTick);

            // Команда подтверждена, но сервер её отклонил (кулдаун 0) — истина серверная.
            prediction.Reconcile(Snapshot(cooldownUntilTick: 0), acknowledgedSequence: 1);
            Assert.Equal(0u, prediction.CooldownUntilTick);
            Assert.Equal(0, prediction.CooldownRemainingTicks(10));
        }

        [Fact]
        public void LoadFromJson_InvalidJson_Throws()
        {
            var exception = Assert.Throws<ConfigValidationException>(
                () => ClientContent.LoadFromJson("{ not json", Abilities(3f, 2f)));

            Assert.Contains("невалидный JSON", exception.Message);
        }

        private static ClientPrediction Predict(ClientContent content)
        {
            var prediction = new ClientPrediction(1, content.GetActor(1), content.GetAbility(1));
            prediction.Reset(Vector3.Zero);
            prediction.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), 1, 1));
            return prediction;
        }

        private static SimulationSnapshot Snapshot(uint cooldownUntilTick)
        {
            var position = Vector3.Zero;
            return new SimulationSnapshot(11, new[]
            {
                new EntitySnapshot(7, 1, position, position, 100, 100, 100, 100, cooldownUntilTick, 0, false),
            });
        }

        private static string Actors(int maxHp, float moveSpeed)
        {
            return string.Format(ActorsTemplate, maxHp, moveSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string Abilities(float range, float cooldown)
        {
            return string.Format(
                AbilitiesTemplate,
                range.ToString(System.Globalization.CultureInfo.InvariantCulture),
                cooldown.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string RepoConfigDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "config");
        }
    }
}
