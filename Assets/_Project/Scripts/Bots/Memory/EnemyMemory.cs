using Arena.Core.Math;
using Arena.Core.Sim;

namespace Arena.Bots.Memory
{
    /// <summary>
    /// What each bot remembers about each enemy (GDD §7: "checks bushes where an enemy may be"): where and when it last
    /// perceived it, with its velocity then (to guess where it went), and when that enemy last damaged the bot ("focused" for
    /// tricks). Flat arrays by <c>bot × MaxFighters + enemy</c>, allocated once; no allocations afterwards. Not part of the
    /// match snapshot (bots are input producers, D-057).
    /// </summary>
    public sealed class EnemyMemory
    {
        public const int Never = -1;

        private readonly int[] _seenTick = new int[SimLimits.MaxFighters * SimLimits.MaxFighters];
        private readonly Vec2[] _seenPosition = new Vec2[SimLimits.MaxFighters * SimLimits.MaxFighters];
        private readonly Vec2[] _seenVelocity = new Vec2[SimLimits.MaxFighters * SimLimits.MaxFighters];
        private readonly int[] _damagedTick = new int[SimLimits.MaxFighters * SimLimits.MaxFighters];

        public EnemyMemory()
        {
            for (int bot = 0; bot < SimLimits.MaxFighters; bot++) Clear(bot);
        }

        private static int Index(int bot, int enemy) => bot * SimLimits.MaxFighters + enemy;

        /// <summary>Forgets everything of <paramref name="bot"/> (new bot, defeat).</summary>
        public void Clear(int bot)
        {
            for (int j = 0; j < SimLimits.MaxFighters; j++)
            {
                int i = Index(bot, j);
                _seenTick[i] = Never;
                _seenPosition[i] = Vec2.Zero;
                _seenVelocity[i] = Vec2.Zero;
                _damagedTick[i] = Never;
            }
        }

        /// <summary><paramref name="bot"/> perceives <paramref name="enemy"/> now.</summary>
        public void Seen(int bot, int enemy, int tick, Vec2 position, Vec2 velocity)
        {
            int i = Index(bot, enemy);
            _seenTick[i] = tick;
            _seenPosition[i] = position;
            _seenVelocity[i] = velocity;
        }

        /// <summary>Drops the sighting (the bush was checked, the enemy fell).</summary>
        public void ForgetSighting(int bot, int enemy) => _seenTick[Index(bot, enemy)] = Never;

        public int LastSeenTick(int bot, int enemy) => _seenTick[Index(bot, enemy)];

        public Vec2 LastSeenPosition(int bot, int enemy) => _seenPosition[Index(bot, enemy)];

        /// <summary>
        /// Where the enemy probably is now: the last position moved by the last velocity for at most
        /// <paramref name="maxPredictTicks"/> ticks since the sighting.
        /// </summary>
        public Vec2 PredictedPosition(int bot, int enemy, int tick, int maxPredictTicks)
        {
            int i = Index(bot, enemy);
            int elapsed = _seenTick[i] == Never ? 0 : MathUtil.Clamp(tick - _seenTick[i], 0, maxPredictTicks);
            return _seenPosition[i] + _seenVelocity[i] * (elapsed * SimTime.Dt);
        }

        /// <summary><paramref name="enemy"/> damaged <paramref name="bot"/> at <paramref name="tick"/>.</summary>
        public void Damaged(int bot, int enemy, int tick) => _damagedTick[Index(bot, enemy)] = tick;

        public int LastDamagedTick(int bot, int enemy) => _damagedTick[Index(bot, enemy)];

        /// <summary>Different enemies that damaged <paramref name="bot"/> within the last <paramref name="windowTicks"/> ticks.</summary>
        public int CountRecentAttackers(int bot, int tick, int windowTicks)
        {
            int n = 0;
            int start = Index(bot, 0);
            for (int j = 0; j < SimLimits.MaxFighters; j++)
            {
                int t = _damagedTick[start + j];
                if (t != Never && tick - t <= windowTicks) n++;
            }
            return n;
        }
    }
}
