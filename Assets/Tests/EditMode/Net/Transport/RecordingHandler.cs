using Arena.Net.Protocol;
using Arena.Net.Transport;

namespace Arena.Tests.EditMode.Net.Transport
{
    /// <summary>
    /// Test handler for both ends: counts payloads and acks, checks that reliable messages arrive in order exactly once.
    /// A reliable test message starts with its u16 index followed by bytes equal to (index + position) — content is verified.
    /// No allocations in the callbacks.
    /// </summary>
    public sealed class RecordingHandler : IServerTransportHandler
    {
        public RejectReason ApproveResult = RejectReason.None;
        public int Connected = -1;
        public int ConnectedCount;
        public int DisconnectedCount;
        public DisconnectReason LastDisconnect;
        public int LastDisconnectedConnection = -1;
        public int Payloads;
        public int PayloadBytes;
        public int Acked;
        public int Reliable;
        public int ReliableErrors;
        public ConnectPurpose LastPurpose;
        public bool LastHasToken;
        public NetToken LastToken;

        public RejectReason Approve(ConnectPurpose purpose, bool hasToken, in NetToken token)
        {
            LastPurpose = purpose;
            LastHasToken = hasToken;
            LastToken = token;
            return ApproveResult;
        }

        public void OnConnected(int connection)
        {
            Connected = connection;
            ConnectedCount++;
        }

        public void OnDisconnected(int connection, DisconnectReason reason)
        {
            DisconnectedCount++;
            LastDisconnect = reason;
            LastDisconnectedConnection = connection;
        }

        public void OnPacketAcked(int connection, ushort sequence) => Acked++;

        public void OnPayload(int connection, ushort sequence, byte[] buffer, int offset, int length)
        {
            Payloads++;
            PayloadBytes += length;
        }

        public void OnReliableMessage(int connection, byte[] buffer, int offset, int length)
        {
            int index = buffer[offset] | (buffer[offset + 1] << 8);
            if (index != (Reliable & 0xFFFF) || length != MessageLength(index)) ReliableErrors++;
            for (int i = 2; i < length; i++)
                if (buffer[offset + i] != (byte)(index + i)) ReliableErrors++;
            Reliable++;
        }

        /// <summary>Length of test message <paramref name="index"/>: 2…1000 bytes, varied so that the rings wrap.</summary>
        public static int MessageLength(int index) => 2 + (index * 397) % 999;

        public static int Fill(byte[] buffer, int index)
        {
            int length = MessageLength(index);
            buffer[0] = (byte)index;
            buffer[1] = (byte)(index >> 8);
            for (int i = 2; i < length; i++) buffer[i] = (byte)(index + i);
            return length;
        }
    }
}
