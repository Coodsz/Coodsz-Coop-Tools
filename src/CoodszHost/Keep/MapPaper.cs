using System.Windows.Media;
using System.Windows.Media.Imaging;
using BannerlordCoopLauncher.Core.Keep;

namespace CoodszHost.Keep;

/// <summary>
/// The drawn map under the markers, in the style of an old paper map. With the real terrain read
/// from the server (<see cref="MapTerrain"/>, coodsz.terrain) it draws the real coast and seas as a
/// muted blue grey ink wash with an inked coastline, rivers, and faint tints for forests, mountains,
/// deserts and snow. Without it (old server tools, or not read yet) the whole map is plain
/// parchment: nothing about water is guessed. Kingdoms are soft tints on the land with thin dashed
/// borders, fading back to parchment away from their settlements (<see cref="LiveMap.Territory"/>).
/// Every size is in game units, so the whole map picture and the sharper picture of the part in
/// view line up.
/// </summary>
public static class MapPaper
{
    /// <summary>The colour around the picture (the canvas edge).</summary>
    public static readonly Color PaperColor = Color.FromRgb(0xCF, 0xC1, 0x9A);

    private static readonly (double R, double G, double B) Paper = (0xCF, 0xC1, 0x9A);
    private static readonly (double R, double G, double B) Ink = (0x3E, 0x30, 0x20);
    private static readonly (double R, double G, double B) Shore = (0x9A, 0xA6, 0xA0);
    private static readonly (double R, double G, double B) Sea = (0x83, 0x95, 0x96);
    private static readonly (double R, double G, double B) DeepSea = (0x72, 0x86, 0x8A);
    private static readonly (double R, double G, double B) RiverInk = (0x6A, 0x80, 0x88);
    private static readonly (double R, double G, double B) Forest = (0xA6, 0xB0, 0x86);
    private static readonly (double R, double G, double B) Mountain = (0xA4, 0x94, 0x78);
    private static readonly (double R, double G, double B) Desert = (0xE6, 0xCC, 0x92);
    private static readonly (double R, double G, double B) Snow = (0xEF, 0xEC, 0xE2);
    private static readonly (double R, double G, double B) Steppe = (0xCC, 0xC6, 0x8E);
    private static readonly (double R, double G, double B) Swamp = (0xA0, 0xA4, 0x88);
    private static readonly (double R, double G, double B) Beach = (0xE4, 0xD6, 0xAA);

    /// <summary>The picture of <paramref name="bounds"/>, <paramref name="columns"/> pixels wide (row 0 is north).</summary>
    public static BitmapSource Picture(MapData map, MapBounds bounds, int columns, int rows, double reach, bool territory = true, MapTerrain? terrain = null)
    {
        var cells = LiveMap.Territory(map, bounds, columns, rows, reach);
        var pixels = Render(cells, columns, rows, bounds, reach, territory, terrain);
        var picture = BitmapSource.Create(columns, rows, 96, 96, PixelFormats.Bgra32, null, pixels, columns * 4);
        picture.Freeze();
        return picture;
    }

