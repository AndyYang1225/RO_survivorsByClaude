using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>
/// 大廳流程用到的設定（每一輪從 cfg 讀一次的快照）。時間都是毫秒。
/// 遠征（autoExpe 底下）：expeTalk（① 走到遠征隊長並交談）、expePick／expeStage（② 選地圖）、expeGo（③ 按出發）、
/// expeDiff（難度：keep 維持遊戲設定／max 全部拉滿／min 全部最低）、expeEnter（④ 按前往野外）。
/// 遠征的開關沒設定時同 schema 的預設（勾選、地圖斐揚洞穴、難度 keep）。新加的欄位放在最後並給預設值，既有的呼叫端不用改。
/// </summary>
public sealed record FlowSettings(
    bool Paused, bool SoloLobby, bool AutoEvent, bool AutoExpe, bool AutoReturn,
    bool RunLimit, int RunLimitN, int Runs, int TStart, int TReturn, int TLobby,
    bool ExpeTalk = true, bool ExpePick = true, string ExpeStage = "斐揚洞穴", bool ExpeGo = true, string ExpeDiff = "keep", bool ExpeEnter = true)
{
    public static FlowSettings From(ConfigStore c) => new(
        c.GetBool("paused"), c.GetBool("soloLobby"), c.GetBool("autoEvent"), c.GetBool("autoExpe"), c.GetBool("autoReturn"),
        c.GetBool("runLimit"), c.GetInt("runLimitN", 10), c.GetInt("runs"),
        c.GetInt("tStartN", 800), c.GetInt("tReturnN", 1500), c.GetInt("tLobbyN", 700),
        c.Current["expeTalk"] is null || c.GetBool("expeTalk"),
        c.Current["expePick"] is null || c.GetBool("expePick"),
        c.GetString("expeStage", "斐揚洞穴"),
        c.Current["expeGo"] is null || c.GetBool("expeGo"),
        c.GetString("expeDiff", "keep"),
        c.Current["expeEnter"] is null || c.GetBool("expeEnter"));
}

/// <summary>
/// 這一輪的決定：顯示在面板上的動作文字、要點的位置（截圖像素，null＝不點）、要不要把局數 +1。
/// StartsRun＝這一輪按了「前往野外」，新的一局開始（選卡要清掉上一局的狀態）；Expe＝這一局是遠征（對局紀錄的 mode）。
/// WheelAt／WheelDelta＝在這個位置捲動滑鼠滾輪（遠征清單找地圖；deltaY 正＝往下）。
/// </summary>
public sealed record FlowDecision(string Action, Point? Click = null, bool CountRun = false, bool StartsRun = false,
    bool Expe = false, Point? WheelAt = null, double WheelDelta = 0);

/// <summary>
/// 大廳流程（視覺模式）：選單人大廳 → 走到節慶主持人 → 交談 → 開始遊戲 → 前往野外；結算時記一局並返回角色。
/// 自動遠征：走到遠征隊長 → 交談 → 遠征視窗選地圖、出發 → 難度對話框 → 前往野外（遠征視窗與難度對話框見 <see cref="ExpeditionFlow"/>）。
/// 判斷順序和等待時間照 engine.js 的 step()／lobbyStep()，差別只在「看到什麼」改由截圖辨識（<see cref="ScreenState"/>）提供。
/// 純邏輯：不截圖、不點擊、時間由呼叫端傳入，所以可以單元測試；實際截圖和點擊由 VisionEngine 負責。
/// </summary>
public sealed class LobbyFlow
{
    /// <summary>「選擇大廳」出現後等多久才按（同 JS：500 ms，等視窗動畫跑完）</summary>
    private const long SoloDelay = 500;

    /// <summary>「未完成的一局」出現後等多久才按「捨棄」（同單人大廳，等對話框動畫跑完）</summary>
    private const long DiscardDelay = 500;

