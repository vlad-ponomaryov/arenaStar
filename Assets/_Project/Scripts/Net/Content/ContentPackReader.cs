using System.IO;
using System.Text;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Core.Modes;
using Arena.Net.Serialization;

namespace Arena.Net.Content
{
    /// <summary>
    /// Reads a content pack written by <see cref="ContentPackWriter"/> (its exact mirror, Server-Spec §5.4) into a
    /// <see cref="ContentBundle"/>. Checks magic, format version, body length and — on the server — the SHA-256 hash.
    /// Does not validate the defs (<see cref="ContentBundle.Validate"/> does). Runs once at start-up, never in a tick.
    /// Throws <see cref="InvalidDataException"/> on a broken or foreign pack.
    /// </summary>
    public static class ContentPackReader
    {
        /// <summary>Header only, without hashing the body (the online client takes the hash from here, D-083).</summary>
        public static ContentPackHeader ReadHeader(byte[] pack)
        {
            if (!ContentPackHeader.TryRead(pack, pack == null ? 0 : pack.Length, out ContentPackHeader header))
                throw new InvalidDataException("Content pack: no header or wrong magic");
            if (header.FormatVersion != ContentPackHeader.CurrentFormatVersion)
                throw new InvalidDataException("Content pack: format version " + header.FormatVersion + ", expected " +
                    ContentPackHeader.CurrentFormatVersion);
            if (header.BodyLength < 0 || header.BodyLength != pack.Length - ContentPackHeader.Size)
                throw new InvalidDataException("Content pack: body length " + header.BodyLength + " does not match the file (" +
                    (pack.Length - ContentPackHeader.Size) + " bytes)");
            return header;
        }

        /// <param name="verifyHash">Recompute SHA-256 of the body and compare it with the header (server: always).</param>
        public static ContentBundle Read(byte[] pack, bool verifyHash, out ContentPackHeader header)
        {
            header = ReadHeader(pack);
            if (verifyHash)
            {
                ulong actual = ContentPackHeader.ComputeHash(pack, ContentPackHeader.Size, header.BodyLength);
                if (actual != header.ContentHash)
                    throw new InvalidDataException("Content pack: hash " + ContentPackHeader.FormatHash(actual) + " != header " +
                        ContentPackHeader.FormatHash(header.ContentHash));
            }
            var r = new BitReader();
            r.Reset(pack, ContentPackHeader.Size, header.BodyLength);
            ContentBundle bundle = ReadBundle(r);
            r.AlignToByte();
            if (r.IsOverflow) throw new InvalidDataException("Content pack: body is truncated or broken");
            if (r.RemainingBits != 0) throw new InvalidDataException("Content pack: " + (r.RemainingBits / 8) + " unread bytes");
            return bundle;
        }

        public static ContentBundle Read(byte[] pack, bool verifyHash) => Read(pack, verifyHash, out _);

        private static ContentBundle ReadBundle(BitReader r)
        {
            var b = new ContentBundle();
            b.Rules = r.ReadBool() ? ReadRules(r) : null;
            int n = ReadCount(r);
            b.Fighters = n < 0 ? null : new FighterDef[n];
            for (int i = 0; i < n; i++) b.Fighters[i] = r.ReadBool() ? ReadFighter(r) : null;
            n = ReadCount(r);
            b.Maps = n < 0 ? null : new MapDef[n];
            for (int i = 0; i < n; i++) b.Maps[i] = r.ReadBool() ? ReadMap(r) : null;
            b.DefaultMapIndex = r.ReadVarInt();
            b.Acorns = r.ReadBool() ? ReadAcorns(r) : null;
            b.LastBeast = r.ReadBool() ? ReadLastBeast(r) : null;
            n = ReadCount(r);
            b.ModeMaps = n < 0 ? null : new ContentModeMaps[n];
            for (int i = 0; i < n; i++) b.ModeMaps[i] = r.ReadBool() ? ReadModeMaps(r) : null;
            b.BotBehaviour = r.ReadBool() ? ReadBotBehaviour(r) : null;
            n = ReadCount(r);
            b.BotDifficulties = n < 0 ? null : new BotDifficultyDef[n];
            for (int i = 0; i < n; i++) b.BotDifficulties[i] = r.ReadBool() ? ReadBotDifficulty(r) : null;
            if (r.ReadBool())
                b.Policy = new ContentMatchPolicy { FighterLevel = r.ReadVarInt(), UnlockAllAbilities = r.ReadBool() };
            else
                b.Policy = null;
            return b;
        }

