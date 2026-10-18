using System;
using Arena.Core.Sim.Events;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// A view event carried by snapshots (Server-Spec §4.4 item 9): <c>Damaged</c>, <c>Healed</c>, <c>AbilityFired</c> of an
    /// ult, <c>UltReady</c> of the own fighter. Not part of the view: each goes in the snapshots of its tick and the next
    /// <c>EventRedundancyTicks − 1</c>; the client drops ticks it already accepted. When the target is hidden from the
    /// receiver's team, only the source's client gets the event, with <see cref="Target"/> = −1 and no point (sound and
    /// vibration only, as offline).
    /// </summary>
    public struct NetSnapshotEvent : IEquatable<NetSnapshotEvent>
    {
        public SimEventType Type;
        public int Tick;

        /// <summary>Fighter slot, or −1.</summary>
        public int Source;

        /// <summary>Fighter slot, or −1 (none or hidden).</summary>
        public int Target;

        public int Value;
        public bool HasPoint;
        public ushort PointX;
        public ushort PointY;

        /// <summary><c>MatchDefs.MakeAbilityRef</c>, or −1.</summary>
        public int AbilityRef;

        public bool Equals(NetSnapshotEvent o) => Type == o.Type && Tick == o.Tick && Source == o.Source && Target == o.Target
            && Value == o.Value && HasPoint == o.HasPoint && PointX == o.PointX && PointY == o.PointY && AbilityRef == o.AbilityRef;

        public override bool Equals(object obj) => obj is NetSnapshotEvent other && Equals(other);
        public override int GetHashCode() => ((int)Type * 397) ^ Tick ^ (Source * 31) ^ Value;
    }
}
