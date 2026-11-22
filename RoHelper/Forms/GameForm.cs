using System.Diagnostics;
using System.Runtime.InteropServices;

using RoHelper.Bridge;
using RoHelper.DevApi;
using RoHelper.Engine;
using RoHelper.Storage;
using RoHelper.Vision;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoHelper.Forms;

/// <summary>
/// 主視窗：用 WebView2 開遊戲，由視覺引擎截圖判斷並送真實輸入（JS 引擎已停用，不注入任何 JS）。關掉這個視窗＝結束程式。
/// WebView2 的使用者資料夾固定在 <see cref="AppPaths.WebViewDir"/>，遊戲登入狀態、角色資料下次開還在。
/// 上方選單：面板（F9）、重新載入（F5）、開發者工具（F12）、開啟資料夾、視覺除錯；最右邊顯示目前模式。
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

    /// <summary>視窗標題：多開時加上 profile 名稱，工作列上才分得出哪個視窗是哪個帳號</summary>
    private static string TitleBase => AppPaths.Profile.Length == 0 ? "RO Survivors 小幫手" : $"RO Survivors 小幫手 [{AppPaths.Profile}]";

    /// <summary>開發介面（<c>--dev-api</c> 啟動時才有）</summary>
    private readonly ConfigStore _config;
    private readonly DevApiOptions? _devApi;
    private readonly EvidenceStore? _evidence;
    private DevApiServer? _devApiServer;

    /// <param name="config">設定（開發介面讀暫停狀態、選卡設定用）</param>
    /// <param name="devApi">命令列的 <c>--dev-api</c>；null＝不開開發介面</param>
    /// <param name="evidence">自動存證據（開發介面列出最近的證據用）；沒開時是 null</param>
    public GameForm(AppSettings settings, EngineBridge bridge, VisionEngine? visionEngine, PanelForm panel,
        ConfigStore config, DevApiOptions? devApi, EvidenceStore? evidence)
    {
        _settings = settings;
        _config = config;
        _devApi = devApi;
        _evidence = evidence;
        _bridge = bridge;
        _panel = panel;
        _visionEngine = visionEngine;
        _updater = new EngineUpdater(settings);

        Text = TitleBase;
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
        menu.Items.Add(new ToolStripMenuItem("多開總覽", null, (_, _) => OpenOverview()));
        var options = new ToolStripMenuItem("選項");
        options.DropDownItems.Add(Toggle("靜音", settings.MuteAudio, on =>
        {
            _settings.MuteAudio = on;
            if (_webView.CoreWebView2 is { } c)
                c.IsMuted = on;
        }));
        options.DropDownItems.Add(Toggle("啟動時自動繼續（不用手動解除暫停）", settings.ResumeOnStart, on => _settings.ResumeOnStart = on));
        options.DropDownItems.Add(Toggle("卡住時自動重新整理", settings.StallRecovery, on => _settings.StallRecovery = on));
        menu.Items.Add(options);
        // 【JS 引擎已停用】檢查引擎更新、切換 JS／視覺模式都用不到了（見 Program.cs）
        // menu.Items.Add(new ToolStripMenuItem("檢查更新", null, async (_, _) => await CheckUpdateManuallyAsync()));
        menu.Items.Add(new ToolStripMenuItem("視覺除錯", null, (_, _) => OpenVisionDebug()));
        // menu.Items.Add(new ToolStripMenuItem(settings.VisionMode ? "模式：視覺" : "模式：JS", null, (_, _) => ToggleMode()));
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
        core.IsMuted = _settings.MuteAudio;
        HandleScriptDialogs(core);
        // 視覺模式：完全不注入 JS、不接網頁訊息、不檢查引擎更新，只截圖和送輸入
        if (_visionEngine is not null)
        {
            _engineItem.Text = "視覺模式（開發中）";
            _visionEngine.StatusReceived += OnStatus;
            _visionEngine.Start(VisionKitFor(core));
            if (_settings.ResumeOnStart && _config.GetBool("paused"))
                _visionEngine.SendPatch(new System.Text.Json.Nodes.JsonObject { ["paused"] = false });
            StartWatch(core, _visionEngine);
            StartDevApi(VisionKitFor(core), _visionEngine);
            core.Navigate(AppPaths.GameUrl);
            return;
        }
        // 【JS 引擎已停用（2026-11-20，使用者決定）】以下是 JS 模式的流程：接網頁訊息、檢查引擎更新、注入 engine.js。
        // 視覺模式一定會走上面的 return，這段不會執行；要恢復時拿掉註解，並還原 Program.cs 的模式判斷。
        // _bridge.TogglePanelRequested += TogglePanel;
        // _bridge.Attach(core);
        //
        // // 步驟 1：選本機最新的引擎
        // var engine = _updater.LoadLocal();
        //
        // // 步驟 2：開遊戲前先問一次線上有沒有新版（最多等 5 秒，失敗就用本機版，不擋遊戲）
        // string? note = null;
        // if (_settings.AutoUpdateEngine && !EngineUpdater.UsingOverride)
        // {
        //     _engineItem.Text = "檢查更新中…";
        //     var result = await _updater.CheckAsync(engine);
        //     if (result is { Status: UpdateStatus.Available, Remote: { } newer })
        //         engine = newer;
        //     else if (result is { Status: UpdateStatus.NeedNewerExe, Remote: { } r })
        //         note = $"｜有新版 v{r.Version.ToString(3)}，需先更新 exe";
        // }
        //
        // // 步驟 3：注入並開遊戲
        // await UseEngineAsync(engine);
        // _engineItem.Text += note;
        // core.Navigate(AppPaths.GameUrl);
    }

    /// <summary>選項選單的開關：切換後馬上存 app.json</summary>
    private ToolStripMenuItem Toggle(string text, bool value, Action<bool> apply)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = value };
        item.CheckedChanged += (_, _) =>
        {
            apply(item.Checked);
            _settings.Save();
        };
        return item;
    }

    #region 標題列狀態、多開總覽、卡住偵測

    private OverviewForm? _overview;

    /// <summary>最近一次的狀態（標題列、status.json 用）</summary>
    private int _runs;
    private string _action = "";
    private bool _paused;

    /// <summary>卡住偵測的警告（重新整理也救不回來時）；空字串＝沒事</summary>
    private string _warning = "";

    private long _statusSavedAt = long.MinValue / 2;

    /// <summary>
    /// 引擎回報狀態：更新標題列（工作列上一眼看出哪個分身在做什麼），並寫 status.json 給多開總覽。
    /// 標題把狀態放前面：工作列按鈕寬度有限，只看得到開頭
    /// </summary>
    private void OnStatus(System.Text.Json.Nodes.JsonObject s)
    {
        _runs = s["runs"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out int n) ? n : 0;
        _action = (s["action"]?.ToString() ?? "").Replace("［視覺］", "");
        _paused = s["paused"] is System.Text.Json.Nodes.JsonValue p && p.TryGetValue(out bool b) && b;
        // 有進度了（開新局）就清掉警告
        if (_warning.Length > 0 && _visionEngine?.Watch.Alerted == false)
            _warning = "";
        UpdateTitle();
        // 狀態每幾百毫秒就變一次，檔案最多 5 秒寫一次；心跳由 StartWatch 的計時器補
        if (Environment.TickCount64 - _statusSavedAt >= 5000)
            SaveStatus();
    }

    private void UpdateTitle()
    {
        var who = AppPaths.Profile.Length == 0 ? "" : $"[{AppPaths.Profile}] ";
        var state = _warning.Length > 0 ? "⚠ " + _warning : _paused ? "已暫停" : _action;
        Text = $"{who}第 {_runs} 局｜{state} - RO Survivors 小幫手";
    }

    private void SaveStatus()
    {
        _statusSavedAt = Environment.TickCount64;
        try
        {
            new InstanceStatus(Environment.ProcessId, Environment.GetCommandLineArgs()[1..], _runs, _action, _paused, _warning, DateTime.Now)
                .Save(AppPaths.ProfileDir);
        }
        catch (IOException)
        {
            // 總覽正在讀：下次再寫
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OpenOverview()
    {
        if (_overview is null || _overview.IsDisposed)
            _overview = new OverviewForm(_devApi is not null);
        _overview.Show();
        _overview.Activate();
    }

    /// <summary>
    /// 每 15 秒：檢查有沒有卡住（見 <see cref="StallWatch"/>），順便寫一次 status.json 當心跳。
    /// 用遊戲視窗自己的計時器、不放在主迴圈裡：主迴圈卡住（輸入一直不回應）時就不會跑了
    /// </summary>
    private void StartWatch(CoreWebView2 core, VisionEngine engine)
    {
        var timer = new System.Windows.Forms.Timer { Interval = 15_000 };
        timer.Tick += (_, _) =>
        {
            SaveStatus();
            if (!_settings.StallRecovery)
            {
                engine.Watch.Idle(Environment.TickCount64);
                return;
            }
            var v = engine.Watch.Check(Environment.TickCount64);
            switch (v.Act)
            {
                case StallAct.Reload:
                    LogEvent($"卡住偵測：{v.Why} → 重新整理頁面（第 {engine.Watch.Reloads} 次）");
                    core.Reload();
                    break;
                case StallAct.Alert:
                    _warning = $"卡住了（{v.Why}，重新整理也沒用）";
                    LogEvent($"卡住偵測：{v.Why}，已重新整理 {engine.Watch.Reloads} 次仍然沒恢復 → 提醒使用者");
                    UpdateTitle();
                    SaveStatus();
                    Flash();
                    break;
            }
        };
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
    }

    /// <summary>工作列按鈕閃爍，直到使用者切到這個視窗</summary>
    private void Flash()
    {
        var info = new NativeMethods.FlashWInfo
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.FlashWInfo>(),
            hwnd = Handle,
            dwFlags = NativeMethods.FlashwAll | NativeMethods.FlashwTimerNoFg,
        };
        NativeMethods.FlashWindowEx(ref info);
    }

    #endregion

    /// <summary>
    /// 網頁的 alert／confirm 對話框改由這裡處理（WebView2 宿主端事件，不是注入 JS）。
    /// 實測（2026-11-22 三開）：同一個帳號在別的裝置登入過時，遊戲跳 alert「你的帳號在另一台裝置上使用過。已載入其最新存檔。」，
    /// 瀏覽器原生的對話框擋住整個網頁（模擬點擊點不到、CDP 輸入也卡住），A 視窗因此停了 8 個多小時。
    /// alert 只有「確定」：一律按掉，訊息記到 events.log；「另一台裝置」那則按掉後再重新整理（使用者說的 F5）；beforeunload 一律允許離開；confirm／prompt 要做選擇，跳視窗給使用者決定（等使用者回應時網頁維持暫停）。
    /// </summary>
    private void HandleScriptDialogs(CoreWebView2 core)
    {
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.ScriptDialogOpening += (_, e) =>
        {
            LogEvent($"{e.Kind}：{e.Message}");
            switch (e.Kind)
            {
                case CoreWebView2ScriptDialogKind.Alert:
                    e.Accept();
                    // 使用者：出現「帳號在另一台裝置上使用過」直接按 F5。重新整理後回到首頁，由大廳流程（首頁 → 選角色 → 活動／遠征）接手
                    if (e.Message.Contains("另一台裝置"))
                    {
                        LogEvent("　→ 重新整理頁面");
                        BeginInvoke(async () =>
                        {
                            await Task.Delay(1000);
                            core.Reload();
                        });
                    }
                    return;
                case CoreWebView2ScriptDialogKind.Beforeunload:
                    e.Accept();
                    return;
            }
            var deferral = e.GetDeferral();
            BeginInvoke(() =>
            {
                try
                {
                    var ok = MessageBox.Show(this, e.Message, $"{TitleBase} - 網頁詢問", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
                    if (ok)
                    {
                        if (e.Kind == CoreWebView2ScriptDialogKind.Prompt)
                            e.ResultText = e.DefaultText;
                        e.Accept();
                    }
                    LogEvent(ok ? "　→ 使用者按確定" : "　→ 使用者按取消");
                }
                finally
                {
                    deferral.Complete();
                }
            });
        };
    }

    private static void LogEvent(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.ProfileDir, "events.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // 記錄失敗不影響遊戲
        }
    }

    /// <summary>
    /// F8 暫停、F9 面板，只在視覺模式處理：焦點在遊戲畫面（WebView2）時，WebView2 控制項會把功能鍵當加速鍵轉給 WinForms 的 ProcessCmdKey，
    /// 而視覺模式沒有 JS 可以聽按鍵。JS 模式維持 engine.js 自己的處理，避免 WebView2 沒擋下按鍵時切兩次（使用者決定）。
    /// 回傳 true＝已處理；預期 WebView2 會把按鍵標成已處理而不再轉給網頁（實機驗證時確認）。
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
        public const uint FlashwAll = 3;
        public const uint FlashwTimerNoFg = 12;

        [StructLayout(LayoutKind.Sequential)]
        public struct FlashWInfo
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FlashWindowEx(ref FlashWInfo pwfi);

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

    /// <summary>
    /// 有 <c>--dev-api</c> 時開本機開發介面，結果附在選單右邊的模式文字後面。
    /// 開不起來（port 被占用等）只顯示原因、不擋程式：開發介面只是輔助。
    /// </summary>
    private void StartDevApi(VisionKit kit, VisionEngine engine)
    {
        if (_devApi is null)
            return;
        var host = new DevApiHost(this, kit, engine, _panel, _config, _evidence);
        var server = new DevApiServer(new DevApiRouter(host), _devApi.Port);
        try
        {
            server.Start();
            _devApiServer = server;
            _engineItem.Text += $"｜開發介面 127.0.0.1:{_devApi.Port}";
        }
        catch (Exception ex)
        {
            server.Dispose();
            _engineItem.Text += $"｜開發介面開不起來 : {ex.Message}";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _devApiServer?.Dispose();
        base.OnFormClosed(e);
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
        Directory.CreateDirectory(AppPaths.ProfileDir);
        Process.Start(new ProcessStartInfo { FileName = AppPaths.ProfileDir, UseShellExecute = true });
    }
}
