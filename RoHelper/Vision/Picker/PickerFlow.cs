namespace RoHelper.Vision.Picker;

/// <summary>這一輪選卡要做的事：面板狀態文字、要按的數字鍵或要點的位置（截圖座標），以及決策本身（寫選卡紀錄用）</summary>
public sealed record PickerAction(string Status, char? Key = null, Point? Click = null, PickDecision? Decision = null);

/// <summary>
/// 選卡流程（同 engine.js pickerStep）：選卡畫面出現後等 tPick 再判斷；做完放逐／專注／重抽後等畫面更新（同 JS 的 +400 ms）；
/// 選卡用數字鍵（同 JS pressKey），按了 0.6 秒畫面還在就改點卡片（同 JS 的 click 補救）。
/// 這兩個等待原本放寬成 700 ms、1.5 秒，是因為辨識在 UI 執行緒上、一輪要好幾秒；辨識移到背景加速後（2026-11-25）縮回來，連續升級時下一張卡不用多等。
/// 也負責一局內的狀態（<see cref="RunPickState"/>）：專注過的、學過的「學到後放逐」技能、鎖定的神祇。
/// 寶箱單張重抽：點那張卡的「重抽卡片」、等 Settle；組隊時每個寶箱畫面最多重抽 <see cref="PartyChestRerolls"/> 次。
/// 純邏輯：時間由呼叫端傳入，不截圖、不送輸入。
/// </summary>
public sealed class PickerFlow(Func<int, int>? random = null)
{
    /// <summary>專注、改點卡片之後的等待（卡片本身不換）</summary>
    private const long Settle = 400;

    /// <summary>
    /// 放逐、重抽全部、單張重抽之後：換上來的卡片會重新淡入，等久一點才讀。
    /// 實測（2026-11-25，C 柏伊亞嵐島）：重抽全部後 0.4 秒就讀，淡入中的「火焰之壁」被讀成「升火 , 內的」、也沒看到「新！」，
    /// 認不出是「不選」的技能，被當成一般卡用「最稀有」選走
    /// </summary>
    private const long NewCardSettle = 1_000;

    /// <summary>
    /// 按了選卡數字鍵之後，畫面還在就改點卡片的等待時間。
    /// 刻意不再更短：遊戲收到按鍵到關掉選卡畫面要一點時間，太短會在按鍵其實有效時又補點一次。
    /// 補點前會確認同一張卡還在原位（見 <see cref="Act"/>），補點的就是同一張卡，不會選錯；但多一次點擊沒有意義
    /// </summary>
    private const long KeyTimeout = 600;

    /// <summary>按了遊戲「自動」之後，選卡畫面還停這麼久沒被選走，就當沒按到</summary>
    private const long GameAutoTimeout = 5_000;

    /// <summary>組隊時每個寶箱畫面最多重抽幾次（單張＋重抽全部）：組隊選卡有秒數限制，連續重抽太多次會來不及（使用者 2026-11-25）</summary>
    private const int PartyChestRerolls = 3;

    /// <summary>這個選卡畫面已經重抽了幾次（<see cref="Gone"/> 歸零）</summary>
    private int _screenRerolls;

    /// <summary>道具分類（schema 的 Items）：寶箱單張重抽判斷「同類有沒有要的」。VisionEngine 啟動時設定</summary>
    public IReadOnlyList<KeyValuePair<string, List<string>>> ItemGroups { get; set; } = [];

    /// <summary>技能神祇選擇畫面出現後至少等多久才選（卡片淡入動畫，見 Act 裡的註解）</summary>
    private const long DeitySettle = 2_000;

    /// <summary>技能神祇畫面卡名一直讀不出來時最多等多久才照現有資訊選</summary>
    private const long DeityMaxWait = 6_000;

    private long? _seenAt;
    private long _readyAt;
    private (int Index, string Name, Rectangle Card)? _pendingPick;

    public RunPickState State { get; private set; } = new();

