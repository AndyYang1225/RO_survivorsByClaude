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
    bool ExpeTalk = true, bool ExpePick = true, string ExpeStage = "斐揚洞穴", bool ExpeGo = true, string ExpeDiff = "keep", bool ExpeEnter = true,
    string PartyRole = "off", string PartyLobby = "約頓海姆")
{
    /// <summary>組隊模式（隊長或隊員）：大廳選多人大廳、不選單人大廳</summary>
    public bool Party => PartyRole is "leader" or "member";

    /// <summary>組隊的隊員：不自己出發，等隊長提議後按「準備好了！」</summary>
    public bool Member => PartyRole == "member";

    public static FlowSettings From(ConfigStore c) => new(
        c.GetBool("paused"), c.GetBool("soloLobby"), c.GetBool("autoEvent"), c.GetBool("autoExpe"), c.GetBool("autoReturn"),
        c.GetBool("runLimit"), c.GetInt("runLimitN", 10), c.GetInt("runs"),
        c.GetInt("tStartN", 800), c.GetInt("tReturnN", 1500), c.GetInt("tLobbyN", 700),
        c.Current["expeTalk"] is null || c.GetBool("expeTalk"),
        c.Current["expePick"] is null || c.GetBool("expePick"),
        c.GetString("expeStage", "斐揚洞穴"),
        c.Current["expeGo"] is null || c.GetBool("expeGo"),
        c.GetString("expeDiff", "keep"),
        c.Current["expeEnter"] is null || c.GetBool("expeEnter"),
        c.GetString("partyRole", "off"),
        c.GetString("partyLobby", "約頓海姆"));
}

/// <summary>
/// 這一輪的決定：顯示在面板上的動作文字、要點的位置（截圖像素，null＝不點）、要不要把局數 +1。
/// StartsRun＝這一輪按了「前往野外」，新的一局開始（選卡要清掉上一局的狀態）；Expe＝這一局是遠征（對局紀錄的 mode）。
/// WheelAt／WheelDelta＝在這個位置捲動滑鼠滾輪（遠征清單找地圖；deltaY 正＝往下）。
/// </summary>
public sealed record FlowDecision(string Action, Point? Click = null, bool CountRun = false, bool StartsRun = false,
    bool Expe = false, Point? WheelAt = null, double WheelDelta = 0, bool AwaitsField = false);
