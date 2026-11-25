namespace RoHelper.Vision.Picker;

/// <summary>卡片下方的動作按鈕種類</summary>
public enum ActKind
{
    Reroll,
    Focus,
    Banish,
}

/// <summary>
/// 卡片下方的一顆動作按鈕（截圖像素座標）。Left＝按鈕上小圓圈的剩餘次數；讀不到數字時，能按就當 99、不能按就當 0（同 JS 的 actLeft）。
/// </summary>
public sealed record ActButton(ActKind Kind, Rectangle Bounds, bool Enabled, int Left)
{
    public bool Usable => Enabled && Left > 0;
}

/// <summary>
/// 選卡畫面上的一張卡。Name＝卡名；Text＝卡片上讀到的所有文字（卡名、副標、效果），判斷神祇等關鍵字用。
/// Rarity：0 普通、1 優良、2 稀有、3 史詩、4 傳說、5 神話（同 JS 的 RARITY 順序）。SkillBadge＝標籤是「技能」（新技能卡），這種卡的稀有度一律當 0。
/// </summary>
public sealed record CardInfo(int Index, Rectangle Bounds, string Name, string Text, int Rarity, bool SkillBadge, bool IsNew, IReadOnlyList<ActButton> Buttons)
{
    public ActButton? Button(ActKind kind) => Buttons.FirstOrDefault(b => b.Kind == kind);
}

/// <summary>
/// 一個選卡畫面：升級或寶箱、各張卡、下方的「重抽全部」和「自動」（沒有這顆按鈕時是 null）。
/// 「自動」是遊戲改版新加的開關：按下後這局剩下的選卡由遊戲自己選。它沒有次數圓圈，Left 固定 99。
/// Slots＝卡片上方的欄位數（升級卡「技能 4/4」、寶箱卡「道具 0/6」），讀不到時是 null。
/// </summary>
public sealed record PickerInfo(bool IsChest, IReadOnlyList<CardInfo> Cards, ActButton? RerollAll, ActButton? Auto = null, (int Used, int Max)? Slots = null)
{
    /// <summary>欄位滿了（技能都選完、六格道具都有了）；讀不到欄位數時是 null</summary>
    public bool? SlotsFull => Slots is { Max: > 0 } x ? x.Used >= x.Max : null;

    /// <summary>
    /// 技能神祇選擇畫面（遊戲 2026.11.23 版起，點道具欄紫色「!」打開）：「選擇你的神祇」4 張「X Deity」卡，數字鍵 1–4 選，沒有放逐／專注／重抽。
    /// 每局只選一次，之後神祇自己升級。選法見 <see cref="CardDecider.DecideDeity"/>
    /// </summary>
    public bool IsDeityPick { get; init; }
}

/// <summary>選卡的動作種類（同 JS decide() 回傳的 do）</summary>
public enum PickDo
{
    Pick,
    Banish,
    Focus,
    Reroll,
    /// <summary>按遊戲的「自動」，這局之後交給遊戲選</summary>
    GameAuto,
    /// <summary>按某一張卡的「重抽卡片」（單張重抽，只會換成同一類；目前只用在寶箱卡）</summary>
    RerollCard,
}

/// <summary>
/// 選卡決定。Index＝第幾張（0 起算；重抽全部時是 null）。Why＝理由（同 JS 的 why 文字，顯示在面板、寫進選卡紀錄）。
/// FocusKey＝專注時要記住的鍵：技能名稱，或「item:道具名」（同 JS 的 st.focused）。
/// </summary>
public sealed record PickDecision(PickDo Do, int? Index, string Why, string? FocusKey = null);

/// <summary>
/// 一局內的選卡狀態（同 JS 的 st.picked／st.focused／st.deityLock），每局開始時換一個新的。
/// Picked＝已經學過的「學到後放逐」技能；Focused＝已經專注過的技能或道具；DeityLock＝這局選定的神祇（空字串＝還沒選）。
/// Banished＝放逐過的清單項目（學到後放逐、直接放逐、一出現就放逐的字詞），判斷何時改按遊戲「自動」用（exe 新增，JS 沒有）。
/// GameAuto＝這局已經按過遊戲的「自動」。
/// Treasure＝這局累計的尋寶率（%，同 JS st.treasure）：VisionEngine 把滑鼠移到道具欄、讀說明框加總（<see cref="Field.TreasureReader"/>），還沒讀到時是 0。
/// </summary>
public sealed class RunPickState
{
    public HashSet<string> Picked { get; } = [];
    public HashSet<string> Banished { get; } = [];

    /// <summary>這局不得已學到的清單外技能（卡名去掉空白和「+N」）：之後再出現就放逐，不升級（使用者：只升級清單裡的技能，第四個技能不幸選到也直接放逐）</summary>
    public HashSet<string> Unwanted { get; } = [];

    /// <summary>
    /// 遊戲 2026.11.23 版起「自動」分成兩顆：升級卡畫面的「自動技能」、寶箱卡畫面的自動，各自要按一次（之前一顆就包含寶箱）。
    /// 分開記：按過「自動技能」後出現寶箱卡畫面，要馬上按寶箱的自動，不能當成已經交給遊戲而乾等（組隊時選卡有秒數限制）
    /// </summary>
    public bool GameAutoSkill { get; set; }

    /// <summary>這局已經按過寶箱卡畫面的「自動」</summary>
    public bool GameAutoChest { get; set; }

    /// <summary>有沒有按過任何一種「自動」；設定時兩種一起設（新的一局清掉）</summary>
    public bool GameAuto
    {
        get => GameAutoSkill || GameAutoChest;
        set => GameAutoSkill = GameAutoChest = value;
    }
    public HashSet<string> Focused { get; } = [];

    /// <summary>這局小幫手選過的寶箱卡（<see cref="CardDecider.SkillKey"/> 後的卡名）：再出現就是「已經有的道具的強化」，算要的卡（不會為它單張重抽）</summary>
    public HashSet<string> Owned { get; } = [];
    public string DeityLock { get; set; } = "";
    public double Treasure { get; set; }

    /// <summary>
    /// 這局是活動（不是遠征）。使用者（2026-11-25）：活動的話尋寶率沒有用，所以活動時不優先尋寶卡、有別的卡就不選，也不讀尋寶率。
    /// 不知道是哪一種（例如在野外中途開程式）時是 false，照遠征處理（舊行為）
    /// </summary>
    public bool IsEvent { get; set; }
}
