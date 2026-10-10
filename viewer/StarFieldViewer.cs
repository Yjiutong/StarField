// Star Field Viewer - interactive demo of the smooth-cruise viewport.
// Pixel-level smooth movement: bilinear sub-pixel sampling (no whole-cell jumps).
// Performance: static star-mask cache (rebuilt on anchor change) + per-cell twinkle cache.
//
// Layout: the star field is drawn on its OWN child control (StarCanvas) instead of the
// form's OnPaint. This keeps the per-frame repaint scoped to the canvas only, so the
// HUD input controls (seed box / button / breathe box) are never starved of WM_PAINT
// and stay visible on screen.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StarFieldAlgo;
// ——— Star canvas: owns the field caches and draws the smooth-cruise star field ———
public class StarCanvas : Control
{
    public StarFieldAlgo.StarField.Params p;
    public StarView.View view = new StarView.View { curX = 20, curY = 24 };
    public string currentCode = SeedCodec.ExampleSeed;

    const int Cell = 40;
    const float Zoom = 2f;
    const float autoSpeed = 5f;    // px/s smooth auto-cruise

    float t0;
    float scrollX, scrollY;
    int lastTick;
    byte[] starCache;
    int cacheW, cacheH;
    float cacheOx, cacheOy;
    float[] twCache;
    int twOx, twOy, twW, twH;

    // Adaptive viewport: the canvas fills the window, so the visible world (in cells)
    // grows with the window. Rendering is capped at 1920x1080 and stretched, keeping
    // the 1080P pixel budget and a constant star size on any screen.
    int CellZoomPx { get { return Cell * (int)Zoom; } }
    int Cols { get { return Width / CellZoomPx; } }
    int Rows { get { return Height / CellZoomPx; } }
    int RenderW { get { return Math.Min(Width, 1920); } }
    int RenderH { get { return Math.Min(Height, 1080); } }

    public StarCanvas(StarFieldAlgo.StarField.Params prm)
    {
        p = prm;
        t0 = (float)(Environment.TickCount / 1000.0);
        lastTick = Environment.TickCount;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Black;
        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
        Invalidate();
    }

    public void Regenerate()
    {
        view = new StarView.View { curX = 20, curY = 24 };
        scrollX = 0f; scrollY = 0f;
        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
        Invalidate();
    }

    // Refresh after parameter changes that alter the star mask (e.g. offset coefficient).
    public void RefreshField()
    {
        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
        Invalidate();
    }

    public void Tick(int nowMs)
    {
        float dt = (nowMs - lastTick) / 1000f;
        lastTick = nowMs;
        float stepPx = Cell * Zoom;
        scrollX += autoSpeed * dt; scrollY += autoSpeed * dt;
        while (scrollX >= stepPx && scrollY >= stepPx)
        {
            scrollX -= stepPx; scrollY -= stepPx;
            view = StarView.MoveNorthWest(view);
            RebuildCache();
        }
        UpdateTwinkle(nowMs);
        Invalidate();
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
        int cols = Cols + 2, rows = Rows + 2;
        cacheW = cols * Cell; cacheH = rows * Cell;
        cacheOx = (view.curX - 1) * Cell; cacheOy = (view.curY - 1) * Cell;
        starCache = BuildStarRegion((int)cacheOx, (int)cacheOy, cacheW, cacheH);
    }

    void UpdateTwinkle(int nowMs)
    {
        float time = nowMs / 1000f - t0;
        twOx = view.curX - 1; twOy = view.curY - 1;
        twW = Cols + 3; twH = Rows + 3;
        if (twCache == null || twCache.Length < twW * twH) twCache = new float[twW * twH];
        for (int gy = 0; gy < twH; gy++)
            for (int gx = 0; gx < twW; gx++)
            {
                float tw = StarFieldAlgo.StarField.TwinkleBrightness(twOx + gx, twOy + gy, p.seed, time);
                twCache[gy * twW + gx] = 1f + (tw - 1f) * p.twinkleAmplitude;   // base breathe factor
            }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        float time = (float)(Environment.TickCount / 1000.0) - t0;

        float baseX = cacheOx - scrollX / Zoom;
        float baseY = cacheOy - scrollY / Zoom;
        float invZ = 1f / Zoom;

        // Adaptive render: sample up to 1920x1080, stretch to the full canvas.
        int rw = RenderW, rh = RenderH;
        float xStep = Width / (float)rw;
        float yStep = Height / (float)rh;

        var bmp = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, rw, rh), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var dst = new byte[bd.Stride * rh];
        int twOxx = twOx, twOyy = twOy;

