using System;
using Arena.Core.Modes;

namespace Arena.Core.State
{
    /// <summary>
    /// Mode rules state (D-002), part of the snapshot. Times are "time at the end of a tick" (<c>tick index + 1</c>), so
    /// "after N calls of Tick" = N. Teams are −1 when none (no countdown / draw).
    /// When adding a field: also add it to <see cref="Equals(ModeState)"/>.
    /// </summary>
    public struct ModeState : IEquatable<ModeState>
    {
        public ModePhase Phase;

        /// <summary>Time of the next normal acorn from the hollow.</summary>
        public int NextSpawnTick;

        /// <summary>Time of the next golden acorn.</summary>
        public int NextGoldenTick;

        /// <summary>Team whose hold countdown runs, or −1.</summary>
        public int CountdownTeam;

        /// <summary>Time at which <see cref="CountdownTeam"/> wins if the countdown is not reset.</summary>
        public int CountdownEndTick;

        /// <summary>Time the match ended (valid when <see cref="Phase"/> is <see cref="ModePhase.Ended"/>).</summary>
        public int EndTick;

        /// <summary>Winning team, or −1 for a draw (valid when ended).</summary>
        public int WinnerTeam;

        /// <summary>Acorns carried by team 0 / team 1 (Acorns mode), written by <c>AcornsRules</c> every tick: the HUD reads
        /// the score here, not as a sum over fighters — a hidden carrier is not in an online snapshot (Server-Spec §4.4, D-081).</summary>
        public int Team0Acorns;
        public int Team1Acorns;

        // --- Last Beast (Server-Spec §9.2); 0 in other modes. ---

        /// <summary>Fog steps done so far (0 = no fog yet).</summary>
        public int FogStep;

        /// <summary>Time of the next fog step.</summary>
        public int NextFogStepTick;

        /// <summary>Time of the next fog damage pulse.</summary>
        public int NextFogPulseTick;

        /// <summary>Safe rectangle (cells): a fighter whose centre is outside it is in the fog.</summary>
        public float SafeMinX;
        public float SafeMinY;
        public float SafeMaxX;
        public float SafeMaxY;

        /// <summary>Fighters alive after the last mode update (the place of a fighter defeated in a tick = this).</summary>
        public int AliveCount;

        public bool IsEnded => Phase == ModePhase.Ended;

        public bool Equals(ModeState o) => Phase == o.Phase && NextSpawnTick == o.NextSpawnTick && NextGoldenTick == o.NextGoldenTick
            && CountdownTeam == o.CountdownTeam && CountdownEndTick == o.CountdownEndTick && EndTick == o.EndTick
            && WinnerTeam == o.WinnerTeam && Team0Acorns == o.Team0Acorns && Team1Acorns == o.Team1Acorns && FogStep == o.FogStep && NextFogStepTick == o.NextFogStepTick
            && NextFogPulseTick == o.NextFogPulseTick && SafeMinX == o.SafeMinX && SafeMinY == o.SafeMinY && SafeMaxX == o.SafeMaxX
            && SafeMaxY == o.SafeMaxY && AliveCount == o.AliveCount;

        public override bool Equals(object obj) => obj is ModeState other && Equals(other);
        public override int GetHashCode() => ((int)Phase * 397) ^ NextSpawnTick ^ (CountdownTeam * 31) ^ WinnerTeam;
    }
}
