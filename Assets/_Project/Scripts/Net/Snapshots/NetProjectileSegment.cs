using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// A projectile as a straight segment (Server-Spec §4.4 item 5): the client computes the position itself —
    /// <c>origin + dir × speed × (t − StartTick) / TickRate</c>, speed and range from the ability. A bounce starts a new segment
    /// (<see cref="AfterBounce"/>, range = <c>BounceRange</c>); <see cref="SpawnTick"/> tells a bounce from a new projectile in
    /// the same slot. An ended projectile keeps its entry for a short while with <see cref="Ended"/>, the end tick and point,
    /// so the view flies it to that point and hides it. Origin = position in the state of tick <see cref="StartTick"/>.
    /// The slot is the key of <see cref="SnapshotEntityList{T}"/>.
    /// When adding a field: add it to <see cref="Equals(NetProjectileSegment)"/>, <see cref="WriteDelta"/> and <see cref="ReadDelta"/>.
    /// </summary>
    public struct NetProjectileSegment : IEquatable<NetProjectileSegment>, ISnapshotEntry<NetProjectileSegment>
    {
        /// <summary>Tick the projectile was fired (its identity).</summary>
        public int SpawnTick;

        public int Owner;

        /// <summary><c>MatchDefs.MakeAbilityRef</c> of the ability (speed, radius, range).</summary>
        public int AbilityRef;

        /// <summary>Tick of the state in which the projectile was at <see cref="OriginX"/>, <see cref="OriginY"/>.</summary>
        public int StartTick;

        public ushort OriginX;
        public ushort OriginY;

        /// <summary>Direction, 16-bit angle (<c>NetProtocol.ProjectileAngleBits</c>; views only).</summary>
        public ushort Direction;

        public int PierceLeft;
        public int BounceLeft;

        /// <summary>Fighters already hit, one bit per slot (<c>NetProtocol.HitMaskBits</c>).</summary>
        public ushort HitMask;

        /// <summary>The segment starts at a bounce: its range is <c>AbilityDef.BounceRange</c>.</summary>
        public bool AfterBounce;

        public bool Ended;
        public int EndTick;
        public ushort EndX;
        public ushort EndY;

        public void WriteDelta(BitWriter w, in NetProjectileSegment prev, int baseTick)
        {
            DeltaCodec.WriteTick(w, SpawnTick, prev.SpawnTick, baseTick);
            DeltaCodec.WriteInt(w, Owner, prev.Owner);
            DeltaCodec.WriteInt(w, AbilityRef, prev.AbilityRef);
            DeltaCodec.WriteTick(w, StartTick, prev.StartTick, baseTick);
            DeltaCodec.WritePoint(w, OriginX, OriginY, prev.OriginX, prev.OriginY);
            DeltaCodec.WriteBits(w, Direction, prev.Direction, NetProtocol.ProjectileAngleBits);
            DeltaCodec.WriteInt(w, PierceLeft, prev.PierceLeft);
            DeltaCodec.WriteInt(w, BounceLeft, prev.BounceLeft);
            DeltaCodec.WriteBits(w, HitMask, prev.HitMask, NetProtocol.HitMaskBits);
            DeltaCodec.WriteBool(w, AfterBounce, prev.AfterBounce);
            DeltaCodec.WriteBool(w, Ended, prev.Ended);
            DeltaCodec.WriteTick(w, EndTick, prev.EndTick, baseTick);
            DeltaCodec.WritePoint(w, EndX, EndY, prev.EndX, prev.EndY);
        }

        public NetProjectileSegment ReadDelta(BitReader r, int baseTick)
        {
            NetProjectileSegment v = this;
            v.SpawnTick = DeltaCodec.ReadTick(r, SpawnTick, baseTick);
            v.Owner = DeltaCodec.ReadInt(r, Owner);
            v.AbilityRef = DeltaCodec.ReadInt(r, AbilityRef);
            v.StartTick = DeltaCodec.ReadTick(r, StartTick, baseTick);
            DeltaCodec.ReadPoint(r, ref v.OriginX, ref v.OriginY);
            v.Direction = (ushort)DeltaCodec.ReadBits(r, Direction, NetProtocol.ProjectileAngleBits);
            v.PierceLeft = DeltaCodec.ReadInt(r, PierceLeft);
            v.BounceLeft = DeltaCodec.ReadInt(r, BounceLeft);
            v.HitMask = (ushort)DeltaCodec.ReadBits(r, HitMask, NetProtocol.HitMaskBits);
            v.AfterBounce = DeltaCodec.ReadBool(r, AfterBounce);
            v.Ended = DeltaCodec.ReadBool(r, Ended);
            v.EndTick = DeltaCodec.ReadTick(r, EndTick, baseTick);
            DeltaCodec.ReadPoint(r, ref v.EndX, ref v.EndY);
            return v;
        }

        public bool Equals(NetProjectileSegment o) => SpawnTick == o.SpawnTick && Owner == o.Owner && AbilityRef == o.AbilityRef
            && StartTick == o.StartTick && OriginX == o.OriginX && OriginY == o.OriginY && Direction == o.Direction
            && PierceLeft == o.PierceLeft && BounceLeft == o.BounceLeft && HitMask == o.HitMask && AfterBounce == o.AfterBounce
            && Ended == o.Ended && EndTick == o.EndTick && EndX == o.EndX && EndY == o.EndY;

        public override bool Equals(object obj) => obj is NetProjectileSegment other && Equals(other);
        public override int GetHashCode() => (SpawnTick * 397) ^ StartTick ^ (OriginX * 31) ^ OriginY;
    }
}
