using System;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;

namespace Arena.Data.Conversion
{
    /// <summary>
    /// ScriptableObject configs (seconds, cells, classes) → pure Core defs (ticks, ints, speeds) (D-003, D-010).
    /// Called once per match start, not in the tick. Throws <see cref="ArgumentException"/> on invalid data.
    /// </summary>
    public static class ConfigConverter
    {
        public static MatchDefs ToMatchDefs(GameDatabase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (db.CombatRules == null) throw new ArgumentException("GameDatabase.CombatRules is not set");
            if (db.Fighters == null || db.Fighters.Count == 0) throw new ArgumentException("GameDatabase.Fighters is empty");

            var fighters = new FighterDef[db.Fighters.Count];
            for (int i = 0; i < fighters.Length; i++)
            {
                if (db.Fighters[i] == null) throw new ArgumentException("GameDatabase.Fighters[" + i + "] is null");
                fighters[i] = ToFighterDef(db.Fighters[i], db.CombatRules);
            }

            var defs = new MatchDefs
            {
                Rules = ToCombatRules(db.CombatRules),
                Fighters = fighters,
                Bounds = ArenaBounds.FromSize(db.TempArenaSize.x, db.TempArenaSize.y)
            };
            defs.Validate();
            return defs;
        }

        public static CombatRules ToCombatRules(CombatRulesConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            return new CombatRules
            {
                MaxCharges = c.MaxCharges,
                RegenDelayTicks = SimTime.SecondsToTicks(c.RegenDelaySeconds),
                RegenFractionPerSecond = c.RegenFractionPerSecond,
                LevelBonusPerLevel = c.LevelBonusPerLevel
            };
        }

        public static FighterDef ToFighterDef(FighterConfig c, CombatRulesConfig rules)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var def = new FighterDef
            {
                Id = c.Id,
                NameKey = c.NameKey,
                MaxHealth = c.MaxHealth,
                MoveSpeed = rules.SpeedFor(c.Speed),
                Radius = c.Radius,
                AttackRange = c.AttackRange,
                AttackRechargeTicks = MathUtil.Max(1, SimTime.SecondsToTicks(c.AttackRechargeSeconds)),
                AttackDamage = c.AttackDamage
            };
            def.Validate();
            return def;
        }
    }
}
