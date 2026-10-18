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

        public bool IsEnded => Phase == ModePhase.Ended;

        public bool Equals(ModeState o) => Phase == o.Phase && NextSpawnTick == o.NextSpawnTick && NextGoldenTick == o.NextGoldenTick
            && CountdownTeam == o.CountdownTeam && CountdownEndTick == o.CountdownEndTick && EndTick == o.EndTick
            && WinnerTeam == o.WinnerTeam;

        public override bool Equals(object obj) => obj is ModeState other && Equals(other);
        public override int GetHashCode() => ((int)Phase * 397) ^ NextSpawnTick ^ (CountdownTeam * 31) ^ WinnerTeam;
    }
}
