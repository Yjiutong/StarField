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
// ------------------------------------------------------------------
// Star canvas: owns the field caches and draws the smooth-cruise star field
// ------------------------------------------------------------------
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
    byte[] twCacheByte;
    // Reused render buffer (no per-frame allocation) + FPS counter
    Bitmap renderBmp;
    int renderW = -1, renderH = -1;
    int fpsFrames, fpsLastTick;
    public float fpsDisplay;
    // Cached GDI objects (no per-frame Font allocation / handle churn)
    Font hudFont = new Font("Consolas", 10);
    // Debounced param refresh shows immediate feedback: clearing=true 鈫?canvas stays
    // black until the mask rebuild commits (see ClearNow / RefreshField).
    public bool clearing;
    // Per-frame breakdown profiler (every 30 frames writes viewer_perf.log)
    int perfCount;
    long perfTFrame, perfTRender, perfTBlit;
    long stTicks; int stFrameMs;

    // Adaptive viewport: the canvas fills the window, so the visible world (in cells)
    // grows with the window. Rendering is capped at 1920x1080 and stretched, keeping
    // the 1080P pixel budget and a constant star size on any screen.
    int CellZoomPx { get { return Cell * (int)Zoom; } }
    int Cols { get { return Width / CellZoomPx; } }
    int Rows { get { return Height / CellZoomPx; } }
    public int RenderW { get { return Math.Min(Width, 1920); } }
    public int RenderH { get { return Math.Min(Height, 1080); } }
    public float RunTime { get { return (float)(Environment.TickCount / 1000.0) - t0; } }

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
        clearing = false;   // commit: stop the black feedback frame
        RebuildCache();
        UpdateTwinkle(Environment.TickCount);
        Invalidate();
    }

    /// <summary>Immediate visual feedback: blank the canvas now; the mask rebuild
    /// (debounced) commits later via RefreshField. Used so "thresh=1 鈫?0 stars" is
    /// seen instantly instead of after the 400ms debounce.</summary>
    public void ClearNow()
    {
        clearing = true;
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
        if (twCacheByte == null || twCacheByte.Length < twW * twH) twCacheByte = new byte[twW * twH];
        for (int gy = 0; gy < twH; gy++)
            for (int gx = 0; gx < twW; gx++)
            {
                // Full breathing chain: breathe factor + 3脳3 neighbor suppression.
                // Stored as 8-bit fixed point (0..255) so the render loop reads a byte.
                float v = StarFieldAlgo.StarField.SuppressedBrightness(twOx + gx, twOy + gy, p, time);
                int vv = (int)(v * 255f);
                twCacheByte[gy * twW + gx] = (byte)(vv > 255 ? 255 : vv);
            }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        stFrameMs = Environment.TickCount;
        stTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        // Pixel-art style + fastest stretch: nearest-neighbor scaling (GDI+ default
        // high-quality interpolation would cost ~10x on the full-screen blit).
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        // Opaque composition: skips source-alpha blending on the full-screen blit.
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        if (clearing)
        {
            // Debounce feedback: blank the whole canvas immediately. The commit
            // (RefreshField) rebuilds the mask and clears this flag.
            g.Clear(Color.Black);
            return;
        }
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
            fixed (byte* tw = twCacheByte)
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
                    int fyI = (int)(fy * 256f);                  // 8-bit fixed point
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
                    int fxI = (int)(fx * 256f);                  // 8-bit fixed point
                    int gxCur = (int)Math.Floor(wx / Cell) - twOx0;
                    float cellEdge = (gxCur + twOx0 + 1) * cellW0;
                    if (gxCur < 0) gxCur = 0; else if (gxCur >= twWx) gxCur = twWx - 1;

                    for (int xx = 0; xx < rw; xx++)
                    {
                        // Integer fixed-point bilinear (8-bit): much higher throughput
                        // than float math for 2M pixels/frame.
                        int s00 = sc[row0 + cacheX];
                        int s10 = sc[row0 + cacheX + 1];
                        int s01 = sc[row1 + cacheX];
                        int s11 = sc[row1 + cacheX + 1];
                        int invFx = 256 - fxI;
                        int top = (s00 * invFx + s10 * fxI) >> 8;
                        int bot = (s01 * invFx + s11 * fxI) >> 8;
                        int star = (top * (256 - fyI) + bot * fyI) >> 8;   // 0..255

                        // Twinkle is bound to the star's OWN cell: stars never cross a cell
                        // boundary (maxOffset + maxRadius <= 0.5 cell edge), so the pixel's
                        // cell IS the star's cell - the whole translated star breathes together.
                        int v = (star * tw[twRow + gxCur]) >> 8;   // byte twinkle (0..255)
                        if (v > 255) v = 255;
                        byte b = (byte)v;
                        dptr[0] = b; dptr[1] = b; dptr[2] = b;
                        dptr += 3;

                        wx += xStepW;
                        fx += xStepW;
                        if (fx >= 1f) { fx -= 1f; cacheX++; if (cacheX >= cacheWO - 1) { cacheX = cacheWO - 2; fx = 1f; } }
                        fxI = (int)(fx * 256f);
                        if (wx >= cellEdge) { gxCur++; cellEdge += cellW0; if (gxCur >= twWx) gxCur = twWx - 1; }
                    }
                }
            }
        }
        renderBmp.UnlockBits(bd);
        perfTRender += (System.Diagnostics.Stopwatch.GetTimestamp() - stTicks);
        stTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        // Stretch blit via DrawImage (NearestNeighbor, set above). StretchBlt/GetHdc
        // was reverted: mixing GDI+ DrawString with GetHdc corrupted the Graphics state
        // and left the canvas black on some paths - DrawImage is reliable and fast enough.
        g.DrawImage(renderBmp, 0, 0, Width, Height);
        perfTBlit += (System.Diagnostics.Stopwatch.GetTimestamp() - stTicks);
        perfTFrame += (System.Diagnostics.Stopwatch.GetTimestamp() - stTicks);
        perfCount++;
        if (perfCount >= 30)
        {
            try
            {
                double fr = 1000.0 * perfTFrame / 30 / System.Diagnostics.Stopwatch.Frequency;
                double rr = 1000.0 * perfTRender / 30 / System.Diagnostics.Stopwatch.Frequency;
                double br = 1000.0 * perfTBlit / 30 / System.Diagnostics.Stopwatch.Frequency;
                System.IO.File.AppendAllText(@"E:\StarField\viewer_perf.log",
                    string.Format("frame={0:F1}ms render={1:F1}ms blit={2:F1}ms fps={3:F0}\r\n",
                        fr, rr, br, fpsDisplay));
            }
            catch { }
            perfCount = 0; perfTFrame = 0; perfTRender = 0; perfTBlit = 0;
        }
    }
}

