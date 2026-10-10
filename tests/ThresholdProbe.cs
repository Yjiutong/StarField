using System;
using StarFieldAlgo;

class ThresholdProbe
{
    static int NonBlack(byte[] px)
    {
        int c = 0;
        foreach (byte b in px) if (b > 0) c++;
        return c;
    }

    static void Main()
    {
        var p = StarFieldAlgo.StarField.Default(12345);
        p.cellW = 40; p.cellH = 40;
        p.qualifyThreshold = 1f;
        var px1 = StarFieldAlgo.StarField.Generate(400, 400, p);
        Console.WriteLine("threshold=1.000  nonblack=" + NonBlack(px1));

        p.qualifyThreshold = 0.987f;
        var px2 = StarFieldAlgo.StarField.Generate(400, 400, p);
        Console.WriteLine("threshold=0.987  nonblack=" + NonBlack(px2));

        p.qualifyThreshold = 0.6f;
        var px3 = StarFieldAlgo.StarField.Generate(400, 400, p);
        Console.WriteLine("threshold=0.600  nonblack=" + NonBlack(px3));

        p.qualifyThreshold = 0f;
        var px0 = StarFieldAlgo.StarField.Generate(400, 400, p);
        Console.WriteLine("threshold=0.000  nonblack=" + NonBlack(px0));
    }
}
