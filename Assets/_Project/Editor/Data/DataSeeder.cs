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

        // Temporary arena = size of the Acorns map (GDD §5.4), until grid maps (S2.5).
        private static readonly Vector2 TempArenaSize = new Vector2(21f, 31f);

        // PROTO (D-013): body radii in cells.
        private const float RadiusDefault = 0.45f;
        private const float RadiusLarge = 0.55f;
        private const float RadiusMedium = 0.5f;

        public static string FighterAssetPath(string id) => FightersFolder + "/" + AssetNameFor(id) + ".asset";

        public static void SeedAll()
        {
            ProjectConfigurator.EnsureFolder(CombatFolder);
            ProjectConfigurator.EnsureFolder(FightersFolder);

            CombatRulesConfig rules = SeedCombatRules();

            var fighters = new[]
            {
                //                     id       HP    speed               radius          range  recharge  damage
                SeedFighter("pix",   3400, SpeedClass.Normal, RadiusDefault, 8f,    1.4f,     380),
                SeedFighter("bruno", 5600, SpeedClass.Normal, RadiusLarge,   2.5f,  1.1f,     1200),
                SeedFighter("chomp", 3000, SpeedClass.Normal, RadiusDefault, 7f,    1.6f,     1100),
                SeedFighter("kora",  4200, SpeedClass.Slow,   RadiusMedium,  7f,    1.5f,     600),
                SeedFighter("nova",  2600, SpeedClass.Normal, RadiusDefault, 10f,   2.0f,     1500),
                SeedFighter("vex",   3600, SpeedClass.Fast,   RadiusDefault, 1.8f,  0.9f,     520)
            };

            GameDatabase db = LoadOrCreate<GameDatabase>(GameDatabasePath);
            db.CombatRules = rules;
            if (db.Fighters == null) db.Fighters = new System.Collections.Generic.List<FighterConfig>();
            db.Fighters.Clear();
            db.Fighters.AddRange(fighters);
            db.TempArenaSize = TempArenaSize;
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
            c.LevelBonusPerLevel = LevelBonusPerLevel;
            c.SlowSpeed = SlowSpeed;
            c.NormalSpeed = NormalSpeed;
            c.FastSpeed = FastSpeed;
            EditorUtility.SetDirty(c);
            return c;
        }

        private static FighterConfig SeedFighter(string id, int maxHealth, SpeedClass speed, float radius, float attackRange,
            float attackRechargeSeconds, int attackDamage)
        {
            FighterConfig c = LoadOrCreate<FighterConfig>(FighterAssetPath(id));
            c.Id = id;
            c.NameKey = "fighter." + id + ".name";
            c.MaxHealth = maxHealth;
            c.Speed = speed;
            c.Radius = radius;
            c.AttackRange = attackRange;
            c.AttackRechargeSeconds = attackRechargeSeconds;
            c.AttackDamage = attackDamage;
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
