using System;
using Arena.Net.Protocol;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// What a client learns about a foreign fighter (ally or visible enemy, Server-Spec §4.4 item 4), already quantized: the
    /// server keeps exactly these values in its view ring, so its view equals what the client restores (§4.4 "Дельта").
    /// Static slot data (fighter, team, level) is in <c>MatchStart</c>. The slot is the key of <see cref="SnapshotEntityList{T}"/>.
    /// When adding a field: add it to <see cref="Equals(NetFighterView)"/>, <see cref="WriteDelta"/> and <see cref="ReadDelta"/>.
    /// </summary>
    public struct NetFighterView : IEquatable<NetFighterView>, ISnapshotEntry<NetFighterView>
    {
        public bool Alive;

        /// <summary>Position, <c>Quantize.ToPosition</c> per axis.</summary>
        public ushort PositionX;
        public ushort PositionY;

        /// <summary>Velocity for extrapolation, <c>Quantize.ToVelocity</c> per axis.</summary>
        public short VelocityX;
        public short VelocityY;

        /// <summary>Look direction, 8-bit angle (<c>NetProtocol.FacingAngleBits</c>).</summary>
        public byte Facing;

        public int Health;
        public int MaxHealth;
        public int Charges;
        public NetFighterFlags Flags;
        public int Acorns;
        public int Berries;

        public void WriteDelta(BitWriter w, in NetFighterView prev, int baseTick)
        {
            DeltaCodec.WriteBool(w, Alive, prev.Alive);
            DeltaCodec.WritePoint(w, PositionX, PositionY, prev.PositionX, prev.PositionY);
            DeltaCodec.WriteBits(w, (ushort)VelocityX, (ushort)prev.VelocityX, NetProtocol.VelocityBits);
            DeltaCodec.WriteBits(w, (ushort)VelocityY, (ushort)prev.VelocityY, NetProtocol.VelocityBits);
            DeltaCodec.WriteBits(w, Facing, prev.Facing, NetProtocol.FacingAngleBits);
            DeltaCodec.WriteInt(w, Health, prev.Health);
            DeltaCodec.WriteInt(w, MaxHealth, prev.MaxHealth);
            DeltaCodec.WriteInt(w, Charges, prev.Charges);
            DeltaCodec.WriteBits(w, (uint)Flags, (uint)prev.Flags, NetProtocol.FighterFlagBits);
            DeltaCodec.WriteInt(w, Acorns, prev.Acorns);
            DeltaCodec.WriteInt(w, Berries, prev.Berries);
        }

        /// <summary>Reads a delta written against this entry (the previous value).</summary>
        public NetFighterView ReadDelta(BitReader r, int baseTick)
        {
            NetFighterView prev = this;
            NetFighterView v = prev;
            v.Alive = DeltaCodec.ReadBool(r, prev.Alive);
            DeltaCodec.ReadPoint(r, ref v.PositionX, ref v.PositionY);
            v.VelocityX = (short)(ushort)DeltaCodec.ReadBits(r, (ushort)prev.VelocityX, NetProtocol.VelocityBits);
            v.VelocityY = (short)(ushort)DeltaCodec.ReadBits(r, (ushort)prev.VelocityY, NetProtocol.VelocityBits);
            v.Facing = (byte)DeltaCodec.ReadBits(r, prev.Facing, NetProtocol.FacingAngleBits);
            v.Health = DeltaCodec.ReadInt(r, prev.Health);
            v.MaxHealth = DeltaCodec.ReadInt(r, prev.MaxHealth);
            v.Charges = DeltaCodec.ReadInt(r, prev.Charges);
            v.Flags = (NetFighterFlags)DeltaCodec.ReadBits(r, (uint)prev.Flags, NetProtocol.FighterFlagBits);
            v.Acorns = DeltaCodec.ReadInt(r, prev.Acorns);
            v.Berries = DeltaCodec.ReadInt(r, prev.Berries);
            return v;
        }

        public bool Equals(NetFighterView o) => Alive == o.Alive && PositionX == o.PositionX && PositionY == o.PositionY
            && VelocityX == o.VelocityX && VelocityY == o.VelocityY && Facing == o.Facing && Health == o.Health
            && MaxHealth == o.MaxHealth && Charges == o.Charges && Flags == o.Flags && Acorns == o.Acorns && Berries == o.Berries;

        public override bool Equals(object obj) => obj is NetFighterView other && Equals(other);
        public override int GetHashCode() => (PositionX * 397) ^ PositionY ^ (Health * 31);
    }
}
