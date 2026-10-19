using Arena.Core.Map;
using Unity.Collections;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Soft shadows of walls on the ground without shadow maps (A.3b, D-125): a small R8 mask of the map, <see cref="TexelsPerCell"/>²
    /// texels per cell with a margin of <see cref="PaddingCells"/> cells (the meadow around the map gets the shadows of edge walls too);
    /// texel = relative height of the caster (wall 1, crate/box and bush lower, open 0), bilinear. Shaders (ArenaEnv.hlsl) march
    /// towards the main light and darken the ground under a caster. Built at match start; a broken crate clears its texels
    /// (no allocations: the texture's raw data is written in place).
    /// </summary>
    public sealed class MapShadowMask
    {
        public const int PaddingCells = 2;

        private static readonly int MaskId = Shader.PropertyToID("_ArenaShadowMask");
        private static readonly int RectId = Shader.PropertyToID("_ArenaShadowMaskRect");
        private static readonly int ParamsId = Shader.PropertyToID("_ArenaShadowParams");

        private readonly Texture2D _texture;

        public int TexelsPerCell { get; }
        public int Width { get; }
        public int Height { get; }
        public Texture2D Texture => _texture;

        public MapShadowMask(in TileGrid grid, in MapMeshStyle style)
        {
            TexelsPerCell = Mathf.Max(1, style.ShadowTexelsPerCell);
            byte[] data = Build(in grid, in style, out int w, out int h);
            Width = w;
            Height = h;
            _texture = new Texture2D(w, h, TextureFormat.R8, false, true)
            {
                name = "MapShadowMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            _texture.SetPixelData(data, 0);
            _texture.Apply(false, false);
        }

        /// <summary>
        /// Mask bytes, row-major from the south-west corner: <c>(grid.Width + 2·padding) · k</c> × <c>(grid.Height + 2·padding) · k</c>.
        /// Walls 255, breakable crates <see cref="MapMeshStyle.CrateShadowHeight"/>, berry boxes <see cref="MapMeshStyle.BoxShadowHeight"/>, bushes
        /// <see cref="MapMeshStyle.BushShadowHeight"/>, the rest 0.
        /// </summary>
        public static byte[] Build(in TileGrid grid, in MapMeshStyle style, out int width, out int height)
        {
            int k = Mathf.Max(1, style.ShadowTexelsPerCell);
            width = (grid.Width + 2 * PaddingCells) * k;
            height = (grid.Height + 2 * PaddingCells) * k;
            var data = new byte[width * height];
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    byte v = CasterValue(grid.Get(c, r), in style);
                    if (v != 0) Fill(data, width, c, r, k, v);
                }
            }
            return data;
        }

        public static byte CasterValue(TileType tile, in MapMeshStyle style)
        {
            switch (tile)
            {
                case TileType.Wall: return 255;
                case TileType.BreakableWall: return (byte)Mathf.RoundToInt(Mathf.Clamp01(style.CrateShadowHeight) * 255f);
                case TileType.BerryBox: return (byte)Mathf.RoundToInt(Mathf.Clamp01(style.BoxShadowHeight) * 255f);
                case TileType.Bush: return (byte)Mathf.RoundToInt(Mathf.Clamp01(style.BushShadowHeight) * 255f);
                default: return 0;
            }
        }

        /// <summary>Clears the texels of a cell (a crate broke). No allocations.</summary>
        public void ClearCell(int column, int row)
        {
            NativeArray<byte> raw = _texture.GetPixelData<byte>(0);
            int k = TexelsPerCell;
            int x0 = (column + PaddingCells) * k, y0 = (row + PaddingCells) * k;
            for (int y = y0; y < y0 + k && y < Height; y++)
                for (int x = x0; x < x0 + k && x < Width; x++)
                    raw[y * Width + x] = 0;
            _texture.Apply(false, false);
        }

        /// <summary>Publishes the mask to the environment shaders: casters <paramref name="casterHeight"/> high.</summary>
        public void Apply(float casterHeight, in MapMeshStyle style)
        {
            float cells = Width / (float)TexelsPerCell, rows = Height / (float)TexelsPerCell;
            Shader.SetGlobalTexture(MaskId, _texture);
            Shader.SetGlobalVector(RectId, new Vector4(-PaddingCells, -PaddingCells, 1f / cells, 1f / rows));
            Shader.SetGlobalVector(ParamsId, new Vector4(casterHeight, style.ShadowStrength, style.ShadowSoftness, 0f));
        }

        /// <summary>Turns the wall shadows off (map gone).</summary>
        public static void Clear() => Shader.SetGlobalVector(ParamsId, Vector4.zero);

        public void Destroy()
        {
            if (_texture == null) return;
            if (Application.isPlaying) Object.Destroy(_texture);
            else Object.DestroyImmediate(_texture);
        }

        private static void Fill(byte[] data, int width, int c, int r, int k, byte v)
        {
            int x0 = (c + PaddingCells) * k, y0 = (r + PaddingCells) * k;
            for (int y = y0; y < y0 + k; y++)
                for (int x = x0; x < x0 + k; x++)
                    data[y * width + x] = v;
        }
    }
}
