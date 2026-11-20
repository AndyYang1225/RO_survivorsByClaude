using RoHelper.Vision;

namespace RoHelper.Tests;

public class ColorBlobsTests
{
    private static bool IsRed(byte r, byte g, byte b) => r > 200 && g < 50 && b < 50;

    [Fact]
    public void Find_SeparatesTwoRedSquares()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);
        TestFrames.Fill(f, new Rectangle(50, 60, 6, 6), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(0, 0, 100, 100), IsRed, step: 1, gap: 2);

        Assert.Equal(2, blobs.Count);
        var a = blobs.Single(b => b.X0 == 10);
        Assert.Equal((10, 17, 17, 64), (a.Y0, a.X1, a.Y1, a.Count));
        var c = blobs.Single(b => b.X0 == 50);
        Assert.Equal((60, 55, 65, 36), (c.Y0, c.X1, c.Y1, c.Count));
        Assert.Equal(new Point(52, 62), c.Center);
    }

    [Fact]
    public void Find_OnlyLooksInsideRegion()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);
        TestFrames.Fill(f, new Rectangle(50, 60, 6, 6), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(40, 40, 60, 60), IsRed);

        Assert.Single(blobs);
        Assert.Equal(50, blobs[0].X0);
    }

    [Fact]
    public void Find_WithStep_SamplesEveryNthPixel()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(0, 0, 100, 100), IsRed, step: 2);

        Assert.Single(blobs);
        Assert.Equal(16, blobs[0].Count);
    }
}
