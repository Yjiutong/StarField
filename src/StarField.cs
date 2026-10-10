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
            public float maxOffset;      // hash-derived four-direction offset (units of cell half-size)
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
                maxOffset = 0.4f,
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
                    // Hash-derived four-direction offset from the cell center
                    float offX, offY;
                    StarCut.StarOffset(gx, gy, p.seed, p.maxOffset, out offX, out offY);
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
                            ? (byte)Math.Round(255f * TwinkleBrightness(gx, gy, p.seed, time))
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
            StarCut.StarOffset(gx, gy, p.seed, p.maxOffset, out offX, out offY);
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
    }
}
