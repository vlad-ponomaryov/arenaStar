using System;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Net.Snapshots;

namespace Arena.Net.Client
{
    /// <summary>
    /// Prediction and reconciliation of the own fighter (Server-Spec §2.5, D-080). A shadow <see cref="MatchSimulation"/> with the
    /// match Defs runs in prediction mode. On every new newest snapshot S (tick Ts): the prediction error on Ts is measured
    /// against the exact server state (history of predicted own states by tick), the shadow is rebuilt from S
    /// (<see cref="ShadowStateBuilder"/>, from tick 0 during the countdown) and ticks Ts … Tpred − 1 are re-simulated with the
    /// inputs from the history — always, no "skip if equal". Every new prediction tick then runs one shadow tick.
    /// The view of the own fighter = predicted position + a correction offset: a reconciliation adds the jump of the predicted
    /// position to the offset, which decays exponentially (<see cref="ClientMatchSettings.CorrectionSmoothingSeconds"/>); a jump
    /// longer than <see cref="ClientMatchSettings.SnapDistanceCells"/> or a respawn places the view at once.
    /// A snapshot with "a bot controls your fighter" turns prediction off: the shadow is the server state, nothing is re-simulated.
    /// No allocations after construction.
    /// </summary>
    public sealed class Predictor
    {
        private const int HistoryMask = NetLimits.InputBufferTicks - 1;

        private readonly ClientMatchSettings _settings;
        private readonly PlayerInput[] _inputs = new PlayerInput[SimLimits.MaxFighters];
        private readonly FighterState[] _history = new FighterState[NetLimits.InputBufferTicks];
        private readonly int[] _historyTick = new int[NetLimits.InputBufferTicks];

        public Predictor(MatchDefs defs, MatchSetup setup, int ownSlot, ClientMatchSettings settings)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if ((uint)ownSlot >= (uint)setup.SlotCount) throw new ArgumentOutOfRangeException(nameof(ownSlot));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Defs = defs;
            Setup = setup;
            OwnSlot = ownSlot;
            OwnTeam = setup.Slots[ownSlot].Team;
            Shadow = new MatchSimulation(defs, setup);
            Errors = new PredictionErrorStats(settings.LargeCorrectionCells);
            for (int i = 0; i < _historyTick.Length; i++) _historyTick[i] = int.MinValue;
        }

        public MatchDefs Defs { get; }
        public MatchSetup Setup { get; }
        public int OwnSlot { get; }
        public int OwnTeam { get; }

        /// <summary>The shadow simulation; its state is at <see cref="ShadowTick"/> (the next tick to predict).</summary>
        public MatchSimulation Shadow { get; }

        public PredictionErrorStats Errors { get; }

        /// <summary>A snapshot was applied: the shadow holds a real state.</summary>
        public bool Ready { get; private set; }

        /// <summary>The newest snapshot said a bot controls the own fighter: no prediction.</summary>
        public bool BotControlled { get; private set; }

        /// <summary>Tick of the newest applied snapshot.</summary>
        public int SnapshotTick { get; private set; }

        public int ShadowTick => Shadow.State.Globals.Tick;

        /// <summary>The own fighter as predicted now.</summary>
        public ref FighterState Own => ref Shadow.State.Fighters[OwnSlot];

        /// <summary>View offset of the own fighter (decays to zero).</summary>
        public Vec2 CorrectionOffset { get; private set; }

        /// <summary>Where the own fighter is drawn: predicted position + correction offset.</summary>
        public Vec2 DisplayPosition => Shadow.State.Fighters[OwnSlot].Position + CorrectionOffset;

        /// <summary>
        /// The correction offset being smoothed now came (at least partly) from something the prediction could not know — a push,
        /// a dash it did not start, a stun (Server-Spec §2.5): the fast catch-up is not a "teleport" (§11.1).
        /// </summary>
        public bool ExternalCorrection { get; private set; }

        /// <summary>Corrections longer than <see cref="ClientMatchSettings.SnapDistanceCells"/> placed at once; of them with an external cause.</summary>
        public long Snaps { get; private set; }
        public long ExternalSnaps { get; private set; }

        public long Reconciliations { get; private set; }
        public long ResimulatedTicks { get; private set; }

        /// <summary>The last reconciliation wanted more than <see cref="ClientMatchSettings.MaxResimTicks"/> ticks (the clock must jump back).</summary>
        public bool ResimLimitHit { get; private set; }

