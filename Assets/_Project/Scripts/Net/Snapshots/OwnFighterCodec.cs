using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.State;
using Arena.Net.Serialization;

namespace Arena.Net.Snapshots
{
    /// <summary>
    /// The own fighter of a snapshot (Server-Spec §4.4 item 3): <b>every</b> field of <see cref="FighterState"/>, exact
    /// (floats by their bits), each as "changed" bit + value against the baseline view; tick fields relative to the snapshot
    /// tick. The own statuses go as a whole list when it differs. <c>SnapshotCodecTests</c> checks by reflection that every
    /// field of <see cref="FighterState"/> round-trips — a new field must be added to both methods here.
    /// </summary>
    public static class OwnFighterCodec
    {
        public static void Write(BitWriter w, in FighterState f, in FighterState p, int baseTick)
        {
            DeltaCodec.WriteBool(w, f.Active, p.Active);
            DeltaCodec.WriteBool(w, f.Alive, p.Alive);
            DeltaCodec.WriteInt(w, f.DefIndex, p.DefIndex);
            DeltaCodec.WriteInt(w, f.Team, p.Team);
            DeltaCodec.WriteInt(w, f.Level, p.Level);
            DeltaCodec.WriteVec2(w, f.Position, p.Position);
            DeltaCodec.WriteVec2(w, f.Velocity, p.Velocity);
            DeltaCodec.WriteVec2(w, f.Facing, p.Facing);
            DeltaCodec.WriteInt(w, f.Health, p.Health);
            DeltaCodec.WriteInt(w, f.MaxHealth, p.MaxHealth);
            DeltaCodec.WriteInt(w, f.Charges, p.Charges);
            DeltaCodec.WriteInt(w, f.ChargeProgressTicks, p.ChargeProgressTicks);
            DeltaCodec.WriteTick(w, f.LastCombatTick, p.LastCombatTick, baseTick);
            DeltaCodec.WriteFloat(w, f.RegenRemainder, p.RegenRemainder);
            DeltaCodec.WriteTick(w, f.RespawnTick, p.RespawnTick, baseTick);
            DeltaCodec.WriteInt(w, f.BurstShotsLeft, p.BurstShotsLeft);
            DeltaCodec.WriteTick(w, f.NextBurstTick, p.NextBurstTick, baseTick);
            DeltaCodec.WriteVec2(w, f.BurstDir, p.BurstDir);
            DeltaCodec.WriteInt(w, f.UltCharge, p.UltCharge);
            DeltaCodec.WriteInt(w, f.TrickUsesLeft, p.TrickUsesLeft);
            DeltaCodec.WriteTick(w, f.TrickReadyTick, p.TrickReadyTick, baseTick);
            DeltaCodec.WriteBool(w, f.TrickUnlocked, p.TrickUnlocked);
            DeltaCodec.WriteBool(w, f.TalentUnlocked, p.TalentUnlocked);
            DeltaCodec.WriteInt(w, f.CarriedAcorns, p.CarriedAcorns);
            DeltaCodec.WriteInt(w, f.Berries, p.Berries);
            DeltaCodec.WriteInt(w, f.BoostedShotsLeft, p.BoostedShotsLeft);
            DeltaCodec.WriteInt(w, f.BoostPercent, p.BoostPercent);
            DeltaCodec.WriteInt(w, f.ShotBonusPercent, p.ShotBonusPercent);
            DeltaCodec.WriteFloat(w, f.SpeedMul, p.SpeedMul);
            DeltaCodec.WriteFloat(w, f.DamageTakenMul, p.DamageTakenMul);
            DeltaCodec.WriteBool(w, f.Stunned, p.Stunned);
            DeltaCodec.WriteBool(w, f.Invisible, p.Invisible);
            DeltaCodec.WriteUInt(w, f.MarkedByTeamMask, p.MarkedByTeamMask);
            DeltaCodec.WriteBool(w, f.InBush, p.InBush);
            DeltaCodec.WriteUInt(w, f.VisibleToTeams, p.VisibleToTeams);
            DeltaCodec.WriteTick(w, f.RevealedUntilTick, p.RevealedUntilTick, baseTick);
            DeltaCodec.WriteVec2(w, f.KnockbackVelocity, p.KnockbackVelocity);
            DeltaCodec.WriteInt(w, f.KnockbackTicks, p.KnockbackTicks);
            DeltaCodec.WriteVec2(w, f.DashVelocity, p.DashVelocity);
            DeltaCodec.WriteInt(w, f.DashTicksLeft, p.DashTicksLeft);
            DeltaCodec.WriteInt(w, f.DashAbilityRef, p.DashAbilityRef);
            DeltaCodec.WriteUInt(w, f.DashHitMask, p.DashHitMask);
            DeltaCodec.WriteUInt(w, (uint)f.Buttons, (uint)p.Buttons);
            DeltaCodec.WriteUInt(w, (uint)f.PrevButtons, (uint)p.PrevButtons);
        }

