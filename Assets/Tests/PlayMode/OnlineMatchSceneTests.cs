using System;
using System.Collections;
using System.Net;
using Arena.Bots;
using Arena.Core.Math;
using Arena.Core.Modes;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Input;
using Arena.Gameplay.Match;
using Arena.Net.Client;
using Arena.Net.Content;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// The Match scene online against a server in the same process over real UDP on 127.0.0.1 (S3.10b, Server-Spec §11.2): Boot
    /// creates the <see cref="OnlineSession"/>, a search fills the match with bots after a second, <c>MatchStart</c> loads the Match
    /// scene, the bootstrap gives the runner the <see cref="OnlineMatchDriver"/>; the match runs 10 s without a logged error;
    /// the own fighter moves on the tick of its input (prediction), the others are shown behind on the render clock
    /// (interpolation) and the bots move. Leaving to the menu closes the connection.
    /// </summary>
    public sealed class OnlineMatchSceneTests
    {
        private const float StepTimeoutSeconds = 20f;
        private const float RunSeconds = 10f;
        private const float BotFillSeconds = 1f;
        private const int RunningTick = 15;
        private const float MinWalkCells = 1f;

        private InProcessServerPump _pump;
        private PortableUdpServerSocket _serverSocket;

        [TearDown]
        public void TearDown()
        {
            if (_pump != null) Object.Destroy(_pump.gameObject);
            _pump = null;
            if (_serverSocket != null) _serverSocket.Close();
            _serverSocket = null;
            if (OnlineSession.Instance != null) Object.Destroy(OnlineSession.Instance.gameObject);
            SceneFlow.ClearLaunchParams();
        }

        [UnityTest]
        public IEnumerator OnlineMatch_AgainstInProcessServer_RunsTenSeconds_PredictedAndInterpolated()
        {
            SceneFlow.ClearLaunchParams();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);
            yield return WaitFor(() => OnlineSession.Instance != null && SceneManager.GetActiveScene().name == SceneNames.Menu,
                "Boot loads the menu");
            OnlineSession online = OnlineSession.Instance;
            Assert.IsTrue(online.IsAvailable, "content pack and NetConfig loaded");

            ContentBundle bundle = ContentPackReader.Read(online.Config.ContentPack.bytes, true, out ContentPackHeader header);
            Assert.AreEqual(online.ContentHash, header.ContentHash);
            ServerSettings settings = ServerSettings.CreateDefault();
            settings.BotFillSeconds = BotFillSeconds;
            _serverSocket = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var node = new ServerNode(_serverSocket, TransportSettings.CreateDefault(), settings, bundle, header.ContentHash,
                new CryptoNetRandom());
            _pump = new GameObject("InProcessServer").AddComponent<InProcessServerPump>();
            _pump.Node = node;

            online.SearchAt(new IPEndPoint(IPAddress.Loopback, _serverSocket.LocalEndPoint.Port), ModeKind.Acorns, "pix", BotDifficulty.Novice);
            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindOnlineRunner()) != null, "online match scene started");
            var driver = (OnlineMatchDriver)runner.Driver;
            MatchClientSession session = driver.Session;
            Assert.AreEqual(OnlinePhase.InMatch, online.Phase);
            Assert.IsNull(runner.Simulation, "no local simulation online");
            Assert.AreEqual("pix", driver.Defs.Fighters[driver.State.Fighters[driver.LocalSlot].DefIndex].Id);

            yield return WaitFor(() => session.PredictionTick > RunningTick, "countdown over, the match runs");
            float end = Time.realtimeSinceStartup + RunSeconds;

            // The HUD would overwrite the touch channel from its (idle) sticks every frame.
            Object.FindFirstObjectByType<MatchHud>().enabled = false;
            Vec2 start = driver.State.Fighters[driver.LocalSlot].Position;
            Vec2 toCenter = (driver.Defs.Map.Center - start).Normalized;
            long builtBefore = driver.BuiltTicks;
            runner.InputCollector.SetMove(InputChannel.Touch, runner.InputCollector.SimToView(toCenter));
            yield return WaitFor(() => driver.BuiltTicks > builtBefore, "a display tick with the move");
            Vec2 firstStep = driver.State.Fighters[driver.LocalSlot].Position;
            Assert.Greater((firstStep - start).Length, 0f, "the own fighter moved on the first ticks of its input (prediction)");

            bool othersBehind = false;
            float walked = 0f; // the farthest point: a defeat respawns the fighter at its start (seen once in S3.12)
            while (Time.realtimeSinceStartup < end)
            {
                if (driver.State.Globals.Tick - session.Presentation.RenderTick > 0) othersBehind = true;
                float d = (driver.State.Fighters[driver.LocalSlot].Position - start).Length;
                if (d > walked) walked = d;
                yield return null;
            }
            runner.InputCollector.SetMove(InputChannel.Touch, Vec2.Zero);

            Assert.IsTrue(runner.enabled, "the runner did not stop on an exception");
            Assert.IsTrue(online.Net.IsConnected, "still connected");
            Assert.Greater(driver.BuiltTicks, (long)(RunSeconds * 30f * 0.5f), "display ticks at ~30 Hz");
            Assert.Greater(walked, MinWalkCells, "the own fighter walked");
            Assert.IsTrue(othersBehind, "the others are shown at the render tick, behind the own fighter");
            Assert.IsTrue(session.Interpolation.Started);
            Assert.AreEqual(0, session.MalformedSnapshots);
            int moved = 0;
            for (int i = 0; i < driver.Setup.SlotCount; i++)
            {
                if (i == driver.LocalSlot) continue;
                if ((driver.State.Fighters[i].Position - driver.Setup.Slots[i].SpawnPosition).Length > MinWalkCells
                    || driver.State.Stats[i].Deaths > 0) moved++;
            }
            Assert.Greater(moved, 0, "the server's bots move on the screen");

            SceneFlow.ToMenu();
            yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneNames.Menu, "back in the menu");
            Assert.AreEqual(OnlinePhase.Idle, online.Phase, "leaving the match closes the connection");
            Assert.IsNull(online.Net);
        }

        private static MatchRunner FindOnlineRunner()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Match) return null;
            MatchRunner r = Object.FindFirstObjectByType<MatchRunner>();
            return r != null && r.Driver is OnlineMatchDriver ? r : null;
        }

        private static IEnumerator WaitFor(Func<bool> condition, string what)
        {
            float end = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end) Assert.Fail("timed out: " + what);
                yield return null;
            }
        }
    }
}
