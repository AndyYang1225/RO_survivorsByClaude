using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>
/// 視覺模式的引擎：每 tTick 毫秒截一張圖 → <see cref="ScreenReader"/> 辨識 → <see cref="LobbyFlow"/> 決定 → CDP 真實點擊。
/// 全程不在遊戲網頁執行任何 JS。設定由這裡擁有（JS 模式是 JS 擁有）：面板的修改、匯入都在這裡套用後存回 <see cref="ConfigStore"/>。
/// 失敗處理：截圖或點擊丟例外時（頁面重新載入中、視窗大小是 0）這一輪跳過，把原因顯示在面板上，下一輪照常再試，不停掉主迴圈。
/// 暫停時連截圖都不做：省 CPU，使用者暫停後畫面完全不受干擾。
/// </summary>
public sealed class VisionEngine : IAutomation, IDisposable
{
    private readonly ConfigStore _config;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly LobbyFlow _flow = new();
    private VisionKit? _kit;
    private ScreenReader? _reader;

    /// <summary>這一輪還沒跑完（截圖＋找範本偶爾會超過間隔），下一輪直接跳過，不重疊</summary>
    private bool _busy;

    /// <summary>上次送給面板的狀態；沒變就不送，避免每 250 ms 重畫一次面板</summary>
    private string _lastStatus = "";

    public VisionEngine(ConfigStore config, EngineSchema schema)
    {
        _config = config;
        Schema = schema;
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public EngineSchema Schema { get; }

    public event Action<EngineSchema>? SchemaReceived;
    public event Action<JsonObject>? StatusReceived;

    /// <summary>WebView2 初始化完成後由遊戲視窗呼叫：補齊設定預設值、通知面板產生分頁、開始主迴圈</summary>
    public void Start(VisionKit kit)
    {
        _kit = kit;
        _reader = new ScreenReader(kit.Templates);
        _config.Replace(ConfigDefaults.Apply(ConfigDefaults.Build(Schema), _config.Current, Schema, reset: false));
        SchemaReceived?.Invoke(Schema);
        _timer.Interval = TickMs();
        _timer.Start();
    }

    public void SendPatch(JsonObject patch)
    {
        var cfg = (JsonObject)_config.Current.DeepClone();
        foreach (var (k, v) in patch)
            cfg[k] = v?.DeepClone();
        ConfigDefaults.ClampTimes(cfg, Schema);
        _config.Replace(cfg);
        UpdateInterval();
    }

    public void SendReplace(JsonObject cfg)
    {
        _config.Replace(ConfigDefaults.Apply(_config.Current, cfg, Schema, reset: true));
        UpdateInterval();
    }

    public void Dispose() => _timer.Dispose();

    private async Task TickAsync()
    {
        if (_busy || _kit is null || _reader is null)
            return;
        _busy = true;
        try
        {
            var settings = FlowSettings.From(_config);
            if (settings.Paused)
            {
                Report("已暫停");
                return;
            }

            // 步驟 1：截圖並辨識
            var frame = await _kit.Grabber.CaptureAsync();
            var dpr = _kit.Viewport.Dpr;
            var screen = _reader.Read(frame, dpr, lobby: settings.AutoEvent);

            // 步驟 2：決定並執行（點擊座標是截圖像素，換成網頁 CSS 座標再送）
            var decision = _flow.Decide(screen, settings, Environment.TickCount64);
            if (decision.CountRun)
                CountRun();
            if (decision.Click is { } p)
            {
                var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
                await _kit.Input.ClickAsync(x, y);
            }
            Report(decision.Action);
        }
        catch (Exception ex)
        {
            Report($"視覺判斷失敗 : {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>結算畫面第一次出現：局數 +1（同 JS）。戰績的地圖、撿到的東西要等計畫 4、5 才讀得到，先只記局數</summary>
    private void CountRun()
    {
        var cfg = (JsonObject)_config.Current.DeepClone();
        cfg["runs"] = (int)_config.GetNumber("runs") + 1;
        _config.Replace(cfg);
    }

    /// <summary>送狀態給面板（格式同 JS 的 status 訊息；尋寶率、下一隻王要等計畫 3、4）</summary>
    private void Report(string action)
    {
        var s = FlowSettings.From(_config);
        var status = new JsonObject
        {
            ["runs"] = s.Runs,
            ["runLimit"] = s.RunLimit ? s.RunLimitN : 0,
            ["treasure"] = 0,
            ["next"] = "",
            ["action"] = "［視覺］" + action,
            ["paused"] = s.Paused,
        };
        var json = status.ToJsonString();
        if (json == _lastStatus)
            return;
        _lastStatus = json;
        StatusReceived?.Invoke(status);
    }

    private int TickMs() => (int)_config.GetNumber("tTickN", 250);

    /// <summary>間隔有變才設：設定 Interval 會讓計時器重新計時，面板每切一次分頁都重設的話主迴圈會被拖慢</summary>
    private void UpdateInterval()
    {
        if (_timer.Interval != TickMs())
            _timer.Interval = TickMs();
    }
}