        /// <summary>
        /// Applies the new newest snapshot and re-simulates up to <paramref name="predictionTick"/> (exclusive) with
        /// <paramref name="inputs"/>. At most <see cref="ClientMatchSettings.MaxResimTicks"/> ticks: beyond, <see cref="ResimLimitHit"/>.
        /// <paramref name="shown"/> — the presentation state (foreign fighters at their shown places, §2.5), or null before the
        /// first display tick (then at the snapshot's places).
        /// </summary>
        public void Reconcile(SnapshotFrame frame, InputHistory inputs, int predictionTick, MatchState shown = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            Reconciliations++;
            ResimLimitHit = false;
            int ts = frame.Tick;
            int baseTick = ts < 0 ? 0 : ts;
            SnapshotTick = ts;

            bool hadView = Ready && !BotControlled;
            int oldTick = ShadowTick;
            Vec2 oldPosition = Own.Position;
            bool oldAlive = Own.Alive;

            ref FighterState server = ref frame.Own;
            bool external = server.KnockbackTicks > 0 || server.IsDashing || server.Stunned;
            if (hadView && ts > 0 && TryGetHistory(ts, out FighterState predicted))
            {
                bool sampleExternal = IsExternal(in predicted, in server);
                Errors.Add(Vec2.Distance(predicted.Position, server.Position), sampleExternal);
                external |= sampleExternal;
            }

            ShadowStateBuilder.Build(Shadow.State, frame, Defs, Setup, OwnSlot, baseTick);
            if (shown != null) ShadowStateBuilder.ApplyShownOthers(Shadow.State, shown, OwnSlot, OwnTeam);
            Ready = true;
            BotControlled = frame.Header.BotControlled;
            Record();
            if (BotControlled)
            {
                CorrectionOffset = Vec2.Zero;
                ExternalCorrection = false;
                return;
            }

            int end = predictionTick;
            if (end - baseTick > _settings.MaxResimTicks)
            {
                end = baseTick + _settings.MaxResimTicks;
                ResimLimitHit = true;
            }
            while (ShadowTick < end && !Shadow.IsEnded)
            {
                RunTick(inputs.Get(ShadowTick));
                ResimulatedTicks++;
            }

            if (!hadView || oldTick != ShadowTick || !oldAlive || !Own.Alive)
            {
                CorrectionOffset = Vec2.Zero;
                ExternalCorrection = false;
                return;
            }
            Vec2 jump = oldPosition - Own.Position;
            if (!jump.IsZero && external) ExternalCorrection = true;
            Vec2 offset = CorrectionOffset + jump;
            if (offset.Length > _settings.SnapDistanceCells)
            {
                CorrectionOffset = Vec2.Zero;
                Snaps++;
                if (external) ExternalSnaps++;
                return;
            }
            CorrectionOffset = offset;
        }

        /// <summary>
        /// Predicts tick <paramref name="tick"/> with the (quantized) own input; the shadow must stand at that tick. True if a
        /// tick ran (its events are in <c>Shadow.Events</c>).
        /// </summary>
        public bool Predict(int tick, in PlayerInput input)
        {
            if (!Ready || BotControlled || Shadow.IsEnded || tick != ShadowTick) return false;
            bool wasAlive = Own.Alive;
            RunTick(in input);
            if (!wasAlive && Own.Alive)
            {
                CorrectionOffset = Vec2.Zero; // respawn: no sliding
                ExternalCorrection = false;
            }
            return true;
        }

        /// <summary>Before a new prediction tick: foreign fighters of the shadow move to their shown places (§2.5).</summary>
        public void SetShownOthers(MatchState shown)
        {
            if (shown == null || !Ready || BotControlled) return;
            ShadowStateBuilder.ApplyShownOthers(Shadow.State, shown, OwnSlot, OwnTeam);
        }

        /// <summary>Decays the correction offset over <paramref name="seconds"/> of real time.</summary>
        public void Decay(float seconds)
        {
            if (seconds <= 0f || CorrectionOffset.IsZero) return;
            float tau = _settings.CorrectionSmoothingSeconds;
            if (tau <= 0f)
            {
                CorrectionOffset = Vec2.Zero;
                return;
            }
            CorrectionOffset *= MathF.Exp(-seconds / tau);
            if (CorrectionOffset.Length <= Serialization.Quantize.PositionStep)
            {
                CorrectionOffset = Vec2.Zero;
                ExternalCorrection = false;
            }
        }

        /// <summary>The predicted own state at the start of <paramref name="tick"/> (after the ticks before it), if still in the history.</summary>
        public bool TryGetHistory(int tick, out FighterState state)
        {
            int i = tick & HistoryMask;
            if (_historyTick[i] == tick)
            {
                state = _history[i];
                return true;
            }
            state = default;
            return false;
        }

        private void RunTick(in PlayerInput input)
        {
            _inputs[OwnSlot] = input;
            Shadow.Tick(_inputs);
            Record();
        }

        private void Record()
        {
            int tick = ShadowTick;
            int i = tick & HistoryMask;
            _historyTick[i] = tick;
            _history[i] = Shadow.State.Fighters[OwnSlot];
        }

        /// <summary>
        /// The server state differs from the prediction by something the shadow cannot know (Server-Spec §2.5): a push or pull,
        /// a stun or slow, a dash it did not start (hook), damage, ult charge from damage dealt (a talent may depend on it), a defeat
        /// or respawn.
        /// </summary>
        private static bool IsExternal(in FighterState predicted, in FighterState server) =>
            predicted.Alive != server.Alive || server.Health < predicted.Health || server.UltCharge > predicted.UltCharge
            || predicted.KnockbackTicks != server.KnockbackTicks || predicted.KnockbackVelocity != server.KnockbackVelocity
            || predicted.DashTicksLeft != server.DashTicksLeft || predicted.DashAbilityRef != server.DashAbilityRef
            || predicted.Stunned != server.Stunned || predicted.SpeedMul != server.SpeedMul;
    }
}
