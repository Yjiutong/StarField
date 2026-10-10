// Deterministic verification suite for StarField.Core.
// Build (Windows, .NET Framework):
//   csc /nologo /out:StarFieldTests.exe src\StarCut.cs src\StarField.cs src\StarView.cs tests\StarFieldTests.cs
using System;
using System.Linq;
using StarFieldAlgo;

static class StarFieldTests
{
    static int passed, failed;

    static void Assert(bool cond, string name)
    {
        if (cond) { passed++; Console.WriteLine("[PASS] " + name); }
        else { failed++; Console.WriteLine("[FAIL] " + name); }
    }

    static void Main()
    {
        // ——— Cut rules ———
        // XYAverage: samples across a cut line always differ (never equal).
        // Exact difference = (cell-1)/(2·cell) → approaches 0.5 as cell grows.
        bool cutRuleOk = true;
        for (int cell = 4; cell <= 40; cell += 4)
        {
            for (int y = 0; y < cell; y++)
            {
                int x0 = cell - 1, x1 = cell;   // across vertical cut
                float d = Math.Abs(StarCut.Value(x0, y, cell, cell) - StarCut.Value(x1, y, cell, cell));
                if (d <= 1e-6f) { cutRuleOk = false; }
                float expect = (cell - 1f) / (2f * cell);
                if (Math.Abs(d - expect) > 1e-6f) { cutRuleOk = false; }
            }
        }
        Assert(cutRuleOk, "Cut: every vertical cut line separates different colors, difference = (cell-1)/(2·cell)");

        // Gradient is monotonic inside a cell
        bool mono = true;
        for (int i = 1; i < 40; i++)
        {
            if (StarCut.Cut1D(i, 40) < StarCut.Cut1D(i - 1, 40)) { mono = false; break; }
        }
        Assert(mono, "Cut1D: linear gradient is monotonic inside a cell");

        // ——— Hashing ———
        float h1 = StarCut.CellHash(3, 7, 12345);
        float h2 = StarCut.CellHash(3, 7, 12345);
        float h3 = StarCut.CellHash(3, 7, 999);
        Assert(h1 == h2, "CellHash: deterministic for same cell + seed");
        Assert(h1 != h3, "CellHash: different seed → different value");
        Assert(h1 >= 0f && h1 <= 1f, "CellHash: value in [0,1]");

        // ——— Star field determinism ———
        var p = StarField.Default(42);
        byte[] a = StarField.Generate(512, 512, p);
        byte[] b = StarField.Generate(512, 512, p);
        Assert(a.SequenceEqual(b), "StarField: same params → identical output");

        int whiteCount = a.Count(v => v > 0);
        Assert(whiteCount > 0, "StarField: produces some stars at default threshold");

        // ——— StarAt matches Generate ———
        bool consistent = true;
        for (int y = 0; y < 256; y += 8)
            for (int x = 0; x < 256; x += 8)
                if (StarField.StarAt(x, y, p) != a[y * 512 + x]) { consistent = false; break; }
        Assert(consistent, "StarAt: single-pixel query matches Generate output");

        // ——— Twinkle ———
        float T = StarField.TwinklePeriod(5, 5, 42);
        Assert(T >= 0.8f && T <= 2.6f, "TwinklePeriod: within [0.8, 2.6]s");
        float b0 = StarField.TwinkleBrightness(5, 5, 42, 1.0f);
        float b1 = StarField.TwinkleBrightness(5, 5, 42, 1.0f);
        Assert(b0 == b1, "TwinkleBrightness: deterministic");
        Assert(b0 >= 0f && b0 <= 1f, "TwinkleBrightness: value in [0,1]");

        // ——— StarView ———
        var v0 = new StarView.View { curX = 10, curY = 10 };
        var win = StarView.Window(v0);
        Assert(win.Length == 4, "StarView: 4 cells resident");
        Assert(win[0].X == 10 && win[0].Y == 10 && win[1].X == 10 && win[1].Y == 9
            && win[2].X == 9 && win[2].Y == 10 && win[3].X == 9 && win[3].Y == 9,
            "StarView: current/up/left/up-left order");
        var v1 = StarView.MoveNorthWest(v0);
        Assert(v1.curX == 9 && v1.curY == 9, "StarView: move NW → new current = former up-left");
        var load = StarView.ToLoad(v0, v1);
        var unload = StarView.ToUnload(v0, v1);
        Assert(load.Length == 3, "StarView: 3 cells loaded after shift");
        Assert(unload.Length == 3, "StarView: 3 cells unloaded after shift");

        // ——— SeedCodec ———
        Assert(SeedCodec.IsValid("4s5t7gb7s8f75"), "SeedCodec: valid 13-char code accepted");
        Assert(!SeedCodec.IsValid("abc"), "SeedCodec: too short rejected");
        Assert(!SeedCodec.IsValid("1234567890ab!"), "SeedCodec: invalid char rejected");
        Assert(SeedCodec.Hash("4s5t7gb7s8f75") == SeedCodec.Hash("4s5t7gb7s8f75"), "SeedCodec: hash deterministic");
        Assert(SeedCodec.Hash("4s5t7gb7s8f75") != SeedCodec.Hash("4S5T7GB7S8F75"), "SeedCodec: hash case-sensitive");
        uint hDec = SeedCodec.DecodeBase36("4s5t7gb7s8f75");
        Assert(hDec == SeedCodec.DecodeBase36("4s5t7gb7s8f75"), "SeedCodec: base36 decode deterministic");

        // ——— Star offset (full 2D hash offset, whole-square translation) ———
        float ox1, oy1, ox2, oy2, ox3, oy3;
        StarCut.StarOffset(3, 5, 42, 0.4f, 1f, out ox1, out oy1);
        StarCut.StarOffset(3, 5, 42, 0.4f, 1f, out ox2, out oy2);
        StarCut.StarOffset(3, 5, 99, 0.4f, 1f, out ox3, out oy3);
        Assert(ox1 == ox2 && oy1 == oy2, "StarOffset: deterministic for same cell + seed");
        Assert(ox1 != ox3 || oy1 != oy3, "StarOffset: different seed → different offset");
        Assert(Math.Abs(ox1) <= 0.4f && Math.Abs(oy1) <= 0.4f, "StarOffset: both axes within [−maxOffset, +maxOffset]");
        // Full 2D: both axes may be non-zero (star lands anywhere in the cell, not only on an axis line)
        bool bothAxesSeen = false;
        for (int gy = 0; gy < 64 && !bothAxesSeen; gy++)
            for (int gx = 0; gx < 64 && !bothAxesSeen; gx++)
            {
                float ax, ay;
                StarCut.StarOffset(gx, gy, 7, 0.45f, 1f, out ax, out ay);
                if (ax != 0f && ay != 0f) bothAxesSeen = true;
            }
        Assert(bothAxesSeen, "StarOffset: full 2D — both axes can be non-zero simultaneously");
        // Offset coefficient: 0 → centered; scaling is linear; coeff 1 reaches near maxOffset
        float c0x, c0y, c1x, c1y, c5x, c5y;
        StarCut.StarOffset(3, 5, 42, 0.45f, 0f, out c0x, out c0y);
        StarCut.StarOffset(3, 5, 42, 0.45f, 1f, out c1x, out c1y);
        StarCut.StarOffset(3, 5, 42, 0.45f, 0.5f, out c5x, out c5y);
        Assert(c0x == 0f && c0y == 0f, "StarOffset: coeff 0 → star stays at cell center");
        Assert(Math.Abs(c5x - c1x * 0.5f) < 1e-6f && Math.Abs(c5y - c1y * 0.5f) < 1e-6f,
            "StarOffset: coeff scales offset linearly");
        float maxSeen = 0f;
        for (int gy = 0; gy < 64; gy++)
            for (int gx = 0; gx < 64; gx++)
            {
                float ax, ay;
                StarCut.StarOffset(gx, gy, 7, 0.45f, 1f, out ax, out ay);
                float m = Math.Max(Math.Abs(ax), Math.Abs(ay));
                if (m > maxSeen) maxSeen = m;
            }
        Assert(maxSeen > 0.44f, "StarOffset: coeff 1 → offset reaches near maxOffset (0.45)");
        // A star must never leave its own cell: offset (≤0.45 half) + radius gate (≤0.05) ≤ cell edge
        bool inside = true;
        var pOff = StarField.Default(42);
        pOff.maxOffset = 0.45f; pOff.offsetCoeff = 1f; pOff.qualifyThreshold = 0.3f;
        byte[] offField = StarField.Generate(1024, 1024, pOff);
        for (int y = 0; y < 1024; y++)
            for (int x = 0; x < 1024; x++)
            {
                if (offField[y * 1024 + x] == 0) continue;
                int gx = x / pOff.cellW, gy = y / pOff.cellH;
                float ddx = (x - (gx * pOff.cellW + pOff.cellW * 0.5f)) / (pOff.cellW * 0.5f);
                float ddy = (y - (gy * pOff.cellH + pOff.cellH * 0.5f)) / (pOff.cellH * 0.5f);
                if (Math.Abs(ddx) > 0.5f + 1e-3f || Math.Abs(ddy) > 0.5f + 1e-3f) { inside = false; break; }
            }
        Assert(inside, "StarOffset: stars never leave their own cell (offset 0.45 + radius ≤ 0.5)");
        // Twinkle must be preserved after offsetting: a star's brightness is still driven by
        // its qualifying cell hash (period salt 0x77 / phase salt 0x88), independent of position.
        bool twinkleOk = true;
        var pTw = StarField.Default(42);
        pTw.qualifyThreshold = 0.3f; pTw.maxOffset = 0.4f;
        byte[] twF = StarField.Generate(512, 512, pTw, 0.7f);
        for (int y = 0; y < 512 && twinkleOk; y++)
            for (int x = 0; x < 512 && twinkleOk; x++)
            {
                byte v = twF[y * 512 + x];
                if (v == 0) continue;
                int gx = x / pTw.cellW, gy = y / pTw.cellH;
                byte expect = (byte)Math.Round(255f * StarField.TwinkleBrightness(gx, gy, pTw.seed, 0.7f));
                if (v != expect) twinkleOk = false;
            }
        Assert(twinkleOk, "Twinkle: brightness still driven by qualifying-cell hash after offset");
        // ——— Base breathe factor ———
        var pB = StarField.Default(42);
        pB.qualifyThreshold = 0.3f; pB.maxOffset = 0.4f;
        pB.twinkleAmplitude = 0f;   // constant bright
        byte[] f0 = StarField.Generate(256, 256, pB, 0.7f);
        Assert(f0.All(v => v == 0 || v == 255), "Breathe: amplitude 0 → stars stay constant bright (0 or 255)");
        float twV = StarField.TwinkleBrightness(3, 7, 42, 0.7f);
        pB.twinkleAmplitude = 1f;   // full breathe
        float breatheFull = StarField.BreatheBrightness(3, 7, pB, 0.7f);
        Assert(Math.Abs(breatheFull - twV) < 1e-6f, "Breathe: amplitude 1 → equals original twinkle");
        pB.twinkleAmplitude = 0.5f; // half breathe around bright state
        float b05 = StarField.BreatheBrightness(3, 7, pB, 0.7f);
        Assert(Math.Abs(b05 - (1f + (twV - 1f) * 0.5f)) < 1e-6f, "Breathe: amplitude 0.5 → midpoint of [1, twinkle]");
        Assert(b05 >= 0.5f && b05 <= 1f, "Breathe: brightness stays in [0.5, 1] for amplitude 0.5");

        // ——— Neighbor twinkle suppression (3×3 linked breathing) ———
        // 1) coeff 0 → no suppression (peers never dim each other).
        float s0 = StarField.SuppressFactor(5, 5, 42, 0.7f, 0f);
        Assert(s0 < 1e-6f, "Suppress: coeff 0 → no suppression");
        // 2) Peak-pull, orthogonal: a bright cell is an orthogonal neighbor of (10,9) with
        //    weight 1, so its contribution (twinkle × coeff × 1) is always in the max.
        bool peakOrth = true;
        for (int t = 0; t < 400 && peakOrth; t++)
        {
            float tm = t * 0.005f;
            float c = StarField.TwinkleBrightness(10, 10, 42, tm);
            float s = StarField.SuppressFactor(10, 9, 42, tm, 1f);
            if (s < c - 1e-5f) peakOrth = false;
        }
        Assert(peakOrth, "Suppress: bright cell dims its orthogonal neighbor by ≥ its own twinkle");
        // 3) Peak-pull, diagonal: (10,10) is a diagonal neighbor of (9,9), weight 1/√2.
        bool peakDiag = true;
        for (int t = 0; t < 400 && peakDiag; t++)
        {
            float tm = t * 0.005f;
            float c = StarField.TwinkleBrightness(10, 10, 42, tm);
            float s = StarField.SuppressFactor(9, 9, 42, tm, 1f);
            if (s < c * 0.70710678f - 1e-4f) peakDiag = false;
        }
        Assert(peakDiag, "Suppress: bright cell dims its diagonal neighbor by ≥ twinkle/√2");
        // 4) SuppressedBrightness is monotone in coeff: higher coeff → darker (when a neighbor is bright).
        float tPick = 0f;
        for (int t = 0; t < 200; t++) { float tm = t * 0.02f; if (StarField.TwinkleBrightness(3, 3, 42, tm) > 0.9f) { tPick = tm; break; } }
        var pSup = StarField.Default(42);
        pSup.qualifyThreshold = 0.3f; pSup.maxOffset = 0.4f; pSup.twinkleAmplitude = 1f;
        pSup.suppressCoeff = 0.2f;
        float brightLow = StarField.SuppressedBrightness(3, 4, pSup, tPick);
        pSup.suppressCoeff = 0.9f;
        float brightHigh = StarField.SuppressedBrightness(3, 4, pSup, tPick);
        Assert(brightHigh < brightLow, "Suppress: stronger coeff → stronger dimming of neighbors");
        // 5) No suppression (coeff 0) → every cell breathes freely = BreatheBrightness, at every moment.
        var pFree = StarField.Default(42);
        pFree.twinkleAmplitude = 1f; pFree.suppressCoeff = 0f;
        bool freeOk = true;
        for (int t = 0; t < 200 && freeOk; t++)
        {
            float tm = t * 0.02f;
            for (int g = 0; g < 9 && freeOk; g++)
            {
                float v = StarField.SuppressedBrightness(g % 3, g / 3, pFree, tm);
                float e = StarField.BreatheBrightness(g % 3, g / 3, pFree, tm);
                if (Math.Abs(v - e) > 1e-5f) freeOk = false;
            }
        }
        Assert(freeOk, "Suppress: coeff 0 → all cells breathe freely (self never suppresses itself)");
        // 6) Output clamped to ≥ 0 even when suppression exceeds the cell's own brightness.
        bool clampOk = true;
        for (int t = 0; t < 400; t++)
        {
            float tm = t * 0.02f;
            var pC = StarField.Default(42);
            pC.twinkleAmplitude = 1f; pC.suppressCoeff = 3f;   // over-suppression on purpose
            float v = StarField.SuppressedBrightness(4, 4, pC, tm);
            if (v < -1e-6f) clampOk = false;
        }
        Assert(clampOk, "Suppress: output clamped to ≥ 0 under over-suppression");

        Console.WriteLine();
        Console.WriteLine("passed={0} failed={1}", passed, failed);
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}
