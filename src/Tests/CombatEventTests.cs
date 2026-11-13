using System;
using System.Collections.Generic;
using Client.Network;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Protocol v11: сервер рассылает <see cref="MessageType.CombatEvent"/>, чтобы клиенты
    /// показывали замах и снаряд у чужих сущностей. Снапшот при этом не меняется.
    /// </summary>
    public sealed class CombatEventTests
    {
        private const uint StarterSwordId = 10;
        private const uint BowId = 14;

        [Fact]
        public void CombatEventCodec_RoundTripsAllStyles()
        {
            foreach (var style in new[] { CombatStyle.Melee, CombatStyle.Arrow, CombatStyle.Magic })
            {
                var payload = CombatEventCodec.Encode(new CombatEvent(7, 9, 3, 1234, style));
                Assert.Equal(CombatEventCodec.Size, payload.Length);
                Assert.True(CombatEventCodec.TryDecode(payload, out var decoded));
                Assert.Equal(7u, decoded.AttackerEntityId);
                Assert.Equal(9u, decoded.TargetEntityId);
                Assert.Equal(3u, decoded.AbilityId);
                Assert.Equal(1234u, decoded.Tick);
                Assert.Equal(style, decoded.Style);
            }
        }

        [Fact]
        public void CombatEventCodec_RejectsWrongLengthZeroAttackerAndUnknownStyle()
        {
            var payload = CombatEventCodec.Encode(new CombatEvent(1, 0, 1, 1, CombatStyle.Melee));
            Assert.False(CombatEventCodec.TryDecode(payload.AsSpan(0, payload.Length - 1), out _));
            Assert.False(CombatEventCodec.TryDecode(new byte[CombatEventCodec.Size], out _));

            var badStyle = (byte[])payload.Clone();
            badStyle[16] = 9;
            Assert.False(CombatEventCodec.TryDecode(badStyle, out _));
        }

        [Fact]
        public void RemoteAttack_BroadcastsArrowAndMeleeStyles()
        {
            var config = TestWorld.ServerConfig();
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(
                config,
                serverTransport,
                new ServerOptions { SpawnTrainingDummy = true });
            server.Start();

            // Наблюдатель: только слушает, сам не атакует.
            var observerId = serverTransport.ConnectClient();
            using var observerTransport = new TestClientTransport(serverTransport, observerId);
            using var observer = new ClientPipeline(observerTransport, new IdleInputSource(), new NullView());
            observer.Start();

            var seen = new List<CombatEvent>();
            observer.CombatEventReceived += seen.Add;

            // Атакующий игрок.
            var attackerId = serverTransport.ConnectClient();
            using var attackerTransport = new TestClientTransport(serverTransport, attackerId);
            var attackerInput = new ScriptedInputSource();
            using var attacker = new ClientPipeline(attackerTransport, attackerInput, new NullView());
            attacker.Start();
            Pump(observer, attacker, server, 6);

            Assert.True(observer.HasSession);
            Assert.True(attacker.HasSession);
            Assert.True(server.Simulation.State.TryGetEntity(attacker.EntityId, out var entity));
            Assert.NotNull(entity);

            // Стартовый меч: сервер должен прислать стиль Melee.
            attackerInput.QueueAbility(targetEntityId: 0);
            Pump(observer, attacker, server, 5);

            Assert.Contains(seen, evt => evt.Style == CombatStyle.Melee && evt.AttackerEntityId == attacker.EntityId);

            // Лук: ждём кулдаун, надеваем лук из инвентаря и получаем стиль Arrow.
            seen.Clear();
            Assert.True(entity!.Inventory.Add(config.GetItem(BowId), 1));
            attackerInput.QueueEquip(BowId);
            Pump(observer, attacker, server, 45);
            Assert.Equal(BowId, entity.Equipment.Weapon!.ItemId);
            seen.Clear();
            attackerInput.QueueAbility(targetEntityId: 0);
            Pump(observer, attacker, server, 5);

            Assert.True(seen.Count > 0,
                "styles: " + string.Join(",", seen.ConvertAll(e => e.Style.ToString())) + " weapon=" + entity.Equipment.Weapon!.ItemId);
            Assert.Contains(seen, evt => evt.Style == CombatStyle.Arrow && evt.AttackerEntityId == attacker.EntityId);
        }

        [Fact]
        public void OwnAttack_IsNotEchoedBackToAttacker()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(TestWorld.Config(), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new TestClientTransport(serverTransport, clientId);
            var input = new ScriptedInputSource();
            using var pipeline = new ClientPipeline(transport, input, new NullView());
            pipeline.Start();
            Pump(null, pipeline, server, 6);

            var seen = new List<CombatEvent>();
            pipeline.CombatEventReceived += seen.Add;

            input.QueueAbility(targetEntityId: 0);
            Pump(null, pipeline, server, 4);

            Assert.Empty(seen);
            Assert.Null(pipeline.LastCombatEvent);
        }

        [Fact]
        public void EquipCommand_SwitchesWeaponAndReturnsOldItem()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(
                TestWorld.Config(
                    starterItems: new[] { new LoadoutItem(BowId, 1) },
                    starterEquipment: new[] { new LoadoutEquipment(StarterSwordId) }),
                serverTransport,
                new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new TestClientTransport(serverTransport, clientId);
            using var pipeline = new ClientPipeline(transport, new IdleInputSource(), new NullView());
            pipeline.Start();
            Pump(null, pipeline, server, 6);

            Assert.True(server.Simulation.State.TryGetEntity(pipeline.EntityId, out var entity));
            Assert.Equal(StarterSwordId, entity!.Equipment.Weapon!.ItemId);
            Assert.Equal(1, entity.Inventory.CountOf(BowId));

            Assert.True(pipeline.SendEquip(BowId));
            Pump(null, pipeline, server, 4);

            Assert.True(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Equal(BowId, entity.Equipment.Weapon!.ItemId);
            Assert.Equal("archer", entity.Equipment.Weapon.Definition.Archetype);
            Assert.Equal(1, entity.Inventory.CountOf(StarterSwordId));
            Assert.Equal(0, entity.Inventory.CountOf(BowId));
        }

        private static void Pump(ClientPipeline? observer, ClientPipeline client, GameServer server, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                observer?.Update(ClientPipeline.TickIntervalSeconds);
                client.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
                server.Tick();
                observer?.Update(ClientPipeline.TickIntervalSeconds);
                client.Update(ClientPipeline.TickIntervalSeconds);
            }
        }

        private sealed class ScriptedInputSource : IInputSource
        {
            private readonly Queue<ICommand> _commands = new Queue<ICommand>();
            private uint _sequence;

            public void QueueEquip(uint itemId)
            {
                _commands.Enqueue(new EquipCommand(itemId, 0, ++_sequence));
            }

            public void QueueAbility(uint targetEntityId)
            {
                _commands.Enqueue(new AbilityCommand(targetEntityId, 0, ++_sequence));
            }

            public bool TryReadCommand(uint clientTick, out ICommand? command)
            {
                if (_commands.Count == 0)
                {
                    command = null;
                    return false;
                }

                command = _commands.Dequeue();
                return true;
            }
        }

        private sealed class TestClientTransport : ITransport
        {
            private readonly InMemoryServerTransport _server;
            private readonly int _connectionId;

            public TestClientTransport(InMemoryServerTransport server, int connectionId)
            {
                _server = server;
                _connectionId = connectionId;
            }

            public event Action<byte[]>? PacketReceived;

            public event Action<string>? Disconnected
            {
                add { }
                remove { }
            }

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
        }

        private sealed class IdleInputSource : IInputSource
        {
            public bool TryReadCommand(uint clientTick, out ICommand? command)
            {
                command = null;
                return false;
            }
        }

        private sealed class NullView : IView
        {
            public void ApplySnapshot(SimulationSnapshot snapshot)
            {
            }

            public void Render(float frameDeltaTime)
            {
            }
        }
    }
}
