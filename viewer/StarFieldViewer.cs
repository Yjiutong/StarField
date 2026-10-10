// Star Field Viewer - interactive demo of the smooth-cruise viewport.
// Pixel-level smooth movement: bilinear sub-pixel sampling (no whole-cell jumps).
// Performance: static star-mask cache (rebuilt on anchor change) + per-cell twinkle cache.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StarFieldAlgo;

public class StarFieldViewerForm : Form
{
    const int Cell = 40;
    const int FieldW = 2048, FieldH = 2072;
    const int ViewCols = 20, ViewRows = 12;
    const int Zoom = 2;
    readonly int viewW = Cell * ViewCols * Zoom;   // 1600
    readonly int viewH = Cell * ViewRows * Zoom;   // 960

    StarFieldAlgo.StarField.Params p;
    StarView.View view = new StarView.View { curX = 20, curY = 24 };
    Timer timer;
    Bitmap fullThumb;
    float t0;
    float scrollX, scrollY;
    const float autoSpeed = 5f;    // px/s
    int lastTick;
    int fullGridW, fullGridH;

    // Smooth-render caches
    byte[] starCache;
    int cacheW, cacheH;
    float cacheOx, cacheOy;
    int lastViewX, lastViewY;
    float[] twCache;
    int twOx, twOy, twW, twH;

