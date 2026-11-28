using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>組隊（遊戲 2026.11.23 版）：多人大廳、隊長「向隊伍提議」、隊員「準備好了！」、「前往頻道」</summary>
public class PartyFlowTests
{
    private static readonly Rectangle Btn = new(400, 300, 100, 40);
    private static readonly Point BtnCenter = new(450, 320);
    private static readonly Rectangle Row = new(700, 400, 80, 20);

    private static FlowSettings Cfg(string role, bool runLimit = false, int runs = 0)
        => new(false, SoloLobby: true, AutoEvent: false, AutoExpe: true, AutoReturn: true, runLimit, 10, runs, TStart: 800, TReturn: 1500, TLobby: 700,
            ExpeDiff: "keep", PartyRole: role, PartyLobby: "約頓海姆");

    private static ScreenState Screen() => new(new Size(1000, 600), 1.0);

    [Fact]
    public void Party_PicksNamedLobby_NotSolo()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Solo = Btn, PartyLobby = Row };

        Assert.Null(flow.Decide(s, Cfg("member"), 0).Click);
        var d = flow.Decide(s, Cfg("member"), 600);
        Assert.Equal(new Point(740, 410), d.Click);
        Assert.Equal("選多人大廳：約頓海姆", d.Action);
    }

    [Fact]
    public void Party_LobbyNotFound_DoesNotFallBackToSolo()
    {
        var d = new LobbyFlow().Decide(Screen() with { Solo = Btn }, Cfg("leader"), 600);

        Assert.Null(d.Click);
        Assert.StartsWith("找不到多人大廳", d.Action);
    }

    [Fact]
    public void Solo_StillPicksSolo()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Solo = Btn, PartyLobby = Row };
        flow.Decide(s, Cfg("off"), 0);

        Assert.Equal(BtnCenter, flow.Decide(s, Cfg("off"), 600).Click);
    }

    [Fact]
    public void Member_ReadyAwaitsField_NotImmediateRun()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { PartyReady = Btn };

        Assert.Null(flow.Decide(s, Cfg("member"), 0).Click);
        var d = flow.Decide(s, Cfg("member"), 600);
        Assert.Equal(BtnCenter, d.Click);
        Assert.False(d.StartsRun);
        Assert.True(d.AwaitsField);
        Assert.True(flow.AwaitingField(1000));
        Assert.False(flow.AwaitingField(600 + LobbyFlow.AwaitField + 1));
    }

    /// <summary>實測 2026-11-23：隊長提議後還在大廳等了 8 秒，又去找遠征隊長；等待中不能再走向 NPC</summary>
    [Fact]
    public void Leader_AfterProposal_DoesNotTalkAgain()
    {
        var flow = new LobbyFlow();
        var diff = Screen() with { DiffTitle = new Rectangle(100, 100, 50, 20), DiffGo = Btn, DiffGoIsProposal = true };
        flow.Decide(diff, Cfg("leader"), 0);
        flow.Decide(diff, Cfg("leader"), 900);

        var d = flow.Decide(Screen() with { LeaderPrompt = Btn, ExpeLeader = Btn }, Cfg("leader"), 5000);
        Assert.Null(d.Click);
        Assert.Equal("組隊：等隊員準備好", d.Action);
        flow.FieldEntered();
        Assert.NotNull(flow.Decide(Screen() with { LeaderPrompt = Btn, ExpeLeader = Btn }, Cfg("leader"), 6000).Click);
    }

    [Fact]
    public void Member_RunLimit_DoesNotAccept()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { PartyReady = Btn };
        flow.Decide(s, Cfg("member", runLimit: true, runs: 10), 0);

        Assert.Null(flow.Decide(s, Cfg("member", runLimit: true, runs: 10), 600).Click);
    }

    [Fact]
    public void Member_DoesNotWalkToExpeditionLeader()
    {
        var d = new LobbyFlow().Decide(Screen() with { ExpeLeader = Btn, LeaderPrompt = Btn }, Cfg("member"), 0);

        Assert.Null(d.Click);
        Assert.Equal("組隊：等隊長提議出發", d.Action);
    }

    /// <summary>組隊結算的戰利品分配：先按「其餘全 Greed」，再按旁邊的「準備好了」，之後等分配完成（不按返回角色）</summary>
    [Fact]
    public void Loot_GreedThenReady()
    {
        var flow = new LobbyFlow();
        var greed = new Rectangle(958, 546, 112, 32);
        var ready = new Rectangle(1084, 546, 86, 32);
        var s = Screen() with { LootGreed = greed, LootReady = ready, Return = Btn };

        var d1 = flow.Decide(s, Cfg("member"), 0);
        Assert.Equal("戰利品：其餘全 Greed", d1.Action);
        Assert.Equal(new Point(1014, 562), d1.Click);
        Assert.Null(flow.Decide(s, Cfg("member"), 300).Click);
        var d2 = flow.Decide(s, Cfg("member"), 900);
        Assert.Equal("戰利品：準備好了", d2.Action);
        var d3 = flow.Decide(s, Cfg("member"), 1500);
        Assert.Null(d3.Click);
        Assert.False(d3.CountRun);

        // 分配完：回到一般的結算流程
        flow.Decide(Screen() with { Return = Btn }, Cfg("member"), 2000);
        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg("member"), 2100).CountRun);
    }

    /// <summary>實測 2026-11-24：分配完成後清單的「Greed」字被當成按鈕，一直卡在「等分配完成」；最多等 25 秒就照常結算</summary>
    [Fact]
    public void Loot_GivesUpAfterTimeout()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { LootGreed = new Rectangle(958, 546, 112, 32), Return = Btn };
        flow.Decide(s, Cfg("member"), 0);
        Assert.Equal("戰利品：等分配完成", flow.Decide(s, Cfg("member"), 10_000).Action);

        flow.Decide(s, Cfg("member"), 26_000);
        Assert.True(flow.Decide(s, Cfg("member"), 26_100).CountRun);
    }

    [Fact]
    public void Maintenance_RetryEveryMinute()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { MaintenanceRetry = Btn };

        Assert.Equal(BtnCenter, flow.Decide(s, Cfg("member"), 0).Click);
        Assert.Null(flow.Decide(s, Cfg("member"), 30_000).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg("member"), 60_000).Click);
    }

    [Fact]
    public void Member_IdleInLobby_StepsToAvoidAfk()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { ExpeLeader = Btn };

        Assert.Null(flow.Decide(s, Cfg("member"), 0).Click);
        Assert.Null(flow.Decide(s, Cfg("member"), 100_000).Click);
        var d = flow.Decide(s, Cfg("member"), 180_000);
        Assert.NotNull(d.Click);
        Assert.Null(flow.Decide(s, Cfg("member"), 181_000).Click);
    }

    [Fact]
    public void Member_StillReturnsFromResult()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Return = Btn };
        flow.Decide(s, Cfg("member"), 0);
        var d = flow.Decide(s, Cfg("member"), 100);

        Assert.True(d.CountRun);
    }

    [Fact]
    public void Party_JoinsLeadersChannel()
    {
        var d = new LobbyFlow().Decide(Screen() with { PartyChannel = Btn }, Cfg("member"), 0);

        Assert.Equal(BtnCenter, d.Click);
    }

    [Fact]
    public void ChannelButton_IgnoredByLeaderAndOnDifficultyDialog()
    {
        Assert.Null(new LobbyFlow().Decide(Screen() with { PartyChannel = Btn }, Cfg("leader"), 0).Click);
        var d = new LobbyFlow().Decide(Screen() with { PartyChannel = Btn, DiffTitle = new Rectangle(100, 100, 50, 20) }, Cfg("member"), 0);
        Assert.NotEqual("組隊：前往隊長的頻道", d.Action);
    }

    [Fact]
    public void Leader_ProposalButton_AwaitsField()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { DiffTitle = new Rectangle(100, 100, 50, 20), DiffGo = Btn, DiffGoIsProposal = true };
        flow.Decide(s, Cfg("leader"), 0);
        var d = flow.Decide(s, Cfg("leader"), 900);

        Assert.Equal("④ 向隊伍提議出發", d.Action);
        Assert.Equal(BtnCenter, d.Click);
        Assert.False(d.StartsRun);
        Assert.True(d.AwaitsField);
    }

    [Theory]
    [InlineData("鮭魚生魚片 ▾ 頻道 2", "鮭魚生魚片", true)]
    [InlineData("鮭 魚 生 魚 片", "鮭魚生魚片", true)]
    [InlineData("鮭魚生角片", "鮭魚生魚片", true)]
    [InlineData("挪威鮭魚 頻道 2 組隊中", "鮭魚生魚片", false)]
    [InlineData("紐西蘭鮭魚 隊長", "挪威鮭魚", false)]
    public void NameLike_ToleratesOneMisread_NotSharedSuffix(string text, string name, bool like)
        => Assert.Equal(like, PartyUi.NameLike(text, name));

    [Theory]
    [InlineData("紐西蘭鮭魚 邀請你加入隊伍。", "紐西蘭鮭魚")]
    [InlineData("紐西蕄鮏魚請加入隊伍 ,", "紐西蕄鮏魚")]
    [InlineData("遊玩 RO Survivors 需要帳號", null)]
    [InlineData("你暫離太久，已回到首頁", null)]
    public void Inviter_ParsesInviteText(string text, string? who)
        => Assert.Equal(who, PartyUi.Inviter(text));

    [Fact]
    public void Missing_ComparesWithPanel()
    {
        string[] panel = ["隊伍 2/3", "紐西蘭鮭魚 隊長", "巫師 · Lv 32 GS 806", "挪威鮭魚", "巫師 · Lv 33"];

        Assert.Equal(["鮭魚生魚片"], PartyUi.Missing(["挪威鮭魚", "鮭魚生魚片"], panel));
        Assert.Equal(["挪威鮭魚"], PartyUi.Missing(["挪威鮭魚"], null));
    }

    [Fact]
    public void Invite_MemberAcceptsOnlyTrusted_OthersDecline()
    {
        var s = Screen() with { PartyInvite = Btn };

        Assert.Equal(BtnCenter, new LobbyFlow().Decide(s with { PartyInviteTrusted = true }, Cfg("member"), 0).Click);
        Assert.Equal(new Point(450 - 73, 320), new LobbyFlow().Decide(s, Cfg("member"), 0).Click);
        Assert.Equal(new Point(450 - 73, 320), new LobbyFlow().Decide(s with { PartyInviteTrusted = true }, Cfg("off"), 0).Click);
    }

    [Fact]
    public void LobbyList_MatchesOcrRow()
    {
        var lines = new[]
        {
            new OcrLine("阿 斯 嘉 特 正 常", new Rectangle(760, 245, 300, 16)),
            new OcrLine("約 頓 海 姆", new Rectangle(760, 410, 50, 16)),
            new OcrLine("上 次", new Rectangle(760, 430, 20, 12)),
        };

        Assert.Equal(new Rectangle(760, 410, 50, 16), LobbyList.Match(lines, "約頓海姆"));
        Assert.Null(LobbyList.Match(lines, "尼達維勒"));
    }

    /// <summary>實測 2026-11-27 C：隊長「紐西蘭鮭魚」連兩次讀成「西魚」被當成別人的隊伍；但面板上是自己或別的分身時要認得出來</summary>
    [Theory]
    [InlineData("西魚", true)]
    [InlineData("紐西蕄鮏魚", true)]
    [InlineData("鮭魚生魚片", false)]
    [InlineData("挪威鮭魚", false)]
    [InlineData("路人甲", false)]
    public void IsOurLeader_ToleratesTruncatedName(string text, bool ours)
        => Assert.Equal(ours, PartyUi.IsOurLeader(text, ["紐西蘭鮭魚"], ["鮭魚生魚片", "挪威鮭魚"]));
}