    public static uint[] Render(uint[] cells, int w, int h, MapBounds bounds, double reach, bool territory, MapTerrain? terrain = null)
    {
        var n = w * h;
        var px = bounds.Width / Math.Max(1, w);
        var pxY = bounds.Height / Math.Max(1, h);

        // The real terrain under each pixel, softened over about one terrain cell so the coast and
        // the forests follow curves instead of the grid's steps.
        float[]? water = null, deep = null, river = null, forest = null, mountain = null, desert = null, snow = null, steppe = null, swamp = null, beach = null;
        if (terrain != null)
        {
            water = new float[n];
            deep = new float[n];
            river = new float[n];
            forest = new float[n];
            mountain = new float[n];
            desert = new float[n];
            snow = new float[n];
            steppe = new float[n];
            swamp = new float[n];
            beach = new float[n];
            for (var row = 0; row < h; row++)
            {
                var wy = bounds.MaxY - (row + 0.5) * pxY;
                for (var col = 0; col < w; col++)
                {
                    var i = row * w + col;
                    switch (terrain.KindAt(bounds.MinX + (col + 0.5) * px, wy))
                    {
                        case TerrainKind.Water: water[i] = 1; break;
                        case TerrainKind.DeepWater: water[i] = 1; deep[i] = 1; break;
                        case TerrainKind.River: river[i] = 1; break;
                        case TerrainKind.Forest: forest[i] = 1; break;
                        case TerrainKind.Mountain: mountain[i] = 1; break;
                        case TerrainKind.Impassable: mountain[i] = 0.6f; break;
                        case TerrainKind.Desert: desert[i] = 1; break;
                        case TerrainKind.Snow: snow[i] = 1; break;
                        case TerrainKind.Steppe: steppe[i] = 1; break;
                        case TerrainKind.Swamp: swamp[i] = 1; break;
                        case TerrainKind.Beach: beach[i] = 1; break;
                    }
                }
            }

            var cell = terrain.CellSize / px;
            var soft = Math.Max(1, (int)Math.Round(cell * 0.6));
            water = Blur(water, w, h, soft, 2);
            deep = Blur(deep, w, h, Math.Max(1, (int)Math.Round(cell * 2)), 2);
            river = Blur(river, w, h, Math.Max(1, (int)Math.Round(cell * 0.35)), 1);
            forest = Blur(forest, w, h, soft, 2);
            mountain = Blur(mountain, w, h, soft, 2);
            desert = Blur(desert, w, h, soft, 2);
            snow = Blur(snow, w, h, soft, 2);
            steppe = Blur(steppe, w, h, soft, 2);
            swamp = Blur(swamp, w, h, soft, 2);
            beach = Blur(beach, w, h, soft, 2);
        }

        // Kingdom colours, softened across the borders and fading out where no settlement is near.
        var present = new float[n];
        var tintR = new float[n];
        var tintG = new float[n];
        var tintB = new float[n];
        for (var i = 0; i < n; i++)
        {
            var c = cells[i];
            if (c == 0)
                continue;
            var (r, g, b) = Muted(c);
            present[i] = 1;
            tintR[i] = (float)r;
            tintG[i] = (float)g;
            tintB[i] = (float)b;
        }

        var tintBlur = Math.Max(1, (int)Math.Round(0.08 * reach / px));
        var fadeBlur = Math.Max(1, (int)Math.Round(0.25 * reach / px));
        var weight = Blur(present, w, h, tintBlur, 2);
        var fade = Blur(present, w, h, fadeBlur, 2);
        tintR = Blur(tintR, w, h, tintBlur, 2);
        tintG = Blur(tintG, w, h, tintBlur, 2);
        tintB = Blur(tintB, w, h, tintBlur, 2);
        var dash = 0.11 * reach;

        var pixels = new uint[n];
        for (var row = 0; row < h; row++)
        {
            var wy = bounds.MaxY - (row + 0.5) * pxY;
            for (var col = 0; col < w; col++)
            {
                var i = row * w + col;
                var wx = bounds.MinX + (col + 0.5) * px;
                var grain = Noise(wx / (0.6 * reach), wy / (0.6 * reach)) * 0.6 + Noise(wx / (0.13 * reach), wy / (0.13 * reach)) * 0.4;
                var color = Shade(Paper, (grain - 0.5) * 0.12);
                var land = 1.0;
                var edgeFade = 1.0;

                if (terrain != null)
                {
                    color = Lerp(color, Multiply(color, Forest), Math.Clamp(forest![i] * 0.3, 0, 1));
                    color = Lerp(color, Multiply(color, Mountain), Math.Clamp(mountain![i] * 0.32, 0, 1));
                    color = Lerp(color, Desert, Math.Clamp(desert![i] * 0.35, 0, 1));
                    color = Lerp(color, Snow, Math.Clamp(snow![i] * 0.3, 0, 1));
                    color = Lerp(color, Multiply(color, Steppe), Math.Clamp(steppe![i] * 0.25, 0, 1));
                    color = Lerp(color, Multiply(color, Swamp), Math.Clamp(swamp![i] * 0.4, 0, 1));
                    color = Lerp(color, Beach, Math.Clamp(beach![i] * 0.35, 0, 1));
                    // The sea fades out toward the edge of the terrain grid, so a world wider than the scene's borders
                    // (settlements a map mod put outside them) does not end in a hard line of sea.
                    var tb = terrain.Bounds;
                    var edge = Math.Min(Math.Min(wx - tb.MinX, tb.MaxX - wx), Math.Min(wy - tb.MinY, tb.MaxY - wy));
                    edgeFade = SmoothStep(0, 0.04 * tb.Width, edge);
                    land = 1 - SmoothStep(0.35, 0.65, water![i]) * edgeFade;
                }

                // Land: tinted by the kingdom that holds it, fading to plain paper away from its settlements.
                if (territory && weight[i] > 0.001f)
                {
                    var tint = (tintR[i] / weight[i], tintG[i] / weight[i], tintB[i] / weight[i]);
                    var strength = 0.45 * Math.Clamp(weight[i] * 1.3, 0, 1) * SmoothStep(0.05, 0.6, fade[i]) * land;
                    color = Lerp(color, Multiply(color, tint), strength);
                }

                // Dashed borders between kingdoms, thin, on the land only, fading with the tint.
                if (territory && cells[i] != 0 && land > 0.6 && IsBorder(cells, w, h, col, row))
                {
                    var t = (wx + wy) / dash;
                    if (t - Math.Floor(t) < 0.55)
                        color = Lerp(color, Ink, 0.42 * SmoothStep(0.15, 0.7, fade[i]));
                }

                if (terrain != null)
                {
                    // Rivers as thin ink washes; the sea as a muted wash, darker out at open sea.
                    color = Lerp(color, RiverInk, Math.Clamp(river![i] * 1.4, 0, 0.75) * land);
                    var sea = Lerp(Shore, Sea, Math.Clamp(water![i] * 1.6 - 0.6, 0, 1));
                    sea = Lerp(sea, DeepSea, Math.Clamp(deep![i], 0, 1));
                    sea = Shade(sea, (grain - 0.5) * 0.05);
                    color = Lerp(sea, color, land);

                    // The inked coastline where the softened water crosses the middle, about one pixel wide.
                    var m = water[i];
                    if (m > 0.05f && m < 0.95f && col > 0 && row > 0 && col < w - 1 && row < h - 1)
                    {
                        var gx = (water[i + 1] - water[i - 1]) * 0.5;
                        var gy = (water[i + w] - water[i - w]) * 0.5;
                        var g = Math.Sqrt(gx * gx + gy * gy);
                        if (g > 0.01)
                        {
                            var coast = Math.Clamp(1 - Math.Abs(m - 0.5) / (g * 1.3), 0, 1);
                            color = Lerp(color, Ink, coast * 0.7 * edgeFade);
                        }
                    }
                }

                pixels[i] = 0xFF000000u | ((uint)Math.Clamp(color.R, 0, 255) << 16) | ((uint)Math.Clamp(color.G, 0, 255) << 8) | (uint)Math.Clamp(color.B, 0, 255);
            }
        }

        return pixels;
    }

