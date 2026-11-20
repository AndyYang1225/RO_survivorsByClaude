using RoHelper.Vision;

namespace RoHelper.Tests;

public sealed class ScreenReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));

    /// <summary>Pattern 圖上的白色方塊附近（當作按鈕）</summary>
    private static readonly Rectangle ButtonArea = new(57, 31, 30, 20);

    /// <summary>Pattern 圖上的黑色方塊附近（當作 NPC 名字）</summary>
    private static readonly Rectangle LabelArea = new(15, 65, 25, 20);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private TemplateLibrary Library(params (string Name, Rectangle Area)[] items)
    {
        var lib = new TemplateLibrary(_dir);
        var f = TestFrames.Pattern();
        foreach (var (name, area) in items)
            lib.Save(name, f.Crop(area), new RectangleF(0, 0, 1, 1), 0.9, 1.0);
        return lib;
    }

    [Fact]
    public void Read_FindsSavedButton_AndListsMissing()
    {
        var s = new ScreenReader(Library((ScreenTemplates.Start, ButtonArea))).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.Equal(ButtonArea, s.Start);
        Assert.Contains(ScreenTemplates.Return, s.Missing);
        Assert.DoesNotContain(ScreenTemplates.Start, s.Missing);
    }

    [Fact]
    public void Read_SkipsLobbyTemplates_WhenAButtonIsVisible()
    {
        var lib = Library((ScreenTemplates.Start, ButtonArea), (ScreenTemplates.FestivalHost, LabelArea));

        var s = new ScreenReader(lib).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.NotNull(s.Start);
        Assert.Null(s.FestivalHost);
    }

    [Fact]
    public void Read_FindsLobbyTemplates_OnlyWhenLobbyRequested()
    {
        var reader = new ScreenReader(Library((ScreenTemplates.FestivalHost, LabelArea)));

        Assert.Equal(LabelArea, reader.Read(TestFrames.Pattern(), 1.0, lobby: true).FestivalHost);
        Assert.Null(reader.Read(TestFrames.Pattern(), 1.0, lobby: false).FestivalHost);
    }
}
