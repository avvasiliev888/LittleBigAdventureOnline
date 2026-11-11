using System;
using System.Collections.Generic;
using System.Numerics;
using Client.Network;
using Simulation.Core;

namespace Tests
{
    /// <summary>
    /// Серверные коллизии: статические препятствия, взаимное выталкивание игроков и скольжение.
    /// </summary>
    public sealed class CollisionTests
    {
        private static readonly WorldColliders RockAtFive = new WorldColliders(new[]
        {
            new WorldCollider("test-rock", 5f, 0f, 1f, "rock"),
        });

        [Fact]
        public void Rock_CannotBePassedThrough()
        {
            var start = new Vector3(0f, 0f, 0f);
            var desired = new Vector3(10f, 0f, 0f);

            var resolved = MovementCollision.Resolve(start, desired, 0.45f, null, RockAtFive);

            // Игрок останавливается у поверхности: 5 - 1 - 0.45 = 3.55, а не оказывается за камнем.
            Assert.True(resolved.X < 3.6f, "Игрок прошёл сквозь камень: x=" + resolved.X);
            Assert.True(Vector3.Distance(resolved, new Vector3(5f, 0f, 0f)) >= 1.449f);
        }

        [Fact]
        public void TwoPlayers_CannotOccupySamePoint()
        {
            var config = TestWorld.Config();
            var first = TestWorld.Entity(1, config, 0f, 0f);
            var second = TestWorld.Entity(2, config, 0f, 0f);
            var simulation = TestWorld.Simulation(config, first, second);

            simulation.Tick(0.05f, Array.Empty<CommandEnvelope>());

            var distance = Vector3.Distance(first.Position, second.Position);
            Assert.True(distance >= 0.9f, "Игроки остались в одной точке: " + distance);
        }

        [Fact]
        public void MovingPlayer_DoesNotPassThroughAnotherPlayer()
        {
            var config = TestWorld.Config(moveSpeed: 4f);
            var mover = TestWorld.Entity(1, config, -2f, 0f);
            var blocker = TestWorld.Entity(2, config, 0f, 0f);
            var simulation = TestWorld.Simulation(config, mover, blocker);

            for (var tick = 0; tick < 20; tick++)
            {
                simulation.Tick(0.05f, new[]
                {
                    TestWorld.Move(1, 1f, 0f, (uint)tick, (uint)tick),
                });
            }

            Assert.True(mover.Position.X <= -0.89f, "Игрок прошёл сквозь другого: x=" + mover.Position.X);
        }

        [Fact]
        public void DiagonalMovement_SlidesAlongRockInsteadOfStopping()
        {
            var start = new Vector3(0f, 0f, 0f);
            var desired = new Vector3(10f, 0f, 1f);

            var resolved = MovementCollision.Resolve(start, desired, 0.45f, null, RockAtFive);

            var distance = Vector2.Distance(new Vector2(resolved.X, resolved.Z), new Vector2(5f, 0f));
            Assert.True(distance >= 1.449f, "Игрок оказался внутри камня: " + distance);
            Assert.True(resolved.X > 1f, "Скольжение не продвинуло игрока вперёд: x=" + resolved.X);
            Assert.True(resolved.Z > 0.05f, "Скольжение потеряло касательную составляющую: z=" + resolved.Z);
        }

        [Fact]
        public void DiagonalMovement_SlidesAlongZoneWall()
        {
            var blue = new ZoneConfig(1, "blue", ZoneKind.Blue, 0f, 0f, 10f, 10f, false, false, 0.1f, 0f, 1, 2,
                new[] { new ZoneGate(2, 10f, 5f, 2f) });
            var yellow = new ZoneConfig(2, "yellow", ZoneKind.Yellow, 10f, 0f, 20f, 10f, true, false, 0.1f, 60f, 1, 2);
            var zones = new ZoneMap(new[] { blue, yellow });

            var resolved = MovementCollision.Resolve(
                new Vector3(9f, 0f, 1f),
                new Vector3(11f, 0f, 2f),
                0.45f,
                zones,
                null);

            Assert.Equal(9f, resolved.X, 3);
            Assert.Equal(2f, resolved.Z, 3);
        }

        [Fact]
        public void ClientPrediction_UsesSameStaticCollision()
        {
            var prediction = new ClientPrediction(1, 4f, zones: null, worldColliders: RockAtFive);
            prediction.Reset(new Vector3(0f, 0f, 0f));

            for (var i = 0; i < 10; i++)
            {
                prediction.ApplyLocalCommand(new MoveCommand(new Vector2(1f, 0f), (uint)i, (uint)i));
            }

            Assert.True(prediction.Position.X < 3.61f, "Предсказание прошло сквозь камень: x=" + prediction.Position.X);
        }
    }
}
