using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class PickerFlowTests
{
    private static ActButton Btn(ActKind k, int x) => new(k, new Rectangle(x, 400, 40, 30), true, 3);

    private static CardInfo Card(int i, string name, bool isNew = true)
        => new(i, new Rectangle(i * 240, 40, 214, 352), name, name, 0, false, isNew, [Btn(ActKind.Focus, i * 240), Btn(ActKind.Banish, i * 240 + 60)]);

    private static PickerInfo Upgrade(params CardInfo[] cards) => new(false, cards, new ActButton(ActKind.Reroll, new Rectangle(300, 470, 80, 30), true, 5));

    private static PickSettings Settings(SkillCfg skills, bool pickSkill = true, bool autoFocus = false)
        => new("巫師", true, skills, false, [], false, "none", true, [], [], autoFocus, 1, [], [], [], true, true, true, 2, "treasure",
            pickSkill, true, 350, false);

    private static readonly SkillCfg Mage = new(["雷爆術", "冰箭術"], ["雷爆術"], [], [], []);

    [Fact]
    public void Waiting_UntilPickDelay()
    {
        var flow = new PickerFlow();

        Assert.True(flow.Waiting(0, 350));
        Assert.True(flow.Waiting(300, 350));
        Assert.False(flow.Waiting(400, 350));
    }

    [Fact]
    public void Pick_PressesDigit_AndRemembersAfterSkill()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Mage), 400);

        Assert.Equal('2', a.Key);
        Assert.Null(a.Click);
        Assert.Equal("選升級卡：雷爆術（技能第 1 優先）", a.Status);
        Assert.Contains("雷爆術", flow.State.Picked);
    }

    [Fact]
    public void Banish_ClicksBanishButton()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "雷爆術", isNew: false), Card(1, "冰箭術")), Settings(Mage), 400);

        Assert.Equal(PickDo.Banish, a.Decision!.Do);
        Assert.Equal(new Point(80, 415), a.Click);
        Assert.Equal("放逐：雷爆術（放逐清單）", a.Status);
    }

    [Fact]
    public void Focus_ClicksFocusButton_AndRemembers()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "冰箭術")), Settings(Mage, autoFocus: true), 400);

        Assert.Equal(new Point(20, 415), a.Click);
        Assert.Contains("冰箭術", flow.State.Focused);
    }

    [Fact]
    public void AfterAction_WaitsForScreenToUpdate()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        flow.Act(Upgrade(Card(0, "雷爆術", isNew: false), Card(1, "冰箭術")), Settings(Mage), 400);

        Assert.True(flow.Waiting(900, 350));
        Assert.False(flow.Waiting(1200, 350));
    }

    [Fact]
    public void PickKeyIgnored_ClicksCardAfterTimeout()
    {
        var flow = new PickerFlow();
        var picker = Upgrade(Card(0, "火箭術"), Card(1, "雷爆術"));
        flow.Waiting(0, 350);
        flow.Act(picker, Settings(Mage), 400);

        Assert.True(flow.Waiting(1500, 350));
        Assert.False(flow.Waiting(2000, 350));
        var retry = flow.Act(picker, Settings(Mage), 2000);

        Assert.Null(retry.Key);
        Assert.Equal(new Point(240 + 107, 40 + 176), retry.Click);
        Assert.Equal("選卡按鍵沒反應，改點卡片：雷爆術", retry.Status);
    }

    [Fact]
    public void PickKeyIgnored_EmptyName_DoesNotClickCard()
    {
        var flow = new PickerFlow();
        var picker = Upgrade(Card(0, ""), Card(1, ""));
        flow.Waiting(0, 350);
        flow.Act(picker, Settings(Mage), 400);
        flow.Waiting(2000, 350);

        var retry = flow.Act(picker, Settings(Mage), 2000);

        Assert.Null(retry.Click);
        Assert.Equal('1', retry.Key);
    }

    [Fact]
    public void PickKeyIgnored_ButNextPickerAlreadyShown_DecidesAgain()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        flow.Act(Upgrade(Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Mage), 400);
        flow.Waiting(2000, 350);

        var next = flow.Act(Upgrade(Card(0, "冰箭術"), Card(1, "火焰之壁")), Settings(Mage), 2000);

        Assert.Equal('1', next.Key);
        Assert.Null(next.Click);
    }

    [Fact]
    public void PickSkillOff_WaitsForUser()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "雷爆術")), Settings(Mage, pickSkill: false), 400);

        Assert.Equal("等你選卡", a.Status);
        Assert.Null(a.Key);
        Assert.Null(a.Click);
    }

    [Fact]
    public void Deity_LocksOnFirstPick()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        var deity = new CardInfo(0, new Rectangle(0, 40, 214, 352), "思想之神 +1", "思想之神 +1 神祇", 0, false, true, []);

        flow.Act(new PickerInfo(true, [deity], null), Settings(SkillCfg.Empty), 400);

        Assert.Equal("思想之神", flow.State.DeityLock);
    }

    [Fact]
    public void NewRun_ClearsState_GoneResetsDelay()
    {
        var flow = new PickerFlow();
        flow.State.Picked.Add("雷爆術");
        flow.Waiting(0, 350);

        flow.NewRun();
        flow.Gone();

        Assert.Empty(flow.State.Picked);
        Assert.True(flow.Waiting(5000, 350));
    }
}
