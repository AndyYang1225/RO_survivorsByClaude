using System.Diagnostics;

using RoHelper.Vision;

namespace RoHelper.Forms;

/// <summary>
/// 視覺除錯視窗：即時顯示 exe 截到的遊戲畫面，用來校正視覺判斷、收集範本。
/// 功能：
/// - 點擊測試：點預覽圖 → 用 CDP 在遊戲的同一點送真實點擊，確認座標換算（dpr）正確
/// - 框選後 OCR、框選後存成範本、對目前畫面找範本（顯示分數和耗時）
/// 全程只截圖和送輸入，不在遊戲頁面執行任何 JS。
/// 按 X 只是隱藏（跟面板一樣），隱藏時截圖計時器也停掉，不在背景白白截圖。
/// </summary>
public sealed class VisionDebugForm : Form
{
    private readonly VisionKit _kit;
    private readonly PictureBox _view = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly TextBox _log = new() { Dock = DockStyle.Bottom, Height = 140, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _live = new() { Text = "即時更新", AutoSize = true, Checked = true };
    private readonly RadioButton _clickMode = new() { Text = "點擊測試", AutoSize = true, Checked = true };
    private readonly RadioButton _selectMode = new() { Text = "框選", AutoSize = true };
    private readonly TextBox _templateName = new() { Width = 120, PlaceholderText = "範本名稱" };
    private readonly ComboBox _templates = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private Frame? _frame;
    private Point? _dragStart;

    /// <summary>框選範圍（圖片座標）</summary>
    private Rectangle? _selection;

    /// <summary>最近一次找範本的結果（圖片座標）</summary>
    private Rectangle? _match;

    /// <summary>截圖進行中：CapturePreviewAsync 偶爾會超過計時器間隔，不重疊呼叫</summary>
    private bool _capturing;

    public VisionDebugForm(VisionKit kit)
    {
        _kit = kit;
        Text = "視覺除錯";
        Font = new Font("Microsoft JhengHei UI", 9f);
        Size = new Size(LogicalToDeviceUnits(1000), LogicalToDeviceUnits(720));

        var snap = new Button { Text = "截一張", AutoSize = true };
        var ocr = new Button { Text = "OCR 框選區", AutoSize = true };
        var save = new Button { Text = "存成範本", AutoSize = true };
        var find = new Button { Text = "找範本", AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        bar.Controls.AddRange([_live, snap, _clickMode, _selectMode, ocr, _templateName, save, _templates, find]);

        // 先加 Fill 再加 Top／Bottom：WinForms 依 z-order 反向處理 Dock
        Controls.Add(_view);
        Controls.Add(_log);
        Controls.Add(bar);

        _timer.Tick += async (_, _) =>
        {
            if (_live.Checked)
                await CaptureAsync();
        };
        snap.Click += async (_, _) => await CaptureAsync();
        ocr.Click += async (_, _) => await OcrSelectionAsync();
        save.Click += (_, _) => SaveTemplate();
        find.Click += (_, _) => FindTemplate();
        _view.MouseDown += OnViewMouseDown;
        _view.MouseMove += OnViewMouseMove;
        _view.MouseUp += OnViewMouseUp;
        _view.Paint += OnViewPaint;

        if (_kit.Ocr is null)
            Log(_kit.OcrError);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible;
        if (Visible)
            RefreshTemplates();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _view.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 截圖

    private async Task CaptureAsync()
    {
        if (_capturing)
            return;
        _capturing = true;
        try
        {
            var sw = Stopwatch.StartNew();
            var frame = await _kit.Grabber.CaptureAsync();
            var ms = sw.ElapsedMilliseconds;
            _frame = frame;
            var old = _view.Image;
            _view.Image = frame.ToBitmap();
            old?.Dispose();
            var dpr = _kit.Viewport.Dpr;
            Text = $"視覺除錯 — {frame.Width}×{frame.Height}，截圖 {ms} ms，dpr {dpr:0.##}，換算網頁寬 {frame.Width / dpr:0}";
        }
        catch (Exception ex)
        {
            // 除錯工具：截圖失敗（頁面重新載入中、WebView2 忙碌）先停掉即時更新並記下來，避免每 0.5 秒洗一次錯誤
            _live.Checked = false;
            Log($"截圖失敗，已停止即時更新 : {ex.Message}");
        }
        finally
        {
            _capturing = false;
        }
    }

    #endregion

    #region 滑鼠：點擊測試／框選

    private void OnViewMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_selectMode.Checked || ToImage(e.Location) is not { } p)
            return;
        _dragStart = p;
        _selection = null;
        _match = null;
        _view.Invalidate();
    }

    private void OnViewMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStart is not { } a || ToImage(e.Location) is not { } b)
            return;
        _selection = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        _view.Invalidate();
    }

    private async void OnViewMouseUp(object? sender, MouseEventArgs e)
    {
        if (_selectMode.Checked)
        {
            _dragStart = null;
            if (_selection is { } r)
                Log($"框選：({r.X}, {r.Y}) {r.Width}×{r.Height}");
            return;
        }
        if (e.Button != MouseButtons.Left || ToImage(e.Location) is not { } p)
            return;

        var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
        try
        {
            await _kit.Input.ClickAsync(x, y);
            Log($"點擊：截圖 ({p.X}, {p.Y}) → 網頁 ({x:0}, {y:0})");
        }
        catch (Exception ex)
        {
            Log($"點擊失敗 : {ex.Message}");
        }
    }

    private void OnViewPaint(object? sender, PaintEventArgs e)
    {
        if (_frame is null)
            return;
        var size = new Size(_frame.Width, _frame.Height);
        if (_selection is { } sel)
        {
            using var pen = new Pen(Color.Lime, 2);
            e.Graphics.DrawRectangle(pen, ZoomMapper.ToBox(_view.ClientSize, size, sel));
        }
        if (_match is { } m)
        {
            using var pen = new Pen(Color.Magenta, 2);
            e.Graphics.DrawRectangle(pen, ZoomMapper.ToBox(_view.ClientSize, size, m));
        }
    }

    private Point? ToImage(Point p)
        => _frame is null ? null : ZoomMapper.ToImage(_view.ClientSize, new Size(_frame.Width, _frame.Height), p);

    #endregion

    #region OCR／範本

    private async Task OcrSelectionAsync()
    {
        if (_kit.Ocr is null)
        {
            Log(_kit.OcrError);
            return;
        }
        if (_frame is null || _selection is not { } r)
        {
            Log("先切到「框選」，在畫面上拖出一塊範圍");
            return;
        }
        var sw = Stopwatch.StartNew();
        var lines = await _kit.Ocr.ReadAsync(_frame, r);
        Log($"OCR（{sw.ElapsedMilliseconds} ms）：" + (lines.Count == 0 ? "（沒有讀到字）" : string.Join(" ｜ ", lines.Select(l => l.Text))));
    }

    /// <summary>
    /// 存成範本。搜尋範圍先存整張截圖、門檻用預設值；要縮小範圍或調門檻，直接改範本旁邊的 JSON（計畫 2 起依各個按鈕校正）。
    /// </summary>
    private void SaveTemplate()
    {
        var name = _templateName.Text.Trim();
        if (_frame is null || _selection is not { } r || name.Length == 0)
        {
            Log("要先框選範圍，並填範本名稱");
            return;
        }
        try
        {
            _kit.Templates.Save(name, _frame.Crop(r), new RectangleF(0, 0, 1, 1), TemplateLibrary.DefaultThreshold, _kit.Viewport.Dpr);
        }
        catch (ArgumentException ex)
        {
            Log(ex.Message);
            return;
        }
        Log($"已存範本「{name}」（{r.Width}×{r.Height}）");
        RefreshTemplates();
        _templates.SelectedItem = name;
    }

    private void FindTemplate()
    {
        if (_frame is null || _templates.SelectedItem is not string name)
        {
            Log("要先有截圖，並選一個範本");
            return;
        }
        var sw = Stopwatch.StartNew();
        var m = _kit.Templates.Match(_frame, name, _kit.Viewport.Dpr);
        var ms = sw.ElapsedMilliseconds;
        var threshold = _kit.Templates.Get(name)!.Threshold;
        _match = m?.Bounds;
        _view.Invalidate();
        Log(m is { } r
            ? $"找「{name}」（{ms} ms）：({r.Bounds.X}, {r.Bounds.Y}) 分數 {r.Score:0.000}，門檻 {threshold:0.00} → {(r.Score >= threshold ? "找到" : "沒找到")}"
            : $"找「{name}」：範本比搜尋範圍還大");
    }

    private void RefreshTemplates()
    {
        var selected = _templates.SelectedItem as string;
        _kit.Templates.Reload();
        _templates.Items.Clear();
        _templates.Items.AddRange([.. _kit.Templates.Names.Order()]);
        if (selected is not null && _templates.Items.Contains(selected))
            _templates.SelectedItem = selected;
    }

    #endregion

    private void Log(string message) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
}