    /// <summary>首頁、角色選擇的「開始遊戲」出現後等多久才按（畫面切換和載入比對話框慢，多等一點）</summary>
    private const long TitleDelay = 1000;

    /// <summary>按「開始遊戲」後多久內出現的「前往野外」算活動流程（同 JS 的 festAt 10 秒；遠征的「前往野外」不能亂按）</summary>
    private const long FestivalWindow = 10_000;

    /// <summary>
    /// 按了交談提示後，提示還在就隔多久再按一次（點擊沒生效時重試）。這段時間內也不往 NPC 走：
    /// 提示消失多半是對話框開了，此時走路會點到對話框把它關掉（同 JS 的 npcDone）
    /// </summary>
    private const long TalkRetry = 5_000;

    /// <summary>名字標籤底部往下多少 CSS px 是 NPC 腳下（同 JS 的 +45）</summary>
    private const int LabelToFeet = 45;

    /// <summary>點地面時離畫面邊緣至少多少 CSS px（邊緣是 UI，點到會開別的東西）</summary>
    private const int EdgeMargin = 20;

    /// <summary>
    /// 「返回角色」要連續幾輪都看到才當成結算畫面（記一局、按按鈕）。
    /// 實測（2026-11-20）野外畫面偶爾有一格讓範本誤判，只看一格就會多記一局、讓野外走位以為離開野外。
    /// </summary>
    private const int ReturnConfirmHits = 2;

    private long? _soloAt;
    private long? _discardAt;
    private long? _titleAt;
    private long? _charSelectAt;
    /// <summary>
    /// 這次結算畫面第一次確認的時間；null＝還沒記過這一局。
    /// 刻意不用「消失多久」來判斷結算結束：實測（2026-11-21）主迴圈 1500 ms、加上結算時 OCR 讀畫面，兩次看到「返回角色」相隔超過 2 秒，
    /// 同一局被記了兩次。改成看到任何大廳畫面（首頁、選角色、單人大廳、節慶主持人…）才算這次結算結束。
    /// </summary>
    private long? _returnAt;
    private int _returnHits;
    private long? _startAt;
    private long? _talkedAt;
    private long _festAt = long.MinValue / 2;
    private long _lastWalk = long.MinValue / 2;
    private readonly ExpeditionFlow _expe = new();

    /// <summary>
    /// 只清等待中的計時（_soloAt／_discardAt／_titleAt／_charSelectAt／_startAt／_talkedAt／_lastWalk），改了設定或暫停後重新開始等待（同 JS：set 只重設 npcDone／npcTalked）。
    /// 刻意保留 _returnAt：結算畫面上暫停或切分頁就清掉的話，恢復後會把同一局再記一次（JS 的 returnSeenAt 也不清）。
    /// 刻意保留 _festAt：按完開始遊戲 10 秒內改設定或暫停就清掉的話，之後的「前往野外」會被當成遠征而不按（JS 的 festAt 也不清）。
    /// </summary>
    public void Reset()
    {
        _soloAt = _discardAt = _titleAt = _charSelectAt = _startAt = _talkedAt = null;
        _lastWalk = long.MinValue / 2;
        _expe.Reset();
    }

