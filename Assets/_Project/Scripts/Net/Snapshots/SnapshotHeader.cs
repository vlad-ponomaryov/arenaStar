using System;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Header of a snapshot (Server-Spec §4.4 item 1). A key frame is a flag (a "baseline 0" would clash with the real tick 0).
    /// Wire: <c>serverTick</c> zigzag varint, key-frame bit, else baseline age varuint (1…<c>SnapshotHistory − 1</c>), own slot
    /// 4 bits, <c>lastAppliedInputTick</c> zigzag relative to the server tick, <c>InputMarginMin</c> zigzag, bot-control bit.
    /// </summary>
    public struct SnapshotHeader : IEquatable<SnapshotHeader>
    {
        /// <summary>Tick of the state this snapshot shows (<c>MatchGlobals.Tick</c> after the server tick; negative in the countdown).</summary>
        public int ServerTick;

        public bool IsKeyframe;

        /// <summary>Tick of the view the delta is against (valid when not a key frame).</summary>
        public int BaselineTick;

        /// <summary>The receiver's fighter slot.</summary>
        public int OwnSlot;

        /// <summary>Newest input tick of this client the server has applied (input acknowledgement, §2.5).</summary>
        public int LastAppliedInputTick;

        /// <summary>Minimum input margin over the window (§2.3, D-079).</summary>
        public int InputMarginMin;

        /// <summary>A bot controls the receiver's fighter (disconnect, AFK, §7): prediction is off.</summary>
        public bool BotControlled;

        public bool Equals(SnapshotHeader o) => ServerTick == o.ServerTick && IsKeyframe == o.IsKeyframe
            && BaselineTick == o.BaselineTick && OwnSlot == o.OwnSlot && LastAppliedInputTick == o.LastAppliedInputTick
            && InputMarginMin == o.InputMarginMin && BotControlled == o.BotControlled;

        public override bool Equals(object obj) => obj is SnapshotHeader other && Equals(other);
        public override int GetHashCode() => (ServerTick * 397) ^ BaselineTick ^ OwnSlot;
    }
}
