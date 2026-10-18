using System;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Modes;
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
            if (db.DefaultMap == null) throw new ArgumentException("GameDatabase.DefaultMap is not set");

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
                Map = MapConverter.ToMapDef(db.DefaultMap)
            };
            if (db.AcornsMode != null)
            {
                defs.Mode = ModeKind.Acorns;
                defs.Acorns = ToAcornsModeDef(db.AcornsMode);
            }
            defs.Validate();
            return defs;
        }

        public static BotDifficultyDef ToBotDifficultyDef(BotDifficultyConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var def = new BotDifficultyDef
            {
                Difficulty = c.Difficulty,
                AimSpreadDegrees = c.AimSpreadDegrees,
                ReactionTicks = SimTime.SecondsToTicks(c.ReactionSeconds)
            };
            def.Validate();
            return def;
        }

        public static BotBehaviourDef ToBotBehaviourDef(BotBehaviourConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var def = new BotBehaviourDef
            {
                DecisionIntervalTicks = MathUtil.Max(1, SimTime.SecondsToTicks(c.DecisionIntervalSeconds)),
                ViewRadius = c.ViewRadius,
                RetreatHealthFraction = c.RetreatHealthFraction,
                RecoverHealthFraction = c.RecoverHealthFraction,
                LastChargeEnemyDistance = c.LastChargeEnemyDistance,
                EngageRangeFraction = c.EngageRangeFraction,
                FireRangeFraction = c.FireRangeFraction,
                MinShotIntervalTicks = SimTime.SecondsToTicks(c.MinShotIntervalSeconds),
                StrafeSwitchTicks = MathUtil.Max(1, SimTime.SecondsToTicks(c.StrafeSwitchSeconds)),
                WaypointReachDistance = c.WaypointReachDistance,
                WanderRadiusCells = c.WanderRadiusCells,
                WanderAttempts = c.WanderAttempts,
                RetreatScore = c.RetreatScore,
                AttackScore = c.AttackScore,
                AttackProximityBonus = c.AttackProximityBonus,
                ObjectiveScore = c.ObjectiveScore,
                WanderScore = c.WanderScore,
                PathNodeLimit = c.PathNodeLimit,
                MaxPathLength = c.MaxPathLength
            };
            def.Validate();
            return def;
        }

        public static AcornsModeDef ToAcornsModeDef(AcornsModeConfig c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var def = new AcornsModeDef
            {
                SpawnIntervalTicks = SimTime.SecondsToTicks(c.SpawnIntervalSeconds),
                GoldenIntervalTicks = SimTime.SecondsToTicks(c.GoldenIntervalSeconds),
                GoldenValue = c.GoldenValue,
                TargetAcorns = c.TargetAcorns,
                HoldTicks = SimTime.SecondsToTicks(c.HoldSeconds),
                DurationTicks = SimTime.SecondsToTicks(c.DurationSeconds),
                SpawnScatterRadius = c.SpawnScatterRadius,
                DropScatterRadius = c.DropScatterRadius,
                PickupRadius = c.PickupRadius,
                PickupDelayTicks = SimTime.SecondsToTicks(c.PickupDelaySeconds),
                ScatterAttempts = c.ScatterAttempts,
                MvpKillWeight = c.MvpKillWeight,
                MvpDamageWeight = c.MvpDamageWeight,
                MvpHealWeight = c.MvpHealWeight,
                MvpAcornWeight = c.MvpAcornWeight,
                MvpDeathWeight = c.MvpDeathWeight
            };
            def.Validate();
            return def;
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
                LevelBonusPerLevel = c.LevelBonusPerLevel,
                UltLossOnDeath = c.UltLossOnDeath,
                TrickUses = c.TrickUses,
                TrickCooldownTicks = SimTime.SecondsToTicks(c.TrickCooldownSeconds),
                TrickUnlockLevel = c.TrickUnlockLevel,
                TalentUnlockLevel = c.TalentUnlockLevel,
                BushRevealDistance = c.BushRevealDistance,
                RevealTicks = SimTime.SecondsToTicks(c.RevealSeconds)
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
                Attack = ToAbilityDef(c.Attack, c.Id + ".Attack"),
                Ult = ToOptionalAbilityDef(c.Ult, c.Id + ".Ult"),
                UltChargeRequired = c.UltChargeRequired,
                Trick = ToOptionalAbilityDef(c.Trick, c.Id + ".Trick"),
                Talent = ToTalentDef(c.Talent)
            };
            def.Validate();
            return def;
        }

        /// <summary>Ability of an optional slot (ult, trick): null if not set or with delivery <c>None</c>.</summary>
        public static AbilityDef ToOptionalAbilityDef(AbilityConfig c, string owner) =>
            c == null || c.Delivery == DeliveryKind.None ? null : ToAbilityDef(c, owner);

        /// <summary>Talent, or null if not set or with modifier <c>None</c>.</summary>
        public static TalentDef ToTalentDef(TalentConfig c)
        {
            if (c == null || c.Modifier == TalentModifier.None) return null;
            return new TalentDef { Condition = c.Condition, Modifier = c.Modifier, Value = c.ValuePercent, Param = c.Param };
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
                ProjectileCount = c.ProjectileCount,
                SpreadDegrees = c.SpreadDegrees,
                PierceCount = c.PierceCount,
                ConeAngleDegrees = c.ConeAngleDegrees,
                BurstCount = c.BurstCount,
                BurstIntervalTicks = SimTime.SecondsToTicks(c.BurstIntervalSeconds),
                DashDistance = c.DashDistance,
                DashSpeed = c.DashSpeed,
                BreaksWalls = c.BreaksWalls,
                IgnoresWalls = c.IgnoresWalls,
                UsesMoveDirection = c.UsesMoveDirection,
                Effects = effects
            };
        }
    }
}
