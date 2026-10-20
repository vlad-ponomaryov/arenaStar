using System;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim.Events;
using Arena.Core.State;
using Arena.Data.Audio;

namespace Arena.Gameplay.Audio
{
    /// <summary>
    /// Sounds of the fighters that have a <see cref="FighterSoundSet"/> (brief A-TheoSfx), from the tick events and the state:
    /// <list type="bullet">
    /// <item>Steps: every <see cref="FighterSoundSet.StepDistance"/> run (simulation positions; jumps longer than
    /// <see cref="FighterSoundSet.StepTeleportDistance"/> do not count).</item>
    /// <item><see cref="SimEventType.AbilityFired"/>: attack → Shot; ult → Ult + VoEffort.</item>
    /// <item><see cref="SimEventType.Damaged"/>: the attacker's Hit and the victim's Hurt + VoHurt, at the victim.</item>
    /// <item><see cref="SimEventType.MatchEnded"/>: the local fighter's VoWin / VoLose (nothing on a draw).</item>
    /// </list>
    /// Only what the local team sees (own team always, enemies by D-024). The local fighter's own sounds are flat (full volume,
    /// centre); others fade with distance from the local fighter and pan (<see cref="SfxSpatial"/>). Cooldowns and
    /// "no same variant twice" per slot and cue (<see cref="SfxCueState"/>). No allocations per tick.
    /// </summary>
    public sealed class FighterSfx
    {
        private readonly ISfxOutput _output;
        private readonly AudioConfig _config;
        private readonly FighterSoundSet[] _sets;
        private readonly int _localSlot;
        private readonly int _localTeam;
        private readonly SfxCueState _cues;
        private readonly Random _rng;
        private readonly float[] _stepRun;
        private readonly Vec2[] _lastPosition;
        private readonly bool[] _hasLast;
        private readonly int[] _played = new int[FighterSoundSet.KindCount];
        private Vec2 _screenRight = Vec2.Right;
        private bool _ended;

        /// <param name="setsBySlot">Sound set of each fighter slot (null = none; that fighter keeps the procedural sounds).</param>
        public FighterSfx(ISfxOutput output, AudioConfig config, FighterSoundSet[] setsBySlot, int localSlot, int localTeam, int seed)
        {
            _output = output;
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _sets = setsBySlot ?? throw new ArgumentNullException(nameof(setsBySlot));
            _localSlot = localSlot;
            _localTeam = localTeam;
            int slots = Math.Max(1, _sets.Length);
            _cues = new SfxCueState(slots, FighterSoundSet.KindCount);
            _rng = new Random(seed);
            _stepRun = new float[slots];
            _lastPosition = new Vec2[slots];
            _hasLast = new bool[slots];
        }

        /// <summary>True if the fighter in <paramref name="slot"/> has its own sounds.</summary>
        public bool HasSet(int slot) => (uint)slot < (uint)_sets.Length && _sets[slot] != null;

        /// <summary>Sounds played of a cue (tests, debug).</summary>
        public int PlayedCount(FighterSoundKind kind) => _played[(int)kind];

        /// <summary>Unit direction "right on screen" in simulation coordinates (camera right on the ground), for the pan.</summary>
        public void SetScreenRight(Vec2 right)
        {
            float len = right.Length;
            _screenRight = len > 0f ? right / len : Vec2.Right;
        }

        /// <param name="now">Unscaled time, seconds.</param>
        public void Process(SimEventBuffer events, MatchState state, float now)
        {
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly SimEvent e = ref events[i];
                switch (e.Type)
                {
                    case SimEventType.AbilityFired:
                        OnFired(in e, state, now);
                        break;
                    case SimEventType.Damaged:
                        OnDamaged(in e, state, now);
                        break;
                    case SimEventType.MatchEnded:
                        OnMatchEnded(e.Value, state, now);
                        break;
                }
            }
            UpdateSteps(state, now);
        }

        public void Reset()
        {
            _cues.Reset();
            _ended = false;
            for (int i = 0; i < _hasLast.Length; i++)
            {
                _hasLast[i] = false;
                _stepRun[i] = 0f;
            }
            for (int i = 0; i < _played.Length; i++) _played[i] = 0;
        }

