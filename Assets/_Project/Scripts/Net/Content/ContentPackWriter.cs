using System;
using System.Text;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Map;
using Arena.Net.Serialization;

namespace Arena.Net.Content
{
    /// <summary>
    /// Canonical binary serialization of a <see cref="ContentBundle"/> (Server-Spec §5.4): fixed field order (declaration
    /// order of the def classes), ints as zigzag varints, floats bit for bit, bools as one bit, enums as varints of their
    /// value, strings as UTF-8 with a varint length + 1 (0 = null), optional objects behind a presence bit, arrays behind a
    /// varint count + 1 (0 = null). The same bundle always gives the same bytes; <see cref="ContentPackReader"/> is its exact
    /// mirror. Runs in the editor (export) and in tests, never in a match tick.
    /// </summary>
    public static class ContentPackWriter
    {
        private const int InitialBodyBytes = 64 * 1024;

        /// <summary>Header + body of the pack.</summary>
        public static byte[] Write(ContentBundle bundle)
        {
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            byte[] body = WriteBody(bundle, out int bodyLength);
            var header = new ContentPackHeader
            {
                Magic = ContentPackHeader.MagicValue,
                FormatVersion = ContentPackHeader.CurrentFormatVersion,
                BodyLength = bodyLength,
                ContentHash = ContentPackHeader.ComputeHash(body, 0, bodyLength)
            };
            var pack = new byte[ContentPackHeader.Size + bodyLength];
            var writer = new BitWriter(pack);
            header.Write(writer);
            if (writer.IsOverflow || writer.BytesWritten != ContentPackHeader.Size)
                throw new InvalidOperationException("ContentPackWriter: header size");
            Buffer.BlockCopy(body, 0, pack, ContentPackHeader.Size, bodyLength);
            return pack;
        }

        private static byte[] WriteBody(ContentBundle bundle, out int length)
        {
            int capacity = InitialBodyBytes;
            while (true)
            {
                var buffer = new byte[capacity];
                var w = new BitWriter(buffer);
                WriteBundle(w, bundle);
                w.AlignToByte();
                if (!w.IsOverflow)
                {
                    length = w.BytesWritten;
                    return buffer;
                }
                capacity *= 2;
            }
        }

        private static void WriteBundle(BitWriter w, ContentBundle b)
        {
            WritePresence(w, b.Rules);
            if (b.Rules != null) WriteRules(w, b.Rules);
            WriteCount(w, b.Fighters);
            if (b.Fighters != null)
                for (int i = 0; i < b.Fighters.Length; i++)
                {
                    WritePresence(w, b.Fighters[i]);
                    if (b.Fighters[i] != null) WriteFighter(w, b.Fighters[i]);
                }
            WriteCount(w, b.Maps);
            if (b.Maps != null)
                for (int i = 0; i < b.Maps.Length; i++)
                {
                    WritePresence(w, b.Maps[i]);
                    if (b.Maps[i] != null) WriteMap(w, b.Maps[i]);
                }
            w.WriteVarInt(b.DefaultMapIndex);
            WritePresence(w, b.Acorns);
            if (b.Acorns != null) WriteAcorns(w, b.Acorns);
            WritePresence(w, b.LastBeast);
            if (b.LastBeast != null) WriteLastBeast(w, b.LastBeast);
            WriteCount(w, b.ModeMaps);
            if (b.ModeMaps != null)
                for (int i = 0; i < b.ModeMaps.Length; i++)
                {
                    WritePresence(w, b.ModeMaps[i]);
                    if (b.ModeMaps[i] != null) WriteModeMaps(w, b.ModeMaps[i]);
                }
            WritePresence(w, b.BotBehaviour);
            if (b.BotBehaviour != null) WriteBotBehaviour(w, b.BotBehaviour);
            WriteCount(w, b.BotDifficulties);
            if (b.BotDifficulties != null)
                for (int i = 0; i < b.BotDifficulties.Length; i++)
                {
                    WritePresence(w, b.BotDifficulties[i]);
                    if (b.BotDifficulties[i] != null) WriteBotDifficulty(w, b.BotDifficulties[i]);
                }
            WritePresence(w, b.Policy);
            if (b.Policy != null)
            {
                w.WriteVarInt(b.Policy.FighterLevel);
                w.WriteBool(b.Policy.UnlockAllAbilities);
            }
        }

        private static void WriteRules(BitWriter w, CombatRules r)
        {
            w.WriteVarInt(r.MaxCharges);
            w.WriteVarInt(r.RegenDelayTicks);
            w.WriteFloat(r.RegenFractionPerSecond);
            w.WriteVarInt(r.RespawnTicks);
            w.WriteFloat(r.LevelBonusPerLevel);
            w.WriteFloat(r.UltLossOnDeath);
            w.WriteVarInt(r.TrickUses);
            w.WriteVarInt(r.TrickCooldownTicks);
            w.WriteVarInt(r.TrickUnlockLevel);
            w.WriteVarInt(r.TalentUnlockLevel);
            w.WriteFloat(r.BushRevealDistance);
            w.WriteVarInt(r.RevealTicks);
        }

