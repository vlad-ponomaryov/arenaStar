using System;
using System.Collections;
using System.Net;
using Arena.Core.Modes;
using Arena.Gameplay.Flow;
using Arena.Gameplay.Match;
using Arena.Gameplay.Settings;
using Arena.Net.Content;
using Arena.Net.Protocol;
using Arena.Net.Server.Lobby;
using Arena.Net.Server.Match;
using Arena.Net.Server.Transport;
using Arena.Net.Transport;
using Arena.Online;
using Arena.UI.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Arena.Tests.PlayMode
{
    /// <summary>
    /// "Back to the match" from the menu (S3.12, Server-Spec §7): a saved ticket of a running match on the server in the process
    /// (the app was killed mid-match) shows the button; it loads the Match scene online in the ticket's slot; leaving forgets the
    /// ticket. A ticket of a match the server no longer has ends with "the match is already over" and is forgotten too.
    /// </summary>
    public sealed class RejoinFlowTests
    {
        private const float StepTimeoutSeconds = 30f;
        private const int Seed = 5;
        private const int OwnSlot = 0;

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
            new RejoinStore(new PlayerPrefsKeyValueStore(), 0f).Clear();
            SceneFlow.ClearLaunchParams();
        }

        [UnityTest]
        public IEnumerator SavedTicket_BackToTheMatch_FromTheMenu_ThenMatchOver()
        {
            SceneFlow.ClearLaunchParams();
            new RejoinStore(new PlayerPrefsKeyValueStore(), 0f).Clear();
            yield return SceneManager.LoadSceneAsync(SceneNames.Boot, LoadSceneMode.Single);
            MenuScreen menu = null;
            yield return WaitFor(() => OnlineSession.Instance != null && (menu = FindMenu()) != null, "Boot loads the menu");
            OnlineSession online = OnlineSession.Instance;
            Assert.IsTrue(online.IsAvailable);
            yield return null;
            Assert.IsFalse(menu.RejoinButton.gameObject.activeSelf, "no ticket: no button");

            ContentBundle bundle = ContentPackReader.Read(online.Config.ContentPack.bytes, true, out ContentPackHeader header);
            _serverSocket = new PortableUdpServerSocket(new IPEndPoint(IPAddress.Loopback, 0));
            var node = new ServerNode(_serverSocket, TransportSettings.CreateDefault(), ServerSettings.CreateDefault(), bundle,
                header.ContentHash, new CryptoNetRandom());
            _pump = new GameObject("InProcessServer").AddComponent<InProcessServerPump>();
            _pump.Node = node;
            var server = new IPEndPoint(IPAddress.Loopback, _serverSocket.LocalEndPoint.Port);

            // A running match whose player slot 0 lost its app: only the ticket on the device is left.
            int mapIndex = bundle.MapsFor(ModeKind.Acorns).MapIndices[0];
            var fighters = new[] { 0, -1, -1, -1, -1, -1 };
            MatchRoom room = node.Matches.CreateMatch(MatchRoomRequest.FromBundle(bundle, mapIndex, ModeKind.Acorns, fighters, Seed),
                NetClock.NowMicros());
            SaveTicket(online, server, room.MatchId, room.GetSlot(OwnSlot).Token);

            yield return null;
            Assert.IsTrue(online.CanRejoin);
            Assert.IsTrue(menu.RejoinButton.gameObject.activeSelf, "\"Back to the match\" is shown");
            menu.RejoinButton.onClick.Invoke();
            Assert.IsTrue(online.IsRejoining);
            MatchRunner runner = null;
            yield return WaitFor(() => (runner = FindOnlineRunner()) != null, "the Match scene online");
            var driver = (OnlineMatchDriver)runner.Driver;
            Assert.AreEqual(OwnSlot, driver.LocalSlot);
            Assert.AreEqual(room.MatchId, driver.Session.Start.MatchId);
            yield return WaitFor(() => driver.Session.Latest != null && driver.BuiltTicks > 30, "snapshots and display ticks");
            Assert.IsTrue(room.GetSlot(OwnSlot).IsConnected, "the server bound the return to the slot");
            Assert.IsTrue(runner.enabled);

            SceneFlow.ToMenu();
            yield return WaitFor(() => (menu = FindMenu()) != null, "back in the menu");
            yield return null;
            Assert.IsFalse(online.CanRejoin, "leaving forgets the ticket");
            Assert.IsFalse(menu.RejoinButton.gameObject.activeSelf);

            // A ticket the server does not know (the room closed long ago): "the match is already over".
            SaveTicket(online, server, 0x1234ul, new NetToken(0x55ul, 0x66ul));
            yield return null;
            Assert.IsTrue(menu.RejoinButton.gameObject.activeSelf);
            menu.RejoinButton.onClick.Invoke();
            yield return WaitFor(() => online.Phase == OnlinePhase.Failed, "the server refuses the return");
            Assert.AreEqual(OnlineFailure.MatchOver, online.Failure);
            yield return null;
            Assert.AreEqual(MenuScreen.FailureKey(OnlineFailure.MatchOver), Arena.Data.Localization.LocKeys.ErrorMatchOver);
            Assert.IsFalse(string.IsNullOrEmpty(menu.OnlineStatus.text), "the error is shown");
            Assert.IsFalse(online.CanRejoin, "the dead ticket is forgotten");
            Assert.IsFalse(menu.RejoinButton.gameObject.activeSelf);
        }

        private static void SaveTicket(OnlineSession online, IPEndPoint server, ulong matchId, NetToken token)
        {
            var ticket = new RejoinTicket
            {
                Server = NetAddress.FromEndPoint(server), MatchId = matchId, Slot = OwnSlot, Token = token,
                SavedAtSeconds = RejoinStore.UnixNowSeconds()
            };
            new RejoinStore(new PlayerPrefsKeyValueStore(), online.Config.ReconnectWindowSeconds).Save(in ticket);
            online.Init(Object.FindFirstObjectByType<MenuScreen>().Database); // as on the next start of the app
        }

        private static MenuScreen FindMenu()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.Menu) return null;
            MenuScreen m = Object.FindFirstObjectByType<MenuScreen>();
            return m != null && m.RejoinButton != null ? m : null;
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
