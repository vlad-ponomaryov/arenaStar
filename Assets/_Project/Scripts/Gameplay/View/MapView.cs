using Arena.Core.Map;
using Arena.Core.Math;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Thin view of the map (D-014, A.3a, A.3b): a meadow frame around the arena (FloorPrefab), the floor grid with occlusion near walls,
    /// chamfered walls as one combined mesh, a sunken water sheet with its banks; soft wall shadows on the ground via
    /// <see cref="MapShadowMask"/>; breakable crates and berry boxes (S3.14) are separate prefabs (switched off when the simulation
    /// reports <c>TileBroken</c>), bushes are blob meshes (a variant and a quarter turn per cell) whose material turns see-through
    /// around the local fighter while it hides in a bush.
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
        private int[] _bushMasks;
        private int _bushCount;
        private MapShadowMask _shadowMask;
        private int _gridWidth;

        public int BreakableCount { get; private set; }

        /// <summary>Berry box cubes built (Last Beast tiles).</summary>
        public int BoxCount { get; private set; }
        public int BushCount => _bushCount;

        /// <summary>Wall cells drawn as obstacle props instead of stone blocks (A.3c).</summary>
        public int ObstaclePropCount { get; private set; }

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

        /// <summary>Bush-neighbour mask (W 1, E 2, S 4, N 8) whose tall-grass mesh bush <paramref name="index"/> got (tests, A.3c).</summary>
        public int BushMask(int index) => _bushMasks[index];

        /// <summary>Current material of bush <paramref name="index"/> (tests: see-through swap, D-024).</summary>
        public Material BushMaterial(int index) => _bushes[index].sharedMaterial;

        /// <summary>The wall shadow mask of this map (tests).</summary>
        public MapShadowMask ShadowMask => _shadowMask;

        /// <summary>True if the breakable wall view of the tile is shown (tests).</summary>
        public bool IsBreakableShown(int tileIndex) =>
            _breakables != null && tileIndex >= 0 && tileIndex < _breakables.Length && _breakables[tileIndex] != null &&
            _breakables[tileIndex].activeSelf;

        public void OnTileBroken(int tileIndex)
        {
            if (_breakables == null || tileIndex < 0 || tileIndex >= _breakables.Length) return;
            GameObject go = _breakables[tileIndex];
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            if (_shadowMask != null && _gridWidth > 0) _shadowMask.ClearCell(tileIndex % _gridWidth, tileIndex / _gridWidth);
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

        private void OnDestroy()
        {
            if (_shadowMask == null) return;
            MapShadowMask.Clear();
            _shadowMask.Destroy();
            _shadowMask = null;
        }

        private void Build(ViewCatalog catalog, in TileGrid grid)
        {
            _catalog = catalog;
            _gridWidth = grid.Width;
            var staticRoot = new GameObject("Static");
            staticRoot.transform.SetParent(transform, false);
            Transform root = staticRoot.transform;
            int cells = grid.Width * grid.Height;
            _breakables = new GameObject[cells];

            MapMeshStyle style = catalog.MapStyle;
            if (catalog.FloorPrefab != null)
            {
                GameObject meadow = Instantiate(catalog.FloorPrefab, root);
                meadow.name = "Meadow";
                var filter = meadow.GetComponentInChildren<MeshFilter>();
                if (filter != null) filter.sharedMesh = MapMeshBuilder.BuildMeadow(grid.Width, grid.Height, in style, "Meadow");
            }

            // A sunken pond (WaterHeight < 0): no floor under the water, banks (wall material) close its sides.
            bool sunken = catalog.WaterHeight < 0f;
            TileType hole = sunken ? TileType.Water : TileType.Wall;
            bool[] propCells = PlaceObstacleProps(catalog, in grid, root);
            CreateMeshObject("Floor", root, MapMeshBuilder.BuildFloor(in grid, TileType.Wall, hole, in style, "Floor", propCells), catalog.Floor);
            CreateMeshObject("Walls", root, MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, catalog.WallHeight, in style, "Walls", propCells), catalog.Wall);
            CreateMeshObject("Water", root, MapMeshBuilder.BuildSheet(in grid, TileType.Water, catalog.WaterHeight, in style, "Water"), catalog.Water);
            if (sunken)
                CreateMeshObject("Banks", root, MapMeshBuilder.BuildBanks(in grid, TileType.Water, catalog.WaterHeight - catalog.BankOverlap, in style, "Banks"), catalog.Wall);

            _shadowMask = new MapShadowMask(in grid, in style);
            _shadowMask.Apply(catalog.WallHeight, in style);

            int bushCount = 0;
            for (int i = 0; i < cells; i++)
                if (grid.Tiles[i] == TileType.Bush) bushCount++;
            _bushes = new Renderer[bushCount];
            _bushCentres = new Vec2[bushCount];
            _bushSeeThrough = new bool[bushCount];
            _bushMasks = new int[bushCount];

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
                    else if (t == TileType.BerryBox && catalog.BerryBoxPrefab != null)
                    {
                        // Last Beast (S3.14): a box breaks like a wall (TileBroken); elsewhere it is an unbreakable block.
                        GameObject go = Instantiate(catalog.BerryBoxPrefab, centre, Quaternion.identity, root);
                        go.name = "BerryBox_" + c + "_" + r;
                        _breakables[grid.IndexOf(c, r)] = go;
                        BoxCount++;
                    }
                    else if (t == TileType.Bush && catalog.BushPrefab != null)
                    {
                        // A.3c: the tall-grass mesh of this cell's bush-neighbour mask (sides towards neighbours join into one
                        // mass), a variant per cell (deterministic, view only). No turn: the mask is in map axes.
                        int hash = CellHash(c, r);
                        GameObject go = Instantiate(catalog.BushPrefab, centre, Quaternion.identity, root);
                        go.name = "Bush_" + c + "_" + r;
                        Mesh[] variants = catalog.BushVariants;
                        int mask = (Is(in grid, c - 1, r, TileType.Bush) ? 1 : 0) | (Is(in grid, c + 1, r, TileType.Bush) ? 2 : 0) |
                                   (Is(in grid, c, r - 1, TileType.Bush) ? 4 : 0) | (Is(in grid, c, r + 1, TileType.Bush) ? 8 : 0);
                        _bushMasks[_bushCount] = mask;
                        if (variants != null && variants.Length > 0)
                        {
                            int perMask = Mathf.Max(1, catalog.BushVariantsPerMask);
                            int index = mask * perMask + hash % perMask;
                            Mesh mesh = variants[index % variants.Length];
                            var filter = go.GetComponentInChildren<MeshFilter>();
                            if (mesh != null && filter != null) filter.sharedMesh = mesh;
                        }
                        _bushes[_bushCount] = go.GetComponentInChildren<Renderer>();
                        _bushCentres[_bushCount] = cell.Center;
                        _bushCount++;
                    }
                }
            }

            StaticBatchingUtility.Combine(staticRoot);
            IsStaticBatched = _bushCount > 0 ? _bushes[0].isPartOfStaticBatch : BreakableCount > 0;
        }

        private static bool Is(in TileGrid grid, int c, int r, TileType type) => grid.InBounds(c, r) && grid.Get(c, r) == type;

        /// <summary>
        /// A.3c (D-126): small connected wall groups (≤ <see cref="ViewCatalog.ObstaclePropMaxCells"/> cells) may be drawn as one prop
        /// type (barrels, stumps, boulders…) instead of stone blocks; types cycle over the groups keyed so that a group and its
        /// mirrored twins (maps are symmetric) look alike. View only: the cells still block fully.
        /// Returns the cells drawn as props (the stone block mesh skips them), or null.
        /// </summary>
        private bool[] PlaceObstacleProps(ViewCatalog catalog, in TileGrid grid, Transform root)
        {
            Mesh[] props = catalog.ObstacleProps;
            if (props == null || props.Length == 0 || catalog.ObstacleProp == null || catalog.ObstaclePropMaxCells <= 0) return null;
            int cells = grid.Width * grid.Height;
            var group = new int[cells];
            for (int i = 0; i < cells; i++) group[i] = -1;
            var propCells = new bool[cells];
            var members = new System.Collections.Generic.List<int>();
            var stack = new System.Collections.Generic.Stack<int>();
            // Small groups: (key, first member index in the flat list, count). The key is the same for a group and its mirrored twin.
            var small = new System.Collections.Generic.List<(int key, int first, int count)>();
            var flat = new System.Collections.Generic.List<int>();
            var keys = new System.Collections.Generic.List<int>();
            int groups = 0;
            for (int start = 0; start < cells; start++)
            {
                if (grid.Tiles[start] != TileType.Wall || group[start] >= 0) continue;
                members.Clear();
                stack.Push(start);
                group[start] = groups;
                while (stack.Count > 0)
                {
                    int k = stack.Pop();
                    members.Add(k);
                    int c = k % grid.Width, r = k / grid.Width;
                    for (int d = 0; d < 4; d++)
                    {
                        int nc = c + (d == 0 ? -1 : d == 1 ? 1 : 0), nr = r + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (!Is(in grid, nc, nr, TileType.Wall)) continue;
                        int ni = grid.IndexOf(nc, nr);
                        if (group[ni] >= 0) continue;
                        group[ni] = groups;
                        stack.Push(ni);
                    }
                }
                groups++;
                if (members.Count > catalog.ObstaclePropMaxCells) continue;
                int key = SymmetricKey(members, grid.Width, grid.Height);
                small.Add((key, flat.Count, members.Count));
                flat.AddRange(members);
                if (!keys.Contains(key)) keys.Add(key);
            }

            // Types cycle over the twin pairs in map order (every type shows up), every ObstacleStoneEvery-th pair stays stone.
            keys.Sort();
            int every = catalog.ObstacleStoneEvery;
            var typeOfKey = new int[keys.Count];
            int next = CellHash(keys.Count, cells) % props.Length;
            for (int i = 0; i < keys.Count; i++)
                typeOfKey[i] = every > 0 && i % every == every - 1 ? -1 : next++ % props.Length;
            for (int g = 0; g < small.Count; g++)
            {
                int type = typeOfKey[keys.IndexOf(small[g].key)];
                if (type < 0) continue;
                Mesh mesh = props[type];
                for (int m = small[g].first; m < small[g].first + small[g].count; m++)
                {
                    int k = flat[m];
                    propCells[k] = true;
                    int c = k % grid.Width, r = k / grid.Width;
                    Vector3 centre = WorldCoords.ToWorld(new GridCell(c, r).Center);
                    var go = new GameObject("Obstacle_" + c + "_" + r);
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, (CellHash(c, r) & 3) * 90f, 0f));
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = catalog.ObstacleProp;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    ObstaclePropCount++;
                }
            }            return propCells;
        }

        /// <summary>
        /// Smallest cell index of the group over the four mirror images of the map (identity, left-right, top-bottom, central):
        /// a group and its mirrored twins share the key, whichever symmetry the map has.
        /// </summary>
        private static int SymmetricKey(System.Collections.Generic.List<int> members, int width, int height)
        {
            int key = int.MaxValue;
            for (int s = 0; s < 4; s++)
            {
                for (int m = 0; m < members.Count; m++)
                {
                    int c = members[m] % width, r = members[m] / width;
                    if ((s & 1) != 0) c = width - 1 - c;
                    if ((s & 2) != 0) r = height - 1 - r;
                    key = Mathf.Min(key, r * width + c);
                }
            }
            return key;
        }

        private static int CellHash(int c, int r)
        {
            unchecked
            {
                uint h = (uint)(c * 73856093) ^ (uint)(r * 19349663) ^ 0x9E3779B9u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (int)(h & 0x7FFFFFFF);
            }
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