        private void OnFired(in SimEvent e, MatchState state, float now)
        {
            if (!HasSet(e.Source) || e.Value < 0 || !IsSeen(state, e.Source)) return;
            AbilitySlot slot = MatchDefs.SlotOf(e.Value);
            if (slot == AbilitySlot.Attack)
            {
                Play(e.Source, FighterSoundKind.Shot, e.Position, state, now);
            }
            else if (slot == AbilitySlot.Ult)
            {
                Play(e.Source, FighterSoundKind.Ult, e.Position, state, now);
                Play(e.Source, FighterSoundKind.VoEffort, e.Position, state, now);
            }
        }

        private void OnDamaged(in SimEvent e, MatchState state, float now)
        {
            if (e.Value <= 0 || !IsSeen(state, e.Target)) return;
            if (HasSet(e.Source) && e.AbilityRef >= 0) Play(e.Source, FighterSoundKind.Hit, e.Position, state, now);
            if (HasSet(e.Target))
            {
                Play(e.Target, FighterSoundKind.Hurt, e.Position, state, now);
                Play(e.Target, FighterSoundKind.VoHurt, e.Position, state, now);
            }
        }

        private void OnMatchEnded(int winner, MatchState state, float now)
        {
            if (_ended) return;
            _ended = true;
            if (winner < 0 || !HasSet(_localSlot)) return;
            Vec2 at = state.Fighters[_localSlot].Position;
            Play(_localSlot, winner == _localTeam ? FighterSoundKind.VoWin : FighterSoundKind.VoLose, at, state, now);
        }

        private void UpdateSteps(MatchState state, float now)
        {
            int count = Math.Min(state.Globals.FighterCount, _sets.Length);
            for (int i = 0; i < count; i++)
            {
                FighterSoundSet set = _sets[i];
                if (set == null) continue;
                ref FighterState f = ref state.Fighters[i];
                if (!f.Alive)
                {
                    _hasLast[i] = false;
                    _stepRun[i] = 0f;
                    continue;
                }
                if (_hasLast[i])
                {
                    float d = Vec2.Distance(f.Position, _lastPosition[i]);
                    if (d > set.StepTeleportDistance)
                    {
                        _stepRun[i] = 0f;
                    }
                    else
                    {
                        _stepRun[i] += d;
                        if (_stepRun[i] >= set.StepDistance)
                        {
                            // One step per tick at most; a dash that covers several strides gives one step.
                            _stepRun[i] -= set.StepDistance;
                            if (_stepRun[i] >= set.StepDistance) _stepRun[i] = 0f;
                            if (IsSeen(state, i)) Play(i, FighterSoundKind.Step, f.Position, state, now);
                        }
                    }
                }
                _lastPosition[i] = f.Position;
                _hasLast[i] = true;
            }
        }

        private void Play(int slot, FighterSoundKind kind, Vec2 at, MatchState state, float now)
        {
            SfxCue cue = _sets[slot].Cue(kind);
            if (cue == null || !cue.HasClips) return;
            bool own = slot == _localSlot;
            if (cue.LocalOnly && !own) return;

            float gain = 1f;
            float pan = 0f;
            if (!own)
            {
                Vec2 offset = at - state.Fighters[_localSlot].Position;
                gain = SfxSpatial.DistanceGain(offset.Length, _config.FullVolumeDistance, _config.SilentDistance);
                if (gain <= 0f) return;
                pan = SfxSpatial.Pan(Vec2.Dot(offset, _screenRight), _config.PanDistance, _config.PanStrength);
            }
            int k = (int)kind;
            if (!_cues.TryUse(slot, k, cue.Cooldown, now)) return;
            UnityEngine.AudioClip clip = cue.Clips[_cues.PickClip(slot, k, cue.Clips.Length, _rng)];
            float pitch = 1f + cue.PitchJitter * (float)(_rng.NextDouble() * 2.0 - 1.0);
            if (_output != null && _output.Play(clip, cue.Channel, cue.Volume * gain, pitch, pan, cue.Priority)) _played[k]++;
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
