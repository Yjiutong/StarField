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
    // Reused render buffer (no per-frame allocation) + FPS counter
    Bitmap renderBmp;
    int renderW = -1, renderH = -1;
    int fpsFrames, fpsLastTick;
    public float fpsDisplay;

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
        // FPS counter (1s window)
        fpsFrames++;
        if (nowMs - fpsLastTick >= 1000)
        {
            fpsDisplay = fpsFrames * 1000f / (nowMs - fpsLastTick);
            fpsFrames = 0; fpsLastTick = nowMs;
        }
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
                // Full breathing chain: breathe factor + 3×3 neighbor suppression.
                float v = StarFieldAlgo.StarField.SuppressedBrightness(twOx + gx, twOy + gy, p, time);
                twCache[gy * twW + gx] = v;
            }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        // Pixel-art style + fastest stretch: nearest-neighbor scaling (GDI+ default
        // high-quality interpolation would cost ~10x on the full-screen blit).
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        float time = (float)(Environment.TickCount / 1000.0) - t0;

        float baseX = cacheOx - scrollX / Zoom;
        float baseY = cacheOy - scrollY / Zoom;
        float invZ = 1f / Zoom;

        // Adaptive render: sample up to 1920x1080, stretch to the full canvas.
        int rw = RenderW, rh = RenderH;
        float xStepW = (Width / (float)rw) * invZ;   // world px per render px (x)
        float yStepW = (Height / (float)rh) * invZ;  // world px per render px (y)

        // Reuse the render buffer: no per-frame 6MB allocation / GC pressure.
        if (renderBmp == null || renderW != rw || renderH != rh)
        {
            if (renderBmp != null) renderBmp.Dispose();
            renderBmp = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
            renderW = rw; renderH = rh;
        }
        var bd = renderBmp.LockBits(new Rectangle(0, 0, rw, rh), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        int stride = bd.Stride;
        int cacheWO = cacheW, cacheHO = cacheH;
        int cacheOX = (int)cacheOx, cacheOY = (int)cacheOy;
        int twWx = twW, twOx0 = twOx, twOy0 = twOy;
        int cellW0 = Cell;

        unsafe
        {
            fixed (byte* sc = starCache)
            fixed (float* tw = twCache)
            {
                byte* dst0 = (byte*)bd.Scan0;
                for (int yy = 0; yy < rh; yy++)
                {
                    float worldY = baseY + yy * yStepW;
                    int cacheY = (int)worldY - cacheOY;
                    float fy;
                    if (cacheY < 0) { cacheY = 0; fy = 0f; }
                    else if (cacheY >= cacheHO - 1) { cacheY = cacheHO - 2; fy = 1f; }
                    else fy = worldY - cacheOY - cacheY;
                    int row0 = cacheY * cacheWO, row1 = row0 + cacheWO;
                    int gy0 = (int)Math.Floor(worldY / Cell) - twOy0;
                    if (gy0 < 0) gy0 = 0; else if (gy0 >= twH) gy0 = twH - 1;
                    int twRow = gy0 * twWx;

                    // Walk the row with incremental cell index (no per-pixel division).
                    byte* dptr = dst0 + yy * stride;
                    float wx = baseX;
                    int cacheX = (int)wx - cacheOX;
                    float fx;
                    if (cacheX < 0) { cacheX = 0; fx = 0f; }
                    else if (cacheX >= cacheWO - 1) { cacheX = cacheWO - 2; fx = 1f; }
                    else fx = wx - cacheOX - cacheX;
                    int gxCur = (int)Math.Floor(wx / Cell) - twOx0;
                    float cellEdge = (gxCur + twOx0 + 1) * cellW0;
                    if (gxCur < 0) gxCur = 0; else if (gxCur >= twWx) gxCur = twWx - 1;

                    for (int xx = 0; xx < rw; xx++)
                    {
                        float s00 = sc[row0 + cacheX];
                        float s10 = sc[row0 + cacheX + 1];
                        float s01 = sc[row1 + cacheX];
                        float s11 = sc[row1 + cacheX + 1];
                        float top = s00 + (s10 - s00) * fx;
                        float bot = s01 + (s11 - s01) * fx;
                        float star = top + (bot - top) * fy;          // bilinear (0..255)

                        // Twinkle is bound to the star's OWN cell: stars never cross a cell
                        // boundary (maxOffset + maxRadius <= 0.5 cell edge), so the pixel's
                        // cell IS the star's cell — the whole translated star breathes together.
                        float bright = tw[twRow + gxCur];
                        float v = star * bright;
                        if (v > 255f) v = 255f;
                        byte b = (byte)v;
                        dptr[0] = b; dptr[1] = b; dptr[2] = b;
                        dptr += 3;

                        wx += xStepW;
                        cacheX = (int)wx - cacheOX;
                        if (cacheX < 0) { cacheX = 0; fx = 0f; }
                        else if (cacheX >= cacheWO - 1) { cacheX = cacheWO - 2; fx = 1f; }
                        else fx = wx - cacheOX - cacheX;
                        if (wx >= cellEdge) { gxCur++; cellEdge += cellW0; if (gxCur >= twWx) gxCur = twWx - 1; }
                    }
                }
            }
        }
        renderBmp.UnlockBits(bd);
        g.DrawImage(renderBmp, 0, 0, Width, Height);

        using (var f = new Font("Consolas", 10))
        {
            g.DrawString(string.Format("anchor ({0},{1})   smooth cruise 5px/s (sub-pixel)   t={2:F1}s",
                view.curX, view.curY, time), f, Brushes.White, 8, 0);
            g.DrawString("auto cruising (Esc=close)", f, Brushes.Orange, 752, 0);
            g.DrawString(string.Format("seed={0}  hash=0x{1:X8}  (输入13位编码点[生成底图])",
                currentCode, (uint)p.seed), f, Brushes.LightGreen, 8, 30);
            g.DrawString(string.Format("render {0}x{1}  fps {2:F0}", rw, rh, fpsDisplay), f, Brushes.Yellow, 8, 60);
        }
    }
}

