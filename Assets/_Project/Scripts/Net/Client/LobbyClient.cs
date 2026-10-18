using System;
using Arena.Core.Modes;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Transport;

namespace Arena.Net.Client
{
    /// <summary>
    /// The client side of the lobby (Server-Spec §6.1, §12.4) over a <see cref="NetClient"/>: connects with
    /// <c>purpose = Lobby</c> (no ticket, no identifier — §6.4), sends <c>QueueJoin</c> once connected, keeps the newest
    /// <c>QueueStatus</c> for the search screen, <c>QueueCancel</c>s, and on <c>MatchAssigned</c> keeps the match id, slot and
    /// rejoin token (for <c>RejoinStore</c>, S3.12) — the same connection then carries <c>MatchStart</c> to the attached
    /// <see cref="MatchClientSession"/>. Polled from the main thread after <see cref="NetClient.Update"/>. No allocations.
    /// </summary>
    public sealed class LobbyClient : ILobbyMessageHandler
    {
        private readonly byte[] _send = new byte[NetLimits.MaxReliableMessageBytes];
        private readonly BitWriter _writer = new BitWriter();
        private bool _joinPending;
        private QueueStatusMessage _status;
        private MatchAssignedMessage _assignment;
        private ServerNoticeMessage _notice;

        public LobbyClient(NetClient client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            client.Lobby = this;
        }

        public NetClient Client { get; }

        public LobbyState State { get; private set; }

        public ModeKind Mode { get; private set; }

        public int FighterIndex { get; private set; }

        public bool HasStatus { get; private set; }

        /// <summary>The newest <c>QueueStatus</c> (valid when <see cref="HasStatus"/>).</summary>
        public ref readonly QueueStatusMessage Status => ref _status;

        /// <summary>Valid in <see cref="LobbyState.Assigned"/>.</summary>
        public ref readonly MatchAssignedMessage Assignment => ref _assignment;

        public QueueRejectReason QueueRejectReason { get; private set; }

        public bool HasNotice { get; private set; }

        public ref readonly ServerNoticeMessage Notice => ref _notice;

        /// <summary>Why the server refused the handshake (<c>Maintenance</c>, <c>VersionMismatch</c> … — §4.1 "update the game").</summary>
        public RejectReason ConnectRejectReason => Client.Transport.RejectReason;

        public DisconnectReason DisconnectReason => Client.Transport.DisconnectReason;

        public long StatusesReceived { get; private set; }

        /// <summary>
        /// Starts a search: connects over <paramref name="socket"/> unless already connected, then joins the queue of
        /// <paramref name="mode"/> with <paramref name="fighterIndex"/>. Also "Again" on a connection that is still open.
        /// </summary>
        public void Search(IDatagramSocket socket, ModeKind mode, int fighterIndex, long nowMicros)
        {
            Mode = mode;
            FighterIndex = fighterIndex;
            HasStatus = false;
            QueueRejectReason = QueueRejectReason.None;
            _joinPending = true;
            State = LobbyState.Connecting;
            if (!Client.IsConnected) Client.ConnectLobby(socket, nowMicros);
            TrySendJoin();
        }

        /// <summary>Leaves the queue; the connection stays open (another search needs no handshake).</summary>
        public void Cancel()
        {
            if (State != LobbyState.Connecting && State != LobbyState.Searching) return;
            _joinPending = false;
            if (State == LobbyState.Searching)
            {
                _send[0] = (byte)MessageKind.QueueCancel;
                Client.SendReliable(_send, 0, 1);
            }
            State = LobbyState.Idle;
            HasStatus = false;
        }

        /// <summary>After <see cref="NetClient.Update"/>: sends the pending join, notices a lost connection.</summary>
        public void Update(long nowMicros)
        {
            TrySendJoin();
            if ((State == LobbyState.Connecting || State == LobbyState.Searching)
                && Client.Transport.State == ClientState.Disconnected)
            {
                _joinPending = false;
                State = LobbyState.Failed;
            }
        }

        private void TrySendJoin()
        {
            if (!_joinPending || !Client.IsConnected) return;
            var join = new QueueJoinMessage { Mode = Mode, FighterIndex = FighterIndex };
            _writer.Reset(_send, 0, _send.Length);
            join.Write(_writer);
            if (!Client.SendReliable(_send, 0, _writer.BytesWritten)) return;
            _joinPending = false;
            State = LobbyState.Searching;
        }

        // ---- ILobbyMessageHandler ----

        public bool OnLobbyMessage(MessageKind kind, BitReader reader)
        {
            switch (kind)
            {
                case MessageKind.QueueStatus:
                {
                    var status = new QueueStatusMessage();
                    if (!status.Read(reader)) return false;
                    StatusesReceived++;
                    if (State != LobbyState.Searching || status.Mode != Mode) return true; // stale (cancelled or another search)
                    _status = status;
                    HasStatus = true;
                    return true;
                }
                case MessageKind.MatchAssigned:
                {
                    var assigned = new MatchAssignedMessage();
                    if (!assigned.Read(reader)) return false;
                    _assignment = assigned;
                    Client.SetRejoinToken(in assigned.RejoinToken); // a lost match connection returns by itself (S3.12)
                    _joinPending = false;
                    State = LobbyState.Assigned;
                    return true;
                }
                case MessageKind.QueueRejected:
                {
                    var rejected = new QueueRejectedMessage();
                    if (!rejected.Read(reader)) return false;
                    QueueRejectReason = rejected.Reason;
                    if (State == LobbyState.Searching || State == LobbyState.Connecting) State = LobbyState.Rejected;
                    return true;
                }
                case MessageKind.ServerNotice:
                {
                    var notice = new ServerNoticeMessage();
                    if (!notice.Read(reader)) return false;
                    _notice = notice;
                    HasNotice = true;
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
