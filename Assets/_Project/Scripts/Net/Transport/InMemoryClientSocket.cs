namespace Arena.Net.Transport
{
    /// <summary>
    /// A connected endpoint of an <see cref="InMemoryNetwork"/> (the client side in tests): sends to and receives only from
    /// one remote address, like <see cref="UdpDatagramSocket"/>. <see cref="BreakPath"/> simulates a dead interface.
    /// </summary>
    public sealed class InMemoryClientSocket : IDatagramSocket
    {
        private readonly InMemorySocket _socket;

        internal InMemoryClientSocket(InMemorySocket socket, NetAddress remote)
        {
            _socket = socket;
            Remote = remote;
        }

        public NetAddress Local => _socket.Local;

        public NetAddress Remote { get; }

        public NetworkConditions Conditions
        {
            get => _socket.Conditions;
            set => _socket.Conditions = value;
        }

        public bool PathBroken { get; private set; }

        /// <summary>From now on sends fail and <see cref="PathBroken"/> is set (Android switched networks).</summary>
        public void BreakPath() => PathBroken = true;

        public bool Send(byte[] buffer, int offset, int length) =>
            !PathBroken && _socket.SendTo(buffer, offset, length, Remote);

        public bool TryReceive(byte[] buffer, out int length)
        {
            if (PathBroken)
            {
                length = 0;
                return false;
            }
            return _socket.TryReceiveFrom(Remote, buffer, out length);
        }

        public void Close() => _socket.Close();
    }
}
