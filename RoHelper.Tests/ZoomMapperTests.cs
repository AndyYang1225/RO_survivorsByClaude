using RoHelper.Forms;

namespace RoHelper.Tests;

public class ZoomMapperTests
{
    // 400×100 的圖放進 200×100 的框：縮成 0.5 倍（200×50），上下各留 25
    private static readonly Size Box = new(200, 100);
    private static readonly Size Image = new(400, 100);

    [Fact]
    public void ToImage_MapsThroughScaleAndOffset()
    {
        Assert.Equal(new Point(200, 50), ZoomMapper.ToImage(Box, Image, new Point(100, 50)));
    }

    [Fact]
    public void ToImage_ReturnsNull_OutsideImage()
    {
        Assert.Null(ZoomMapper.ToImage(Box, Image, new Point(100, 10)));
    }

    [Fact]
    public void ToBox_IsInverseOfToImage()
    {
        Assert.Equal(new Rectangle(50, 35, 50, 10), ZoomMapper.ToBox(Box, Image, new Rectangle(100, 20, 100, 20)));
    }
}
