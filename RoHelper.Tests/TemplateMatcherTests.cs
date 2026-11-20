using RoHelper.Vision;

namespace RoHelper.Tests;

public class TemplateMatcherTests
{
    [Fact]
    public void Find_LocatesExactCrop_AtOddPosition()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(57, 31, 30, 20));

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, f.Width, f.Height));

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(57, 31, 30, 20), m.Value.Bounds);
        Assert.Equal(1.0, m.Value.Score, 3);
    }

    [Fact]
    public void Find_LargeRegion_UsesPyramid_AndStillExact()
    {
        // 800×600 > 25 萬像素，會走半尺寸粗找；位置刻意用奇數，驗證回到原尺寸後精找得回來
        var f = TestFrames.Pattern(800, 600);
        var t = f.Crop(new Rectangle(457, 331, 60, 40));

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, f.Width, f.Height));

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(457, 331, 60, 40), m.Value.Bounds);
        Assert.Equal(1.0, m.Value.Score, 3);
    }

    [Fact]
    public void Find_StaysInsideRegion()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(57, 31, 30, 20));

        var m = TemplateMatcher.Find(f, t, new Rectangle(100, 0, 100, 100));

        Assert.NotNull(m);
        Assert.True(m.Value.Bounds.X >= 100);
        Assert.True(m.Value.Score < 1.0);
    }

    [Fact]
    public void Find_ReturnsNull_WhenTemplateLargerThanRegion()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(0, 0, 50, 50));

        Assert.Null(TemplateMatcher.Find(f, t, new Rectangle(0, 0, 40, 40)));
    }

    [Fact]
    public void Find_ScoresZero_ForOppositeColors()
    {
        var f = TestFrames.Solid(50, 50, 0, 0, 0);
        var t = TestFrames.Solid(10, 10, 255, 255, 255);

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, 50, 50));

        Assert.NotNull(m);
        Assert.Equal(0.0, m.Value.Score, 3);
    }
}
