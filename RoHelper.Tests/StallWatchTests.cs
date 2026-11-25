using RoHelper.Vision;

namespace RoHelper.Tests;

public class StallWatchTests
{
    private const long Min = 60_000;

    private static StallWatch New() => new(frozenMs: 3 * Min, noRunMs: 45 * Min, maxReloads: 2);

    /// <summary>每 15 秒換一張不同的畫面，跑到 until</summary>
    private static void Animate(StallWatch w, long from, long until)
    {
        for (var t = from; t <= until; t += 15_000)
            w.Frame(t, (ulong)t);
    }

    [Fact]
    public void ChangingFrames_WithinRunTime_NoAction()
    {
        var w = New();
        w.Idle(0);
        Animate(w, 0, 40 * Min);
        Assert.Equal(StallAct.None, w.Check(40 * Min).Act);
    }

    [Fact]
    public void FrozenFrame_Reloads()
    {
        var w = New();
        w.Idle(0);
        for (var t = 0L; t <= 4 * Min; t += 15_000)
            w.Frame(t, 42);
        var v = w.Check(4 * Min);
        Assert.Equal(StallAct.Reload, v.Act);
        Assert.Contains("畫面", v.Why);
    }

    /// <summary>主迴圈卡住（輸入一直不回應）＝完全沒有新截圖，也要算凍結</summary>
    [Fact]
    public void NoFramesAtAll_Reloads()
    {
        var w = New();
        w.Idle(0);
        w.Frame(1000, 1);
        Assert.Equal(StallAct.None, w.Check(3 * Min).Act);
        Assert.Equal(StallAct.Reload, w.Check(3 * Min + 1000).Act);
    }

    [Fact]
    public void NoRunFor45Min_Reloads()
    {
        var w = New();
        w.Idle(0);
        Animate(w, 0, 46 * Min);
        var v = w.Check(46 * Min);
        Assert.Equal(StallAct.Reload, v.Act);
        Assert.Contains("新局", v.Why);
    }

    [Fact]
    public void Progress_ResetsRunTimer()
    {
        var w = New();
        w.Idle(0);
        Animate(w, 0, 46 * Min);
        w.Progress(30 * Min);
        Assert.Equal(StallAct.None, w.Check(46 * Min).Act);
    }

    [Fact]
    public void Escalation_TwoReloadsThenOneAlert_ThenQuiet()
    {
        var w = New();
        w.Idle(0);
        Assert.Equal(StallAct.Reload, w.Check(4 * Min).Act);
        Assert.Equal(StallAct.None, w.Check(5 * Min).Act); // 重新整理後重新計時
        Assert.Equal(StallAct.Reload, w.Check(8 * Min).Act);
        Assert.Equal(2, w.Reloads);
        Assert.Equal(StallAct.Alert, w.Check(12 * Min).Act);
        Assert.True(w.Alerted);
        Assert.Equal(StallAct.None, w.Check(16 * Min).Act);
        Assert.Equal(StallAct.None, w.Check(60 * Min).Act);
    }

    [Fact]
    public void Progress_ClearsEscalation()
    {
        var w = New();
        w.Idle(0);
        w.Check(4 * Min);
        w.Check(8 * Min);
        w.Check(12 * Min);
        Assert.True(w.Alerted);
        w.Progress(13 * Min);
        Assert.False(w.Alerted);
        Assert.Equal(0, w.Reloads);
        Assert.Equal(StallAct.Reload, w.Check(17 * Min).Act);
    }

    /// <summary>暫停中（主迴圈不截圖）每輪 Idle：多久都不算卡住，解除暫停後重新計時</summary>
    [Fact]
    public void Idle_WhilePaused_NeverStalls()
    {
        var w = New();
        for (var t = 0L; t <= 120 * Min; t += 1500)
            w.Idle(t);
        Assert.Equal(StallAct.None, w.Check(120 * Min).Act);
        Assert.Equal(StallAct.None, w.Check(122 * Min).Act);
        Assert.Equal(StallAct.Reload, w.Check(123 * Min).Act);
    }

    [Fact]
    public void Signature_SameFrameSame_DifferentPixelDifferent()
    {
        var a = TestFrames.Solid(200, 100, 10, 20, 30);
        var b = TestFrames.Solid(200, 100, 10, 20, 30);
        Assert.Equal(StallWatch.Signature(a), StallWatch.Signature(b));
        // 取樣點 (3,1)×(200/32, 100/32) 附近：第 1 列第 0 個取樣點
        var y = (int)(1.5 * 100 / 32);
        var x = (int)(0.5 * 200 / 32);
        b.Pixels[(y * 200 + x) * 4] = 99;
        Assert.NotEqual(StallWatch.Signature(a), StallWatch.Signature(b));
    }
}

public class StallWatchForceTests
{
    [Fact]
    public void Force_ReloadsOnNextCheck_Once()
    {
        var w = new StallWatch();
        w.Progress(0);
        w.Frame(0, 1);
        Assert.Equal(StallAct.None, w.Check(1000).Act);
        w.Force("遊戲時間超過 18 分鐘");
        var v = w.Check(2000);
        Assert.Equal(StallAct.Reload, v.Act);
        Assert.Contains("18", v.Why);
        w.Frame(2500, 2);
        Assert.Equal(StallAct.None, w.Check(3000).Act);
    }
}
