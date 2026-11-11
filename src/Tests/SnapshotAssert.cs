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
                Assert.Equal(left.ActorId, right.ActorId);
                Assert.Equal(left.Position, right.Position);
                Assert.Equal(left.SpawnPosition, right.SpawnPosition);
                Assert.Equal(left.Hp, right.Hp);
                Assert.Equal(left.MaxHp, right.MaxHp);
                Assert.Equal(left.Resource, right.Resource);
                Assert.Equal(left.MaxResource, right.MaxResource);
                Assert.Equal(left.CooldownUntilTick, right.CooldownUntilTick);
                Assert.Equal(left.RespawnAtTick, right.RespawnAtTick);
                Assert.Equal(left.IsDead, right.IsDead);
                Assert.Equal(left.ZoneId, right.ZoneId);
                Assert.Equal(left.AbilityId, right.AbilityId);
                Assert.Equal(left.MoveSpeed, right.MoveSpeed);
                Assert.Equal(left.Silver, right.Silver);
                Assert.Equal(left.HostileUntilTick, right.HostileUntilTick);
                Assert.Equal(left.WeaponItemId, right.WeaponItemId);
                Assert.Equal(left.ChestItemId, right.ChestItemId);
                Assert.Equal(left.LegsItemId, right.LegsItemId);
                Assert.Equal(left.FeetItemId, right.FeetItemId);
                Assert.Equal(left.FameTotal, right.FameTotal);
                Assert.Equal(left.BranchFame, right.BranchFame);
            }

            Assert.Equal(expected.Corpses.Count, actual.Corpses.Count);
            for (var i = 0; i < expected.Corpses.Count; i++)
            {
                var left = expected.Corpses[i];
                var right = actual.Corpses[i];
                Assert.Equal(left.Id, right.Id);
                Assert.Equal(left.Position, right.Position);
                Assert.Equal(left.LootRange, right.LootRange);
                Assert.Equal(left.Silver, right.Silver);
                Assert.Equal(left.ExpireAtTick, right.ExpireAtTick);
                Assert.Equal(left.Loot.Count, right.Loot.Count);
                for (var j = 0; j < left.Loot.Count; j++)
                {
                    Assert.Equal(left.Loot[j].ItemId, right.Loot[j].ItemId);
                    Assert.Equal(left.Loot[j].Quantity, right.Loot[j].Quantity);
                }
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
