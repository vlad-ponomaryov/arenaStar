using Arena.Core.Defs;
using Arena.Data;
using Arena.Data.Localization;
using Arena.Editor.Setup;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.Data
{
    /// <summary>
    /// Creates or overwrites all config assets (D-010: on stage 2 the seeder is the source of truth, idempotent).
    /// Numbers from GDD §5.2 / §6; numbers that are not in the GDD are marked PROTO here and in Decisions.md (D-013).
    /// Run: Tools/unity-method.ps1 -Method Arena.Editor.Data.DataSeeder.SeedAll
    /// </summary>
    public static class DataSeeder
    {
        public const string DataRoot = "Assets/_Project/Data";
        public const string CombatFolder = DataRoot + "/Combat";
        public const string FightersFolder = DataRoot + "/Fighters";
        public const string FeelFolder = DataRoot + "/Feel";
        public const string MapsFolder = DataRoot + "/Maps";
        public const string AcornsGladePath = MapsFolder + "/Acorns_Glade.asset";
        public const string CameraConfigPath = FeelFolder + "/CameraConfig.asset";
        public const string MatchRunnerConfigPath = CombatFolder + "/MatchRunnerConfig.asset";
        public const string CombatRulesPath = CombatFolder + "/CombatRules.asset";
        public const string GameDatabasePath = DataRoot + "/GameDatabase.asset";
        public const string ModesFolder = DataRoot + "/Modes";
        public const string AcornsModePath = ModesFolder + "/Acorns.asset";
        public const string LocalizationFolder = DataRoot + "/Localization";
        public const string StringsPath = LocalizationFolder + "/Strings.asset";

        // Acorns mode (GDD §5.4): acorn every 6 s, golden every 60 s worth 3, 12 to hold for 12 s, 2:30 match.
        private const float AcornSpawnSeconds = 6f;
        private const float AcornGoldenSeconds = 60f;
        private const int AcornGoldenValue = 3;
        private const int AcornTarget = 12;
        private const float AcornHoldSeconds = 12f;
        private const float AcornMatchSeconds = 150f;

        // PROTO (D-013, D-025): scatter around the hollow / the defeated fighter, acorn radius, pickup delay of dropped
        // acorns, landing attempts, MVP weights (a takedown ≈ 1000 damage, an acorn ≈ 400, a knockout −500).
        private const float AcornSpawnScatter = 1.5f;
        private const float AcornDropScatter = 1.2f;
        private const float AcornPickupRadius = 0.3f;
        private const float AcornPickupDelaySeconds = 0.5f;
        private const int AcornScatterAttempts = 8;
        private const float MvpKillWeight = 1000f;
        private const float MvpDamageWeight = 1f;
        private const float MvpHealWeight = 1f;
        private const float MvpAcornWeight = 400f;
        private const float MvpDeathWeight = 500f;

        // UI strings EN / RU (D-019). Fighter names are original (GDD §6).
        private static readonly string[][] Strings =
        {
            new[] { LocKeys.MenuPlay, "Play", "Играть" },
            new[] { LocKeys.MenuFighter, "Fighter", "Боец" },
            new[] { LocKeys.MenuBots, "Bots", "Боты" },
            new[] { LocKeys.MenuLanguage, "Language", "Язык" },
            new[] { LocKeys.MenuVersion, "Version", "Версия" },
            new[] { LocKeys.BotsNovice, "Novice", "Новичок" },
            new[] { LocKeys.BotsEasy, "Easy", "Лёгкий" },
            new[] { LocKeys.LanguageEn, "English", "English" },
            new[] { LocKeys.LanguageRu, "Русский", "Русский" },
            new[] { LocKeys.ModeAcorns, "Acorns", "Жёлуди" },
            new[] { LocKeys.HudHoldAlly, "We hold:", "Держим:" },
            new[] { LocKeys.HudHoldEnemy, "They hold:", "Держат враги:" },
            new[] { LocKeys.ResultsVictory, "Victory!", "Победа!" },
            new[] { LocKeys.ResultsDefeat, "Defeat", "Поражение" },
            new[] { LocKeys.ResultsDraw, "Draw", "Ничья" },
            new[] { LocKeys.ResultsMvp, "Best player", "Лучший игрок" },
            new[] { LocKeys.ResultsAgain, "Play again", "Ещё раз" },
            new[] { LocKeys.ResultsMenu, "Menu", "Меню" },
            new[] { LocKeys.ResultsKills, "Takedowns", "Победы" },
            new[] { LocKeys.ResultsDeaths, "Knocked out", "Выбыл" },
            new[] { LocKeys.ResultsDamage, "Damage", "Урон" },
            new[] { LocKeys.ResultsAcorns, "Acorns", "Жёлуди" },
            new[] { "fighter.pix.name", "Pix", "Пикс" },
            new[] { "fighter.bruno.name", "Bruno", "Бруно" },
            new[] { "fighter.chomp.name", "Chomp", "Чомп" },
            new[] { "fighter.kora.name", "Kora", "Кора" },
            new[] { "fighter.nova.name", "Nova", "Нова" },
            new[] { "fighter.vex.name", "Vex", "Векс" }
        };

        /// <summary>Fighter ids in menu order (asset names are the capitalized ids).</summary>
        public static readonly string[] FighterIds = { "pix", "bruno", "chomp", "kora", "nova", "vex" };

        // GDD §5.2
        private const int MaxCharges = 3;
        private const float RegenDelaySeconds = 3f;
        private const float RegenFractionPerSecond = 0.12f;
        private const float LevelBonusPerLevel = 0.06f;
        private const float SlowSpeed = 2.2f;
        private const float NormalSpeed = 2.5f;
        private const float FastSpeed = 2.8f;
        private const float RespawnSeconds = 3f;
        private const float UltLossOnDeath = 0.5f;
        private const int TrickUses = 3;
        private const float TrickCooldownSeconds = 5f;

        // GDD §9.4: trick opens at level 5, talent at 8 (the prototype unlocks all, D-012).
        private const int TrickUnlockLevel = 5;
        private const int TalentUnlockLevel = 8;
        private const bool UnlockAllAbilities = true;

        // GDD §5.2: a bush hides unless an enemy is closer than 2 cells; an attack reveals for 1 s (D-024).
        private const float BushRevealDistance = 2f;
        private const float RevealSeconds = 1f;

        // Acorns map "Glade" (original layout, 21×31, GDD §5.4–5.5): three lanes split by walls and ponds, bushes on the
        // flanks and next to the centre (equal for both sides), breakable cover in front of each base, the hollow in the
        // centre. Only the bottom half (team 0, rows 0–14, written top row first) and the middle row are written here;
        // the top half is generated as the central mirror (A → B), so the map is symmetric by construction.
        private const string AcornsGladeId = "acorns_glade";
        private const int SpawnsPerTeam = 3;
        private static readonly string[] AcornsGladeBottomHalf =
        {
            "..~~...\"\"...\"\"...~~..", // row 14
            ".......\"\"...\"\".......",
            ".....#..%...%..#.....",
            "...###.........###...",
            "..........#..........",
            "\"\"....~~..#..~~....\"\"",
            "\"\"\"...~~.....~~...\"\"\"",
            "\"\"\"...##.....##...\"\"\"",
            ".....................",
            "..#......%%%......#..",
            "..###...........###..",
            ".....................",
            "......A...A...A......",
            "..\"\".............\"\"..",
            "....................." // row 0
        };
        private const string AcornsGladeMiddleRow = "~~...#...\"H\"...#...~~"; // row 15, its own mirror

        // PROTO (D-013): body radii in cells.
        private const float RadiusDefault = 0.45f;
        private const float RadiusLarge = 0.55f;
        private const float RadiusMedium = 0.5f;

        // PROTO (D-013): attack delivery. Pix and Bruno are final for S2.3; the others are placeholders until their
        // own deliveries (lob, bounce, pierce falloff) arrive in S2.11–S2.12.
        private const float PixDropSpeed = 12f;
        private const float PixDropRadius = 0.2f;
        private const int PixBurstCount = 4;
        private const float PixBurstInterval = 0.1f;
        private const float BrunoConeAngle = 90f;
        private const float ChompBombSpeed = 9f;
        private const float ChompBombRadius = 0.3f;
        private const float KoraCupSpeed = 10f;
        private const float KoraCupRadius = 0.25f;
        private const float NovaConeSpeed = 18f;
        private const float NovaConeRadius = 0.15f;
        private const int NovaPierce = 1;
        private const float VexClawAngle = 90f;
        private const int VexClawHits = 2;
        private const float VexClawInterval = 0.15f;

        // Pix full set (GDD §6): ult "Downpour" — fan of 10 drops, 8 cells, 400 each, light knockback; trick "Roll" — dash
        // 3 cells along movement; talent "Tailwind" — +10 % speed while the ult is charged. PROTO: fan angle, knockback,
        // roll speed, ult charge.
        private const int PixUltCharge = 7600;      // PROTO ≈ 5 full bursts hitting (4 × 380 × 5)
        private const int PixUltDrops = 10;
        private const float PixUltSpread = 50f;     // PROTO
        private const int PixUltDamage = 400;
        private const float PixUltPush = 0.5f;      // PROTO "light knockback", cells
        private const float PixUltPushTime = 0.1f;  // PROTO
        private const float PixRollDistance = 3f;
        private const float PixRollSpeed = 15f;     // PROTO: 3 cells in 0.2 s
        private const int PixTalentSpeedPercent = 10;

        // Bruno full set (GDD §6): ult "Ram" — dash 6 cells, 1000, knockback 2 cells, stun 0.5 s, breaks walls; trick
        // "Hard Hat" — −40 % damage taken for 2 s; talent "Stubborn" — +15 % damage below 40 % health. PROTO: speeds, charge.
        private const int BrunoUltCharge = 7200;    // PROTO ≈ 6 swings hitting (6 × 1200)
        private const float BrunoRamDistance = 6f;
        private const float BrunoRamSpeed = 15f;    // PROTO: 6 cells in 0.4 s
        private const int BrunoRamDamage = 1000;
        private const float BrunoRamPush = 2f;
        private const float BrunoRamPushTime = 0.2f; // PROTO
        private const float BrunoRamStun = 0.5f;
        private const int BrunoHelmetPercent = 40;
        private const float BrunoHelmetSeconds = 2f;
        private const int BrunoTalentHealthPercent = 40;
        private const int BrunoTalentDamagePercent = 15;

        // Camera (GDD §5.5: ~55°); the rest PROTO (D-013).
        private const float CameraPitch = 55f;
        private const float CameraDistance = 18f;
        private const float CameraFieldOfView = 50f;
        private const float CameraForwardOffset = 2f;
        private const float CameraEdgeInset = 7f;
        private const float CameraFollowSharpness = 8f;

        // Match runner: technical limit + prototype setup (3×3: pix/bruno/chomp vs kora/nova/vex dummies). PROTO spawns.
        private const int MaxTicksPerFrame = 4;
        private const int MatchSeed = 1;
        private const int LocalSlot = 0;
        private const int TeamSize = 3;
        private const int FighterLevel = 1;
        private static readonly string[] SlotFighterIds = { "pix", "bruno", "chomp", "kora", "nova", "vex" };

        public static string FighterAssetPath(string id) => FightersFolder + "/" + AssetNameFor(id) + ".asset";

        public static void SeedAll()
        {
            ProjectConfigurator.EnsureFolder(CombatFolder);
            ProjectConfigurator.EnsureFolder(FightersFolder);
            ProjectConfigurator.EnsureFolder(FeelFolder);
            ProjectConfigurator.EnsureFolder(MapsFolder);
            ProjectConfigurator.EnsureFolder(ModesFolder);
            ProjectConfigurator.EnsureFolder(LocalizationFolder);

            CombatRulesConfig rules = SeedCombatRules();

            FighterConfig[] fighters =
            {
                //                     id       HP    speed               radius         recharge  attack (range, damage — GDD §6)
                SeedFighter("pix",   3400, SpeedClass.Normal, RadiusDefault, 1.4f, Projectile(8f, PixDropSpeed, PixDropRadius, 0, PixBurstCount, PixBurstInterval, 380)),
                SeedFighter("bruno", 5600, SpeedClass.Normal, RadiusLarge,   1.1f, Cone(2.5f, BrunoConeAngle, 1, 0f, 1200)),
                SeedFighter("chomp", 3000, SpeedClass.Normal, RadiusDefault, 1.6f, Projectile(7f, ChompBombSpeed, ChompBombRadius, 0, 1, 0f, 1100)),
                SeedFighter("kora",  4200, SpeedClass.Slow,   RadiusMedium,  1.5f, Projectile(7f, KoraCupSpeed, KoraCupRadius, 0, 1, 0f, 600)),
                SeedFighter("nova",  2600, SpeedClass.Normal, RadiusDefault, 2.0f, Projectile(10f, NovaConeSpeed, NovaConeRadius, NovaPierce, 1, 0f, 1500)),
                SeedFighter("vex",   3600, SpeedClass.Fast,   RadiusDefault, 0.9f, Cone(1.8f, VexClawAngle, VexClawHits, VexClawInterval, 520))
            };

            SeedPixAbilities(fighters[0]);
            SeedBrunoAbilities(fighters[1]);

            GameDatabase db = LoadOrCreate<GameDatabase>(GameDatabasePath);
            db.CombatRules = rules;
            if (db.Fighters == null) db.Fighters = new System.Collections.Generic.List<FighterConfig>();
            db.Fighters.Clear();
            db.Fighters.AddRange(fighters);
            db.DefaultMap = SeedAcornsGlade();
            db.Camera = SeedCamera();
            db.MatchRunner = SeedMatchRunner();
            db.AcornsMode = SeedAcornsMode();
            db.Strings = SeedStrings();
            EditorUtility.SetDirty(db);

            AssetDatabase.SaveAssets();
            Debug.Log("ARENA_DATA_SEEDED fighters=" + db.Fighters.Count);
        }

        private static CombatRulesConfig SeedCombatRules()
        {
            CombatRulesConfig c = LoadOrCreate<CombatRulesConfig>(CombatRulesPath);
            c.MaxCharges = MaxCharges;
            c.RegenDelaySeconds = RegenDelaySeconds;
            c.RegenFractionPerSecond = RegenFractionPerSecond;
            c.RespawnSeconds = RespawnSeconds;
            c.LevelBonusPerLevel = LevelBonusPerLevel;
            c.UltLossOnDeath = UltLossOnDeath;
            c.TrickUses = TrickUses;
            c.TrickCooldownSeconds = TrickCooldownSeconds;
            c.TrickUnlockLevel = TrickUnlockLevel;
            c.TalentUnlockLevel = TalentUnlockLevel;
            c.BushRevealDistance = BushRevealDistance;
            c.RevealSeconds = RevealSeconds;
            c.SlowSpeed = SlowSpeed;
            c.NormalSpeed = NormalSpeed;
            c.FastSpeed = FastSpeed;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static AcornsModeConfig SeedAcornsMode()
        {
            AcornsModeConfig c = LoadOrCreate<AcornsModeConfig>(AcornsModePath);
            c.SpawnIntervalSeconds = AcornSpawnSeconds;
            c.GoldenIntervalSeconds = AcornGoldenSeconds;
            c.GoldenValue = AcornGoldenValue;
            c.SpawnScatterRadius = AcornSpawnScatter;
            c.TargetAcorns = AcornTarget;
            c.HoldSeconds = AcornHoldSeconds;
            c.DurationSeconds = AcornMatchSeconds;
            c.PickupRadius = AcornPickupRadius;
            c.DropScatterRadius = AcornDropScatter;
            c.PickupDelaySeconds = AcornPickupDelaySeconds;
            c.ScatterAttempts = AcornScatterAttempts;
            c.MvpKillWeight = MvpKillWeight;
            c.MvpDamageWeight = MvpDamageWeight;
            c.MvpHealWeight = MvpHealWeight;
            c.MvpAcornWeight = MvpAcornWeight;
            c.MvpDeathWeight = MvpDeathWeight;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static LocTable SeedStrings()
        {
            LocTable t = LoadOrCreate<LocTable>(StringsPath);
            if (t.Entries == null) t.Entries = new System.Collections.Generic.List<LocEntry>();
            t.Entries.Clear();
            for (int i = 0; i < Strings.Length; i++)
                t.Entries.Add(new LocEntry { Key = Strings[i][0], En = Strings[i][1], Ru = Strings[i][2] });
            EditorUtility.SetDirty(t);
            return t;
        }

        private static CameraConfig SeedCamera()
        {
            CameraConfig c = LoadOrCreate<CameraConfig>(CameraConfigPath);
            c.PitchDegrees = CameraPitch;
            c.Distance = CameraDistance;
            c.FieldOfView = CameraFieldOfView;
            c.ForwardOffset = CameraForwardOffset;
            c.EdgeInset = CameraEdgeInset;
            c.FollowSharpness = CameraFollowSharpness;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static MatchRunnerConfig SeedMatchRunner()
        {
            MatchRunnerConfig c = LoadOrCreate<MatchRunnerConfig>(MatchRunnerConfigPath);
            c.MaxTicksPerFrame = MaxTicksPerFrame;
            c.Seed = MatchSeed;
            c.LocalSlot = LocalSlot;
            if (c.SlotFighterIds == null) c.SlotFighterIds = new System.Collections.Generic.List<string>();
            c.SlotFighterIds.Clear();
            c.SlotFighterIds.AddRange(SlotFighterIds);
            c.TeamSize = TeamSize;
            c.FighterLevel = FighterLevel;
            c.UnlockAllAbilities = UnlockAllAbilities;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static MapConfig SeedAcornsGlade()
        {
            MapConfig c = LoadOrCreate<MapConfig>(AcornsGladePath);
            c.Id = AcornsGladeId;
            c.Rows = MirrorBottomHalf(AcornsGladeBottomHalf, AcornsGladeMiddleRow);
            c.RequiredSpawnsPerTeam = SpawnsPerTeam;
            c.RequireCentralSymmetry = true;
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>
        /// Full rows (top first) from the bottom half (top first) and the middle row: the top half is the bottom half rotated
        /// by 180° with team 0 spawns turned into team 1 spawns.
        /// </summary>
        public static string[] MirrorBottomHalf(string[] bottomHalfTopFirst, string middleRow)
        {
            int half = bottomHalfTopFirst.Length;
            var rows = new string[half * 2 + 1];
            for (int i = 0; i < half; i++)
            {
                // Text row i (top half) mirrors bottom text row (half − 1 − i) counted from the bottom.
                char[] chars = bottomHalfTopFirst[half - 1 - i].ToCharArray();
                System.Array.Reverse(chars);
                for (int k = 0; k < chars.Length; k++)
                    if (chars[k] == Arena.Data.Conversion.MapConverter.Team0SpawnChar) chars[k] = Arena.Data.Conversion.MapConverter.Team1SpawnChar;
                rows[i] = new string(chars);
            }
            rows[half] = middleRow;
            for (int i = 0; i < half; i++) rows[half + 1 + i] = bottomHalfTopFirst[i];
            return rows;
        }

        private static void SeedPixAbilities(FighterConfig c)
        {
            c.UltChargeRequired = PixUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Projectile, Range = 8f, ProjectileSpeed = PixDropSpeed, ProjectileRadius = PixDropRadius,
                ProjectileCount = PixUltDrops, SpreadDegrees = PixUltSpread,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, PixUltDamage),
                    Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, PixUltPushTime, PixUltPush)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.Dash, DashDistance = PixRollDistance, DashSpeed = PixRollSpeed, UsesMoveDirection = true,
                Effects = new EffectConfig[0]
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.UltCharged, Modifier = TalentModifier.MoveSpeedMul, ValuePercent = PixTalentSpeedPercent
            };
            EditorUtility.SetDirty(c);
        }

        private static void SeedBrunoAbilities(FighterConfig c)
        {
            c.UltChargeRequired = BrunoUltCharge;
            c.Ult = new AbilityConfig
            {
                Delivery = DeliveryKind.Dash, Range = BrunoRamDistance, DashDistance = BrunoRamDistance, DashSpeed = BrunoRamSpeed,
                BreaksWalls = true,
                Effects = new[]
                {
                    Effect(EffectKind.Damage, TargetFilter.Enemy, BrunoRamDamage),
                    Effect(EffectKind.Knockback, TargetFilter.Enemy, 0, BrunoRamPushTime, BrunoRamPush),
                    Effect(EffectKind.Stun, TargetFilter.Enemy, 0, BrunoRamStun)
                }
            };
            c.Trick = new AbilityConfig
            {
                Delivery = DeliveryKind.SelfBuff,
                Effects = new[] { Effect(EffectKind.DamageReduction, TargetFilter.Self, BrunoHelmetPercent, BrunoHelmetSeconds) }
            };
            c.Talent = new TalentConfig
            {
                Condition = TalentCondition.HealthBelow, Param = BrunoTalentHealthPercent,
                Modifier = TalentModifier.DamageMul, ValuePercent = BrunoTalentDamagePercent
            };
            EditorUtility.SetDirty(c);
        }

        private static EffectConfig Effect(EffectKind kind, TargetFilter filter, int value, float durationSeconds = 0f, float distance = 0f) =>
            new EffectConfig { Kind = kind, Filter = filter, Value = value, DurationSeconds = durationSeconds, Distance = distance };

        private static AbilityConfig None() => new AbilityConfig { Delivery = DeliveryKind.None, Effects = new EffectConfig[0] };

        private static AbilityConfig Projectile(float range, float speed, float radius, int pierce, int burstCount,
            float burstInterval, int damage)
        {
            return new AbilityConfig
            {
                Delivery = DeliveryKind.Projectile, Range = range, ProjectileSpeed = speed, ProjectileRadius = radius,
                PierceCount = pierce, BurstCount = burstCount, BurstIntervalSeconds = burstInterval, Effects = EnemyDamage(damage)
            };
        }

        private static AbilityConfig Cone(float range, float angle, int burstCount, float burstInterval, int damage)
        {
            return new AbilityConfig
            {
                Delivery = DeliveryKind.MeleeCone, Range = range, ConeAngleDegrees = angle,
                BurstCount = burstCount, BurstIntervalSeconds = burstInterval, Effects = EnemyDamage(damage)
            };
        }

        private static EffectConfig[] EnemyDamage(int damage) =>
            new[] { new EffectConfig { Kind = EffectKind.Damage, Filter = TargetFilter.Enemy, Value = damage } };

        private static FighterConfig SeedFighter(string id, int maxHealth, SpeedClass speed, float radius,
            float attackRechargeSeconds, AbilityConfig attack)
        {
            FighterConfig c = LoadOrCreate<FighterConfig>(FighterAssetPath(id));
            c.Id = id;
            c.NameKey = "fighter." + id + ".name";
            c.MaxHealth = maxHealth;
            c.Speed = speed;
            c.Radius = radius;
            c.AttackRechargeSeconds = attackRechargeSeconds;
            c.Attack = attack;
            // Ult, trick and talent: set per fighter after this (Pix, Bruno); the others get theirs in S2.11–S2.12.
            c.UltChargeRequired = 0;
            c.Ult = None();
            c.Trick = None();
            c.Talent = new TalentConfig();
            EditorUtility.SetDirty(c);
            return c;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static string AssetNameFor(string id) => char.ToUpperInvariant(id[0]) + id.Substring(1);
    }
}
