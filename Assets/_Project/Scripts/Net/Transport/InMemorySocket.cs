namespace Arena.Net.Transport
{
    /// <summary>An unconnected endpoint of an <see cref="InMemoryNetwork"/> (the server side in tests).</summary>
    public sealed class InMemorySocket : IServerDatagramSocket
    {
        private readonly InMemoryNetwork _network;
        private bool _closed;

        internal InMemorySocket(InMemoryNetwork network, NetAddress local)
        {
            _network = network;
            Local = local;
        }

        public NetAddress Local { get; }

        /// <summary>Conditions of this endpoint's outgoing link; null — the network's default.</summary>
        public NetworkConditions Conditions { get; set; }

        public long ResetCount => 0;

        public long OversizeCount { get; internal set; }

        public bool IsClosed => _closed;

        internal long LinkBusyUntil;
        internal bool InBurst;

        public bool TryReceive(byte[] buffer, out int length, out NetAddress from)
        {
            if (_closed)
            {
                length = 0;
                from = default;
                return false;
            }
            return _network.Receive(this, false, default, buffer, out length, out from);
        }

        internal bool TryReceiveFrom(in NetAddress remote, byte[] buffer, out int length)
        {
            if (_closed)
            {
                length = 0;
                return false;
            }
            return _network.Receive(this, true, remote, buffer, out length, out _);
        }

        public bool SendTo(byte[] buffer, int offset, int length, in NetAddress to) =>
            !_closed && _network.Send(this, to, buffer, offset, length);

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _network.Remove(this);
        }
    }
}
