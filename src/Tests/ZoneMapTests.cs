using System;
using System.Numerics;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Карта зон (ADR-013): покрытие мира, запрет пересечений, поиск зоны и границы мира.
    /// </summary>
    public sealed class ZoneMapTests
    {
        [Fact]
        public void ThreeZones_CoverWorldAndResolveByPosition()
        {
            var map = new ZoneMap(ZoneFixture.ThreeZones());

            Assert.Equal(ZoneKind.Blue, map.GetZone(new Vector3(ZoneFixture.BlueX, 0f, 0f)).Kind);
            Assert.Equal(ZoneKind.Yellow, map.GetZone(new Vector3(ZoneFixture.YellowX, 0f, 0f)).Kind);
            Assert.Equal(ZoneKind.Red, map.GetZone(new Vector3(ZoneFixture.RedX, 0f, 0f)).Kind);

            Assert.Equal(-30f, map.MinX);
            Assert.Equal(20f, map.MaxX);
            Assert.Equal(-30f, map.MinZ);
            Assert.Equal(30f, map.MaxZ);
        }

        [Fact]
        public void GetZone_SharedBoundary_BelongsToEarlierZone()
        {
            var map = new ZoneMap(ZoneFixture.ThreeZones());

            // На стыке зон точка принадлежит зоне, объявленной раньше в конфиге (документировано
            // в ZoneMap.TryGetZone): так граница детерминирована и не зависит от порядка обхода.
            Assert.Equal(ZoneKind.Blue, map.GetZone(new Vector3(0f, 0f, 0f)).Kind);
            Assert.Equal(ZoneKind.Yellow, map.GetZone(new Vector3(10f, 0f, 0f)).Kind);
            Assert.Equal(ZoneKind.Red, map.GetZone(new Vector3(10.001f, 0f, 0f)).Kind);
        }

        [Fact]
        public void OverlappingZones_Throw()
        {
            var zones = new[]
            {
                new ZoneConfig(1, "a", ZoneKind.Yellow, 0f, 0f, 10f, 10f, true, false, 0.1f, 60f, 1, 2),
                new ZoneConfig(2, "b", ZoneKind.Yellow, 5f, 5f, 15f, 15f, true, false, 0.1f, 60f, 1, 2),
            };

            var exception = Assert.Throws<ArgumentException>(() => new ZoneMap(zones));

            Assert.Contains("пересекаются", exception.Message);
        }

        [Fact]
        public void ZonesWithGap_Throw()
        {
            var zones = new[]
            {
                new ZoneConfig(1, "a", ZoneKind.Yellow, 0f, 0f, 10f, 10f, true, false, 0.1f, 60f, 1, 2),
                new ZoneConfig(2, "b", ZoneKind.Yellow, 11f, 0f, 20f, 10f, true, false, 0.1f, 60f, 1, 2),
            };

            var exception = Assert.Throws<ArgumentException>(() => new ZoneMap(zones));

            Assert.Contains("не покрывают мир", exception.Message);
        }

        [Fact]
        public void SliverGapBetweenZones_Throws()
        {
            var zones = new[]
            {
                new ZoneConfig(1, "a", ZoneKind.Yellow, 0f, 0f, 10f, 20f, true, false, 0.1f, 60f, 1, 2),
                new ZoneConfig(2, "b", ZoneKind.Yellow, 10.0001f, 0f, 20f, 20f, true, false, 0.1f, 60f, 1, 2),
            };

            var exception = Assert.Throws<ArgumentException>(() => new ZoneMap(zones));

            Assert.Contains("не покрывают мир", exception.Message);
        }

        [Fact]
        public void StaggeredZones_CoveredByTwoZonesInStrip()
        {
            var zones = new[]
            {
                new ZoneConfig(1, "a", ZoneKind.Blue, 0f, 0f, 10f, 20f, false, false, 0.1f, 0f, 1, 2),
                new ZoneConfig(2, "b", ZoneKind.Yellow, 10f, 0f, 20f, 10f, true, false, 0.1f, 60f, 1, 2),
                new ZoneConfig(3, "c", ZoneKind.Red, 10f, 10f, 20f, 20f, true, false, 0f, 90f, 3, 4),
            };

            var map = new ZoneMap(zones);

            Assert.Equal(20f, map.MaxX);
            Assert.Equal(20f, map.MaxZ);
        }


        [Fact]
        public void DuplicateZoneId_Throws()
        {
            var zones = new[]
            {
                new ZoneConfig(1, "a", ZoneKind.Blue, -10f, -10f, 0f, 10f, false, false, 0.1f, 0f, 1, 2),
                new ZoneConfig(1, "b", ZoneKind.Yellow, 0f, -10f, 10f, 10f, true, false, 0.1f, 60f, 1, 2),
            };

            var exception = Assert.Throws<ArgumentException>(() => new ZoneMap(zones));

            Assert.Contains("Дубликат id", exception.Message);
        }

        [Fact]
        public void EmptyZoneList_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ZoneMap(Array.Empty<ZoneConfig>()));
        }

        [Fact]
        public void ZoneRules_AreExposedFromConfig()
        {
            var map = new ZoneMap(ZoneFixture.ThreeZones());
            var red = map.GetZoneById(3);

            Assert.True(red.PvpEnabled);
            Assert.True(red.FullLootOnDeath);
            Assert.Equal(90f, red.HostileFlagSeconds);
            Assert.Equal(3, red.ResourceTierMin);
            Assert.Equal(4, red.ResourceTierMax);
        }

        [Fact]
        public void OutsideWorld_IsNotAZone_AndIsClampedBack()
        {
            var map = new ZoneMap(ZoneFixture.ThreeZones());

            Assert.False(map.TryGetZone(new Vector3(100f, 0f, 0f), out var zone));
            Assert.Null(zone);
            Assert.Throws<InvalidOperationException>(() => map.GetZone(new Vector3(100f, 0f, 0f)));

            var clamped = map.ClampToBounds(new Vector3(100f, 7f, -100f));

            Assert.Equal(20f, clamped.X);
            Assert.Equal(7f, clamped.Y);
            Assert.Equal(-30f, clamped.Z);
        }

        [Fact]
        public void ZoneConfig_RejectsContradictingRules()
        {
            // Полный лут без PvP — противоречие: некому убивать.
            Assert.Throws<ArgumentException>(
                () => new ZoneConfig(1, "x", ZoneKind.Blue, 0f, 0f, 10f, 10f, false, true, 0.1f, 0f, 1, 2));

            // PvP без длительности флага: атакующий не станет целью.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneConfig(1, "x", ZoneKind.Red, 0f, 0f, 10f, 10f, true, false, 0.1f, 0f, 1, 2));

            // Доля потери прочности вне 0..1.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZoneConfig(1, "x", ZoneKind.Red, 0f, 0f, 10f, 10f, true, false, 1.5f, 60f, 1, 2));
        }
    }
}