        public static FighterState Read(BitReader r, in FighterState p, int baseTick)
        {
            FighterState f = p;
            f.Active = DeltaCodec.ReadBool(r, p.Active);
            f.Alive = DeltaCodec.ReadBool(r, p.Alive);
            f.DefIndex = DeltaCodec.ReadInt(r, p.DefIndex);
            f.Team = DeltaCodec.ReadInt(r, p.Team);
            f.Level = DeltaCodec.ReadInt(r, p.Level);
            f.Position = DeltaCodec.ReadVec2(r, p.Position);
            f.Velocity = DeltaCodec.ReadVec2(r, p.Velocity);
            f.Facing = DeltaCodec.ReadVec2(r, p.Facing);
            f.Health = DeltaCodec.ReadInt(r, p.Health);
            f.MaxHealth = DeltaCodec.ReadInt(r, p.MaxHealth);
            f.Charges = DeltaCodec.ReadInt(r, p.Charges);
            f.ChargeProgressTicks = DeltaCodec.ReadInt(r, p.ChargeProgressTicks);
            f.LastCombatTick = DeltaCodec.ReadTick(r, p.LastCombatTick, baseTick);
            f.RegenRemainder = DeltaCodec.ReadFloat(r, p.RegenRemainder);
            f.RespawnTick = DeltaCodec.ReadTick(r, p.RespawnTick, baseTick);
            f.BurstShotsLeft = DeltaCodec.ReadInt(r, p.BurstShotsLeft);
            f.NextBurstTick = DeltaCodec.ReadTick(r, p.NextBurstTick, baseTick);
            f.BurstDir = DeltaCodec.ReadVec2(r, p.BurstDir);
            f.UltCharge = DeltaCodec.ReadInt(r, p.UltCharge);
            f.TrickUsesLeft = DeltaCodec.ReadInt(r, p.TrickUsesLeft);
            f.TrickReadyTick = DeltaCodec.ReadTick(r, p.TrickReadyTick, baseTick);
            f.TrickUnlocked = DeltaCodec.ReadBool(r, p.TrickUnlocked);
            f.TalentUnlocked = DeltaCodec.ReadBool(r, p.TalentUnlocked);
            f.CarriedAcorns = DeltaCodec.ReadInt(r, p.CarriedAcorns);
            f.Berries = DeltaCodec.ReadInt(r, p.Berries);
            f.BoostedShotsLeft = DeltaCodec.ReadInt(r, p.BoostedShotsLeft);
            f.BoostPercent = DeltaCodec.ReadInt(r, p.BoostPercent);
            f.ShotBonusPercent = DeltaCodec.ReadInt(r, p.ShotBonusPercent);
            f.SpeedMul = DeltaCodec.ReadFloat(r, p.SpeedMul);
            f.DamageTakenMul = DeltaCodec.ReadFloat(r, p.DamageTakenMul);
            f.Stunned = DeltaCodec.ReadBool(r, p.Stunned);
            f.Invisible = DeltaCodec.ReadBool(r, p.Invisible);
            f.MarkedByTeamMask = DeltaCodec.ReadUInt(r, p.MarkedByTeamMask);
            f.InBush = DeltaCodec.ReadBool(r, p.InBush);
            f.VisibleToTeams = DeltaCodec.ReadUInt(r, p.VisibleToTeams);
            f.RevealedUntilTick = DeltaCodec.ReadTick(r, p.RevealedUntilTick, baseTick);
            f.KnockbackVelocity = DeltaCodec.ReadVec2(r, p.KnockbackVelocity);
            f.KnockbackTicks = DeltaCodec.ReadInt(r, p.KnockbackTicks);
            f.DashVelocity = DeltaCodec.ReadVec2(r, p.DashVelocity);
            f.DashTicksLeft = DeltaCodec.ReadInt(r, p.DashTicksLeft);
            f.DashAbilityRef = DeltaCodec.ReadInt(r, p.DashAbilityRef);
            f.DashHitMask = DeltaCodec.ReadUInt(r, p.DashHitMask);
            f.Buttons = (InputButtons)(byte)DeltaCodec.ReadUInt(r, (uint)p.Buttons);
            f.PrevButtons = (InputButtons)(byte)DeltaCodec.ReadUInt(r, (uint)p.PrevButtons);
            return f;
        }

        /// <summary>Own statuses: one bit "same as the baseline", otherwise the whole list (they change rarely).</summary>
        public static void WriteStatuses(BitWriter w, StatusEffectState[] items, int count, StatusEffectState[] prevItems, int prevCount, int baseTick)
        {
            bool same = count == prevCount;
            for (int i = 0; same && i < count; i++) same = items[i].Equals(prevItems[i]);
            w.WriteBool(!same);
            if (same) return;
            w.WriteVarUInt((uint)count);
            for (int i = 0; i < count; i++)
            {
                ref StatusEffectState s = ref items[i];
                w.WriteVarUInt((uint)s.Kind);
                w.WriteVarInt(s.Value);
                w.WriteVarInt(unchecked(s.ExpireTick - baseTick));
                w.WriteVarInt(s.Source);
                w.WriteVarInt(s.SourceTeam);
            }
        }

        /// <summary>Reads the own statuses into <paramref name="items"/>; false if the count does not fit (malformed).</summary>
        public static bool ReadStatuses(BitReader r, StatusEffectState[] items, ref int count, int ownSlot, int baseTick)
        {
            if (!r.ReadBool()) return true;
            uint n = r.ReadVarUInt();
            if (n > (uint)items.Length) return false;
            for (int i = 0; i < (int)n; i++)
            {
                var s = new StatusEffectState
                {
                    Active = true,
                    Target = ownSlot,
                    Kind = (EffectKind)(byte)r.ReadVarUInt(),
                    Value = r.ReadVarInt()
                };
                s.ExpireTick = unchecked(r.ReadVarInt() + baseTick);
                s.Source = r.ReadVarInt();
                s.SourceTeam = r.ReadVarInt();
                items[i] = s;
            }
            count = (int)n;
            return true;
        }
    }
}