        private static CombatRules ReadRules(BitReader r)
        {
            var c = new CombatRules();
            c.MaxCharges = r.ReadVarInt();
            c.RegenDelayTicks = r.ReadVarInt();
            c.RegenFractionPerSecond = r.ReadFloat();
            c.RespawnTicks = r.ReadVarInt();
            c.LevelBonusPerLevel = r.ReadFloat();
            c.UltLossOnDeath = r.ReadFloat();
            c.TrickUses = r.ReadVarInt();
            c.TrickCooldownTicks = r.ReadVarInt();
            c.TrickUnlockLevel = r.ReadVarInt();
            c.TalentUnlockLevel = r.ReadVarInt();
            c.BushRevealDistance = r.ReadFloat();
            c.RevealTicks = r.ReadVarInt();
            c.AutoAimPreferClearShot = r.ReadBool();
            c.AutoAimTargetsBoxes = r.ReadBool();
            c.AutoAimHealthWeightCells = r.ReadFloat();
            c.AutoAimStickyTicks = r.ReadVarInt();
            c.AutoAimStickyMarginCells = r.ReadFloat();
            c.AutoAimThreatRangeFraction = r.ReadFloat();
            c.AutoAimBoxBonusCells = r.ReadFloat();
            return c;
        }

        private static FighterDef ReadFighter(BitReader r)
        {
            var f = new FighterDef();
            f.Id = ReadString(r);
            f.NameKey = ReadString(r);
            f.MaxHealth = r.ReadVarInt();
            f.MoveSpeed = r.ReadFloat();
            f.Radius = r.ReadFloat();
            f.AttackRechargeTicks = r.ReadVarInt();
            f.Attack = ReadOptionalAbility(r);
            f.Ult = ReadOptionalAbility(r);
            f.UltChargeRequired = r.ReadVarInt();
            f.Trick = ReadOptionalAbility(r);
            if (r.ReadBool())
            {
                var t = new TalentDef();
                t.Condition = (TalentCondition)r.ReadVarInt();
                t.Modifier = (TalentModifier)r.ReadVarInt();
                t.Value = r.ReadVarInt();
                t.Param = r.ReadVarInt();
                t.Ticks = r.ReadVarInt();
                f.Talent = t;
            }
            f.BotTrickHint = (BotTrickHint)r.ReadVarInt();
            return f;
        }

        private static AbilityDef ReadOptionalAbility(BitReader r)
        {
            if (!r.ReadBool()) return null;
            var a = new AbilityDef();
            a.Delivery = (DeliveryKind)r.ReadVarInt();
            a.Range = r.ReadFloat();
            a.ProjectileSpeed = r.ReadFloat();
            a.ProjectileRadius = r.ReadFloat();
            a.ProjectileCount = r.ReadVarInt();
            a.SpreadDegrees = r.ReadFloat();
            a.PierceCount = r.ReadVarInt();
            a.PierceFalloffPercent = r.ReadVarInt();
            a.PierceAll = r.ReadBool();
            a.BounceCount = r.ReadVarInt();
            a.BounceRange = r.ReadFloat();
            a.ConeAngleDegrees = r.ReadFloat();
            a.BurstCount = r.ReadVarInt();
            a.BurstIntervalTicks = r.ReadVarInt();
            a.DashDistance = r.ReadFloat();
            a.DashSpeed = r.ReadFloat();
            a.BreaksWalls = r.ReadBool();
            a.IgnoresWalls = r.ReadBool();
            a.UsesMoveDirection = r.ReadBool();
            a.AreaRadius = r.ReadFloat();
            a.AreaDelayTicks = r.ReadVarInt();
            a.AreaDurationTicks = r.ReadVarInt();
            a.AreaPulseTicks = r.ReadVarInt();
            a.PlacedArea = (AreaKind)r.ReadVarInt();
            a.BlocksEnemyProjectiles = r.ReadBool();
            a.HiddenFromEnemies = r.ReadBool();
            int n = ReadCount(r);
            a.Effects = n < 0 ? null : new EffectDef[n];
            for (int i = 0; i < n; i++)
            {
                if (!r.ReadBool()) continue;
                var e = new EffectDef();
                e.Kind = (EffectKind)r.ReadVarInt();
                e.Filter = (TargetFilter)r.ReadVarInt();
                e.Value = r.ReadVarInt();
                e.DurationTicks = r.ReadVarInt();
                e.Distance = r.ReadFloat();
                e.Count = r.ReadVarInt();
                a.Effects[i] = e;
            }
            return a;
        }

