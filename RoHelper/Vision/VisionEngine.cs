using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Storage;
using RoHelper.Vision.Field;
using RoHelper.Vision.Picker;

namespace RoHelper.Vision;

/// <summary>
/// 視覺模式的引擎：每 tTick 毫秒截一張圖 → <see cref="ScreenReader"/> 辨識 → <see cref="LobbyFlow"/> 決定 → CDP 真實點擊。
/// 選卡畫面優先於大廳流程（<see cref="PickAsync"/>）；在野外（<see cref="FieldGate"/>）而且畫面上沒有大廳按鈕時走位（<see cref="FieldStepAsync"/>）。
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
    private readonly FieldGate _gate = new();
    private readonly FieldPlanner _planner = new(Random.Shared.NextDouble);
    private readonly RunStore _runs;
    private readonly RunRecorder _recorder = new();

    /// <summary>對局紀錄多久取樣一次血量與狀態（同 JS 每 1 秒 recSample）</summary>
    private const long SampleEvery = 1000;
    private long _sampleAt = long.MinValue / 2;

    /// <summary>進野外多久後才拉遠鏡頭（等野外畫面載入；JS 是 1.2 秒，截圖辨識比較慢，多等一點）</summary>
    private const long ZoomDelay = 3000;

    /// <summary>拉遠鏡頭：滾輪幾次、每次多少（deltaY 正＝往下滾＝拉遠，同 JS 的 WheelEvent deltaY 300）</summary>
    private const int ZoomSteps = 3;
    private const double ZoomDelta = 300;

    /// <summary>在野外時多久讀一次金錢與寶箱價格：OCR 一次約 100～300 ms，每輪都讀會拖慢走位；金錢變化也沒那麼快</summary>
    private const long HudEvery = 3000;

    /// <summary>
    /// 在野外時多久做一次完整辨識（找全部大廳範本）；其餘輪只找「返回角色」。
    /// 離開野外平常靠結算畫面（返回角色）就夠，完整辨識只是為了涵蓋斷線回首頁、局被中斷之類的情況，慢幾秒發現沒關係。
    /// </summary>
    private const long FullReadEvery = 5000;

    /// <summary>上次完整辨識的時間；不在野外時每輪都是完整辨識，所以剛進野外的頭 5 秒只找返回角色</summary>
    private long _fullReadAt = long.MinValue / 2;

    /// <summary>
    /// 上一輪是完整辨識而且看到大廳的東西：下一輪也要完整辨識。
    /// 為什麼：FieldGate 要連續 2 輪看到大廳範本才離開野外、LobbyFlow 的首頁／捨棄要持續看到一段時間才按，
    /// 中間插一輪只找返回角色的話，那些欄位變 null，計數和計時都會被清掉，永遠離不開野外。
    /// </summary>
    private bool _lobbySeen;

    /// <summary>這一局已經拉遠過鏡頭</summary>
    private bool _zoomed;

    /// <summary>最近一次讀到的金錢與寶箱價格（null＝還沒讀或讀不到，當買得起）和讀的時間</summary>
    private Hud? _hud;
    private long _hudAt = long.MinValue / 2;
    private HudReader? _hudReader;

    /// <summary>
    /// 躲鐘開著時多久 OCR 一次畫面上方的公告（王的喊話）：OCR 一次約 100～200 ms，每輪都讀太慢；
    /// JS 每輪讀（讀網頁文字很便宜），公告會掛在畫面上好幾秒，1 秒讀一次來得及
    /// </summary>
    private const long BannerEvery = 1000;
    private long _bannerAt = long.MinValue / 2;
    private BannerReader? _bannerReader;

    /// <summary>
    /// 多久 OCR 一次計時器的遊戲時間（約 50～100 ms；讀之間由 <see cref="GameClock"/> 推算）。
    /// 遊戲時間給 preMvp（MVP 前先選完升級）、面板的下一隻王、對局紀錄的 gt 用。
    /// </summary>
    private const long ClockEvery = 1000;
    private long _clockAt = long.MinValue / 2;
    private readonly GameClock _clock = new();

    /// <summary>累積升級按 B（autoUpgrade、preMvp）</summary>
    private readonly BankPresser _bank = new();

    /// <summary>面板「下一隻」那一行（不在野外時空字串）</summary>
    private string _next = "";
    private VisionKit? _kit;
    private ScreenReader? _reader;
    private PickerReader? _pickerReader;
    private ResultReader? _resultReader;
    private FieldInfoReader? _infoReader;
    private ExpeditionReader? _expeReader;

    /// <summary>讀地名與職業失敗時多久再試一次、每局最多試幾次（讀不到的職業、被擋住的角色框不要整局一直花時間 OCR）</summary>
    private const long InfoEvery = 3000;
    private const int InfoMaxTries = 10;

    /// <summary>這一局讀地名與職業的狀態：上次試的時間、試了幾次、讀到的地名（空字串＝還沒讀到）、讀到職業了沒</summary>
    private long _infoAt = long.MinValue / 2;
    private int _infoTries;
    private string _stage = "";
    private bool _jobRead;

    /// <summary>自動存證據（<c>--dev-api</c> 啟動時才有；null＝不存）</summary>
    private readonly EvidenceStore? _evidence;
    private readonly EvidencePolicy? _evidencePolicy;

    /// <summary>這一輪還沒跑完（截圖＋找範本偶爾會超過間隔），下一輪直接跳過，不重疊</summary>
    private bool _busy;

    /// <summary>上次送給面板的狀態；沒變就不送，避免每 250 ms 重畫一次面板</summary>
    private string _lastStatus = "";

    public VisionEngine(ConfigStore config, LogStore log, RunStore runs, EngineSchema schema, EvidenceStore? evidence = null)
    {
        _config = config;
        _log = log;
        _runs = runs;
        Schema = schema;
        _evidence = evidence;
        _evidencePolicy = evidence is null ? null : new EvidencePolicy();
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public EngineSchema Schema { get; }

    /// <summary>最近的狀態變化（開發介面、自動存證據用）</summary>
    public StatusHistory History { get; } = new();

    /// <summary>本局選卡狀態（唯讀，給開發介面預覽「現在會怎麼選」）</summary>
    public RunPickState PickState => _picker.State;

    /// <summary>現在是不是在野外（給開發介面看）</summary>
    public bool InField => _gate.InField;

    /// <summary>走位判定的王死掉地點（給開發介面看；null＝沒有）</summary>
    public BossSpot? BossSpot => _planner.BossSpot;

    /// <summary>躲鐘的狀態（給開發介面看：躲到什麼時候、最近找到的鐘）</summary>
    public BellDodger Bell => _planner.Bell;

    /// <summary>引擎推算的遊戲時間（秒；null＝還沒讀到或計時器已經變成倒數）與這一局讀到的地名（給開發介面看）</summary>
    public int? GameSecs => _clock.Now(Environment.TickCount64);
    public string Stage => _stage;

    public event Action<EngineSchema>? SchemaReceived;
    public event Action<JsonObject>? StatusReceived;

    /// <summary>WebView2 初始化完成後由遊戲視窗呼叫：補齊設定預設值、通知面板產生分頁、開始主迴圈</summary>
    public void Start(VisionKit kit)
    {
        _kit = kit;
        _reader = new ScreenReader(kit.Templates);
        _pickerReader = kit.Ocr is { } ocr ? new PickerReader(ocr) : null;
        _hudReader = kit.Ocr is null ? null : new HudReader(kit.Ocr);
        _bannerReader = kit.Ocr is null ? null : new BannerReader(kit.Ocr);
        var jobs = Schema.JobSkills.Select(j => j.Key).ToList();
        _resultReader = kit.Ocr is null ? null : new ResultReader(kit.Ocr, jobs);
        _infoReader = kit.Ocr is null ? null : new FieldInfoReader(kit.Ocr, jobs, Schema.Stages);
        _expeReader = kit.Ocr is null ? null : new ExpeditionReader(kit.Ocr, Schema.Stages);
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
        Frame? frame = null;
        try
        {
            var settings = FlowSettings.From(_config);
            if (settings.Paused)
            {
                _flow.Reset();
                _picker.Gone();
                // 暫停時也要記「這局暫停過」（同 JS：使用者可能暫停小幫手自己玩）
                _recorder.Sample(null, "", paused: true);
                Report("已暫停");
                return;
            }

            frame = await _kit.Grabber.CaptureAsync();
            var dpr = _kit.Viewport.Dpr;

            // 步驟 1：選卡畫面優先（選卡會擋住整局，大廳流程的按鈕這時也不會出現）
            // 讀不到任何卡名時（其實不是選卡畫面）不動作，這一輪照常走大廳流程
            string? unreadable = null;
            if (CardLayout.FindCards(frame, dpr).Count >= 2)
            {
                var (handled, note) = await PickAsync(frame, dpr);
                if (handled)
                {
                    // 按 B 打開的選卡出現了（同 JS 選卡時清 bankTry）
                    _bank.PickerSeen();
                    return;
                }
                unreadable = note;
            }
            else
                _picker.Gone();

            // 步驟 2：辨識。在野外時只找返回角色，每 FullReadEvery 才完整辨識一次（看到大廳的東西後就每輪完整辨識，見 _lobbySeen）
            var readAt = Environment.TickCount64;
            var full = !_gate.InField || _lobbySeen || readAt - _fullReadAt >= FullReadEvery;
            if (full)
                _fullReadAt = readAt;
            var screen = _reader.Read(frame, dpr, lobby: settings.AutoEvent, inField: !full, expe: settings.AutoExpe);
            // 返回角色不算：只找返回角色的那幾輪也找得到它，不必為了它改成每輪完整辨識
            _lobbySeen = full && FieldGate.LobbyVisible(screen with { Return = null });

            // 步驟 3：決定並執行（點擊座標是截圖像素，換成網頁 CSS 座標再送）
            var now = Environment.TickCount64;
            // 自動遠征：遠征視窗開著時 OCR 地圖清單（選地圖用）
            var stages = settings.AutoExpe && screen.StageList is { } list && _expeReader is not null
                ? await _expeReader.ReadAsync(frame, dpr, list) : null;
            var decision = _flow.Decide(screen, settings, now, stages);
            // 結算：先讀結算畫面再記錄（同一輪；第一次確認結算的這一輪不會按返回角色，要等 tReturn，結算畫面還在）
            if (decision.CountRun)
                await CountRunAsync(frame, dpr);
            if (decision.StartsRun)
            {
                _picker.NewRun();
                NewFieldRun();
                _recorder.Start(DateTime.Now, PickSettings.From(_config).Job, paused: false, decision.Expe ? "expe" : "event");
            }
            _gate.Update(screen, decision, now);
            if (decision.Click is { } p)
            {
                var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
                await _kit.Input.ClickAsync(x, y);
            }
            if (decision.WheelAt is { } w)
            {
                var (x, y) = _kit.Viewport.ToCss(w.X, w.Y);
                await _kit.Input.WheelAsync(x, y, decision.WheelDelta);
            }

            // 步驟 4：在野外、畫面上沒有大廳按鈕時走位。
            // 看起來像選卡但讀不到卡名時（unreadable）不走位：點地面可能點到卡片
            var action = unreadable is not null && decision.Action == "待機" ? unreadable : decision.Action;
            if (!_gate.InField)
                _next = "";
            if (_gate.InField && decision.Click is null && unreadable is null && !FieldGate.LobbyVisible(screen))
            {
                var walk = await FieldStepAsync(frame, dpr, now);
                if (decision.Action == "待機" && walk.Length > 0)
                    action = walk;
            }
            if (_gate.InField && now - _sampleAt >= SampleEvery)
            {
                _sampleAt = now;
                _recorder.Sample(HpReader.Read(frame, dpr), action, paused: false);
            }
            Report(action);
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
            // 暫停時這一輪在截圖前就 return，frame 是 null，自然不存
            if (frame is not null)
                SaveEvidence(frame);
            _busy = false;
        }
    }

    /// <summary>每輪結束時問一次要不要存證據（異常或定時），要的話存這一輪的截圖＋當下動作＋最近 20 筆狀態紀錄</summary>
    private void SaveEvidence(Frame frame)
    {
        if (_evidence is null || _evidencePolicy is null)
            return;
        try
        {
            if (_evidencePolicy.Check(History.Last, Environment.TickCount64) is not { } reason)
                return;
            var history = new JsonArray(History.Recent(20).Select(e => (JsonNode?)new JsonObject
            {
                ["t"] = e.Time.ToString("HH:mm:ss"),
                ["a"] = e.Action,
            }).ToArray());
            _evidence.Save(frame, reason, DateTime.Now, new JsonObject { ["action"] = History.Last, ["history"] = history });
        }
        catch
        {
            // 為什麼吞掉：存證據只是開發輔助，磁碟滿、檔案被占用時不能讓主迴圈（async void 的計時器事件）掛掉
        }
    }

    /// <summary>
    /// 結算畫面第一次出現（同 JS）：局數 +1、讀結算畫面、對局紀錄寫進 runs.jsonl（這局有開始記錄才寫，同 JS recFinish）、
    /// 面板戰績（cfg.stats）加一筆（最多 50 筆）。沒有繁中 OCR 時結算內容讀不到，紀錄照寫（outcome 是 unknown）。
    /// 讀結算或寫檔失敗不影響局數與後面的流程。
    /// </summary>
    private async Task CountRunAsync(Frame frame, double dpr)
    {
        RunResult? result = null;
        try
        {
            if (_resultReader is not null)
                result = await _resultReader.ReadAsync(frame, dpr);
        }
        catch
        {
            // 為什麼吞掉：結算畫面讀不到只是紀錄少了內容，局數、返回角色照常
        }
        // 結算畫面的職業比角色框好讀（字比較大、不會跟角色名黏在一起）：這局沒從角色框讀到職業時用它補（exe 新增，JS 在選角色畫面讀）
        if (!_jobRead && result is { Job.Length: > 0 })
        {
            UpdateJob(result.Job);
            _recorder.SetInfo("", PickSettings.From(_config).Job);
        }
        var now = DateTime.Now;
        var run = _recorder.Finish(result, now);
        if (run is not null)
        {
            try
            {
                _runs.Append(run);
            }
            catch
            {
                // 為什麼吞掉：磁碟滿、檔案被占用時不能讓主迴圈掛掉；這局的紀錄就算了
            }
        }
        var wall = run?["wallSecs"]?.GetValue<int>() ?? 0;
        var stage = (string?)run?["stage"] ?? "";

        var cfg = (JsonObject)_config.Current.DeepClone();
        cfg["runs"] = (int)Math.Min((long)_config.GetInt("runs") + 1, int.MaxValue);
        cfg["stats"] = RunRecorder.AppendStat(cfg["stats"], RunRecorder.Stat(now, stage, result?.Secs, wall));
        _config.Replace(cfg);
        _picker.NewRun();
        NewFieldRun();
    }

    /// <summary>新的一局：走位狀態（卡住、忽略清單、繞圈角度）清掉，鏡頭要重新拉遠，金錢要重新讀</summary>
    private void NewFieldRun()
    {
        _planner.NewRun();
        _zoomed = false;
        _hud = null;
        _hudAt = long.MinValue / 2;
        _bannerAt = long.MinValue / 2;
        _infoAt = long.MinValue / 2;
        _infoTries = 0;
        _stage = "";
        _jobRead = false;
        _clock.Reset();
        _clockAt = long.MinValue / 2;
        _bank.NewRun();
        _next = "";
    }

    /// <summary>
    /// 野外走位一輪：進野外滿 <see cref="ZoomDelay"/> 後拉遠鏡頭一次（設定 zoomOut），再讀小地圖 → <see cref="FieldPlanner"/> 決定 → 點擊。
    /// 小地圖找不到自己時傳 null 給決策（它會繞圈）。點擊是世界畫面比例：野外時遊戲畫布佔滿視窗，所以乘上截圖寬高就是截圖像素。
    /// chestMoney 開著時每 <see cref="HudEvery"/> 讀一次金錢與寶箱價格，錢不夠就不追寶箱；沒有繁中 OCR 時一律當買得起。
    /// HP 與計時器顏色（MVP 死後的撿取倒數）每輪都讀（純像素，很便宜），lowHpStop、撿取倒數用。
    /// seekBall 開著、不在撿取倒數時每輪掃一次畫面找扭蛋（<see cref="BallFinder"/>，實測一張約 4～6 ms；JS 每 0.5 秒掃一次，這裡夠便宜就每輪掃，看到的位置比較新）。
    /// bellDodge 開著時每 <see cref="BannerEvery"/> OCR 一次公告（<see cref="BellAsync"/>），躲鐘中每輪掃一次畫面找鐘（約 1 ms），這時不找扭蛋。
    /// 進野外滿 ZoomDelay 後也讀地名與職業（<see cref="ReadInfoAsync"/>，讀到為止）。
    /// 每 <see cref="ClockEvery"/> OCR 一次遊戲時間（<see cref="GameClock"/>）。
    /// 不在躲鐘時先看累積升級（<see cref="BankAsync"/>，同 JS 順序：躲鐘 → 升級 → 走位）：這一輪要按 B 或等選卡畫面時不走位。
    /// 回傳面板文字：走位的狀態前面加「走位：」，升級的狀態照 JS 原文；升級打不開的附註接在最後（同 JS bankNote）。空字串＝什麼都沒做。
    /// </summary>
    private async Task<string> FieldStepAsync(Frame frame, double dpr, long now)
    {
        var kit = _kit!;
        var s = FieldSettings.From(_config);
        if (s.ZoomOut && !_zoomed && now - _gate.Since >= ZoomDelay)
        {
            var (cx, cy) = kit.Viewport.ToCss(frame.Width / 2.0, frame.Height / 2.0);
            for (var i = 0; i < ZoomSteps; i++)
                await kit.Input.WheelAsync(cx, cy, ZoomDelta);
            _zoomed = true;
        }
        if (now - _gate.Since >= ZoomDelay)
            await ReadInfoAsync(frame, dpr, now);
        if (s.ChestMoney && _hudReader is not null && now - _hudAt >= HudEvery)
        {
            _hud = await _hudReader.ReadAsync(frame, dpr);
            _hudAt = now;
        }
        var timer = TimerReader.Read(frame, dpr);
        var lootPhase = timer == TimerPhase.Loot;
        await ClockAsync(frame, dpr, now, timer);
        var bell = await BellAsync(frame, dpr, now, s, lootPhase);
        var note = "";
        if (bell is null)
        {
            var bank = await BankAsync(frame, dpr, now, s, lootPhase);
            if (bank.Status.Length > 0)
                return bank.Status;
            note = bank.Note;
        }
        // 躲鐘中不做別的走位，不用找扭蛋
        var balls = s.SeekBall && !lootPhase && bell is null ? BallFinder.Find(frame, dpr) : null;
        var d = _planner.Step(MinimapReader.Read(frame, dpr), s, now, s.CanAffordChest(_hud), HpReader.Read(frame, dpr), lootPhase, balls, bell);
        if (d.Click is { } c)
        {
            var (x, y) = kit.Viewport.ToCss(c.X * frame.Width, c.Y * frame.Height);
            await kit.Input.ClickAsync(x, y);
        }
        var walk = d.Status.Length > 0 ? "走位：" + d.Status : "";
        return note.Length == 0 ? walk : walk.Length == 0 ? note : walk + " " + note;
    }

    /// <summary>
    /// 遊戲時間：撿取倒數（紅字）時計時器已經不是遊戲時間；平常每 <see cref="ClockEvery"/> OCR 一次（沒有繁中 OCR 時沒有遊戲時間）。
    /// 順便更新面板「下一隻」那一行（同 JS render 的 nx）。
    /// </summary>
    private async Task ClockAsync(Frame frame, double dpr, long now, TimerPhase? timer)
    {
        if (timer == TimerPhase.Loot)
            _clock.Loot();
        else if (timer == TimerPhase.Normal && _kit!.Ocr is { } ocr && now - _clockAt >= ClockEvery)
        {
            _clockAt = now;
            _clock.Read(await TimerReader.ReadClockAsync(ocr, frame, dpr), now);
        }
        _next = StageBosses.NextText(_stage, _clock.Now(now), timer == TimerPhase.Loot, _clock.Countdown);
    }

    /// <summary>
    /// 累積升級：看「升級 N [B]」標籤（<see cref="BankReader.Look"/>，純像素）→ <see cref="BankPresser"/> 決定 → 要按就按 B。
    /// MVP 還有幾秒用地名（<see cref="ReadInfoAsync"/> 讀到的）查 <see cref="StageBosses"/> 減遊戲時間。
    /// </summary>
    private async Task<BankStep> BankAsync(Frame frame, double dpr, long now, FieldSettings s, bool lootPhase)
    {
        var ready = BankReader.Look(frame, dpr) == BankLook.Ready;
        var mvpLeft = StageBosses.MvpLeft(_stage, _clock.Now(now));
        var step = _bank.Step(ready, mvpLeft, lootPhase, s.AutoUpgrade, s.PreMvp, s.PreMvpN, now);
        if (step.Press)
            await _kit!.Input.KeyAsync('b');
        return step;
    }

    /// <summary>
    /// 躲鐘：每 <see cref="BannerEvery"/> OCR 一次公告交給走位決策（沒有繁中 OCR 時讀不到公告，不會躲鐘）；
    /// 躲鐘中掃畫面找鐘，回傳這一輪看到的鐘（沒在躲鐘回 null）。撿取倒數中（MVP 死了）不讀，走位決策會結束躲鐘。
    /// </summary>
    private async Task<BellView?> BellAsync(Frame frame, double dpr, long now, FieldSettings s, bool lootPhase)
    {
        if (!s.BellDodge || lootPhase)
            return null;
        if (_bannerReader is not null && now - _bannerAt >= BannerEvery)
        {
            _bannerAt = now;
            _planner.Bell.Announce(await _bannerReader.ReadAsync(frame, dpr), now);
        }
        if (!_planner.Bell.Active(now))
            return null;
        return new BellView(BellFinder.Find(frame, dpr, _planner.Bell.Seen), frame.Width, frame.Height, dpr);
    }

    /// <summary>
    /// 進野外滿 <see cref="ZoomDelay"/>（畫面載入完）後讀小地圖標題的地名與角色框的職業，讀不到每 <see cref="InfoEvery"/> 再試，最多 <see cref="InfoMaxTries"/> 次。
    /// 職業寫進 cfg curJob（<see cref="UpdateJob"/>），地名、職業給對局紀錄。
    /// </summary>
    private async Task ReadInfoAsync(Frame frame, double dpr, long now)
    {
        if (_infoReader is null || (_stage.Length > 0 && _jobRead) || _infoTries >= InfoMaxTries || now - _infoAt < InfoEvery)
            return;
        _infoAt = now;
        _infoTries++;
        var info = await _infoReader.ReadAsync(frame, dpr);
        if (info.Stage.Length > 0)
            _stage = info.Stage;
        if (info.Job.Length > 0)
        {
            _jobRead = true;
            UpdateJob(info.Job);
        }
        _recorder.SetInfo(_stage, PickSettings.From(_config).Job);
    }

    /// <summary>
    /// 偵測到的職業寫進 cfg curJob（同 JS detectJob：只寫職業清單裡有的、跟現在不同才寫）。面板職業選「自動」時選卡就用它。
    /// </summary>
    private void UpdateJob(string job)
    {
        if (!Schema.JobSkills.Any(j => j.Key == job) || _config.GetString("curJob") == job)
            return;
        var cfg = (JsonObject)_config.Current.DeepClone();
        cfg["curJob"] = job;
        _config.Replace(cfg);
    }

    /// <summary>
    /// 選卡：等 tPick（剛出現、或動作後畫面還沒更新時不做 OCR）→ 讀卡 → 決定 → 按數字鍵或點按鈕。
    /// 沒有繁中 OCR 時只顯示「等你選卡」，不亂點。
    /// 回傳 Handled＝這一輪已處理完（不再走大廳流程）；所有卡名都讀不到時回 false 並附說明（多半不是選卡畫面，不能按鍵或點擊）。
    /// </summary>
    private async Task<(bool Handled, string? Note)> PickAsync(Frame frame, double dpr)
    {
        var s = PickSettings.From(_config);
        if (_pickerReader is null)
        {
            Report("等你選卡（沒有繁中 OCR）");
            return (true, null);
        }
        if (_picker.Waiting(Environment.TickCount64, s.PickDelay))
        {
            Report("選卡中…");
            return (true, null);
        }
        if (await _pickerReader.ReadAsync(frame, dpr) is not { } info)
            return (true, null);
        if (info.Cards.All(c => c.Name.Length == 0))
        {
            _picker.Gone();
            return (false, "看起來像選卡但讀不到卡名，不動作");
        }
        var action = _picker.Act(info, s, Environment.TickCount64);
        if (action.Key is { } key)
            await _kit!.Input.KeyAsync(key);
        if (action.Click is { } p)
        {
            var (x, y) = _kit!.Viewport.ToCss(p.X, p.Y);
            await _kit.Input.ClickAsync(x, y);
        }
        if (action.Decision is { } d)
        {
            _recorder.Pick(d, info, _clock.Text(Environment.TickCount64));
            if (s.DebugLog)
                LogPick(info, s, action.Status, d);
        }
        // 還沒讀到職業（進野外後才讀角色框）而且沒選職業時技能規則不生效，提醒使用者
        var status = s.Job.Length == 0 ? action.Status + "（還沒偵測到職業：技能規則不生效，可在面板選職業）" : action.Status;
        Report(status);
        return (true, null);
    }

    /// <summary>選卡紀錄（面板「除錯」勾選時）：格式同 JS 的 logPick，exe 寫進 picklog.json</summary>
    private void LogPick(PickerInfo info, PickSettings s, string status, PickDecision d)
    {
        var cards = new JsonArray(info.Cards.Select(c => (JsonNode?)JsonValue.Create(
            string.Join(" | ", new[] { c.Name, c.SkillBadge ? "技能" : RunRecorder.RarityName(c.Rarity), c.IsNew ? "新！" : "", c.Text }.Where(x => x.Length > 0)))).ToArray());
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

    /// <summary>送狀態給面板（格式同 JS 的 status 訊息；尋寶率還沒做，下一隻王見 <see cref="ClockAsync"/>）</summary>
    private void Report(string action)
    {
        History.Add(DateTime.Now, action);
        var s = FlowSettings.From(_config);
        var status = new JsonObject
        {
            ["runs"] = s.Runs,
            ["runLimit"] = s.RunLimit ? s.RunLimitN : 0,
            ["treasure"] = 0,
            ["next"] = _next,
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
