namespace RoHelper.Vision;

/// <summary>
/// 自動遠征的遠征視窗與難度對話框（移植 engine.js stageStep 619～645、applyDifficulty 648～657、前往野外 1329～1338）。
/// 走到遠征隊長、交談由 <see cref="LobbyFlow"/> 處理（跟節慶主持人同一套）。純邏輯：時間由呼叫端傳入，看到什麼由 <see cref="ScreenState"/>、<see cref="StageListView"/> 提供。
///
/// 跟 JS 不同：
/// - 找不到地圖時先捲動清單再找（JS 讀網頁不用捲動）：往下捲 3 次、再往上捲 6 次，都找不到才「找不到地圖：X」。
/// - 「尚未開放」、「跳過劇情」還沒在實機看過長什麼樣子，不做：選了不能出發的地圖時看不到藍色「出發」，顯示「X 目前不能出發，請換一張地圖」（同 JS）。
/// - 難度：JS 直接設定滑桿的值；視覺模式點滑桿軌道的最右端（全部拉滿）或最左端（全部最低），每輪點一條。
/// - 選了地圖後等 <see cref="PickRetry"/> 才再點（畫面還沒更新時不重複點）。
/// </summary>
public sealed class ExpeditionFlow
{
    /// <summary>點了地圖卡之後，右邊還沒換成那張地圖時隔多久再點一次</summary>
    private const long PickRetry = 1500;

    /// <summary>捲動清單的間隔（等捲動動畫跑完、下一輪 OCR 讀到新的內容）</summary>
    private const long ScrollEvery = 600;

    /// <summary>找地圖時往下捲幾次、總共捲幾次（之後往上捲）</summary>
    private const int ScrollDown = 3;
    private const int ScrollMax = 9;

    /// <summary>每次捲動的量（deltaY，正＝往下）</summary>
    private const double ScrollDelta = 300;

    /// <summary>
    /// 難度對話框的滑桿位置（dpr 1，相對難度標題範本（「難度」兩個字）的左上角）：實測（遠征難度）標題範本在 (772, 168)，
    /// 五條滑桿軌道 x 990～1097、中心 y 219／247／275／303／331；最右端點 x 1096、最左端點 x 991
    /// </summary>
    private const int SliderMinX = 219, SliderMaxX = 324, SliderY = 51, SliderStep = 28, Sliders = 5;

    private long? _stageAt;
    private long _pickAt = long.MinValue / 2;
    private int _scrolls;
    private long _scrollAt = long.MinValue / 2;
    private int _sliders;
    private long? _enterAt;

    /// <summary>改了設定或暫停後重新開始等待（同 LobbyFlow.Reset）</summary>
    public void Reset()
    {
        _stageAt = null;
        _pickAt = long.MinValue / 2;
        _scrolls = 0;
        _enterAt = null;
    }

    /// <summary>
    /// 遠征視窗（同 JS stageStep）：沒看到遠征視窗回 null。
    /// 選地圖（expePick）：清單上找 expeStage → 右邊還不是那張地圖就點那張卡「② 選擇地圖：X」；找不到就捲動清單。
    /// 出發（expeGo）：等 tStart 後點藍色「出發」「③ 出發：X」；沒有藍色出發 →「X 目前不能出發，請換一張地圖」。
    /// view 是 null（沒有繁中 OCR）時沒辦法選地圖，只做出發。
    /// </summary>
    public FlowDecision? Stage(ScreenState s, StageListView? view, FlowSettings c, bool limit, long now)
    {
        if (s.StageList is null)
        {
            _stageAt = null;
            _scrolls = 0;
            return null;
        }
        if (limit)
            return new("已達局數上限");
        string want;
        if (c.ExpePick && view is not null)
        {
            want = c.ExpeStage;
            if (view.Selected != want)
            {
                _stageAt = null;
                if (view.Cards.FirstOrDefault(x => x.Stage == want) is { } card)
                {
                    _scrolls = 0;
                    if (now - _pickAt < PickRetry)
                        return new("② 選擇地圖：" + want);
                    _pickAt = now;
                    return new("② 選擇地圖：" + want, Center(card.Bounds));
                }
                if (_scrolls >= ScrollMax)
                    return new("找不到地圖：" + want);
                if (now - _scrollAt < ScrollEvery)
                    return new("找地圖：" + want);
                _scrollAt = now;
                _scrolls++;
                return new("找地圖：" + want, WheelAt: Center(view.List), WheelDelta: _scrolls <= ScrollDown ? ScrollDelta : -ScrollDelta);
            }
        }
        else
            want = view?.Selected ?? "";
        if (!c.ExpeGo)
            return new("等你按「出發」");
        _stageAt ??= now;
        if (now - _stageAt < c.TStart)
            return new("準備出發：" + want);
        if (s.ExpeGo is { } go)
        {
            _stageAt = null;
            return new("③ 出發：" + want, Center(go));
        }
        return new(want + " 目前不能出發，請換一張地圖");
    }

    /// <summary>
    /// 難度對話框（同 JS 前往野外的遠征分支）：沒看到難度標題和裡面的「前往野外」回 null（並清掉這次調過的滑桿，同 JS diffDone）。
    /// expeDiff＝max／min 時先每輪點一條滑桿（最右端／最左端），點完五條、而且對話框出現滿 tStart 後按「前往野外」→ 新的一局（StartsRun、Expe）。
    /// expeEnter 沒勾時不按（等使用者自己按）。
    /// </summary>
    public FlowDecision? Difficulty(ScreenState s, FlowSettings c, bool limit, long now)
    {
        if (s.DiffTitle is not { } title || s.DiffGo is not { } go)
        {
            _sliders = 0;
            _enterAt = null;
            return null;
        }
        if (limit)
            return new("已達局數上限，停止自動開始");
        if (!c.ExpeEnter)
            return new("待機");
        // 組隊的隊長：按鈕是「向隊伍提議」，按了之後隊員都按「準備好了」才一起進野外（按的這一輪就當成新的一局，隊員沒接受的話 FieldGate 看到大廳會離開）
        var action = s.DiffGoIsProposal ? "④ 向隊伍提議出發" : "④ 準備進入關卡";
        _enterAt ??= now;
        if (c.ExpeDiff is "max" or "min" && _sliders < Sliders)
        {
            var x = title.X + (c.ExpeDiff == "max" ? SliderMaxX : SliderMinX) * s.Dpr;
            var y = title.Y + (SliderY + SliderStep * _sliders) * s.Dpr;
            _sliders++;
            return new(action, new Point((int)Math.Round(x), (int)Math.Round(y)));
        }
        if (now - _enterAt <= c.TStart)
            return new(action);
        _enterAt = null;
        // 組隊提議：要等隊員都準備好才進野外，不是馬上開局（見 FlowDecision.AwaitsField）
        return s.DiffGoIsProposal
            ? new(action, Center(go), Expe: true, AwaitsField: true)
            : new(action, Center(go), StartsRun: true, Expe: true);
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