    public StarFieldViewerForm()
    {
        Text = "Star Field Viewer (1080P auto-cruise, twinkling stars)";
        ClientSize = new Size(1920, 1080);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        p = StarFieldAlgo.StarField.Default(0x4D54);
        fullGridW = FieldW / Cell; fullGridH = FieldH / Cell;
        t0 = (float)(Environment.TickCount / 1000.0);

        var full = BuildStarRegion(0, 0, FieldW, FieldH);
        fullThumb = new Bitmap(FieldW, FieldH, PixelFormat.Format24bppRgb);
        var bd = fullThumb.LockBits(new Rectangle(0, 0, FieldW, FieldH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var dst = new byte[bd.Stride * FieldH];
        for (int y = 0; y < FieldH; y++)
            for (int x = 0; x < FieldW; x++)
            {
                byte v = full[y * FieldW + x];
                int i = y * bd.Stride + x * 3;
                dst[i] = v; dst[i + 1] = v; dst[i + 2] = v;
            }
        Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
        fullThumb.UnlockBits(bd);

        RebuildCache();
        UpdateTwinkle(Environment.TickCount);   // pre-init twinkle cache (avoids null on first paint)

        timer = new Timer { Interval = 16 };   // ~60fps
        lastTick = Environment.TickCount;
        timer.Tick += delegate(object s, EventArgs e2)
        {
            int now = Environment.TickCount;
            float dt = (now - lastTick) / 1000f;
            lastTick = now;
            float stepPx = Cell * Zoom;
            scrollX += autoSpeed * dt; scrollY += autoSpeed * dt;
            while (scrollX >= stepPx && scrollY >= stepPx)
            {
                scrollX -= stepPx; scrollY -= stepPx;
                view = StarView.MoveNorthWest(view);
                RebuildCache();
            }
            UpdateTwinkle(now);
            Invalidate();
        };
        timer.Start();
    }

    byte[] BuildStarRegion(int ox, int oy, int w, int h)
    {
        var px = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = StarFieldAlgo.StarField.StarAt(ox + x, oy + y, p);
        return px;
    }

    void RebuildCache()
    {
        cacheW = (ViewCols + 2) * Cell; cacheH = (ViewRows + 2) * Cell;
        cacheOx = (view.curX - 1) * Cell; cacheOy = (view.curY - 1) * Cell;
        starCache = BuildStarRegion((int)cacheOx, (int)cacheOy, cacheW, cacheH);
        lastViewX = view.curX; lastViewY = view.curY;
    }

    void UpdateTwinkle(int nowMs)
    {
        float time = nowMs / 1000f - t0;
        twOx = view.curX - 1; twOy = view.curY - 1;
        twW = ViewCols + 3; twH = ViewRows + 3;
        if (twCache == null || twCache.Length < twW * twH) twCache = new float[twW * twH];
        for (int gy = 0; gy < twH; gy++)
            for (int gx = 0; gx < twW; gx++)
                twCache[gy * twW + gx] = StarFieldAlgo.StarField.TwinkleBrightness(twOx + gx, twOy + gy, p.seed, time);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        float time = (float)(Environment.TickCount / 1000.0) - t0;

        int ox = 8, oy = 24;
        // Smooth render: per display pixel → continuous world coordinate → bilinear star-mask × cell twinkle
        float zoomF = Zoom;
        float baseX = cacheOx - scrollX / zoomF;
        float baseY = cacheOy - scrollY / zoomF;
        float invZ = 1f / zoomF;

        var bmp = new Bitmap(viewW, viewH, PixelFormat.Format24bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, viewW, viewH), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var dst = new byte[bd.Stride * viewH];
        int twOxx = twOx, twOyy = twOy;

        for (int yy = 0; yy < viewH; yy++)
        {
            float worldY = baseY + yy * invZ;
            int cacheY = (int)worldY - (int)cacheOy;
            if (cacheY < 0) cacheY = 0; if (cacheY >= cacheH - 1) cacheY = cacheH - 2;
            float fy = worldY - cacheOy - cacheY;
            int row0 = cacheY * cacheW, row1 = (cacheY + 1) * cacheW;
            int gy0 = (int)Math.Floor(worldY / Cell) - twOyy;
            if (gy0 < 0) gy0 = 0; if (gy0 >= twH) gy0 = twH - 1;

            for (int xx = 0; xx < viewW; xx++)
            {
                float worldX = baseX + xx * invZ;
                int cacheX = (int)worldX - (int)cacheOx;
                if (cacheX < 0) cacheX = 0; if (cacheX >= cacheW - 1) cacheX = cacheW - 2;
                float fx = worldX - cacheOx - cacheX;
                float s00 = starCache[row0 + cacheX];
                float s10 = starCache[row0 + cacheX + 1];
                float s01 = starCache[row1 + cacheX];
                float s11 = starCache[row1 + cacheX + 1];
                float top = s00 + (s10 - s00) * fx;
                float bot = s01 + (s11 - s01) * fx;
                float star = top + (bot - top) * fy;          // bilinear (0..255)

                int gx0 = (int)Math.Floor(worldX / Cell) - twOxx;
                if (gx0 < 0) gx0 = 0; if (gx0 >= twW) gx0 = twW - 1;
                float bright = twCache[gy0 * twW + gx0];      // per-cell twinkle

                float v = star * bright;
                if (v > 255f) v = 255f;
                byte b = (byte)v;
                int i = yy * bd.Stride + xx * 3;
                dst[i] = b; dst[i + 1] = b; dst[i + 2] = b;
            }
        }
        Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
        bmp.UnlockBits(bd);
        g.DrawImage(bmp, ox, oy, viewW, viewH);
        bmp.Dispose();

        using (var f = new Font("Consolas", 10))
        {
            g.DrawString(string.Format("anchor ({0},{1})   smooth cruise 5px/s (sub-pixel)   t={2:F1}s",
                view.curX, view.curY, time), f, Brushes.White, 8, 0);
            g.DrawString("auto cruising (Esc=close)", f, Brushes.Orange, 760, 0);
        }

        // Sidebar thumbnail + viewport box
        int sx = ox + viewW + 12, sy = oy + 8;
        int sw = 292, sh = (int)(sw * (FieldH / (float)FieldW));
        g.DrawImage(fullThumb, sx, sy, sw, sh);
        float kx = sw / (float)fullGridW, ky = sh / (float)fullGridH;
        int bx = (int)(sx + view.curX * kx), by = (int)(sy + view.curY * ky);
        g.DrawRectangle(Pens.Lime, bx, by, (int)(ViewCols * kx), (int)(ViewRows * ky));
        using (var fs = new Font("Consolas", 9))
        {
            g.DrawString("full field thumbnail (green = viewport)", fs, Brushes.LightGreen, sx, sy + sh + 6);
            g.DrawString(string.Format("viewport x[{0},{1}] y[{2},{3}]",
                view.curX, view.curX + ViewCols - 1, view.curY, view.curY + ViewRows - 1), fs, Brushes.Gray, sx, sy + sh + 22);
        }
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StarFieldViewerForm());
    }
}
