using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Core.State;
using Arena.Gameplay.Input;
using Arena.Net.Client;
using Arena.Online;
using Arena.Tests.EditMode.Net.Lobby;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Online
{
    /// <summary>
    /// <see cref="OnlineMatchDriver"/> over a real lobby and match server in memory (S3.10b, Server-Spec §12.2): the session from a
    /// lobby search (the rest filled with bots), the driver is stepped like <c>MatchRunner</c> does it. The own fighter moves on the
    /// very tick its input is given (prediction, before the server has it), the others are shown behind (interpolation), the
    /// match ends with the server's result in the display state (the results screen reads it).
    /// </summary>
    public sealed class OnlineMatchDriverTests
    {
        private const float FrameSeconds = 1f / 60f;
        private const float MaxMatchSeconds = 240f;

        [Test]
        public void Driver_PlaysAFullOnlineMatch_OwnFighterPredicted_OthersInterpolated_EndsWithResults()
        {
            var rig = new LobbyRig(seed: 11);
            LobbyTestClient client = rig.AddClient();
            client.Paused = true; // pumped below, through the driver
            client.Lobby.Search(client.Socket, ModeKind.Acorns, 0, rig.Now);
            var input = new LocalInputCollector();
            OnlineMatchDriver driver = null;
            bool movedAtOnce = false, checkedMove = false, othersBehind = false;
            Vec2 spawnOther = default;
            int otherSlot = -1;

            long end = rig.Now + (long)(MaxMatchSeconds * 1000000.0);
            while (rig.Now < end && (driver == null || !driver.IsEnded))
            {
                rig.Step();
                long now = rig.Now;
                client.Net.Update(now);
                client.Lobby.Update(now);
                MatchClientSession session = client.Session;
                if (driver == null)
                {
                    if (!session.HasStart) continue;
                    driver = new OnlineMatchDriver(session, false, 0f, () => rig.Now);
                    otherSlot = driver.LocalSlot == 0 ? 1 : 0;
                    spawnOther = driver.Setup.Slots[otherSlot].SpawnPosition;
                    Assert.AreEqual(session.Presentation.State, driver.State, "the display state is shown");
                    Assert.Greater(driver.State.Globals.MapWidth, 0, "the initial state has the map before the first tick");
                }

                bool running = session.PredictionTick > 60;
                if (running && !checkedMove)
                {
                    ref FighterState own = ref driver.State.Fighters[driver.LocalSlot];
                    Vec2 toCenter = driver.Defs.Map.Center - own.Position;
                    input.SetMove(InputChannel.Touch, input.SimToView(toCenter.Normalized));
                }

                int ticks = driver.BeginFrame(FrameSeconds);
                for (int i = 0; i < ticks; i++)
                {
                    Vec2 before = driver.State.Fighters[driver.LocalSlot].Position;
                    long built = driver.BuiltTicks;
                    int serverTick = session.NewestServerTick;
                    bool moving = !input.Move.IsZero;
                    driver.Step(input);
                    if (driver.BuiltTicks == built) Assert.AreEqual(0, driver.Events.Count, "no display tick — no events");
                    if (moving && !checkedMove && driver.BuiltTicks > built)
                    {
                        checkedMove = true;
                        Vec2 after = driver.State.Fighters[driver.LocalSlot].Position;
                        movedAtOnce = (after - before).Length > 0f;
                        Assert.GreaterOrEqual(session.PredictionTick - 1, serverTick, "the tick was predicted before the server simulated it");
                    }
                }
                if (running && driver.State.Globals.Tick - session.Presentation.RenderTick > 0) othersBehind = true;
            }

            Assert.IsNotNull(driver, "MatchStart came");
            Assert.IsTrue(checkedMove, "the own fighter was moved");
            Assert.IsTrue(movedAtOnce, "the own fighter moves on the tick of its input (prediction)");
            Assert.IsTrue(othersBehind, "the others are shown at the render tick, behind the own fighter");
            Vec2 otherNow = driver.State.Fighters[otherSlot].Position;
            Assert.Greater((otherNow - spawnOther).Length + driver.State.Stats[otherSlot].Deaths, 0f, "another fighter (bot) played");
            Assert.IsTrue(driver.IsEnded, "the match ended in " + MaxMatchSeconds + " s");
            Assert.AreEqual(ModePhase.Ended, driver.State.Mode.Phase, "the result is in the display state");
            Assert.Greater(driver.BuiltTicks, 1000);
        }
    }
}
