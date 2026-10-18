using System;
using Arena.Core.Math;

namespace Arena.Core.State
{
    /// <summary>
    /// One projectile slot of <see cref="MatchState.Projectiles"/> (D-002): blittable, no references. Moves along
    /// <see cref="Dir"/> until <see cref="RemainingDistance"/> runs out or it hits a target (segment–circle test).
    /// When adding a field: also add it to <see cref="Equals(ProjectileState)"/> (StateEqualityTests).
    /// </summary>
    public struct ProjectileState : IEquatable<ProjectileState>
    {
        public bool Active;

        /// <summary>Fighter slot that fired it (stays valid after the owner's death).</summary>
        public int Owner;
        public int Team;

        /// <summary>Tick it was fired: views use it to tell a reused slot from the same projectile.</summary>
        public int SpawnTick;

        public Vec2 Position;

        /// <summary>Unit direction of flight.</summary>
        public Vec2 Dir;

        /// <summary>Cells per second.</summary>
        public float Speed;

        /// <summary>Hit radius in cells.</summary>
        public float Radius;

        public float RemainingDistance;

        /// <summary>Ability whose effects apply on hit (<c>MatchDefs.MakeAbilityRef</c>).</summary>
        public int AbilityRef;

        /// <summary>Further targets it may pass through.</summary>
        public int PierceLeft;

        /// <summary>Further jumps to a new target after a hit (<c>AbilityDef.BounceCount</c>).</summary>
        public int BounceLeft;

        /// <summary>Bit per fighter slot already hit (the owner's bit is set at spawn): no repeated hits.</summary>
        public uint HitMask;

        /// <summary>Targets hit so far (pierce falloff: <c>AbilityDef.PierceFalloffPercent</c> after the first).</summary>
        public int Hits;

        /// <summary>Damage of the shot in percent of the effect values (100 + shot boost, <c>FighterState.ShotBonusPercent</c>).</summary>
        public int DamagePercent;

        public bool Equals(ProjectileState o)
        {
            return Active == o.Active && Owner == o.Owner && Team == o.Team && SpawnTick == o.SpawnTick
                && Position == o.Position && Dir == o.Dir && Speed == o.Speed && Radius == o.Radius
                && RemainingDistance == o.RemainingDistance && AbilityRef == o.AbilityRef && PierceLeft == o.PierceLeft && BounceLeft == o.BounceLeft
                && HitMask == o.HitMask && Hits == o.Hits && DamagePercent == o.DamagePercent;
        }

        public override bool Equals(object obj) => obj is ProjectileState other && Equals(other);
        public override int GetHashCode() => (Owner * 397) ^ SpawnTick ^ Position.GetHashCode();
    }
}
