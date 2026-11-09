using System;
using Server;
using Xunit;

namespace Tests
{
    /// <summary>Фейковые часы: <see cref="Sleep(int)"/> двигает время вперёд без реальных пауз.</summary>
    internal sealed class FakeClock : IClock
    {
        public long ElapsedMilliseconds { get; private set; }

        public int SleepCalls { get; private set; }

        public void Sleep(int milliseconds)
        {
            SleepCalls++;
            if (milliseconds > 0)
            {
                ElapsedMilliseconds += milliseconds;
            }
        }
    }

    /// <summary>Серверный цикл (M4): стабильный тик 20 Гц без накопления дрейфа.</summary>
    public sealed class ServerLoopTests
    {
        [Fact]
        public void HundredTicks_TakeFiveSecondsWithinTolerance()
        {
            var (server, transport) = CreateServer();
            using (transport)
            using (server)
            {
                var clock = new FakeClock();
                var loop = new ServerLoop(server, clock, tickRate: 20);

                // Первый тик выполняется сразу, поэтому 101 тик = 100 интервалов по 50 мс.
                for (var i = 0; i < 101; i++)
                {
                    loop.RunOnce();
                }

                Assert.Equal(101, loop.TicksExecuted);
                Assert.InRange(clock.ElapsedMilliseconds, 4900, 5100);
                Assert.Equal(50, loop.TickIntervalMs);
            }
        }

        [Fact]
        public void Loop_TicksSimulationEveryRun()
        {
            var (server, transport) = CreateServer();
            using (transport)
            using (server)
            {
                var loop = new ServerLoop(server, new FakeClock(), tickRate: 20);

                loop.RunOnce();
                loop.RunOnce();
                loop.RunOnce();

                Assert.Equal(3u, server.Simulation.State.Tick);
            }
        }

        [Fact]
        public void Loop_UsesProvidedTickRate()
        {
            var (server, transport) = CreateServer();
            using (transport)
            using (server)
            {
                var loop = new ServerLoop(server, new FakeClock(), tickRate: 10);

                Assert.Equal(100, loop.TickIntervalMs);
            }
        }

        [Fact]
        public void Loop_RejectsInvalidTickRate()
        {
            var (server, transport) = CreateServer();
            using (transport)
            using (server)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new ServerLoop(server, new FakeClock(), tickRate: 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => new ServerLoop(server, new FakeClock(), tickRate: 1001));
            }
        }

        [Fact]
        public void Loop_WithNullArguments_Throws()
        {
            var (server, transport) = CreateServer();
            using (transport)
            using (server)
            {
                Assert.Throws<ArgumentNullException>(() => new ServerLoop(null!, new FakeClock()));
                Assert.Throws<ArgumentNullException>(() => new ServerLoop(server, null!));
            }
        }

        private static (GameServer Server, InMemoryServerTransport Transport) CreateServer()
        {
            var transport = new InMemoryServerTransport();
            var server = new GameServer(TestWorld.Config(), transport, new ServerOptions());
            return (server, transport);
        }
    }
}
