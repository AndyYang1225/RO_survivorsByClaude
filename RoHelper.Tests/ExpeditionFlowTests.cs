using RoHelper.Vision;

namespace RoHelper.Tests;

public class ExpeditionFlowTests
{
    private static readonly Rectangle Title = ExpeditionScreenTests.StageListArea;
    private static readonly Rectangle Go = ExpeditionScreenTests.ExpeGoArea;
    private static readonly Rectangle DiffTitle = ExpeditionScreenTests.DiffTitleArea;
    private static readonly Rectangle DiffGo = new(1061, 478, 82, 28);
    private static readonly Rectangle List = new(505, 95, 290, 487);

    private static FlowSettings Cfg(string stage = "斐揚森林", string diff = "keep", bool pick = true, bool go = true, bool enter = true,
        bool talk = true, bool runLimit = false, int runs = 0)
        => new(false, SoloLobby: true, AutoEvent: false, AutoExpe: true, AutoReturn: true, runLimit, 10, runs, TStart: 800, TReturn: 1500, TLobby: 700,
            ExpeTalk: talk, ExpePick: pick, ExpeStage: stage, ExpeGo: go, ExpeDiff: diff, ExpeEnter: enter);

    private static ScreenState Screen() => new(new Size(1904, 682), 1.0);

