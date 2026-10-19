using Arena.Core.Map;
using Unity.Collections;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Soft shadows and contact occlusion on the ground without shadow maps (A.3b, D-125; A.3e, D-130): a small RG8 mask of the map,
    /// <see cref="TexelsPerCell"/>² texels per cell with a margin of <see cref="PaddingCells"/> cells (the meadow around the map gets
    /// the shadows of edge casters too).
    /// R = relative caster height (wall 1, crate/prop/basket/bush lower, open 0) rasterised by footprint shape (a square with rounded
    /// open corners for blocks, crates and bushes, a disc for round props and the berry basket) and blurred (soft shadow edges);
    /// shaders (ArenaEnv.hlsl) march towards the light and darken the ground under a caster.
    /// G = contact occlusion: the footprints blurred wide, so every object "stands" on the ground.
    /// Built at match start (allocations there only); a broken crate clears its footprint and re-blurs the 3 × 3 cells around it
    /// with buffers allocated up front — no allocations in the match.
    /// </summary>
    public sealed class MapShadowMask
    {
        public const int PaddingCells = 2;
        private const int KernelOne = 1 << 12;

        private static readonly int MaskId = Shader.PropertyToID("_ArenaShadowMask");
        private static readonly int RectId = Shader.PropertyToID("_ArenaShadowMaskRect");
        private static readonly int ParamsId = Shader.PropertyToID("_ArenaShadowParams");

        private readonly Texture2D _texture;
        private readonly byte[] _height;   // unblurred footprints, one byte per texel
        private readonly byte[] _ao;
        private readonly byte[] _packed;   // blurred RG pairs (the texture's data)
        private readonly int[] _scratch;
        private readonly int[] _heightKernel;
        private readonly int[] _aoKernel;

        public int TexelsPerCell { get; }
        public int Width { get; }
        public int Height { get; }
        public Texture2D Texture => _texture;

        public MapShadowMask(in TileGrid grid, in MapMeshStyle style, bool[] roundCells = null)
            : this(in grid, in style, roundCells, true)
        {
        }

        private MapShadowMask(in TileGrid grid, in MapMeshStyle style, bool[] roundCells, bool createTexture)
        {
            TexelsPerCell = Mathf.Max(1, style.ShadowTexelsPerCell);
            int k = TexelsPerCell;
            Width = (grid.Width + 2 * PaddingCells) * k;
            Height = (grid.Height + 2 * PaddingCells) * k;
            _height = new byte[Width * Height];
            _ao = new byte[Width * Height];
            _packed = new byte[Width * Height * 2];
            _scratch = new int[Width * Height];
            _heightKernel = Kernel(style.ShadowBlur * k);
            _aoKernel = Kernel(style.ContactAoBlur * k);
            Rasterise(in grid, in style, roundCells, k, Width, _height, _ao);
            Blur(_height, _heightKernel, 0, 0, 0, Width, Height);
            Blur(_ao, _aoKernel, 1, 0, 0, Width, Height);
            if (!createTexture) return;
            _texture = new Texture2D(Width, Height, TextureFormat.RG16, false, true)
            {
                name = "MapShadowMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
            _texture.SetPixelData(_packed, 0);
            _texture.Apply(false, false);
        }

        /// <summary>
        /// Mask texels as RG pairs, row-major from the south-west corner: <c>(grid.Width + 2·padding) · k</c> ×
        /// <c>(grid.Height + 2·padding) · k</c> texels, R = blurred caster height, G = contact occlusion (tests, tools).
        /// </summary>
        public static byte[] Build(in TileGrid grid, in MapMeshStyle style, out int width, out int height, bool[] roundCells = null)
        {
            var mask = new MapShadowMask(in grid, in style, roundCells, false);
            width = mask.Width;
            height = mask.Height;
            return mask._packed;
        }

        public static byte CasterValue(TileType tile, in MapMeshStyle style)
        {
            switch (tile)
            {
                case TileType.Wall: return 255;
                case TileType.BreakableWall: return ToByte(style.CrateShadowHeight);
                case TileType.BerryBox: return ToByte(style.BoxShadowHeight);
                case TileType.Bush: return ToByte(style.BushShadowHeight);
                default: return 0;
            }
        }

        /// <summary>Clears the footprint of a cell (a crate broke) and re-blurs its 3 × 3 neighbourhood. No allocations.</summary>
        public void ClearCell(int column, int row)
        {
            int k = TexelsPerCell;
            int x0 = (column + PaddingCells) * k, y0 = (row + PaddingCells) * k;
            for (int y = y0; y < y0 + k && y < Height; y++)
            {
                for (int x = x0; x < x0 + k && x < Width; x++)
                {
                    _height[y * Width + x] = 0;
                    _ao[y * Width + x] = 0;
                }
            }
            int rx0 = Mathf.Max(0, x0 - k), ry0 = Mathf.Max(0, y0 - k);
            int rx1 = Mathf.Min(Width, x0 + 2 * k), ry1 = Mathf.Min(Height, y0 + 2 * k);
            Blur(_height, _heightKernel, 0, rx0, ry0, rx1, ry1);
            Blur(_ao, _aoKernel, 1, rx0, ry0, rx1, ry1);
            NativeArray<byte> raw = _texture.GetPixelData<byte>(0);
            raw.CopyFrom(_packed);
            _texture.Apply(false, false);
        }

        /// <summary>Blurred caster height (R) at a texel, 0–255 (tests).</summary>
        public byte HeightAt(int x, int y) => _packed[(y * Width + x) * 2];

        /// <summary>Contact occlusion (G) at a texel, 0–255 (tests).</summary>
        public byte OcclusionAt(int x, int y) => _packed[(y * Width + x) * 2 + 1];

        /// <summary>Publishes the mask to the environment shaders: casters <paramref name="casterHeight"/> high.</summary>
        public void Apply(float casterHeight, in MapMeshStyle style)
        {
            float cells = Width / (float)TexelsPerCell, rows = Height / (float)TexelsPerCell;
            Shader.SetGlobalTexture(MaskId, _texture);
            Shader.SetGlobalVector(RectId, new Vector4(-PaddingCells, -PaddingCells, 1f / cells, 1f / rows));
            Shader.SetGlobalVector(ParamsId, new Vector4(casterHeight, style.ShadowStrength, style.ShadowSoftness, style.ContactAoStrength));
        }

        /// <summary>Turns the ground shadows off (map gone).</summary>
        public static void Clear() => Shader.SetGlobalVector(ParamsId, Vector4.zero);

        public void Destroy()
        {
            if (_texture == null) return;
            if (Application.isPlaying) Object.Destroy(_texture);
            else Object.DestroyImmediate(_texture);
        }

        private static byte ToByte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        /// <summary>Footprints of all casters: heights into <paramref name="height"/>, occlusion weights into <paramref name="ao"/>.</summary>
        private static void Rasterise(in TileGrid grid, in MapMeshStyle style, bool[] roundCells, int k, int width, byte[] height, byte[] ao)
        {
            byte bushAo = ToByte(style.BushAoWeight);
            for (int r = 0; r < grid.Height; r++)
            {
                for (int c = 0; c < grid.Width; c++)
                {
                    TileType t = grid.Get(c, r);
                    int index = grid.IndexOf(c, r);
                    bool round = roundCells != null && index < roundCells.Length && roundCells[index];
                    byte h;
                    byte w = 255;
                    switch (t)
                    {
                        case TileType.Wall: h = round ? ToByte(style.PropShadowHeight) : (byte)255; break;
                        case TileType.BreakableWall: h = CasterValue(t, in style); break;
                        case TileType.BerryBox: h = CasterValue(t, in style); round = true; break;
                        case TileType.Bush: h = CasterValue(t, in style); w = bushAo; break;
                        default: continue;
                    }
                    // Blocks and bushes join their own kind: no inset or rounding on a side towards a neighbour of that kind.
                    bool joins = t == TileType.Wall || t == TileType.Bush;
                    bool west = joins && !round && Joins(in grid, roundCells, c - 1, r, t);
                    bool east = joins && !round && Joins(in grid, roundCells, c + 1, r, t);
                    bool south = joins && !round && Joins(in grid, roundCells, c, r - 1, t);
                    bool north = joins && !round && Joins(in grid, roundCells, c, r + 1, t);
                    FillFootprint(height, ao, width, c, r, k, h, w, round, style.ObstaclePropRadius, style.FootprintInset,
                        style.FootprintCorner, west, east, south, north);
                }
            }
        }

        private static bool Joins(in TileGrid grid, bool[] roundCells, int c, int r, TileType type)
        {
            if (!grid.InBounds(c, r) || grid.Get(c, r) != type) return false;
            int i = grid.IndexOf(c, r);
            return roundCells == null || i >= roundCells.Length || !roundCells[i];
        }

        private static void FillFootprint(byte[] height, byte[] ao, int width, int c, int r, int k, byte h, byte w, bool round,
            float radius, float inset, float corner, bool west, bool east, bool south, bool north)
        {
            // Box in cell units, extended past a joined side so its rounding never notches the shared edge.
            float minX = west ? -1f : inset, maxX = east ? 2f : 1f - inset;
            float minY = south ? -1f : inset, maxY = north ? 2f : 1f - inset;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f, hx = (maxX - minX) * 0.5f, hy = (maxY - minY) * 0.5f;
            float rc = Mathf.Min(corner, Mathf.Min(hx, hy));
            int x0 = (c + PaddingCells) * k, y0 = (r + PaddingCells) * k;
            for (int y = 0; y < k; y++)
            {
                for (int x = 0; x < k; x++)
                {
                    float u = (x + 0.5f) / k, v = (y + 0.5f) / k;
                    float d;
                    if (round)
                    {
                        d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) - radius;
                    }
                    else
                    {
                        float qx = Mathf.Abs(u - cx) - hx + rc, qy = Mathf.Abs(v - cy) - hy + rc;
                        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                        d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rc;
                    }
                    float coverage = Mathf.Clamp01(0.5f - d * k);
                    if (coverage <= 0f) continue;
                    int i = (y0 + y) * width + x0 + x;
                    height[i] = (byte)Mathf.Max(height[i], Mathf.RoundToInt(h * coverage));
                    ao[i] = (byte)Mathf.Max(ao[i], Mathf.RoundToInt(w * coverage));
                }
            }
        }

        /// <summary>Normalised integer Gaussian (weights sum to <see cref="KernelOne"/>); sigma in texels, 0 = identity.</summary>
        private static int[] Kernel(float sigma)
        {
            if (sigma < 0.3f) return new[] { KernelOne };
            int radius = Mathf.CeilToInt(sigma * 2.5f);
            var weights = new float[radius * 2 + 1];
            float sum = 0f;
            for (int i = -radius; i <= radius; i++)
            {
                weights[i + radius] = Mathf.Exp(-(i * i) / (2f * sigma * sigma));
                sum += weights[i + radius];
            }
            var kernel = new int[weights.Length];
            int total = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                kernel[i] = Mathf.RoundToInt(weights[i] / sum * KernelOne);
                total += kernel[i];
            }
            kernel[radius] += KernelOne - total;
            return kernel;
        }

        /// <summary>
        /// Separable blur of <paramref name="src"/> into channel <paramref name="channel"/> of the packed RG data over the texel
        /// rectangle [x0, x1) × [y0, y1) (the horizontal pass also covers the rows the vertical pass reads). Clamped at the mask edges.
        /// </summary>
        private void Blur(byte[] src, int[] kernel, int channel, int x0, int y0, int x1, int y1)
        {
            int radius = kernel.Length / 2;
            int hy0 = Mathf.Max(0, y0 - radius), hy1 = Mathf.Min(Height, y1 + radius);
            for (int y = hy0; y < hy1; y++)
            {
                int row = y * Width;
                for (int x = x0; x < x1; x++)
                {
                    int acc = 0;
                    for (int j = -radius; j <= radius; j++)
                    {
                        int sx = x + j;
                        sx = sx < 0 ? 0 : sx >= Width ? Width - 1 : sx;
                        acc += src[row + sx] * kernel[j + radius];
                    }
                    _scratch[row + x] = acc;
                }
            }
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    long acc = 0;
                    for (int j = -radius; j <= radius; j++)
                    {
                        int sy = y + j;
                        sy = sy < hy0 ? hy0 : sy >= hy1 ? hy1 - 1 : sy;
                        acc += (long)_scratch[sy * Width + x] * kernel[j + radius];
                    }
                    int v = (int)((acc + ((long)KernelOne * KernelOne >> 1)) / ((long)KernelOne * KernelOne));
                    _packed[(y * Width + x) * 2 + channel] = (byte)(v > 255 ? 255 : v);
                }
            }
        }
    }
}
