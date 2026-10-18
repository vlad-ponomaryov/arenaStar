using System;
using Arena.Core.Modes;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// Mode block of a snapshot (Server-Spec §4.4 item 2): phase, hold countdown, end and winner, team acorns
    /// (<c>ModeState.Team0Acorns</c> / <c>Team1Acorns</c> — a hidden carrier is not in the snapshot), Last Beast fog step and
    /// safe rectangle (quantized like positions) and fighters alive. Times are absolute ticks ("time at the end of a tick").
    /// When adding a field: add it to <see cref="Equals(NetModeView)"/>, <see cref="WriteDelta"/> and <see cref="ReadDelta"/>.
    /// </summary>
    public struct NetModeView : IEquatable<NetModeView>, ISnapshotEntry<NetModeView>
    {
        public ModePhase Phase;
        public int CountdownTeam;
        public int CountdownEndTick;
        public int EndTick;
        public int WinnerTeam;
        public int Team0Acorns;
        public int Team1Acorns;
        public int FogStep;
        public int NextFogStepTick;
        public ushort SafeMinX;
        public ushort SafeMinY;
        public ushort SafeMaxX;
        public ushort SafeMaxY;
        public int AliveCount;

        public void WriteDelta(BitWriter w, in NetModeView prev, int baseTick)
        {
            DeltaCodec.WriteInt(w, (int)Phase, (int)prev.Phase);
            DeltaCodec.WriteInt(w, CountdownTeam, prev.CountdownTeam);
            DeltaCodec.WriteTick(w, CountdownEndTick, prev.CountdownEndTick, baseTick);
            DeltaCodec.WriteTick(w, EndTick, prev.EndTick, baseTick);
            DeltaCodec.WriteInt(w, WinnerTeam, prev.WinnerTeam);
            DeltaCodec.WriteInt(w, Team0Acorns, prev.Team0Acorns);
            DeltaCodec.WriteInt(w, Team1Acorns, prev.Team1Acorns);
            DeltaCodec.WriteInt(w, FogStep, prev.FogStep);
            DeltaCodec.WriteTick(w, NextFogStepTick, prev.NextFogStepTick, baseTick);
            DeltaCodec.WritePoint(w, SafeMinX, SafeMinY, prev.SafeMinX, prev.SafeMinY);
            DeltaCodec.WritePoint(w, SafeMaxX, SafeMaxY, prev.SafeMaxX, prev.SafeMaxY);
            DeltaCodec.WriteInt(w, AliveCount, prev.AliveCount);
        }

        public NetModeView ReadDelta(BitReader r, int baseTick)
        {
            NetModeView v = this;
            v.Phase = (ModePhase)DeltaCodec.ReadInt(r, (int)Phase);
            v.CountdownTeam = DeltaCodec.ReadInt(r, CountdownTeam);
            v.CountdownEndTick = DeltaCodec.ReadTick(r, CountdownEndTick, baseTick);
            v.EndTick = DeltaCodec.ReadTick(r, EndTick, baseTick);
            v.WinnerTeam = DeltaCodec.ReadInt(r, WinnerTeam);
            v.Team0Acorns = DeltaCodec.ReadInt(r, Team0Acorns);
            v.Team1Acorns = DeltaCodec.ReadInt(r, Team1Acorns);
            v.FogStep = DeltaCodec.ReadInt(r, FogStep);
            v.NextFogStepTick = DeltaCodec.ReadTick(r, NextFogStepTick, baseTick);
            DeltaCodec.ReadPoint(r, ref v.SafeMinX, ref v.SafeMinY);
            DeltaCodec.ReadPoint(r, ref v.SafeMaxX, ref v.SafeMaxY);
            v.AliveCount = DeltaCodec.ReadInt(r, AliveCount);
            return v;
        }

        public bool Equals(NetModeView o) => Phase == o.Phase && CountdownTeam == o.CountdownTeam
            && CountdownEndTick == o.CountdownEndTick && EndTick == o.EndTick && WinnerTeam == o.WinnerTeam
            && Team0Acorns == o.Team0Acorns && Team1Acorns == o.Team1Acorns && FogStep == o.FogStep
            && NextFogStepTick == o.NextFogStepTick && SafeMinX == o.SafeMinX && SafeMinY == o.SafeMinY
            && SafeMaxX == o.SafeMaxX && SafeMaxY == o.SafeMaxY && AliveCount == o.AliveCount;

        public override bool Equals(object obj) => obj is NetModeView other && Equals(other);
        public override int GetHashCode() => ((int)Phase * 397) ^ CountdownEndTick ^ (Team0Acorns * 31) ^ Team1Acorns ^ FogStep;
    }
}
