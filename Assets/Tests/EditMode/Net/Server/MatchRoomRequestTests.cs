using System.Collections.Generic;
using Arena.Core.Defs;
using Arena.Core.Modes;
using Arena.Net.Content;
using Arena.Net.Server.Match;
using Arena.Tests.EditMode.Net.Snapshots;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net.Server
{
    /// <summary>
    /// The server's line-up (S3.6): <see cref="MatchSetupBuilder.PickRandomFighters"/> keeps the players' choices, gives bots
    /// seeded fighters without repeats within a team; <see cref="MatchRoomRequest.FromBundle"/> builds team and free-for-all setups.
    /// </summary>
    public sealed class MatchRoomRequestTests
    {
        [Test]
        public void PickRandomFighters_KeepsChoices_NoRepeatsInTeam_BySeed()
        {
            ContentBundle bundle = SnapshotMatchRig.LoadBundle();
            MatchDefs defs = bundle.CreateMatchDefs(bundle.MapsFor(ModeKind.Acorns).MapIndices[0], ModeKind.Acorns);
            int teamSize = defs.Map.SpawnCount(0);
            int slots = teamSize * 2;
            var fixedSlots = new bool[slots];
            fixedSlots[0] = fixedSlots[teamSize] = true;
            int[] a = Pick(defs, slots, fixedSlots, teamSize, 42);
            int[] b = Pick(defs, slots, fixedSlots, teamSize, 42);
            CollectionAssert.AreEqual(a, b, "same seed — same fighters");
            Assert.AreEqual(1, a[0], "choice kept");
            Assert.AreEqual(1, a[teamSize], "the other team may have the same fighter");
            for (int team = 0; team < 2; team++)
            {
                var used = new HashSet<int>();
                for (int i = team * teamSize; i < (team + 1) * teamSize; i++) Assert.IsTrue(used.Add(a[i]), "no repeats in team " + team);
            }
            bool differs = false;
            for (int seed = 1; seed < 10 && !differs; seed++)
                differs = !Equal(a, Pick(defs, slots, fixedSlots, teamSize, seed));
            Assert.IsTrue(differs, "another seed — another line-up");
        }

        [Test]
        public void FromBundle_TeamsAndFreeForAll()
        {
            ContentBundle bundle = SnapshotMatchRig.LoadBundle();
            int acornsMap = bundle.MapsFor(ModeKind.Acorns).MapIndices[0];
            MatchRoomRequest r = MatchRoomRequest.FromBundle(bundle, acornsMap, ModeKind.Acorns, new[] { 2, -1, -1, -1, 0, -1 }, 9);
            Assert.AreEqual(6, r.Setup.SlotCount);
            Assert.AreEqual(2, r.Setup.Slots[0].FighterIndex);
            Assert.AreEqual(0, r.Setup.Slots[4].FighterIndex);
            Assert.AreEqual(0, r.Setup.Slots[2].Team);
            Assert.AreEqual(1, r.Setup.Slots[3].Team);
            Assert.IsTrue(r.PlayerSlots[0] && r.PlayerSlots[4] && !r.PlayerSlots[1]);
            r.Validate();

            int beastMap = bundle.MapsFor(ModeKind.LastBeast).MapIndices[0];
            var fighters = new int[10];
            for (int i = 0; i < fighters.Length; i++) fighters[i] = -1;
            fighters[3] = 1;
            MatchRoomRequest ffa = MatchRoomRequest.FromBundle(bundle, beastMap, ModeKind.LastBeast, fighters, 9);
            Assert.AreEqual(10, ffa.Setup.SlotCount);
            for (int i = 0; i < 10; i++) Assert.AreEqual(i, ffa.Setup.Slots[i].Team, "a team per slot");
            Assert.IsTrue(ffa.Defs.NoRespawn);
        }

        private static int[] Pick(MatchDefs defs, int slots, bool[] fixedSlots, int teamSize, int seed)
        {
            var f = new int[slots];
            for (int i = 0; i < slots; i++) f[i] = fixedSlots[i] ? 1 : -1;
            MatchSetupBuilder.PickRandomFighters(defs, f, fixedSlots, teamSize, seed);
            return f;
        }

        private static bool Equal(int[] a, int[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
