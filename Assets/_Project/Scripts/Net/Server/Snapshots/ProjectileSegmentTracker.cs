using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using Arena.Net.Protocol;
using Arena.Net.Serialization;
using Arena.Net.Snapshots;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// Turns projectile slots into segments (Server-Spec §4.4 item 5), once per server tick for all clients of a match: a new
    /// projectile (new <c>SpawnTick</c>) or a change of direction (bounce) starts a segment at the current position; pierces
    /// only update the counters and the hit mask; a projectile that went inactive becomes an ended entry (end tick, last point)
    /// and is forgotten after <c>ProjectileEndLingerTicks</c>. Preallocated per slot, no allocations.
    /// </summary>
    public sealed class ProjectileSegmentTracker
    {
        private readonly NetProjectileSegment[] _segments = new NetProjectileSegment[SimLimits.MaxProjectiles];
        private readonly bool[] _has = new bool[SimLimits.MaxProjectiles];
        private readonly Vec2[] _lastDir = new Vec2[SimLimits.MaxProjectiles];

        /// <summary>Where the projectile is now (or ended) — for the area of interest and priorities.</summary>
        private readonly Vec2[] _point = new Vec2[SimLimits.MaxProjectiles];

        public int SlotCount => _segments.Length;

        public bool Has(int slot) => _has[slot];

        public ref NetProjectileSegment Get(int slot) => ref _segments[slot];

        public Vec2 PointOf(int slot) => _point[slot];

        public void Reset()
        {
            for (int i = 0; i < _has.Length; i++)
            {
                _has[i] = false;
                _segments[i] = default;
            }
        }

        public void Capture(MatchState state, int lingerTicks)
        {
            int tick = state.Globals.Tick;
            ProjectileState[] items = state.Projectiles;
            for (int i = 0; i < items.Length; i++)
            {
                ref ProjectileState p = ref items[i];
                ref NetProjectileSegment s = ref _segments[i];
                if (p.Active)
                {
                    bool fresh = !_has[i] || s.Ended || s.SpawnTick != p.SpawnTick;
                    if (fresh || p.Dir != _lastDir[i])
                    {
                        bool bounce = !fresh;
                        s = default;
                        s.SpawnTick = p.SpawnTick;
                        s.Owner = p.Owner;
                        s.AbilityRef = p.AbilityRef;
                        s.StartTick = tick;
                        s.OriginX = Quantize.ToPosition(p.Position.X);
                        s.OriginY = Quantize.ToPosition(p.Position.Y);
                        s.Direction = (ushort)Quantize.ToDirectionAngle(p.Dir, NetProtocol.ProjectileAngleBits);
                        s.AfterBounce = bounce;
                        _lastDir[i] = p.Dir;
                        _has[i] = true;
                    }
                    s.PierceLeft = p.PierceLeft;
                    s.BounceLeft = p.BounceLeft;
                    s.HitMask = (ushort)(p.HitMask & ((1u << NetProtocol.HitMaskBits) - 1u));
                    _point[i] = p.Position;
                    continue;
                }
                if (!_has[i]) continue;
                if (!s.Ended)
                {
                    s.Ended = true;
                    s.EndTick = tick;
                    s.EndX = Quantize.ToPosition(p.Position.X);
                    s.EndY = Quantize.ToPosition(p.Position.Y);
                    s.HitMask = (ushort)(p.HitMask & ((1u << NetProtocol.HitMaskBits) - 1u));
                    _point[i] = p.Position;
                }
                else if (tick - s.EndTick > lingerTicks)
                {
                    _has[i] = false;
                    s = default;
                }
            }
        }
    }
}