        private static void WriteFighter(BitWriter w, FighterDef f)
        {
            WriteString(w, f.Id);
            WriteString(w, f.NameKey);
            w.WriteVarInt(f.MaxHealth);
            w.WriteFloat(f.MoveSpeed);
            w.WriteFloat(f.Radius);
            w.WriteVarInt(f.AttackRechargeTicks);
            WriteOptionalAbility(w, f.Attack);
            WriteOptionalAbility(w, f.Ult);
            w.WriteVarInt(f.UltChargeRequired);
            WriteOptionalAbility(w, f.Trick);
            WritePresence(w, f.Talent);
            if (f.Talent != null)
            {
                w.WriteVarInt((int)f.Talent.Condition);
                w.WriteVarInt((int)f.Talent.Modifier);
                w.WriteVarInt(f.Talent.Value);
                w.WriteVarInt(f.Talent.Param);
                w.WriteVarInt(f.Talent.Ticks);
            }
            w.WriteVarInt((int)f.BotTrickHint);
        }

        private static void WriteOptionalAbility(BitWriter w, AbilityDef a)
        {
            WritePresence(w, a);
            if (a == null) return;
            w.WriteVarInt((int)a.Delivery);
            w.WriteFloat(a.Range);
            w.WriteFloat(a.ProjectileSpeed);
            w.WriteFloat(a.ProjectileRadius);
            w.WriteVarInt(a.ProjectileCount);
            w.WriteFloat(a.SpreadDegrees);
            w.WriteVarInt(a.PierceCount);
            w.WriteVarInt(a.PierceFalloffPercent);
            w.WriteBool(a.PierceAll);
            w.WriteVarInt(a.BounceCount);
            w.WriteFloat(a.BounceRange);
            w.WriteFloat(a.ConeAngleDegrees);
            w.WriteVarInt(a.BurstCount);
            w.WriteVarInt(a.BurstIntervalTicks);
            w.WriteFloat(a.DashDistance);
            w.WriteFloat(a.DashSpeed);
            w.WriteBool(a.BreaksWalls);
            w.WriteBool(a.IgnoresWalls);
            w.WriteBool(a.UsesMoveDirection);
            w.WriteFloat(a.AreaRadius);
            w.WriteVarInt(a.AreaDelayTicks);
            w.WriteVarInt(a.AreaDurationTicks);
            w.WriteVarInt(a.AreaPulseTicks);
            w.WriteVarInt((int)a.PlacedArea);
            w.WriteBool(a.BlocksEnemyProjectiles);
            w.WriteBool(a.HiddenFromEnemies);
            WriteCount(w, a.Effects);
            if (a.Effects == null) return;
            for (int i = 0; i < a.Effects.Length; i++)
            {
                EffectDef e = a.Effects[i];
                WritePresence(w, e);
                if (e == null) continue;
                w.WriteVarInt((int)e.Kind);
                w.WriteVarInt((int)e.Filter);
                w.WriteVarInt(e.Value);
                w.WriteVarInt(e.DurationTicks);
                w.WriteFloat(e.Distance);
                w.WriteVarInt(e.Count);
            }
        }

        private static void WriteMap(BitWriter w, MapDef m)
        {
            WriteString(w, m.Id);
            w.WriteVarInt(m.Width);
            w.WriteVarInt(m.Height);
            WriteCount(w, m.Tiles);
            if (m.Tiles != null)
                for (int i = 0; i < m.Tiles.Length; i++) w.WriteVarInt((int)m.Tiles[i]);
            WriteCount(w, m.Spawns);
            if (m.Spawns != null)
                for (int i = 0; i < m.Spawns.Length; i++)
                {
                    w.WriteVarInt(m.Spawns[i].Team);
                    WriteCell(w, m.Spawns[i].Cell);
                }
            w.WriteBool(m.HasHollow);
            WriteCell(w, m.Hollow);
        }

        private static void WriteCell(BitWriter w, GridCell c)
        {
            w.WriteVarInt(c.Column);
            w.WriteVarInt(c.Row);
        }

        private static void WriteAcorns(BitWriter w, AcornsModeDef a)
        {
            w.WriteVarInt(a.SpawnIntervalTicks);
            w.WriteVarInt(a.GoldenIntervalTicks);
            w.WriteVarInt(a.GoldenValue);
            w.WriteVarInt(a.TargetAcorns);
            w.WriteVarInt(a.HoldTicks);
            w.WriteVarInt(a.DurationTicks);
            w.WriteFloat(a.SpawnScatterRadius);
            w.WriteFloat(a.DropScatterRadius);
            w.WriteFloat(a.PickupRadius);
            w.WriteVarInt(a.PickupDelayTicks);
            w.WriteVarInt(a.ScatterAttempts);
            w.WriteFloat(a.MvpKillWeight);
            w.WriteFloat(a.MvpDamageWeight);
            w.WriteFloat(a.MvpHealWeight);
            w.WriteFloat(a.MvpAcornWeight);
            w.WriteFloat(a.MvpDeathWeight);
        }

