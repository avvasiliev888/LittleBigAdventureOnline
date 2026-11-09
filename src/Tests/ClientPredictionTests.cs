using System;
using System.Numerics;
using Client.Network;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M7: предсказание движения и реконсиляция. Юнит-уровень — без сети и Unity,
    /// чтобы точно проверить правило «сервер + неподтверждённые команды», а не «телепорт к серверу».
    /// </summary>
    public sealed class ClientPredictionTests
    {
        private const float Speed = 4f;
        private const float Step = Speed * ClientPrediction.TickDelta;

        [Fact]
        public void ApplyLocalCommand_MovesBySpeedPerTick()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(Vector3.Zero);

            prediction.ApplyLocalCommand(Move(1f, 0f, 1));

            Assert.Equal(Step, prediction.Position.X, 4);
            Assert.Single(prediction.Unacknowledged);
            Assert.Equal(0, prediction.Reconciliations);
        }

        [Fact]
        public void Reconcile_WhenServerMatchesPrediction_DoesNotCountReconciliation()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(Vector3.Zero);
            prediction.ApplyLocalCommand(Move(1f, 0f, 1));
            prediction.ApplyLocalCommand(Move(1f, 0f, 2));

            var corrected = prediction.Reconcile(Snapshot(2, Step * 2f), acknowledgedSequence: 2);

            Assert.False(corrected);
            Assert.Equal(0, prediction.Reconciliations);
            Assert.Empty(prediction.Unacknowledged);
        }

        [Fact]
        public void Reconcile_DeliberateDesync_ConvergesToServer()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(new Vector3(5f, 0f, 0f));

            var corrected = prediction.Reconcile(Snapshot(7, Step), acknowledgedSequence: 0);

            Assert.True(corrected);
            Assert.Equal(1, prediction.Reconciliations);
            Assert.Equal(Step, prediction.Position.X, 4);
            Assert.True(prediction.LastError > 4f);
        }

        [Fact]
        public void Reconcile_ReappliesUnacknowledgedCommands_NotTeleportsToServer()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(Vector3.Zero);
            prediction.ApplyLocalCommand(Move(1f, 0f, 1));
            prediction.ApplyLocalCommand(Move(1f, 0f, 2));
            prediction.ApplyLocalCommand(Move(1f, 0f, 3));

            // Сервер подтвердил только первую команду, но клиент знает про две неподтверждённые.
            var corrected = prediction.Reconcile(Snapshot(10, Step), acknowledgedSequence: 1);

            Assert.False(corrected);
            Assert.Equal(Step * 3f, prediction.Position.X, 4);
            Assert.NotEqual(Step, prediction.Position.X, 4);
            Assert.Equal(2, prediction.Unacknowledged.Count);
        }

        [Fact]
        public void Reconcile_AfterDesync_EndsAtPredictedPositionNotServerPosition()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(Vector3.Zero);
            prediction.ApplyLocalCommand(Move(1f, 0f, 1));
            prediction.ApplyLocalCommand(Move(1f, 0f, 2));
            prediction.ApplyLocalCommand(Move(1f, 0f, 3));

            // Клиент «сломался» и уехал далеко; на сервере только первая команда.
            prediction.Reset(new Vector3(20f, 0f, 0f));
            prediction.ApplyLocalCommand(Move(1f, 0f, 4));
            prediction.ApplyLocalCommand(Move(1f, 0f, 5));

            var corrected = prediction.Reconcile(Snapshot(10, Step), acknowledgedSequence: 1);

            Assert.True(corrected);
            Assert.Equal(1, prediction.Reconciliations);
            Assert.Equal(Step * 3f, prediction.Position.X, 4);
            Assert.NotEqual(Step, prediction.Position.X, 4);
        }

        [Fact]
        public void Reconcile_IgnoresSnapshotWithoutOwnEntity()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(new Vector3(3f, 0f, 0f));

            var snapshot = new SimulationSnapshot(5, new[]
            {
                new EntitySnapshot(99, 1, Vector3.Zero, Vector3.Zero, 100, 100, 0, 0, 0, 0, false),
            });

            Assert.False(prediction.Reconcile(snapshot, 0));
            Assert.Equal(3f, prediction.Position.X, 4);
        }

        [Fact]
        public void Reconcile_IgnoresDivergenceBelowTolerance()
        {
            var prediction = new ClientPrediction(1, Speed, tolerance: 0.05f);
            prediction.Reset(new Vector3(1f, 0f, 0f));

            var corrected = prediction.Reconcile(Snapshot(2, 1.02f), acknowledgedSequence: 0);

            Assert.False(corrected);
            Assert.Equal(0, prediction.Reconciliations);
            Assert.Equal(1f, prediction.Position.X, 4);
        }

        [Fact]
        public void Reconcile_RemovesAcknowledgedCommandsOnly()
        {
            var prediction = new ClientPrediction(1, Speed);
            prediction.Reset(Vector3.Zero);
            prediction.ApplyLocalCommand(Move(1f, 0f, 1));
            prediction.ApplyLocalCommand(Move(1f, 0f, 2));
            prediction.ApplyLocalCommand(Move(1f, 0f, 3));

            prediction.Reconcile(Snapshot(3, Step * 2f), acknowledgedSequence: 2);

            var remaining = Assert.Single(prediction.Unacknowledged);
            Assert.Equal(3u, remaining.Sequence);
        }

        private static MoveCommand Move(float x, float z, uint sequence)
        {
            return new MoveCommand(new Vector2(x, z), sequence, sequence);
        }

        private static SimulationSnapshot Snapshot(uint tick, float x)
        {
            return new SimulationSnapshot(tick, new[]
            {
                new EntitySnapshot(1, 1, new Vector3(x, 0f, 0f), Vector3.Zero, 100, 100, 0, 0, 0, 0, false),
            });
        }
    }
}
