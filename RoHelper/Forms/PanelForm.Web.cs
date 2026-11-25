using System.Text.Json;
using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Panel;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoHelper.Forms;

/// <summary>
/// 新版面板：視窗內容換成 WebView2，畫面由 Panel/web 的 HTML／CSS／JS 負責，這裡只收發訊息（格式見 docs/superpowers/plans/2026-11-25-panel-redesign-phase1.md）。
/// WebView2 環境由遊戲視窗交過來（同一個使用者資料夾的第二個 WebView2 必須用同一個環境，選項不同會建立失敗），不自己建。
/// 網頁送 ready 之前（還在載入、或面板一直隱藏所以還沒初始化）不送任何訊息；收到 ready 再一次補送 schema、設定、狀態。
/// 改設定的訊息交給 <see cref="PanelActions"/> 算出 patch，再走跟舊面板一樣的 <see cref="IAutomation.SendPatch"/>。
/// </summary>
public sealed partial class PanelForm
{
    private WebView2? _web;
    private bool _webReady;

    /// <summary>引擎最近一次的狀態（<see cref="ShowStatus"/> 收到的）；網頁 ready 時補送</summary>
    private JsonObject? _lastStatus;

    /// <summary>新版模式：<see cref="UseWebPanelAsync"/> 之後為 true，OnSchema、ShowStatus、ApplyConfig 改走網頁，不建 WinForms 控制項</summary>
    private bool WebMode => _web is not null;

    /// <summary>
    /// 切換成新版面板。第一個 await 之前就進入新版模式，所以要在引擎 Start（送出規則清單）之前呼叫。
    /// 呼叫端不要 await：面板隱藏時 WebView2 要等視窗第一次顯示才初始化，await 會卡住遊戲視窗的啟動流程。
    /// 初始化失敗時在面板上顯示原因和退路（遊戲視窗「選項」關掉新版面板），不讓例外逃出去。
    /// </summary>
    public async Task UseWebPanelAsync(CoreWebView2Environment env)
    {
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0xE9, 0xED, 0xF5) };
        SuspendLayout();
        foreach (var old in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(old);
            old.Dispose();
        }
        Controls.Add(_web);
        ResumeLayout();
        _config.Changed += PostConfig;
        _log.Changed += PostStatus;
        _runs.Changed += PostStatus;

        try
        {
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.AddWebResourceRequestedFilter(PanelAssets.Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                e.Response = PanelAssets.TryGet(e.Request.Uri, out var data, out var type)
                    ? env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", "Content-Type: " + type)
                    : env.CreateWebResourceResponse(null, 404, "Not Found", "");
            };
            core.WebMessageReceived += OnWebMessage;
            core.Navigate(PanelAssets.Origin + "index.html");
        }
        catch (Exception ex)
        {
            Controls.Remove(_web);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"新版面板載入失敗 : {ex.Message}\n可以在遊戲視窗「選項」關掉「新版面板」後重新啟動，改用舊面板。",
            });
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 只收面板自己的網頁送來的訊息
        if (!e.Source.StartsWith(PanelAssets.Origin, StringComparison.OrdinalIgnoreCase))
            return;
        JsonObject? msg;
        try
        {
            msg = JsonNode.Parse(e.WebMessageAsJson) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (msg is null)
            return;

        switch (EngineSchema.Str(msg["type"]))
        {
            case "ready":
                _webReady = true;
                PostWindow();
                PostSchema();
                PostConfig();
                PostStatus();
                return;
            case "pause":
                TogglePause();
                return;
            case "hide":
                Hide();
                return;
            case "topMost":
                TopMost = msg["on"] is JsonValue v && v.TryGetValue(out bool on) && on;
                PostWindow();
                return;
            case "export":
                ExportConfig();
                return;
            case "import":
                ImportConfig();
                return;
            case "applyOthers":
                PushToProfiles();
                return;
            case "copyLog":
                CopyText(_log.ToText(), "紀錄");
                return;
            case "clearLog":
                _log.Clear();
                return;
            case "copyStats":
                CopyText(string.Join("\n", Stats().Select(FormatStat)), "戰績");
                return;
        }

        if (PanelActions.ToPatch(msg, _config.Current) is { } patch)
        {
            _automation.SendPatch(patch);
            Post(new JsonObject { ["type"] = "saved" });
        }
    }

    private void Post(JsonObject msg)
    {
        if (_webReady && _web?.CoreWebView2 is { } core)
            core.PostWebMessageAsJson(msg.ToJsonString());
    }

    private void PostSchema()
    {
        if (_schema is { } schema)
            Post(new JsonObject { ["type"] = "schema", ["schema"] = PanelSchema.ToJson(schema) });
    }

    private void PostConfig() => Post(new JsonObject { ["type"] = "config", ["config"] = _config.Current.DeepClone() });

    private void PostStatus()
    {
        var status = _lastStatus?.DeepClone() as JsonObject ?? [];
        status["logCount"] = _log.Count;
        status["runsRecorded"] = _runs.Count;
        Post(new JsonObject { ["type"] = "status", ["status"] = status });
    }

    private void PostWindow() => Post(new JsonObject { ["type"] = "window", ["topMost"] = TopMost });
}
