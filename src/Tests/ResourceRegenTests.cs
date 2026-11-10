using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Регенерация ресурса (M10.10.1): без неё 10 ударов по 10 ресурса навсегда блокировали
    /// способность, и манекен после респавна становился «бессмертным» на 75 HP.
    /// </summary>
    public sealed class ResourceRegenTests
    {
        [Fact]
        public void Resource_RegeneratesFractionally_WithoutLosingSubTickRemainder()
        {
            var config = TestWorld.Config(resourceRegenPerSecond: 5f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));
            var caster = simulation.State.Entities[0];
            caster.Resource = 0;

            // 5 ед/с при тике 0.05 — это 0.25 за тик: одна единица только на пятом тике.
            TestWorld.TickMany(simulation, 1);
            Assert.Equal(0, caster.Resource);
            Assert.Equal(0.25f, caster.ResourceRegenRemainder, precision: 4);

            TestWorld.TickMany(simulation, 4);
            Assert.Equal(1, caster.Resource);

            TestWorld.TickMany(simulation, 20);
            Assert.Equal(6, caster.Resource);
        }

        [Fact]
        public void Resource_StopsAtMax_AndDiscardsRemainder()
        {
            var config = TestWorld.Config(resourceRegenPerSecond: 100f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));
            var caster = simulation.State.Entities[0];
            caster.Resource = 96;

            TestWorld.TickMany(simulation, 3);

            Assert.Equal(100, caster.Resource);
            Assert.Equal(0f, caster.ResourceRegenRemainder);
        }

        [Fact]
        public void DeadEntity_DoesNotRegenerate()
        {
            var config = TestWorld.Config(resourceRegenPerSecond: 100f);
            var simulation = TestWorld.Simulation(config, TestWorld.Entity(1, config));
            var caster = simulation.State.Entities[0];
            caster.Resource = 0;
            caster.Hp = 0;

            TestWorld.TickMany(simulation, 5);

            Assert.Equal(0, caster.Resource);
            Assert.Equal(0f, caster.ResourceRegenRemainder);
        }

        [Fact]
        public void RepeatedCasts_DoNotStarveAndKeepDamagingAfterRespawn()
        {
            // Ровно сценарий оператора: много ударов, респавн манекена и удары после него.
            // Без регенерации на 11-м ударе ресурс был бы 0, и манекен остался бы с 75 HP.
            var config = TestWorld.Config(
                damage: 25,
                cooldownSeconds: 2f,
                resourceCost: 10,
                maxResource: 100,
                respawnSeconds: 1f,
                resourceRegenPerSecond: 5f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, clientTick: 0, sequence: 0));
            for (var i = 1; i < 12; i++)
            {
                TestWorld.TickMany(simulation, 39);
                TestWorld.Tick(simulation, TestWorld.Attack(
                    1,
                    2,
                    clientTick: (uint)(i * 40),
                    sequence: (uint)i));
            }

            Assert.Equal(0, simulation.State.Entities[1].Hp);
            Assert.Equal(90, simulation.State.Entities[0].Resource);
        }

        [Fact]
        public void StarvedCaster_RegeneratesAndDealsDamageAgain()
        {
            // 10 ресурса стоит удар, максимум 10: после первого удара ресурс 0. Через 1 секунду
            // регенерация 10/с возвращает ровно стоимость, и второй удар снова наносит урон.
            var config = TestWorld.Config(
                damage: 25,
                cooldownSeconds: 1f,
                resourceCost: 10,
                maxResource: 10,
                resourceRegenPerSecond: 10f);
            var simulation = TestWorld.Simulation(
                config,
                TestWorld.Entity(1, config),
                TestWorld.Entity(2, config, x: 1f));

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2));
            Assert.Equal(75, simulation.State.Entities[1].Hp);
            Assert.Equal(0, simulation.State.Entities[0].Resource);

            TestWorld.TickMany(simulation, 20);
            Assert.Equal(10, simulation.State.Entities[0].Resource);

            TestWorld.Tick(simulation, TestWorld.Attack(1, 2, clientTick: 21));
            Assert.Equal(50, simulation.State.Entities[1].Hp);
        }
    }
}
