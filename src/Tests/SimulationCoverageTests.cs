using System.Linq;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Цель стоит вплотную: моб берёт запасное направление dx=1, dz=0, distance=1
    /// и всё равно отходит, а не остаётся делить точку с целью.
    /// </summary>
    public class SimulationCoverageTests
    {
        [Fact]
        public void MoveMobAway_TargetOnTopOfMob_RetreatsWithFallbackDirection()
        {
            var config = TestWorld.Config(mobs: new[]
            {
                TestWorld.Mob(id: 1, spawns: new[] { new Vector2(5f, 5f) },
                    attackRange: 5f, keepDistance: 4f, weaponType: "staff",
                    aggroRange: 10f, leashRange: 30f),
            });
            var player = TestWorld.Entity(1, config, 5f, 5f);
            var simulation = TestWorld.Simulation(config, player);
            Assert.Equal(1, simulation.SpawnMobs());
            var mob = simulation.State.Entities.Single(entity => entity.Mob != null);

            TestWorld.Tick(simulation);

            Assert.True(Vector3.Distance(mob.Position, new Vector3(5f, 0f, 5f)) > 0.9f);
        }
    }
}
