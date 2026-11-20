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
        bool treasureCap = false, int treasureCapN = 300, bool hpAfterCap = false)
        => new("巫師", true, skills ?? SkillCfg.Empty, banExtra, banExtraWords ?? [], deityPrio, deityOrder, deityLock, deity ?? [], deityBan ?? [],
            autoFocus, autoFocusN, itemFocus ?? [], itemPrio ?? [], itemBan ?? [], rerollAll, avoidNew, banishKeep, banishKeepN, pickOrder,
            true, true, 350, false, TreasureCap: treasureCap, TreasureCapN: treasureCapN, HpAfterCap: hpAfterCap);

    private static RunPickState Treasure(double t) => new() { Treasure = t };

    private static SkillCfg Skills(string[]? order = null, string[]? after = null, string[]? ban = null, string[]? focus = null, string[]? skip = null)
        => new(order ?? [], after ?? [], ban ?? [], focus ?? [], skip ?? []);

    [Fact]
    public void FirstPrioritySkill_IsPicked()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Skills(order: ["雷爆術", "火箭術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "技能第 1 優先"), d);
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

    [Fact]
    public void AutoFocus_FirstNonAfterPriority_Once()
    {
        // 雷爆術是「學到後放逐」：要標成新卡，否則會先被放逐
        var picker = Picker(false, Card(0, "冰箭術"), Card(1, "雷爆術", isNew: true));
        var settings = Settings(Skills(order: ["雷爆術", "冰箭術"], after: ["雷爆術"]), autoFocus: true, autoFocusN: 1);
        var st = new RunPickState();

        var first = CardDecider.Decide(picker, settings, st);
        st.Focused.Add(first.FocusKey!);
        var second = CardDecider.Decide(picker, settings, st);

        Assert.Equal(new PickDecision(PickDo.Focus, 0, "專注：冰箭術", "冰箭術"), first);
        Assert.Equal(PickDo.Pick, second.Do);
    }

    [Fact]
    public void AutoFocus_SkippedWhenFocusButtonUnusable()
    {
        var card = Card(0, "冰箭術", buttons: [Btn(ActKind.Focus, enabled: false, left: 0), Btn(ActKind.Banish)]);

        var d = CardDecider.Decide(Picker(false, card), Settings(Skills(order: ["冰箭術"]), autoFocus: true), new RunPickState());

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
}
