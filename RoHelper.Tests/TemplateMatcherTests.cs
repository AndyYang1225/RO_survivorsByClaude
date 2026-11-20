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
    public void Find_LargeRegion_ShortWideTemplate_StillExact()
    {
        // 只驗證又扁又寬的範本在大範圍找得對（正確性），不能證明有走金字塔那條路
        var f = TestFrames.Pattern(800, 600);
        var t = f.Crop(new Rectangle(457, 331, 90, 18));

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, f.Width, f.Height));

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(457, 331, 90, 18), m.Value.Bounds);
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

    /// <summary>
    /// 實測（2026-11-21）：整張畫面找按鈕時，粗找只留最佳一點會被別處相似的色塊搶走（對話框藍色標題列 0.926），
    /// 真正的按鈕（1.000）被漏掉。改成保留多個候選後都要找到。
    /// </summary>
    [Fact]
    public void Find_WholeScreen_RealButton_NotStolenByLookalike()
        => AssertFinds("前往野外", 1061, 383, 82, 28, "前往野外", new Rectangle(0, 0, 1904, 682), 1061, 383);

    /// <summary>
    /// 對話框按鈕的位置會隨訊息行數上下移（實測單行 y 370、兩行 y 381）。整張畫面找會被金字塔漏掉（見 TemplateMatcher 的限制），
    /// 所以範本搜尋範圍限縮在畫面中央（x 35～65%、y 40～75%），這個範圍不走金字塔
    /// </summary>
    [Fact]
    public void Find_DialogButton_ShiftedByTwoLineMessage_InCenterRegion()
        => AssertFinds("未完成的一局", 1012, 370, 56, 26, "未完成的一局2",
            Rectangle.FromLTRB(1904 * 35 / 100, 682 * 40 / 100, 1904 * 65 / 100, 682 * 75 / 100), 1012, 381);

    private static void AssertFinds(string src, int x, int y, int w, int h, string target, Rectangle region, int ex, int ey)
    {
        var t = CardLayoutTests.Fixture(src).Crop(new Rectangle(x, y, w, h));
        var f = CardLayoutTests.Fixture(target);

        var m = TemplateMatcher.Find(f, t, region);

        Assert.NotNull(m);
        Assert.True(m.Value.Score > 0.97, $"score {m.Value.Score:F3} at {m.Value.Bounds}");
        Assert.InRange(m.Value.Bounds.X, ex - 3, ex + 3);
        Assert.InRange(m.Value.Bounds.Y, ey - 3, ey + 3);
    }
}
