using Arena.Core.Math;

namespace Arena.Core.Sim.Events
{
    /// <summary>
    /// One event of a tick (D-008). A single flat struct for every kind; meaning of the fields is documented per
    /// <see cref="SimEventType"/>. Unused fields are 0 / −1.
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public int Tick;

        /// <summary>Fighter slot that caused the event, or −1.</summary>
        public int Source;

        /// <summary>Fighter slot affected by the event, or −1.</summary>
        public int Target;

        public int Value;
        public Vec2 Position;
        public Vec2 Direction;

        /// <summary>Ability that caused the event (<c>MatchDefs.MakeAbilityRef</c>), or −1 (unknown, environment). Set for
        /// <see cref="SimEventType.Damaged"/>: the view tells an ult hit (hit stop, shake) from an attack hit.</summary>
        public int AbilityRef;

        public SimEvent(SimEventType type, int tick, int source, int target, int value, Vec2 position, Vec2 direction)
            : this(type, tick, source, target, value, position, direction, -1)
        {
        }

        public SimEvent(SimEventType type, int tick, int source, int target, int value, Vec2 position, Vec2 direction, int abilityRef)
        {
            AbilityRef = abilityRef;
            Type = type;
            Tick = tick;
            Source = source;
            Target = target;
            Value = value;
            Position = position;
            Direction = direction;
        }
    }
}
