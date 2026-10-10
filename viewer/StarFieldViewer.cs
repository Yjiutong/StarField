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
    float t0;
    float scrollX, scrollY;
    const float autoSpeed = 5f;    // px/s
    int lastTick;

    // Smooth-render caches
    byte[] starCache;
    int cacheW, cacheH;
    float cacheOx, cacheOy;
    int lastViewX, lastViewY;
    float[] twCache;
    int twOx, twOy, twW, twH;

    // Seed input controls
    TextBox seedBox;
    Button genBtn;
    Label seedLabel;
    string currentCode = SeedCodec.ExampleSeed;

    public StarFieldViewerForm()
    {
        Text = "Star Field Viewer (1080P auto-cruise, twinkling stars)";
        // Fit the screen (canvas 1600x960 + input row); smaller displays get a smaller window
        var wa = Screen.PrimaryScreen.WorkingArea;
        ClientSize = new Size(Math.Min(1920, wa.Width - 20), Math.Min(1080, wa.Height - 20));
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        p = StarFieldAlgo.StarField.Default((int)SeedCodec.Hash(SeedCodec.ExampleSeed));
        p.qualifyThreshold = 0.6f;   // demo density: keeps enough stars inside the 20x12 viewport
        t0 = (float)(Environment.TickCount / 1000.0);

        // Seed input row: 13-char code → regenerate the whole base field
        var seedCaption = new Label { Left = 8, Top = 996, Width = 104, Height = 26, Text = "种子(13位)",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        seedBox = new TextBox { Left = 118, Top = 992, Width = 210, Font = new Font("Consolas", 12) };
        seedBox.Text = SeedCodec.ExampleSeed;
        genBtn = new Button { Left = 336, Top = 990, Width = 96, Height = 30, Text = "生成底图" };
        genBtn.Click += delegate(object s2, EventArgs e2) { RegenerateField(seedBox.Text); };
        seedBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        {
            if (e2.KeyCode == Keys.Enter) { RegenerateField(seedBox.Text); e2.SuppressKeyPress = true; }
        };
        seedLabel = new Label { Left = 448, Top = 996, Width = 900, Height = 26, ForeColor = Color.LightGray,
            Font = new Font("Consolas", 10) };
        Controls.Add(seedCaption); Controls.Add(seedBox); Controls.Add(genBtn); Controls.Add(seedLabel);
        UpdateSeedLabel();

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

    // Regenerate the whole base field from a 13-char seed code (deterministic hash → same field).
    void RegenerateField(string code)
    {
        if (!SeedCodec.IsValid(code))
        {
            seedLabel.ForeColor = Color.OrangeRed;
            seedLabel.Text = "种子需为 13 位，每位为字母或数字（0-9A-Za-z）";
            return;
        }
        uint h = SeedCodec.Hash(code);
        p.seed = (int)h;
        p.qualifyThreshold = 0.6f;   // demo density (same as initial view)
        currentCode = code;
        view = new StarView.View { curX = 20, curY = 24 };
        scrollX = 0f; scrollY = 0f;

        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
        UpdateSeedLabel();
        Invalidate();
    }

    void UpdateSeedLabel()
    {
        seedLabel.ForeColor = Color.LightGray;
        seedLabel.Text = "seed hash = 0x" + ((uint)p.seed).ToString("X8") + "   底图已按该种子确定性生成";
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
            g.DrawString(string.Format("seed={0}  hash=0x{1:X8}  (输入13位编码点[生成底图])",
                currentCode, (uint)p.seed), f, Brushes.LightGreen, 8, 30);
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
        Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
        {
            try { System.IO.File.WriteAllText("viewer_error.log", e.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        {
            try { System.IO.File.WriteAllText("viewer_error.log", (e.ExceptionObject as Exception).ToString()); } catch { }
        };
        Application.Run(new StarFieldViewerForm());
    }
}