// AwaitsField＝組隊：按了「向隊伍提議」或「準備好了！」，要等全隊都準備好才會進野外（幾秒到幾十秒），
// 這段時間還在大廳，不能當成已經進野外（FieldGate 看到大廳會離開、隊長會再去找遠征隊長）。VisionEngine 看到野外的計時器才開始這一局

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

    /// <summary>組隊：提議／準備好之後最多等多久進野外（隊員沒回應就放棄，隊長重新來過）</summary>
    public const long AwaitField = 60_000;

    /// <summary>隊員在大廳等多久走一步（遊戲 5 分鐘沒動作算暫離）</summary>
    private const long AntiAfk = 180_000;

    /// <summary>按了「其餘全 Greed」後等多久再按「準備好了」（等按鈕狀態更新）</summary>
    private const long LootReadyDelay = 800;

    /// <summary>戰利品分配最多等多久（遊戲倒數約 15 秒）</summary>
    private const long LootWait = 25_000;

    /// <summary>維護中多久按一次「再檢查一次」</summary>
    private const long MaintenanceRetryEvery = 60_000;

    private long _maintAt = long.MinValue / 2;
    private long? _greedAt;
    private bool _lootReady;
    /// <summary>隊員防暫離那一步的動作文字（VisionEngine 在野外時認這個字串丟掉點擊）</summary>
    public const string AntiAfkAction = "組隊：等隊長（防暫離走一步）";

    private long? _idleSince;
    private long? _soloAt;
    private long? _readyAt;
    private long? _awaitUntil;

    /// <summary>組隊：剛按了「向隊伍提議」或「準備好了！」，還在等進野外</summary>
    public bool AwaitingField(long now) => _awaitUntil is { } u && now < u;

    /// <summary>真的進野外了（VisionEngine 看到計時器）：不用再等</summary>
    public void FieldEntered() => _awaitUntil = null;
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
        _soloAt = _readyAt = _discardAt = _titleAt = _charSelectAt = _startAt = _talkedAt = null;
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
            || s.ExpeLeader is not null || s.LeaderPrompt is not null || s.StageList is not null || s.DiffTitle is not null
            || s.PartyReady is not null || s.PartyChannel is not null)
            _returnAt = null;
        var limit = c.RunLimit && c.Runs >= c.RunLimitN;

        // 步驟 0：遊戲 0.7.8 起，被中斷的局再上線會先跳「未完成的一局（捨棄／繼續）」，按捨棄後再跳「已捨棄本局（OK）」，兩個都會擋住大廳。
        // 刻意選「捨棄」不選「繼續」：繼續的話會直接回到野外、不經過「前往野外」，視覺模式不知道已在野外（FieldGate 只信自己按的前往野外），不會走位。
        // 捨棄只在開了自動活動或自動遠征時才按：都沒開的話是使用者自己在玩，要不要繼續那一局由使用者決定。OK 只是關掉通知，一律按。
        if (s.DiscardOk is { } ok)
            return new("關閉已捨棄本局", Center(ok));
        // 組隊邀請：隊員只接受本機隊長分身的邀請；其他人的邀請（或沒開組隊時）在自動模式下拒絕，免得對話框擋住流程。
        // 拒絕在接受左邊 73 px（實測 2026-11-23：拒絕 x 1010～1070、接受 x 1083～1144，y 368～400）
        if (s.PartyInvite is { } invite)
        {
            if (c.Member && s.PartyInviteTrusted)
                return new("組隊：接受隊長的邀請", Center(invite));
            if (c.AutoEvent || c.AutoExpe)
                return new("拒絕不是自己隊長的組隊邀請", new Point(invite.X + invite.Width / 2 - (int)Math.Round(73 * s.Dpr), invite.Y + invite.Height / 2));
            return new("有人邀請你組隊（等你決定）");
        }
        // 實測（2026-11-21 多開）：「暫離」（暫停超過 5 分鐘被踢回首頁）、「中文頻道」（新帳號第一次進大廳）兩個通知會擋住流程。
        // 都只是通知，一律關掉：暫離按「接受」（之後回首頁，由首頁步驟接手）、中文頻道按「現在不要」（不改使用者的聊天設定）
        if (s.AfkOk is { } afk)
            return new("關閉暫離通知", Center(afk));
        if (s.ChannelNo is { } no)
            return new("關閉中文頻道提示", Center(no));
        // 組隊：「隊長在頻道 N。要加入嗎？」→ 前往隊長的頻道（不同頻道收不到隊長的出發提議）。
        // 只有隊員會被問；遠征視窗、難度對話框開著時不看（實測 2026-11-23：範本曾把難度對話框的「前往野外」認成「前往頻道」，隊長因此沒經過 StartsRun 就進了野外、不走位）
        if (c.Member && s.PartyChannel is { } channel && s.StageList is null && s.DiffTitle is null)
            return new("組隊：前往隊長的頻道", Center(channel));
        if ((c.AutoEvent || c.AutoExpe) && s.Discard is { } discard)
        {
            _discardAt ??= now;
            if (now - _discardAt <= DiscardDelay)
                return new("捨棄未完成的一局");
            _discardAt = null;
            return new("捨棄未完成的一局", Center(discard));
        }
        _discardAt = null;

        // 伺服器維護（實測 2026-11-24 凌晨：首頁右邊「維護中，幾分鐘後就會回來」＋「再檢查一次」）：每分鐘按一次，維護結束後回到首頁照常接手
        if ((c.AutoEvent || c.AutoExpe) && s.MaintenanceRetry is { } retry)
        {
            if (now - _maintAt < MaintenanceRetryEvery)
                return new("伺服器維護中，等待");
            _maintAt = now;
            return new("伺服器維護中：再檢查一次", Center(retry));
        }

        // 組隊結算的戰利品分配（使用者：直接按「其餘全 Greed」）：先按 Greed，再按旁邊的「準備好了」提早結束倒數。
        // 分配完之前「返回角色」是灰的，所以放在結算步驟前面
        // 最多等 LootWait：分配倒數約 15 秒，超過還看到就當成認錯（照常往下走結算、返回角色）
        if ((c.AutoEvent || c.AutoExpe) && s.LootGreed is { } greed && (_greedAt is null || now - _greedAt < LootWait))
        {
            if (_greedAt is null)
            {
                _greedAt = now;
                return new("戰利品：其餘全 Greed", Center(greed));
            }
            if (!_lootReady && s.LootReady is { } lr && now - _greedAt >= LootReadyDelay)
            {
                _lootReady = true;
                return new("戰利品：準備好了", Center(lr));
            }
            return new("戰利品：等分配完成");
        }
        if (s.LootGreed is null)
        {
            _greedAt = null;
            _lootReady = false;
        }

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

        // 步驟 1：「選擇大廳」→ 等 0.5 秒按「單人大廳」；組隊時改選指定的多人大廳（單人大廳不能組隊）
        if (c.Party && s.Solo is not null)
        {
            if (s.PartyLobby is not { } row)
                return new("找不到多人大廳：" + c.PartyLobby);
            _soloAt ??= now;
            if (now - _soloAt <= SoloDelay)
                return new("選多人大廳：" + c.PartyLobby);
            _soloAt = null;
            return new("選多人大廳：" + c.PartyLobby, Center(row));
        }
        if (!c.Party && c.SoloLobby && s.Solo is { } solo)
        {
            _soloAt ??= now;
            if (now - _soloAt <= SoloDelay)
                return new("選單人大廳");
            _soloAt = null;
            return new("選單人大廳", Center(solo));
        }
        _soloAt = null;

        // 組隊的隊員：隊長提議出發 →「準備好了！」＝新的一局（遠征）。局數到了就不按（留給使用者決定）
        if (c.Member && s.PartyReady is { } ready)
        {
            if (limit)
                return new("已達局數上限，不接受隊長的出發提議");
            _readyAt ??= now;
            if (now - _readyAt <= SoloDelay)
                return new("組隊：準備好了");
            _readyAt = null;
            _awaitUntil = now + AwaitField;
            return new("組隊：準備好了", Center(ready), Expe: true, AwaitsField: true);
        }
        _readyAt = null;

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

        // 組隊的隊員：不自己找 NPC、不自己出發，等隊長提議。
        // 在大廳廣場等太久會被遊戲當成「暫離」踢回首頁（實測 5 分鐘），錯過隊長的邀請：每 AntiAfk 在角色旁邊點一下走一步
        if (c.Member)
        {
            if (limit)
                return new("已達局數上限");
            // 不看 NPC 標籤：實測（2026-11-24）站的位置看不到「遠征隊長」標籤、節慶主持人只在自動活動時才找，結果每 5 分鐘被踢一次。
            // 走到這裡＝沒有任何對話框、結算、首頁等畫面；在野外時 VisionEngine 會把這一步丟掉
            _idleSince ??= now;
            if (now - _idleSince >= AntiAfk)
            {
                _idleSince = now;
                var (w, h) = (s.FrameSize.Width, s.FrameSize.Height);
                return new(AntiAfkAction, new Point(w / 2 + (int)(60 * s.Dpr), h / 2 + (int)(60 * s.Dpr)));
            }
            return new("組隊：等隊長提議出發");
        }

        // 步驟 3：「開始遊戲」／活動的「前往野外」→ 等 tStart 後按
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
        {
            if (diff.AwaitsField)
                _awaitUntil = now + AwaitField;
            return diff;
        }
        // 組隊的隊長：提議出發後等隊員按準備好（不要又去找遠征隊長、再提議一次）
        if (c.PartyRole == "leader" && _awaitUntil is { } until && now < until)
            return new("組隊：等隊員準備好");
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
