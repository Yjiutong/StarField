// Example: render pipeline outputs to PNG using only the core library.
// Build (Windows, .NET Framework):
//   csc /nologo /r:System.Drawing.dll /out:StarFieldExample.exe src\StarCut.cs src\StarField.cs examples\Program.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using StarFieldAlgo;

static class Program
{
    static void SaveGray(byte[] px, int w, int h, string path)
    {
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        {
            var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var dst = new byte[bd.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = px[y * w + x];
                    int i = y * bd.Stride + x * 3;
                    dst[i] = v; dst[i + 1] = v; dst[i + 2] = v;
                }
            System.Runtime.InteropServices.Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "output";
        Directory.CreateDirectory(outDir);
        int seed = 20261008;

        // Step 1: grid cut gradient (80px cells)
        SaveGray(StarCut.Render(1024, 1024, 80, 80, StarCut.Combine.XYAverage), 1024, 1024,
            Path.Combine(outDir, "step1_cut.png"));

        // Step 2: per-cell hash → random black/white
        SaveGray(StarCut.RandomBW(1024, 1024, 80, 80, seed, 0.5f), 1024, 1024,
            Path.Combine(outDir, "step2_bw.png"));

        // Step 3: star mask (square pixel stars, qualify 0.987)
        var p = StarField.Default(seed);
        SaveGray(StarCut.StarMask(1024, 1024, 40, 40, seed, p.qualifyThreshold, p.minRadius, p.maxRadius, StarCut.StarShape.Square),
            1024, 1024, Path.Combine(outDir, "step3_stars.png"));

        // Step 4: full field, no twinkle
        SaveGray(StarField.Generate(1024, 1024, p), 1024, 1024, Path.Combine(outDir, "step4_field.png"));

        // Step 5: full field with twinkle at t=1.2s
        SaveGray(StarField.Generate(1024, 1024, p, 1.2f), 1024, 1024, Path.Combine(outDir, "step5_twinkle_t1.2.png"));

        Console.WriteLine("PNG outputs written to: " + Path.GetFullPath(outDir));
    }
}
