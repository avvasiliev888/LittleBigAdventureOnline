using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Client.Network;
using Server;
using Shared;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>
    /// Protocol v10: сервер присылает авторитетный инвентарь и результат UI-команды.
    /// Регрессия на баг «куплено/скрафчено, но в инвентаре пусто»: панель должна получать
    /// именно серверное содержимое, а не локальный журнал отправленных действий.
    /// </summary>
    public sealed class InventorySyncTests
    {
        private const uint TraderId = 1;
        private const uint WoodId = 1;
        private const uint OreId = 2;
        private const uint BowId = 14;
        private const uint StaffId = 15;
        private const uint BowRecipeId = 5;

        [Fact]
        public void CommandResultCodec_RoundTripsAcceptedAndRejected()
        {
            var accepted = CommandResultCodec.Encode(7, true, string.Empty);
            Assert.True(CommandResultCodec.TryDecode(accepted, out var acceptedResult));
            Assert.Equal(7u, acceptedResult.Sequence);
            Assert.True(acceptedResult.Accepted);
            Assert.Equal(string.Empty, acceptedResult.Detail);

            var rejected = CommandResultCodec.Encode(8, false, "нужно 120 серебра, есть 0");
            Assert.True(CommandResultCodec.TryDecode(rejected, out var rejectedResult));
            Assert.Equal(8u, rejectedResult.Sequence);
            Assert.False(rejectedResult.Accepted);
            Assert.Contains("120", rejectedResult.Detail);
        }

        [Fact]
        public void CommandResultCodec_RejectsTruncatedOrInvalidPayload()
        {
            var payload = CommandResultCodec.Encode(9, true, "ok");
            Assert.False(CommandResultCodec.TryDecode(payload.AsSpan(0, payload.Length - 1), out _));

            var badFlag = (byte[])payload.Clone();
            badFlag[4] = 2;
            Assert.False(CommandResultCodec.TryDecode(badFlag, out _));
        }

        [Fact]
        public void InventoryCodec_RoundTripsStacks()
        {
            var stacks = new[]
            {
                new ItemStack(WoodId, 5),
                new ItemStack(BowId, 1),
            };

            var payload = InventoryCodec.Encode(42, stacks);
            Assert.True(InventoryCodec.TryDecode(payload, out var message));
            Assert.NotNull(message);
            Assert.Equal(42u, message!.EntityId);
            Assert.Equal(2, message.Stacks.Count);
            Assert.Equal(WoodId, message.Stacks[0].ItemId);
            Assert.Equal(5, message.Stacks[0].Quantity);
            Assert.Equal(BowId, message.Stacks[1].ItemId);
        }

        [Fact]
        public void InventoryCodec_RejectsZeroQuantity()
        {
            var payload = InventoryCodec.Encode(42, new[] { new ItemStack(WoodId, 1) });
            payload[6 + 4] = 0;
            payload[6 + 5] = 0;
            payload[6 + 6] = 0;
            payload[6 + 7] = 0;

            Assert.False(InventoryCodec.TryDecode(payload, out _));
        }

        [Fact]
        public void BuyAndCraft_AppearInClientInventory_AndReportAccepted()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(EconomyConfig(traderX: 2f), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new TestClientTransport(serverTransport, clientId);
            using var pipeline = new ClientPipeline(transport, new IdleInputSource(), new NullView());
            pipeline.Start();
            Pump(pipeline, server, 6);

            Assert.True(pipeline.HasSession);
            Assert.True(pipeline.HasInventory, "сервер должен прислать стартовый инвентарь сразу после HelloAck");
            Assert.True(server.Simulation.State.TryGetEntity(pipeline.EntityId, out var entity));
            Assert.Equal(5, entity!.Inventory.CountOf(WoodId));

            Assert.True(pipeline.SendBuy(TraderId, StaffId, 1));
            server.Poll();
            server.Tick();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);

            Assert.NotNull(pipeline.LastCommandResult);
            Assert.True(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Equal(1, CountOf(pipeline, StaffId));
            Assert.Equal(380, entity.Silver);

            Assert.True(pipeline.SendCraft(BowRecipeId));
            server.Poll();
            server.Tick();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);

            Assert.True(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Equal(1, CountOf(pipeline, BowId));
            Assert.Equal(2, CountOf(pipeline, WoodId));
            Assert.Equal(2, CountOf(pipeline, OreId));
            Assert.Equal(1, entity.Inventory.CountOf(BowId));
        }

        [Fact]
        public void RejectedBuy_ReportsReason_AndDoesNotAppearInInventory()
        {
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(EconomyConfig(traderX: 100f), serverTransport, new ServerOptions());
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new TestClientTransport(serverTransport, clientId);
            using var pipeline = new ClientPipeline(transport, new IdleInputSource(), new NullView());
            pipeline.Start();
            Pump(pipeline, server, 6);

            Assert.True(pipeline.SendBuy(TraderId, StaffId, 1));
            server.Poll();
            server.Tick();
            pipeline.Update(ClientPipeline.TickIntervalSeconds);

            Assert.NotNull(pipeline.LastCommandResult);
            Assert.False(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Contains("вне радиуса", pipeline.LastCommandResult.Value.Detail);
            Assert.Equal(0, CountOf(pipeline, StaffId));
        }

        [Fact]
        public void RepoConfig_BuyStaffAndCraftBow_BothReachClientInventory()
        {
            var config = ConfigLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "config"));
            var serverTransport = new InMemoryServerTransport();
            using var server = new GameServer(
                config,
                serverTransport,
                new ServerOptions { AllowTeleport = true });
            server.Start();

            var clientId = serverTransport.ConnectClient();
            using var transport = new TestClientTransport(serverTransport, clientId);
            using var pipeline = new ClientPipeline(transport, new IdleInputSource(), new NullView());
            pipeline.Start();
            Pump(pipeline, server, 6);

            // Игрок стоит у синего торговца: точка (2,0) слишком далеко от (-25,0).
            Assert.True(pipeline.SendTeleport(-24f, 0f));
            Pump(pipeline, server, 4);
            Assert.True(pipeline.SendBuy(1, 15, 1));
            Pump(pipeline, server, 3);

            Assert.True(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Equal(1, CountOf(pipeline, 15));

            Assert.True(pipeline.SendCraft(5));
            Pump(pipeline, server, 3);
            Assert.True(pipeline.LastCommandResult!.Value.Accepted);
            Assert.Equal(1, CountOf(pipeline, 14));
            Assert.Equal(2, CountOf(pipeline, 1));
            Assert.Equal(2, CountOf(pipeline, 2));
        }

        private static SimulationConfig EconomyConfig(float traderX)
        {
            var items = new List<ItemDefinition>(TestWorld.DefaultItems)
            {
                new ItemDefinition(StaffId, "staff", ItemKind.Equipment, 1, 100, EquipmentSlot.MainHand, "warrior", 5),
            };

            return TestWorld.Config(
                items: items,
                starterItems: new[]
                {
                    new LoadoutItem(WoodId, 5),
                    new LoadoutItem(OreId, 3),
                },
                startingSilver: 500,
                traders: new[]
                {
                    TestWorld.Trader(
                        TraderId,
                        "test",
                        traderX,
                        0f,
                        5f,
                        TestWorld.Price(StaffId, 120, 30)),
                },
                recipes: new[]
                {
                    new Recipe(
                        BowRecipeId,
                        "hunter_bow_t1",
                        BowId,
                        1,
                        new[]
                        {
                            new RecipeMaterial(WoodId, 3),
                            new RecipeMaterial(OreId, 1),
                        },
                        4),
                });
        }

        private static int CountOf(ClientPipeline pipeline, uint itemId)
        {
            var stack = pipeline.Inventory.FirstOrDefault(entry => entry.ItemId == itemId);
            return stack.Quantity;
        }

        private static void Pump(ClientPipeline pipeline, GameServer server, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                pipeline.Update(ClientPipeline.TickIntervalSeconds);
                server.Poll();
                server.Tick();
            }

            pipeline.Update(ClientPipeline.TickIntervalSeconds);
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
