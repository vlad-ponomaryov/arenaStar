using System;
using System.Collections.Generic;
using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Data;
using Arena.Gameplay.View;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena.Editor.Art.Capture
{
    /// <summary>
    /// Test stand for the control screenshots (Art-Spec §6.5), built in the open scene without the simulation: a 9×7 piece of map
    /// (floor, walls, breakable wall, berry box, bushes, water) through the real <see cref="MapView"/>, and every fighter of the
    /// database in a row across the middle (first half team 0, second half team 1), built like <c>MatchRunner</c> builds them.
    /// </summary>
    public sealed class CaptureStage
    {
        /// <summary>Rows from the top (far side for team 0, row 6) to the bottom (row 0). W wall, X breakable, O box, B bush, ~ water.</summary>
        private static readonly string[] Layout =
        {
            "WWWW.XO.W",
            "W.......W",
            ".........",
            ".........",
            ".........",
            "BB.....~~",
            "BB..O..~~"
        };

        /// <summary>Fighters stand on this world Z (map Y), centred across the stage with this spacing, cells.</summary>
        public const float RowZ = 3.5f;
        private const float Spacing = 1.2f;

        private readonly GameObject _root;
        private readonly ViewCatalog _catalog;
        private readonly List<FighterView> _views = new List<FighterView>();
        private readonly List<string> _ids = new List<string>();
        private readonly List<int> _teams = new List<int>();

        public int Width { get; }
        public int Height { get; }
        public GameObject MapRoot { get; }
        public int FighterCount => _views.Count;

        private CaptureStage(GameObject root, ViewCatalog catalog, int width, int height, GameObject mapRoot)
        {
            _root = root;
            _catalog = catalog;
            Width = width;
            Height = height;
            MapRoot = mapRoot;
        }

        public static CaptureStage Build(GameDatabase database, ViewCatalog catalog)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            int height = Layout.Length, width = Layout[0].Length;
            var tiles = new TileType[width * height];
            for (int r = 0; r < height; r++)
            {
                string line = Layout[height - 1 - r];
                for (int c = 0; c < width; c++) tiles[r * width + c] = Parse(line[c]);
            }
            var root = new GameObject("CaptureStage");
            MapView map = MapView.Create(root.transform, catalog, new TileGrid(width, height, tiles));
            var stage = new CaptureStage(root, catalog, width, height, map.gameObject);

            int count = database.Fighters.Count;
            float x0 = width * 0.5f - (count - 1) * Spacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                FighterConfig config = database.Fighters[i];
                FighterView view = Object.Instantiate(ResolvePrefab(catalog, config.Id), root.transform);
                view.name = "Fighter" + i + "_" + config.Id;
                view.SetBodyMaterial(catalog.FighterBody);
                view.SetPose(WorldCoords.ToWorld(new Vec2(x0 + i * Spacing, RowZ)), Vector3.back);
                stage._views.Add(view);
                stage._ids.Add(config.Id);
                stage._teams.Add(i < (count + 1) / 2 ? 0 : 1);
            }
            stage.SetLocalTeam(0);
            return stage;
        }

        /// <summary>The fighter prefab of a fighter id, as <c>MatchRunner</c> picks it (<see cref="ViewCatalog.FighterPrefab"/>).</summary>
        public static FighterView ResolvePrefab(ViewCatalog catalog, string fighterId) => catalog.FighterPrefab(fighterId);

        public FighterView Fighter(int index) => _views[index];

        /// <summary>Every fighter on the normal body material.</summary>
        public void ResetBodyMaterials()
        {
            for (int i = 0; i < _views.Count; i++) _views[i].SetBodyMaterial(_catalog.FighterBody);
        }
        public string FighterId(int index) => _ids[index];

        /// <summary>Team rings relative to the viewing team (as in a match); every fighter faces that team's camera.</summary>
        public void SetLocalTeam(int team)
        {
            Vector3 facing = team == 0 ? Vector3.back : Vector3.forward;
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].SetTeamMaterial(_teams[i] == team ? _catalog.TeamAlly : _catalog.TeamEnemy);
                _views[i].SetPose(_views[i].transform.position, facing);
            }
        }

        public void Destroy()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        private static TileType Parse(char ch)
        {
            switch (ch)
            {
                case 'W': return TileType.Wall;
                case 'X': return TileType.BreakableWall;
                case 'O': return TileType.BerryBox;
                case 'B': return TileType.Bush;
                case '~': return TileType.Water;
                default: return TileType.Floor;
            }
        }
    }
}
