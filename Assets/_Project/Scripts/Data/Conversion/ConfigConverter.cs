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
                RespawnTicks = SimTime.SecondsToTicks(c.RespawnSeconds),
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
                AttackRechargeTicks = MathUtil.Max(1, SimTime.SecondsToTicks(c.AttackRechargeSeconds)),
                Attack = ToAbilityDef(c.Attack, c.Id + ".Attack")
            };
            def.Validate();
            return def;
        }

        public static AbilityDef ToAbilityDef(AbilityConfig c, string owner)
        {
            if (c == null) throw new ArgumentException(owner + ": ability is not set");
            int effectCount = c.Effects == null ? 0 : c.Effects.Length;
            var effects = new EffectDef[effectCount];
            for (int i = 0; i < effectCount; i++)
            {
                EffectConfig e = c.Effects[i];
                if (e == null) throw new ArgumentException(owner + ": effect " + i + " is null");
                effects[i] = new EffectDef
                {
                    Kind = e.Kind,
                    Filter = e.Filter,
                    Value = e.Value,
                    DurationTicks = SimTime.SecondsToTicks(e.DurationSeconds),
                    Distance = e.Distance
                };
            }
            return new AbilityDef
            {
                Delivery = c.Delivery,
                Range = c.Range,
                ProjectileSpeed = c.ProjectileSpeed,
                ProjectileRadius = c.ProjectileRadius,
                PierceCount = c.PierceCount,
                ConeAngleDegrees = c.ConeAngleDegrees,
                BurstCount = c.BurstCount,
                BurstIntervalTicks = SimTime.SecondsToTicks(c.BurstIntervalSeconds),
                Effects = effects
            };
        }
    }
}