    private static bool IsBorder(uint[] cells, int w, int h, int col, int row)
    {
        var c = cells[row * w + col];
        bool Other(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && cells[y * w + x] != 0 && cells[y * w + x] != c;
        return Other(col + 1, row) || Other(col, row + 1);
    }

    /// <summary>A kingdom colour pulled toward grey and lightened, so the tints stay calm on the paper.</summary>
    private static (double R, double G, double B) Muted(uint argb)
    {
        double r = (argb >> 16) & 0xFF, g = (argb >> 8) & 0xFF, b = argb & 0xFF;
        var grey = 0.3 * r + 0.59 * g + 0.11 * b;
        r = grey + (r - grey) * 0.7;
        g = grey + (g - grey) * 0.7;
        b = grey + (b - grey) * 0.7;
        return (r + (255 - r) * 0.3, g + (255 - g) * 0.3, b + (255 - b) * 0.3);
    }

    private static (double R, double G, double B) Multiply((double R, double G, double B) a, (double R, double G, double B) b) =>
        (a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255);

    private static (double R, double G, double B) Lerp((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static (double R, double G, double B) Shade((double R, double G, double B) a, double amount) =>
        (a.R * (1 + amount), a.G * (1 + amount), a.B * (1 + amount));

    private static double SmoothStep(double a, double b, double x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A box blur run <paramref name="passes"/> times (close to a Gaussian), edges clamped.</summary>
    private static float[] Blur(float[] source, int w, int h, int radius, int passes)
    {
        var a = (float[])source.Clone();
        var b = new float[a.Length];
        for (var p = 0; p < passes; p++)
        {
            BoxH(a, b, w, h, radius);
            BoxV(b, a, w, h, radius);
        }

        return a;
    }

    private static void BoxH(float[] src, float[] dst, int w, int h, int r)
    {
        var scale = 1f / (2 * r + 1);
        for (var y = 0; y < h; y++)
        {
            var o = y * w;
            float sum = 0;
            for (var x = -r; x <= r; x++)
                sum += src[o + Math.Clamp(x, 0, w - 1)];
            for (var x = 0; x < w; x++)
            {
                dst[o + x] = sum * scale;
                sum += src[o + Math.Min(x + r + 1, w - 1)] - src[o + Math.Max(x - r, 0)];
            }
        }
    }

    private static void BoxV(float[] src, float[] dst, int w, int h, int r)
    {
        var scale = 1f / (2 * r + 1);
        for (var x = 0; x < w; x++)
        {
            float sum = 0;
            for (var y = -r; y <= r; y++)
                sum += src[Math.Clamp(y, 0, h - 1) * w + x];
            for (var y = 0; y < h; y++)
            {
                dst[y * w + x] = sum * scale;
                sum += src[Math.Min(y + r + 1, h - 1) * w + x] - src[Math.Max(y - r, 0) * w + x];
            }
        }
    }

    /// <summary>Smooth value noise in 0..1 at a world point (the same everywhere for the same point).</summary>
    private static double Noise(double x, double y)
    {
        var x0 = Math.Floor(x);
        var y0 = Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var ix = (int)x0;
        var iy = (int)y0;
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var e = Hash(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + e) * fx * fy;
    }

    private static double Hash(int x, int y)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }
}
