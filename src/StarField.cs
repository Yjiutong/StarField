using System;

namespace StarFieldAlgo
{
    /// <summary>
    /// Complete star-field generator (integrates all four algorithm steps).
    /// Flow:
    ///   ① Cut gradient: StarCut.Value (vertical + horizontal averaged);
    ///   ② Per-cell hash: StarCut.CellHash (deterministic per-cell coordinate hash);
    ///   ③ Star qualification: cells with hash &gt; qualifyThreshold host stars;
    ///   ④ Star shape: in-cell normalized coordinates + step function (Square / Circle).
    /// Output: star pixels = white (255), optionally modulated by per-star twinkle;
    /// non-star pixels = black.
    /// Pure, deterministic: same parameters → identical star field.
    /// </summary>
    public static class StarField
    {
        public struct Params
        {
            public int cellW, cellH;
            public int seed;
            public float qualifyThreshold;
            public float minRadius, maxRadius;
            public float maxOffset;      // max in-cell offset, full 2D (units of cell half-size); default 0.45
            public float offsetCoeff;    // offset coefficient 0..1: 0 = star stays at cell center, 1 = full maxOffset
            public float twinkleAmplitude;  // base breathe factor 0..1: 0 = constant bright, 1 = full triangle-wave breathe
            public float suppressCoeff;  // neighbor-suppression coefficient 0..1: how hard a peak cell dims its 8 neighbors
            public StarCut.StarShape shape;
            public StarCut.Combine combine;
        }

        public static Params Default(int seed = 0)
        {
            return new Params
            {
                cellW = 40, cellH = 40,
                seed = seed,
                qualifyThreshold = 0.987f,
                minRadius = 0.02f, maxRadius = 0.05f,   // max star width = 4 star-pixels
                maxOffset = 0.45f,                      // geometry: 0.45 offset + 0.05 radius ≤ 0.5 cell edge
                offsetCoeff = 1f,
                twinkleAmplitude = 1f,
                suppressCoeff = 0.5f,
                shape = StarCut.StarShape.Square,
                combine = StarCut.Combine.XYAverage,
            };
        }

