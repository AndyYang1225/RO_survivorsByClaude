using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using RoHelper.Storage;

using Microsoft.Web.WebView2.Core;

namespace RoHelper.Bridge;

/// <summary>
/// JS 引擎 ↔ C# 之間唯一的溝通管道（WebView2 的 postMessage）。
/// 收：hello（規則清單）、cfg（整份設定）、status（目前動作等狀態）、log（選卡紀錄）、run（一局的完整紀錄）。
/// 送：init（頁面載入後帶入存檔設定）、set（面板改了部分設定）、replace（匯入整份設定）。
/// 只接受遊戲網域送來的訊息：使用者在遊戲視窗裡點到別的網站時，那邊的頁面不能冒充引擎改設定。
/// </summary>
public sealed class EngineBridge : IAutomation
{
    private readonly ConfigStore _config;
    private readonly LogStore _log;
    private readonly RunStore _runs;
    private CoreWebView2? _core;

    public EngineBridge(ConfigStore config, LogStore log, RunStore runs)
    {
        _config = config;
        _log = log;
        _runs = runs;
    }

    /// <summary>最近一次收到的規則清單；遊戲頁面還沒載入前是 null</summary>
    public EngineSchema? Schema { get; private set; }

    public event Action<EngineSchema>? SchemaReceived;
    public event Action<JsonObject>? StatusReceived;

    /// <summary>遊戲裡按 F9</summary>
    public event Action? TogglePanelRequested;

    public void Attach(CoreWebView2 core)
    {
        _core = core;
        core.WebMessageReceived += OnMessage;
    }

    /// <summary>面板改了幾個設定值 → 交給 JS 套用（JS 套完會回傳整份 cfg）</summary>
    public void SendPatch(JsonObject patch)
        => Post(new JsonObject { ["type"] = "set", ["patch"] = patch });

    /// <summary>匯入設定：整份換掉</summary>
    public void SendReplace(JsonObject cfg)
        => Post(new JsonObject { ["type"] = "replace", ["cfg"] = cfg });

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsGameOrigin(e.Source))
            return;

        JsonObject? msg;
        try
        {
            msg = JsonNode.Parse(e.WebMessageAsJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"引擎訊息格式錯誤 : {ex.Message}");
            return;
        }
        if (msg is null)
            return;

        switch (EngineSchema.Str(msg["type"]))
        {
            // 頁面（重新）載入：記下規則清單，再把存檔的設定帶給 JS，JS 收到才開始跑
            case "hello" when msg["schema"] is JsonObject schema:
                Schema = EngineSchema.Parse(schema);
                SchemaReceived?.Invoke(Schema);
                Post(new JsonObject { ["type"] = "init", ["cfg"] = _config.Current.DeepClone() });
                break;
            case "cfg" when msg["cfg"] is JsonObject cfg:
                _config.Replace(cfg.DeepClone().AsObject());
                break;
            case "status" when msg["status"] is JsonObject status:
                StatusReceived?.Invoke(status);
                break;
            case "log" when msg["entry"] is JsonNode entry:
                _log.Add(entry);
                break;
            case "run" when msg["run"] is JsonObject run:
                _runs.Append(run);
                break;
            case "togglePanel":
                TogglePanelRequested?.Invoke();
                break;
        }
    }

    private void Post(JsonObject msg)
    {
        if (_core is null)
            return;
        _core.PostWebMessageAsJson(msg.ToJsonString());
    }

    private static bool IsGameOrigin(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri)
           && (uri.Host == "ro-survivors.com" || uri.Host.EndsWith(".ro-survivors.com", StringComparison.OrdinalIgnoreCase));
}
