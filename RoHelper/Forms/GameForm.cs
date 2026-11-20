using System.Diagnostics;
using System.Runtime.InteropServices;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Storage;
using RoHelper.Vision;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoHelper.Forms;

/// <summary>
/// 主視窗：用 WebView2 開遊戲，並在每個頁面「文件建立時」注入 JS 引擎。關掉這個視窗＝結束程式。
/// WebView2 的使用者資料夾固定在 <see cref="AppPaths.WebViewDir"/>，遊戲登入狀態、角色資料下次開還在。
/// 上方選單：面板（F9）、重新載入（F5）、開發者工具（F12）、開啟資料夾、檢查更新；最右邊顯示目前引擎版本。
/// </summary>
public sealed class GameForm : Form
{
    /// <summary>
    /// 關掉 Chromium 的背景省電，讓視窗最小化或被擋住時也照常掛機。不關的話：
    /// setInterval 會被降到約每秒一次（主迴圈本來 250ms 一輪）、requestAnimationFrame 直接暫停（躲鐘、扭蛋偵測靠它，遊戲動畫也會停）。
    /// CalculateNativeWinOcclusion＝Windows 上判斷「視窗被完全擋住」的功能，被擋住時一樣會被當成背景。
    /// </summary>
    private const string BrowserArgs =
        "--disable-background-timer-throttling --disable-renderer-backgrounding " +
        "--disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion";

    private readonly AppSettings _settings;
    private readonly EngineBridge _bridge;
    private readonly PanelForm _panel;

    /// <summary>視覺模式的引擎；JS 模式是 null</summary>
    private readonly VisionEngine? _visionEngine;
    private readonly EngineUpdater _updater;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly ToolStripMenuItem _engineItem = new() { Alignment = ToolStripItemAlignment.Right, Enabled = false };

    private EngineScript? _engine;

    /// <summary>目前註冊的注入腳本 id（換新版引擎時要先移除舊的）</summary>
    private string? _scriptId;

    /// <summary>視覺工具組。第一次開「視覺除錯」時才建立（要等 WebView2 初始化完成）</summary>
    private VisionKit? _vision;

    private VisionDebugForm? _visionDebug;

    public GameForm(AppSettings settings, EngineBridge bridge, VisionEngine? visionEngine, PanelForm panel)
    {
        _settings = settings;
        _bridge = bridge;
        _panel = panel;
        _visionEngine = visionEngine;
        _updater = new EngineUpdater(settings);

        Text = "RO Survivors 小幫手";
        StartPosition = FormStartPosition.WindowsDefaultBounds;
        if (AppSettings.OnScreen(settings.GameBounds) is { } r)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
        }
        else
            Size = new Size(LogicalToDeviceUnits(1280), LogicalToDeviceUnits(800));
        if (settings.GameMaximized)
            WindowState = FormWindowState.Maximized;

        var menu = new MenuStrip();
        menu.Items.Add(new ToolStripMenuItem("面板 (F9)", null, (_, _) => TogglePanel()));
        menu.Items.Add(new ToolStripMenuItem("重新載入 (F5)", null, (_, _) => _webView.CoreWebView2?.Reload()));
        menu.Items.Add(new ToolStripMenuItem("開發者工具 (F12)", null, (_, _) => _webView.CoreWebView2?.OpenDevToolsWindow()));
        menu.Items.Add(new ToolStripMenuItem("開啟資料夾", null, (_, _) => OpenDataFolder()));
        menu.Items.Add(new ToolStripMenuItem("檢查更新", null, async (_, _) => await CheckUpdateManuallyAsync()));
        menu.Items.Add(new ToolStripMenuItem("視覺除錯", null, (_, _) => OpenVisionDebug()));
        menu.Items.Add(new ToolStripMenuItem(settings.VisionMode ? "模式：視覺" : "模式：JS", null, (_, _) => ToggleMode()));
        menu.Items.Add(_engineItem);

