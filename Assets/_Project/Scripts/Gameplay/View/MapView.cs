using Arena.Core.Map;
using Arena.Core.Math;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Thin view of the map (D-014): the floor is one scaled quad, walls one combined mesh, water one combined sheet;
    /// breakable walls are separate cubes (switched off when the simulation reports <c>TileBroken</c>), bushes are low
    /// cubes whose material turns see-through around the local fighter while it hides in a bush.
    /// Built once at match start from the state's tiles; no allocations per frame. All map renderers sit under one "Static"
    /// child that is static-batched at runtime (<see cref="StaticBatchingUtility.Combine(GameObject)"/>, S2.13): the many
    /// bush and breakable cubes become a few draw calls; switching a breakable off and swapping a bush material still work.
    /// </summary>
    public sealed class MapView : MonoBehaviour
    {
        private ViewCatalog _catalog;
        private GameObject[] _breakables;
        private Renderer[] _bushes;
        private Vec2[] _bushCentres;
        private bool[] _bushSeeThrough;
        private int _bushCount;

        public int BreakableCount { get; private set; }
        public int BushCount => _bushCount;

        /// <summary>True if the bush and breakable cubes are static-batched (tests).</summary>
        public bool IsStaticBatched { get; private set; }

        public static MapView Create(Transform parent, ViewCatalog catalog, in TileGrid grid)
        {
            var go = new GameObject("Map");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MapView>();
            view.Build(catalog, in grid);
            return view;
        }

        /// <summary>True if the breakable wall view of the tile is shown (tests).</summary>
        public bool IsBreakableShown(int tileIndex) =>
            _breakables != null && tileIndex >= 0 && tileIndex < _breakables.Length && _breakables[tileIndex] != null &&
            _breakables[tileIndex].activeSelf;

        public void OnTileBroken(int tileIndex)
        {
            if (_breakables == null || tileIndex < 0 || tileIndex >= _breakables.Length) return;
            GameObject go = _breakables[tileIndex];
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        /// <summary>
        /// Bushes within <see cref="ViewCatalog.BushSeeThroughRadius"/> of the local fighter turn see-through while it is
        /// in a bush, so the player sees the fighter hidden inside.
        /// </summary>
        public void UpdateSeeThrough(Vec2 localPosition, bool localInBush)
        {
            float radius = _catalog.BushSeeThroughRadius;
            float sq = radius * radius;
            for (int i = 0; i < _bushCount; i++)
            {
                bool see = localInBush && Vec2.SqrDistance(_bushCentres[i], localPosition) <= sq;
                if (see == _bushSeeThrough[i]) continue;
                _bushSeeThrough[i] = see;
                _bushes[i].sharedMaterial = see ? _catalog.BushSeeThrough : _catalog.Bush;
            }
        }

        private void Build(ViewCatalog catalog, in TileGrid grid)
        {
            _catalog = catalog;
            var staticRoot = new GameObject("Static");
            staticRoot.transform.SetParent(transform, false);
            Transform root = staticRoot.transform;
            int cells = grid.Width * grid.Height;
            _breakables = new GameObject[cells];

            if (catalog.FloorPrefab != null)
            {
                GameObject floor = Instantiate(catalog.FloorPrefab, root);
                floor.name = "Floor";
                floor.transform.position = new Vector3(grid.Width * 0.5f, 0f, grid.Height * 0.5f);
                floor.transform.localScale = new Vector3(grid.Width, 1f, grid.Height);
            }

            CreateMeshObject("Walls", root, MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, catalog.WallHeight, "Walls"), catalog.Wall);
            CreateMeshObject("Water", root, MapMeshBuilder.BuildSheet(in grid, TileType.Water, catalog.WaterHeight, "Water"), catalog.Water);

            int bushCount = 0;
            for (int i = 0; i < cells; i++)
                if (grid.Tiles[i] == TileType.Bush) bushCount++;
            _bushes = new Renderer[bushCount];
            _bushCentres = new Vec2[bushCount];
            _bushSeeThrough = new bool[bushCount];

            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    TileType t = grid.Get(c, r);
                    var cell = new GridCell(c, r);
                    Vector3 centre = WorldCoords.ToWorld(cell.Center);
                    if (t == TileType.BreakableWall && catalog.BreakableWallPrefab != null)
                    {
                        GameObject go = Instantiate(catalog.BreakableWallPrefab, centre, Quaternion.identity, root);
                        go.name = "Breakable_" + c + "_" + r;
                        _breakables[grid.IndexOf(c, r)] = go;
                        BreakableCount++;
                    }
                    else if (t == TileType.Bush && catalog.BushPrefab != null)
                    {
                        GameObject go = Instantiate(catalog.BushPrefab, centre, Quaternion.identity, root);
                        go.name = "Bush_" + c + "_" + r;
                        _bushes[_bushCount] = go.GetComponentInChildren<Renderer>();
                        _bushCentres[_bushCount] = cell.Center;
                        _bushCount++;
                    }
                }
            }

            StaticBatchingUtility.Combine(staticRoot);
            IsStaticBatched = _bushCount > 0 ? _bushes[0].isPartOfStaticBatch : BreakableCount > 0;
        }

        private static void CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            if (mesh.vertexCount == 0) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
