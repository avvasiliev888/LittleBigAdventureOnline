using System;
using Shared;
using Xunit;

namespace Tests
{
    /// <summary>Заголовок сообщения (ADR-004): magic, версия, тип, длина.</summary>
    public sealed class MessageFramingTests
    {
        [Fact]
        public void WrapThenUnwrap_ReturnsSameTypeAndPayload()
        {
            var payload = new byte[] { 1, 2, 3, 4 };

            var packet = MessageFraming.Wrap(MessageType.Command, payload);
            var error = MessageFraming.TryUnwrap(packet, out var frame);

            Assert.Equal(FramingError.None, error);
            Assert.Equal(MessageType.Command, frame.Type);
            Assert.Equal(payload, frame.Payload);
            Assert.Equal(MessageFraming.HeaderSize + payload.Length, packet.Length);
        }

        [Fact]
        public void Wrap_EmptyPayload_Works()
        {
            var packet = MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty);

            Assert.Equal(FramingError.None, MessageFraming.TryUnwrap(packet, out var frame));
            Assert.Empty(frame.Payload);
        }

        [Fact]
        public void Unwrap_Truncated_ReturnsTruncated()
        {
            var error = MessageFraming.TryUnwrap(new byte[3], out _);

            Assert.Equal(FramingError.Truncated, error);
        }

        [Fact]
        public void Unwrap_BadMagic_ReturnsBadMagic()
        {
            var packet = MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty);
            packet[0] ^= 0xFF;

            Assert.Equal(FramingError.BadMagic, MessageFraming.TryUnwrap(packet, out _));
        }

        [Fact]
        public void Unwrap_BadVersion_ReturnsBadVersion()
        {
            var packet = MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty);
            packet[2] = 0xFF;
            packet[3] = 0xFF;

            Assert.Equal(FramingError.BadVersion, MessageFraming.TryUnwrap(packet, out _));
        }

        [Fact]
        public void Unwrap_WrongLength_ReturnsBadLength()
        {
            var packet = MessageFraming.Wrap(MessageType.Command, new byte[] { 1, 2, 3 });
            var shortened = new byte[packet.Length - 1];
            Array.Copy(packet, shortened, shortened.Length);

            Assert.Equal(FramingError.BadLength, MessageFraming.TryUnwrap(shortened, out _));
        }

        [Fact]
        public void TryReadVersion_ReadsVersionWithoutFullValidation()
        {
            var packet = MessageFraming.Wrap(MessageType.Hello, ReadOnlySpan<byte>.Empty);

            Assert.True(MessageFraming.TryReadVersion(packet, out var version));
            Assert.Equal(Protocol.Version, version);
            Assert.False(MessageFraming.TryReadVersion(new byte[2], out _));
        }

        [Fact]
        public void ProtocolConstants_AreStable()
        {
            Assert.Equal(0xA1B0, Protocol.Magic);
            Assert.Equal(3, Protocol.Version);
        }
    }
}
