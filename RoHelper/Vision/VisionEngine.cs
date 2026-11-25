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

    /// <summary>連續看到撿取倒數（紅字、沒有 MVP 血條）的開始時間；null＝這輪不是</summary>
    private long? _lootSince;

    private const long LootConfirm = 1_500;
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

    /// <summary>
    /// 讀尋寶率（<see cref="TreasureReader"/>：滑鼠移到道具欄每一格、OCR 說明框）。移滑鼠會打斷走位（那一輪不走），所以不照 JS 每 5 秒讀，改成：
    /// 選卡畫面關掉後 <see cref="TreasureSettle"/>（選了卡，尋寶率可能變了）、道具欄有東西的格數變了就讀，其餘時間不讀（<see cref="TreasureIdle"/>）；
    /// 讀失敗（等不到說明框）<see cref="TreasureRetry"/> 後再試；兩次讀之間至少隔 <see cref="TreasureGap"/>。
    /// </summary>
    private const long TreasureSettle = 1500;

    /// <summary>
    /// 讀完之後的下一次：不定時補讀。尋寶率只會因為選卡（道具升級、精煉）或道具欄變動而改變，這兩種都會觸發重讀；
    /// 原本每 30 秒補讀一次，但每次要停下走位約 1 秒（2026-11-25 拿掉）
    /// </summary>
    private const long TreasureIdle = long.MaxValue / 2;
    private const long TreasureRetry = 10_000;
    private const long TreasureGap = 3000;
    private TreasureReader? _treasureReader;
    private long _treasureDue = long.MinValue / 2;
    private long _treasureAt = long.MinValue / 2;
    private int _treasureSlots = -1;

    /// <summary>點了技能神祇「!」之後，這麼久之內不再點（等選擇畫面打開，打開後由選卡流程接手）</summary>
    private const long DeityClickGap = 3000;
    private long _deityClickAt = long.MinValue / 2;

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

    /// <summary>躲鐘紀錄（<see cref="BellTrace"/>）：觸發的公告原文、截圖存在 shots/bell（最多 300 張，不跟一般證據搶上限）</summary>
    private readonly BellTrace _bellTrace = new();
    private string _bellBanner = "";
    private readonly EvidenceStore _bellShots = new(Path.Combine(AppPaths.ShotsDir, "bell"), 300);

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

    /// <summary>卡住偵測（遊戲視窗定時檢查、決定要不要重新整理）</summary>
    public StallWatch Watch { get; } = new();

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

    /// <summary>最近一次讀道具欄的結果與時間（給開發介面看；null＝這局還沒讀到）</summary>
    public TreasureRead? LastTreasure { get; private set; }
    public long LastTreasureAt => _treasureAt;

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
        _picker.ItemGroups = Schema.Items;
        _pickerReader = kit.Ocr is { } ocr ? new PickerReader(ocr) : null;
        _hudReader = kit.Ocr is null ? null : new HudReader(kit.Ocr);
        _bannerReader = kit.Ocr is null ? null : new BannerReader(kit.Ocr);
        _treasureReader = kit.Ocr is null ? null : new TreasureReader(kit.Ocr);
        var jobs = Schema.JobSkills.Select(j => j.Key).ToList();
        _resultReader = kit.Ocr is null ? null : new ResultReader(kit.Ocr, jobs);
        _infoReader = kit.Ocr is null ? null : new FieldInfoReader(kit.Ocr, jobs, Schema.Stages);
        _expeReader = kit.Ocr is null ? null : new ExpeditionReader(kit.Ocr, Schema.Stages);
        _config.Replace(ConfigDefaults.Apply(ConfigDefaults.Build(Schema), _config.Current, Schema, reset: false));
        Watch.Idle(Environment.TickCount64);
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
            // 別的分身按了「套用到其他分身」：把送來的規則併進來（暫停中也要收，見 ConfigSync）
            if (ConfigSync.TakePending(AppPaths.ProfileDir) is { } pending)
                SendPatch(pending);
            var settings = FlowSettings.From(_config);
            // 暫停、局數到了、沒開自動活動／遠征：本來就不會開新局，不算卡住
            if (settings.Paused || settings.RunLimit && settings.Runs >= settings.RunLimitN || !settings.AutoEvent && !settings.AutoExpe)
                Watch.Idle(Environment.TickCount64);
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
            Watch.Frame(Environment.TickCount64, StallWatch.Signature(frame));
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
                    // 選了卡，尋寶率可能變了：選卡畫面關掉後再讀
                    _treasureDue = Environment.TickCount64 + TreasureSettle;
                    return;
                }
                unreadable = note;
            }
            else
                _picker.Gone();

            // 步驟 2：辨識。在野外時只找返回角色，每 FullReadEvery 才完整辨識一次（看到大廳的東西後就每輪完整辨識，見 _lobbySeen）
            var readAt = Environment.TickCount64;
            var full = !_gate.InField || _lobbySeen || readAt - _fullReadAt >= FullReadEvery;
            // 範本比對丟到背景執行緒：這是每輪最花時間的一段，在 UI 執行緒做的話面板和遊戲視窗會跟著卡住。
            // 只有比對（截圖進、辨識結果出，不碰任何狀態）在背景，之後的判斷、改狀態、點擊照舊在 UI 執行緒，狀態一樣只有一個地方在改。
            // 比對期間 UI 執行緒空著，使用者可能剛好暫停或改了設定（設定會整份換掉）：這一輪是用舊設定判斷的，丟掉不執行，下一輪用新設定重來
            var (reader, shot, cfgAtRead) = (_reader, frame, _config.Current);
            var screen = await Task.Run(() => reader.Read(shot, dpr, lobby: settings.AutoEvent, inField: !full, expe: settings.AutoExpe));
            if (!ReferenceEquals(_config.Current, cfgAtRead))
                return;
            // 這一輪確定有效才記下完整辨識的時間：被丟掉的那輪不算，下一輪會再做完整辨識
            if (full)
                _fullReadAt = readAt;
            // 返回角色不算：只找返回角色的那幾輪也找得到它，不必為了它改成每輪完整辨識
            _lobbySeen = full && FieldGate.LobbyVisible(screen with { Return = null });

            // 步驟 3：決定並執行（點擊座標是截圖像素，換成網頁 CSS 座標再送）
            var now = Environment.TickCount64;
            // 自動遠征：遠征視窗開著時 OCR 地圖清單（選地圖用）
            var stages = settings.AutoExpe && screen.StageList is { } list && _expeReader is not null
                ? await _expeReader.ReadAsync(frame, dpr, list) : null;
            // 組隊：「選擇大廳」視窗開著時 OCR 大廳清單，找要選的多人大廳那一列
            if (settings.Party && screen.Solo is { } soloBtn && _kit.Ocr is { } lobbyOcr)
                screen = screen with { PartyLobby = await LobbyList.FindAsync(lobbyOcr, frame, dpr, soloBtn, settings.PartyLobby) };
            // 組隊邀請長得跟「暫離」一樣（同一個「接受」範本）：讀對話框文字分出來，不管有沒有開組隊（免得把陌生人的邀請當暫離按接受）
            if (screen.AfkOk is { } afkBtn && _kit.Ocr is { } inviteOcr && await PartyUi.ReadInviterAsync(inviteOcr, frame, dpr, afkBtn) is { } inviter)
            {
                var leaders = PartyRoster.LeaderNames(PartyRoster.Others(AppPaths.Profile, DateTime.Now));
                screen = screen with { AfkOk = null, PartyInvite = afkBtn, PartyInviteTrusted = leaders.Any(n => PartyUi.NameLike(inviter, n)) };
            }
            // 「準備好了！」範本會認錯成邀請對話框的「接受」（同樣的藍色按鈕、同一個位置，實測 2026-11-24）：
            // 讀對話框文字，是出發提議才留著；是組隊邀請就改走邀請的判斷（只接受自己隊長的）；都不是就不按
            if (screen.PartyReady is { } readyBtn && _kit.Ocr is { } readyOcr)
            {
                var text = await PartyUi.ReadDialogTextAsync(readyOcr, frame, dpr, readyBtn);
                if (PartyUi.Inviter(text) is { } who)
                {
                    var leaders = PartyRoster.LeaderNames(PartyRoster.Others(AppPaths.Profile, DateTime.Now));
                    screen = screen with { PartyReady = null, PartyInvite = readyBtn, PartyInviteTrusted = leaders.Any(n => PartyUi.NameLike(who, n)) };
                }
                else if (!PartyUi.IsDepartProposal(text))
                {
                    screen = screen with { PartyReady = null };
                    // 診斷：認出按鈕、文字卻不是出發提議（實測 2026-11-24 局中出現過，疑似戰利品擲骰視窗）：存起來看長什麼樣（30 秒一張）
                    if (now - _unknownDialogAt > 30_000)
                    {
                        _unknownDialogAt = now;
                        try
                        {
                            _partyShots.Save(frame, "不明對話框", DateTime.Now, new JsonObject { ["text"] = text, ["button"] = $"{readyBtn.X},{readyBtn.Y},{readyBtn.Width},{readyBtn.Height}" });
                        }
                        catch
                        {
                            // 診斷存不了沒關係
                        }
                    }
                }
            }
            // 「其餘全 Greed」範本會認錯大廳的「與隊長交談」（同樣的白色圓角按鈕、位置重疊，實測 2026-11-24 三個都卡在「等分配完成」）：
            // 讀按鈕上的字，有「Greed」才算
            if (screen.LootGreed is { } greedBtn && _kit.Ocr is { } greedOcr)
            {
                var area = Rectangle.Intersect(Rectangle.Inflate(greedBtn, (int)(6 * dpr), (int)(4 * dpr)), new Rectangle(0, 0, frame.Width, frame.Height));
                var words = string.Join(" ", (await PartyUi.ReadBothAsync(greedOcr, frame, area)).Select(l => l.Text));
                // 要讀到「其餘」：分配完成後清單每一行都有「Greed 80」之類的字，只看 Greed 會把清單當成按鈕（實測 2026-11-24 卡在「等分配完成」）
                if (!words.Contains("其餘") && !words.Contains("其馀") && !words.Contains("餘全"))
                    screen = screen with { LootGreed = null, LootReady = null };
            }
            // 組隊的隊長：在大廳、隊員還沒到齊時先邀請，到齊（或等太久）才往遠征隊長走
            if (settings.PartyRole == "leader" && !_gate.InField && await PartyLeaderAsync(frame, dpr, screen, settings, now) is { } partyAction)
            {
                Report(partyAction);
                return;
            }
            // 組隊的隊員：在別人當隊長的隊伍裡（例如隊長重開時遊戲把隊長轉給隊員）就離開，等自己的隊長邀請
            if (settings.Member && !_gate.InField && await PartyMemberAsync(frame, dpr, screen, now) is { } memberAction)
            {
                Report(memberAction);
                return;
            }
            var decision = _flow.Decide(screen, settings, now, stages);
            // 隊員防暫離的那一步在野外時不點（野外有自己的走位）
            if (_gate.InField && decision.Action == LobbyFlow.AntiAfkAction)
                decision = decision with { Click = null, Action = "組隊：等隊長提議出發" };
            // 結算：先讀結算畫面再記錄（同一輪；第一次確認結算的這一輪不會按返回角色，要等 tReturn，結算畫面還在）
            if (decision.CountRun || decision.StartsRun)
                Watch.Progress(now);
            if (decision.CountRun)
                await CountRunAsync(frame, dpr);
            if (decision.StartsRun)
                StartRun(decision.Expe);
            _gate.Update(screen, decision, now);
            // 組隊：提議／準備好之後，看到野外的計時器（全隊都準備好、進了野外）才開始這一局
            // 組隊時不限 60 秒內：實測（2026-11-24）隊員按完準備好剛好一直在選卡，60 秒內沒看到計時器，整局都當成在大廳。
            // 組隊模式下連續 3 輪看到野外的計時器、畫面上沒有大廳的東西，就算進了野外
            var fieldTimer = !_gate.InField && !decision.StartsRun && FieldGate.LobbyVisible(screen) is false && TimerReader.Read(frame, dpr) is not null;
            _fieldTimerHits = fieldTimer ? _fieldTimerHits + 1 : 0;
            if (fieldTimer && (_flow.AwaitingField(now) || settings.Party && _fieldTimerHits >= 3))
            {
                _fieldTimerHits = 0;
                _flow.FieldEntered();
                Watch.Progress(now);
                StartRun(expe: true);
                _gate.Enter(now);
                decision = decision with { Action = "組隊：全隊進入野外" };
            }
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
                // 隊員在野外時大廳流程回「組隊：等隊長提議出發」（它不知道在不在野外），一樣換成走位的狀態
                if ((decision.Action == "待機" || decision.Action.StartsWith("組隊：等", StringComparison.Ordinal)) && walk.Length > 0)
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
        // 躲鐘到一半就離開野外（被秒殺、結算）：結束這次躲鐘紀錄，標記 interrupted
        TraceBell(null, null, "", null, Environment.TickCount64);
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
        cfg["stats"] = RunRecorder.AppendStat(cfg["stats"], RunRecorder.Stat(now, stage, result?.Secs, wall, result?.Outcome));
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
        _bellTrace.Reset();
        _clock.Reset();
        _clockAt = long.MinValue / 2;
        _bank.NewRun();
        _treasureDue = long.MinValue / 2;
        _treasureAt = long.MinValue / 2;
        _treasureSlots = -1;
        LastTreasure = null;
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
    /// 接著是點技能神祇「!」（<see cref="DeityIconAsync"/>）、讀尋寶率（<see cref="TreasureAsync"/>），躲鐘、撿取倒數時都不做：有點或移滑鼠的那一輪不走位。
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
        // 撿取倒數要連續 LootConfirm 毫秒才算：實測喊「躲到鐘下」時畫面染紅光，MVP 血條偶爾有一格認不出來，
        // 單格誤判成撿取倒數會把剛開始的躲鐘取消（真的撿取倒數有 15 秒，晚 1.5 秒沒差）
        _lootSince = timer == TimerPhase.Loot ? _lootSince ?? now : null;
        var lootPhase = _lootSince is { } ls && now - ls >= LootConfirm;
        await ClockAsync(frame, dpr, now, timer);
        var bell = await BellAsync(frame, dpr, now, s, lootPhase);
        var note = "";
        if (bell is null)
            TraceBell(frame, null, "", null, now);
        if (bell is null)
        {
            var bank = await BankAsync(frame, dpr, now, s, lootPhase);
            if (bank.Status.Length > 0)
                return bank.Status;
            note = bank.Note;
        }
        // 技能神祇：道具欄出現紫色「!」就點開（選擇畫面由選卡流程處理）。躲鐘、撿取倒數時不點，點的這一輪不走位
        if (bell is null && !lootPhase && await DeityIconAsync(frame, dpr, now))
            return "打開技能神祇";
        // 讀尋寶率要移滑鼠：躲鐘、撿取倒數時不讀，讀的這一輪不走位
        if (bell is null && !lootPhase && await TreasureAsync(frame, dpr, now))
            return "讀道具欄（尋寶率）";
        // 組隊站樁：組隊時各自走位會越走越散、各自被包圍（實測 2026-11-24 斐揚森林全拉滿：各自走位 4 分鐘全滅；三人站著不動撐了約 15 分鐘）。
        // 開著時不繞圈、不追寶箱掉落物；躲鐘、MVP 死後的撿取倒數照常
        var hp = HpReader.Read(frame, dpr);
        if (bell is null && !lootPhase && PartyStay())
        {
            return note.Length == 0 ? "走位：組隊站樁" : "走位：組隊站樁 " + note;
        }
        // 躲鐘中不做別的走位，不用找扭蛋
        var balls = s.SeekBall && !lootPhase && bell is null ? BallFinder.Find(frame, dpr) : null;
        var d = _planner.Step(MinimapReader.Read(frame, dpr), s, now, s.CanAffordChest(_hud), hp, lootPhase, balls, bell);
        if (bell is not null)
            TraceBell(frame, bell, d.Status, hp, now);
        if (d.Click is { } c)
        {
            var (x, y) = kit.Viewport.ToCss(c.X * frame.Width, c.Y * frame.Height);
            await kit.Input.ClickAsync(x, y);
        }
        var walk = d.Status.Length > 0 ? "走位：" + d.Status : "";
        return note.Length == 0 ? walk : walk.Length == 0 ? note : walk + " " + note;
    }

    /// <summary>
    /// 技能神祇「!」（<see cref="DeityIconReader"/>）：有開「升級卡自動選」才點；點了之後 <see cref="DeityClickGap"/> 內不重點。
    /// 有點回 true（這一輪不走位）。
    /// </summary>
    private async Task<bool> DeityIconAsync(Frame frame, double dpr, long now)
    {
        if (!_config.GetBool("pickSkill") || now - _deityClickAt < DeityClickGap || DeityIconReader.Find(frame, dpr) is not { } icon)
            return false;
        _deityClickAt = now;
        var (x, y) = _kit!.Viewport.ToCss(icon.X, icon.Y);
        await _kit.Input.ClickAsync(x, y);
        return true;
    }

    /// <summary>
    /// 讀尋寶率（時機見 <see cref="TreasureSettle"/>）。進野外滿 <see cref="ZoomDelay"/> 後才讀（畫面載入完）。
    /// 道具欄全空時不移滑鼠，尋寶率直接是 0、這一輪照常走位（回 false）；有移滑鼠讀（不管成功與否）回 true＝這一輪不走位。
    /// 讀失敗（中途跳出選卡畫面之類）保留上次的值。
    /// </summary>
    private async Task<bool> TreasureAsync(Frame frame, double dpr, long now)
    {
        // 活動不讀：尋寶率沒用，讀一次要停下來約 1 秒
        if (_treasureReader is null || _picker.State.IsEvent || now - _gate.Since < ZoomDelay)
            return false;
        var slots = TreasureReader.FilledSlots(frame, dpr);
        if (slots.Count != _treasureSlots)
            _treasureDue = Math.Min(_treasureDue, now);
        if (now < _treasureDue || now - _treasureAt < TreasureGap)
            return false;
        _treasureSlots = slots.Count;
        _treasureAt = now;
        if (slots.Count == 0)
        {
            SetTreasure(new TreasureRead(0, [], 0));
            _treasureDue = TreasureIdle;
            return false;
        }
        var kit = _kit!;
        var r = await _treasureReader.ReadAsync(frame, dpr, kit.Grabber.CaptureAsync, p =>
        {
            var (x, y) = kit.Viewport.ToCss(p.X, p.Y);
            return kit.Input.MoveAsync(x, y);
        }, () => Environment.TickCount64);
        var end = Environment.TickCount64;
        if (r is null)
        {
            _treasureDue = end + TreasureRetry;
            return true;
        }
        SetTreasure(r);
        _treasureDue = TreasureIdle;
        return true;
    }

    private void SetTreasure(TreasureRead r)
    {
        LastTreasure = r;
        _picker.State.Treasure = r.Total;
        _recorder.SetTreasure(r.Total, r.Items);
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
        // 一局最長約 17 分（1020 秒）：遊戲時間超過 18 分還在往上數＝卡在異常的局（實測 2026-11-24 組隊：隊友都離開後一個人困在 36:08、沒有怪），重新整理離開
        if (_clock.Now(now) is > StuckClockSecs && !_clock.Countdown)
            Watch.Force($"遊戲時間超過 {StuckClockSecs / 60} 分鐘（卡在異常的局）");
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
            var text = await _bannerReader.ReadAsync(frame, dpr);
            if (BannerReader.DodgeMs(text) is not null)
                _bellBanner = text;
            _planner.Bell.Announce(text, now);
        }
        if (!_planner.Bell.Active(now))
            return null;
        return new BellView(BellFinder.Find(frame, dpr, _planner.Bell.Seen), frame.Width, frame.Height, dpr);
    }

    /// <summary>
    /// 躲鐘紀錄一輪（<see cref="BellTrace"/>）：view null＝這一輪沒在躲鐘（剛結束的話把摘要交給對局紀錄）；要截圖就存到 shots/bell。
    /// frame null＝已經離開野外（死了、結算），只結束紀錄不截圖。
    /// </summary>
    private void TraceBell(Frame? frame, BellView? view, string status, double? hp, long now)
    {
        var t = _clock.Now(now);
        var clock = t is { } c ? $"{c / 60}:{c % 60:00}" : "";
        var (shot, done) = _bellTrace.Step(view is not null, _bellBanner, view, status, hp, clock, now);
        if (done is not null)
        {
            if (frame is null)
                done["interrupted"] = true;
            _recorder.Bell(done);
        }
        if (shot is null || frame is null)
            return;
        try
        {
            var b = view?.Bell;
            _bellShots.Save(frame, shot, DateTime.Now, new JsonObject
            {
                ["status"] = status,
                ["clock"] = clock,
                ["hp"] = hp,
                ["bell"] = b is null ? null : $"{b.X0},{b.Y0},{b.X1},{b.Y1} n{b.N} g{b.Gold} r{b.Red} d{b.Dim} fill{b.Fill:0.00}",
                ["banner"] = _bellBanner,
            });
        }
        catch
        {
            // 為什麼吞掉：診斷截圖存不了（磁碟滿、檔案被占用）不能影響躲鐘
        }
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
        // 角色名：組隊時隊長邀請、隊員確認邀請者用。只在還沒有的時候寫：
        // 實測（2026-11-23）角色框的名字常被吃掉第一個字（「挪威鮭魚」→「威魚」），而且兩種讀法一致，覆蓋掉正確的名字後隊長就找不到人
        if (info.Name.Length > 0 && _config.GetString("charName").Length == 0)
        {
            var cfg = (JsonObject)_config.Current.DeepClone();
            cfg["charName"] = info.Name;
            _config.Replace(cfg);
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
            ["treasure"] = (int)Math.Round(_picker.State.Treasure),
            ["cards"] = cards,
            ["act"] = status,
            ["why"] = d.Why,
        });
    }

    /// <summary>組隊中而且開了「組隊時原地站樁」（沒設定時預設開）</summary>
    private bool PartyStay()
        => _config.GetString("partyRole", "off") is "leader" or "member" && (_config.Current["partyStay"] is null || _config.GetBool("partyStay"));

    /// <summary>遊戲時間超過這麼多秒（還在往上數）就當成卡住</summary>
    private const int StuckClockSecs = 18 * 60;

    private int _fieldTimerHits;
    private long _memberCheckAt = long.MinValue / 2;
    private int _foreignLeaderHits;

    /// <summary>
    /// 組隊的隊員：每 20 秒看一次隊伍面板，隊長（面板最上面那個名字）不是本機的隊長分身就按「離開隊伍」，回到可以被自己隊長邀請的狀態。
    /// 實測（2026-11-24）：隊長重開程式時遊戲把隊長轉給隊員，之後真正的隊長回來邀請不到（好友名單顯示「組隊中」）、只好自己出發。
    /// 連續兩次都認出別人是隊長才離開（OCR 讀錯一次不算）；只在沒有任何對話框時看。
    /// </summary>
    private async Task<string?> PartyMemberAsync(Frame frame, double dpr, ScreenState s, long now)
    {
        if (_kit?.Ocr is not { } ocr || now - _memberCheckAt < 20_000)
            return null;
        if (s.PartyInvite is not null || s.PartyReady is not null || s.PartyChannel is not null || s.Solo is not null || s.Title is not null || s.CharSelect is not null
            || s.Return is not null || s.LootGreed is not null || s.AfkOk is not null || s.Discard is not null || s.DiscardOk is not null || s.StageList is not null || s.DiffTitle is not null)
            return null;
        _memberCheckAt = now;
        var leaders = PartyRoster.LeaderNames(PartyRoster.Others(AppPaths.Profile, DateTime.Now));
        if (leaders.Count == 0 || await PartyUi.ReadPanelLinesAsync(ocr, frame, dpr) is not { } lines || PartyUi.PanelLeader(lines) is not { } leader)
            return null;
        if (leaders.Any(n => PartyUi.NameLike(leader, n)))
        {
            _foreignLeaderHits = 0;
            return null;
        }
        if (++_foreignLeaderHits < 2 || PartyUi.LeaveButton(lines, dpr) is not { } leave)
            return null;
        _foreignLeaderHits = 0;
        var (x, y) = _kit.Viewport.ToCss(leave.Bounds.X + leave.Bounds.Width / 2, leave.Bounds.Y + leave.Bounds.Height / 2);
        await _kit.Input.ClickAsync(x, y);
        try
        {
            await Task.Delay(800);
            _partyShots.Save(await _kit.Grabber.CaptureAsync(), "離開隊伍", DateTime.Now, new JsonObject { ["leader"] = leader, ["ours"] = string.Join("、", leaders) });
        }
        catch
        {
            // 診斷存不了沒關係
        }
        return $"組隊：隊長是 {leader} 不是 {leaders[0]}，離開隊伍";
    }

    /// <summary>新的一局：清掉選卡、走位的狀態，開始記錄對局</summary>
    private void StartRun(bool expe)
    {
        // 組隊：等隊員的次數每局重算（實測 2026-11-24：等到上限先出發後計數器卡在上限，之後隊長再也不邀請）
        _partyWaits = 0;
        _partyHold = false;
        _picker.NewRun(isEvent: !expe);
        NewFieldRun();
        _recorder.Start(DateTime.Now, PickSettings.From(_config).Job, paused: false, expe ? "expe" : "event");
    }

    /// <summary>隊長多久看一次隊伍面板（OCR，約 100 ms）</summary>
    private const long PartyCheckEvery = 8_000;

    /// <summary>隊長兩次邀請之間至少隔多久（對方接受、換頻道要時間）</summary>
    private const long PartyInviteEvery = 25_000;

    /// <summary>隊員一直沒到齊時最多等幾次檢查（8 秒一次，約 2 分鐘），之後先帶著到齊的人出發（某個隊員卡住不能讓大家都停）</summary>
    private const int PartyMaxWaits = 15;

    private readonly EvidenceStore _partyShots = new(Path.Combine(AppPaths.ShotsDir, "party"), 100);
    private long _partyCheckAt = long.MinValue / 2;
    private long _unknownDialogAt = long.MinValue / 2;
    private long _partyInviteAt = long.MinValue / 2;
    private int _partyWaits;
    private bool _partyHold;

    /// <summary>
    /// 組隊的隊長：在大廳廣場（看得到遠征隊長或交談提示）時檢查隊伍面板，本機的隊員分身（PartyRoster，同一個大廳、正在執行）還沒在隊上就邀請；
    /// 沒到齊時不往遠征隊長走（回「組隊：等隊員加入」），等太久（PartyMaxWaits）就先出發。回 null＝這一輪照常走大廳流程。
    /// </summary>
    private async Task<string?> PartyLeaderAsync(Frame frame, double dpr, ScreenState screen, FlowSettings settings, long now)
    {
        if (_kit?.Ocr is not { } ocr || (screen.ExpeLeader is null && screen.LeaderPrompt is null))
            return null;
        if (now - _partyCheckAt < PartyCheckEvery)
            return _partyHold ? "組隊：等隊員加入" : null;
        _partyCheckAt = now;
        var members = PartyRoster.MemberNames(PartyRoster.Others(AppPaths.Profile, DateTime.Now), settings.PartyLobby);
        var missing = members.Count == 0 ? [] : PartyUi.Missing(members, await PartyUi.ReadPanelAsync(ocr, frame, dpr));
        if (missing.Count == 0 || _partyWaits >= PartyMaxWaits)
        {
            _partyHold = false;
            if (missing.Count == 0)
                _partyWaits = 0;
            return null;
        }
        _partyHold = true;
        _partyWaits++;
        if (now - _partyInviteAt < PartyInviteEvery)
            return "組隊：等隊員加入（" + string.Join("、", missing) + "）";
        _partyInviteAt = now;
        var invited = await InviteAsync(ocr, dpr, missing);
        return invited.Count > 0 ? "組隊：邀請 " + string.Join("、", invited) : "組隊：好友名單上找不到 " + string.Join("、", missing);
    }

    /// <summary>
    /// 從好友視窗邀請：按「好友」→ OCR 名單找每個要邀的人（離線的跳過）→ 點名字 → 選單裡的「邀請組隊」→ 最後按「關閉」。回成功點到「邀請組隊」的人。
    /// 每一步都重新截圖確認（視窗動畫約 0.5 秒），中途找不到就收起來不硬點。
    /// </summary>
    private async Task<List<string>> InviteAsync(OcrReader ocr, double dpr, IReadOnlyList<string> names)
    {
        var kit = _kit!;
        var invited = new List<string>();
        async Task Click(Point p)
        {
            var (x, y) = kit.Viewport.ToCss(p.X, p.Y);
            await kit.Input.ClickAsync(x, y);
        }
        async Task<(Frame F, IReadOnlyList<OcrLine> Lines)> Look(Func<Frame, Rectangle> area, int waitMs)
        {
            await Task.Delay(waitMs);
            var f = await kit.Grabber.CaptureAsync();
            var lines = await PartyUi.ReadBothAsync(ocr, f, area(f));
            try
            {
                // 診斷：每一步的畫面和讀到的字存到 shots/party（最多 100 張）
                _partyShots.Save(f, "邀請", DateTime.Now, new JsonObject
                {
                    ["names"] = string.Join("、", names),
                    ["lines"] = new JsonArray(lines.Select(l => (JsonNode?)$"{l.Text} @ {l.Bounds.X},{l.Bounds.Y},{l.Bounds.Width},{l.Bounds.Height}").ToArray()),
                });
            }
            catch
            {
                // 診斷存不了不影響邀請
            }
            return (f, lines);
        }
        static bool IsFriends(IReadOnlyList<OcrLine> lines) => lines.Any(l => OcrReader.Compact(l.Text).StartsWith("好友", StringComparison.Ordinal));

        var first = await kit.Grabber.CaptureAsync();
        await Click(PartyUi.FriendsButton(first, dpr));
        var (friends, rows) = await Look(f => PartyUi.FriendsArea(f, dpr), 900);
        // 視窗還在開（動畫、網路慢）：再等一下
        for (var i = 0; i < 2 && !IsFriends(rows); i++)
            (friends, rows) = await Look(f => PartyUi.FriendsArea(f, dpr), 700);
        if (!IsFriends(rows))
            return invited;
        foreach (var name in names)
        {
            var row = rows.FirstOrDefault(l => PartyUi.NameLike(l.Text, name) && !l.Text.Contains("離線"));
            if (row is null)
                continue;
            // 同一列有「組隊中」（OCR 常讀成「组隊中」、而且跟名字分成兩行）＝已經在某個隊伍裡，不用（也不能）邀請
            if (rows.Any(l => Math.Abs(l.Bounds.Y - row.Bounds.Y) < 8 * dpr && l.Text.Contains('隊')))
                continue;
            // 點名字開選單（行的左邊一點，避開同一行的「頻道 N」）
            var at = new Point(row.Bounds.X + (int)(15 * dpr), row.Bounds.Y + row.Bounds.Height / 2);
            await Click(at);
            // 選單以點擊位置為中心往上展開：只讀那一窄欄（寬讀會被好友視窗的邊框干擾，後面幾項讀不到）
            Rectangle MenuArea(Frame f) => Rectangle.Intersect(Rectangle.FromLTRB(at.X - (int)(60 * dpr), at.Y - (int)(260 * dpr), at.X + (int)(60 * dpr), at.Y + (int)(2 * dpr)),
                new Rectangle(0, 0, f.Width, f.Height));
            var (menuFrame, menu) = await Look(MenuArea, 700);
            // 整欄讀不到「邀請組隊」時，單獨讀最後一項那一格（選單最後一項在點擊位置上方 29 px）。
            // 實測 2026-11-24：對方在別的頻道、選單多一項「前往頻道 N」時，整欄怎麼放大都讀不到最後一項，單獨裁一格就讀得到（「謮隊」）；
            // 「檢舉」那一格單獨讀從來讀不出「隊」，所以還是只在讀到「隊」時才按
            if (!menu.Any(l => l.Text.Contains('隊') || l.Text.Contains("邀請")))
            {
                var last = new Point(at.X, at.Y - (int)Math.Round(29 * dpr));
                if (await PartyUi.IsInviteCellAsync(ocr, menuFrame, dpr, last))
                    menu = [.. menu, new OcrLine("邀請組隊", new Rectangle(last.X - 1, last.Y - 1, 2, 2))];
            }
            // 只按讀得到的「邀請組隊」（OCR 常讀成「组隊」）：選單裡只有這一項有「隊」。
            // 刻意不照固定位置按最後一項：已經組隊的人選單沒有「邀請組隊」，最後一項是「檢舉」（實測 2026-11-23 按到過，跳出檢舉視窗）
            if (menu.FirstOrDefault(l => l.Text.Contains('隊') || l.Text.Contains("邀請")) is { } item)
            {
                await Click(new Point(item.Bounds.X + item.Bounds.Width / 2, item.Bounds.Y + item.Bounds.Height / 2));
                invited.Add(name);
            }
            else
                await Click(at); // 沒看到「邀請組隊」：再點一次名字把選單收起來
            (friends, rows) = await Look(f => PartyUi.FriendsArea(f, dpr), 700);
        }
        // 關掉好友視窗：找「關閉」，找不到就再按一次「好友」
        if (rows.FirstOrDefault(l => OcrReader.Compact(l.Text) == "關閉") is { } close)
            await Click(new Point(close.Bounds.X + close.Bounds.Width / 2, close.Bounds.Y + close.Bounds.Height / 2));
        else
            await Click(PartyUi.FriendsButton(friends, dpr));
        return invited;
    }

    /// <summary>送狀態給面板（格式同 JS 的 status 訊息；尋寶率見 <see cref="TreasureAsync"/>，下一隻王見 <see cref="ClockAsync"/>）</summary>
    private void Report(string action)
    {
        History.Add(DateTime.Now, action);
        var s = FlowSettings.From(_config);
        var status = new JsonObject
        {
            ["runs"] = s.Runs,
            ["runLimit"] = s.RunLimit ? s.RunLimitN : 0,
            ["treasure"] = (int)Math.Round(_picker.State.Treasure),
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
