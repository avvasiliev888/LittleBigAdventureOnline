using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Проходы между зонами (ADR-013): границу с объявленным проходом нельзя пересечь вне него,
    /// и клиентское предсказание шагает по тому же правилу, что серверная симуляция (ADR-006).
    /// </summary>
    public sealed class ZoneGateTests
    {
        [Fact]
        public void CrossingOutsideGate_IsBlockedAndSlidesAlongBorder()
        {
            var map = Map();
            var result = map.ResolveMovement(new Vector3(9f, 0f, 1f), new Vector3(11f, 0f, 1f));

            Assert.Equal(9f, result.X, 3);
            Assert.Equal(1f, result.Z, 3);
            Assert.Equal("blue", map.GetZone(result).Key);
        }

        [Fact]
        public void CrossingThroughGate_IsAllowed()
        {
            var map = Map();
            var result = map.ResolveMovement(new Vector3(9f, 0f, 5f), new Vector3(11f, 0f, 5f));

            Assert.Equal(11f, result.X, 3);
            Assert.Equal("yellow", map.GetZone(result).Key);
        }

        [Fact]
        public void LongStepThroughGate_IsAllowed()
        {
            var map = Map();
            var result = map.ResolveMovement(new Vector3(4f, 0f, 5f), new Vector3(16f, 0f, 5f));

            Assert.Equal(16f, result.X, 3);
            Assert.Equal("yellow", map.GetZone(result).Key);
        }

        [Fact]
        public void BlockedDiagonalStep_KeepsSafeXAndLetsPlayerSlide()
        {
            var map = Map();
            var result = map.ResolveMovement(new Vector3(9f, 0f, 1f), new Vector3(11f, 0f, 2f));

            Assert.Equal(9f, result.X, 3);
            Assert.Equal(2f, result.Z, 3);
        }

        [Fact]
        public void BorderWithoutGates_RemainsOpenForTestArenas()
        {
            var map = new ZoneMap(new[]
            {
                Zone(1, "a", 0f, 10f),
                Zone(2, "b", 10f, 20f),
            });

            var result = map.ResolveMovement(new Vector3(9f, 0f, 5f), new Vector3(11f, 0f, 5f));

            Assert.Equal(11f, result.X, 3);
        }

        [Fact]
        public void WorldEdge_ClampsWithoutCrossingAnyZone()
        {
            var map = Map();
            var result = map.ResolveMovement(new Vector3(1f, 0f, 1f), new Vector3(-5f, 0f, 1f));

            Assert.Equal(0f, result.X, 3);
            Assert.Equal("blue", map.GetZone(result).Key);
        }

        [Fact]
        public void GateOffSharedBorder_IsRejected()
        {
            var bad = new ZoneConfig(1, "blue", ZoneKind.Blue, 0f, 0f, 10f, 10f, false, false, 0.1f, 0f, 1, 2,
                new[] { new ZoneGate(2, 8f, 5f, 2f) });

            Assert.Throws<ArgumentException>(() => new ZoneMap(new[] { bad, Zone(2, "yellow", 10f, 20f) }));
        }

        [Fact]
        public void GateToMissingZone_IsRejected()
        {
            var zone = Zone(1, "blue", 0f, 10f, new[] { new ZoneGate(9, 10f, 5f, 2f) });
            var other = Zone(2, "yellow", 10f, 20f);

            Assert.Throws<ArgumentException>(() => new ZoneMap(new[] { zone, other }));
        }

        [Fact]
        public void GateWiderThanSharedBorder_IsRejected()
        {
            var zone = Zone(1, "blue", 0f, 10f, new[] { new ZoneGate(2, 10f, 1f, 5f) });
            var other = Zone(2, "yellow", 10f, 20f);

            Assert.Throws<ArgumentException>(() => new ZoneMap(new[] { zone, other }));
        }

        [Fact]
        public void Prediction_UsesSameGateRuleAsServer()
        {
            var zones = Map();
            var blocked = new ClientPrediction(1, 4f, zones: zones);
            blocked.Reset(new Vector3(9.9f, 0f, 1f));
            blocked.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), 0, 1));
            blocked.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), 0, 2));
            Assert.Equal(9.9f, blocked.Position.X, 3);

            var through = new ClientPrediction(1, 4f, zones: zones);
            through.Reset(new Vector3(9.9f, 0f, 5f));
            through.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), 0, 1));
            through.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), 0, 2));
            Assert.Equal(10.3f, through.Position.X, 3);
        }

        private static ZoneConfig Zone(uint id, string key, float minX, float maxX, ZoneGate[]? gates = null)
        {
            return new ZoneConfig(id, key, ZoneKind.Yellow, minX, 0f, maxX, 10f, true, false, 0.1f, 60f, 1, 2, gates);
        }

        private static ZoneMap Map()
        {
            var blue = new ZoneConfig(1, "blue", ZoneKind.Blue, 0f, 0f, 10f, 10f, false, false, 0.1f, 0f, 1, 2,
                new[] { new ZoneGate(2, 10f, 5f, 2f) });
            var yellow = Zone(2, "yellow", 10f, 20f);
            return new ZoneMap(new[] { blue, yellow });
        }
    }
}
