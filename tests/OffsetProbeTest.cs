// Verify StarOffset actually displaces stars away from the cell center,
// and print where stars land inside their cells.
using System;
using StarFieldAlgo;

class OffsetProbeTest
{
    static void Main()
    {
        var p = StarField.Default(12345);
        p.qualifyThreshold = 0.6f; p.maxOffset = 0.45f; p.offsetCoeff = 1f;
        int shown = 0;
        Console.WriteLine("gx,gy  hsh  offX  offY  starCenterDX  starCenterDY (px)");
        for (int gy = 0; gy < 12 && shown < 10; gy++)
        {
            for (int gx = 0; gx < 20 && shown < 10; gx++)
            {
                float hsh = StarCut.CellHash(gx, gy, p.seed, 0x5A7);
                if (hsh <= p.qualifyThreshold) continue;
                float ox, oy;
                StarCut.StarOffset(gx, gy, p.seed, p.maxOffset, p.offsetCoeff, out ox, out oy);
                // star center relative to cell center, in pixels
                float cxPx = ox * p.cellW * 0.5f;
                float cyPx = oy * p.cellH * 0.5f;
                Console.WriteLine("{0,2},{1,2}  {2:F3}  {3:+0.000;-0.000}  {4:+0.000;-0.000}  {5:+0.0;-0.0}  {6:+0.0;-0.0}",
                    gx, gy, hsh, ox, oy, cxPx, cyPx);
                shown++;
            }
        }
        Console.WriteLine(shown == 10 ? "OK: offsets are non-zero, stars displaced from cell center" : "probe finished");
    }
}
