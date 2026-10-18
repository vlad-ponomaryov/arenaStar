using Arena.Core.Modes;
using Arena.Net.Transport;

namespace Arena.Net.Server.Snapshots
{
    /// <summary>
    /// Server settings of snapshots (Server-Spec §4.4, §13 — "S", <c>server.json</c> from S3.9). Values of
    /// <see cref="CreateDefault"/> are PROTO (§13), in one place like <c>TransportSettings.CreateDefault</c>.
    /// </summary>
    public sealed class SnapshotSettings
    {
        /// <summary>Area of interest for foreign fighters and projectiles, cells; 0 = off (Acorns: off; Last Beast: 16).</summary>
        public float AoiRadiusCellsAcorns;
        public float AoiRadiusCellsLastBeast;

        /// <summary>Events per snapshot at most (newest first).</summary>
        public int MaxEventsPerSnapshot;

        /// <summary>Each view event goes in this many snapshots in a row (§4.4).</summary>
        public int EventRedundancyTicks;

        /// <summary>A snapshot every N ticks (1 = 30 Hz); the event window is redundancy × divisor ticks.</summary>
        public int SnapshotRateDivisor;

        /// <summary>
        /// Byte budget of one snapshot payload. The datagram limit is 1200 B (§3.2): 18 B transport header + this + room for a
        /// message kind byte and small reliable messages riding along (<c>NetConnection.MaxPayloadBytes</c> = 1182).
        /// </summary>
        public int MaxSnapshotBytes;

        /// <summary>An ended projectile stays in the views this many ticks with its end point.</summary>
        public int ProjectileEndLingerTicks;

        public static SnapshotSettings CreateDefault() => new SnapshotSettings
        {
            AoiRadiusCellsAcorns = 0f,
            AoiRadiusCellsLastBeast = 16f,
            MaxEventsPerSnapshot = 48,
            EventRedundancyTicks = 3,
            SnapshotRateDivisor = 1,
            MaxSnapshotBytes = 1100,
            ProjectileEndLingerTicks = 15
        };

        public float AoiRadiusFor(ModeKind mode) => mode == ModeKind.LastBeast ? AoiRadiusCellsLastBeast : AoiRadiusCellsAcorns;

        /// <summary>Ticks of events each snapshot carries (≤ <c>NetLimits.EventJournalTicks</c>).</summary>
        public int EventWindowTicks
        {
            get
            {
                int w = EventRedundancyTicks * (SnapshotRateDivisor < 1 ? 1 : SnapshotRateDivisor);
                if (w < 1) w = 1;
                return w > NetLimits.EventJournalTicks ? NetLimits.EventJournalTicks : w;
            }
        }

        /// <summary>Throws if a value is out of range.</summary>
        public void Validate()
        {
            if (AoiRadiusCellsAcorns < 0f || AoiRadiusCellsLastBeast < 0f) throw new System.ArgumentException("SnapshotSettings: AOI radius < 0");
            if (MaxEventsPerSnapshot < 0 || MaxEventsPerSnapshot > NetLimits.MaxSnapshotEvents) throw new System.ArgumentException("SnapshotSettings: MaxEventsPerSnapshot out of range");
            if (EventRedundancyTicks < 1 || SnapshotRateDivisor < 1) throw new System.ArgumentException("SnapshotSettings: redundancy and divisor must be ≥ 1");
            if (MaxSnapshotBytes < 64 || MaxSnapshotBytes > NetConnection.MaxPayloadBytes) throw new System.ArgumentException("SnapshotSettings: MaxSnapshotBytes out of range");
            if (ProjectileEndLingerTicks < 0) throw new System.ArgumentException("SnapshotSettings: ProjectileEndLingerTicks < 0");
        }

        public SnapshotSettings Clone() => (SnapshotSettings)MemberwiseClone();
    }
}