        for (int yy = 0; yy < rh; yy++)
        {
            float worldY = baseY + yy * yStep * invZ;
            int cacheY = (int)worldY - (int)cacheOy;
            float fy;
            if (cacheY < 0) { cacheY = 0; fy = 0f; }
            else if (cacheY >= cacheH - 1) { cacheY = cacheH - 2; fy = 1f; }
            else fy = worldY - cacheOy - cacheY;
            int row0 = cacheY * cacheW, row1 = (cacheY + 1) * cacheW;
            int gy0 = (int)Math.Floor(worldY / Cell) - twOyy;
            if (gy0 < 0) gy0 = 0; if (gy0 >= twH) gy0 = twH - 1;

            for (int xx = 0; xx < rw; xx++)
            {
                float worldX = baseX + xx * xStep * invZ;
                int cacheX = (int)worldX - (int)cacheOx;
                float fx;
                if (cacheX < 0) { cacheX = 0; fx = 0f; }
                else if (cacheX >= cacheW - 1) { cacheX = cacheW - 2; fx = 1f; }
                else fx = worldX - cacheOx - cacheX;
                float s00 = starCache[row0 + cacheX];
                float s10 = starCache[row0 + cacheX + 1];
                float s01 = starCache[row1 + cacheX];
                float s11 = starCache[row1 + cacheX + 1];
                float top = s00 + (s10 - s00) * fx;
                float bot = s01 + (s11 - s01) * fx;
                float star = top + (bot - top) * fy;          // bilinear (0..255)

                // Twinkle is bound to the star's OWN cell: stars never cross a cell
                // boundary (maxOffset + maxRadius <= 0.5 cell edge), so the pixel's cell
                // IS the star's cell — the whole translated star breathes together.
                int gx0 = (int)Math.Floor(worldX / Cell) - twOxx;
                if (gx0 < 0) gx0 = 0; if (gx0 >= twW) gx0 = twW - 1;
                float bright = twCache[gy0 * twW + gx0];      // per-cell twinkle (breathe factor applied)

                float v = star * bright;
                if (v > 255f) v = 255f;
                byte b = (byte)v;
                int i = yy * bd.Stride + xx * 3;
                dst[i] = b; dst[i + 1] = b; dst[i + 2] = b;
            }
        }
        Marshal.Copy(dst, 0, bd.Scan0, dst.Length);
        bmp.UnlockBits(bd);
        g.DrawImage(bmp, 0, 0, Width, Height);
        bmp.Dispose();

        using (var f = new Font("Consolas", 10))
        {
            g.DrawString(string.Format("anchor ({0},{1})   smooth cruise 5px/s (sub-pixel)   t={2:F1}s",
                view.curX, view.curY, time), f, Brushes.White, 8, 0);
            g.DrawString("auto cruising (Esc=close)", f, Brushes.Orange, 752, 0);
            g.DrawString(string.Format("seed={0}  hash=0x{1:X8}  (输入13位编码点[生成底图])",
                currentCode, (uint)p.seed), f, Brushes.LightGreen, 8, 30);
        }
    }
}

// ——— Main form: layout only, owns the HUD input row ———
public class StarFieldViewerForm : Form
{
    StarFieldAlgo.StarField.Params p;
    StarCanvas starCanvas;
    Timer timer;

    // Seed input controls
    TextBox seedBox;
    Button genBtn;
    Label seedLabel;
    // Breathe factor control
    NumericUpDown breatheBox;
    Label breatheLabel;
    // Offset coefficient control
    NumericUpDown offsetBox;
    Label offsetLabel;

