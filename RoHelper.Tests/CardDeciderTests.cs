using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class CardDeciderTests
{
    private static ActButton Btn(ActKind k, bool enabled = true, int left = 3) => new(k, new Rectangle(0, 0, 10, 10), enabled, left);

    /// <summary>升級卡：預設有能用的專注和放逐（各 3 次），沒有重抽</summary>
    private static CardInfo Card(int i, string name, int rarity = 0, bool isNew = false, bool skillBadge = false, string? text = null, ActButton[]? buttons = null)
        => new(i, new Rectangle(i * 100, 0, 90, 150), name, text ?? name, rarity, skillBadge, isNew, buttons ?? [Btn(ActKind.Focus), Btn(ActKind.Banish)]);

    private static PickerInfo Picker(bool chest, params CardInfo[] cards) => new(chest, cards, Btn(ActKind.Reroll, left: 5));

    private static PickSettings Settings(SkillCfg? skills = null, bool autoFocus = false, int autoFocusN = 1, string pickOrder = "treasure",
        bool deityPrio = false, string deityOrder = "none", bool deityLock = false, string[]? deity = null, string[]? deityBan = null,
        string[]? itemPrio = null, string[]? itemBan = null, string[]? itemFocus = null, bool avoidNew = true, bool rerollAll = true,
        bool banishKeep = true, int banishKeepN = 2, bool banExtra = false, string[]? banExtraWords = null,
        bool treasureCap = false, int treasureCapN = 300, bool hpAfterCap = false, bool chestReroll = false)
        => new("巫師", true, skills ?? SkillCfg.Empty, banExtra, banExtraWords ?? [], deityPrio, deityOrder, deityLock, deity ?? [], deityBan ?? [],
            autoFocus, autoFocusN, itemFocus ?? [], itemPrio ?? [], itemBan ?? [], rerollAll, avoidNew, banishKeep, banishKeepN, pickOrder,
            true, true, 350, false, TreasureCap: treasureCap, TreasureCapN: treasureCapN, HpAfterCap: hpAfterCap, ChestReroll: chestReroll);

    private static RunPickState Treasure(double t) => new() { Treasure = t };

    private static SkillCfg Skills(string[]? order = null, string[]? after = null, string[]? ban = null, string[]? focus = null, string[]? skip = null)
        => new(order ?? [], after ?? [], ban ?? [], focus ?? [], skip ?? []);

    [Fact]
    public void FirstPrioritySkill_IsPicked()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Skills(order: ["雷爆術", "火箭術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "技能第 1 優先"), d);
    }

    /// <summary>實測：OCR 把「冰箭術」讀成「冰箾術」，要照技能優先選，不是當一般卡用最稀有選</summary>
    [Fact]
    public void OcrLookalike_StillMatchesSkill()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "智慧祝褔", rarity: 3), Card(1, "冰箾術", rarity: 0)),
            Settings(Skills(order: ["雷爆術", "冰箭術"])), new RunPickState());

        Assert.Equal(1, d.Index);
        Assert.Equal("技能優先（稀有度 > 順序）", d.Why);
    }

    [Fact]
    public void OcrLookalike_ItemPrio()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "傷害秘藥", rarity: 3), Card(1, "智慧祝褔 + 1", rarity: 0)),
            Settings(itemPrio: ["智慧祝福"]), new RunPickState());

        Assert.Equal(1, d.Index);
    }

    private static RunPickState LearnedFireWall()
    {
        var st = new RunPickState();
        st.Unwanted.Add(CardDecider.SkillKey("火焰之壁"));
        return st;
    }

    /// <summary>使用者：只升級清單裡的技能，第四個技能不幸學到了也直接放逐</summary>
    [Fact]
    public void LearnedUnwantedSkill_IsBanished()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "冰箭術"), Card(1, "火焰之壁 + 1", rarity: 3)),
            Settings(Skills(order: ["冰箭術"])), LearnedFireWall());

        Assert.Equal(new PickDecision(PickDo.Banish, 1, "學到的清單外技能"), d);
    }

    /// <summary>放逐清單還沒放逐完、放逐次數只剩保留的量：不放逐它，但也不升級它</summary>
    [Fact]
    public void LearnedUnwantedSkill_KeepsBanishesForBanList_ButNeverUpgrades()
    {
        var low = new[] { Btn(ActKind.Focus), Btn(ActKind.Banish, left: 2) };
        var d = CardDecider.Decide(Picker(false, Card(0, "火焰之壁", rarity: 3, buttons: low), Card(1, "冰箭術", buttons: low)),
            Settings(Skills(order: ["冰箭術"], after: ["雷爆術"]), banishKeepN: 2), LearnedFireWall());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "技能第 1 優先"), d);
    }

    [Fact]
    public void LearnedUnwantedSkill_BanListDone_UsesLastBanishes()
    {
        var low = new[] { Btn(ActKind.Focus), Btn(ActKind.Banish, left: 1) };
        var st = LearnedFireWall();
        st.Banished.Add("雷爆術");
        var d = CardDecider.Decide(Picker(false, Card(0, "火焰之壁", buttons: low), Card(1, "冰箭術", buttons: low)),
            Settings(Skills(order: ["冰箭術"], after: ["雷爆術"])), st);

        Assert.Equal(PickDo.Banish, d.Do);
        Assert.Equal(0, d.Index);
    }

    /// <summary>學到的清單外技能不算進放逐清單：沒放逐它也照樣可以按遊戲「自動」</summary>
    [Fact]
    public void LearnedUnwantedSkill_NotRequiredForGameAuto()
    {
        var st = LearnedFireWall();
        st.Banished.Add("雷爆術");

        Assert.True(CardDecider.ReadyForGameAuto(Settings(Skills(after: ["雷爆術"])) with { AutoAfterBan = true }, st));
    }

    [Fact]
    public void OtherPrioritySkills_RarityThenOrder()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "冰箭術", rarity: 0), Card(1, "火箭術", rarity: 1)),
            Settings(Skills(order: ["雷爆術", "冰箭術", "火箭術"])), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
        Assert.Equal(1, d.Index);
        Assert.Equal("技能優先（稀有度 > 順序）", d.Why);
    }

    [Fact]
    public void EmptyListEntries_AreIgnored()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術")), Settings(Skills(ban: [""], after: [""])), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
        Assert.Equal(0, d.Index);
    }

    [Fact]
    public void BanList_IsBanishedFirst()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "雷爆術"), Card(1, "火箭術")), Settings(Skills(order: ["雷爆術"], ban: ["火箭術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 1, "放逐清單"), d);
    }

    [Fact]
    public void AfterSkill_NotNew_IsBanished_ButNewIsPicked()
    {
        var learned = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: false)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), new RunPickState());
        var fresh = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: true)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), new RunPickState());

        Assert.Equal(PickDo.Banish, learned.Do);
        Assert.Equal(PickDo.Pick, fresh.Do);
    }

    [Fact]
    public void AfterSkill_PickedThisRun_IsBanishedEvenIfNew()
    {
        var st = new RunPickState();
        st.Picked.Add("雷爆術");

        var d = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: true)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), st);

        Assert.Equal(PickDo.Banish, d.Do);
    }

    [Fact]
    public void BanExtraWords_AreBanished()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "攻擊：屬性附加"), Card(1, "火箭術")),
            Settings(Skills(order: ["火箭術"]), banExtra: true, banExtraWords: ["屬性附加"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 0, "放逐清單"), d);
    }

    /// <summary>
    /// 只專注技能頁設成「優先＋專注」的（使用者 2026-11-25：沒設任何專注時小幫手卻去專注第 1 優先的火箭術）。
    /// 每個技能只專注一次
    /// </summary>
    [Fact]
    public void AutoFocus_OnlyMarkedSkills_Once()
    {
        var picker = Picker(false, Card(0, "冰箭術"), Card(1, "火箭術"));
        var settings = Settings(Skills(order: ["火箭術", "冰箭術"], focus: ["冰箭術"]), autoFocus: true);
        var st = new RunPickState();

        var first = CardDecider.Decide(picker, settings, st);
        st.Focused.Add(first.FocusKey!);
        var second = CardDecider.Decide(picker, settings, st);

        Assert.Equal(new PickDecision(PickDo.Focus, 0, "專注：冰箭術", "冰箭術"), first);
        Assert.Equal(PickDo.Pick, second.Do);
    }

    /// <summary>技能頁沒有任何「優先＋專注」：不專注（不再退回專注前 N 個優先技能）</summary>
    [Fact]
    public void AutoFocus_NothingMarked_DoesNotFocus()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術")), Settings(Skills(order: ["火箭術", "冰箭術"]), autoFocus: true, autoFocusN: 2), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void AutoFocus_SkippedWhenFocusButtonUnusable()
    {
        var card = Card(0, "冰箭術", buttons: [Btn(ActKind.Focus, enabled: false, left: 0), Btn(ActKind.Banish)]);

        var d = CardDecider.Decide(Picker(false, card), Settings(Skills(order: ["冰箭術"], focus: ["冰箭術"]), autoFocus: true), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ItemFocus_WorksOnChestToo()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "傷害祝福"), Card(1, "傷害 秘藥")), Settings(autoFocus: true, itemFocus: ["傷害秘藥"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Focus, 1, "道具專注：傷害秘藥", "item:傷害秘藥"), d);
    }

    [Fact]
    public void Chest_DoesNotBanishOrUseSkillPriority()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "雷爆術"), Card(1, "傷害秘藥", rarity: 2)), Settings(Skills(order: ["雷爆術"], ban: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void UnwantedNewSkill_Avoided_WhenOtherCardExists()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "聖靈召喚", isNew: true, skillBadge: true), Card(1, "傷害祝福")),
            Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void OnlyUnwantedNewSkills_RerollAll()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "聖靈召喚", isNew: true, skillBadge: true), Card(1, "火焰之壁", isNew: true, skillBadge: true)),
            Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "只有不要的新技能"), d);
    }

    [Fact]
    public void OnlyUnwantedNewSkills_NoReroll_BanishKeepsTwo()
    {
        var noReroll = new ActButton(ActKind.Reroll, Rectangle.Empty, false, 0);
        var few = Card(0, "聖靈召喚", isNew: true, skillBadge: true, buttons: [Btn(ActKind.Banish, left: 2)]);
        var many = Card(1, "火焰之壁", isNew: true, skillBadge: true, buttons: [Btn(ActKind.Banish, left: 3)]);

        var d = CardDecider.Decide(new PickerInfo(false, [few, many], noReroll), Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 1, "不要的新技能（重抽用完）"), d);
    }

    [Fact]
    public void AllForcedSkip_RerollAll()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "回復藥水"), Card(1, "冷卻藥水")), Settings(itemBan: ["回復藥水", "冷卻藥水"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "全部都是強制不選的卡"), d);
    }

    [Fact]
    public void FavItem_PickedRarestAmongFavs()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "傷害祝福", rarity: 1), Card(1, "傷害秘藥", rarity: 2), Card(2, "堡壘之神", rarity: 0, text: "堡壘之神 神祇")),
            Settings(itemPrio: ["傷害祝福", "傷害秘藥"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "勾選的道具"), d);
    }

    [Fact]
    public void PickOrder_FirstAndRarity()
    {
        var picker = Picker(true, Card(0, "回復藥水", rarity: 0), Card(1, "傷害秘藥", rarity: 2), Card(2, "傷害祝福", rarity: 2));

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "第一張"), CardDecider.Decide(picker, Settings(pickOrder: "first"), new RunPickState()));
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "稀有度優先"), CardDecider.Decide(picker, Settings(pickOrder: "rarity"), new RunPickState()));
    }

    /// <summary>遊戲 2026.11.23 版：「疾風之神」改名「疾風神器」；設定裡還是舊名字，也要選得到、也要擋得掉</summary>
    [Fact]
    public void Artifact_MatchesOldDeityNames()
    {
        var picker = Picker(true, Card(0, "豐饒祝福", rarity: 2), Card(1, "疾風神器", text: "疾風神器 神器 +10% 速度"));

        var d = CardDecider.Decide(picker, Settings(deityPrio: true, deityOrder: "first", deity: ["疾風之神"]), new RunPickState());
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "神祇優先：疾風之神"), d);

        var banned = CardDecider.Decide(picker, Settings(deityBan: ["疾風之神"]), new RunPickState());
        Assert.Equal(0, banned.Index);
    }

    [Fact]
    public void DeityPriority_FirstMode_PicksDeity()
    {
        var picker = Picker(true, Card(0, "傷害秘藥", rarity: 2), Card(1, "思想之神", text: "思想之神 神祇"));

        var d = CardDecider.Decide(picker, Settings(deityPrio: true, deityOrder: "first", deity: ["思想之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "神祇優先：思想之神"), d);
    }

    [Fact]
    public void DeityPriority_RarityMode_YieldsToRarerCard()
    {
        var picker = Picker(true, Card(0, "傷害秘藥", rarity: 2), Card(1, "思想之神", rarity: 0, text: "思想之神 神祇"));

        var d = CardDecider.Decide(picker, Settings(deityPrio: true, deityOrder: "rarity", deity: ["思想之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "最稀有"), d);
    }

    [Fact]
    public void DeityLock_RefinesSameDeity_AndIgnoresOthers()
    {
        var st = new RunPickState { DeityLock = "思想之神" };
        var picker = Picker(true, Card(0, "堡壘之神", rarity: 3, text: "堡壘之神 神祇"), Card(1, "思想之神 +2", text: "思想之神 +2 神祇"), Card(2, "回復藥水"));

        var refine = CardDecider.Decide(picker, Settings(deityLock: true, deityOrder: "first"), st);
        var otherOnly = CardDecider.Decide(Picker(true, Card(0, "堡壘之神", rarity: 3, text: "堡壘之神 神祇"), Card(1, "回復藥水")), Settings(deityLock: true), st);

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "神祇精煉：思想之神"), refine);
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), otherOnly);
    }

    [Fact]
    public void BannedDeity_NotPicked()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "貪婪之神", rarity: 3, text: "貪婪之神 神祇"), Card(1, "回復藥水")), Settings(deityBan: ["貪婪之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void Helpers_DeityDetectionAndName()
    {
        Assert.True(CardDecider.IsDeity(Card(0, "堡壘之神")));
        Assert.True(CardDecider.IsDeity(Card(0, "雷爆術 Deity")));
        Assert.True(CardDecider.IsDeity(Card(0, "某某", text: "某某 神祇")));
        Assert.False(CardDecider.IsDeity(Card(0, "傷害祝福")));
        Assert.Equal("思想之神", CardDecider.DeityName("思想之神 +3"));
    }

    [Fact]
    public void TreasureOrder_PicksRarestTreasureCard()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 3), Card(1, "幸運符", rarity: 1, text: "幸運符 +5% 尋寶率"), Card(2, "金幣袋", rarity: 2, text: "金幣袋 +8% 尋寶率"));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "尋寶率優先"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    [Fact]
    public void TreasureOrder_NoTreasureCard_Rarest()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, "防禦力", rarity: 2));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    [Theory]
    [InlineData("尋寶藥水")]
    [InlineData("驚奇秘藥 +2")]
    [InlineData("豐饒 祝福")]
    public void KnownTreasureItem_CountsEvenIfTextMisread(string name)
    {
        // 卡片文字的「尋寶」讀錯了（「尋賨」），靠卡名認
        var picker = Picker(true, Card(0, "攻擊力", rarity: 3), Card(1, name, rarity: 0, text: name + " +5% 尋賨率"));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "尋寶率優先"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    [Fact]
    public void Capped_SkipsTreasureCards_RarestOfRest()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, "尋寶藥水", rarity: 4, text: "尋寶藥水 +10% 尋寶率"), Card(2, "防禦力", rarity: 2));

        var d = CardDecider.Decide(picker, Settings(treasureCap: true, treasureCapN: 300), Treasure(300));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "最稀有"), d);
    }

    [Fact]
    public void BelowCap_OrCapOff_TreasureStillPreferred()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 3), Card(1, "尋寶藥水", rarity: 0, text: "尋寶藥水 +10% 尋寶率"));

        Assert.Equal(1, CardDecider.Decide(picker, Settings(treasureCap: true, treasureCapN: 300), Treasure(299.5)).Index);
        Assert.Equal(1, CardDecider.Decide(picker, Settings(treasureCap: false), Treasure(1000)).Index);
    }

    /// <summary>法師系不點閃避類（迴避秘藥）：有別的卡時跳過，即使比較稀有</summary>
    [Theory]
    [InlineData("迴避秘藥")]
    public void Mage_SkipsDodgeItems(string dodge)
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, dodge, rarity: 4), Card(2, "防禦力", rarity: 2));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "最稀有"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    /// <summary>AGI 料理不算閃避類（AGI 主要加冷卻縮減）：法師照常可以選</summary>
    [Fact]
    public void Mage_AgiDishStillAllowed()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, "AGI 料理", rarity: 4));

        Assert.Equal(1, CardDecider.Decide(picker, Settings(), new RunPickState()).Index);
    }

    [Fact]
    public void NonMage_DodgeItemsStillAllowed()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, "迴避秘藥", rarity: 4));

        Assert.Equal(1, CardDecider.Decide(picker, Settings() with { Job = "騎士" }, new RunPickState()).Index);
    }

    [Fact]
    public void Mage_OnlyDodgeItems_PicksOneWithoutReroll()
    {
        var picker = Picker(true, Card(0, "迴避秘藥", rarity: 1), Card(1, "迴避秘藥", rarity: 3));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    /// <summary>活動的尋寶率沒用：有別的卡就不選尋寶卡（即使尋寶卡比較稀有）</summary>
    [Fact]
    public void Event_SkipsTreasureCards_RarestOfRest()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 1), Card(1, "尋寶藥水", rarity: 4, text: "尋寶藥水 +10% 尋寶率"), Card(2, "防禦力", rarity: 2));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "最稀有"), CardDecider.Decide(picker, Settings(), new RunPickState { IsEvent = true }));
        // 遠征照舊優先尋寶
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "尋寶率優先"), CardDecider.Decide(picker, Settings(), new RunPickState()));
    }

    [Fact]
    public void Event_RarityOrder_NoTreasureTieBreak()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 2), Card(1, "豐饒祝福", rarity: 2, text: "+5% 尋寶率"));

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "稀有度優先"), CardDecider.Decide(picker, Settings(pickOrder: "rarity"), new RunPickState { IsEvent = true }));
    }

    [Fact]
    public void Event_OnlyTreasureCards_StillPicksOne()
    {
        var picker = Picker(true, Card(0, "尋寶藥水", rarity: 0, text: "+10% 尋寶率"), Card(1, "豐饒祝福", rarity: 2, text: "+5% 尋寶率"));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), CardDecider.Decide(picker, Settings(), new RunPickState { IsEvent = true }));
    }

    [Fact]
    public void Capped_OnlyTreasureCards_StillPicksOne()
    {
        var picker = Picker(true, Card(0, "尋寶藥水", rarity: 0, text: "+10% 尋寶率"), Card(1, "豐饒祝福", rarity: 2, text: "+5% 尋寶率"));

        var d = CardDecider.Decide(picker, Settings(treasureCap: true), Treasure(500));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Theory]
    [InlineData("最大生命 +10%")]
    [InlineData("HP 回復")]
    [InlineData("治癒之光")]
    [InlineData("血量上限")]
    [InlineData("恢復速度")]
    public void Capped_HpAfterCap_PicksRarestHpCard(string hpText)
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 4), Card(1, "生存", rarity: 0, text: hpText), Card(2, "生存2", rarity: 1, text: hpText),
            Card(3, "尋寶藥水", rarity: 5, text: "+10% 尋寶率"));

        var d = CardDecider.Decide(picker, Settings(treasureCap: true, hpAfterCap: true), Treasure(300));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "尋寶已滿→生命"), d);
    }

    [Fact]
    public void HpAfterCap_NotCapped_NoHpPreference()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 4), Card(1, "生存", rarity: 0, text: "最大生命 +10%"));

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "最稀有"), CardDecider.Decide(picker, Settings(treasureCap: true, hpAfterCap: true), Treasure(10)));
    }

    [Fact]
    public void Capped_FavItemStillBeforeHp()
    {
        var picker = Picker(true, Card(0, "金剛石", rarity: 0), Card(1, "生存", rarity: 3, text: "最大生命 +10%"));

        var d = CardDecider.Decide(picker, Settings(itemPrio: ["金剛石"], treasureCap: true, hpAfterCap: true), Treasure(300));

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "勾選的道具"), d);
    }

    [Fact]
    public void RarityOrder_SameRarity_TreasureFirst_UnlessCapped()
    {
        var picker = Picker(true, Card(0, "攻擊力", rarity: 2), Card(1, "金幣袋", rarity: 2, text: "+5% 尋寶率"), Card(2, "防禦力", rarity: 1));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "稀有度優先"), CardDecider.Decide(picker, Settings(pickOrder: "rarity"), new RunPickState()));
        Assert.Equal(new PickDecision(PickDo.Pick, 0, "稀有度優先"), CardDecider.Decide(picker, Settings(pickOrder: "rarity", treasureCap: true), Treasure(300)));
    }

    [Fact]
    public void FirstOrder_Capped_SkipsTreasureCard()
    {
        var picker = Picker(true, Card(0, "金幣袋", text: "+5% 尋寶率"), Card(1, "攻擊力"));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "第一張"), CardDecider.Decide(picker, Settings(pickOrder: "first", treasureCap: true), Treasure(300)));
    }

    private static PickerInfo DeityPick(params string[] names)
        => new(false, names.Select((n, i) => Card(i, n, buttons: [])).ToArray(), null) { IsDeityPick = true };

    [Fact]
    public void Deity_FollowsDeityList_IgnoringOcrSuffix()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 De", "火箾術 De"), Settings(Skills(order: ["冰箭術"]), deity: ["火箭術", "雷擊術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "技能神祇優先：火箭術"), d);
    }

    [Fact]
    public void Deity_BannedSkipped_ThenSkillOrder()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 Deity", "火焰之壁 De"),
            Settings(Skills(order: ["火焰之壁", "雷擊術"]), deity: ["冰箭術"], deityBan: ["冰箭術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "技能優先：火焰之壁"), d);
    }

    [Fact]
    public void Deity_NothingMatches_FirstNotBanned()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 De"), Settings(deityBan: ["冰箭術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "第一張"), d);
    }

    // ── 寶箱卡單張重抽 ──

    private static readonly List<KeyValuePair<string, List<string>>> Groups =
    [
        new("藥水", ["回復藥水", "冷卻藥水", "尋寶藥水"]),
        new("秘藥", ["傷害秘藥", "驚奇秘藥"]),
        new("祝福", ["傷害祝福", "智慧祝福"]),
    ];

    /// <summary>寶箱卡：有單張重抽（5 次）、專注、放逐</summary>
    private static CardInfo Chest(int i, string name, bool reroll = true, string? text = null)
        => Card(i, name, text: text, buttons: reroll ? [Btn(ActKind.Reroll), Btn(ActKind.Focus), Btn(ActKind.Banish)] : [Btn(ActKind.Focus), Btn(ActKind.Banish)]);

    /// <summary>尋寶率已經超過上限：尋寶卡不算要的</summary>
    private static PickSettings Capped(string[]? itemPrio = null)
        => Settings(itemPrio: itemPrio, treasureCap: true, treasureCapN: 100, chestReroll: true);

    [Fact]
    public void ChestReroll_RerollsCardWhoseCategoryHasWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水"), Chest(2, "傷害祝福")),
            Capped(itemPrio: ["冷卻藥水"]), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.RerollCard, d.Do);
        Assert.Equal(1, d.Index);
    }

    [Fact]
    public void ChestReroll_SeveralHopeful_UsesRandom()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水"), Chest(2, "傷害祝福")),
            Capped(itemPrio: ["冷卻藥水", "智慧祝福"]), Treasure(200), Groups, n => n - 1);

        Assert.Equal(PickDo.RerollCard, d.Do);
        Assert.Equal(2, d.Index);
    }

    [Fact]
    public void ChestReroll_NoHopeful_RandomAmongAll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")),
            Capped(), Treasure(200), Groups, n => n - 1);

        Assert.Equal(new PickDecision(PickDo.RerollCard, 1, "寶箱都不是要的：單張重抽"), d);
    }

    [Fact]
    public void ChestReroll_NoCardReroll_UsesRerollAll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥", reroll: false), Chest(1, "回復藥水", reroll: false)),
            Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "寶箱都不是要的：重抽全部"), d);
    }

    [Fact]
    public void ChestReroll_NothingLeft_PicksByRules()
    {
        var p = new PickerInfo(true, [Chest(0, "傷害秘藥", reroll: false), Chest(1, "回復藥水", reroll: false)], Btn(ActKind.Reroll, enabled: false, left: 0));

        var d = CardDecider.Decide(p, Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_FavItemPresent_Picks()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "冷卻藥水")),
            Capped(itemPrio: ["冷卻藥水"]), Treasure(200), Groups, _ => 0);

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "勾選的道具"), d);
    }

    [Fact]
    public void ChestReroll_TreasureCardBelowCap_IsWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "回復藥水"), Chest(1, "尋寶藥水")),
            Settings(treasureCap: true, treasureCapN: 300, chestReroll: true), Treasure(100), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
        Assert.Equal(1, d.Index);
    }

    [Fact]
    public void ChestReroll_TreasureCardAboveCap_NotWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "回復藥水"), Chest(1, "尋寶藥水")),
            Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.RerollCard, d.Do);
    }

    [Fact]
    public void ChestReroll_OwnedItem_IsWanted()
    {
        var st = Treasure(200);
        st.Owned.Add("回復藥水");

        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水 + 1")), Capped(), st, Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    /// <summary>deityOrder「none」＝神祇步驟不直接選（交給後面），這樣才會走到寶箱重抽的「要的」判斷：優先神器在場就不重抽</summary>
    [Fact]
    public void ChestReroll_PriorityArtifact_IsWanted()
    {
        var s = Settings(deityPrio: true, deityOrder: "none", deity: ["戰爭之神"], treasureCap: true, treasureCapN: 100, chestReroll: true);

        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "戰爭神器", text: "戰爭神器 神器")), s, Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_UpgradeCards_NeverRerollCard()
    {
        var d = CardDecider.Decide(Picker(false, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")), Capped(), Treasure(200), Groups, _ => 0);

        Assert.NotEqual(PickDo.RerollCard, d.Do);
    }

    [Fact]
    public void ChestReroll_Disabled_NoReroll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")),
            Settings(treasureCap: true, treasureCapN: 100), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_NoBudget_PicksByRules()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")), Capped(), Treasure(200), Groups, _ => 0, rerollBudget: 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    // ── 道具排名（野外／活動分開，使用者 2026-11-25）──

    private static PickSettings Ranked()
        => Settings() with
        {
            RankExpe = [["尋寶藥水", "貪婪藥水", "傷害藥水"], ["驚奇秘藥", "傷害秘藥", "詠唱秘藥"], ["貪婪神器", "智慧神器", "思想神器"]],
            RankEvent = [["傷害藥水", "詠唱藥水", "護甲藥水"], ["傷害秘藥", "詠唱秘藥", "生命秘藥"], ["智慧神器", "思想神器", "戰爭神器"]],
        };

    [Fact]
    public void Rank_ForRun_OrdersFirstsThenSeconds()
    {
        var s = Ranked().ForRun(isEvent: false);

        Assert.True(s.ItemPrioRanked);
        Assert.Equal(["尋寶藥水", "驚奇秘藥", "貪婪神器", "貪婪藥水", "傷害秘藥", "智慧神器", "傷害藥水", "詠唱秘藥", "思想神器"], s.ItemPrio);
    }

    [Fact]
    public void Rank_NotSet_KeepsItemPrio()
    {
        var s = Settings(itemPrio: ["傷害祝福"]).ForRun(isEvent: true);

        Assert.False(s.ItemPrioRanked);
        Assert.Equal(["傷害祝福"], s.ItemPrio);
    }

    /// <summary>不同類：第 1 名先於第 2 名，就算第 2 名比較稀有</summary>
    [Fact]
    public void Rank_Event_FirstBeatsRarerSecond()
    {
        var p = Picker(true, Card(0, "詠唱秘藥", rarity: 3), Card(1, "傷害藥水", rarity: 0), Card(2, "回復祝福", rarity: 4));

        var d = CardDecider.Decide(p, Ranked().ForRun(isEvent: true), new RunPickState { IsEvent = true });

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "道具排名：傷害藥水"), d);
    }

    /// <summary>活動排第 1 的智慧神器帶尋寶率：活動會濾掉尋寶卡，但排名裡的照選</summary>
    [Fact]
    public void Rank_Event_RankedTreasureArtifactNotFiltered()
    {
        var p = Picker(true, Card(0, "生命秘藥", rarity: 2), Card(1, "智慧神器", text: "神器 +20% 經驗值 +4% 尋寶率"));

        var d = CardDecider.Decide(p, Ranked().ForRun(isEvent: true), new RunPickState { IsEvent = true });

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "道具排名：智慧神器"), d);
    }

    /// <summary>野外尋寶率已達上限：排第 1 的尋寶藥水也不選，改選排名下一個</summary>
    [Fact]
    public void Rank_Capped_SkipsRankedTreasure()
    {
        var p = Picker(true, Card(0, "尋寶藥水", text: "+10% 尋寶率"), Card(1, "傷害秘藥"));
        var s = (Ranked() with { TreasureCap = true, TreasureCapN = 300 }).ForRun(false);

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "道具排名：傷害秘藥"), CardDecider.Decide(p, s, Treasure(300)));
    }

    /// <summary>野外和活動用不同排名：同樣三張卡，野外選尋寶藥水、活動選傷害秘藥</summary>
    [Fact]
    public void Rank_ExpeAndEvent_Differ()
    {
        var p = Picker(true, Card(0, "尋寶藥水", text: "+10% 尋寶率"), Card(1, "傷害秘藥"), Card(2, "STR 料理"));

        Assert.Equal(0, CardDecider.Decide(p, Ranked().ForRun(false), new RunPickState()).Index);
        Assert.Equal(1, CardDecider.Decide(p, Ranked().ForRun(true), new RunPickState { IsEvent = true }).Index);
    }
}
