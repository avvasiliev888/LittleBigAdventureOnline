using System;
using System.Collections.Generic;
using System.Numerics;
using Client.Network;
using Server;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// M6/M7: клиентский конвейер целиком — подключение, ввод, снапшоты, отрисовка,
    /// предсказание движения и реконсиляция по ServerAck.
    /// Сервер и клиент в одном процессе, транспорт in-memory/симулированный, поэтому тест детерминирован.
    /// </summary>
    public sealed class ClientPipelineTests
    {
        [Fact]
        public void Pipeline_Connects_MovesAndRenders()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new InMemoryClientTransport(serverTransport, clientId);
            var view = new RecordingView();
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(1f, 0f)), view);

            uint entityId = 0;
            pipeline.SessionReady += id => entityId = id;
            pipeline.Start();

            for (var i = 0; i < 60; i++)
            {
                pipeline.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
                server.Tick();
            }

            Assert.True(pipeline.HasSession);
            Assert.Equal(1u, entityId);
            Assert.True(pipeline.SnapshotsApplied > 30, "снапшоты не дошли до представления");
            Assert.True(view.RenderCalls > 0);
            Assert.True(view.Buffer.LatestTick > 30);

            var entity = Assert.Single(view.Buffer.Sample(view.Buffer.LatestTick));
            Assert.True(entity.Position.X > 3f, "персонаж не сдвинулся от точки появления: x=" + entity.Position.X);
        }

        [Fact]
        public void Pipeline_RequestsFullSnapshot_WhenSnapshotsStop()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new InMemoryClientTransport(serverTransport, clientId);
            var view = new RecordingView();
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(0f, 0f)), view);

            pipeline.Start();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);
            server.Poll();
            server.Tick();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);
            Assert.Equal(1, pipeline.SnapshotsApplied);

            // Сервер перестал тикать: обычные снапшоты больше не приходят, но Poll отвечает на запросы.
            for (var i = 0; i < 40; i++)
            {
                pipeline.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
            }

            Assert.True(pipeline.SnapshotsApplied >= 2, "клиент не запросил полный снапшот после простоя");
        }

        [Fact]
        public void Pipeline_ReportsDisconnect_AndStopsSession()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new InMemoryClientTransport(serverTransport, clientId);
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(0f, 0f)), new RecordingView());

            var reason = string.Empty;
            pipeline.Disconnected += text => reason = text;

            pipeline.Start();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);
            server.Poll();
            server.Tick();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);
            Assert.True(pipeline.HasSession);

            transport.SimulateDisconnect("RemoteConnectionClose");
            pipeline.Update(ClientPipeline.TickIntervalSeconds);

            Assert.False(pipeline.HasSession);
            Assert.Equal("RemoteConnectionClose", reason);
        }

        [Fact]
        public void Pipeline_PredictsMovement_BeforeServerConfirms()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 0, delayTicks: 2, seed: 1, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(1f, 0f)), new RecordingView());
            pipeline.Start();

            // Hello → HelloAck → первые снапшоты (задержаны на 2 тика), затем движение.
            for (var i = 0; i < 20; i++)
            {
                Step(pipeline, server, ref tick);
            }

            Assert.NotNull(pipeline.Prediction);
            Assert.True(server.Simulation.State.TryGetEntity(pipeline.EntityId, out var entity));
            Assert.True(pipeline.Prediction!.Position.X > entity!.Position.X + 0.1f,
                "предсказание не опережает подтверждённое сервером состояние: prediction="
                + pipeline.Prediction.Position.X.ToString("0.###") + ", server=" + entity!.Position.X.ToString("0.###"));
        }

        [Fact]
        public void Pipeline_ConvergesWithDelayAndLoss()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 20, delayTicks: 2, seed: 2026, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            var input = new ScriptedInputSource(new Vector2(1f, 0f));
            using var pipeline = new ClientPipeline(transport, input, new RecordingView());
            pipeline.Start();

            for (var i = 0; i < 80; i++)
            {
                Step(pipeline, server, ref tick);
            }

            input.Direction = Vector2.Zero;

            for (var i = 0; i < 20; i++)
            {
                Step(pipeline, server, ref tick);
            }

            Assert.True(pipeline.SnapshotsApplied > 30, "слишком мало снапшотов для проверки сходимости");
            Assert.True(link.DroppedPackets > 0, "имитация потерь не сработала");
            Assert.NotNull(pipeline.Prediction);
            Assert.True(server.Simulation.State.TryGetEntity(pipeline.EntityId, out var entity));

            var error = Vector3.Distance(pipeline.Prediction!.Position, entity!.Position);
            Assert.True(error <= ClientPrediction.DefaultTolerance,
                "предсказание не сошлось за 20 тиков: error=" + error.ToString("0.####"));
        }

        [Fact]
        public void Pipeline_AppliesDeltas_AndRecoversGapsWithFullSnapshot()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 30, delayTicks: 2, seed: 2026, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            var view = new RecordingView();
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(1f, 0f)), view);
            pipeline.Start();

            for (var i = 0; i < 120; i++)
            {
                Step(pipeline, server, ref tick);
            }

            Assert.True(server.DeltasSent > 0, "сервер не отправил ни одной дельты");
            Assert.True(pipeline.DeltasApplied > 0, "клиент не применил ни одной дельты");
            Assert.True(pipeline.FullSnapshotRequests > 0, "при 30% потерь клиент ни разу не запросил полный снапшот");
            Assert.True(pipeline.SnapshotsApplied > 40, "слишком мало снапшотов для проверки восстановления");

            // Восстановление после потери базы: полный снапшот приходит по надёжному каналу,
            // поэтому отставание ограничено сетевой задержкой, а не растёт со временем.
            var lagTicks = server.Simulation.State.Tick - view.Buffer.LatestTick;
            Assert.InRange(lagTicks, 0u, 8u);
        }
        [Fact]
        public void Pipeline_UsesConfigForPredictionAndAbilityCooldown()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(cooldownSeconds: 2f), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new InMemoryClientTransport(serverTransport, clientId);
            var content = ClientContent.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "config"));
            var input = new SingleCommandInputSource();
            var view = new RecordingView();
            using var pipeline = new ClientPipeline(transport, input, view, content: content);
            pipeline.Start();

            for (var i = 0; i < 5; i++)
            {
                pipeline.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
                server.Tick();
            }

            Assert.NotNull(pipeline.Prediction);
            Assert.True(pipeline.HasSession);
            Assert.Equal(40, pipeline.Prediction!.AbilityCooldownTicks);
            Assert.Equal(3f, pipeline.Prediction.AbilityRange, 3);

            // Space → AbilityCommand по себе (единственная цель): клиент сразу рисует кулдаун из конфига.
            input.Command = new AbilityCommand(pipeline.EntityId, pipeline.ClientTick, 1);
            pipeline.Update(ClientPipeline.TickIntervalSeconds);

            Assert.True(pipeline.Prediction.CooldownRemainingTicks(pipeline.ClientTick) > 0,
                "предсказанный кулдаун не появился после команды способности");

            server.Poll();
            server.Tick();
            for (var i = 0; i < 10; i++)
            {
                pipeline.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
                server.Tick();
            }

            Assert.NotNull(pipeline.LastSnapshot);
            var own = Array.Find(pipeline.LastSnapshot!.Entities.ToArray(), entity => entity.Id == pipeline.EntityId);
            Assert.True(own.Hp < own.MaxHp, "сервер не применил способность: hp=" + own.Hp + "/" + own.MaxHp);
            Assert.True(pipeline.Prediction.CooldownRemainingTicks(pipeline.ClientTick) > 0);
        }

        [Fact]
        public void Pipeline_StableNetwork_DoesNotReconcile()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 0, delayTicks: 0, seed: 7, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(1f, 0f)), new RecordingView());
            pipeline.Start();

            for (var i = 0; i < 60; i++)
            {
                Step(pipeline, server, ref tick);
            }

            Assert.NotNull(pipeline.Prediction);
            Assert.Equal(0, pipeline.Prediction!.Reconciliations);
            Assert.True(server.Simulation.State.TryGetEntity(pipeline.EntityId, out var entity));
            Assert.True(Vector3.Distance(pipeline.Prediction.Position, entity!.Position) <= ClientPrediction.DefaultTolerance,
                "предсказание разошлось на стабильной сети");
        }

        [Fact]
        public void Pipeline_PushesPredictionSampleEveryTick_MonotonicWithDelayAndLoss()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 20, delayTicks: 2, seed: 2026, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            var view = new PredictedRecordingView();
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(new Vector2(1f, 0f)), view);
            pipeline.Start();

            for (var i = 0; i < 120; i++)
            {
                Step(pipeline, server, ref tick);
            }

            // Сэмпл каждый клиентский тик: ровный тайминг независимо от джиттера и потерь сети.
            Assert.True(view.Samples.Count >= 115, "сэмплов предсказания слишком мало: " + view.Samples.Count);

            var previous = float.NegativeInfinity;
            foreach (var sample in view.Samples)
            {
                Assert.True(sample.X >= previous - 0.001f,
                    "сэмплы предсказания пошли назад: " + previous.ToString("0.###") + " -> " + sample.X.ToString("0.###"));
                previous = sample.X;
            }

            Assert.True(view.Samples[view.Samples.Count - 1].X > 15f, "предсказание не двигалось");
        }

        [Fact]
        public void Pipeline_LogsHelloAndSessionState()
        {
            var tick = 0u;
            var link = new SimulatedLink(lossPercent: 0, delayTicks: 0, seed: 3, currentTick: () => tick);
            using var server = new GameServer(TestWorld.Config(), link, new ServerOptions());
            server.Start();

            var clientId = link.ConnectClient();
            using var transport = new LinkClientTransport(link, clientId);
            using var pipeline = new ClientPipeline(transport, new ScriptedInputSource(Vector2.Zero), new RecordingView());
            pipeline.Start();

            for (var i = 0; i < 5; i++)
            {
                Step(pipeline, server, ref tick);
            }

            Assert.Contains(pipeline.LogEntries, entry => entry.StartsWith("[hello]", StringComparison.Ordinal));
        }

        private static void Step(ClientPipeline pipeline, GameServer server, ref uint tick)
        {
            pipeline.Update(ClientPipeline.TickIntervalSeconds);
            server.Poll();
            server.Tick();
            tick++;
        }

        private sealed class InMemoryClientTransport : ITransport
        {
            private readonly InMemoryServerTransport _server;
            private readonly int _connectionId;

            public InMemoryClientTransport(InMemoryServerTransport server, int connectionId)
            {
                _server = server;
                _connectionId = connectionId;
            }

            public event Action<byte[]>? PacketReceived;

            public event Action<string>? Disconnected;

            public bool IsConnected { get; private set; } = true;

            public void Start()
            {
            }

            public void Poll()
            {
                while (_server.TryReceive(_connectionId, out var packet) && packet != null)
                {
                    PacketReceived?.Invoke(packet);
                }
            }

            public void Send(byte[] packet, DeliveryMode mode)
            {
                _server.SendFromClient(_connectionId, packet);
            }

            public void Dispose()
            {
            }

            public void SimulateDisconnect(string reason)
            {
                IsConnected = false;
                Disconnected?.Invoke(reason);
            }
        }

        private sealed class LinkClientTransport : ITransport
        {
            private readonly SimulatedLink _link;
            private readonly int _connectionId;

            public LinkClientTransport(SimulatedLink link, int connectionId)
            {
                _link = link;
                _connectionId = connectionId;
            }

            public event Action<byte[]>? PacketReceived;

            public event Action<string>? Disconnected;

            public bool IsConnected { get; private set; } = true;

            public void Start()
            {
            }

            public void Poll()
            {
                while (_link.TryReceive(_connectionId, out var packet) && packet != null)
                {
                    PacketReceived?.Invoke(packet);
                }
            }

            public void Send(byte[] packet, DeliveryMode mode)
            {
                _link.SendFromClient(_connectionId, packet);
            }

            public void Dispose()
            {
            }

            public void SimulateDisconnect(string reason)
            {
                IsConnected = false;
                Disconnected?.Invoke(reason);
            }
        }

        private sealed class SingleCommandInputSource : IInputSource
        {
            public ICommand? Command { get; set; }

            public bool TryReadCommand(uint clientTick, out ICommand? command)
            {
                command = Command;
                Command = null;
                return command != null;
            }
        }

        private sealed class ScriptedInputSource : IInputSource
        {
            private uint _sequence;

            public ScriptedInputSource(Vector2 direction)
            {
                Direction = direction;
            }

            public Vector2 Direction { get; set; }

            public bool TryReadCommand(uint clientTick, out ICommand? command)
            {
                if (Direction == Vector2.Zero)
                {
                    command = null;
                    return false;
                }

                command = new MoveCommand(Direction, clientTick, ++_sequence);
                return true;
            }
        }

        private sealed class PredictedRecordingView : IPredictedView
        {
            public List<Vector3> Samples { get; } = new List<Vector3>();

            public void SetPredictedPlayerPosition(uint tick, Vector3 position, Vector3 velocity, bool reconciled)
            {
                Samples.Add(position);
            }

            public void ApplySnapshot(SimulationSnapshot snapshot)
            {
            }

            public void Render(float frameDeltaTime)
            {
            }
        }

        private sealed class RecordingView : IView
        {
            public SnapshotBuffer Buffer { get; } = new SnapshotBuffer();

            public int RenderCalls { get; private set; }

            public void ApplySnapshot(SimulationSnapshot snapshot)
            {
                Buffer.Add(snapshot);
            }

            public void Render(float frameDeltaTime)
            {
                RenderCalls++;
            }
        }
    }
}