        private static void WriteLastBeast(BitWriter w, LastBeastModeDef l)
        {
            w.WriteVarInt(l.FogStartTicks);
            w.WriteVarInt(l.FogStepIntervalTicks);
            w.WriteFloat(l.FogShrinkCells);
            w.WriteFloat(l.FogMinHalfSize);
            w.WriteVarInt(l.FogDamagePerSecond);
            w.WriteVarInt(l.FogDamageGrowthPerStep);
            w.WriteVarInt(l.FogPulseTicks);
            w.WriteVarInt(l.BoxHealth);
            w.WriteVarInt(l.BerriesPerBox);
            w.WriteVarInt(l.BerryDamagePercent);
            w.WriteVarInt(l.BerryHealthPercent);
            w.WriteVarInt(l.ExtraBerriesOnDefeat);
            w.WriteFloat(l.BerryPickupRadius);
            w.WriteFloat(l.BerryDropScatterRadius);
            w.WriteVarInt(l.BerryPickupDelayTicks);
            w.WriteVarInt(l.ScatterAttempts);
            w.WriteVarInt(l.MaxDurationTicks);
        }

        private static void WriteModeMaps(BitWriter w, ContentModeMaps m)
        {
            w.WriteVarInt((int)m.Mode);
            WriteCount(w, m.MapIndices);
            if (m.MapIndices != null)
                for (int i = 0; i < m.MapIndices.Length; i++) w.WriteVarInt(m.MapIndices[i]);
        }

        private static void WriteBotBehaviour(BitWriter w, BotBehaviourDef b)
        {
            w.WriteVarInt(b.DecisionIntervalTicks);
            w.WriteFloat(b.ViewRadius);
            w.WriteFloat(b.RetreatHealthFraction);
            w.WriteFloat(b.RecoverHealthFraction);
            w.WriteFloat(b.LastChargeEnemyDistance);
            w.WriteFloat(b.EngageRangeFraction);
            w.WriteFloat(b.FireRangeFraction);
            w.WriteVarInt(b.MinShotIntervalTicks);
            w.WriteVarInt(b.StrafeSwitchTicks);
            w.WriteFloat(b.WaypointReachDistance);
            w.WriteVarInt(b.WanderRadiusCells);
            w.WriteVarInt(b.WanderAttempts);
            w.WriteFloat(b.RetreatScore);
            w.WriteFloat(b.AttackScore);
            w.WriteFloat(b.AttackProximityBonus);
            w.WriteFloat(b.ObjectiveScore);
            w.WriteFloat(b.WanderScore);
            w.WriteVarInt(b.PathNodeLimit);
            w.WriteVarInt(b.MaxPathLength);
            w.WriteVarInt(b.UltMinEnemies);
            w.WriteVarInt(b.FocusMinEnemies);
            w.WriteVarInt(b.FocusWindowTicks);
            w.WriteFloat(b.EscapeEnemyDistance);
            w.WriteFloat(b.EnemyCloseDistance);
            w.WriteFloat(b.HealAllyHealthFraction);
            w.WriteFloat(b.ObjectivePlaceDistance);
            w.WriteVarInt(b.MemoryTicks);
            w.WriteVarInt(b.MemoryPredictTicks);
            w.WriteVarInt(b.BushNearCells);
            w.WriteVarInt(b.HideBushRadiusCells);
            w.WriteVarInt(b.EmptyBushShotAttempts);
            w.WriteVarInt(b.CarrierAcorns);
            w.WriteFloat(b.CarrierRetreatHealthFraction);
            w.WriteFloat(b.CarrierBehindDistance);
            w.WriteFloat(b.HideInBushScore);
            w.WriteFloat(b.CheckBushScore);
            w.WriteFloat(b.StayWithTeamScore);
            w.WriteFloat(b.FleeFogScore);
            w.WriteVarInt(b.FogWarnTicks);
        }

        private static void WriteBotDifficulty(BitWriter w, BotDifficultyDef d)
        {
            w.WriteVarInt((int)d.Difficulty);
            w.WriteFloat(d.AimSpreadDegrees);
            w.WriteVarInt(d.ReactionTicks);
            w.WriteFloat(d.UltUseChance);
            w.WriteFloat(d.UltMisuseChance);
            w.WriteFloat(d.TrickUseChance);
            w.WriteFloat(d.TrickMisuseChance);
            w.WriteFloat(d.EmptyBushShotChance);
            w.WriteFloat(d.RetreatThresholdJitter);
        }

        private static void WritePresence(BitWriter w, object value) => w.WriteBool(value != null);

        /// <summary>Array length + 1 (0 = null array).</summary>
        private static void WriteCount(BitWriter w, Array array) => w.WriteVarUInt(array == null ? 0u : (uint)array.Length + 1u);

        private static void WriteString(BitWriter w, string s)
        {
            if (s == null)
            {
                w.WriteVarUInt(0u);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            w.WriteVarUInt((uint)bytes.Length + 1u);
            w.WriteBytes(bytes, 0, bytes.Length);
        }
    }
}
