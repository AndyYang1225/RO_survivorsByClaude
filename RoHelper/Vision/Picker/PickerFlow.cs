namespace RoHelper.Vision.Picker;

/// <summary>這一輪選卡要做的事：面板狀態文字、要按的數字鍵或要點的位置（截圖座標），以及決策本身（寫選卡紀錄用）</summary>
public sealed record PickerAction(string Status, char? Key = null, Point? Click = null, PickDecision? Decision = null);

/// <summary>
/// 選卡流程（同 engine.js pickerStep）：選卡畫面出現後等 tPick 再判斷；做完放逐／專注／重抽後等畫面更新（同 JS 的 +400 ms，
/// 視覺模式多算截圖和 OCR 的時間，用 700 ms）；選卡用數字鍵（同 JS pressKey），按了 1.5 秒畫面還在就改點卡片（同 JS 的 click 補救）。
/// 也負責一局內的狀態（<see cref="RunPickState"/>）：專注過的、學過的「學到後放逐」技能、鎖定的神祇。
/// 純邏輯：時間由呼叫端傳入，不截圖、不送輸入。
/// </summary>
public sealed class PickerFlow
{
    /// <summary>放逐／專注／重抽之後，等畫面換成新卡片的時間</summary>
    private const long Settle = 700;

    /// <summary>按了選卡數字鍵之後，畫面還在就改點卡片的等待時間</summary>
    private const long KeyTimeout = 1_500;

    /// <summary>按了遊戲「自動」之後，選卡畫面還停這麼久沒被選走，就當沒按到</summary>
    private const long GameAutoTimeout = 5_000;

    private long? _seenAt;
    private long _readyAt;
    private (int Index, string Name, Rectangle Card)? _pendingPick;

    public RunPickState State { get; private set; } = new();

    /// <summary>新的一局：清掉專注過、學過、鎖定的神祇（同 JS 進野外時重設 st.picked／st.focused／st.deityLock）</summary>
    public void NewRun() => State = new RunPickState();

    /// <summary>畫面上沒有選卡：下次出現時重新等 tPick</summary>
    public void Gone()
    {
        _seenAt = null;
        _readyAt = 0;
        _pendingPick = null;
    }

    /// <summary>還在等（剛出現未滿 tPick、動作後畫面還沒更新）就回 true：這一輪不用花時間 OCR</summary>
    public bool Waiting(long now, int pickDelay)
    {
        _seenAt ??= now;
        return now - _seenAt < pickDelay || now < _readyAt;
    }

    public PickerAction Act(PickerInfo p, PickSettings s, long now)
    {
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

        // 已經按過遊戲「自動」：交給遊戲選。同一個選卡畫面停超過 GameAutoTimeout 都沒被選走，代表沒按到（或被關掉），再按一次
        if (State.GameAuto)
        {
            if (now - (_seenAt ?? now) < GameAutoTimeout)
                return new("交給遊戲自動選卡");
            State.GameAuto = false;
        }
        if (p.Auto is { Usable: true } auto && CardDecider.ReadyForGameAuto(s, State))
        {
            State.GameAuto = true;
            _seenAt = now;
            var why = "放逐清單都放逐過了：" + string.Join("、", CardDecider.BanTargets(s));
            return new($"按遊戲「自動」（{why}）", Click: Center(auto.Bounds), Decision: new(PickDo.GameAuto, null, why));
        }

        var d = CardDecider.Decide(p, s, State);
        var name = d.Index is { } i ? p.Cards[i].Name : "";
        switch (d.Do)
        {
            case PickDo.Banish:
                if (CardDecider.SkillOf(p.Cards[d.Index!.Value], CardDecider.BanTargets(s)) is { } bw)
                    State.Banished.Add(bw);
                _readyAt = now + Settle;
                return new($"放逐：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Banish)!.Bounds), Decision: d);
            case PickDo.Focus:
                State.Focused.Add(d.FocusKey!);
                _readyAt = now + Settle;
                return new($"專注：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Focus)!.Bounds), Decision: d);
            case PickDo.Reroll:
                _readyAt = now + Settle;
                return new($"重抽全部（{d.Why}）", Click: Center(p.RerollAll!.Bounds), Decision: d);
        }

        // 選卡：記住學過的「學到後放逐」技能、第一次選的神祇（之後只精煉同一個）
        var idx = d.Index!.Value;
        var picked = p.Cards[idx];
        if (CardDecider.IsDeity(picked) && State.DeityLock.Length == 0)
            State.DeityLock = CardDecider.DeityName(picked.Name);
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
