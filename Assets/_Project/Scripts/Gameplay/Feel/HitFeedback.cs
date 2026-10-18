using Arena.Core.Defs;
using Arena.Core.Sim;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data;
using Arena.Gameplay.View;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Turns the tick events (<see cref="SimEventBuffer"/>) into feedback (GDD §5.3, D-059). Called by the runner after every tick.
    /// <list type="bullet">
    /// <item>Every hit (<see cref="SimEventType.Damaged"/>) on a fighter the local team sees: flash of the target's body and a
    /// damage number (red for the local team's losses, large for an ult hit). If the local fighter dealt or took it: a short
    /// sound and a light vibration (rate-limited).</item>
    /// <item>An ult hit that the local fighter dealt or took: hit stop (views only, D-015), camera shake, a longer vibration.</item>
    /// <item>An ult fired by a fighter the local team sees: the ult sound. The local ult becoming charged: the ready signal.</item>
    /// <item>Healing of a seen fighter: a green number.</item>
    /// </list>
    /// Every output is optional (null = skipped), so the rules are testable without a scene. No allocations per tick.
    /// </summary>
    public sealed class HitFeedback
    {
        private readonly FeelConfig _feel;
        private readonly int _localSlot;
        private readonly int _localTeam;
        private readonly HitStop _hitStop;
        private readonly CameraShake _shake;
        private readonly Haptics _haptics;
        private readonly ProceduralSounds _sounds;
        private readonly DamageNumberPool _numbers;
        private readonly float[] _flashUntil = new float[SimLimits.MaxFighters];

        public HitFeedback(FeelConfig feel, int localSlot, int localTeam, HitStop hitStop, CameraShake shake, Haptics haptics,
            ProceduralSounds sounds, DamageNumberPool numbers)
        {
            _feel = feel;
            _localSlot = localSlot;
            _localTeam = localTeam;
            _hitStop = hitStop;
            _shake = shake;
            _haptics = haptics;
            _sounds = sounds;
            _numbers = numbers;
        }

        /// <summary>Counters for tests and debug.</summary>
        public int HitsShown { get; private set; }
        public int UltHitsFelt { get; private set; }
        public int UltReadySignals { get; private set; }
        public int UltSoundsPlayed { get; private set; }

        /// <summary>True while the fighter's body shows the flash material.</summary>
        public bool IsFlashing(int slot, float now) => (uint)slot < (uint)_flashUntil.Length && now < _flashUntil[slot];

        /// <param name="now">Unscaled time, seconds.</param>
        public void Process(SimEventBuffer events, MatchState state, float now)
        {
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly SimEvent e = ref events[i];
                switch (e.Type)
                {
                    case SimEventType.Damaged:
                        OnDamaged(in e, state, now);
                        break;
                    case SimEventType.Healed:
                        if (e.Value > 0 && IsSeen(state, e.Target) && _numbers != null)
                            _numbers.Spawn(WorldCoords.ToWorld(e.Position), e.Value, _feel.HealColor, false);
                        break;
                    case SimEventType.AbilityFired:
                        if (e.Value >= 0 && MatchDefs.SlotOf(e.Value) == AbilitySlot.Ult && IsSeen(state, e.Source))
                        {
                            UltSoundsPlayed++;
                            _sounds?.PlayUlt();
                        }
                        break;
                    case SimEventType.UltReady:
                        if (e.Source == _localSlot)
                        {
                            UltReadySignals++;
                            _sounds?.PlayUltReady();
                        }
                        break;
                }
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _flashUntil.Length; i++) _flashUntil[i] = 0f;
            _hitStop?.Reset();
            _shake?.Reset();
            _numbers?.Clear();
        }

        /// <summary>True if <paramref name="ability"/> (an ability reference) is an ult.</summary>
        public static bool IsUlt(int ability) => ability >= 0 && MatchDefs.SlotOf(ability) == AbilitySlot.Ult;

        private void OnDamaged(in SimEvent e, MatchState state, float now)
        {
            if (e.Value <= 0) return;
            int target = e.Target;
            bool ult = IsUlt(e.AbilityRef);
            if (IsSeen(state, target))
            {
                HitsShown++;
                if ((uint)target < (uint)_flashUntil.Length) _flashUntil[target] = now + _feel.FlashSeconds;
                if (_numbers != null)
                {
                    bool ownLoss = state.Fighters[target].Team == _localTeam;
                    _numbers.Spawn(WorldCoords.ToWorld(e.Position), e.Value, ownLoss ? _feel.DamageTakenColor : _feel.DamageColor, ult);
                }
            }

            bool local = e.Source == _localSlot || target == _localSlot;
            if (!local) return;
            _sounds?.PlayHit(now);
            if (ult)
            {
                UltHitsFelt++;
                _hitStop?.Trigger(_feel.HitStopSeconds);
                _shake?.Trigger(_feel.ShakeAmplitude, _feel.ShakeSeconds, _feel.ShakeFrequency);
                _haptics?.Vibrate(_feel.UltVibrationMs, now);
            }
            else
            {
                _haptics?.Vibrate(_feel.HitVibrationMs, now);
            }
        }

        /// <summary>Own team always; others by the visibility rules (D-024).</summary>
        private bool IsSeen(MatchState state, int slot)
        {
            if (slot < 0 || slot >= state.Globals.FighterCount) return false;
            ref FighterState f = ref state.Fighters[slot];
            return f.Team == _localTeam || f.IsVisibleTo(_localTeam);
        }
    }
}
