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

        Console.WriteLine();
        Console.WriteLine("passed={0} failed={1}", passed, failed);
        Environment.Exit(failed == 0 ? 0 : 1);
    }
}
