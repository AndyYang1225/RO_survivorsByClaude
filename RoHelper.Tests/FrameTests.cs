using RoHelper.Vision;

namespace RoHelper.Tests;

public class FrameTests
{
    [Fact]
    public void At_ReadsRgbFromBgra()
    {
        var f = TestFrames.Solid(4, 3, 10, 20, 30);

        Assert.Equal(((byte)10, (byte)20, (byte)30), f.At(3, 2));
    }

    [Fact]
    public void Crop_CopiesRegion_AndClipsToBounds()
    {
        var f = TestFrames.Solid(10, 10, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(8, 8, 2, 2), 255, 0, 0);

        var c = f.Crop(new Rectangle(7, 7, 10, 10));

        Assert.Equal(3, c.Width);
        Assert.Equal(3, c.Height);
        Assert.Equal(((byte)0, (byte)0, (byte)0), c.At(0, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0), c.At(2, 2));
    }

    [Fact]
    public void Resize_Doubles_WithNearestNeighbor()
    {
        var f = TestFrames.Solid(2, 1, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(1, 0, 1, 1), 255, 255, 255);

        var r = f.Resize(2);

        Assert.Equal(4, r.Width);
        Assert.Equal(2, r.Height);
        Assert.Equal(((byte)0, (byte)0, (byte)0), r.At(1, 1));
        Assert.Equal(((byte)255, (byte)255, (byte)255), r.At(2, 1));
    }

    [Fact]
    public void BitmapRoundTrip_KeepsPixels()
    {
        var f = TestFrames.Pattern(30, 20);

        using var bmp = f.ToBitmap();
        var back = Frame.FromBitmap(bmp);

        Assert.Equal(f.Pixels, back.Pixels);
    }

    [Fact]
    public void Gray_UsesLuma()
    {
        var f = TestFrames.Solid(1, 1, 255, 0, 0);

        Assert.Equal(76, f.Gray[0]);
    }

    [Fact]
    public void Half_IsHalfSize_AndCached()
    {
        var f = TestFrames.Pattern();

        Assert.Equal(100, f.Half.Width);
        Assert.Equal(50, f.Half.Height);
        Assert.Same(f.Half, f.Half);
    }

    [Fact]
    public void ViewportMapper_DividesByDpr()
    {
        var m = new ViewportMapper(1.25);

        Assert.Equal((200d, 80d), m.ToCss(250, 100));
    }
}
