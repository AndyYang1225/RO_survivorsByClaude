using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>大廳流程用到的設定（每一輪從 cfg 讀一次的快照）。時間都是毫秒</summary>
public sealed record FlowSettings(
    bool Paused, bool SoloLobby, bool AutoEvent, bool AutoExpe, bool AutoReturn,
    bool RunLimit, int RunLimitN, int Runs, int TStart, int TReturn, int TLobby)
{
    public static FlowSettings From(ConfigStore c) => new(
        c.GetBool("paused"), c.GetBool("soloLobby"), c.GetBool("autoEvent"), c.GetBool("autoExpe"), c.GetBool("autoReturn"),
        c.GetBool("runLimit"), c.GetInt("runLimitN", 10), c.GetInt("runs"),
        c.GetInt("tStartN", 800), c.GetInt("tReturnN", 1500), c.GetInt("tLobbyN", 700));
}

/// <summary>
/// 這一輪的決定：顯示在面板上的動作文字、要點的位置（截圖像素，null＝不點）、要不要把局數 +1。
/// StartsRun＝這一輪按了活動的「前往野外」，新的一局開始（選卡要清掉上一局的狀態）
/// </summary>
public sealed record FlowDecision(string Action, Point? Click = null, bool CountRun = false, bool StartsRun = false);

/// <summary>
/// 大廳流程（視覺模式）：選單人大廳 → 走到節慶主持人 → 交談 → 開始遊戲 → 前往野外；結算時記一局並返回角色。
/// 判斷順序和等待時間照 engine.js 的 step()／lobbyStep()，差別只在「看到什麼」改由截圖辨識（<see cref="ScreenState"/>）提供。
/// 純邏輯：不截圖、不點擊、時間由呼叫端傳入，所以可以單元測試；實際截圖和點擊由 VisionEngine 負責。
/// 刻意不處理遠征（要用 OCR 選地圖、調難度滑桿），之後另寫計畫。
/// </summary>
public sealed class LobbyFlow
{
    /// <summary>「選擇大廳」出現後等多久才按（同 JS：500 ms，等視窗動畫跑完）</summary>
    private const long SoloDelay = 500;

    /// <summary>「未完成的一局」出現後等多久才按「捨棄」（同單人大廳，等對話框動畫跑完）</summary>
    private const long DiscardDelay = 500;

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
    /// 「返回角色」消失超過多久才算結算畫面真的結束（才允許下一次記局）。
    /// 範本比對在單一畫面可能因雜訊漏判一次（JS 看 DOM 不會），不寬限的話漏一格就會重複記一局
    /// </summary>
    private const long ReturnGrace = 2_000;

    /// <summary>
    /// 「返回角色」要連續幾輪都看到才當成結算畫面（記一局、按按鈕）。
    /// 實測（2026-11-20）野外畫面偶爾有一格讓範本誤判，只看一格就會多記一局、讓野外走位以為離開野外。
    /// </summary>
    private const int ReturnConfirmHits = 2;

    private long? _soloAt;
    private long? _discardAt;
    private long? _returnAt;
    private int _returnHits;
    private long _returnSeenAt;
    private long? _startAt;
    private long? _talkedAt;
    private long _festAt = long.MinValue / 2;
    private long _lastWalk = long.MinValue / 2;

    /// <summary>
    /// 只清等待中的計時（_soloAt／_discardAt／_startAt／_talkedAt／_lastWalk），改了設定或暫停後重新開始等待（同 JS：set 只重設 npcDone／npcTalked）。
    /// 刻意保留 _returnAt 和最後看到返回角色的時間：結算畫面上暫停或切分頁就清掉的話，恢復後會把同一局再記一次（JS 的 returnSeenAt 也不清）。
    /// 刻意保留 _festAt：按完開始遊戲 10 秒內改設定或暫停就清掉的話，之後的「前往野外」會被當成遠征而不按（JS 的 festAt 也不清）。
    /// </summary>
    public void Reset()
    {
        _soloAt = _discardAt = _startAt = _talkedAt = null;
        _lastWalk = long.MinValue / 2;
    }

    public FlowDecision Decide(ScreenState s, FlowSettings c, long now)
    {
        if (c.Paused)
            return new("已暫停");

        // 步驟 0：遊戲 0.7.8 起，被中斷的局再上線會先跳「未完成的一局（捨棄／繼續）」，按捨棄後再跳「已捨棄本局（OK）」，兩個都會擋住大廳。
        // 刻意選「捨棄」不選「繼續」：繼續的話會直接回到野外、不經過「前往野外」，視覺模式不知道已在野外（FieldGate 只信自己按的前往野外），不會走位。
        // 捨棄只在開了自動活動時才按：沒開的話是使用者自己在玩，要不要繼續那一局由使用者決定。OK 只是關掉通知，一律按。
        if (s.DiscardOk is { } ok)
            return new("關閉已捨棄本局", Center(ok));
        if (c.AutoEvent && s.Discard is { } discard)
        {
            _discardAt ??= now;
            if (now - _discardAt <= DiscardDelay)
                return new("捨棄未完成的一局");
            _discardAt = null;
            return new("捨棄未完成的一局", Center(discard));
        }
        _discardAt = null;

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
            // 消失太久才當成新的一次結算（惰性判斷，Decide 沒被呼叫的期間也算）
            if (_returnAt is not null && now - _returnSeenAt > ReturnGrace)
                _returnAt = null;
            // 還沒確認過的結算畫面：連續看到 ReturnConfirmHits 輪才算（已確認的漏判一格不受影響，靠上面的寬限）
            if (_returnAt is null && ++_returnHits < ReturnConfirmHits)
                return new("確認結算畫面");
            _returnHits = 0;
            var first = _returnAt is null;
            _returnAt ??= now;
            _returnSeenAt = now;
            if (first)
                _talkedAt = null;
            if (!c.AutoReturn)
                return new("結算中", CountRun: first);
            return new("準備返回角色", now - _returnAt > c.TReturn ? Center(back) : null, first);
        }
        // 沒看到「返回角色」：寬限期內不清 _returnAt（可能只是漏判一格），其餘步驟照常往下走
        _returnHits = 0;
        if (_returnAt is not null && now - _returnSeenAt > ReturnGrace)
            _returnAt = null;

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
        if (limit && (s.GoField is not null || s.Start is not null))
            return new("已達局數上限，停止自動開始");

        // 步驟 4：大廳 → 有交談提示就按；否則看得到節慶主持人就往那邊走
        if (c.AutoExpe)
            return new("視覺模式還不支援遠征");
        if (!c.AutoEvent)
            return new("待機");
        if (limit)
            return new("已達局數上限");
        if (s.TalkPrompt is { } prompt)
        {
            if (_talkedAt is { } t && now - t < TalkRetry)
                return new("和節慶主持人交談");
            _talkedAt = now;
            return new("和節慶主持人交談", Center(prompt));
        }
        if (_talkedAt is { } talked && now - talked < TalkRetry)
            return new("等待對話框");
        if (s.FestivalHost is { } host)
        {
            if (now - _lastWalk < c.TLobby)
                return new("走向節慶主持人");
            _lastWalk = now;
            return new("走向節慶主持人", WalkTarget(s, host));
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
