using System;
using Arena.Gameplay.Settings;
using Arena.Net.Protocol;
using Arena.Net.Transport;

namespace Arena.Online
{
    /// <summary>
    /// Keeps the <see cref="RejoinTicket"/> of the current match on the device (Server-Spec §7, §12.3; S3.10b, S3.12): saved on
    /// <c>MatchAssigned</c> (re-saved with a new time when the app goes to the background or quits mid-match), cleared when the
    /// match ends, the player leaves or the server answers <c>MatchOver</c>; a ticket older than the rejoin window is ignored.
    /// The menu offers "Back to the match" while a ticket is valid. Plain values in an <see cref="IKeyValueStore"/> (PlayerPrefs on the device).
    /// </summary>
    public sealed class RejoinStore
    {
        public const string Prefix = "arena.rejoin.";
        private const string HasKey = Prefix + "has";
        private const string ServerHighKey = Prefix + "server_high";
        private const string ServerLowKey = Prefix + "server_low";
        private const string ServerPortKey = Prefix + "server_port";
        private const string MatchKey = Prefix + "match";
        private const string SlotKey = Prefix + "slot";
        private const string TokenHighKey = Prefix + "token_high";
        private const string TokenLowKey = Prefix + "token_low";
        private const string SavedKey = Prefix + "saved";
        private const int IntBits = 32;
        private const ulong LowMask = 0xFFFFFFFFul;

        private readonly IKeyValueStore _store;
        private readonly float _windowSeconds;

        public RejoinStore(IKeyValueStore store, float windowSeconds)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (windowSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            _windowSeconds = windowSeconds;
        }

        public static long UnixNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public void Save(in RejoinTicket ticket)
        {
            _store.SetInt(HasKey, 1);
            SetULong(ServerHighKey, ticket.Server.High);
            SetULong(ServerLowKey, ticket.Server.Low);
            _store.SetInt(ServerPortKey, ticket.Server.Port);
            SetULong(MatchKey, ticket.MatchId);
            _store.SetInt(SlotKey, ticket.Slot);
            SetULong(TokenHighKey, ticket.Token.High);
            SetULong(TokenLowKey, ticket.Token.Low);
            SetULong(SavedKey, (ulong)ticket.SavedAtSeconds);
            _store.Save();
        }

        /// <summary>The saved ticket if there is one and it is younger than the window at <paramref name="nowSeconds"/>.</summary>
        public bool TryLoad(long nowSeconds, out RejoinTicket ticket)
        {
            ticket = default;
            if (_store.GetInt(HasKey, 0) == 0) return false;
            ticket.Server = new NetAddress(GetULong(ServerHighKey), GetULong(ServerLowKey), (ushort)_store.GetInt(ServerPortKey, 0));
            ticket.MatchId = GetULong(MatchKey);
            ticket.Slot = _store.GetInt(SlotKey, 0);
            ticket.Token = new NetToken(GetULong(TokenHighKey), GetULong(TokenLowKey));
            ticket.SavedAtSeconds = (long)GetULong(SavedKey);
            long age = nowSeconds - ticket.SavedAtSeconds;
            return age >= 0 && age <= _windowSeconds && !ticket.Token.IsEmpty;
        }

        public void Clear()
        {
            if (_store.GetInt(HasKey, 0) == 0) return;
            _store.SetInt(HasKey, 0);
            _store.Save();
        }

        private void SetULong(string key, ulong value)
        {
            _store.SetInt(key + "_hi", unchecked((int)(value >> IntBits)));
            _store.SetInt(key + "_lo", unchecked((int)(value & LowMask)));
        }

        private ulong GetULong(string key)
        {
            ulong hi = unchecked((uint)_store.GetInt(key + "_hi", 0));
            ulong lo = unchecked((uint)_store.GetInt(key + "_lo", 0));
            return (hi << IntBits) | lo;
        }
    }
}
