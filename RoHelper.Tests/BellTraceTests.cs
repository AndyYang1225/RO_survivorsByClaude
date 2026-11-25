using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class BellTraceTests
{
    private static readonly BellBlob Blob = new(500, 400, 400, 600, 150, 400, 80, 200, 250, 70, 10, 0.3);

    [Fact]
    public void Idle_NothingRecorded()
    {
        var t = new BellTrace();
        Assert.Equal((null, null), t.Step(false, "", null, "", 1, "", 0));
        Assert.False(t.Active);
    }

    [Fact]
    public void Dodge_ShotsAndSummary()
    {
        var t = new BellTrace();
        var view = new BellView(Blob, 1904, 682, 1);
        Assert.Equal("躲鐘開始", t.Step(true, "躲到鐘下！", new BellView(null, 1904, 682, 1), "躲鐘：找鐘中，原地等", 0.9, "14:02", 0).Shot);
        Assert.Null(t.Step(true, "躲到鐘下！", view, "躲到鐘下！", 0.9, "14:02", 1000).Shot);
        Assert.Equal("躲鐘3s", t.Step(true, "躲到鐘下！", view, "在鐘下等待", 0.8, "14:05", 3000).Shot);
        var (shot, done) = t.Step(false, "", null, "", null, "", 15000);
        Assert.Equal("躲鐘結束", shot);
        Assert.NotNull(done);
        Assert.Equal(3, (int)done!["frames"]!);
        Assert.Equal(2, (int)done["found"]!);
        Assert.Equal(1, (int)done["parked"]!);
        Assert.Equal("在鐘下等待", (string)done["lastStatus"]!);
        Assert.Equal(80, (int)done["hpEnd"]!);
        Assert.Equal("14:02", (string)done["clock"]!);
        Assert.Equal(3, done["samples"]!.AsArray().Count);
        Assert.False(t.Active);
    }

    [Fact]
    public void Reset_DropsUnfinished()
    {
        var t = new BellTrace();
        t.Step(true, "x", null, "", null, "", 0);
        t.Reset();
        Assert.Equal((null, null), t.Step(false, "", null, "", null, "", 100));
    }
}
