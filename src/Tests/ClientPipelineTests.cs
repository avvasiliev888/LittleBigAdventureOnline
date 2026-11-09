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
    /// M6 Smoke-тест: клиентский конвейер целиком — подключение, Hello, ввод, снапшоты, отрисовка.
    /// Сервер и клиент в одном процессе, транспорт in-memory, поэтому тест детерминирован.
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

        private sealed class ScriptedInputSource : IInputSource
        {
            private readonly Vector2 _direction;
            private uint _sequence;

            public ScriptedInputSource(Vector2 direction)
            {
                _direction = direction;
            }

            public bool TryReadCommand(uint clientTick, out ICommand? command)
            {
                if (_direction == Vector2.Zero)
                {
                    command = null;
                    return false;
                }

                command = new MoveCommand(_direction, clientTick, ++_sequence);
                return true;
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
