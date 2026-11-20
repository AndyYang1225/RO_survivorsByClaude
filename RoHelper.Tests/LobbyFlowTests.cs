using RoHelper.Vision;

namespace RoHelper.Tests;

public class LobbyFlowTests
{
    /// <summary>按鈕位置，中心是 (450, 320)</summary>
    private static readonly Rectangle Btn = new(400, 300, 100, 40);
    private static readonly Point BtnCenter = new(450, 320);

    private static FlowSettings Cfg(bool paused = false, bool autoEvent = true, bool autoExpe = false, bool autoReturn = true,
        bool runLimit = false, int runLimitN = 10, int runs = 0)
        => new(paused, SoloLobby: true, autoEvent, autoExpe, autoReturn, runLimit, runLimitN, runs, TStart: 800, TReturn: 1500, TLobby: 700);

    private static ScreenState Screen() => new(new Size(1000, 600), 1.0);

    [Fact]
    public void Paused_DoesNothing()
    {
        var d = new LobbyFlow().Decide(Screen() with { Start = Btn }, Cfg(paused: true), 0);

        Assert.Equal("已暫停", d.Action);
        Assert.Null(d.Click);
    }

    [Fact]
    public void Solo_ClicksAfterHalfSecond()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Solo = Btn };

        Assert.Null(flow.Decide(s, Cfg(), 0).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 600).Click);
    }

    [Fact]
    public void Return_CountsRunOnce_ThenClicksAfterDelay()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Return = Btn };

        var first = flow.Decide(s, Cfg(), 0);
        var waiting = flow.Decide(s, Cfg(), 1000);
        var click = flow.Decide(s, Cfg(), 1600);

        Assert.True(first.CountRun);
        Assert.Null(first.Click);
        Assert.False(waiting.CountRun);
        Assert.Null(waiting.Click);
        Assert.False(click.CountRun);
        Assert.Equal(BtnCenter, click.Click);
    }

    [Fact]
    public void Return_GoneThenBack_CountsAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), 0);
        flow.Decide(Screen(), Cfg(), 2000);

        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 3000).CountRun);
    }

    [Fact]
    public void Start_ThenGoField_EachClickedAfterTStart()
    {
        var flow = new LobbyFlow();

        Assert.Null(flow.Decide(Screen() with { Start = Btn }, Cfg(), 0).Click);
        Assert.Equal(BtnCenter, flow.Decide(Screen() with { Start = Btn }, Cfg(), 900).Click);
        Assert.Null(flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2000).Click);
        var go = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2900);

        Assert.Equal("準備前往野外", go.Action);
        Assert.Equal(BtnCenter, go.Click);
    }

    [Fact]
    public void GoField_WithoutRecentStart_IsIgnored()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { GoField = Btn }, Cfg(), 0);
        var d = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 900);

        Assert.Null(d.Click);
        Assert.Equal("待機", d.Action);
    }

    [Fact]
    public void RunLimit_BlocksStart()
    {
        var d = new LobbyFlow().Decide(Screen() with { Start = Btn }, Cfg(runLimit: true, runLimitN: 10, runs: 10), 5000);

        Assert.Null(d.Click);
        Assert.Equal("已達局數上限，停止自動開始", d.Action);
    }

    [Fact]
    public void TalkPrompt_ClicksOnce_RetriesAfterFiveSeconds()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { TalkPrompt = Btn };

        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 0).Click);
        Assert.Null(flow.Decide(s, Cfg(), 1000).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 5100).Click);
    }

    [Fact]
    public void AfterTalking_DoesNotWalkWhilePromptGone()
    {
        var flow = new LobbyFlow();

        Assert.Equal(BtnCenter, flow.Decide(Screen() with { TalkPrompt = Btn }, Cfg(), 0).Click);

        // 交談提示消失（對話框開啟中）：等待，不往 NPC 走（否則可能點掉對話框）
        var waiting = flow.Decide(Screen() with { FestivalHost = new Rectangle(800, 100, 80, 20) }, Cfg(), 1000);
        Assert.Null(waiting.Click);
        Assert.Equal("等待對話框", waiting.Action);

        // 過了 TalkRetry 仍沒有交談提示，才恢復走路
        Assert.NotNull(flow.Decide(Screen() with { FestivalHost = new Rectangle(800, 100, 80, 20) }, Cfg(), 5100).Click);
    }

    [Fact]
    public void FestivalHost_WalksTowardLabel_Throttled()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { FestivalHost = new Rectangle(800, 100, 80, 20) };

        var first = flow.Decide(s, Cfg(), 0);

        Assert.Equal("走向節慶主持人", first.Action);
        Assert.NotNull(first.Click);
        Assert.Null(flow.Decide(s, Cfg(), 300).Click);
        Assert.NotNull(flow.Decide(s, Cfg(), 800).Click);
    }

    [Fact]
    public void WalkTarget_ScalesStepThenAddsFeetOffset()
    {
        // 中心 (500,300)；往名字底部 (970,580) 的方向走 600×0.35＝210（k≈0.384）→ (680,407)，再往下 45 → (680,452)
        var p = LobbyFlow.WalkTarget(Screen(), new Rectangle(950, 560, 40, 20));

        Assert.Equal(new Point(680, 452), p);
    }

    [Fact]
    public void WalkTarget_ClampsAwayFromEdges()
    {
        // 100×100：中心 (50,50)；往 (50,95) 走 35 → (50,85)，加 45 → (50,130)，離下緣至少 20 → (50,80)
        var p = LobbyFlow.WalkTarget(new ScreenState(new Size(100, 100), 1.0), new Rectangle(40, 90, 20, 5));

        Assert.Equal(new Point(50, 80), p);
    }

    [Fact]
    public void NothingFound_ReportsMissingTemplates()
    {
        var d = new LobbyFlow().Decide(Screen() with { Missing = ["交談提示"] }, Cfg(), 0);

        Assert.Equal("缺少範本：交談提示", d.Action);
    }

    [Fact]
    public void Expedition_NotSupportedYet()
    {
        var d = new LobbyFlow().Decide(Screen(), Cfg(autoEvent: false, autoExpe: true), 0);

        Assert.Equal("視覺模式還不支援遠征", d.Action);
    }

    [Fact]
    public void Return_MissedOneFrame_DoesNotCountAgain()
    {
        var flow = new LobbyFlow();

        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 0).CountRun);
        flow.Decide(Screen(), Cfg(), 300);

        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 600).CountRun);
    }

    [Fact]
    public void Reset_ClearsPendingDelay()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Solo = Btn };

        flow.Decide(s, Cfg(), 0);
        flow.Reset();

        Assert.Null(flow.Decide(s, Cfg(), 600).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 1200).Click);
    }

    [Fact]
    public void Reset_KeepsCountedResultScreen()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Return = Btn };

        Assert.True(flow.Decide(s, Cfg(), 0).CountRun);
        flow.Reset();

        Assert.False(flow.Decide(s, Cfg(), 300).CountRun);
    }

    [Fact]
    public void Reset_KeepsFestivalWindow()
    {
        var flow = new LobbyFlow();
        var start = Screen() with { Start = Btn };
        var go = Screen() with { GoField = Btn };

        flow.Decide(start, Cfg(), 0);
        Assert.Equal(BtnCenter, flow.Decide(start, Cfg(), 900).Click);
        flow.Reset();

        Assert.Null(flow.Decide(go, Cfg(), 2000).Click);
        var d = flow.Decide(go, Cfg(), 2900);
        Assert.Equal(BtnCenter, d.Click);
        Assert.Equal("準備前往野外", d.Action);
    }
}
