using Arena.Core.Input;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.State;
using Arena.Net.Client;
using Arena.Net.Server.Match;
using Arena.Tests.EditMode.Net.Server;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Client
{
    /// <summary>
    /// Lag compensation end to end (Server-Spec §2.8, S3.13): two real clients over the virtual network in CI conditions
    /// (RTT 150 ms, jitter 50 ms, GDD §8.3), Bruno against Pix in the open bottom lane of the Glade. Pix walks away; Bruno
    /// swings the paw the moment the shown Pix is still within reach. With compensation the server rewinds Pix to where Bruno
    /// saw him — a hit; without it Pix is already out of reach — a miss.
    /// </summary>
    public sealed class LagCompensationMatchTests
    {
        private const float LaneY = 2.5f;      // row 2 of the Glade: open floor from column 5 to 15
        private const float BrunoX = 4.5f;
        private const float PixStartX = 6f;
        private const float PixStopX = 14f;
        private const float SwingDistance = 2.4f; // shown distance of the swing: reach is 2.5 + Pix's 0.45

        private sealed class Duel
        {
            public MatchClientRig Rig;
            public bool Go;
            public bool Fired;
            public int FireTick = -1;
            public float ShownDistance;
        }

        private static int FighterIndex(string id)
        {
            var fighters = Arena.Tests.EditMode.Net.Snapshots.SnapshotMatchRig.LoadBundle().Fighters;
            for (int i = 0; i < fighters.Length; i++)
                if (fighters[i].Id == id) return i;
            Assert.Fail("no fighter " + id);
            return -1;
        }

        private static Duel Play(bool lagComp, int seed)
        {
            ServerSettings settings = MatchServerRig.CreateSettings();
            settings.LagCompEnabled = lagComp;
            int bruno = FighterIndex("bruno"), pix = FighterIndex("pix");
            var duel = new Duel();
            MatchClientRig rig = duel.Rig = new MatchClientRig(ModeKind.Acorns, new[] { 0, 1 }, seed: seed, conditions: MatchServerRig.CiConditions(),
                fighters: new[] { bruno, pix }, serverSettings: settings, slotCount: 2, teamSize: 1);
            MatchClientSession attacker = rig.Sessions[0];
            MatchClientSession target = rig.Sessions[1];

            rig.Scripts[1] = tick =>
            {
                if (!duel.Go || !target.Presentation.HasState) return default;
                return target.Presentation.State.Fighters[1].Position.X < PixStopX
                    ? new PlayerInput(Vec2.Right, Vec2.Zero, InputButtons.None)
                    : default;
            };
            rig.Scripts[0] = tick =>
            {
                if (!duel.Go || duel.Fired || !attacker.Presentation.HasState) return default;
                MatchState shown = attacker.Presentation.State;
                Vec2 to = shown.Fighters[1].Position - shown.Fighters[0].Position;
                if (to.Length < SwingDistance) return default;
                duel.Fired = true;
                duel.FireTick = tick;
                duel.ShownDistance = to.Length;
                return new PlayerInput(Vec2.Zero, to.Normalized, InputButtons.Attack);
            };

            rig.ConnectAll();
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Phase == MatchPhase.Running && rig.Room.Tick > 2, 20f), "the match starts");
            MatchState server = rig.Room.Simulation.State;
            server.Fighters[0].Position = new Vec2(BrunoX, LaneY);
            server.Fighters[1].Position = new Vec2(PixStartX, LaneY);
            rig.RunSeconds(2f); // both clients settle on the new positions
            Assert.AreEqual(server.Fighters[1].MaxHealth, server.Fighters[1].Health);
            duel.Go = true;
            Assert.IsTrue(rig.RunUntil(() => duel.Fired, 5f), "Bruno swings");
            Assert.IsTrue(rig.RunUntil(() => rig.Room.Tick > duel.FireTick + 15, 5f), "the swing is simulated");
            return duel;
        }

        /// <summary>The simulated input with Bruno's press (on its tick, or a few ticks later if it was carried).</summary>
        private static PlayerInput PressOf(Duel duel)
        {
            InputLog log = duel.Rig.Room.InputLog;
            for (int t = duel.FireTick; t < duel.FireTick + 8 && t < log.Ticks; t++)
                if (log.Get(t, 0).Has(InputButtons.Attack)) return log.Get(t, 0);
            Assert.Fail("the press never reached the simulation");
            return default;
        }

        [Test]
        public void Paw_AtRtt150Jitter50_HitsWithCompensation_MissesWithout()
        {
            Duel with = Play(true, 41);
            MatchState s = with.Rig.Room.Simulation.State;
            PlayerInput logged = PressOf(with);
            Assert.Greater(logged.ViewDelayTicks, 6, "the client saw Pix ≈ 300 ms behind");
            Assert.LessOrEqual(logged.ViewDelayTicks, with.Rig.Host.Settings.LagCompMaxRewindTicks, "limited to 350 ms");
            Assert.Less(s.Fighters[1].Health, s.Fighters[1].MaxHealth,
                "with compensation the paw hits (shown distance " + with.ShownDistance + ", delay " + logged.ViewDelayTicks + ")");
            TestContext.WriteLine("with compensation: shown distance " + with.ShownDistance + ", ViewDelayTicks " + logged.ViewDelayTicks
                + ", Pix health " + s.Fighters[1].Health + "/" + s.Fighters[1].MaxHealth);
            // No replay check here: the test moved both fighters by hand (LagCompensationServerTests replays a rewound match).

            Duel without = Play(false, 41);
            MatchState n = without.Rig.Room.Simulation.State;
            Assert.AreEqual(0, PressOf(without).ViewDelayTicks, "compensation off: no rewind");
            Assert.AreEqual(n.Fighters[1].MaxHealth, n.Fighters[1].Health,
                "without compensation Pix is already out of reach (shown distance " + without.ShownDistance + ")");
        }
    }
}
