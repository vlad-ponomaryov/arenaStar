using Arena.Core.Math;
using Arena.Core.Random;
using Arena.Core.State;
using Arena.Net.Diagnostics;
using NUnit.Framework;

namespace Arena.Tests.EditMode.Net
{
    public sealed class StateHashTests
    {
        [Test]
        public void EqualStates_SameHash_AnyFieldChangesIt()
        {
            var a = new MatchState();
            a.Globals.Tick = 42;
            a.Globals.Rng = DeterministicRng.FromSeed(9);
            a.Fighters[0].Active = true;
            a.Fighters[0].Position = new Vec2(1.5f, 2.5f);
            var b = new MatchState();
            b.CopyFrom(a);
            ulong hash = StateHash.Compute(a);
            Assert.AreEqual(hash, StateHash.Compute(b));

            b.Fighters[0].Position = new Vec2(1.5f, 2.5000002f);
            Assert.AreNotEqual(hash, StateHash.Compute(b), "a float bit");

            b.CopyFrom(a);
            b.Projectiles[255].SpawnTick = 1;
            Assert.AreNotEqual(hash, StateHash.Compute(b), "an inactive last slot");

            b.CopyFrom(a);
            b.Tiles[b.Tiles.Length - 1] = Arena.Core.Map.TileType.Wall;
            Assert.AreNotEqual(hash, StateHash.Compute(b), "a tile");

            b.CopyFrom(a);
            b.Globals.Rng = DeterministicRng.FromSeed(10);
            Assert.AreNotEqual(hash, StateHash.Compute(b), "the RNG state");

            b.CopyFrom(a);
            b.TeamRevealUntilTick[3] = 5;
            Assert.AreNotEqual(hash, StateHash.Compute(b), "a team array");
        }

        [Test]
        public void ClearedState_HashIsStable()
        {
            Assert.AreEqual(StateHash.Compute(new MatchState()), StateHash.Compute(new MatchState()));
        }
    }
}