        // 先加 Fill 再加 Top：WinForms 依 z-order 反向處理 Dock，這樣選單才不會蓋住遊戲畫面
        Controls.Add(_webView);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_settings.PanelVisible)
            _panel.Show();

        try
        {
            var options = new CoreWebView2EnvironmentOptions(BrowserArgs);
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, options);
            await _webView.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Windows 11 內建 WebView2 Runtime；Windows 10 舊機器可能沒有 → 給下載連結後結束，不要留一個空白視窗
            MessageBox.Show(this,
                "這台電腦沒有安裝 Microsoft Edge WebView2 Runtime。\n請到 https://go.microsoft.com/fwlink/p/?LinkId=2124703 下載安裝後再開。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _webView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.DocumentTitleChanged += (_, _) => Text = $"RO Survivors 小幫手 - {core.DocumentTitle}";
        // 視覺模式：完全不注入 JS、不接網頁訊息、不檢查引擎更新，只截圖和送輸入
        if (_visionEngine is not null)
        {
            _engineItem.Text = "視覺模式（開發中）";
            _visionEngine.Start(VisionKitFor(core));
            core.Navigate(AppPaths.GameUrl);
            return;
        }
        _bridge.TogglePanelRequested += TogglePanel;
        _bridge.Attach(core);

        // 步驟 1：選本機最新的引擎
        var engine = _updater.LoadLocal();

        // 步驟 2：開遊戲前先問一次線上有沒有新版（最多等 5 秒，失敗就用本機版，不擋遊戲）
        string? note = null;
        if (_settings.AutoUpdateEngine && !EngineUpdater.UsingOverride)
        {
            _engineItem.Text = "檢查更新中…";
            var result = await _updater.CheckAsync(engine);
            if (result is { Status: UpdateStatus.Available, Remote: { } newer })
                engine = newer;
            else if (result is { Status: UpdateStatus.NeedNewerExe, Remote: { } r })
                note = $"｜有新版 v{r.Version.ToString(3)}，需先更新 exe";
        }

        // 步驟 3：注入並開遊戲
        await UseEngineAsync(engine);
        _engineItem.Text += note;
        core.Navigate(AppPaths.GameUrl);
    }

    /// <summary>
    /// F8 暫停、F9 面板，只在視覺模式處理：焦點在遊戲畫面（WebView2）時，WebView2 控制項會把功能鍵當加速鍵轉給 WinForms 的 ProcessCmdKey，
    /// 而視覺模式沒有 JS 可以聽按鍵。JS 模式維持 engine.js 自己的處理，避免 WebView2 沒擋下按鍵時切兩次（使用者決定）。
    /// 回傳 true＝已處理，網頁不會再收到這個按鍵。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_visionEngine is null)
            return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.F8:
                _panel.TogglePause();
                return true;
            case Keys.F9:
                TogglePanel();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    #region 最小化時繼續掛機（移到螢幕外）

    // 實測（v8.0）：加了 BrowserArgs 後，計時器在最小化時仍是全速；但 WebView2 執行環境偵測到主視窗「真的最小化」時，
    // 一樣會把頁面變成 hidden、requestAnimationFrame 掉到 0 幀（遊戲動畫停、躲鐘和扭蛋的畫面偵測失效）。
    // 這時 controller.IsVisible 仍是 true，控制項也沒有開關可以改，所以改成：攔截最小化，把視窗移到螢幕外。
    // 視窗沒有真的最小化，遊戲照常跑；點工作列或 Alt+Tab 切回來時移回原位。
    // 限制：Win+D（顯示桌面）是由系統直接最小化，不經過這裡，那種情況遊戲還是會暫停。

    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;
    private Rectangle? _parkedFrom;
    private bool _parkedMaximized;

    protected override void WndProc(ref Message m)
    {
        // 最小化按鈕、點工作列（視窗在前景時）、Alt+空白鍵選單的「最小化」都會送 SC_MINIMIZE
        if (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScMinimize && _settings.KeepRunningWhenMinimized)
        {
            if (_parkedFrom is null)
                Park();
            else
                Unpark();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>視窗在螢幕外時被切到前景（Alt+Tab、點工作列）→ 移回來</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_parkedFrom is not null)
            BeginInvoke(Unpark);
    }

    private void Park()
    {
        _parkedMaximized = WindowState == FormWindowState.Maximized;
        if (_parkedMaximized)
            WindowState = FormWindowState.Normal;
        _parkedFrom = Bounds;
        var screens = SystemInformation.VirtualScreen;
        Location = new Point(screens.Left - Width - 200, screens.Top);
        ActivateNextWindow();
    }

    private void Unpark()
    {
        if (_parkedFrom is not { } from)
            return;
        _parkedFrom = null;
        Bounds = from;
        if (_parkedMaximized)
            WindowState = FormWindowState.Maximized;
        Activate();
    }

    /// <summary>
    /// 移走後把前景交給後面的視窗，跟真的最小化一樣；否則鍵盤焦點還留在看不到的遊戲裡，按鍵會打進遊戲。
    /// 跳過自己的視窗（面板）。
    /// </summary>
    private void ActivateNextWindow()
    {
        var self = (uint)Environment.ProcessId;
        for (var h = NativeMethods.GetWindow(Handle, NativeMethods.GwHwndNext); h != IntPtr.Zero; h = NativeMethods.GetWindow(h, NativeMethods.GwHwndNext))
        {
            if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h))
                continue;
            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            if (pid == self || NativeMethods.GetWindowTextLength(h) == 0)
                continue;
            NativeMethods.SetForegroundWindow(h);
            return;
        }
    }

    private static class NativeMethods
    {
        public const uint GwHwndNext = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 在螢幕外時關掉：存「移走前」的位置，下次才不會開在看不到的地方
        if (_parkedFrom is { } from)
        {
            _settings.GameMaximized = _parkedMaximized;
            _settings.GameBounds = WindowBounds.From(from);
            _panel.SaveWindowState();
            _settings.Save();
            base.OnFormClosing(e);
            return;
        }
        _settings.GameMaximized = WindowState == FormWindowState.Maximized;
        _settings.GameBounds = WindowBounds.From(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        _panel.SaveWindowState();
        _settings.Save();
        base.OnFormClosing(e);
    }

    /// <summary>
    /// 換成指定的引擎：移除舊的注入腳本、註冊新的。已經開著的頁面不受影響，要重新載入才會生效。
    /// </summary>
    private async Task UseEngineAsync(EngineScript engine)
    {
        var core = _webView.CoreWebView2;
        if (_scriptId is not null)
            core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
        _scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(engine.Source);
        _engine = engine;
        _engineItem.Text = engine.Describe();
    }

    private async Task CheckUpdateManuallyAsync()
    {
        if (_visionEngine is not null)
        {
            MessageBox.Show(this, "視覺模式不使用 JS 引擎，不需要更新。", Text);
            return;
        }
        if (_engine is null || _webView.CoreWebView2 is null)
            return;
        if (EngineUpdater.UsingOverride)
        {
            MessageBox.Show(this, "正在使用 exe 旁邊的 engine.js（開發模式），不檢查線上更新。\n要恢復自動更新，請把那個檔案移走後重開。", Text);
            return;
        }

        var result = await _updater.CheckAsync(_engine);
        switch (result.Status)
        {
            case UpdateStatus.Available when result.Remote is { } newer:
                var apply = MessageBox.Show(this,
                    $"有新版引擎 v{newer.Version.ToString(3)}（目前 v{_engine.Version.ToString(3)}）。\n\n" +
                    "要現在套用嗎？會重新載入遊戲頁面，進行中的這一局會中斷。\n選「否」則下次開啟時自動套用。",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (apply != DialogResult.Yes)
                    return;
                await UseEngineAsync(newer);
                _webView.CoreWebView2.Reload();
                break;
            case UpdateStatus.UpToDate:
                MessageBox.Show(this, $"已經是最新版（{_engine.Describe()}）。", Text);
                break;
            case UpdateStatus.NeedNewerExe when result.Remote is { } r:
                MessageBox.Show(this,
                    $"有新版引擎 v{r.Version.ToString(3)}，但需要 exe v{r.MinExe.ToString(3)} 以上（目前 v{EngineScript.ExeVersion.ToString(3)}）。\n請先下載新版 RoHelper.exe。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case UpdateStatus.Failed:
                MessageBox.Show(this, $"檢查更新失敗 : {result.Error}\n\n更新網址：{_settings.EngineUpdateUrl}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    /// <summary>開視覺除錯視窗（截圖、點擊測試、OCR、存範本、找範本）</summary>
    private void OpenVisionDebug()
    {
        if (_webView.CoreWebView2 is not { } core)
            return;
        var kit = VisionKitFor(core);
        if (_visionDebug is null || _visionDebug.IsDisposed)
            _visionDebug = new VisionDebugForm(kit);
        _visionDebug.Show();
        _visionDebug.Activate();
    }

    /// <summary>
    /// 視覺工具組（視覺引擎和除錯視窗共用一份）。dpr＝控制項的 DPI 縮放 × 頁面縮放：WebView2 依控制項的 DPI 決定實體像素，
    /// 刻意不從頁面讀 window.devicePixelRatio，那要在頁面執行 JS。
    /// </summary>
    private VisionKit VisionKitFor(CoreWebView2 core)
        => _vision ??= new VisionKit(core, _webView.Handle, () => _webView.DeviceDpi / 96.0 * _webView.ZoomFactor);

    /// <summary>切換 JS／視覺模式。重新開啟後生效：執行中切換的話，已經注入的 JS 拿不掉，兩邊會同時點畫面</summary>
    private void ToggleMode()
    {
        var toVision = !_settings.VisionMode;
        var message = toVision
            ? "切換到視覺模式：不在遊戲網頁注入任何 JS，只看截圖判斷。\n\n目前只會：選單人大廳、走到節慶主持人、開始遊戲、前往野外、返回角色。\n選卡和野外走位還沒做，進野外後要自己選卡。\n\n要切換嗎？重新開啟小幫手後生效。"
            : "切換回 JS 模式（原本的全自動）？重新開啟小幫手後生效。";
        if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _settings.VisionMode = toVision;
        _settings.Save();
    }

    /// <summary>
    /// 顯示／隱藏面板。JS 模式：遊戲裡按 F9 時由 engine.js 送 togglePanel 訊息觸發（焦點在 WebView2 裡時按鍵不一定經過 WinForms）；視覺模式：由 <see cref="ProcessCmdKey"/> 觸發。
    /// </summary>
    private void TogglePanel()
    {
        if (_panel.Visible && _panel.WindowState != FormWindowState.Minimized)
        {
            _panel.Hide();
            return;
        }
        _panel.Show();
        if (_panel.WindowState == FormWindowState.Minimized)
            _panel.WindowState = FormWindowState.Normal;
        _panel.Activate();
    }

    private static void OpenDataFolder()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        Process.Start(new ProcessStartInfo { FileName = AppPaths.DataDir, UseShellExecute = true });
    }
}
