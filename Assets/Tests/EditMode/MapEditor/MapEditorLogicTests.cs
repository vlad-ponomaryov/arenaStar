using System;
using System.Reflection;
using Arena.Core.Map;
using Arena.Data;
using Arena.Data.Conversion;
using Arena.Editor.Data;
using Arena.Editor.MapEditor;
using Arena.Editor.Menu;
using NUnit.Framework;
using UnityEditor;

namespace Arena.Tests.EditMode.MapEditor
{
    public sealed class MapEditorLogicTests
    {
        private static MapGrid Floor(int w, int h) => new MapGrid(w, h, MapConverter.FloorChar);

        private static int Count(MapGrid grid, char ch)
        {
            int n = 0;
            foreach (char c in grid.Cells) if (c == ch) n++;
            return n;
        }

        [Test]
        public void Brush1_PaintsOneCell()
        {
            MapGrid g = Floor(5, 5);
            Assert.AreEqual(1, MapEditorLogic.Paint(g, 2, 1, MapConverter.WallChar, 1));
            Assert.AreEqual(MapConverter.WallChar, g.Get(2, 1));
            Assert.AreEqual(1, Count(g, MapConverter.WallChar));
            Assert.AreEqual(0, MapEditorLogic.Paint(g, 2, 1, MapConverter.WallChar, 1), "repaint changes nothing");
            Assert.AreEqual(0, MapEditorLogic.Paint(g, 7, 1, MapConverter.WallChar, 1), "outside the map");
        }

        [Test]
        public void Brush3_PaintsSquare_ClippedAtEdges()
        {
            MapGrid g = Floor(5, 5);
            Assert.AreEqual(9, MapEditorLogic.Paint(g, 2, 2, MapConverter.BushChar, 3));
            for (int t = 1; t <= 3; t++)
                for (int c = 1; c <= 3; c++)
                    Assert.AreEqual(MapConverter.BushChar, g.Get(c, t));

            MapGrid corner = Floor(5, 5);
            Assert.AreEqual(4, MapEditorLogic.Paint(corner, 0, 4, MapConverter.WaterChar, 3));
            Assert.AreEqual(MapConverter.WaterChar, corner.Get(0, 3));
            Assert.AreEqual(MapConverter.WaterChar, corner.Get(1, 4));
            Assert.AreEqual(4, Count(corner, MapConverter.WaterChar));
        }

        [Test]
        public void Markers_AreSingleCell_AndHollowIsUnique()
        {
            MapGrid g = Floor(5, 5);
            Assert.AreEqual(1, MapEditorLogic.Paint(g, 2, 2, MapConverter.Team0SpawnChar, 3));
            Assert.AreEqual(1, Count(g, MapConverter.Team0SpawnChar));

            MapEditorLogic.Paint(g, 0, 0, MapConverter.HollowChar, 1);
            MapEditorLogic.Paint(g, 4, 4, MapConverter.HollowChar, 3);
            Assert.AreEqual(1, Count(g, MapConverter.HollowChar));
            Assert.AreEqual(MapConverter.HollowChar, g.Get(4, 4));
            Assert.AreEqual(MapConverter.FloorChar, g.Get(0, 0));
        }

