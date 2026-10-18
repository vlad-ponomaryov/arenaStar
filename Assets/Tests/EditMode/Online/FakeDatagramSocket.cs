using System;
using System.Collections.Generic;
using Arena.Net.Transport;

namespace Arena.Tests.EditMode.Online
{
    /// <summary>A client socket for tests: keeps what was sent, returns what was queued for receiving.</summary>
    public sealed class FakeDatagramSocket : IDatagramSocket
    {
        public readonly List<byte[]> Sent = new List<byte[]>();
        public readonly Queue<byte[]> ToReceive = new Queue<byte[]>();
        public bool Closed;

        public bool PathBroken => false;

        public bool Send(byte[] buffer, int offset, int length)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, offset, copy, 0, length);
            Sent.Add(copy);
            return true;
        }

        public bool TryReceive(byte[] buffer, out int length)
        {
            length = 0;
            if (ToReceive.Count == 0) return false;
            byte[] next = ToReceive.Dequeue();
            Buffer.BlockCopy(next, 0, buffer, 0, next.Length);
            length = next.Length;
            return true;
        }

        public void Close() => Closed = true;
    }
}