    /// <summary>stages＝遠征視窗 OCR 到的內容（沒開遠征視窗、沒有繁中 OCR 時是 null）</summary>
    public FlowDecision Decide(ScreenState s, FlowSettings c, long now, StageListView? stages = null)
    {
        if (c.Paused)
            return new("已暫停");

        // 看到結算以外的大廳畫面＝上一次結算已經結束，之後再看到「返回角色」就是新的一局
        if (s.Solo is not null || s.Start is not null || s.GoField is not null || s.FestivalHost is not null || s.TalkPrompt is not null
            || s.Title is not null || s.CharSelect is not null || s.Discard is not null || s.DiscardOk is not null || s.AfkOk is not null || s.ChannelNo is not null
            || s.ExpeLeader is not null || s.LeaderPrompt is not null || s.StageList is not null || s.DiffTitle is not null)
            _returnAt = null;

        // 步驟 0：遊戲 0.7.8 起，被中斷的局再上線會先跳「未完成的一局（捨棄／繼續）」，按捨棄後再跳「已捨棄本局（OK）」，兩個都會擋住大廳。
        // 刻意選「捨棄」不選「繼續」：繼續的話會直接回到野外、不經過「前往野外」，視覺模式不知道已在野外（FieldGate 只信自己按的前往野外），不會走位。
        // 捨棄只在開了自動活動或自動遠征時才按：都沒開的話是使用者自己在玩，要不要繼續那一局由使用者決定。OK 只是關掉通知，一律按。
        if (s.DiscardOk is { } ok)
            return new("關閉已捨棄本局", Center(ok));
        // 實測（2026-11-21 多開）：「暫離」（暫停超過 5 分鐘被踢回首頁）、「中文頻道」（新帳號第一次進大廳）兩個通知會擋住流程。
        // 都只是通知，一律關掉：暫離按「接受」（之後回首頁，由首頁步驟接手）、中文頻道按「現在不要」（不改使用者的聊天設定）
        if (s.AfkOk is { } afk)
            return new("關閉暫離通知", Center(afk));
        if (s.ChannelNo is { } no)
            return new("關閉中文頻道提示", Center(no));
        if ((c.AutoEvent || c.AutoExpe) && s.Discard is { } discard)
        {
            _discardAt ??= now;
            if (now - _discardAt <= DiscardDelay)
                return new("捨棄未完成的一局");
            _discardAt = null;
            return new("捨棄未完成的一局", Center(discard));
        }
        _discardAt = null;

        // 步驟 0.5：重開程式或斷線後會回到首頁（開始遊戲！）→ 角色選擇（開始遊戲）。
        // 只在開了自動活動或自動遠征時才按，理由同捨棄：都沒開的話是使用者自己在玩，不要搶。
        // 角色選擇刻意直接按目前選中的角色、不切換角色：用哪隻由使用者自己決定（遊戲會記住上次選的）。
        if ((c.AutoEvent || c.AutoExpe) && s.Title is { } title)
        {
            _titleAt ??= now;
            if (now - _titleAt <= TitleDelay)
                return new("首頁：開始遊戲");
            _titleAt = null;
            return new("首頁：開始遊戲", Center(title));
        }
        _titleAt = null;
        if ((c.AutoEvent || c.AutoExpe) && s.CharSelect is { } select)
        {
            _charSelectAt ??= now;
            if (now - _charSelectAt <= TitleDelay)
                return new("選角色：開始遊戲");
            _charSelectAt = null;
            return new("選角色：開始遊戲", Center(select));
        }
        _charSelectAt = null;

        // 步驟 1：「選擇大廳」→ 等 0.5 秒按「單人大廳」
        if (c.SoloLobby && s.Solo is { } solo)
        {
            _soloAt ??= now;
            if (now - _soloAt <= SoloDelay)
                return new("選單人大廳");
            _soloAt = null;
            return new("選單人大廳", Center(solo));
        }
        _soloAt = null;

        // 步驟 2：結算畫面 → 第一次看到時記一局；等 tReturn 後按「返回角色」
        if (s.Return is { } back)
        {
            // 還沒確認過的結算畫面：連續看到 ReturnConfirmHits 輪才算（已確認的結算畫面漏判幾格都不會重記，見 _returnAt）
            if (_returnAt is null && ++_returnHits < ReturnConfirmHits)
                return new("確認結算畫面");
            _returnHits = 0;
            var first = _returnAt is null;
            _returnAt ??= now;
            if (first)
                _talkedAt = null;
            if (!c.AutoReturn)
                return new("結算中", CountRun: first);
            return new("準備返回角色", now - _returnAt > c.TReturn ? Center(back) : null, first);
        }
        // 沒看到「返回角色」：不清 _returnAt（可能只是漏判幾格；要看到大廳畫面才清，見開頭），其餘步驟照常往下走
        _returnHits = 0;

        // 步驟 3：「開始遊戲」／活動的「前往野外」→ 等 tStart 後按
        var limit = c.RunLimit && c.Runs >= c.RunLimitN;
        Rectangle? button = null;
        var isStart = false;
        if (c.AutoEvent && !limit)
        {
            if (s.GoField is { } go && now - _festAt < FestivalWindow)
                button = go;
            else if (s.Start is { } start)
                (button, isStart) = (start, true);
        }
        if (button is { } b)
        {
            var action = isStart ? "準備開始遊戲" : "準備前往野外";
            _startAt ??= now;
            if (now - _startAt <= c.TStart)
                return new(action);
            _startAt = null;
            if (isStart)
                _festAt = now;
            return new(action, Center(b), StartsRun: !isStart);
        }
        _startAt = null;
        // 遠征的難度對話框（同 JS：前往野外跟活動的開始遊戲在同一步）
        if (c.AutoExpe && _expe.Difficulty(s, c, limit, now) is { } diff)
            return diff;
        if (limit && (s.GoField is not null || s.Start is not null))
            return new("已達局數上限，停止自動開始");

        // 步驟 4：遠征視窗（同 JS lobbyStep 先做 stageStep）
        if (c.AutoExpe && _expe.Stage(s, stages, c, limit, now) is { } stage)
            return stage;

        // 步驟 5：大廳 → 有交談提示就按；否則看得到 NPC 就往那邊走（遠征：遠征隊長；活動：節慶主持人，同 JS lobbyStep 的 target）
        var (name, talk, npc) = c.AutoExpe ? ("遠征隊長", s.LeaderPrompt, s.ExpeLeader) : ("節慶主持人", s.TalkPrompt, s.FestivalHost);
        if (c.AutoExpe ? !c.ExpeTalk : !c.AutoEvent)
            return new("待機");
        if (limit)
            return new("已達局數上限");
        if (talk is { } prompt)
        {
            if (_talkedAt is { } t && now - t < TalkRetry)
                return new("和" + name + "交談");
            _talkedAt = now;
            return new("和" + name + "交談", Center(prompt));
        }
        if (_talkedAt is { } talked && now - talked < TalkRetry)
            return new("等待對話框");
        if (npc is { } host)
        {
            if (now - _lastWalk < c.TLobby)
                return new("走向" + name);
            _lastWalk = now;
            return new("走向" + name, WalkTarget(s, host));
        }
        return new(s.Missing.Count > 0 ? "缺少範本：" + string.Join("、", s.Missing) : "待機");
    }

