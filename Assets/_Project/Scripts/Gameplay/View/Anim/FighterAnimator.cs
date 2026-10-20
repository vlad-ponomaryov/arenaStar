using Arena.Core.Defs;
using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Procedural animation of one fighter slot (brief A-TheoAnim §2–3). Events start clips, <see cref="Evaluate"/> sums the layers
    /// (base idle/run or victory, shot / ult / trick, hurt, spawn, blinking, look, springs) into <see cref="Pose"/>, and
    /// <see cref="Apply"/> is the only place that writes bone transforms. Created once per slot at match start; no allocations
    /// afterwards.
    /// </summary>
    public sealed class FighterAnimator : IFighterAnimSink
    {
        /// <summary>Capacity of the spring list taken from the rig (array size, D-004).</summary>
        public const int MaxSprings = 4;
        private const int SpringAxes = 2;
        private const float Epsilon = 1e-6f;
        /// <summary>Knuth's multiplicative hash: spreads slot numbers into xorshift seeds.</summary>
        private const uint SeedHash = 2654435761u;
        private const float FloatFrom24Bits = 1f / 16777216f;

        private readonly FighterAnimConfig _config;
        private readonly int _slot;
        private readonly FighterSpring[] _springs = new FighterSpring[MaxSprings];
        private int _springCount;
        private float _topHeight;
        private FighterAttackStyle _attackStyle;
        private FighterAttackStyle _ultStyle;
        private FighterTrickStyle _trickStyle;
        private FighterAnimState _state;

        /// <summary>Bone offsets of the last <see cref="Evaluate"/>.</summary>
        public readonly PoseBuffer Pose = new PoseBuffer();

        public FighterAnimator(FighterAnimConfig config, int slot)
        {
            _config = config;
            _slot = slot;
            _state.Springs = new SpringState[MaxSprings * SpringAxes];
            ResetAll();
        }

        public FighterAnimState State => _state;
        public int Slot => _slot;
        public float TopHeight => _topHeight;
        public int SpringCount => _springCount;

        /// <summary>The defeat clip has played out: the view can be hidden.</summary>
        public bool DefeatFinished => _state.Dead && _state.DefeatElapsed >= _config.DefeatSeconds;

        /// <summary>Takes the per-fighter data (height, styles, springs) from the rig.</summary>
        public void Bind(FighterRig rig) =>
            Configure(rig.TopHeight, rig.AttackStyle, rig.UltStyle, rig.TrickStyle, rig.Springs);

        public void Configure(float topHeight, FighterAttackStyle attack, FighterAttackStyle ult, FighterTrickStyle trick,
            FighterSpring[] springs)
        {
            _topHeight = topHeight;
            _attackStyle = attack;
            _ultStyle = ult;
            _trickStyle = trick;
            _springCount = 0;
            if (springs != null)
            {
                for (int i = 0; i < springs.Length && _springCount < MaxSprings; i++) _springs[_springCount++] = springs[i];
            }
            ClearSprings();
        }

        public void OnMove(float speed, Vector3 worldDir, float dt)
        {
            if (dt <= 0f) return;
            float distance = speed * dt;
            if (distance > _config.TeleportDistance) return; // teleport / correction: no step, no acceleration

            worldDir.y = 0f;
            float len = worldDir.magnitude;
            Vector3 velocity = len > Epsilon ? worldDir * (speed / len) : Vector3.zero;

            float sdt = Mathf.Min(dt, _config.MaxDt);
            float k = 1f - Mathf.Exp(-_config.SpeedSmoothing * sdt);
            Vector3 prev = _state.SmoothVelocity;
            Vector3 next = prev + (velocity - prev) * k;

            Vector3 fwd = _state.Look;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 accel = (next - prev) / sdt;
            _state.LocalAccel = new Vector3(Vector3.Dot(accel, right), 0f, Vector3.Dot(accel, fwd));

            float leanTarget = 0f;
            if (prev.sqrMagnitude > Epsilon && next.sqrMagnitude > Epsilon)
            {
                float turnRate = Vector3.SignedAngle(prev, next, Vector3.up) / sdt;
                float speed01 = Mathf.Clamp01(next.magnitude / _config.RunSpeedRef);
                leanTarget = Mathf.Clamp(turnRate / _config.LeanTurnRateRef, -1f, 1f) * speed01;
            }
            _state.Lean += (leanTarget - _state.Lean) * k;
            _state.SmoothVelocity = next;

            float stride = _config.StrideLengthPerHeight * _topHeight;
            _state.Phase = PuppetPose.AdvanceRunPhase(_state.Phase, distance, stride, _config.TeleportDistance);
        }

        public void OnShoot(AbilitySlot slot, Vector3 aimDir)
        {
            if (_state.Dead) return;
            switch (slot)
            {
                case AbilitySlot.Attack:
                    _state.ShotLeft = _config.ShotSeconds;
                    if (_attackStyle == FighterAttackStyle.Shoot) RequestMuzzle(1f);
                    break;
                case AbilitySlot.Ult:
                    _state.UltLeft = _config.UltSeconds;
                    _state.UltShotStarted = false;
                    break;
                case AbilitySlot.Trick:
                    if (_trickStyle != FighterTrickStyle.None) _state.TrickLeft = _config.TrickSeconds;
                    break;
            }
        }

        public void OnHurt(Vector3 fromDir, bool heavy)
        {
            if (_state.Dead) return;
            _state.HurtLeft = _config.HurtSeconds;
            _state.HurtStrength = heavy ? _config.HurtHeavyScale : 1f;
            _state.HurtFrom = ToLocal(fromDir);
        }

        public void OnSpawn()
        {
            ResetAll();
            _state.SpawnLeft = _config.SpawnSeconds;
        }

        public void OnDeath()
        {
            if (_state.Dead) return;
            _state.Dead = true;
            _state.DefeatElapsed = 0f;
            _state.ShotLeft = 0f;
            _state.UltLeft = 0f;
            _state.TrickLeft = 0f;
            _state.HurtLeft = 0f;
            _state.SpawnLeft = 0f;
            _state.MuzzlePending = false;
        }

        public void OnMatchEnd(bool won)
        {
            _state.Ended = true;
            _state.Won = won;
            _state.VictoryTime = 0f;
        }

        public void SetStatus(bool stunned, bool dashing)
        {
            _state.Stunned = stunned;
            _state.Dashing = dashing;
        }

        public void SetLook(Vector3 worldDir)
        {
            worldDir.y = 0f;
            float len = worldDir.magnitude;
            if (len > Epsilon) _state.Look = worldDir / len;
        }

        /// <summary>
        /// True once per requested muzzle flash (attack shot, the shot part of the ult); <paramref name="scale"/> — size factor.
        /// Read after <see cref="Apply"/>, so the flash sits at the recoiling barrel.
        /// </summary>
        public bool ConsumeMuzzle(out float scale)
        {
            scale = _state.MuzzleScale;
            if (!_state.MuzzlePending) return false;
            _state.MuzzlePending = false;
            return true;
        }

        /// <summary>Starts a blink now (previews, the eyelid check); the regular blink timer is not changed.</summary>
        public void TriggerBlink() => _state.BlinkLeft = _config.BlinkSeconds;

        public void ResetAll()
        {
            SpringState[] springs = _state.Springs;
            _state = default;
            _state.Springs = springs;
            ClearSprings();
            _state.Look = Vector3.forward;
            uint seed = (uint)(_slot + 1) * SeedHash;
            _state.Rng = seed == 0u ? 1u : seed;
            _state.NextBlinkIn = NextBlinkInterval();
            Pose.Clear();
        }

        /// <summary>Advances the clips by <paramref name="dt"/> (frame seconds) and sums the layers into <see cref="Pose"/>.</summary>
        public void Evaluate(float dt)
        {
            FighterAnimConfig c = _config;
            if (dt < 0f) dt = 0f;
            float sdt = Mathf.Min(dt, c.MaxDt);
            Pose.Clear();

            if (_state.Dead)
            {
                _state.DefeatElapsed += dt;
                PuppetPose.Defeat(Pose, c, _state.DefeatElapsed / c.DefeatSeconds);
                return;
            }

            // 1. Base
            if (_state.Ended && _state.Won)
            {
                _state.VictoryTime = c.VictoryHopHz > 0f ? Mathf.Repeat(_state.VictoryTime + dt, 1f / c.VictoryHopHz) : 0f;
                PuppetPose.Victory(Pose, c, _state.VictoryTime, 1f);
            }
            else
            {
                _state.IdleTime = c.BreathHz > 0f ? Mathf.Repeat(_state.IdleTime + dt, 1f / c.BreathHz) : 0f;
                float speed = _state.SmoothVelocity.magnitude;
                float runTarget = speed > c.RunThreshold ? 1f : 0f;
                _state.RunWeight = Mathf.MoveTowards(_state.RunWeight, runTarget, dt / c.BlendSeconds);
                float idleWeight = 1f - _state.RunWeight;
                if (idleWeight > 0f) PuppetPose.Idle(Pose, c, _state.IdleTime, idleWeight);
                if (_state.RunWeight > 0f)
                {
                    float speed01 = Mathf.Clamp01(speed / c.RunSpeedRef);
                    PuppetPose.Run(Pose, c, _state.Phase, speed01, _state.Lean, _state.RunWeight);
                    PuppetPose.Look(Pose, c, Mathf.Clamp(_state.Lean, -1f, 1f), _state.RunWeight);
                }
            }

            // 2. Actions
            if (_state.ShotLeft > 0f)
            {
                _state.ShotLeft -= dt;
                PuppetPose.Shot(Pose, c, Progress(_state.ShotLeft, c.ShotSeconds), _attackStyle, 1f, 1f);
            }
            if (_state.UltLeft > 0f)
            {
                _state.UltLeft -= dt;
                float u = Progress(_state.UltLeft, c.UltSeconds);
                if (!_state.UltShotStarted && u >= c.UltCrouchFraction)
                {
                    _state.UltShotStarted = true;
                    if (_ultStyle == FighterAttackStyle.Shoot) RequestMuzzle(c.UltShotScale);
                }
                PuppetPose.Ult(Pose, c, u, _ultStyle, 1f);
            }
            if (_state.TrickLeft > 0f)
            {
                _state.TrickLeft -= dt;
                // A dash longer than the clip: keep rolling until it ends.
                if (_state.TrickLeft <= 0f && _state.Dashing) _state.TrickLeft += c.TrickSeconds;
                PuppetPose.Trick(Pose, c, Progress(_state.TrickLeft, c.TrickSeconds), _trickStyle, 1f);
            }
            _state.StunWeight = Mathf.MoveTowards(_state.StunWeight, _state.Stunned ? 1f : 0f, dt / c.BlendSeconds);
            if (_state.StunWeight > 0f)
            {
                _state.StunTime = c.StunWobbleHz > 0f ? Mathf.Repeat(_state.StunTime + dt, 1f / c.StunWobbleHz) : 0f;
                PuppetPose.Stunned(Pose, c, _state.StunTime, _state.StunWeight);
            }

            // 3. Reactions
            if (_state.HurtLeft > 0f)
            {
                _state.HurtLeft -= dt;
                PuppetPose.Hurt(Pose, c, Progress(_state.HurtLeft, c.HurtSeconds), _state.HurtFrom, _state.HurtStrength, 1f);
            }
            if (_state.SpawnLeft > 0f)
            {
                _state.SpawnLeft -= dt;
                PuppetPose.Spawn(Pose, c, Progress(_state.SpawnLeft, c.SpawnSeconds), 1f);
            }

            // 4. Face
            UpdateBlink(dt);

            // 5. Springs
            UpdateSprings(sdt);
        }

        /// <summary>Writes the pose into the rig's bones (rest × offset); bones the rig does not have are skipped.</summary>
        public void Apply(FighterRig rig)
        {
            Transform[] bones = rig.Bones;
            int n = Mathf.Min(bones.Length, PoseBuffer.BoneCount);
            for (int i = 0; i < n; i++)
            {
                Transform t = bones[i];
                if (t == null) continue;
                BonePose p = Pose.Bones[i];
                Quaternion restRotation = rig.RestRotations[i];
                t.SetLocalPositionAndRotation(rig.RestPositions[i] + restRotation * p.Position,
                    restRotation * Quaternion.Euler(p.Euler));
                t.localScale = BonePose.ComposeScale(rig.RestScales[i], p.Scale);
            }
        }

        private void RequestMuzzle(float scale)
        {
            _state.MuzzlePending = true;
            _state.MuzzleScale = scale;
        }

        private void UpdateBlink(float dt)
        {
            FighterAnimConfig c = _config;
            if (_state.BlinkLeft > 0f)
            {
                _state.BlinkLeft -= dt;
                PuppetPose.Blink(Pose, Progress(_state.BlinkLeft, c.BlinkSeconds));
            }

            _state.NextBlinkIn -= dt;
            if (_state.NextBlinkIn > 0f) return;

            _state.BlinkLeft = c.BlinkSeconds;
            _state.BlinkCount++;
            if (!_state.DoubleBlinkUsed && NextFloat01() < c.DoubleBlinkChance)
            {
                _state.DoubleBlinkUsed = true;
                _state.NextBlinkIn += 2f * c.BlinkSeconds; // closed-open, a pause of one blink, then the second one
            }
            else
            {
                _state.DoubleBlinkUsed = false;
                _state.NextBlinkIn += NextBlinkInterval();
            }
            if (_state.NextBlinkIn <= 0f) _state.NextBlinkIn = NextBlinkInterval(); // after a long frame spike
        }

        private void UpdateSprings(float sdt)
        {
            float max = _config.SpringMaxDegrees;
            Vector3 a = _state.LocalAccel;
            SpringState[] states = _state.Springs;
            for (int i = 0; i < _springCount; i++)
            {
                FighterSpring spring = _springs[i];
                // The bone lags behind the body: forward acceleration swings it back, sideways acceleration to the other side.
                float targetPitch = Mathf.Clamp(-spring.Amount * a.z, -max, max);
                float targetRoll = Mathf.Clamp(spring.Amount * a.x, -max, max);
                ref SpringState pitch = ref states[i * SpringAxes];
                ref SpringState roll = ref states[i * SpringAxes + 1];
                SpringMath.Step(ref pitch, targetPitch, spring.Frequency, spring.Damping, sdt);
                SpringMath.Step(ref roll, targetRoll, spring.Frequency, spring.Damping, sdt);
                SpringMath.Clamp(ref pitch, max);
                SpringMath.Clamp(ref roll, max);
                Pose.AddEuler(spring.Bone, new Vector3(pitch.Angle, 0f, roll.Angle), 1f);
            }
        }

        private void ClearSprings()
        {
            SpringState[] springs = _state.Springs;
            for (int i = 0; i < springs.Length; i++) springs[i] = default;
        }

        /// <summary>World direction → body frame (x right, z forward), flat and normalized.</summary>
        private Vector3 ToLocal(Vector3 worldDir)
        {
            worldDir.y = 0f;
            float len = worldDir.magnitude;
            if (len <= Epsilon) return Vector3.zero;
            worldDir /= len;
            Vector3 fwd = _state.Look;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            return new Vector3(Vector3.Dot(worldDir, right), 0f, Vector3.Dot(worldDir, fwd));
        }

        private static float Progress(float left, float duration) => 1f - Mathf.Max(left, 0f) / duration;

        private float NextBlinkInterval() =>
            Mathf.Lerp(_config.BlinkIntervalMin, _config.BlinkIntervalMax, NextFloat01());

        private float NextFloat01()
        {
            uint x = _state.Rng;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state.Rng = x;
            return (x >> 8) * FloatFrom24Bits;
        }
    }
}
