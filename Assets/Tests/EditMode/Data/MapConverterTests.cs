using System;
using Arena.Core.Map;
using Arena.Data;
using Arena.Data.Conversion;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Tests.EditMode.Data
{
    public sealed class MapConverterTests
    {
        [Test]
        public void Legend_Size_And_Orientation()
        {
            MapDef map = MapConverter.FromRows("legend", new[]
            {
                "B%\"~$", // top = row 2
                "..H..",
                "A#..." // bottom = row 0
            });
            Assert.AreEqual(5, map.Width);
            Assert.AreEqual(3, map.Height);
            Assert.AreEqual(TileType.Floor, map.Get(0, 2), "spawn cell is floor");
            Assert.AreEqual(TileType.BreakableWall, map.Get(1, 2));
            Assert.AreEqual(TileType.Bush, map.Get(2, 2));
            Assert.AreEqual(TileType.Water, map.Get(3, 2));
            Assert.AreEqual(TileType.BerryBox, map.Get(4, 2));
            Assert.AreEqual(TileType.Floor, map.Get(2, 1), "hollow cell is floor");
            Assert.AreEqual(TileType.Wall, map.Get(1, 0));
            Assert.AreEqual(TileType.Wall, map.Get(-1, 0), "outside the map reads as wall");
            Assert.IsTrue(map.HasHollow);
            Assert.AreEqual(new GridCell(2, 1), map.Hollow);
            Assert.AreEqual(1, map.SpawnCount(0));
            Assert.AreEqual(1, map.SpawnCount(1));
            Assert.IsTrue(map.TryGetSpawn(0, 0, out GridCell a));
            Assert.AreEqual(new GridCell(0, 0), a);
            Assert.IsTrue(map.TryGetSpawn(1, 0, out GridCell b));
            Assert.AreEqual(new GridCell(0, 2), b);
            Assert.IsFalse(map.TryGetSpawn(0, 1, out _));
        }

        [Test]
        public void SpawnOrder_PairsMirrorImages()
        {
            MapDef map = MapConverter.FromRows("pairs", new[] { "B...B", ".....", "A...A" });
            for (int k = 0; k < 2; k++)
            {
                map.TryGetSpawn(0, k, out GridCell a);
                map.TryGetSpawn(1, k, out GridCell b);
                Assert.AreEqual(MapSymmetry.Mirror(a, map.Width, map.Height), b, "pair " + k);
            }
            Assert.IsTrue(MapSymmetry.Check(map, out _));
        }

        [Test]
        public void FormatErrors_Throw_WithCell()
        {
            Assert.Throws<ArgumentException>(() => MapConverter.FromRows("bad", new[] { "....", "..." }), "uneven rows");
            Assert.Throws<ArgumentException>(() => MapConverter.FromRows("bad", new string[0]), "no rows");
            var unknown = Assert.Throws<ArgumentException>(() => MapConverter.FromRows("bad", new[] { "....", ".x.." }));
            StringAssert.Contains("(1, 0)", unknown.Message);
            Assert.Throws<ArgumentException>(() => MapConverter.FromRows("bad", new[] { "H...", "...H" }), "two hollows");
        }

        [Test]
        public void Rules_SpawnCount_And_Symmetry()
        {
            MapDef ok = MapConverter.FromRows("ok", new[] { "B.B.B", "..H..", "B...B".Replace('B', '.'), "A.A.A" });
            Assert.Throws<ArgumentException>(() => MapConverter.ValidateRules(ok, 2, false), "3 spawns, expected 2");
            Assert.DoesNotThrow(() => MapConverter.ValidateRules(ok, 3, false));

            MapDef asym = MapConverter.FromRows("asym", new[] { "B....", ".....", "A...." });
            var e = Assert.Throws<ArgumentException>(() => MapConverter.ValidateRules(asym, 1, true));
            StringAssert.Contains("symmetric", e.Message);

            MapDef wall = MapConverter.FromRows("wall", new[] { ".....", "#....", "....." });
            Assert.IsFalse(MapSymmetry.Check(wall, out GridCell mismatch));
            Assert.AreEqual(new GridCell(0, 1), mismatch, "first differing cell");
        }

        [Test]
        public void ToMapDef_UsesConfigRules()
        {
            var config = ScriptableObject.CreateInstance<MapConfig>();
            try
            {
                config.Id = "cfg";
                config.Rows = new[] { "..B..", "..H..", "..A.." };
                config.RequiredSpawnsPerTeam = 1;
                config.RequireCentralSymmetry = true;
                MapDef map = MapConverter.ToMapDef(config);
                Assert.AreEqual("cfg", map.Id);
                Assert.IsTrue(MapConverter.CheckSymmetry(config, out _, out string error), error);

                config.Rows = new[] { "..B..", "..H..", "...A." };
                Assert.Throws<ArgumentException>(() => MapConverter.ToMapDef(config));
                Assert.IsFalse(MapConverter.CheckSymmetry(config, out GridCell cell, out string message));
                Assert.AreEqual(new GridCell(3, 0), cell);
                StringAssert.Contains("(3, 0)", message);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
