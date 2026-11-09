using System;
using System.Collections.Generic;
using Simulation.Core;
using Xunit;

namespace Tests
{
    /// <summary>Канонический порядок команд (ClientTick, Sequence, EntityId).</summary>
    public sealed class CommandOrderingTests
    {
        [Fact]
        public void Sort_OrdersByTickThenSequenceThenEntity()
        {
            var commands = new List<CommandEnvelope>
            {
                Envelope(entity: 2, tick: 5, sequence: 0),
                Envelope(entity: 1, tick: 5, sequence: 1),
                Envelope(entity: 1, tick: 5, sequence: 0),
                Envelope(entity: 3, tick: 4, sequence: 2),
            };

            var sorted = CommandOrdering.Sort(commands);

            Assert.Equal(3u, sorted[0].EntityId);
            Assert.Equal(1u, sorted[1].EntityId);
            Assert.Equal(2u, sorted[2].EntityId);
            Assert.Equal(1u, sorted[3].EntityId);
            Assert.Equal(0u, sorted[1].Command.Sequence);
            Assert.Equal(1u, sorted[3].Command.Sequence);
        }

        [Fact]
        public void Sort_DoesNotMutateInput()
        {
            var commands = new List<CommandEnvelope>
            {
                Envelope(entity: 1, tick: 2, sequence: 1),
                Envelope(entity: 1, tick: 1, sequence: 1),
            };

            _ = CommandOrdering.Sort(commands);

            Assert.Equal(2u, commands[0].Command.ClientTick);
            Assert.Equal(1u, commands[1].Command.ClientTick);
        }

        [Fact]
        public void Sort_WithNull_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CommandOrdering.Sort(null!));
        }

        [Fact]
        public void Sort_OfEmptyList_ReturnsEmpty()
        {
            var sorted = CommandOrdering.Sort(Array.Empty<CommandEnvelope>());

            Assert.Empty(sorted);
        }

        private static CommandEnvelope Envelope(uint entity, uint tick, uint sequence)
        {
            return new CommandEnvelope(entity, new MoveCommand(new System.Numerics.Vector2(1f, 0f), tick, sequence));
        }
    }
}