    public StarFieldViewerForm()
    {
        Text = "Star Field Viewer (adaptive full-screen, 1080P auto-cruise, twinkling stars)";
        var wa = Screen.PrimaryScreen.WorkingArea;
        // Adaptive: maximize to fill the whole working area on any resolution.
        // Window chrome (title bar) is absorbed by the OS during maximize, so the
        // client area lands exactly on the screen edge.
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;

        p = StarFieldAlgo.StarField.Default((int)SeedCodec.Hash(SeedCodec.ExampleSeed));
        p.qualifyThreshold = 0.6f;   // demo density: keeps enough stars inside the viewport
        // Adaptive canvas: fills the window below the HUD row (40px). The visible world
        // (cell count) grows with the window; rendering stays capped at 1920x1080.
        starCanvas = new StarCanvas(p) { Location = new Point(0, 40), Size = new Size(wa.Width, wa.Height - 40) };
        Resize += delegate(object s2, EventArgs e2)
        {
            starCanvas.Size = new Size(ClientSize.Width, ClientSize.Height - 40);
        };
        Controls.Add(starCanvas);

        // Seed input row (HUD, above the canvas): 13-char code → regenerate the whole base field
        var seedCaption = new Label { Left = 8, Top = 4, Width = 104, Height = 26, Text = "种子(13位)",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        seedBox = new TextBox { Left = 118, Top = 0, Width = 210, Font = new Font("Consolas", 12) };
        seedBox.Text = SeedCodec.ExampleSeed;
        genBtn = new Button { Left = 336, Top = 0, Width = 96, Height = 30, Text = "生成底图" };
        genBtn.Click += delegate(object s2, EventArgs e2) { RegenerateField(seedBox.Text); };
        seedBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        {
            if (e2.KeyCode == Keys.Enter) { RegenerateField(seedBox.Text); e2.SuppressKeyPress = true; }
        };
        seedLabel = new Label { Left = 448, Top = 4, Width = 380, Height = 26, ForeColor = Color.LightGray,
            Font = new Font("Consolas", 10) };
        // Base breathe factor (0 = constant bright, 1 = full twinkle)
        var breatheCaption = new Label { Left = 840, Top = 4, Width = 70, Height = 26, Text = "呼吸系数",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        breatheBox = new NumericUpDown { Left = 914, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 1m, Font = new Font("Consolas", 10) };
        breatheBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.twinkleAmplitude = (float)breatheBox.Value;
            starCanvas.Invalidate();
        };
        breatheLabel = new Label { Left = 996, Top = 4, Width = 120, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateBreatheLabel();
        // Offset coefficient (0 = star at cell center, 1 = full 2D hash offset, maxOffset 0.45)
        var offsetCaption = new Label { Left = 1120, Top = 4, Width = 70, Height = 26, Text = "偏移系数",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        offsetBox = new NumericUpDown { Left = 1194, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 1m, Font = new Font("Consolas", 10) };
        offsetBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.offsetCoeff = (float)offsetBox.Value;
            starCanvas.RefreshField();
        };
        offsetLabel = new Label { Left = 1276, Top = 4, Width = 120, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateOffsetLabel();
        Controls.Add(seedCaption); Controls.Add(seedBox); Controls.Add(genBtn); Controls.Add(seedLabel);
        Controls.Add(breatheCaption); Controls.Add(breatheBox); Controls.Add(breatheLabel);
        Controls.Add(offsetCaption); Controls.Add(offsetBox); Controls.Add(offsetLabel);
        UpdateSeedLabel();

        timer = new Timer { Interval = 16 };   // ~60fps, repaints only the canvas child
        timer.Tick += delegate(object s, EventArgs e2)
        {
            starCanvas.Tick(Environment.TickCount);
        };
        timer.Start();
    }

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
        starCanvas.currentCode = code;
        starCanvas.Regenerate();
        UpdateSeedLabel();
    }

    void UpdateSeedLabel()
    {
        seedLabel.ForeColor = Color.LightGray;
        seedLabel.Text = "seed hash = 0x" + ((uint)p.seed).ToString("X8") + "   底图已按该种子确定性生成";
    }

    void UpdateBreatheLabel()
    {
        breatheLabel.Text = "breathe=" + breatheBox.Value.ToString("0.00");
    }

    void UpdateOffsetLabel()
    {
        offsetLabel.Text = "offset=" + offsetBox.Value.ToString("0.00");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        // Without DPI awareness the layout is scaled up by the system DPI (e.g. 125%):
        // the window's physical width then exceeds the screen and the right-side HUD
        // controls (offset coefficient) get clipped off-screen.
        try { SetProcessDPIAware(); } catch { }
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