// ------------------------------------------------------------------
// Main form: layout only, owns the HUD input row
// ------------------------------------------------------------------
public class StarFieldViewerForm : Form
{
    StarFieldAlgo.StarField.Params p;
    StarCanvas starCanvas;
    Timer timer;
    int lastTitleTick;
    // 1ms system-timer resolution (timeBeginPeriod) makes the Timer intervals real:
    // without it the 15.6ms system clock caps the actual rate at ~32Hz.
    [DllImport("winmm.dll")]
    static extern uint timeBeginPeriod(uint uPeriod);
    [DllImport("winmm.dll")]
    static extern uint timeEndPeriod(uint uPeriod);

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
    // Neighbor-suppression coefficient control (3脳3 linked breathing)
    NumericUpDown suppressBox;
    Label suppressLabel;
    // Frame-rate control (5..60 fps, default 30)
    NumericUpDown fpsBox;
    Label fpsLabel;
    // HUD overlay labels (form-level, above the canvas): text drawn as Labels, not
    // per-frame DrawString, to avoid GDI/GDI+ handle mixing after GetHdc/StretchBlt.
    Label hudAnchor, hudSeed, hudRender;
    // Debounced parameter refresh: rapid spinner clicks only commit after 400ms of
    // inactivity; direct typing commits immediately on Enter / focus-loss.
    Timer paramDebounce;
    bool pendingRebuild;

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
        // Adaptive canvas: fills the window below the HUD row (40px). The visible world
        // (cell count) grows with the window; rendering stays capped at 1920x1080.
        starCanvas = new StarCanvas(p) { Location = new Point(0, 40), Size = new Size(wa.Width, wa.Height - 40) };
        Resize += delegate(object s2, EventArgs e2)
        {
            starCanvas.Size = new Size(ClientSize.Width, ClientSize.Height - 40);
        };
        Controls.Add(starCanvas);