        [Test]
        public void Resize_KeepsContent_AnchoredBottomLeft()
        {
            MapGrid g = MapEditorLogic.FromRows(new[] { "B.#", ".%.", "A~\"" });
            MapGrid bigger = MapEditorLogic.Resize(g, 5, 4);
            Assert.AreEqual(5, bigger.Width);
            Assert.AreEqual(4, bigger.Height);
            CollectionAssert.AreEqual(new[] { ".....", "B.#..", ".%...", "A~\".." }, MapEditorLogic.ToRows(bigger));

            MapGrid smaller = MapEditorLogic.Resize(g, 2, 2);
            CollectionAssert.AreEqual(new[] { ".%", "A~" }, MapEditorLogic.ToRows(smaller));

            MapGrid back = MapEditorLogic.Resize(bigger, 3, 3);
            CollectionAssert.AreEqual(MapEditorLogic.ToRows(g), MapEditorLogic.ToRows(back));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MirrorCentral_MakesSymmetricMap(bool fromTop)
        {
            MapGrid g = MapEditorLogic.FromRows(new[]
            {
                "B.#.%",
                "~~\"..",
                "..H#.",
                ".\".~.",
                "A%..#"
            });
            Assert.IsFalse(MapEditorLogic.CheckSymmetry(g, "mirror", out _, out _, out _));
            MapEditorLogic.MirrorCentral(g, fromTop);
            Assert.IsTrue(MapEditorLogic.CheckSymmetry(g, "mirror", out GridCell cell, out _, out string error),
                error + " " + cell);
            MapDef map = MapConverter.FromRows("mirror", MapEditorLogic.ToRows(g));
            Assert.IsTrue(MapSymmetry.Check(map, out _));
            Assert.AreEqual(1, map.SpawnCount(0));
            Assert.AreEqual(1, map.SpawnCount(1));
            Assert.IsTrue(map.HasHollow);
            Assert.AreEqual(new GridCell(2, 2), map.Hollow, "centre cell is kept");
        }

        [Test]
        public void MirrorCentral_EvenSize_CopiesHalfAndSwapsSpawns()
        {
            MapGrid g = MapEditorLogic.FromRows(new[] { "A#..", "....", "....", ".%.." });
            MapEditorLogic.MirrorCentral(g, true);
            CollectionAssert.AreEqual(new[] { "A#..", "....", "....", "..#B" }, MapEditorLogic.ToRows(g));
            MapEditorLogic.MirrorCentral(g, false);
            CollectionAssert.AreEqual(new[] { "A#..", "....", "....", "..#B" }, MapEditorLogic.ToRows(g),
                "an already symmetric map does not change");
        }

        [Test]
        public void CheckSymmetry_ReportsCell()
        {
            MapGrid g = Floor(5, 5);
            MapEditorLogic.Paint(g, 1, 4, MapConverter.WallChar, 1); // map cell (1, 0)
            Assert.IsFalse(MapEditorLogic.CheckSymmetry(g, "asym", out GridCell cell, out bool hasCell, out string error));
            Assert.IsTrue(hasCell);
            Assert.AreEqual(new GridCell(1, 0), cell);
            StringAssert.Contains("symmetric", error);

            MapEditorLogic.Paint(g, 0, 0, 'z', 1);
            Assert.IsFalse(MapEditorLogic.CheckSymmetry(g, "bad", out _, out bool parseCell, out string parseError));
            Assert.IsFalse(parseCell);
            StringAssert.Contains("unknown character", parseError);
        }

        [Test]
        public void RowsRoundTrip_AndUnequalRowsThrow()
        {
            string[] rows = { "B%\"~$", "..H..", "A#..." };
            MapGrid g = MapEditorLogic.FromRows(rows);
            Assert.AreEqual(5, g.Width);
            Assert.AreEqual(3, g.Height);
            Assert.AreEqual('A', g.Get(0, 2));
            CollectionAssert.AreEqual(rows, MapEditorLogic.ToRows(g));
            Assert.Throws<ArgumentException>(() => MapEditorLogic.FromRows(new[] { "...", ".." }));
            Assert.Throws<ArgumentException>(() => MapEditorLogic.FromRows(new string[0]));
        }

        [Test]
        public void SeededGlade_RoundTripsAndValidates()
        {
            var config = AssetDatabase.LoadAssetAtPath<MapConfig>(DataSeeder.AcornsGladePath);
            Assert.IsNotNull(config, DataSeeder.AcornsGladePath);
            MapGrid g = MapEditorLogic.FromRows(config.Rows);
            CollectionAssert.AreEqual(config.Rows, MapEditorLogic.ToRows(g));
            Assert.IsTrue(MapEditorLogic.Validate(g, config.Id, config.RequiredSpawnsPerTeam, config.RequireCentralSymmetry,
                out string error), error);

            MapEditorLogic.MirrorCentral(g, true);
            CollectionAssert.AreEqual(config.Rows, MapEditorLogic.ToRows(g), "mirroring a symmetric map is a no-op");

            Assert.IsFalse(MapEditorLogic.Validate(g, config.Id, config.RequiredSpawnsPerTeam + 1, false, out string spawnError));
            StringAssert.Contains("spawns", spawnError);
        }

        [TestCase(nameof(ArenaMenu.SeedData), "Arena/Seed Data")]
        [TestCase(nameof(ArenaMenu.RebuildScenes), "Arena/Rebuild Scenes")]
        [TestCase(nameof(ArenaMenu.RebuildViewAssets), "Arena/Rebuild View Assets")]
        [TestCase(nameof(ArenaMenu.BuildAndroidApk), "Arena/Build Android APK")]
        public void ArenaMenu_HasItem(string method, string path)
        {
            MethodInfo info = typeof(ArenaMenu).GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(info, method);
            var item = info.GetCustomAttribute<MenuItem>();
            Assert.IsNotNull(item, method);
            Assert.AreEqual(path, item.menuItem);
        }

        [Test]
        public void MapEditorWindow_HasMenuItem()
        {
            MethodInfo info = typeof(MapEditorWindow).GetMethod(nameof(MapEditorWindow.Open), BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(info);
            Assert.AreEqual("Arena/Map Editor", info.GetCustomAttribute<MenuItem>().menuItem);
        }
    }
}
