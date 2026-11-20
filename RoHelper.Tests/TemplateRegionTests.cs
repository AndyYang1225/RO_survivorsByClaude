using RoHelper.Vision;

namespace RoHelper.Tests;

public class TemplateRegionTests
{
    [Fact]
    public void Around_InflatesByLongSidePlusFivePercent()
    {
        // 長邊 40 ＋ 畫面長邊 1000 的 5%（50）＝ 每邊擴大 90
        var r = TemplateRegion.Around(new Rectangle(100, 100, 40, 20), new Size(1000, 500));

        Assert.Equal(0.01f, r.X, 3);
        Assert.Equal(0.02f, r.Y, 3);
        Assert.Equal(0.22f, r.Width, 3);
        Assert.Equal(0.40f, r.Height, 3);
    }

    [Fact]
    public void Around_ClampsToFrame()
    {
        var r = TemplateRegion.Around(new Rectangle(0, 0, 10, 10), new Size(100, 100));

        Assert.Equal(new RectangleF(0, 0, 0.25f, 0.25f), r);
    }
}