        // Seed input row (HUD, above the canvas): 13-char code 鈫?regenerate the whole base field
        Font capFont = new Font("Microsoft YaHei", 10, FontStyle.Bold);
        var seedCaption = new Label { Left = 8, Top = 4, Width = 104, Height = 26, Text = "种子(13位)",
            ForeColor = Color.Gold, Font = capFont };
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
        var breatheCaption = new Label { Left = 840, Top = 4, Width = 70, Height = 26, Text = "鍛煎惛绯绘暟",
            ForeColor = Color.Gold, Font = capFont };
        breatheBox = new NumericUpDown { Left = 914, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 1m, Font = new Font("Consolas", 10) };
        breatheBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.twinkleAmplitude = (float)breatheBox.Value;
            UpdateBreatheLabel();
            ScheduleParamUpdate(false);   // breathe: repaint only (twinkle cache re-eval)
        };
        breatheBox.Leave += delegate { ImmediateParamUpdate(false); };
        breatheBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        { if (e2.KeyCode == Keys.Enter) { ImmediateParamUpdate(false); e2.SuppressKeyPress = true; } };
        breatheLabel = new Label { Left = 996, Top = 4, Width = 120, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateBreatheLabel();
        // Offset coefficient (0 = star at cell center, 1 = full 2D hash offset, maxOffset 0.45)
        var offsetCaption = new Label { Left = 1120, Top = 4, Width = 70, Height = 26, Text = "鍋忕Щ绯绘暟",
            ForeColor = Color.Gold, Font = capFont };
        offsetBox = new NumericUpDown { Left = 1194, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 1m, Font = new Font("Consolas", 10) };
        offsetBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.offsetCoeff = (float)offsetBox.Value;
            UpdateOffsetLabel();
            ScheduleParamUpdate(true);    // offset: rebuild star mask (new positions)
        };
        offsetBox.Leave += delegate { ImmediateParamUpdate(true); };
        offsetBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        { if (e2.KeyCode == Keys.Enter) { ImmediateParamUpdate(true); e2.SuppressKeyPress = true; } };
        offsetLabel = new Label { Left = 1276, Top = 4, Width = 120, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateOffsetLabel();
        // Hash threshold (star qualification): higher = fewer stars, lower = denser field
        var thresholdCaption = new Label { Left = 1400, Top = 4, Width = 70, Height = 26, Text = "哈希阈值",
            ForeColor = Color.Gold, Font = capFont };
        thresholdBox = new NumericUpDown { Left = 1474, Top = 0, Width = 76, DecimalPlaces = 3,
            Minimum = 0.01m, Maximum = 1m, Increment = 0.01m, Value = 0.6m, Font = new Font("Consolas", 10) };
        thresholdBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.qualifyThreshold = (float)thresholdBox.Value;
            UpdateThresholdLabel();
            ScheduleParamUpdate(true);    // hash threshold: rebuild star mask (star count)
        };
        // Initial sync: the spinner value is set before the ValueChanged hookup, so push
        // it into p explicitly (the hard-coded 0.6f on p was removed - HUD and render
        // must always agree, and thresh=1.0 must yield 0 stars).
        p.qualifyThreshold = (float)thresholdBox.Value;
        starCanvas.RefreshField();
        thresholdBox.Leave += delegate { ImmediateParamUpdate(true); };
        thresholdBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        { if (e2.KeyCode == Keys.Enter) { ImmediateParamUpdate(true); e2.SuppressKeyPress = true; } };
        thresholdLabel = new Label { Left = 1556, Top = 4, Width = 150, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateThresholdLabel();
        // Neighbor suppression (0 = no 3脳3 linked dimming, 1 = peak cell strongly dims its 8 neighbors)
        var suppressCaption = new Label { Left = 1720, Top = 4, Width = 70, Height = 26, Text = "鎶戝埗绯绘暟",
            ForeColor = Color.Gold, Font = capFont };
        suppressBox = new NumericUpDown { Left = 1794, Top = 0, Width = 76, DecimalPlaces = 2,
            Minimum = 0m, Maximum = 1m, Increment = 0.05m, Value = 0.5m, Font = new Font("Consolas", 10) };
        suppressBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            p.suppressCoeff = (float)suppressBox.Value;
            UpdateSuppressLabel();
            ScheduleParamUpdate(false);   // suppression: repaint only (twinkle re-eval)
        };
        suppressBox.Leave += delegate { ImmediateParamUpdate(false); };
        suppressBox.KeyDown += delegate(object s2, KeyEventArgs e2)
        { if (e2.KeyCode == Keys.Enter) { ImmediateParamUpdate(false); e2.SuppressKeyPress = true; } };
        suppressLabel = new Label { Left = 1876, Top = 4, Width = 150, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateSuppressLabel();
        // Frame-rate target (5..60 fps, default 30): Timer.Interval = 1000/f
        var fpsCaption = new Label { Left = 2030, Top = 4, Width = 70, Height = 26, Text = "甯х巼",
            ForeColor = Color.Gold, Font = capFont };
        fpsBox = new NumericUpDown { Left = 2104, Top = 0, Width = 60, DecimalPlaces = 0,
            Minimum = 5m, Maximum = 60m, Increment = 1m, Value = 30m, Font = new Font("Consolas", 10) };
        fpsBox.ValueChanged += delegate(object s2, EventArgs e2)
        {
            int f = (int)fpsBox.Value;
            // System timer granularity is 15.6ms: map the fps target to a whole number
            // of ticks (n = round(1000/f / 15.6)), so 60->4fps, 30->2fps, 5->.9fps.
            int n = Math.Max(1, (int)Math.Round(64.1 / f));
            timer.Interval = n <= 1 ? 8 : (int)(n * 15.6);   // n=1 must stay <15.6ms (1 tick)
            UpdateFpsLabel();
        };
        fpsLabel = new Label { Left = 2170, Top = 4, Width = 120, Height = 26, ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10) };
        UpdateFpsLabel();
        Controls.Add(seedCaption); Controls.Add(seedBox); Controls.Add(genBtn); Controls.Add(seedLabel);
        Controls.Add(breatheCaption); Controls.Add(breatheBox); Controls.Add(breatheLabel);
        Controls.Add(offsetCaption); Controls.Add(offsetBox); Controls.Add(offsetLabel);
        Controls.Add(thresholdCaption); Controls.Add(thresholdBox); Controls.Add(thresholdLabel);
        Controls.Add(suppressCaption); Controls.Add(suppressBox); Controls.Add(suppressLabel);
        Controls.Add(fpsCaption); Controls.Add(fpsBox); Controls.Add(fpsLabel);
        UpdateSeedLabel();

        // HUD overlay labels above the canvas (semi-transparent dark backing).
        hudAnchor = new Label { Left = 10, Top = 48, Width = 760, Height = 22,
            ForeColor = Color.White, BackColor = Color.FromArgb(110, 0, 0, 0), Font = new Font("Consolas", 10) };
        hudSeed = new Label { Left = 10, Top = 74, Width = 760, Height = 22,
            ForeColor = Color.LightGreen, BackColor = Color.FromArgb(110, 0, 0, 0), Font = new Font("Consolas", 10) };
        hudRender = new Label { Left = 10, Top = 100, Width = 420, Height = 22,
            ForeColor = Color.Yellow, BackColor = Color.FromArgb(110, 0, 0, 0), Font = new Font("Consolas", 10) };
        Controls.Add(hudAnchor); Controls.Add(hudSeed); Controls.Add(hudRender);
        UpdateHudText();

        timeBeginPeriod(1);   // request 1ms system-timer resolution
        paramDebounce = new Timer { Interval = 400 };   // 400ms inactivity 鈫?commit parameter
        paramDebounce.Tick += delegate(object s2, EventArgs e2) { ExecuteParamUpdate(); };
        timer = new Timer { Interval = 31 };   // default 30fps target 鈫?2 ticks 鈮?32fps real
        timer.Tick += delegate(object s, EventArgs e2)
        {
            starCanvas.Tick(Environment.TickCount);
            if (Environment.TickCount - lastTitleTick >= 500)
            {
                lastTitleTick = Environment.TickCount;
                Text = string.Format("Star Field Viewer (fps {0:F0})", starCanvas.fpsDisplay);
                UpdateHudText();
            }
        };
        timer.Start();
    }

    void UpdateHudText()
    {
        hudAnchor.Text = string.Format("anchor ({0},{1})   smooth cruise 5px/s (sub-pixel)   t={2:F1}s   (Esc=close)",
            starCanvas.view.curX, starCanvas.view.curY, starCanvas.RunTime);
        hudSeed.Text = string.Format("seed={0}  hash=0x{1:X8}  (输入13位编码点[生成底图])",
            starCanvas.currentCode, (uint)starCanvas.p.seed);
        hudRender.Text = string.Format("render {0}x{1}  fps {2:F0}", starCanvas.RenderW, starCanvas.RenderH, starCanvas.fpsDisplay);
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

    void UpdateFpsLabel()
    {
        int f = (int)fpsBox.Value;
        fpsLabel.Text = "fps鐩爣=" + f;
    }

    // ------------------------------------------------------------------
    // Debounced parameter refresh
    // ------------------------------------------------------------------
    // Spinner clicks commit 400ms after the last change (rapid clicking batches into
    // one rebuild); direct typing commits immediately on Enter or focus loss.
    void ScheduleParamUpdate(bool rebuildField)
    {
        pendingRebuild = pendingRebuild || rebuildField;
        // Immediate visual feedback: blank the canvas now; the debounced commit
        // rebuilds the mask 400ms after the last change.
        starCanvas.ClearNow();
        paramDebounce.Stop();
        paramDebounce.Start();
    }

    void ExecuteParamUpdate()
    {
        paramDebounce.Stop();
        if (pendingRebuild) starCanvas.RefreshField();   // rebuild star mask + repaint
        else starCanvas.Invalidate();
        pendingRebuild = false;
    }

    void ImmediateParamUpdate(bool rebuildField)
    {
        paramDebounce.Stop();
        pendingRebuild = false;
        if (rebuildField) starCanvas.RefreshField();
        else starCanvas.Invalidate();
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
