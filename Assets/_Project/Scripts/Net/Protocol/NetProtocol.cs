namespace Arena.Net.Protocol
{
    /// <summary>
    /// Protocol identity and wire-format constants (Server-Spec §3.2, §4.1, §4.3). <see cref="Version"/> changes with any
    /// change of the format. Quantization steps and field widths are technical constants of the format (like
    /// <c>SimLimits</c>, D-004): changing one is a protocol change, not a tuning.
    /// </summary>
    public static class NetProtocol
    {
        /// <summary>Protocol "magic": ASCII "ARN1" little-endian. Packets with another id are dropped.</summary>
        public const uint Id = 0x314E5241u;

        /// <summary>Wire format version (u16). Bump on any format change. 2 — match messages (<see cref="MessageKind"/>, S3.6);
        /// 3 — lobby messages (S3.8); 4 — <c>ViewDelayTicks</c> per input and the interpolation delay per <c>Input</c> (S3.13);
        /// 5 — <c>ClientStats</c> (S3.15).</summary>
        public const ushort Version = 5;

        /// <summary>Transport header size in bytes: kind|flags, connectionId, sequence, ack, ackBits, ackDelay (§3.2).</summary>
        public const int HeaderBytes = 18;

        public const int PacketKindBits = 4;
        public const int PacketFlagsBits = 4;

        /// <summary>Unit of the header's ackDelay byte, microseconds (×0.25 ms).</summary>
        public const int AckDelayUnitMicros = 250;

        // ---- Transport packet sizes (§3.2) ----

        /// <summary>
        /// <c>ConnectRequest</c> and <c>ConnectResponse</c> are padded to exactly this size: answers to an unverified address are
        /// never larger than the request (no amplification) and the path is proven to carry full-size packets.
        /// </summary>
        public const int HandshakePacketBytes = NetLimits.MaxPacketBytes;

        /// <summary><c>Ping</c> and <c>Pong</c> are the same size (header + protocol id + nonce, padded).</summary>
        public const int PingPacketBytes = 32;

        /// <summary><c>Discover</c> is padded to the size of <c>DiscoverReply</c> (§3.2).</summary>
        public const int DiscoverPacketBytes = 48;

        /// <summary>
        /// <c>PathChallenge</c> / <c>PathResponse</c> are a bare header: the 64-bit challenge rides in the sequence, ack and ackBits
        /// fields, so a challenge is never larger than any packet that can trigger it (§3.2).
        /// </summary>
        public const int PathPacketBytes = HeaderBytes;

        /// <summary>Header + reason byte of <c>Disconnect</c>.</summary>
        public const int DisconnectPacketBytes = HeaderBytes + 1;

        // ---- Quantization (§4.3) ----

        /// <summary>Quantized positions cover [0, <see cref="PositionRangeCells"/>) cells on each axis.</summary>
        public const int PositionRangeCells = 64;

        /// <summary>Position steps per cell (1/1024 cell). 64 × 1024 = 65536 values fit u16.</summary>
        public const int PositionStepsPerCell = 1024;

        public const int PositionBits = 16;

        /// <summary>Aim axis scale: int16, value = q / 32767 (division only, no trigonometry — §2.4).</summary>
        public const int AimScale = 32767;

        /// <summary>Move axis scale: int8, value = q / 127.</summary>
        public const int MoveScale = 127;

        /// <summary>
        /// Quantized unit vectors are kept below length² = 1 − 1/divisor (integer check), so float rounding on any platform
        /// never makes <c>PlayerInput.Sanitized</c> clamp them — client prediction and server see the same bits.
        /// </summary>
        public const int UnitLengthMarginDivisor = 100000;

        public const int InputButtonBits = 4;

        /// <summary>Inputs in one <c>Input</c> message minus one (1…8, <see cref="NetLimits.MaxInputsPerPacket"/>).</summary>
        public const int InputCountBits = 3;

        /// <summary>Facing direction of a foreign fighter: u8 angle (≈ 1.4°).</summary>
        public const int FacingAngleBits = 8;

        /// <summary>Direction of a foreign projectile: u16 angle (views only).</summary>
        public const int ProjectileAngleBits = 16;

        /// <summary>Area radius: u8 in 1/32 cell.</summary>
        public const int RadiusStepsPerCell = 32;
        public const int RadiusBits = 8;

        /// <summary>Snapshot event fields (§4.4).</summary>
        public const int EventTypeBits = 5;
        public const int SlotBits = 4;

        /// <summary>A 4-bit slot field with this value means "no fighter" (−1).</summary>
        public const int NoSlot = (1 << SlotBits) - 1;

        // ---- Snapshots (§4.4, S3.5) ----

        /// <summary>Velocity of a foreign fighter (extrapolation only): int16 per axis in 1/<see cref="VelocityStepsPerCell"/> cell/s.</summary>
        public const int VelocityStepsPerCell = 64;
        public const int VelocityBits = 16;

        /// <summary>Slot field widths of the snapshot lists: <c>SimLimits.MaxProjectiles</c> = 256, <c>MaxAreas</c> = <c>MaxPickups</c> = 64.</summary>
        public const int ProjectileSlotBits = 8;
        public const int AreaSlotBits = 6;
        public const int PickupSlotBits = 6;

        /// <summary>Mask of fighters a projectile already hit: one bit per fighter slot (<c>SimLimits.MaxFighters</c>).</summary>
        public const int HitMaskBits = 10;

        /// <summary>Foreign fighter flags (<c>NetFighterFlags</c>).</summary>
        public const int FighterFlagBits = 8;
    }
}
