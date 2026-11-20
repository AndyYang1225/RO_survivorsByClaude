using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Storage;
using RoHelper.Vision.Picker;

namespace RoHelper.Vision;

/// <summary>
/// 視覺模式的引擎：每 tTick 毫秒截一張圖 → <see cref="ScreenReader"/> 辨識 → <see cref="LobbyFlow"/> 決定 → CDP 真實點擊。
/// 選卡畫面優先於大廳流程（<see cref="PickAsync"/>）。
/// 全程不在遊戲網頁執行任何 JS。設定由這裡擁有（JS 模式是 JS 擁有）：面板的修改、匯入都在這裡套用後存回 <see cref="ConfigStore"/>。
/// 失敗處理：截圖或點擊丟例外時（頁面重新載入中、視窗大小是 0）這一輪跳過，把原因顯示在面板上，下一輪照常再試，不停掉主迴圈。
/// 暫停時連截圖都不做：省 CPU，使用者暫停後畫面完全不受干擾。
/// </summary>
public sealed class VisionEngine : IAutomation, IDisposable
{
    private readonly ConfigStore _config;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly LogStore _log;
    private readonly LobbyFlow _flow = new();
    private readonly PickerFlow _picker = new();
    private VisionKit? _kit;
    private ScreenReader? _reader;
    private PickerReader? _pickerReader;

    /// <summary>這一輪還沒跑完（截圖＋找範本偶爾會超過間隔），下一輪直接跳過，不重疊</summary>
    private bool _busy;

    /// <summary>上次送給面板的狀態；沒變就不送，避免每 250 ms 重畫一次面板</summary>
    private string _lastStatus = "";

    public VisionEngine(ConfigStore config, LogStore log, EngineSchema schema)
    {
        _config = config;
        _log = log;
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
        _pickerReader = kit.Ocr is { } ocr ? new PickerReader(ocr) : null;
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
        _flow.Reset();
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
                _flow.Reset();
                Report("已暫停");
                return;
            }

            var frame = await _kit.Grabber.CaptureAsync();
            var dpr = _kit.Viewport.Dpr;

            // 步驟 1：選卡畫面優先（選卡會擋住整局，大廳流程的按鈕這時也不會出現）
            if (CardLayout.FindCards(frame, dpr).Count >= 2)
            {
                await PickAsync(frame, dpr);
                return;
            }
            _picker.Gone();

            // 步驟 2：辨識
            var screen = _reader.Read(frame, dpr, lobby: settings.AutoEvent);

            // 步驟 3：決定並執行（點擊座標是截圖像素，換成網頁 CSS 座標再送）
            var decision = _flow.Decide(screen, settings, Environment.TickCount64);
            if (decision.CountRun)
                CountRun();
            if (decision.StartsRun)
                _picker.NewRun();
            if (decision.Click is { } p)
            {
                var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
                await _kit.Input.ClickAsync(x, y);
            }
            Report(decision.Action);
        }
        catch (Exception ex)
        {
            try
            {
                Report($"視覺判斷失敗 : {ex.Message}");
            }
            catch
            {
                // 為什麼吞掉：Report 會讀設定，若問題就出在設定，這裡再拋會從 async void 的計時器事件逃出去讓程式結束
            }
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
        cfg["runs"] = (int)Math.Min((long)_config.GetInt("runs") + 1, int.MaxValue);
        _config.Replace(cfg);
        _picker.NewRun();
    }

    /// <summary>
    /// 選卡：等 tPick（剛出現、或動作後畫面還沒更新時不做 OCR）→ 讀卡 → 決定 → 按數字鍵或點按鈕。
    /// 沒有繁中 OCR 時只顯示「等你選卡」，不亂點。
    /// </summary>
    private async Task PickAsync(Frame frame, double dpr)
    {
        var s = PickSettings.From(_config);
        if (_pickerReader is null)
        {
            Report("等你選卡（沒有繁中 OCR）");
            return;
        }
        if (_picker.Waiting(Environment.TickCount64, s.PickDelay))
        {
            Report("選卡中…");
            return;
        }
        if (await _pickerReader.ReadAsync(frame, dpr) is not { } info)
            return;
        var action = _picker.Act(info, s, Environment.TickCount64);
        if (action.Key is { } key)
            await _kit!.Input.KeyAsync(key);
        if (action.Click is { } p)
        {
            var (x, y) = _kit!.Viewport.ToCss(p.X, p.Y);
            await _kit.Input.ClickAsync(x, y);
        }
        if (s.DebugLog && action.Decision is { } d)
            LogPick(info, s, action.Status, d);
        Report(action.Status);
    }

    /// <summary>選卡紀錄（面板「除錯」勾選時）：格式同 JS 的 logPick，exe 寫進 picklog.json</summary>
    private void LogPick(PickerInfo info, PickSettings s, string status, PickDecision d)
    {
        var cards = new JsonArray(info.Cards.Select(c => (JsonNode?)JsonValue.Create(
            string.Join(" | ", new[] { c.Name, c.SkillBadge ? "技能" : RarityName(c.Rarity), c.IsNew ? "新！" : "", c.Text }.Where(x => x.Length > 0)))).ToArray());
        _log.Add(new JsonObject
        {
            ["t"] = DateTime.Now.ToString("HH:mm:ss"),
            ["job"] = s.Job,
            ["title"] = info.IsChest ? "寶箱" : "升級",
            ["treasure"] = 0,
            ["cards"] = cards,
            ["act"] = status,
            ["why"] = d.Why,
        });
    }

    private static string RarityName(int r) => r is >= 0 and <= 5 ? new[] { "普通", "優良", "稀有", "史詩", "傳說", "神話" }[r] : "";

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

    private int TickMs() => _config.GetInt("tTickN", 250);

    /// <summary>間隔有變才設：設定 Interval 會讓計時器重新計時，面板每切一次分頁都重設的話主迴圈會被拖慢</summary>
    private void UpdateInterval()
    {
        if (_timer.Interval != TickMs())
            _timer.Interval = TickMs();
    }
}
