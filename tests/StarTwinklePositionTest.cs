// Verification: does twinkle stay bound to the star's own (post-offset) pixels,
// or does brightness change leak onto non-star pixels / the pre-offset cell center?
// Compile: csc /nologo /out:TwinklePosTest.exe src\StarCut.cs src\StarField.cs src\StarView.cs src\SeedCodec.cs tests\StarTwinklePositionTest.cs
using System;
using StarFieldAlgo;

class StarTwinklePositionTest
{
    static void Main()
    {
        int w = 400, h = 240;
        var p = StarField.Default(12345);
        p.qualifyThreshold = 0.6f;      // demo density
        p.maxOffset = 0.45f; p.offsetCoeff = 1f;
        p.twinkleAmplitude = 1f;

        var star = StarField.Generate(w, h, p, -1f);    // static star mask (post-offset positions)
        var t1 = StarField.Generate(w, h, p, 1.0f);
        var t2 = StarField.Generate(w, h, p, 1.43f);    // different phase window

        // Count star pixels and twinkle-change pixels, inside vs outside star pixels
        int starPx = 0, changeIn = 0, changeOut = 0;
        for (int i = 0; i < star.Length; i++)
        {
            if (star[i] > 0) starPx++;
            int d = Math.Abs(t1[i] - t2[i]);
            if (d > 30)
            {
                if (star[i] > 0) changeIn++; else changeOut++;
            }
        }

        // Per-cell sync check: within one qualifying cell, all star pixels must share
        // the same twinkle phase (i.e. all rise/fall together between t1 and t2).
        int syncOK = 0, syncBad = 0;
        for (int gy = 0; gy < h / p.cellH; gy++)
        {
            for (int gx = 0; gx < w / p.cellW; gx++)
            {
                int dir = 0;
                for (int yy = 0; yy < p.cellH; yy++)
                {
                    for (int xx = 0; xx < p.cellW; xx++)
                    {
                        int i = (gy * p.cellH + yy) * w + gx * p.cellW + xx;
                        if (star[i] == 0) continue;
                        int d = t2[i] - t1[i];
                        int s = d > 2 ? 1 : (d < -2 ? -1 : 0);
                        if (dir == 0) dir = s;
                        else if (s != 0 && s != dir) { syncBad++; goto nextCell; }
                    }
                }
                if (dir != 0) syncOK++;
                nextCell: ;
            }
        }

        Console.WriteLine("starPx={0}  twinkleChangeInStar={1}  twinkleChangeOutside={2}", starPx, changeIn, changeOut);
        Console.WriteLine("cellsWithStar={0}  syncOK={1}  syncBad={2}", syncOK + syncBad, syncOK, syncBad);
        Console.WriteLine(changeOut == 0 && syncBad == 0 ? "PASS: twinkle is bound to the star's own post-offset pixels, whole star breathes together" : "FAIL: twinkle leaks or desyncs");
    }
}
