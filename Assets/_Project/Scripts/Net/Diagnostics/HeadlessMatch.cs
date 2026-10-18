using System;
using System.Diagnostics;
using Arena.Bots;
using Arena.Bots.Defs;
using Arena.Core.Defs;
using Arena.Core.Input;
using Arena.Core.Map;
using Arena.Core.Sim;
using Arena.Net.Content;

namespace Arena.Net.Diagnostics
{
    /// <summary>
    /// A whole match of bots without views or network (Server-Spec §11, S3.2): the server self-test and EditMode tests run
    /// it on the content pack to measure ms/tick and to compare final <see cref="StateHash"/>es of two runs or two sources
    /// of defs. The tick loop is the offline one: zero inputs → bots write theirs → <c>MatchSimulation.Tick</c>.
    /// Creating the match allocates; the loop itself does not.
    /// </summary>
    public static class HeadlessMatch
    {
        /// <summary>
        /// Setup with every spawn of the map filled (team size = spawns of team 0; a free-for-all map — every shared spawn up to
        /// <see cref="SimLimits.MaxFighters"/>, one team per slot), fighter of slot i = i mod fighter count, level and unlock
        /// flag from the pack's policy.
        /// </summary>
        public static MatchSetup CreateBotSetup(MatchDefs defs, ContentMatchPolicy policy, int seed)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            int shared = defs.Map.SpawnCount(MapSpawn.SharedTeam);
            if (shared > 0)
            {
                int n = shared < SimLimits.MaxFighters ? shared : SimLimits.MaxFighters;
                var ffa = new int[n];
                for (int i = 0; i < n; i++) ffa[i] = i % defs.Fighters.Length;
                return MatchSetupBuilder.CreateFreeForAll(defs, policy, ffa, seed);
            }
            int teamSize = defs.Map.SpawnCount(0);
            int slots = teamSize + defs.Map.SpawnCount(1);
            if (teamSize == 0 || slots > SimLimits.MaxFighters) throw new ArgumentException("HeadlessMatch: map '" + defs.Map.Id + "' spawns do not fit");
            var fighters = new int[slots];
            for (int i = 0; i < slots; i++) fighters[i] = i % defs.Fighters.Length;
            return MatchSetupBuilder.Create(defs, policy, fighters, teamSize, seed);
        }

        /// <summary>Runs until the mode ends or <paramref name="maxTicks"/>; every slot is a bot of <paramref name="difficulty"/>.</summary>
        public static HeadlessMatchResult Run(MatchDefs defs, MatchSetup setup, BotBehaviourDef behaviour, BotDifficultyDef difficulty, int maxTicks)
        {
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            var sim = new MatchSimulation(defs, setup);
            var bots = new BotController(behaviour);
            for (int i = 0; i < setup.SlotCount; i++) bots.AddBot(i, difficulty, setup.Seed);
            var inputs = new PlayerInput[SimLimits.MaxFighters];

            var result = new HeadlessMatchResult();
            long total = 0, max = 0;
            while (!sim.IsEnded && result.Ticks < maxTicks)
            {
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < inputs.Length; i++) inputs[i] = default;
                bots.Update(sim.State, sim.Defs, sim.Events, inputs);
                sim.Tick(inputs);
                long spent = Stopwatch.GetTimestamp() - start;
                total += spent;
                if (spent > max) max = spent;
                result.Ticks++;
            }
            result.Ended = sim.IsEnded;
            result.WinnerTeam = sim.State.Mode.WinnerTeam;
            for (int i = 0; i < setup.SlotCount; i++) result.TotalDamage += sim.State.Stats[i].DamageDealt;
            result.StateHash = StateHash.Compute(sim.State);
            double msPerTimestamp = 1000.0 / Stopwatch.Frequency;
            result.AverageTickMs = result.Ticks == 0 ? 0.0 : total * msPerTimestamp / result.Ticks;
            result.MaxTickMs = max * msPerTimestamp;
            return result;
        }
    }
}
