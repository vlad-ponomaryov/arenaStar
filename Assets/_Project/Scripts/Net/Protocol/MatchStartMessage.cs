using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>MatchStart</c> (reliable, server → client, Server-Spec §4.2): map index in the content pack, mode, the slot table,
    /// the receiver's slot, <c>CountdownTicks</c>, the snapshot rate divisor, the input redundancy and <c>UnlockAllAbilities</c>.
    /// The match seed is not sent; the fight begins by the snapshot ticks (§2.3). Preallocated, reused; no allocations.
    /// </summary>
    public sealed class MatchStartMessage
    {
        public ulong MatchId;
        public int MapIndex;
        public ModeKind Mode;
        public int OwnSlot;
        public int CountdownTicks;
        public int SnapshotRateDivisor;
        public int InputRedundancy;
        public bool UnlockAllAbilities;

        /// <summary>The table carries <see cref="MatchSlotInfo.IsBot"/> (D-087: off by default).</summary>
        public bool ShowBots;

        public int SlotCount;
        public readonly MatchSlotInfo[] Slots = new MatchSlotInfo[SimLimits.MaxFighters];

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.MatchStart);
            w.WriteUInt64(MatchId);
            w.WriteVarUInt((uint)MapIndex);
            w.WriteByte((byte)Mode);
            w.WriteBits((uint)OwnSlot, NetProtocol.SlotBits);
            w.WriteVarUInt((uint)CountdownTicks);
            w.WriteVarUInt((uint)SnapshotRateDivisor);
            w.WriteVarUInt((uint)InputRedundancy);
            w.WriteBool(UnlockAllAbilities);
            w.WriteBool(ShowBots);
            w.WriteVarUInt((uint)SlotCount);
            for (int i = 0; i < SlotCount; i++)
            {
                ref MatchSlotInfo s = ref Slots[i];
                w.WriteVarUInt((uint)s.FighterIndex);
                w.WriteVarUInt((uint)s.Team);
                w.WriteVarUInt((uint)s.Level);
                w.WriteFloat(s.SpawnX);
                w.WriteFloat(s.SpawnY);
                if (ShowBots) w.WriteBool(s.IsBot);
            }
        }

        /// <summary>Reads the body after the kind byte. False — malformed (the message is ignored).</summary>
        public bool Read(BitReader r)
        {
            MatchId = r.ReadUInt64();
            MapIndex = (int)r.ReadVarUInt();
            Mode = (ModeKind)r.ReadByte();
            OwnSlot = (int)r.ReadBits(NetProtocol.SlotBits);
            CountdownTicks = (int)r.ReadVarUInt();
            SnapshotRateDivisor = (int)r.ReadVarUInt();
            InputRedundancy = (int)r.ReadVarUInt();
            UnlockAllAbilities = r.ReadBool();
            ShowBots = r.ReadBool();
            uint count = r.ReadVarUInt();
            if (r.IsOverflow || count == 0u || count > (uint)Slots.Length) return false;
            SlotCount = (int)count;
            for (int i = 0; i < SlotCount; i++)
            {
                ref MatchSlotInfo s = ref Slots[i];
                s.FighterIndex = (int)r.ReadVarUInt();
                s.Team = (int)r.ReadVarUInt();
                s.Level = (int)r.ReadVarUInt();
                s.SpawnX = r.ReadFloat();
                s.SpawnY = r.ReadFloat();
                s.IsBot = ShowBots && r.ReadBool();
            }
            return !r.IsOverflow && OwnSlot < SlotCount && MapIndex >= 0 && CountdownTicks >= 0 && SnapshotRateDivisor >= 1
                && InputRedundancy >= 1;
        }
    }
}