// ——— Main form: layout only, owns the HUD input row ———
public class StarFieldViewerForm : Form
{
    StarFieldAlgo.StarField.Params p;
    StarCanvas starCanvas;
    Timer timer;
    int lastTitleTick;

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
    // Hash threshold control (star qualification: higher = fewer stars)
    NumericUpDown thresholdBox;
    Label thresholdLabel;
    // Neighbor-suppression coefficient control (3×3 linked breathing)
    NumericUpDown suppressBox;
    Label suppressLabel;

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
        // Hash threshold (star qualification): higher = fewer stars, lower = denser field
        var thresholdCaption = new Label { Left = 1400, Top = 4, Width = 70, Height = 26, Text = "哈希阈值",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        thresholdBox = new NumericUpDown { Left = 1474, Top = 0, Width = 76, DecimalPlaces = 3,
            Minimum = 0.01m, Maximum = 1m, Increment = 0.01m, Value = 0.6m, Font = new Font("Consolas", 10) };
        thresholdBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.qualifyThreshold = (float)thresholdBox.Value;
            starCanvas.RefreshField();
        };
        thresholdLabel = new Label { Left = 1556, Top = 4, Width = 150, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateThresholdLabel();
        // Neighbor suppression (0 = no 3×3 linked dimming, 1 = peak cell strongly dims its 8 neighbors)
        var suppressCaption = new Label { Left = 1720, Top = 4, Width = 70, Height = 26, Text = "抑制系数",
            ForeColor = Color.White, Font = new Font("Microsoft YaHei", 10) };
        suppressBox = new NumericUpDown { Left = 1794, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 0.5m, Font = new Font("Consolas", 10) };
        suppressBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.suppressCoeff = (float)suppressBox.Value;
            starCanvas.Invalidate();
        };
        suppressLabel = new Label { Left = 1876, Top = 4, Width = 150, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateSuppressLabel();
        Controls.Add(seedCaption); Controls.Add(seedBox); Controls.Add(genBtn); Controls.Add(seedLabel);
        Controls.Add(breatheCaption); Controls.Add(breatheBox); Controls.Add(breatheLabel);
        Controls.Add(offsetCaption); Controls.Add(offsetBox); Controls.Add(offsetLabel);
        Controls.Add(thresholdCaption); Controls.Add(thresholdBox); Controls.Add(thresholdLabel);
        Controls.Add(suppressCaption); Controls.Add(suppressBox); Controls.Add(suppressLabel);
        UpdateSeedLabel();

        timer = new Timer { Interval = 16 };   // ~60fps, repaints only the canvas child
        timer.Tick += delegate(object s, EventArgs e2)
        {
            starCanvas.Tick(Environment.TickCount);
            if (Environment.TickCount - lastTitleTick >= 500)
            {
                lastTitleTick = Environment.TickCount;
                Text = string.Format("Star Field Viewer (fps {0:F0})", starCanvas.fpsDisplay);
            }
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

    void UpdateThresholdLabel()
    {
        thresholdLabel.Text = "thresh=" + thresholdBox.Value.ToString("0.000");
    }

    void UpdateSuppressLabel()
    {
        suppressLabel.Text = "suppr=" + suppressBox.Value.ToString("0.00");
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
