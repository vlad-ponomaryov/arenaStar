using Arena.Core.Map;
using Arena.Core.Math;
using Arena.Data;
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
    /// D-140 (Map-SnowValley-LB Р1/Р2): a map may bring a theme (<see cref="MapThemeConfig"/>: materials, colours, props) and a decor
    /// layer (<see cref="MapConfig.Decor"/>, legend <see cref="MapDecor"/>): walls become a spruce, fort corners, drifts, sleds;
    /// breakable walls snowmen or snow bricks (a flat snow pile shows when they break); berry boxes gifts. View only.
    /// D-142: fort corners with lanterns, a snow-brick frame wall with garlands and cabins behind it (<see cref="MapFrameDecor"/>), snow
    /// lumps at wall feet and footprint trails (<see cref="SnowTrailBuilder"/>).
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
        private GameObject[] _rubble;
        private Material _bushMaterial;
        private Material _bushSeeThroughMaterial;

        public int BreakableCount { get; private set; }

        /// <summary>Berry box cubes built (Last Beast tiles).</summary>
        public int BoxCount { get; private set; }
        public int BushCount => _bushCount;

        /// <summary>Wall cells drawn as obstacle props instead of stone blocks (A.3c).</summary>
        public int ObstaclePropCount { get; private set; }

        /// <summary>Wall cells drawn as separate stone blocks (A.3f).</summary>
        public int WallBlockCount { get; private set; }

        /// <summary>Cells drawn by the decor layer (walls, breakables and boxes with a decor prop, D-140) and frame trees placed.</summary>
        public int DecorPropCount { get; private set; }
        public int FrameTreeCount { get; private set; }

        /// <summary>D-142: frame wall objects (segments, posts, garlands, lumps), cabins / woodpiles / mounds behind it, snow lumps at
        /// decor walls, and whether footprint trails were built.</summary>
        public int FrameWallCount { get; private set; }
        public int FrameExtraCount { get; private set; }
        public int WallLumpCount { get; private set; }
        public bool HasTrails { get; private set; }

        /// <summary>Theme of the map this view was built for, or null (shared look).</summary>
        public MapThemeConfig Theme { get; private set; }

        /// <summary>Floor details planned for this map and their triangles (A.3e).</summary>
        public int DecorCount { get; private set; }
        public int DecorTriangles { get; private set; }

        /// <summary>True if the bush and breakable cubes are static-batched (tests).</summary>
        public bool IsStaticBatched { get; private set; }

        public static MapView Create(Transform parent, ViewCatalog catalog, in TileGrid grid) => Create(parent, catalog, in grid, null);

        /// <summary>The view of <paramref name="grid"/> with the theme and decor of <paramref name="map"/> (null = shared look; a decor
        /// layer that fails <see cref="MapDecor.Validate"/> is ignored with a warning).</summary>
        public static MapView Create(Transform parent, ViewCatalog catalog, in TileGrid grid, MapConfig map)
        {
            var go = new GameObject("Map");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MapView>();
            view.Build(catalog, in grid, map);
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

        /// <summary>True if the snow pile of a broken decor breakable is shown on the tile (tests).</summary>
        public bool IsRubbleShown(int tileIndex) =>
            _rubble != null && tileIndex >= 0 && tileIndex < _rubble.Length && _rubble[tileIndex] != null && _rubble[tileIndex].activeSelf;

        public void OnTileBroken(int tileIndex)
        {
            if (_breakables == null || tileIndex < 0 || tileIndex >= _breakables.Length) return;
            GameObject go = _breakables[tileIndex];
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            if (_rubble != null && _rubble[tileIndex] != null) _rubble[tileIndex].SetActive(true);
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
                _bushes[i].sharedMaterial = see ? _bushSeeThroughMaterial : _bushMaterial;
            }
        }

        private void OnDestroy()
        {
            if (_shadowMask == null) return;
            MapShadowMask.Clear();
            _shadowMask.Destroy();
            _shadowMask = null;
        }

        private void Build(ViewCatalog catalog, in TileGrid grid, MapConfig map)
        {
            _catalog = catalog;
            MapThemeConfig theme = map != null ? map.Theme : null;
            Theme = theme;
            string[] decor = null;
            if (map != null && map.Decor != null && map.Decor.Length > 0)
            {
                string problem = MapDecor.Validate(map);
                if (problem == null) decor = map.Decor;
                else Debug.LogWarning("MapView: decor of map '" + map.Id + "' ignored: " + problem);
            }
            _bushMaterial = theme != null && theme.Bush != null ? theme.Bush : catalog.Bush;
            _bushSeeThroughMaterial = theme != null && theme.BushSeeThrough != null ? theme.BushSeeThrough : catalog.BushSeeThrough;
            Material floorMaterial = theme != null && theme.Floor != null ? theme.Floor : catalog.Floor;
            Material waterMaterial = theme != null && theme.Water != null ? theme.Water : catalog.Water;
            Material propMaterial = theme != null && theme.Props != null ? theme.Props : catalog.ObstacleProp;
            _gridWidth = grid.Width;
            var staticRoot = new GameObject("Static");
            staticRoot.transform.SetParent(transform, false);
            Transform root = staticRoot.transform;
            int cells = grid.Width * grid.Height;
            _breakables = new GameObject[cells];

            MapMeshStyle style = catalog.MapStyle;
            if (theme != null)
            {
                style.FloorOcclusionColor = theme.FloorOcclusionColor;
                style.MeadowEdge = theme.MeadowEdge;
                style.MeadowInner = theme.MeadowInner;
                style.FloorPatchCoverage = theme.FloorPatchCoverage;
            }
            if (catalog.FloorPrefab != null)
            {
                GameObject meadow = Instantiate(catalog.FloorPrefab, root);
                meadow.name = "Meadow";
                var filter = meadow.GetComponentInChildren<MeshFilter>();
                if (filter != null) filter.sharedMesh = MapMeshBuilder.BuildMeadow(grid.Width, grid.Height, in style, "Meadow");
                var meadowRenderer = meadow.GetComponentInChildren<Renderer>();
                if (theme != null && theme.Meadow != null && meadowRenderer != null) meadowRenderer.sharedMaterial = theme.Meadow;
            }
            if (theme != null)
            {
                FrameTreeCount = PlaceFrameTrees(theme, propMaterial, in grid, root);
                FrameWallCount = MapFrameDecor.PlaceWall(theme, propMaterial, in grid, root);
                FrameExtraCount = MapFrameDecor.PlaceExtras(theme, propMaterial, in grid, root);
            }

            // A sunken pond (WaterHeight < 0): no floor under the water, banks (wall material) close its sides.
            bool sunken = catalog.WaterHeight < 0f;
            TileType hole = sunken ? TileType.Water : TileType.Wall;
            bool[] decorWalls = theme != null && decor != null ? PlaceDecorWalls(theme, propMaterial, in grid, decor, root) : null;
            if (theme != null) WallLumpCount = MapFrameDecor.PlaceWallLumps(theme, propMaterial, in grid, decor, root);
            bool[] propCells = Merge(PlaceObstacleProps(catalog, in grid, root, decorWalls), decorWalls);
            // A.3f: every wall cell is a separate stone block with a gap to its neighbours — the floor runs under all of them.
            bool blocks = catalog.WallBlockVariants != null && catalog.WallBlockVariants.Length > 0 && catalog.ObstacleProp != null;
            bool[] floorUnder = propCells;
            if (blocks)
            {
                floorUnder = new bool[cells];
                for (int i = 0; i < cells; i++) floorUnder[i] = grid.Tiles[i] == TileType.Wall;
            }
            CreateMeshObject("Floor", root, MapMeshBuilder.BuildFloor(in grid, TileType.Wall, hole, in style, "Floor", floorUnder), floorMaterial);
            // A.3e (D-130): stepping-stone paths, pebbles, sprouts and flowers — one mesh with the floor material.
            var details = new System.Collections.Generic.List<FloorDecorItem>();
            EnvDecorStyle decorStyle = catalog.DecorStyle;
            if (theme == null || theme.FloorDecor) DecorTriangles = FloorDecorPlanner.Plan(in grid, in decorStyle, details);
            DecorCount = details.Count;
            if (details.Count > 0) CreateMeshObject("FloorDecor", root, FloorDecorMeshBuilder.Build(details, in decorStyle, "FloorDecor"), floorMaterial);
            // D-142: footprint trails on the snow (spawns → centre).
            Mesh trails = theme != null && map != null ? SnowTrailBuilder.Build(in grid, map.Rows, theme) : null;
            HasTrails = trails != null;
            if (trails != null) CreateMeshObject("Trails", root, trails, propMaterial);
            if (blocks) PlaceWallBlocks(catalog, in grid, root, propCells);
            else CreateMeshObject("Walls", root, MapMeshBuilder.BuildBlocks(in grid, TileType.Wall, catalog.WallHeight, in style, "Walls", propCells), catalog.Wall);
            if (style.PondRimWidth > 0f && sunken && catalog.ObstacleProp != null)
            {
                // A.3f: one round pond with a plank rim (corners smoothed by the neighbour mask) instead of square sheets.
                Mesh water = MapMeshBuilder.BuildPond(in grid, TileType.Water, catalog.WaterHeight, catalog.WaterHeight - catalog.BankOverlap,
                    in style, "Water", out Mesh rim, out Mesh fill);
                CreateMeshObject("Water", root, water, waterMaterial);
                CreateMeshObject("PondRim", root, rim, catalog.ObstacleProp);
                CreateMeshObject("PondFill", root, fill, floorMaterial);
            }
            else
            {
                CreateMeshObject("Water", root, MapMeshBuilder.BuildSheet(in grid, TileType.Water, catalog.WaterHeight, in style, "Water"), waterMaterial);
                if (sunken)
                    CreateMeshObject("Banks", root, MapMeshBuilder.BuildBanks(in grid, TileType.Water, catalog.WaterHeight - catalog.BankOverlap, in style, "Banks"), catalog.Wall);
            }

            _shadowMask = new MapShadowMask(in grid, in style, propCells);
            _shadowMask.Apply(catalog.WallHeight, in style);

            int bushCount = 0;
            for (int i = 0; i < cells; i++)
                if (grid.Tiles[i] == TileType.Bush) bushCount++;
            _bushes = new Renderer[bushCount];
            _bushCentres = new Vec2[bushCount];
            _bushSeeThrough = new bool[bushCount];
            _bushMasks = new int[bushCount];
            Mesh[] bushVariants = theme != null && theme.BushVariants != null && theme.BushVariants.Length > 0 ? theme.BushVariants : catalog.BushVariants;
            Transform rubbleRoot = null;

            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    TileType t = grid.Get(c, r);
                    var cell = new GridCell(c, r);
                    Vector3 centre = WorldCoords.ToWorld(cell.Center);
                    char mark = decor != null ? MapDecor.At(decor, c, r) : MapDecor.None;
                    Mesh decorMesh = theme != null ? BreakableDecorMesh(theme, t, mark, CellHash(c, r)) : null;
                    if (decorMesh != null)
                    {
                        // D-140: a snowman, snow bricks or a gift; breaks like the crate / box (TileBroken), a snow pile stays for m / f / g.
                        int index = grid.IndexOf(c, r);
                        float yaw = mark == MapDecor.Snowman ? SnowmanYaw(theme, c, r) : (CellHash(c, r) >> 3 & 3) * QuarterTurn;
                        _breakables[index] = CreatePropObject((t == TileType.BerryBox || mark == MapDecor.SmallGift ? "Gift_" : "Decor_") + c + "_" + r, root, decorMesh,
                            propMaterial, centre, yaw);
                        if (t == TileType.BerryBox) BoxCount++;
                        else BreakableCount++;
                        DecorPropCount++;
                        if (t == TileType.BreakableWall && theme.SnowPile != null)
                        {
                            if (rubbleRoot == null)
                            {
                                rubbleRoot = new GameObject("Rubble").transform;
                                rubbleRoot.SetParent(transform, false);
                                _rubble = new GameObject[cells];
                            }
                            GameObject pile = CreatePropObject("Pile_" + c + "_" + r, rubbleRoot, theme.SnowPile, propMaterial, centre, yaw);
                            pile.SetActive(false);
                            _rubble[index] = pile;
                        }
                    }
                    else if (t == TileType.BreakableWall && catalog.BreakableWallPrefab != null)
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
                        Mesh[] variants = bushVariants;
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
                        _bushes[_bushCount].sharedMaterial = _bushMaterial;
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
        private bool[] PlaceObstacleProps(ViewCatalog catalog, in TileGrid grid, Transform root, bool[] skip)
        {
            Mesh[] props = catalog.ObstacleProps;
            if (props == null || props.Length == 0 || catalog.ObstacleProp == null || catalog.ObstaclePropMaxCells <= 0) return null;
            int cells = grid.Width * grid.Height;
            var group = new int[cells];
            for (int i = 0; i < cells; i++) group[i] = skip != null && skip[i] ? int.MaxValue : -1;
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
            }
            return propCells;
        }

        /// <summary>Cells marked in either mask (null if both are null).</summary>
        private static bool[] Merge(bool[] a, bool[] b)
        {
            if (a == null) return b;
            if (b == null) return a;
            for (int i = 0; i < a.Length; i++) a[i] |= b[i];
            return a;
        }

        private const float QuarterTurn = 90f;

        /// <summary>
        /// D-140: wall cells with a decor prop — the spruce on the centre of a 'T' plus (one object for the five cells), fort corners /
        /// piers, drifts and sleds (a sled pair along a column keeps the mesh's long axis, along a row it turns 90°). Returns the cells
        /// drawn so (stone blocks and props skip them, the shadow mask treats them as prop footprints).
        /// </summary>
        private bool[] PlaceDecorWalls(MapThemeConfig theme, Material material, in TileGrid grid, string[] decor, Transform root)
        {
            int cells = grid.Width * grid.Height;
            var taken = new bool[cells];
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    int index = grid.IndexOf(c, r);
                    if (grid.Tiles[index] != TileType.Wall) continue;
                    char mark = MapDecor.At(decor, c, r);
                    Mesh mesh;
                    float yaw = 0f;
                    switch (mark)
                    {
                        case MapDecor.Tree:
                            mesh = theme.Tree;
                            if (mesh == null) continue;
                            taken[index] = true;
                            bool centre = MapDecor.At(decor, c - 1, r) == MapDecor.Tree && MapDecor.At(decor, c + 1, r) == MapDecor.Tree &&
                                          MapDecor.At(decor, c, r - 1) == MapDecor.Tree && MapDecor.At(decor, c, r + 1) == MapDecor.Tree;
                            if (!centre) continue;
                            break;
                        case MapDecor.Fort:
                            mesh = theme.FortCorner;
                            break;
                        case MapDecor.Lantern:
                            mesh = theme.FortLantern != null ? theme.FortLantern : theme.FortCorner;
                            break;
                        case MapDecor.Drift:
                            mesh = theme.Drift;
                            yaw = (CellHash(c, r) >> 5 & 3) * QuarterTurn;
                            break;
                        case MapDecor.Sled:
                            mesh = theme.Sled;
                            bool alongColumn = MapDecor.At(decor, c, r - 1) == MapDecor.Sled || MapDecor.At(decor, c, r + 1) == MapDecor.Sled;
                            yaw = alongColumn ? 0f : QuarterTurn;
                            break;
                        default:
                            continue;
                    }
                    if (mesh == null) continue;
                    taken[index] = true;
                    CreatePropObject("Decor_" + c + "_" + r, root, mesh, material, WorldCoords.ToWorld(new GridCell(c, r).Center), yaw);
                    DecorPropCount++;
                }
            }
            return taken;
        }

        private static Mesh BreakableDecorMesh(MapThemeConfig theme, TileType tile, char mark, int hash)
        {
            if (tile == TileType.BerryBox)
            {
                if (mark == MapDecor.BigGift && theme.GiftBig != null) return theme.GiftBig;
                return theme.Gifts != null && theme.Gifts.Length > 0 ? theme.Gifts[hash % theme.Gifts.Length] : null;
            }
            if (tile != TileType.BreakableWall) return null;
            if (mark == MapDecor.SmallGift)
                return theme.Gifts != null && theme.Gifts.Length > 0 ? theme.Gifts[hash % theme.Gifts.Length] : null;
            if (mark == MapDecor.Snowman)
                return theme.Snowmen != null && theme.Snowmen.Length > 0 ? theme.Snowmen[hash % theme.Snowmen.Length] : null;
            return mark == MapDecor.Fort ? theme.FortBrick : null;
        }

        /// <summary>A snowman faces the camera (−Z, the Last Beast view is never rotated), turned by a hash within the jitter.</summary>
        private static float SnowmanYaw(MapThemeConfig theme, int c, int r)
        {
            float t = (CellHash(c, r) & 0xFF) / 255f * 2f - 1f;
            return t * theme.SnowmanYawJitter;
        }

        /// <summary>
        /// Spruces around the left, right and far sides of the arena (<see cref="MapThemeConfig.FrameTreeRings"/> rings, every other ring
        /// shifted by half a step); the near side stays open so nothing hides the field. Returns the trees placed.
        /// </summary>
        private static int PlaceFrameTrees(MapThemeConfig theme, Material material, in TileGrid grid, Transform root)
        {
            if (theme.FrameTrees == null || theme.FrameTrees.Length == 0 || theme.FrameTreeSpacing <= 0f) return 0;
            int count = 0;
            float spacing = theme.FrameTreeSpacing;
            for (int ring = 0; ring < theme.FrameTreeRings; ring++)
            {
                float d = theme.FrameTreeOffset + ring * theme.FrameTreeRingStep;
                float shift = (ring & 1) * spacing * 0.5f;
                float minX = -d, maxX = grid.Width + d, maxZ = grid.Height + d;
                for (float x = minX + shift; x <= maxX; x += spacing) count += PlaceFrameTree(theme, material, root, new Vector3(x, 0f, maxZ), count);
                for (float z = shift; z < maxZ; z += spacing)
                {
                    count += PlaceFrameTree(theme, material, root, new Vector3(minX, 0f, z), count);
                    count += PlaceFrameTree(theme, material, root, new Vector3(maxX, 0f, z), count);
                }
            }
            return count;
        }

        private const float FrameHashScale = 4f;

        private static int PlaceFrameTree(MapThemeConfig theme, Material material, Transform root, Vector3 position, int n)
        {
            int hash = CellHash(Mathf.RoundToInt(position.x * FrameHashScale), Mathf.RoundToInt(position.z * FrameHashScale));
            Mesh mesh = theme.FrameTrees[hash % theme.FrameTrees.Length];
            if (mesh == null) return 0;
            float scale = 1f + ((hash >> 8 & 0xFF) / 255f * 2f - 1f) * theme.FrameTreeScaleJitter;
            GameObject go = CreatePropObject("FrameTree_" + n, root, mesh, material, position, (hash >> 4 & 3) * QuarterTurn);
            go.transform.localScale = new Vector3(scale, scale, scale);
            return 1;
        }

        public static GameObject CreatePropObject(string name, Transform parent, Mesh mesh, Material material, Vector3 position, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// A.3f: one stone block renderer per wall cell that is not a prop: a variant and a quarter turn by a hash that is the same
        /// for a cell and its mirror on the other half of the map (both teams see the same blocks). View only.
        /// </summary>
        private void PlaceWallBlocks(ViewCatalog catalog, in TileGrid grid, Transform root, bool[] propCells)
        {
            Mesh[] variants = catalog.WallBlockVariants;
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    int index = grid.IndexOf(c, r);
                    if (grid.Tiles[index] != TileType.Wall || (propCells != null && propCells[index])) continue;
                    int mc = grid.Width - 1 - c, mr = grid.Height - 1 - r;
                    bool mirrored = r * grid.Width + c > mr * grid.Width + mc;
                    int hash = mirrored ? CellHash(mc, mr) : CellHash(c, r);
                    float yaw = (hash >> 4 & 3) * 90f + (mirrored ? 180f : 0f);
                    var go = new GameObject("Block_" + c + "_" + r);
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(WorldCoords.ToWorld(new GridCell(c, r).Center), Quaternion.Euler(0f, yaw, 0f));
                    go.AddComponent<MeshFilter>().sharedMesh = variants[hash % variants.Length];
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = catalog.ObstacleProp;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    WallBlockCount++;
                }
            }
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

        internal static int CellHash(int c, int r)
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
