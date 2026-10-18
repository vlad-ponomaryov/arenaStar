using Arena.Core.Defs;
using Arena.Data;
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
        public const string CameraConfigPath = FeelFolder + "/CameraConfig.asset";
        public const string MatchRunnerConfigPath = CombatFolder + "/MatchRunnerConfig.asset";
        public const string CombatRulesPath = CombatFolder + "/CombatRules.asset";
        public const string GameDatabasePath = DataRoot + "/GameDatabase.asset";

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

        // Temporary arena = size of the Acorns map (GDD §5.4), until grid maps (S2.5).
        private static readonly Vector2 TempArenaSize = new Vector2(21f, 31f);

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
        private const float SpawnEdgeInset = 3f;
        private const float SpawnSpacing = 4f;
        private static readonly string[] SlotFighterIds = { "pix", "bruno", "chomp", "kora", "nova", "vex" };

        public static string FighterAssetPath(string id) => FightersFolder + "/" + AssetNameFor(id) + ".asset";

        public static void SeedAll()
        {
            ProjectConfigurator.EnsureFolder(CombatFolder);
            ProjectConfigurator.EnsureFolder(FightersFolder);
            ProjectConfigurator.EnsureFolder(FeelFolder);

            CombatRulesConfig rules = SeedCombatRules();

            var fighters = new[]
            {
                //                     id       HP    speed               radius         recharge  attack (range, damage — GDD §6)
                SeedFighter("pix",   3400, SpeedClass.Normal, RadiusDefault, 1.4f, Projectile(8f, PixDropSpeed, PixDropRadius, 0, PixBurstCount, PixBurstInterval, 380)),
                SeedFighter("bruno", 5600, SpeedClass.Normal, RadiusLarge,   1.1f, Cone(2.5f, BrunoConeAngle, 1, 0f, 1200)),
                SeedFighter("chomp", 3000, SpeedClass.Normal, RadiusDefault, 1.6f, Projectile(7f, ChompBombSpeed, ChompBombRadius, 0, 1, 0f, 1100)),
                SeedFighter("kora",  4200, SpeedClass.Slow,   RadiusMedium,  1.5f, Projectile(7f, KoraCupSpeed, KoraCupRadius, 0, 1, 0f, 600)),
                SeedFighter("nova",  2600, SpeedClass.Normal, RadiusDefault, 2.0f, Projectile(10f, NovaConeSpeed, NovaConeRadius, NovaPierce, 1, 0f, 1500)),
                SeedFighter("vex",   3600, SpeedClass.Fast,   RadiusDefault, 0.9f, Cone(1.8f, VexClawAngle, VexClawHits, VexClawInterval, 520))
            };

            GameDatabase db = LoadOrCreate<GameDatabase>(GameDatabasePath);
            db.CombatRules = rules;
            if (db.Fighters == null) db.Fighters = new System.Collections.Generic.List<FighterConfig>();
            db.Fighters.Clear();
            db.Fighters.AddRange(fighters);
            db.TempArenaSize = TempArenaSize;
            db.Camera = SeedCamera();
            db.MatchRunner = SeedMatchRunner();
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
            c.SlowSpeed = SlowSpeed;
            c.NormalSpeed = NormalSpeed;
            c.FastSpeed = FastSpeed;
            EditorUtility.SetDirty(c);
            return c;
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
            c.SpawnEdgeInset = SpawnEdgeInset;
            c.SpawnSpacing = SpawnSpacing;
            EditorUtility.SetDirty(c);
            return c;
        }

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
