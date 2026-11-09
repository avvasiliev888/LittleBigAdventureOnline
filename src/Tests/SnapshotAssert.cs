using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Сравнение снапшотов по всем полям — для проверок детерминизма и восстановления.</summary>
    internal static class SnapshotAssert
    {
        public static void Equal(SimulationSnapshot expected, SimulationSnapshot actual)
        {
            Assert.Equal(expected.Tick, actual.Tick);
            Assert.Equal(expected.Entities.Count, actual.Entities.Count);

            for (var i = 0; i < expected.Entities.Count; i++)
            {
                var left = expected.Entities[i];
                var right = actual.Entities[i];

                Assert.Equal(left.Id, right.Id);
                Assert.Equal(left.Position, right.Position);
                Assert.Equal(left.Hp, right.Hp);
                Assert.Equal(left.MaxHp, right.MaxHp);
                Assert.Equal(left.IsDead, right.IsDead);
            }
        }

        public static void Equal(Vector3 expected, Vector3 actual, float tolerance = 1e-5f)
        {
            Assert.Equal(expected.X, actual.X, tolerance);
            Assert.Equal(expected.Y, actual.Y, tolerance);
            Assert.Equal(expected.Z, actual.Z, tolerance);
        }
    }
}
