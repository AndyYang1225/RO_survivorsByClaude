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

        Assert.Equal("確認結算畫面", flow.Decide(s, Cfg(), -300).Action);
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
    public void Return_AfterLobbyScreen_CountsAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), -300);
        flow.Decide(Screen() with { Return = Btn }, Cfg(), 0);
        flow.Decide(Screen() with { Solo = Btn }, Cfg(), 2000);

        flow.Decide(Screen() with { Return = Btn }, Cfg(), 900_000);
        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 900_300).CountRun);
    }

    /// <summary>
    /// 實測（2026-11-21）：主迴圈 1500 ms＋結算時讀畫面，兩次看到「返回角色」相隔超過 2 秒，同一局被記兩次。
    /// 沒看到大廳畫面之前，結算畫面消失多久再出現都不能重記
    /// </summary>
    [Fact]
    public void Return_GoneLongWithoutLobby_DoesNotCountAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), -300);
        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 0).CountRun);
        flow.Decide(Screen(), Cfg(), 1500);
        flow.Decide(Screen(), Cfg(), 3000);

        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 4500).CountRun);
        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 6000).CountRun);
    }

    /// <summary>
    /// 實測（2026-11-25）：死亡結算畫面上方的計時器列讓「前往野外」範本到 0.908（門檻 0.9），
    /// 每次完整辨識都「看到大廳按鈕」把這次結算清掉，同一局每 1～2 秒重記一次（局數 54 → 205）。
    /// 畫面上還有「返回角色」就一定是結算畫面，同時認到的大廳按鈕不算
    /// </summary>
    [Fact]
    public void Return_WithFalseLobbyButtonOnSameScreen_DoesNotCountAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), -300);
        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 0).CountRun);
        flow.Decide(Screen() with { Return = Btn, GoField = Btn }, Cfg(), 1500);

        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 3000).CountRun);
        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 4500).CountRun);
    }

    /// <summary>同上，誤判成的是「組隊準備好」按鈕</summary>
    [Fact]
    public void Return_WithFalsePartyReadyOnSameScreen_DoesNotCountAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), -300);
        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 0).CountRun);
        flow.Decide(Screen() with { Return = Btn, PartyReady = Btn }, Cfg(), 1500);

        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 3000).CountRun);
        Assert.False(flow.Decide(Screen() with { Return = Btn }, Cfg(), 4500).CountRun);
    }

    /// <summary>實測野外畫面偶爾有一格讓「返回角色」範本誤判：只看到一格不能記一局、不能點</summary>
    [Fact]
    public void Return_SingleFalseFrame_NotCounted()
    {
        var flow = new LobbyFlow();

        var d = flow.Decide(Screen() with { Return = Btn }, Cfg(), 0);
        flow.Decide(Screen(), Cfg(), 300);
        var again = flow.Decide(Screen() with { Return = Btn }, Cfg(), 600);

        Assert.False(d.CountRun);
        Assert.Null(d.Click);
        Assert.False(again.CountRun);
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
    public void GoField_Click_StartsRun()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Start = Btn }, Cfg(), 0);
        var start = flow.Decide(Screen() with { Start = Btn }, Cfg(), 900);
        flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2000);
        var go = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2900);

        Assert.False(start.StartsRun);
        Assert.True(go.StartsRun);
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
    public void Expedition_NothingVisible_Idle()
    {
        var d = new LobbyFlow().Decide(Screen(), Cfg(autoEvent: false, autoExpe: true), 0);

        Assert.Equal("待機", d.Action);
    }

    [Fact]
    public void Return_MissedOneFrame_DoesNotCountAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), -300);
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

        flow.Decide(s, Cfg(), -300);
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

    [Fact]
    public void Discard_ClicksAfterHalfSecond_BeforeSoloLobby()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Discard = Btn, Solo = new Rectangle(0, 0, 10, 10) };

        var first = flow.Decide(s, Cfg(), 0);
        Assert.Equal("捨棄未完成的一局", first.Action);
        Assert.Null(first.Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 600).Click);
    }

    [Fact]
    public void Discard_NotClicked_WhenAutoEventOff()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Discard = Btn };

        flow.Decide(s, Cfg(autoEvent: false), 0);
        Assert.Null(flow.Decide(s, Cfg(autoEvent: false), 600).Click);
    }

    [Fact]
    public void DiscardOk_ClicksImmediately()
    {
        var d = new LobbyFlow().Decide(Screen() with { DiscardOk = Btn, Solo = new Rectangle(0, 0, 10, 10) }, Cfg(autoEvent: false), 0);

        Assert.Equal("關閉已捨棄本局", d.Action);
        Assert.Equal(BtnCenter, d.Click);
        Assert.False(d.CountRun);
        Assert.False(d.StartsRun);
    }

    [Fact]
    public void Discard_GoneThenBack_WaitsAgain()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Discard = Btn };

        flow.Decide(s, Cfg(), 0);
        flow.Decide(Screen(), Cfg(), 300);
        Assert.Null(flow.Decide(s, Cfg(), 600).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 1200).Click);
    }

    [Fact]
    public void Title_ClicksAfterOneSecond_BeforeSoloLobby()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Title = Btn, Solo = new Rectangle(0, 0, 10, 10) };

        var first = flow.Decide(s, Cfg(), 0);
        Assert.Equal("首頁：開始遊戲", first.Action);
        Assert.Null(first.Click);
        Assert.Null(flow.Decide(s, Cfg(), 900).Click);
        var d = flow.Decide(s, Cfg(), 1100);
        Assert.Equal(BtnCenter, d.Click);
        Assert.False(d.CountRun);
        Assert.False(d.StartsRun);
    }

    [Fact]
    public void CharSelect_ClicksAfterOneSecond()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { CharSelect = Btn };

        var first = flow.Decide(s, Cfg(), 0);
        Assert.Equal("選角色：開始遊戲", first.Action);
        Assert.Null(first.Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 1100).Click);
    }

    [Fact]
    public void Discard_BeforeTitle()
    {
        var d = new LobbyFlow().Decide(Screen() with { DiscardOk = Btn, Title = new Rectangle(0, 0, 10, 10) }, Cfg(), 0);

        Assert.Equal("關閉已捨棄本局", d.Action);
    }

    [Fact]
    public void TitleAndCharSelect_NotClicked_WhenAutoEventOff()
    {
        var flow = new LobbyFlow();
        var title = Screen() with { Title = Btn };
        var select = Screen() with { CharSelect = Btn };

        flow.Decide(title, Cfg(autoEvent: false), 0);
        Assert.Null(flow.Decide(title, Cfg(autoEvent: false), 1100).Click);
        flow.Decide(select, Cfg(autoEvent: false), 2000);
        Assert.Null(flow.Decide(select, Cfg(autoEvent: false), 3100).Click);
    }

    /// <summary>實測（2026-11-21）：切成自動遠征（自動活動關）後重開程式，停在首頁不動</summary>
    [Fact]
    public void TitleCharSelectDiscard_Clicked_WhenAutoExpeOn()
    {
        var flow = new LobbyFlow();
        var cfg = Cfg(autoEvent: false, autoExpe: true);

        flow.Decide(Screen() with { Title = Btn }, cfg, 0);
        Assert.Equal(BtnCenter, flow.Decide(Screen() with { Title = Btn }, cfg, 1100).Click);
        flow.Decide(Screen() with { CharSelect = Btn }, cfg, 2000);
        Assert.Equal(BtnCenter, flow.Decide(Screen() with { CharSelect = Btn }, cfg, 3100).Click);
        flow.Decide(Screen() with { Discard = Btn }, cfg, 4000);
        Assert.Equal(BtnCenter, flow.Decide(Screen() with { Discard = Btn }, cfg, 4600).Click);
    }

    [Fact]
    public void Title_GoneThenBack_WaitsAgain()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Title = Btn };

        flow.Decide(s, Cfg(), 0);
        flow.Decide(Screen(), Cfg(), 500);
        Assert.Null(flow.Decide(s, Cfg(), 1100).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 2200).Click);
    }

    /// <summary>暫離、中文頻道的通知一律馬上關掉（不管自動活動有沒有開：只是通知）</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfkAndChannelNotices_ClosedImmediately(bool autoEvent)
    {
        var flow = new LobbyFlow();

        var afk = flow.Decide(Screen() with { AfkOk = Btn }, Cfg(autoEvent: autoEvent), 0);
        var channel = flow.Decide(Screen() with { ChannelNo = Btn }, Cfg(autoEvent: autoEvent), 100);

        Assert.Equal(new FlowDecision("關閉暫離通知", BtnCenter), afk);
        Assert.Equal(new FlowDecision("關閉中文頻道提示", BtnCenter), channel);
    }

    // ---- 燈籠節視窗：九連抽 → 製作香 → 點香 → 開始遊戲 ----

    private static readonly Point GachaTabAt = new(10, 10), IncenseTabAt = new(20, 10), NineAt = new(30, 30), CraftAt = new(40, 40), BoxAt = new(50, 50);

    private static FestivalPanel Panel(bool gacha = true, bool nine = false, bool craft = false, bool ticked = false)
        => new(gacha, !gacha, GachaTabAt, IncenseTabAt, nine ? [NineAt] : [], craft ? [CraftAt] : [], ticked, BoxAt);

    private static FlowSettings FestCfg() => Cfg() with { FestGacha = true, FestCraft = true, FestIncense = true };

    private static ScreenState Fest(FestivalPanel p) => Screen() with { Start = Btn, Festival = p };

    [Fact]
    public void Festival_PullsNine_WaitsAnimation_ThenCraftsTicksAndStarts()
    {
        var flow = new LobbyFlow();
        var c = FestCfg();

        Assert.Equal("燈籠節：等視窗打開", flow.Decide(Fest(Panel()), c, -2000).Action); // 視窗淡入中，先不看
        Assert.Equal(NineAt, flow.Decide(Fest(Panel(nine: true)), c, 0).Click);
        Assert.Null(flow.Decide(Fest(Panel()), c, 1000).Click);           // 剛按，等畫面
        Assert.Equal("燈籠節：等扭蛋動畫", flow.Decide(Fest(Panel()), c, 5000).Action);
        Assert.Equal(NineAt, flow.Decide(Fest(Panel(nine: true)), c, 7000).Click); // 抽完還夠 9 顆，再抽
        Assert.Equal(IncenseTabAt, flow.Decide(Fest(Panel()), c, 20_000).Click); // 抽完了，去香分頁
        Assert.Equal(CraftAt, flow.Decide(Fest(Panel(gacha: false, craft: true)), c, 22_000).Click);
        Assert.Equal(BoxAt, flow.Decide(Fest(Panel(gacha: false)), c, 24_000).Click);
        Assert.Equal("準備開始遊戲", flow.Decide(Fest(Panel(gacha: false, ticked: true)), c, 26_000).Action);
        Assert.Equal(BtnCenter, flow.Decide(Fest(Panel(gacha: false, ticked: true)), c, 27_000).Click);
    }

    [Fact]
    public void Festival_TicksIncenseOnlyOnce()
    {
        var flow = new LobbyFlow();
        var c = Cfg() with { FestIncense = true };

        flow.Decide(Fest(Panel()), c, -2000);
        Assert.Equal(BoxAt, flow.Decide(Fest(Panel()), c, 0).Click);
        Assert.Equal("準備開始遊戲", flow.Decide(Fest(Panel()), c, 2000).Action); // 勾不起來（沒有香）就不再按
    }

    [Fact]
    public void Festival_Off_StartsDirectly()
    {
        var flow = new LobbyFlow();

        flow.Decide(Fest(Panel(nine: true, craft: true)), Cfg(), 0);
        Assert.Equal(BtnCenter, flow.Decide(Fest(Panel(nine: true)), Cfg(), 1000).Click);
    }
}