        /// <summary>Generate the final star-field grayscale (0..255). With time ≥ 0, each star's brightness follows its own hash-derived twinkle period.</summary>
        public static byte[] Generate(int w, int h, Params p, float time = -1f)
        {
            var px = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int gx = x / p.cellW, gy = y / p.cellH;
                    float hsh = StarCut.CellHash(gx, gy, p.seed, 0x5A7);
                    // Star qualification
                    if (hsh <= p.qualifyThreshold) { px[y * w + x] = 0; continue; }
                    // Hash-derived full-2D offset from the cell center (whole-square translation,
                    // shape unchanged; star may land anywhere inside its cell)
                    float offX, offY;
                    StarCut.StarOffset(gx, gy, p.seed, p.maxOffset, p.offsetCoeff, out offX, out offY);
                    float cx = gx * p.cellW + p.cellW * 0.5f + offX * p.cellW * 0.5f;
                    float cy = gy * p.cellH + p.cellH * 0.5f + offY * p.cellH * 0.5f;
                    // In-cell normalized coordinates relative to the offset star center
                    float dx = (x - cx) / (p.cellW * 0.5f);
                    float dy = (y - cy) / (p.cellH * 0.5f);
                    // Step function: hash decides the radius gate
                    float edge = p.minRadius + (p.maxRadius - p.minRadius) * hsh;
                    bool hit = p.shape == StarCut.StarShape.Square
                        ? (Math.Abs(dx) <= edge && Math.Abs(dy) <= edge)
                        : (Math.Sqrt(dx * dx + dy * dy) <= edge);
                    if (hit)
                    {
                        px[y * w + x] = time >= 0f
                            ? (byte)Math.Round(255f * BreatheBrightness(gx, gy, p, time))
                            : (byte)255;
                        continue;
                    }
                    // Non-star pixels stay black
                    px[y * w + x] = 0;
                }
            }
            return px;
        }

        /// <summary>Single-pixel query at continuous world coordinates; 255 = star pixel, 0 = background.</summary>
        public static byte StarAt(float wx, float wy, Params p)
        {
            int gx = (int)Math.Floor(wx / p.cellW), gy = (int)Math.Floor(wy / p.cellH);
            float hsh = StarCut.CellHash(gx, gy, p.seed, 0x5A7);
            if (hsh <= p.qualifyThreshold) return 0;
            float offX, offY;
            StarCut.StarOffset(gx, gy, p.seed, p.maxOffset, p.offsetCoeff, out offX, out offY);
            float cx = gx * p.cellW + p.cellW * 0.5f + offX * p.cellW * 0.5f;
            float cy = gy * p.cellH + p.cellH * 0.5f + offY * p.cellH * 0.5f;
            float dx = (wx - cx) / (p.cellW * 0.5f);
            float dy = (wy - cy) / (p.cellH * 0.5f);
            float edge = p.minRadius + (p.maxRadius - p.minRadius) * hsh;
            bool hit = p.shape == StarCut.StarShape.Square
                ? (Math.Abs(dx) <= edge && Math.Abs(dy) <= edge)
                : (Math.Sqrt(dx * dx + dy * dy) <= edge);
            return hit ? (byte)255 : (byte)0;
        }

        // ————— Per-star continuous twinkle (period derived from the cell coordinate hash) —————

        /// <summary>Twinkle period (seconds): derived from the star's cell hash, range [minT, maxT].</summary>
        public static float TwinklePeriod(int gx, int gy, int seed, float minT = 0.8f, float maxT = 2.6f)
        {
            return minT + (maxT - minT) * StarCut.CellHash(gx, gy, seed, 0x77);
        }

        /// <summary>Phase offset 0..1: derived from the cell hash (staggered twinkle timing).</summary>
        public static float TwinklePhase(int gx, int gy, int seed)
        {
            return StarCut.CellHash(gx, gy, seed, 0x88);
        }

        /// <summary>
        /// Twinkle brightness 0..1: triangle wave (brighten fast → fade slow → dark → brighten again),
        /// period = TwinklePeriod, phases staggered.
        /// Same cell + same time → same brightness (deterministic).
        /// </summary>
        public static float TwinkleBrightness(int gx, int gy, int seed, float time,
            float minT = 0.8f, float maxT = 2.6f)
        {
            float T = TwinklePeriod(gx, gy, seed, minT, maxT);
            if (T <= 0f) return 1f;
            float u = (time / T + TwinklePhase(gx, gy, seed)) % 1f;
            // Triangle wave: 0→0.5 brightens (0→1), 0.5→1 fades (1→0)
            return u < 0.5f ? u * 2f : 1f - (u - 0.5f) * 2f;
        }

        /// <summary>
        /// Final breathe brightness 0..1 with the base breathe factor applied:
        /// amplitude 0 → constant bright (1.0); amplitude 1 → full twinkle (0..1 triangle wave);
        /// in between → scaled breathe around the bright state.
        /// </summary>
        public static float BreatheBrightness(int gx, int gy, Params p, float time)
        {
            float tw = TwinkleBrightness(gx, gy, p.seed, time);
            return 1f + (tw - 1f) * p.twinkleAmplitude;
        }

        // ————— Neighbor twinkle suppression (3×3 linked breathing) —————

        /// <summary>
        /// Suppression factor 0..1 applied to cell (gx,gy) because of its 8 neighbors:
        /// each neighbor n that is currently bright (near its twinkle peak) dims this cell by
        /// twinkle(n) × coeff × distWeight. The brightest neighbor dominates (max).
        /// Dist weight: orthogonal neighbors 1.0, diagonal 1/√2 (farther → weaker pull).
        /// Result: when the center cell of a 3×3 block hits its peak, all 9 cells fall back
        /// together; when the center reaches its trough, suppression fades and the neighbors
        /// resume their own breathing. Pure &amp; deterministic.
        /// </summary>
        public static float SuppressFactor(int gx, int gy, int seed, float time, float coeff)
        {
            float s = 0f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    float nc = TwinkleBrightness(gx + dx, gy + dy, seed, time);
                    float w = (dx == 0 || dy == 0) ? 1f : 0.70710678f;
                    float c = nc * coeff * w;
                    if (c > s) s = c;
                }
            }
            return s > 1f ? 1f : s;
        }

        /// <summary>
        /// Final per-cell brightness with neighbor suppression applied:
        /// bright = BreatheBrightness × (1 − SuppressFactor), clamped to ≥ 0.
        /// The cell's own twinkle is NOT suppressed by itself — only its 8 neighbors
        /// dim it (and it dims them in turn).
        /// </summary>
        public static float SuppressedBrightness(int gx, int gy, Params p, float time)
        {
            float breathe = BreatheBrightness(gx, gy, p, time);
            float s = SuppressFactor(gx, gy, p.seed, time, p.suppressCoeff);
            float v = breathe * (1f - s);
            return v < 0f ? 0f : v;
        }
    }
}
