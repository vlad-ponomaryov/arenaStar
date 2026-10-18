using Arena.Net;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>Hand-made connectionless packets for handshake and limit tests (what a foreign or old client would send).</summary>
    public static class RawPackets
    {
        public static int ConnectRequest(byte[] buffer, ushort version, ulong contentHash, ulong nonce, int padTo = NetProtocol.HandshakePacketBytes)
        {
            var writer = new BitWriter();
            writer.Reset(buffer, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);
            new ConnectRequestMessage
            {
                ProtocolId = NetProtocol.Id,
                ProtocolVersion = version,
                ContentHash = contentHash,
                Purpose = ConnectPurpose.Lobby,
                Nonce = nonce,
            }.Write(writer);
            return Finish(buffer, writer, PacketKind.ConnectRequest, padTo);
        }

        public static int ConnectResponse(byte[] buffer, ulong nonce, ulong cookie, ulong contentHash = TransportRig.ContentHash)
        {
            var writer = new BitWriter();
            writer.Reset(buffer, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);
            new ConnectResponseMessage
            {
                ProtocolId = NetProtocol.Id,
                ProtocolVersion = NetProtocol.Version,
                ContentHash = contentHash,
                Purpose = ConnectPurpose.Lobby,
                Nonce = nonce,
                Cookie = cookie,
            }.Write(writer);
            return Finish(buffer, writer, PacketKind.ConnectResponse, NetProtocol.HandshakePacketBytes);
        }

        public static int Ping(byte[] buffer, ulong nonce, PacketKind kind = PacketKind.Ping, int padTo = NetProtocol.PingPacketBytes)
        {
            var writer = new BitWriter();
            writer.Reset(buffer, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);
            new PingMessage { ProtocolId = NetProtocol.Id, Nonce = nonce }.Write(writer);
            return Finish(buffer, writer, kind, padTo);
        }

        public static int Discover(byte[] buffer, ulong nonce)
        {
            var writer = new BitWriter();
            writer.Reset(buffer, NetProtocol.HeaderBytes, NetLimits.MaxPacketBytes - NetProtocol.HeaderBytes);
            new DiscoverMessage { ProtocolId = NetProtocol.Id, Nonce = nonce }.Write(writer);
            return Finish(buffer, writer, PacketKind.Discover, NetProtocol.DiscoverPacketBytes);
        }

        public static PacketKind KindOf(byte[] buffer, int length) =>
            PacketHeader.TryRead(buffer, length, out PacketHeader h) ? h.Kind : PacketKind.Invalid;

        public static BitReader Body(byte[] buffer, int length)
        {
            var reader = new BitReader();
            reader.Reset(buffer, NetProtocol.HeaderBytes, length - NetProtocol.HeaderBytes);
            return reader;
        }

        private static int Finish(byte[] buffer, BitWriter writer, PacketKind kind, int padTo)
        {
            int length = NetProtocol.HeaderBytes + writer.BytesWritten;
            if (padTo > length)
            {
                writer.WriteZeroBytes(padTo - length);
                length = padTo;
            }
            PacketHeader.Connectionless(kind).Write(buffer);
            return length;
        }
    }
}
