using Arena.Core.Map;
using Arena.Data;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// D-142 (Snow Valley feedback 1): the themed frame of the arena and small ground details, view only — a low snow-brick wall with
    /// posts and bulb garlands on all four sides (the near side lower, so it does not hide the first row), cabins / woodpiles / snow
    /// mounds behind the left, right and far walls, snow lumps at the foot of the frame wall and of unbreakable decor walls. Built once
    /// by <see cref="MapView"/> from <see cref="MapThemeConfig"/>; every object is static (batched with the map).
    /// </summary>
    public static class MapFrameDecor
    {
        private const float QuarterTurn = 90f;
        private const float LumpJitter = 0.3f;
        private const float ExtraJitter = 0.35f;
        private const float ExtraScaleJitter = 0.12f;
        private const float MoundYawJitter = 40f;

        /// <summary>Places the frame wall, posts and garlands; returns the objects placed (0 if the theme has no frame wall).</summary>
        public static int PlaceWall(MapThemeConfig theme, Material material, in TileGrid grid, Transform root)
        {
            if (theme.FrameWall == null) return 0;
            float off = theme.FrameWallOffset, w = grid.Width, h = grid.Height;
            int count = 0;
            for (int side = 0; side < 4; side++)
            {
                Side(side, off, w, h, out Vector3 start, out Vector3 dir, out float yaw, out float length);
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                float heightScale = side == NearSide ? theme.FrameNearHeight : 1f;
                int segments = Mathf.Max(1, Mathf.RoundToInt(length / theme.FrameWallSegment));
                float segment = length / segments;
                for (int i = 0; i < segments; i++)
                {
                    GameObject go = MapView.CreatePropObject("FrameWall_" + side + "_" + i, root, theme.FrameWall, material,
                        start + dir * ((i + 0.5f) * segment), yaw);
                    go.transform.localScale = new Vector3(segment / theme.FrameWallSegment, heightScale, 1f);
                    count++;
                }
                int spans = Mathf.Max(1, Mathf.RoundToInt(length / theme.FramePostSpacing));
                float span = length / spans;
                for (int i = 0; i <= spans; i++)
                {
                    // Corner posts belong to the far and near sides.
                    bool corner = i == 0 || i == spans;
                    if (theme.FramePost != null && !(corner && (side == LeftSide || side == RightSide)))
                    {
                        GameObject post = MapView.CreatePropObject("FramePost_" + side + "_" + i, root, theme.FramePost, material, start + dir * (i * span), yaw);
                        post.transform.localScale = new Vector3(1f, heightScale, 1f);
                        count++;
                    }
                    if (theme.FrameGarland == null || i == spans) continue;
                    GameObject garland = MapView.CreatePropObject("FrameGarland_" + side + "_" + i, root, theme.FrameGarland, material, start + dir * (i * span), yaw);
                    garland.transform.localScale = new Vector3(span / theme.FramePostSpacing, heightScale, 1f);
                    count++;
                }
                if (theme.WallLump == null || theme.WallLumpChance <= 0f) continue;
                Vector3 field = rotation * Vector3.back;
                int steps = Mathf.RoundToInt(length);
                for (int i = 0; i < steps; i++)
                {
                    int hash = MapView.CellHash(side * 4099 + i, 7919);
                    if (Unit(hash) >= theme.WallLumpChance) continue;
                    float along = (i + 0.5f + (Unit(hash >> 8) - 0.5f) * LumpJitter) * (length / steps);
                    Vector3 p = start + dir * along + field * theme.FrameLumpDistance;
                    MapView.CreatePropObject("FrameLump_" + side + "_" + i, root, theme.WallLump, material, p, yaw + (hash >> 16 & 1) * 180f);
                    count++;
                }
            }
            return count;
        }

        /// <summary>Cabins, woodpiles and mounds behind the left, right and far walls, fronts to the field; returns the objects placed.</summary>
        public static int PlaceExtras(MapThemeConfig theme, Material material, in TileGrid grid, Transform root)
        {
            Mesh[] extras = theme.FrameExtras;
            if (extras == null || extras.Length == 0 || theme.FrameExtraSpacing <= 0f) return 0;
            float off = theme.FrameExtraOffset, w = grid.Width, h = grid.Height;
            int count = 0;
            for (int side = 0; side < 4; side++)
            {
                if (side == NearSide) continue;
                Side(side, off, w, h, out Vector3 start, out Vector3 dir, out float yaw, out float length);
                for (float along = theme.FrameExtraSpacing * 0.5f; along < length; along += theme.FrameExtraSpacing)
                {
                    int hash = MapView.CellHash(side * 131 + Mathf.RoundToInt(along * 4f), 3301);
                    Mesh mesh = extras[hash % extras.Length];
                    if (mesh == null) continue;
                    float jitter = (Unit(hash >> 6) - 0.5f) * 2f * ExtraJitter * theme.FrameExtraSpacing;
                    float turn = mesh == extras[extras.Length - 1] ? (Unit(hash >> 12) - 0.5f) * 2f * MoundYawJitter : 0f;
                    GameObject go = MapView.CreatePropObject("FrameExtra_" + side + "_" + count, root, mesh, material,
                        start + dir * Mathf.Clamp(along + jitter, 0f, length), yaw + turn);
                    float scale = 1f + (Unit(hash >> 18) - 0.5f) * 2f * ExtraScaleJitter;
                    go.transform.localScale = new Vector3(scale, scale, scale);
                    count++;
                }
            }
            return count;
        }

        /// <summary>Snow lumps on the open floor sides of unbreakable decor walls (forts, piers, drifts) by a cell hash.</summary>
        public static int PlaceWallLumps(MapThemeConfig theme, Material material, in TileGrid grid, string[] decor, Transform root)
        {
            if (theme.WallLump == null || theme.WallLumpChance <= 0f || decor == null) return 0;
            int count = 0;
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    if (grid.Get(c, r) != TileType.Wall) continue;
                    char mark = MapDecor.At(decor, c, r);
                    if (mark != MapDecor.Fort && mark != MapDecor.Lantern && mark != MapDecor.Drift) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int dc = d == 0 ? -1 : d == 1 ? 1 : 0, dr = d == 2 ? -1 : d == 3 ? 1 : 0;
                        if (!grid.InBounds(c + dc, r + dr) || grid.Get(c + dc, r + dr) != TileType.Floor) continue;
                        int hash = MapView.CellHash(c * 4 + d, r);
                        if (Unit(hash) >= theme.WallLumpChance) continue;
                        float slide = (Unit(hash >> 8) - 0.5f) * 2f * LumpJitter;
                        var p = new Vector3(c + 0.5f + dc * 0.5f + dr * slide, 0f, r + 0.5f + dr * 0.5f + dc * slide);
                        float yaw = (dc != 0 ? QuarterTurn : 0f) + (hash >> 16 & 1) * 180f;
                        MapView.CreatePropObject("Lump_" + c + "_" + r + "_" + d, root, theme.WallLump, material, p, yaw);
                        count++;
                    }
                }
            }
            return count;
        }

        private const int FarSide = 0, NearSide = 1, LeftSide = 2, RightSide = 3;

        /// <summary>Side line <paramref name="offset"/> outside the arena: start, direction (local +X of the props), yaw (local −Z faces
        /// the field) and length.</summary>
        private static void Side(int side, float offset, float w, float h, out Vector3 start, out Vector3 dir, out float yaw, out float length)
        {
            switch (side)
            {
                case FarSide: start = new Vector3(-offset, 0f, h + offset); dir = Vector3.right; yaw = 0f; length = w + 2f * offset; break;
                case NearSide: start = new Vector3(w + offset, 0f, -offset); dir = Vector3.left; yaw = 2f * QuarterTurn; length = w + 2f * offset; break;
                case LeftSide: start = new Vector3(-offset, 0f, -offset); dir = Vector3.forward; yaw = -QuarterTurn; length = h + 2f * offset; break;
                default: start = new Vector3(w + offset, 0f, h + offset); dir = Vector3.back; yaw = QuarterTurn; length = h + 2f * offset; break;
            }
        }

        private static float Unit(int hash) => (hash & 0xFF) / 255f;
    }
}