        private static MapDef ReadMap(BitReader r)
        {
            var m = new MapDef();
            m.Id = ReadString(r);
            m.Width = r.ReadVarInt();
            m.Height = r.ReadVarInt();
            int n = ReadCount(r);
            m.Tiles = n < 0 ? null : new TileType[n];
            for (int i = 0; i < n; i++) m.Tiles[i] = (TileType)r.ReadVarInt();
            n = ReadCount(r);
            m.Spawns = n < 0 ? null : new MapSpawn[n];
            for (int i = 0; i < n; i++)
            {
                int team = r.ReadVarInt();
                m.Spawns[i] = new MapSpawn(team, ReadCell(r));
            }
            m.HasHollow = r.ReadBool();
            m.Hollow = ReadCell(r);
            return m;
        }

        private static GridCell ReadCell(BitReader r)
        {
            int column = r.ReadVarInt();
            return new GridCell(column, r.ReadVarInt());
        }

        private static AcornsModeDef ReadAcorns(BitReader r)
        {
            var a = new AcornsModeDef();
            a.SpawnIntervalTicks = r.ReadVarInt();
            a.GoldenIntervalTicks = r.ReadVarInt();
            a.GoldenValue = r.ReadVarInt();
            a.TargetAcorns = r.ReadVarInt();
            a.HoldTicks = r.ReadVarInt();
            a.DurationTicks = r.ReadVarInt();
            a.SpawnScatterRadius = r.ReadFloat();
            a.DropScatterRadius = r.ReadFloat();
            a.PickupRadius = r.ReadFloat();
            a.PickupDelayTicks = r.ReadVarInt();
            a.ScatterAttempts = r.ReadVarInt();
            a.MvpKillWeight = r.ReadFloat();
            a.MvpDamageWeight = r.ReadFloat();
            a.MvpHealWeight = r.ReadFloat();
            a.MvpAcornWeight = r.ReadFloat();
            a.MvpDeathWeight = r.ReadFloat();
            return a;
        }

        private static LastBeastModeDef ReadLastBeast(BitReader r)
        {
            var l = new LastBeastModeDef();
            l.FogStartTicks = r.ReadVarInt();
            l.FogStepIntervalTicks = r.ReadVarInt();
            l.FogShrinkCells = r.ReadFloat();
            l.FogMinHalfSize = r.ReadFloat();
            l.FogDamagePerSecond = r.ReadVarInt();
            l.FogDamageGrowthPerStep = r.ReadVarInt();
            l.FogPulseTicks = r.ReadVarInt();
            l.BoxHealth = r.ReadVarInt();
            l.BerriesPerBox = r.ReadVarInt();
            l.BerryDamagePercent = r.ReadVarInt();
            l.BerryHealthPercent = r.ReadVarInt();
            l.MaxBonusBerries = r.ReadVarInt();
            l.BerryHealPercent = r.ReadVarInt();
            l.ExtraBerriesOnDefeat = r.ReadVarInt();
            l.BerryPickupRadius = r.ReadFloat();
            l.BerryDropScatterRadius = r.ReadFloat();
            l.BerryPickupDelayTicks = r.ReadVarInt();
            l.ScatterAttempts = r.ReadVarInt();
            l.MaxDurationTicks = r.ReadVarInt();
            return l;
        }

        private static ContentModeMaps ReadModeMaps(BitReader r)
        {
            var m = new ContentModeMaps();
            m.Mode = (ModeKind)r.ReadVarInt();
            int n = ReadCount(r);
            m.MapIndices = n < 0 ? null : new int[n];
            for (int i = 0; i < n; i++) m.MapIndices[i] = r.ReadVarInt();
            return m;
        }

