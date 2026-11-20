using RoHelper.Vision;

namespace RoHelper.Tests;

public sealed class TemplateLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenReload_FindsTemplate()
    {
        var f = TestFrames.Pattern();
        new TemplateLibrary(_dir).Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0, 0, 1, 1), 0.9, 1.0);

        var lib = new TemplateLibrary(_dir);
        var m = lib.Find(f, "白塊", 1.0);

        Assert.Contains("白塊", lib.Names);
        Assert.NotNull(m);
        Assert.Equal(new Rectangle(57, 31, 30, 20), m.Value.Bounds);
    }

    [Fact]
    public void Find_ReturnsNull_BelowThreshold_ButMatchStillReportsScore()
    {
        var lib = new TemplateLibrary(_dir);
        lib.Save("白", TestFrames.Solid(10, 10, 255, 255, 255), new RectangleF(0, 0, 1, 1), 0.9, 1.0);
        var black = TestFrames.Solid(50, 50, 0, 0, 0);

        Assert.Null(lib.Find(black, "白", 1.0));
        Assert.Equal(0.0, lib.Match(black, "白", 1.0)!.Value.Score, 3);
    }

    [Fact]
    public void Find_ScalesTemplate_WhenDprDiffers()
    {
        var f = TestFrames.Pattern();
        var lib = new TemplateLibrary(_dir);
        lib.Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0, 0, 1, 1), 0.9, 1.0);

        var m = lib.Find(f.Resize(2), "白塊", 2.0);

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(114, 62, 60, 40), m.Value.Bounds);
    }

    [Fact]
    public void Find_SearchesOnlyInsideRegion()
    {
        var f = TestFrames.Pattern();
        var lib = new TemplateLibrary(_dir);
        // 只在右半邊找：白塊在左半邊，應該找不到
        lib.Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0.5f, 0, 0.5f, 1), 0.99, 1.0);

        Assert.Null(lib.Find(f, "白塊", 1.0));
    }

    [Fact]
    public void Save_RejectsInvalidName()
    {
        var lib = new TemplateLibrary(_dir);

        Assert.Throws<ArgumentException>(() => lib.Save("a/b", TestFrames.Solid(2, 2, 0, 0, 0), new RectangleF(0, 0, 1, 1), 0.9, 1.0));
    }
}
