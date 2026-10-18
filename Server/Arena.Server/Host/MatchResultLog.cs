using System;
using System.Globalization;
using System.IO;
using System.Text;
using Arena.Core.Defs;
using Arena.Net.Protocol;
using Arena.Net.Server.Match;
using Arena.Net.Transport;

namespace Arena.Server.Host
{
    /// <summary>
    /// <see cref="IMatchResultSink"/> of stage 3 (Server-Spec §5.3, §5.6): the result of each match as one JSON line in
    /// <c>logs/matches.jsonl</c> — mode, map, duration, winner, MVP and per slot: team, fighter, human or bot, taken over / AFK,
    /// statistics and an RTT bucket of humans. No player identifiers and no IP addresses (§6.4). Runs on the game thread once
    /// per match (allocates a line — not in the per-tick path); a failed write is logged and the match goes on.
    /// </summary>
    public sealed class MatchResultLog : IMatchResultSink
    {
        private static readonly long[] RttBucketsMs = { 50, 100, 150, 250 };

        private readonly string _path;
        private readonly ServerTransport _transport;
        private readonly ConsoleLog _log;

        /// <param name="path">Target file (folders are created); null or empty — results go to the console log only.</param>
        public MatchResultLog(string path, ServerTransport transport, ConsoleLog log)
        {
            _path = string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            if (_path != null)
            {
                string dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
        }

        public string FilePath => _path;

        public long Written { get; private set; }

        public void OnMatchEnded(MatchRoom room)
        {
            string line = Format(room, _transport);
            _log.Info("match " + ConsoleLog.FormatMatchId(room.MatchId) + " ended: " + room.Mode + " on " + room.Defs.Map.Id + ", "
                + room.Result.EndTick + " ticks, winner " + room.Result.WinnerTeam);
            if (_path == null) return;
            try
            {
                File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
                Written++;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _log.Error(room.MatchId, "result log write failed: " + e.Message);
            }
        }

        /// <summary>The JSON line of <paramref name="room"/> (also used by the self-test).</summary>
        public static string Format(MatchRoom room, ServerTransport transport)
        {
            MatchEndedMessage r = room.Result;
            MatchDefs defs = room.Defs;
            var sb = new StringBuilder(1024);
            sb.Append('{');
            Str(sb, "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(',');
            Str(sb, "matchId", ConsoleLog.FormatMatchId(room.MatchId)).Append(',');
            Str(sb, "mode", room.Mode.ToString()).Append(',');
            Str(sb, "map", defs.Map.Id).Append(',');
            Num(sb, "endTick", r.EndTick).Append(',');
            sb.Append("\"durationSeconds\":").Append((r.EndTick / (double)Arena.Core.Sim.SimLimits.TickRate).ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            Num(sb, "winnerTeam", r.WinnerTeam).Append(',');
            Num(sb, "mvpSlot", r.MvpSlot).Append(',');
            sb.Append("\"slots\":[");
            for (int i = 0; i < r.SlotCount; i++)
            {
                if (i > 0) sb.Append(',');
                RoomSlot slot = room.GetSlot(i);
                PlayerSlotSetup setup = room.Setup.Slots[i];
                MatchSlotResult s = r.Slots[i];
                sb.Append('{');
                Num(sb, "slot", i).Append(',');
                Num(sb, "team", setup.Team).Append(',');
                Str(sb, "fighter", defs.Fighters[setup.FighterIndex].Id).Append(',');
                Bool(sb, "human", slot.IsPlayer).Append(',');
                Bool(sb, "botAtEnd", room.Takeover.IsBot(i)).Append(',');
                Bool(sb, "afk", s.Afk).Append(',');
                Num(sb, "damage", s.DamageDealt).Append(',');
                Num(sb, "healing", s.HealingDone).Append(',');
                Num(sb, "kills", s.Kills).Append(',');
                Num(sb, "deaths", s.Deaths).Append(',');
                Num(sb, "acorns", s.AcornsCollected).Append(',');
                Num(sb, "place", s.Place).Append(',');
                Str(sb, "rtt", slot.IsPlayer ? RttBucket(transport, slot) : "-");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string RttBucket(ServerTransport transport, RoomSlot slot)
        {
            NetConnection c = slot.Connection >= 0 ? transport.GetConnection(slot.Connection) : null;
            if (c == null || !c.Rtt.HasSample) return "unknown";
            long ms = c.Rtt.SmoothedMicros / 1000;
            for (int i = 0; i < RttBucketsMs.Length; i++)
                if (ms < RttBucketsMs[i]) return "<" + RttBucketsMs[i].ToString(CultureInfo.InvariantCulture);
            return ">=" + RttBucketsMs[RttBucketsMs.Length - 1].ToString(CultureInfo.InvariantCulture);
        }

        private static StringBuilder Str(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(name).Append("\":\"");
            if (value != null)
            {
                foreach (char ch in value)
                {
                    if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                    else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                }
            }
            return sb.Append('"');
        }

        private static StringBuilder Num(StringBuilder sb, string name, long value) =>
            sb.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

        private static StringBuilder Bool(StringBuilder sb, string name, bool value) =>
            sb.Append('"').Append(name).Append("\":").Append(value ? "true" : "false");
    }
}