        private static BotBehaviourDef ReadBotBehaviour(BitReader r)
        {
            var b = new BotBehaviourDef();
            b.DecisionIntervalTicks = r.ReadVarInt();
            b.ViewRadius = r.ReadFloat();
            b.RetreatHealthFraction = r.ReadFloat();
            b.RecoverHealthFraction = r.ReadFloat();
            b.LastChargeEnemyDistance = r.ReadFloat();
            b.EngageRangeFraction = r.ReadFloat();
            b.FireRangeFraction = r.ReadFloat();
            b.MinShotIntervalTicks = r.ReadVarInt();
            b.StrafeSwitchTicks = r.ReadVarInt();
            b.WaypointReachDistance = r.ReadFloat();
            b.WanderRadiusCells = r.ReadVarInt();
            b.WanderAttempts = r.ReadVarInt();
            b.RetreatScore = r.ReadFloat();
            b.AttackScore = r.ReadFloat();
            b.AttackProximityBonus = r.ReadFloat();
            b.ObjectiveScore = r.ReadFloat();
            b.WanderScore = r.ReadFloat();
            b.PathNodeLimit = r.ReadVarInt();
            b.MaxPathLength = r.ReadVarInt();
            b.UltMinEnemies = r.ReadVarInt();
            b.FocusMinEnemies = r.ReadVarInt();
            b.FocusWindowTicks = r.ReadVarInt();
            b.EscapeEnemyDistance = r.ReadFloat();
            b.EnemyCloseDistance = r.ReadFloat();
            b.HealAllyHealthFraction = r.ReadFloat();
            b.ObjectivePlaceDistance = r.ReadFloat();
            b.MemoryTicks = r.ReadVarInt();
            b.MemoryPredictTicks = r.ReadVarInt();
            b.BushNearCells = r.ReadVarInt();
            b.HideBushRadiusCells = r.ReadVarInt();
            b.EmptyBushShotAttempts = r.ReadVarInt();
            b.CarrierAcorns = r.ReadVarInt();
            b.CarrierRetreatHealthFraction = r.ReadFloat();
            b.CarrierBehindDistance = r.ReadFloat();
            b.HideInBushScore = r.ReadFloat();
            b.CheckBushScore = r.ReadFloat();
            b.StayWithTeamScore = r.ReadFloat();
            b.FleeFogScore = r.ReadFloat();
            b.FogWarnTicks = r.ReadVarInt();
            b.BreakBoxScore = r.ReadFloat();
            b.LootRadiusCells = r.ReadVarInt();
            b.FogSafeMarginCells = r.ReadFloat();
            b.ExploreRadiusCells = r.ReadVarInt();
            b.CautiousEngageDistance = r.ReadFloat();
            b.CautiousUntilAlive = r.ReadVarInt();
            b.ProvokedTicks = r.ReadVarInt();
            return b;
        }

        private static BotDifficultyDef ReadBotDifficulty(BitReader r)
        {
            var d = new BotDifficultyDef();
            d.Difficulty = (BotDifficulty)r.ReadVarInt();
            d.AimSpreadDegrees = r.ReadFloat();
            d.ReactionTicks = r.ReadVarInt();
            d.UltUseChance = r.ReadFloat();
            d.UltMisuseChance = r.ReadFloat();
            d.TrickUseChance = r.ReadFloat();
            d.TrickMisuseChance = r.ReadFloat();
            d.EmptyBushShotChance = r.ReadFloat();
            d.RetreatThresholdJitter = r.ReadFloat();
            d.LeadFraction = r.ReadFloat();
            d.FocusHealthWeightCells = r.ReadFloat();
            d.DodgeChance = r.ReadFloat();
            d.DodgeLookaheadTicks = r.ReadVarInt();
            d.DodgeMarginCells = r.ReadFloat();
            d.ShotIntervalTicks = r.ReadVarInt();
            return d;
        }

        /// <summary>Array length, or −1 for a null array. Every element takes at least one bit, so a count above the
        /// remaining bits is a broken pack (and never allocates a huge array).</summary>
        private static int ReadCount(BitReader r)
        {
            uint raw = r.ReadVarUInt();
            if (r.IsOverflow) throw new InvalidDataException("Content pack: body is truncated or broken");
            if (raw == 0u) return -1;
            uint count = raw - 1u;
            if (count > (uint)r.RemainingBits) throw new InvalidDataException("Content pack: array of " + count + " entries is longer than the data");
            return (int)count;
        }

        private static string ReadString(BitReader r)
        {
            int length = ReadCount(r);
            if (length < 0) return null;
            if (length * 8L > r.RemainingBits) throw new InvalidDataException("Content pack: string is longer than the data");
            var bytes = new byte[length];
            r.ReadBytes(bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
