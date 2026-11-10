using System;
using Server;
using Xunit;

namespace Tests
{
    /// <summary>Метрики тика (M11): окно, перцентили и лог сервера.</summary>
    public sealed class ServerMetricsTests
    {
        [Fact]
        public void Snapshot_ComputesAverageMedianP99AndMax()
        {
            var metrics = new ServerMetrics(8);
            foreach (var value in new[] { 1d, 2d, 3d, 4d })
            {
                metrics.Record(value);
            }

            var snapshot = metrics.Snapshot();

            Assert.Equal(4, snapshot.Count);
            Assert.Equal(2.5d, snapshot.AverageMs, precision: 6);
            Assert.Equal(2d, snapshot.P50Ms, precision: 6);
            Assert.Equal(4d, snapshot.P99Ms, precision: 6);
            Assert.Equal(4d, snapshot.MaxMs, precision: 6);
            Assert.Contains("n=4", metrics.Format("tick"));
            Assert.Contains("avg=2.5ms", metrics.Format("tick"));
        }

        [Fact]
        public void RingBuffer_DropsOldestSamples()
        {
            var metrics = new ServerMetrics(3);
            foreach (var value in new[] { 10d, 20d, 30d, 40d })
            {
                metrics.Record(value);
            }

            var snapshot = metrics.Snapshot();

            Assert.Equal(3, snapshot.Count);
            Assert.Equal(30d, snapshot.AverageMs, precision: 6);
            Assert.Equal(40d, snapshot.MaxMs, precision: 6);
        }

        [Fact]
        public void EmptyMetrics_AreZeroAndDoNotThrow()
        {
            var metrics = new ServerMetrics();
            var snapshot = metrics.Snapshot();

            Assert.Equal(0, snapshot.Count);
            Assert.Contains("n=0", metrics.Format());
            metrics.Reset();
            Assert.Equal(0, metrics.SampleCount);
        }

        [Fact]
        public void InvalidSamplesAndCapacity_AreRejected()
        {
            var metrics = new ServerMetrics(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Record(-1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Record(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Record(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ServerMetrics(0));
        }

        [Fact]
        public void GameServer_LogsMetricsEveryConfiguredTick()
        {
            using var transport = new InMemoryServerTransport();
            using var server = new GameServer(
                TestWorld.Config(),
                transport,
                new ServerOptions { LogTickMetricsEvery = 2 });

            server.Tick();
            server.Tick();

            Assert.Equal(2, server.Metrics.SampleCount);
            Assert.Contains(server.LogEntries, entry => entry.Contains("tick metrics:", StringComparison.Ordinal));
            Assert.Contains(server.LogEntries, entry => entry.Contains("n=2", StringComparison.Ordinal));

            server.LogMetrics("tick final");
            Assert.Contains(server.LogEntries, entry => entry.Contains("tick final metrics:", StringComparison.Ordinal));
        }

        [Fact]
        public void GameServer_RejectsNegativeMetricsPeriod()
        {
            using var transport = new InMemoryServerTransport();
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameServer(
                TestWorld.Config(),
                transport,
                new ServerOptions { LogTickMetricsEvery = -1 }));
        }
    }
}
