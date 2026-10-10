using System;

namespace StarFieldAlgo
{
    /// <summary>
    /// Procedural star-field cutting / hashing / masking primitives.
    ///
    /// Grid cutting: uniform square cells; each cell has a linear gradient in one
    /// consistent direction. Rules:
    ///   1) Any two samples immediately across a cut line always differ in value;
    ///   2) Between two neighbouring cut lines lies a black→white transition band
    ///      (the linear gradient inside each cell).
    /// Pure, deterministic: same coordinate + same cell size → same value.
    /// </summary>
    public static class StarCut
    {
        public const int DefaultCellW = 80;
        public const int DefaultCellH = 80;

        /// <summary>Combine mode: XOnly = vertical cuts only; XYAverage = vertical + horizontal averaged.</summary>
        public enum Combine
        {
            XOnly,
            XYAverage,
        }

        /// <summary>1D cut: linear gradient 0..1 inside a cell (cell start = 0 black, cell end = 1 white).</summary>
        public static float Cut1D(int pos, int cell)
        {
            if (cell <= 0) return 0f;
            float v = (float)(pos % cell) / cell;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// 2D cut value:
        /// XOnly → vx; XYAverage → (vx + vy) / 2.
        /// Averaging guarantees every cut line keeps a constant 0.5 difference
        /// across it (including at intersections), so neighbours never match.
        /// </summary>
        public static float Value(int x, int y, int cellW = DefaultCellW, int cellH = DefaultCellH, Combine mode = Combine.XYAverage)
        {
            float vx = Cut1D(x, cellW);
            if (mode == Combine.XOnly) return vx;
            float vy = Cut1D(y, cellH);
            return (vx + vy) / 2f;
        }

        /// <summary>Render a full cutting grayscale (bytes 0..255, per pixel, image origin top-left).</summary>
        public static byte[] Render(int w, int h, int cellW = DefaultCellW, int cellH = DefaultCellH, Combine mode = Combine.XYAverage)
        {
            var px = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = (byte)Math.Round(Value(x, y, cellW, cellH, mode) * 255f);
            return px;
        }

        // ————— Per-cell hashing (step 3: hash each cell → pseudo-random pixel field) —————

        /// <summary>Deterministic hash of cell (gx, gy), returns 0..1 (same cell + same seed → same value).</summary>
        public static float CellHash(int gx, int gy, int seed, int salt = 0)
        {
            uint n = (uint)(gx * 374761393 + gy * 668265263 + seed * 1442695041 + salt * 429496729);
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;
            return (float)(n & 0x7FFFFFFFu) / 2147483647.0f;
        }

        /// <summary>
        /// Per-cell hash → random black/white pixel field: hash &gt; threshold = white (255), else black (0).
        /// Used as an independent star-qualification / brightness mask layer.
        /// </summary>
        public static byte[] RandomBW(int w, int h, int cellW = DefaultCellW, int cellH = DefaultCellH, int seed = 0, float threshold = 0.5f)
        {
            var px = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int gx = x / cellW, gy = y / cellH;
                    float hsh = CellHash(gx, gy, seed, 0x5A7);
                    px[y * w + x] = hsh > threshold ? (byte)255 : (byte)0;
                }
            return px;
        }

        // ————— Star mask (step 4: in-cell normalized coordinates + step function) —————

        /// <summary>Star shape: Circle = dot; Square = small pixel block.</summary>
        public enum StarShape
        {
            Circle,
            Square,
        }

        /// <summary>
        /// Per-star in-cell offset, derived from the cell hash.
        /// Direction: one of four (up / down / left / right), chosen by a direction hash;
        /// magnitude: 0..maxOffset (in units of cell half-size), chosen by a magnitude hash.
        /// Deterministic: same cell + same seed → same offset.
        /// </summary>
        /// <summary>
        /// Hash-derived in-cell offset for a star (units of cell half-size).
        /// v0.9: full 2D offset — the star square is TRANSLATED as a whole (shape unchanged, no
        /// stretching) and may land anywhere inside its cell, not just on an axis line.
        /// offX = (h_x·2−1) · maxOffset · coeff, offY = (h_y·2−1) · maxOffset · coeff,
        /// where h_x = CellHash(...,0x9A), h_y = CellHash(...,0x9B).
        /// Geometry: |offset| ≤ maxOffset and star radius ≤ maxRadius keeps the star inside its
        /// cell as long as maxOffset + maxRadius ≤ 0.5 (half-size units).
        /// Deterministic: same cell + seed → same offset.
        /// </summary>
        public static void StarOffset(int gx, int gy, int seed, float maxOffset, float coeff,
            out float offX, out float offY)
        {
            float hx = CellHash(gx, gy, seed, 0x9A);
            float hy = CellHash(gx, gy, seed, 0x9B);
            float amp = maxOffset * coeff;
            offX = (hx * 2f - 1f) * amp;
            offY = (hy * 2f - 1f) * amp;
        }

        /// <summary>
        /// Star mask: each cell is hashed first (must exceed the qualify threshold to host a star),
        /// then a step function compares the in-cell distance to the radius gate derived from that
        /// cell's hash. The star center is offset from the cell center by a hash-derived
        /// full-2D offset (StarOffset, coeff default 1), so stars are not pinned to cell centers.
        /// dist = normalized distance to the offset star center (center 0, cell edge ±1).
        /// Square star: |dx| ≤ edge and |dy| ≤ edge; Circle star: dist ≤ edge.
        /// </summary>
        public static byte[] StarMask(int w, int h, int cellW = DefaultCellW, int cellH = DefaultCellH, int seed = 0,
            float qualifyThreshold = 0.5f, float minRadius = 0.06f, float maxRadius = 0.16f,
            StarShape shape = StarShape.Square, float maxOffset = 0.4f, float offsetCoeff = 1f)
        {
            var px = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int gx = x / cellW, gy = y / cellH;
                    float hsh = CellHash(gx, gy, seed, 0x5A7);
                    if (hsh <= qualifyThreshold) { px[y * w + x] = 0; continue; }   // no-star cell: black
                    // Hash-derived full-2D offset (units of cell half-size)
                    float offX, offY;
                    StarOffset(gx, gy, seed, maxOffset, offsetCoeff, out offX, out offY);
                    // Star center: cell center + offset
                    float cx = gx * cellW + cellW * 0.5f + offX * cellW * 0.5f;
                    float cy = gy * cellH + cellH * 0.5f + offY * cellH * 0.5f;
                    // In-cell normalized coordinates relative to the offset star center
                    float dx = (x - cx) / (cellW * 0.5f);
                    float dy = (y - cy) / (cellH * 0.5f);
                    // Step function: this cell's hash decides the radius gate (larger hash → larger star)
                    float edge = minRadius + (maxRadius - minRadius) * hsh;
                    bool hit = shape == StarShape.Square
                        ? (Math.Abs(dx) <= edge && Math.Abs(dy) <= edge)
                        : (Math.Sqrt(dx * dx + dy * dy) <= edge);
                    px[y * w + x] = hit ? (byte)255 : (byte)0;
                }
            return px;
        }
    }
}
