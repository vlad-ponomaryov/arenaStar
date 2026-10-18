using Arena.Core.Modes;
using Arena.Core.Sim;
using Arena.Net.Serialization;

namespace Arena.Net.Protocol
{
    /// <summary>
    /// <c>QueueStatus</c> (unreliable, server → client, once per <c>QueueStatusIntervalSeconds</c>, Server-Spec §4.2, §6.2): the
    /// ticket's state, whole seconds waited and "found <see cref="Found"/> of <see cref="Needed"/>" for the search screen (§12.4).
    /// </summary>
    public struct QueueStatusMessage
    {
        public QueueState State;
        public ModeKind Mode;
        public int WaitSeconds;
        public int Found;
        public int Needed;

        public void Write(BitWriter w)
        {
            w.WriteByte((byte)MessageKind.QueueStatus);
            w.WriteByte((byte)State);
            w.WriteByte((byte)Mode);
            w.WriteVarUInt((uint)WaitSeconds);
            w.WriteVarUInt((uint)Found);
            w.WriteVarUInt((uint)Needed);
        }

        /// <summary>Reads the body after the kind byte. False — malformed.</summary>
        public bool Read(BitReader r)
        {
            State = (QueueState)r.ReadByte();
            Mode = (ModeKind)r.ReadByte();
            uint wait = r.ReadVarUInt();
            uint found = r.ReadVarUInt();
            uint needed = r.ReadVarUInt();
            if (r.IsOverflow || wait > int.MaxValue || found > needed || needed > SimLimits.MaxFighters) return false;
            WaitSeconds = (int)wait;
            Found = (int)found;
            Needed = (int)needed;
            return true;
        }
    }
}
