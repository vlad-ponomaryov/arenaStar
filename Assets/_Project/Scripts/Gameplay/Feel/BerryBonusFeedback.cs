using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.Feel
{
    /// <summary>
    /// Last Beast (D-131): when the own fighter's berry count grows, a large "+N% power" text in the berry colour rises over it —
    /// N is the damage / health bonus gained (<see cref="LastBeastRules.DamageBonusPercent"/>, nothing past the cap). Reads the
    /// count from the state each frame, so it works offline and online alike (berries are in the own snapshot).
    /// </summary>
    public sealed class BerryBonusFeedback
    {
        private readonly DamageNumberPool _numbers;
        private readonly FeelConfig _feel;
        private readonly PercentTextCache _texts;
        private int _lastBerries = -1;

        /// <summary>Texts shown, for tests and debug.</summary>
        public int Shown { get; private set; }

        public BerryBonusFeedback(DamageNumberPool numbers, FeelConfig feel, string format)
        {
            _numbers = numbers;
            _feel = feel;
            _texts = new PercentTextCache(format);
        }

        /// <param name="berries">The own fighter's berries now.</param>
        /// <param name="world">Where the text starts (the own fighter).</param>
        public void Update(MatchDefs defs, int berries, Vector3 world)
        {
            if (defs == null || defs.Mode != ModeKind.LastBeast || defs.LastBeast == null)
            {
                _lastBerries = -1;
                return;
            }
            if (_lastBerries >= 0 && berries > _lastBerries)
            {
                int gained = LastBeastRules.DamageBonusPercent(defs.LastBeast, berries)
                    - LastBeastRules.DamageBonusPercent(defs.LastBeast, _lastBerries);
                if (gained > 0)
                {
                    Shown++;
                    _numbers?.SpawnText(world, _texts.Get(gained), _feel.BerryBonusColor, true);
                }
            }
            _lastBerries = berries;
        }

        public void Reset() => _lastBerries = -1;
    }
}