    /// <summary>新的一局：清掉專注過、學過、鎖定的神祇（同 JS 進野外時重設 st.picked／st.focused／st.deityLock）</summary>
    /// <param name="isEvent">這局是活動（尋寶率沒用）；不知道時 false</param>
    public void NewRun(bool isEvent = false) => State = new RunPickState { IsEvent = isEvent };

    /// <summary>畫面上沒有選卡：下次出現時重新等 tPick</summary>
    public void Gone()
    {
        _seenAt = null;
        _readyAt = 0;
        _pendingPick = null;
        _screenRerolls = 0;
    }

    /// <summary>還在等（剛出現未滿 tPick、動作後畫面還沒更新）就回 true：這一輪不用花時間 OCR</summary>
    public bool Waiting(long now, int pickDelay)
    {
        _seenAt ??= now;
        return now - _seenAt < pickDelay || now < _readyAt;
    }

    public PickerAction Act(PickerInfo p, PickSettings s, long now)
    {
        s = s.ForRun(State.IsEvent);
        var kind = p.IsChest ? "選寶箱卡" : "選升級卡";
        if (!(p.IsChest ? s.PickChest : s.PickSkill))
            return new("等你選卡");

        // 選卡的數字鍵沒反應（同一張卡還在原位）：改點那張卡（同 JS 按鍵後 250 ms 補一次 click）。
        // 卡名讀不到（空字串）時無法確認是同一張卡，不點。要比對卡名：連續升級時下一個選卡畫面可能已經出現（主迴圈間隔長時看不到中間沒有選卡的那一刻），那時不能點舊位置
        if (_pendingPick is { } pending)
        {
            _pendingPick = null;
            if (pending.Name.Length > 0 && pending.Index < p.Cards.Count && p.Cards[pending.Index].Name == pending.Name)
            {
                _readyAt = now + Settle;
                var card = pending.Card;
                return new($"選卡按鍵沒反應，改點卡片：{pending.Name}", Click: new Point(card.X + card.Width / 2, card.Y + card.Height / 2));
            }
        }

        // 技能神祇選擇畫面：沒有放逐／專注／重抽，也不受遊戲「自動」影響（自動技能不會幫你選神祇）；用數字鍵選，沒反應同樣會改點卡片。
        // 出現後至少等 DeitySettle 才選，不管 tPick 設多少：卡片是一張張淡入的，實測（2026-11-25）打開 1.5 秒時第 3、4 張還半透明，
        // OCR 把「火焰之壁 Deity」讀成「火壁 Deity」，太早讀會對不上設定的順序。每局只選一次，多等一點沒關係
        if (p.IsDeityPick)
        {
            if (now - (_seenAt ?? now) < DeitySettle)
                return new("等技能神祇卡片出現…");
            var dd = CardDecider.DecideDeity(p, s);
            // 神祇清單的都沒對上、又有卡名讀不出來（還在淡入）：再等，最多等到 DeityMaxWait，免得照技能順序／第一張選錯（實測 2026-11-26 選成火箭術）
            if (!dd.Why.StartsWith("技能神祇優先") && p.Cards.Any(c => c.Name.Length < 2) && now - (_seenAt ?? now) < DeityMaxWait)
                return new("等技能神祇卡名讀出來…");
            State.DeityPicked = true;
            var chosen = p.Cards[dd.Index!.Value];
            _pendingPick = (dd.Index.Value, chosen.Name, chosen.Bounds);
            _readyAt = now + KeyTimeout;
            return new($"選技能神祇：{chosen.Name}（{dd.Why}）", Key: (char)('1' + dd.Index.Value), Decision: dd);
        }

        // 已經按過這種畫面的遊戲「自動」（升級卡＝自動技能、寶箱卡＝自動寶箱，分開記）：交給遊戲選。
        // 同一個選卡畫面停超過 GameAutoTimeout 都沒被選走，代表沒按到（或被關掉），再按一次
        var autoName = p.IsChest ? "自動寶箱" : "自動技能";
        if (p.IsChest ? State.GameAutoChest : State.GameAutoSkill)
        {
            if (now - (_seenAt ?? now) < GameAutoTimeout)
                return new("交給遊戲" + autoName);
            if (p.IsChest)
                State.GameAutoChest = false;
            else
                State.GameAutoSkill = false;
        }
        if (p.Auto is { Usable: true } auto && CardDecider.GameAutoWhy(p, s, State) is { } why)
        {
            if (p.IsChest)
                State.GameAutoChest = true;
            else
                State.GameAutoSkill = true;
            _seenAt = now;
            return new($"按遊戲「{autoName}」（{why}）", Click: Center(auto.Bounds), Decision: new(PickDo.GameAuto, null, why));
        }

        var budget = p.IsChest && s.Party ? Math.Max(0, PartyChestRerolls - _screenRerolls) : int.MaxValue;
        var d = CardDecider.Decide(p, s, State, ItemGroups, random, budget);
        var name = d.Index is { } i ? p.Cards[i].Name : "";
        switch (d.Do)
        {
            case PickDo.Banish:
                if (CardDecider.SkillOf(p.Cards[d.Index!.Value], CardDecider.BanTargets(s)) is { } bw)
                    State.Banished.Add(bw);
                _readyAt = now + NewCardSettle;
                return new($"放逐：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Banish)!.Bounds), Decision: d);
            case PickDo.Focus:
                State.Focused.Add(d.FocusKey!);
                _readyAt = now + Settle;
                return new($"專注：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Focus)!.Bounds), Decision: d);
            case PickDo.Reroll:
                if (p.IsChest)
                    _screenRerolls++;
                _readyAt = now + NewCardSettle;
                return new($"重抽全部（{d.Why}）", Click: Center(p.RerollAll!.Bounds), Decision: d);
            case PickDo.RerollCard:
                _screenRerolls++;
                _readyAt = now + NewCardSettle;
                return new($"單張重抽：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Reroll)!.Bounds), Decision: d);
        }

        // 選卡：記住學過的「學到後放逐」技能、第一次選的神祇（之後只精煉同一個）
        var idx = d.Index!.Value;
        var picked = p.Cards[idx];
        if (CardDecider.IsDeity(picked) && State.DeityLock.Length == 0)
            State.DeityLock = CardDecider.DeityName(picked.Name);
        // 寶箱卡：記下拿過的道具，之後再出現（強化它）算要的卡，不會為它單張重抽
        if (p.IsChest && picked.Name.Length > 0)
            State.Owned.Add(CardDecider.SkillKey(picked.Name));
        // 選了尋寶卡：先把卡上的「+N% 尋寶率」加進尋寶率（使用者 2026-11-27：1229 已超過 300% 還連選 4 次驚奇秘藥）。
        // 寶箱常連開好幾個，中間沒機會移滑鼠重讀道具欄，不先加的話每次都用同一個舊值判斷上限。
        // 卡上寫的是這次增加的量（百科：每次精煉 +10～20%），下次讀道具欄會用實際值蓋掉
        State.Treasure += CardDecider.TreasureGain(picked);
        if (CardDecider.SkillOf(picked, s.Skills.After) is { } w)
            State.Picked.Add(w);
        // 不得已學了清單外的新技能（重抽、放逐都用完）：記下來，之後再出現就放逐
        if (!p.IsChest && picked.Name.Length > 0 && CardDecider.IsUnwantedNew(picked, s))
            State.Unwanted.Add(CardDecider.SkillKey(picked.Name));
        _pendingPick = (idx, picked.Name, picked.Bounds);
        _readyAt = now + KeyTimeout;
        var label = name.Length > 0 ? name : $"第 {idx + 1} 張";
        return new($"{kind}：{label}（{d.Why}）", Key: (char)('1' + idx), Decision: d);
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
