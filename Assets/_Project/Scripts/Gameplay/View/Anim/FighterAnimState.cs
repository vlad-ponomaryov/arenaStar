using UnityEngine;

namespace Arena.Gameplay.View.Anim
{
    /// <summary>
    /// Everything a <see cref="FighterAnimator"/> remembers between frames (view only, not part of the match snapshot).
    /// Timers are frame seconds; a one-shot clip runs while its "Left" timer is above 0.
    /// </summary>
    public struct FighterAnimState
    {
        // Base
        public float IdleTime;
        /// <summary>Step phase, cycles 0–1.</summary>
        public float Phase;
        /// <summary>Weight of the run state (idle = 1 − this).</summary>
        public float RunWeight;
        /// <summary>Lean into the turn, −1…1 (+ = right).</summary>
        public float Lean;
        public Vector3 SmoothVelocity;
        /// <summary>Acceleration of the smoothed velocity in the body frame (x right, z forward), units/s².</summary>
        public Vector3 LocalAccel;
        /// <summary>Body facing (flat, normalized).</summary>
        public Vector3 Look;

        // Actions and reactions
        public float ShotLeft;
        public float UltLeft;
        public float TrickLeft;
        public float HurtLeft;
        public float SpawnLeft;
        public Vector3 HurtFrom;
        public float HurtStrength;
        /// <summary>The shot part of the running ult has started (its muzzle flash was requested).</summary>
        public bool UltShotStarted;
        /// <summary>A muzzle flash waits to be spawned after the bones are written; its size factor.</summary>
        public bool MuzzlePending;
        public float MuzzleScale;

        // Status and end of life / match
        public bool Stunned;
        public float StunTime;
        /// <summary>Weight of the stunned wobble (blends in and out over BlendSeconds).</summary>
        public float StunWeight;
        public bool Dashing;
        public bool Dead;
        public float DefeatElapsed;
        public bool Ended;
        public bool Won;
        public float VictoryTime;

        // Face
        public float BlinkLeft;
        public float NextBlinkIn;
        public bool DoubleBlinkUsed;
        public int BlinkCount;
        /// <summary>xorshift32 state, seeded by the slot (deterministic, no allocations).</summary>
        public uint Rng;

        /// <summary>Two axes (pitch, roll) per rig spring; allocated once by the animator.</summary>
        public SpringState[] Springs;
    }
}
