using Arena.Core.Math;
using Arena.Net.Snapshots;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// A view event as journaled by the server: the full event plus who could see its source and target at that tick
    /// (team bits, own team included) — the per-client filter of §4.4 item 9 is decided from these.
    /// </summary>
    public struct JournalEvent
    {
        public NetSnapshotEvent Event;
        public int SourceTeam;
        public int TargetTeam;
        public uint SourceSeenBy;
        public uint TargetSeenBy;

        /// <summary>The event point unquantized — for the area of interest (F2, D-117).</summary>
        public Vec2 Point;
    }
}