    /// <summary>
    /// 往 NPC 走的點擊位置（同 JS lobbyStep 的算法）。鏡頭跟著角色，所以角色當作在畫面正中間；
    /// 方向是畫面中心往名字標籤底部中央，走的距離夾到畫面短邊的 35%，之後才往下加 45 CSS px（NPC 腳下），
    /// 最後點的位置離畫面邊緣至少 20 CSS px。腳下的偏移不算進距離，和 JS 一樣。
    /// 刻意不辨識自己角色的名字：每個玩家的名字不同，沒辦法做成固定範本。
    /// </summary>
    public static Point WalkTarget(ScreenState s, Rectangle label)
    {
        var (w, h) = (s.FrameSize.Width, s.FrameSize.Height);
        double cx = w / 2.0, cy = h / 2.0;
        var dx = label.X + label.Width / 2.0 - cx;
        var dy = label.Bottom - cy;
        var dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var k = Math.Min(1, Math.Min(w, h) * 0.35 / dist);
        var margin = EdgeMargin * s.Dpr;
        return new Point(
            (int)Math.Round(Math.Clamp(cx + dx * k, margin, w - margin)),
            (int)Math.Round(Math.Clamp(cy + dy * k + LabelToFeet * s.Dpr, margin, h - margin)));
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
