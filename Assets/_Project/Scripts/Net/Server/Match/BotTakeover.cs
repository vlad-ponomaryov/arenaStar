using System;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Input;
using Arena.Core.Sim;

namespace Arena.Net.Server.Match
{
    /// <summary>
    /// Who drives each slot of a room (Server-Spec §7): the player or a bot of <see cref="BotController"/>. Reasons
    /// (<see cref="BotControlReason"/>) are set and cleared by the room; on every change the bot is switched on (a brain from
    /// scratch, seed = match + slot + takeover number) or off with <see cref="BotController.SetBotActive"/>.
    /// <see cref="Observe"/> runs once per tick with the player's input: no real input for <c>BotTakeoverTicks</c> → bot;
    /// a real input → the player again in the same tick; only zero inputs for <c>AfkTicks</c> → bot until a move or press.
    /// The snapshot flag "a bot drives your fighter" is <see cref="IsBot"/> of a player slot. No allocations.
    /// </summary>
    public sealed class BotTakeover
    {
        private readonly BotController _bots;
        private readonly BotDifficultyDef _difficulty;
        private readonly int _matchSeed;
        private readonly int _takeoverTicks;
        private readonly int _afkTicks;
        private readonly BotControlReason[] _reasons = new BotControlReason[SimLimits.MaxFighters];
        private readonly int[] _ticksWithoutInput = new int[SimLimits.MaxFighters];
        private readonly int[] _zeroInputTicks = new int[SimLimits.MaxFighters];
        private readonly int[] _takeovers = new int[SimLimits.MaxFighters];
        private readonly bool[] _everAfk = new bool[SimLimits.MaxFighters];

        public BotTakeover(BotController bots, BotDifficultyDef difficulty, int matchSeed, int takeoverTicks, int afkTicks)
        {
            _bots = bots ?? throw new ArgumentNullException(nameof(bots));
            _difficulty = difficulty ?? throw new ArgumentNullException(nameof(difficulty));
            _matchSeed = matchSeed;
            _takeoverTicks = takeoverTicks < 1 ? 1 : takeoverTicks;
            _afkTicks = afkTicks < 1 ? 1 : afkTicks;
        }

        public BotController Bots => _bots;

        /// <summary>Takeovers of player slots so far (metrics).</summary>
        public long TakeoverCount { get; private set; }

        public bool IsBot(int slot) => _reasons[slot] != BotControlReason.None;

        /// <summary>Forgets the count (the room calls it after setting the initial reasons — those are not takeovers).</summary>
        public void ResetCount() => TakeoverCount = 0;

        public BotControlReason ReasonsOf(int slot) => _reasons[slot];

        /// <summary>The slot was taken over for AFK at least once (result flag, §7).</summary>
        public bool WasAfk(int slot) => _everAfk[slot];

        public void Set(int slot, BotControlReason reason) => Change(slot, _reasons[slot] | reason);

        public void Clear(int slot, BotControlReason reason) => Change(slot, _reasons[slot] & ~reason);

        /// <summary>
        /// The input of a player slot for this tick: <paramref name="real"/> — it arrived in time. Updates the no-input and AFK
        /// timers and the reasons; call before the bots write their inputs.
        /// </summary>
        public void Observe(int slot, bool real, in PlayerInput input)
        {
            BotControlReason reasons = _reasons[slot];
            if (real)
            {
                _ticksWithoutInput[slot] = 0;
                reasons &= ~BotControlReason.NoInput;
                bool idle = input.Move.IsZero && input.Aim.IsZero && (input.Buttons & InputButtons.All) == 0;
                if (idle)
                {
                    if (_zeroInputTicks[slot] < int.MaxValue) _zeroInputTicks[slot]++;
                    if (_zeroInputTicks[slot] >= _afkTicks)
                    {
                        reasons |= BotControlReason.Afk;
                        _everAfk[slot] = true;
                    }
                }
                else
                {
                    _zeroInputTicks[slot] = 0;
                    reasons &= ~BotControlReason.Afk;
                }
            }
            else
            {
                if (_ticksWithoutInput[slot] < int.MaxValue) _ticksWithoutInput[slot]++;
                if (_ticksWithoutInput[slot] >= _takeoverTicks) reasons |= BotControlReason.NoInput;
            }
            Change(slot, reasons);
        }

        private void Change(int slot, BotControlReason reasons)
        {
            BotControlReason old = _reasons[slot];
            if (old == reasons) return;
            _reasons[slot] = reasons;
            bool was = old != BotControlReason.None;
            bool now = reasons != BotControlReason.None;
            if (was == now) return;
            if (now)
            {
                int index = _takeovers[slot]++;
                if ((reasons & BotControlReason.Fill) == 0) TakeoverCount++;
                _bots.SetBotActive(slot, true, _difficulty, _matchSeed, index);
            }
            else _bots.SetBotActive(slot, false, _difficulty, _matchSeed, 0);
        }
    }
}
