using System.Text;
using Arena.Core.Balance;
using Arena.Core.Defs;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.Data
{
    /// <summary>
    /// Balance targets of GDD §6 on the seeded fighters (S2.12, D-061): time to defeat each fighter with continuous hits
    /// (target 3–4 s; soft check 2–6 s on the average over all defenders, every pair must end) and ults charged in 30 s of
    /// active fight (target 1–2; soft check 0.8–3). Kora (support, GDD numbers) is outside the kill-time range — Q-011.
    /// </summary>
    public sealed class BalanceProbeTests
    {
        private const float SoftKillMin = 2f;
        private const float SoftKillMax = 6f;
        private const float SoftUltMin = 0.8f;
        private const float SoftUltMax = 3f;

        /// <summary>Fighters whose kill time is outside the soft range by GDD numbers (support role) — Q-011.</summary>
        private static readonly string[] KillTimeExceptions = { "kora" };

        private static BalanceProbeSettings Settings() => new BalanceProbeSettings
        {
            MapWidth = 15, MapHeight = 21, DistanceFraction = 0.7f, MaxKillSeconds = 30f,
            FightSeconds = 30f, HitFraction = 0.5f, DummyHealth = 10000000
        };

        private static MatchDefs LoadDefs()
        {
            DataSeeder.SeedAll();
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DataSeeder.GameDatabasePath);
            return ConfigConverter.ToMatchDefs(db);
        }

        [Test]
        public void KillTimes_EveryPairEnds_AverageInSoftRange()
        {
            MatchDefs defs = LoadDefs();
            BalanceProbeSettings s = Settings();
            int n = defs.Fighters.Length;
            var table = new StringBuilder("Kill time, s (row = attacker, column = defender):\n");
            var failures = new StringBuilder();
            for (int a = 0; a < n; a++)
            {
                float sum = 0f;
                table.Append(defs.Fighters[a].Id).Append(':');
                for (int d = 0; d < n; d++)
                {
                    float seconds = BalanceProbe.KillSeconds(defs, a, d, s);
                    Assert.IsFalse(float.IsInfinity(seconds), defs.Fighters[a].Id + " never defeats " + defs.Fighters[d].Id);
                    sum += seconds;
                    table.Append(' ').Append(defs.Fighters[d].Id).Append(' ').Append(seconds.ToString("0.00"));
                }
                float average = sum / n;
                table.Append(" | average ").Append(average.ToString("0.00")).Append('\n');
                bool exception = System.Array.IndexOf(KillTimeExceptions, defs.Fighters[a].Id) >= 0;
                if (!exception && (average < SoftKillMin || average > SoftKillMax))
                    failures.Append(defs.Fighters[a].Id).Append(" average ").Append(average.ToString("0.00")).Append("; ");
                if (exception) Assert.Greater(average, SoftKillMax, defs.Fighters[a].Id + " is back in range: drop the exception and Q-011");
            }
            TestContext.WriteLine(table.ToString());
            UnityEngine.Debug.Log(table.ToString());
            Assert.AreEqual(string.Empty, failures.ToString(), "average kill time outside " + SoftKillMin + "–" + SoftKillMax + " s");
        }

        /// <summary>Melee fighters whose stored charges must not defeat anyone almost at once (D-149: Kovach's burst).</summary>
        private static readonly string[] HeavySwingFighters = { "bruno" };
        private const float MinBurstKillSeconds = 1.7f;

        /// <summary>
        /// Burst kill time (D-149): the attacker starts with all its charges and presses whenever it can. Logs the whole table;
        /// the heavy swingers need at least <see cref="MinBurstKillSeconds"/> against every fighter.
        /// </summary>
        [Test]
        public void BurstKillTimes_HeavySwingersAreNotInstant()
        {
            MatchDefs defs = LoadDefs();
            BalanceProbeSettings s = Settings();
            s.FullCharges = true;
            int n = defs.Fighters.Length;
            var table = new StringBuilder("Burst kill time, s (full charges; row = attacker, column = defender):\n");
            var failures = new StringBuilder();
            for (int a = 0; a < n; a++)
            {
                bool heavy = System.Array.IndexOf(HeavySwingFighters, defs.Fighters[a].Id) >= 0;
                table.Append(defs.Fighters[a].Id).Append(':');
                for (int d = 0; d < n; d++)
                {
                    float seconds = BalanceProbe.KillSeconds(defs, a, d, s);
                    table.Append(' ').Append(defs.Fighters[d].Id).Append(' ').Append(seconds.ToString("0.00"));
                    if (heavy && seconds < MinBurstKillSeconds)
                        failures.Append(defs.Fighters[a].Id).Append('→').Append(defs.Fighters[d].Id).Append(' ').Append(seconds.ToString("0.00")).Append("; ");
                }
                table.Append('\n');
            }
            TestContext.WriteLine(table.ToString());
            UnityEngine.Debug.Log(table.ToString());
            Assert.AreEqual(string.Empty, failures.ToString(), "burst kill faster than " + MinBurstKillSeconds + " s");
        }

        [Test]
        public void UltCharges_In30SecondsOfActiveFight_InSoftRange()
        {
            MatchDefs defs = LoadDefs();
            BalanceProbeSettings s = Settings();
            var table = new StringBuilder("Ults per 30 s of active fight (50 % hits):");
            var failures = new StringBuilder();
            for (int a = 0; a < defs.Fighters.Length; a++)
            {
                float ults = BalanceProbe.UltsPerFight(defs, a, s);
                table.Append(' ').Append(defs.Fighters[a].Id).Append(' ').Append(ults.ToString("0.00"));
                if (ults < SoftUltMin || ults > SoftUltMax) failures.Append(defs.Fighters[a].Id).Append(' ').Append(ults.ToString("0.00")).Append("; ");
            }
            TestContext.WriteLine(table.ToString());
            UnityEngine.Debug.Log(table.ToString());
            Assert.AreEqual(string.Empty, failures.ToString(), "ults per fight outside " + SoftUltMin + "–" + SoftUltMax);
        }

        [Test]
        public void Probe_IsDeterministic()
        {
            MatchDefs defs = LoadDefs();
            BalanceProbeSettings s = Settings();
            int pix = defs.IndexOfFighter("pix");
            int bruno = defs.IndexOfFighter("bruno");
            Assert.AreEqual(BalanceProbe.KillSeconds(defs, pix, bruno, s), BalanceProbe.KillSeconds(defs, pix, bruno, s));
        }
    }
}
