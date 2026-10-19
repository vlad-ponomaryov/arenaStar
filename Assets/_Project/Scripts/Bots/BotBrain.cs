using Arena.Bots.Utility;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Core.Random;

namespace Arena.Bots
{
    /// <summary>
    /// State of one bot (blittable, no references — bots run on the stage-3 server unchanged, D-022): current action and
    /// target, goal and path cursor, the bot's own RNG, shot / strafe timers. Arrays that do not fit into a struct live in
    /// <see cref="BotController"/> by slot: the path cells (<c>MaxPathLength</c> per bot) and the tick each enemy was first
    /// perceived (reaction delay). Not part of the match snapshot: bots are input producers, like players.
    /// </summary>
    public struct BotBrain
    {
        /// <summary>The slot is driven by a bot.</summary>
        public bool Active;

        public BotAction Action;

        /// <summary>Enemy slot chosen at the last decision, or −1.</summary>
        public int TargetSlot;

        /// <summary>Where the current action leads (target position, acorn, hollow, own spawn, wander cell).</summary>
        public Vec2 Goal;

        /// <summary>The path in the controller's buffer is valid for <see cref="PathGoal"/>.</summary>
        public bool HasPath;
        public GridCell PathGoal;
        public int PathLength;

        /// <summary>Index of the next waypoint in the path.</summary>
        public int PathCursor;

        /// <summary>Bot RNG (aim spread, strafe, wander) — use by reference.</summary>
        public DeterministicRng Rng;

        public int NextDecisionTick;

        /// <summary>First tick a new attack press is allowed (<c>MinShotIntervalTicks</c> after the last one).</summary>
        public int NextShotTick;

        /// <summary>Strafe side around the target: −1 or +1, kept until <see cref="StrafeUntilTick"/>.</summary>
        public int StrafeSign;
        public int StrafeUntilTick;

        /// <summary>Retreat threshold shift of this life (difficulty mistake, rolled at the start and after each defeat).</summary>
        public float RetreatShift;

        /// <summary>The last decision chose to use the ult at <see cref="TargetSlot"/>; pressed on the next possible tick.</summary>
        public bool UltPending;

        /// <summary>The last decision chose to use the trick along <see cref="TrickDir"/> (movement or aim by the trick).</summary>
        public bool TrickPending;
        public Vec2 TrickDir;

        /// <summary>A mistake: shoot once at <see cref="BushShotPoint"/>, a bush where nobody was seen.</summary>
        public bool BushShotPending;
        public Vec2 BushShotPoint;

        /// <summary>Enemy whose vanishing near a bush is being checked (<c>BotAction.CheckBush</c>), or −1.</summary>
        public int MemoryEnemy;

        /// <summary>Berry box being broken (<c>BotAction.BreakBox</c>); <see cref="Goal"/> is the free cell next to it.</summary>
        public GridCell BoxCell;

        /// <summary>Sidestep enemy projectiles until the next decision (rolled with <c>BotDifficultyDef.DodgeChance</c>).</summary>
        public bool Dodging;
    }
}