    private static StageListView View(string selected, params string[] cards)
        => new(cards.Select((c, i) => new StageCard(c, new Rectangle(590, 115 + 88 * i, 80, 14))).ToList(), selected, List);

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    [Fact]
    public void Stage_ClicksWantedCard_ThenWaitsBeforeRetry()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title };
        var v = View("普隆德拉城", "普隆德拉原野", "斐揚森林");

        var d = flow.Decide(s, Cfg(), 0, v);
        Assert.Equal("② 選擇地圖：斐揚森林", d.Action);
        Assert.Equal(new Point(630, 210), d.Click);

        Assert.Null(flow.Decide(s, Cfg(), 1000, v).Click);
        Assert.NotNull(flow.Decide(s, Cfg(), 1600, v).Click);
    }

    [Fact]
    public void Stage_Selected_WaitsTStart_ThenClicksGo()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title, ExpeGo = Go };
        var v = View("斐揚森林", "斐揚森林");

        Assert.Equal("準備出發：斐揚森林", flow.Decide(s, Cfg(), 0, v).Action);
        Assert.Null(flow.Decide(s, Cfg(), 500, v).Click);
        var d = flow.Decide(s, Cfg(), 900, v);
        Assert.Equal("③ 出發：斐揚森林", d.Action);
        Assert.Equal(Center(Go), d.Click);
    }

    [Fact]
    public void Stage_NoBlueGo_CannotStart()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title };
        var v = View("普隆德拉城", "普隆德拉城");

        flow.Decide(s, Cfg(stage: "普隆德拉城"), 0, v);
        var d = flow.Decide(s, Cfg(stage: "普隆德拉城"), 900, v);

        Assert.Equal("普隆德拉城 目前不能出發，請換一張地圖", d.Action);
        Assert.Null(d.Click);
    }

    [Fact]
    public void Stage_NotInList_ScrollsDownThenUp_ThenGivesUp()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title };
        var v = View("斐揚森林", "普隆德拉原野");
        var deltas = new List<double>();

        for (var t = 0L; t < 20_000; t += 250)
        {
            var d = flow.Decide(s, Cfg(stage: "普隆德拉城"), t, v);
            if (d.WheelAt is { } w)
            {
                Assert.Equal(Center(List), w);
                deltas.Add(d.WheelDelta);
            }
        }

        Assert.Equal([300, 300, 300, -300, -300, -300, -300, -300, -300], deltas);
        Assert.Equal("找不到地圖：普隆德拉城", flow.Decide(s, Cfg(stage: "普隆德拉城"), 20_000, v).Action);
    }

    [Fact]
    public void Stage_FoundAfterScrolling_Clicks()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title };

        Assert.NotNull(flow.Decide(s, Cfg(stage: "普隆德拉城"), 0, View("斐揚森林", "普隆德拉原野")).WheelAt);
        var d = flow.Decide(s, Cfg(stage: "普隆德拉城"), 700, View("斐揚森林", "斐揚洞穴", "普隆德拉城"));

        Assert.Equal("② 選擇地圖：普隆德拉城", d.Action);
        Assert.NotNull(d.Click);
    }

    [Fact]
    public void Stage_PickOff_UsesSelected()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { StageList = Title, ExpeGo = Go };

        flow.Decide(s, Cfg(pick: false), 0, View("失事輪"));

        Assert.Equal("③ 出發：失事輪", flow.Decide(s, Cfg(pick: false), 900, View("失事輪")).Action);
    }

    [Fact]
    public void Stage_GoOff_Waits()
    {
        var s = Screen() with { StageList = Title, ExpeGo = Go };

        Assert.Equal("等你按「出發」", new LobbyFlow().Decide(s, Cfg(go: false), 0, View("斐揚森林")).Action);
    }

    [Fact]
    public void Stage_RunLimit_Stops()
    {
        var s = Screen() with { StageList = Title, ExpeGo = Go };

        Assert.Equal("已達局數上限", new LobbyFlow().Decide(s, Cfg(runLimit: true, runs: 10), 0, View("斐揚森林")).Action);
    }

    [Fact]
    public void Difficulty_Keep_WaitsTStart_ThenStartsExpeditionRun()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { DiffTitle = DiffTitle, DiffGo = DiffGo };

        var wait = flow.Decide(s, Cfg(), 0);
        Assert.Equal("④ 準備進入關卡", wait.Action);
        Assert.Null(wait.Click);
        var d = flow.Decide(s, Cfg(), 900);
        Assert.Equal(Center(DiffGo), d.Click);
        Assert.True(d.StartsRun);
        Assert.True(d.Expe);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("min")]
    public void Difficulty_ClicksEachSliderEnd_OnRealDialog(string diff)
    {
        var flow = new LobbyFlow();
        var s = Screen() with { DiffTitle = DiffTitle, DiffGo = DiffGo };
        var f = CardLayoutTests.Fixture("遠征難度");
        var clicks = Enumerable.Range(0, 5).Select(i => flow.Decide(s, Cfg(diff: diff), i * 250).Click!.Value).ToList();

        // 每一點都在滑桿上（軌道是灰色 (239,239,239)～(178,178,178)，圓鈕是藍色 (58,91,180)），由上到下 5 條
        foreach (var p in clicks)
        {
            var (r, g, b) = f.At(p.X, p.Y);
            Assert.True((r == g && g == b && r is >= 170 and <= 245) || b - r > 80, $"{p} 是 ({r},{g},{b})");
        }
        Assert.Equal([219, 247, 275, 303, 331], clicks.Select(p => p.Y).ToArray());
        Assert.All(clicks, p => Assert.Equal(diff == "max" ? 1096 : 991, p.X));

        // 點完滑桿而且滿 tStart 才按前往野外
        Assert.True(flow.Decide(s, Cfg(diff: diff), 1300).StartsRun);
    }

    [Fact]
    public void Difficulty_EnterOff_DoesNotClick()
    {
        var s = Screen() with { DiffTitle = DiffTitle, DiffGo = DiffGo };

        Assert.Null(new LobbyFlow().Decide(s, Cfg(enter: false), 5000).Click);
    }

    [Fact]
    public void Difficulty_RunLimit_Stops()
    {
        var s = Screen() with { DiffTitle = DiffTitle, DiffGo = DiffGo };

        Assert.Equal("已達局數上限，停止自動開始", new LobbyFlow().Decide(s, Cfg(runLimit: true, runs: 10), 0).Action);
    }

    [Fact]
    public void Difficulty_IgnoredWhenAutoExpeOff()
    {
        var s = Screen() with { DiffTitle = DiffTitle, DiffGo = DiffGo };
        var c = Cfg() with { AutoExpe = false };

        Assert.Null(new LobbyFlow().Decide(s, c, 5000).Click);
    }

    [Fact]
    public void Lobby_TalksToLeader()
    {
        var flow = new LobbyFlow();
        var d = flow.Decide(Screen() with { LeaderPrompt = new Rectangle(892, 610, 76, 22), ExpeLeader = new Rectangle(1074, 294, 62, 20) }, Cfg(), 0);

        Assert.Equal("和遠征隊長交談", d.Action);
        Assert.Equal(new Point(930, 621), d.Click);
    }

    [Fact]
    public void Lobby_WalksToLeader_IgnoresFestivalHost()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { ExpeLeader = new Rectangle(1074, 294, 62, 20), FestivalHost = new Rectangle(1290, 120, 70, 20) };

        var d = flow.Decide(s, Cfg(), 0);

        Assert.Equal("走向遠征隊長", d.Action);
        Assert.Equal(LobbyFlow.WalkTarget(s, s.ExpeLeader!.Value), d.Click);
    }

    [Fact]
    public void Lobby_TalkOff_Idle()
    {
        var s = Screen() with { ExpeLeader = new Rectangle(1074, 294, 62, 20) };

        Assert.Equal("待機", new LobbyFlow().Decide(s, Cfg(talk: false), 0).Action);
    }

    [Fact]
    public void FestivalGoField_NotPressedInExpeditionMode()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { GoField = new Rectangle(1061, 384, 82, 28) };

        Assert.Null(flow.Decide(s, Cfg(), 0).Click);
        Assert.Null(flow.Decide(s, Cfg(), 5000).Click);
    }
}
