using System;
using Arena.Bots;
using Arena.Core.Sim;
using Arena.Net.Server.Snapshots;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// Settings of the match server (Server-Spec §13, "S" — <c>server.json</c> from S3.9; tests set them in code, §5.5). Values of
    /// <see cref="CreateDefault"/> are PROTO (§13), in one place like <c>TransportSettings.CreateDefault</c>. Seconds are turned
    /// into ticks with <see cref="SimTime.SecondsToTicks"/>.
    /// </summary>
    public sealed class ServerSettings
    {
        private const int MillisPerSecond = 1000;

        // ---- Input (§2.3, §2.4) ----

        /// <summary>Input for tick t is accepted while t ≤ T + this (500 ms); farther ahead it is dropped and counted.</summary>
        public int MaxInputAheadTicks;

        /// <summary>A missing input repeats the last move/aim without buttons this many ticks (200 ms), then a zero input.</summary>
        public int MissingInputRepeatTicks;

        /// <summary><c>InputMarginMin</c> is the minimum margin of the input packets of this many last ticks.</summary>
        public int MarginWindowTicks;

        /// <summary>Inputs per client packet (sent to the client in <c>MatchStart</c>): survives 3 lost packets in a row.</summary>
        public int InputRedundancy;

        // ---- Lag compensation (§2.8, S3.13) ----

        /// <summary>Instant contact hits rewind the targets by the input's <c>ViewDelayTicks</c>; off — every input gets 0 (playtests).</summary>
        public bool LagCompEnabled;

        /// <summary>The rewind never exceeds this (covers RTT 150 ms + jitter 50 ms, GDD §8.3); floored to whole ticks.</summary>
        public int LagCompMaxRewindMs;

        /// <summary>The client's reported interpolation delay counts at most this much in the server's estimate of its view delay.</summary>
        public int InterpDelayMaxMs;

        /// <summary>Slack of the estimate RTT + input margin + interpolation delay (rounding of the render clock and the snapshot interval).</summary>
        public int LagCompSlackTicks;

        // ---- Takeover (§7) ----

        /// <summary>No input this long — a bot drives the fighter (and the snapshot flag says so).</summary>
        public float BotTakeoverSeconds;

        /// <summary>Connected, but only zero inputs (no stick, no button) this long — a bot until the next move or press.</summary>
        public float AfkSeconds;

        /// <summary>Difficulty of fill bots and of takeovers (D-107: Medium, "Normal" — Novice/Easy were too weak; was Novice, D-087).</summary>
        public BotDifficulty FillBotDifficulty;

        /// <summary>The <c>MatchStart</c> slot table marks bots (D-087: off).</summary>
        public bool ShowBots;

        // ---- Life cycle (§5.3) ----

        public float JoinTimeoutSeconds;
        public float LoadTimeoutSeconds;
        public float CountdownSeconds;
        public float ResultLingerSeconds;

        /// <summary>A room more than this many ticks late skips them (counted) — no "spiral of death" (§5.3).</summary>
        public int MaxCatchUpTicks;

        /// <summary>Rooms one <c>MatchHost</c> holds at most.</summary>
        public int MaxMatches;

        /// <summary>Record every applied input of every slot (<c>InputLog</c>, <c>ReplayVerifier</c>); tests — always.</summary>
        public bool RecordInputLog;

        // ---- Lobby (§6.2, S3.8) ----

        /// <summary>The oldest ticket of a queue waited this long — the match is assembled with bots in the free slots (D-107: 5 s; GDD §7 said 15).</summary>
        public float BotFillSeconds;

        /// <summary>The same for players under 300 trophies (GDD §7) — used from stage 4, when trophies exist.</summary>
        public float BotFillSecondsNewPlayers;

        /// <summary>A queued client gets <c>QueueStatus</c> this often (it also keeps the NAT mapping alive).</summary>
        public float QueueStatusIntervalSeconds;

        public SnapshotSettings Snapshots;

        public static ServerSettings CreateDefault() => new ServerSettings
        {
            MaxInputAheadTicks = 15,
            MissingInputRepeatTicks = 6,
            MarginWindowTicks = 30,
            InputRedundancy = 4,
            LagCompEnabled = true,
            LagCompMaxRewindMs = 350,
            InterpDelayMaxMs = 200,
            LagCompSlackTicks = 2,
            BotTakeoverSeconds = 1.5f,
            AfkSeconds = 30f,
            FillBotDifficulty = BotDifficulty.Medium,
            ShowBots = false,
            JoinTimeoutSeconds = 10f,
            LoadTimeoutSeconds = 15f,
            CountdownSeconds = 3f,
            ResultLingerSeconds = 30f,
            MaxCatchUpTicks = 5,
            MaxMatches = 200,
            RecordInputLog = false,
            BotFillSeconds = 5f,
            BotFillSecondsNewPlayers = 8f,
            QueueStatusIntervalSeconds = 1f,
            Snapshots = SnapshotSettings.CreateDefault()
        };

        public int BotTakeoverTicks => SimTime.SecondsToTicks(BotTakeoverSeconds);
        public int AfkTicks => SimTime.SecondsToTicks(AfkSeconds);
        public int JoinTimeoutTicks => SimTime.SecondsToTicks(JoinTimeoutSeconds);
        public int LoadTimeoutTicks => SimTime.SecondsToTicks(LoadTimeoutSeconds);
        public int CountdownTicks => SimTime.SecondsToTicks(CountdownSeconds);
        public int ResultLingerTicks => SimTime.SecondsToTicks(ResultLingerSeconds);

        /// <summary><see cref="LagCompMaxRewindMs"/> in whole ticks (rounded down: never more than the limit), at most the history.</summary>
        public int LagCompMaxRewindTicks
        {
            get
            {
                int ticks = LagCompMaxRewindMs * SimLimits.TickRate / MillisPerSecond;
                return ticks > SimLimits.MaxRewindTicks ? SimLimits.MaxRewindTicks : ticks;
            }
        }

        /// <summary><see cref="InterpDelayMaxMs"/> in ticks, rounded up.</summary>
        public int InterpDelayMaxTicks => (InterpDelayMaxMs * SimLimits.TickRate + MillisPerSecond - 1) / MillisPerSecond;

        /// <summary>Throws if a value is out of range.</summary>
        public void Validate()
        {
            if (MaxInputAheadTicks < 1 || MaxInputAheadTicks >= NetLimits.InputBufferTicks / 2)
                throw new ArgumentException("ServerSettings: MaxInputAheadTicks must be in [1, InputBufferTicks / 2)");
            if (MissingInputRepeatTicks < 0) throw new ArgumentException("ServerSettings: MissingInputRepeatTicks < 0");
            if (MarginWindowTicks < 1 || MarginWindowTicks > NetLimits.MarginHistoryTicks)
                throw new ArgumentException("ServerSettings: MarginWindowTicks must be in [1, MarginHistoryTicks]");
            if (InputRedundancy < 1 || InputRedundancy > NetLimits.MaxInputsPerPacket)
                throw new ArgumentException("ServerSettings: InputRedundancy must be in [1, MaxInputsPerPacket]");
            if (LagCompMaxRewindMs < 0 || InterpDelayMaxMs < 0 || LagCompSlackTicks < 0)
                throw new ArgumentException("ServerSettings: lag compensation values must be ≥ 0");
            if (BotTakeoverSeconds <= 0f || AfkSeconds <= 0f) throw new ArgumentException("ServerSettings: takeover times must be > 0");
            if (JoinTimeoutSeconds < 0f || LoadTimeoutSeconds < 0f || CountdownSeconds < 0f || ResultLingerSeconds < 0f)
                throw new ArgumentException("ServerSettings: times must be ≥ 0");
            if (MaxCatchUpTicks < 1) throw new ArgumentException("ServerSettings: MaxCatchUpTicks must be ≥ 1");
            if (MaxMatches < 1) throw new ArgumentException("ServerSettings: MaxMatches must be ≥ 1");
            if (BotFillSeconds < 0f || BotFillSecondsNewPlayers < 0f) throw new ArgumentException("ServerSettings: bot fill times must be ≥ 0");
            if (QueueStatusIntervalSeconds <= 0f) throw new ArgumentException("ServerSettings: QueueStatusIntervalSeconds must be > 0");
            if (Snapshots == null) throw new ArgumentException("ServerSettings: Snapshots missing");
            Snapshots.Validate();
        }

        public ServerSettings Clone()
        {
            var copy = (ServerSettings)MemberwiseClone();
            copy.Snapshots = Snapshots?.Clone();
            return copy;
        }
    }
}
