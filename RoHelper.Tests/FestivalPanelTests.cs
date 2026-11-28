using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>燈籠節視窗：九連抽、製作香、點香（實測截圖 2026-11-28 B，開始遊戲範本在 (1224, 535, 154, 57)）</summary>
public class FestivalPanelTests
{
    private static readonly Rectangle StartAt = new(1224, 535, 154, 57);

    private static FestivalPanel Read(string fixture)
        => Assert.IsType<FestivalPanel>(FestivalPanel.Read(CardLayoutTests.Fixture(fixture), StartAt, 1.0));

    [Fact]
    public void GachaTab_OnlyFestivalNineEnabled()
    {
        var p = Read("燈籠節-扭蛋");

        Assert.True(p.GachaTab);
        Assert.False(p.IncenseTab);
        var nine = Assert.Single(p.Nine);
        Assert.InRange(nine.X, 855, 883);
        Assert.InRange(nine.Y, 220, 234);
        Assert.False(p.IncenseChecked);
    }

    [Fact]
    public void GachaAnimating_NothingEnabled()
    {
        var p = Read("燈籠節-抽獎中");

        Assert.True(p.GachaTab);
        Assert.Empty(p.Nine);
    }

    [Fact]
    public void GachaDone_NineEnabledAgain()
        => Assert.Single(Read("燈籠節-抽完").Nine);

    [Fact]
    public void IncenseTab_TwoCraftsEnabled()
    {
        var p = Read("燈籠節-香");

        Assert.True(p.IncenseTab);
        Assert.Equal(2, p.Craft.Count);
        Assert.InRange(p.Craft[0].Y, 220, 236);
        Assert.InRange(p.Craft[1].Y, 326, 342);
        Assert.Empty(p.Nine);
    }

    [Fact]
    public void AfterCraft_OnlySnakeCraft_AndIncenseChecked()
    {
        var p = Read("燈籠節-點香");

        Assert.InRange(Assert.Single(p.Craft).Y, 326, 342);
        Assert.True(p.IncenseChecked);
        Assert.InRange(p.IncenseBox.X, 1278, 1292);
        Assert.InRange(p.IncenseBox.Y, 496, 510);
    }

    /// <summary>實測 2026-11-28 A：九連抽結果卡片讓「單人大廳」範本過門檻，燈籠節視窗開著時不採用</summary>
    [Fact]
    public void ScreenReader_FestivalOpen_IgnoresSoloLobby()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));
        try
        {
            var shot = CardLayoutTests.Fixture("燈籠節-結果誤認單人大廳");
            var lib = new TemplateLibrary(dir);
            var full = new RectangleF(0, 0, 1, 1);
            lib.Save(ScreenTemplates.Start, shot.Crop(StartAt), full, 0.9, 1.0);
            lib.Save(ScreenTemplates.Solo, shot.Crop(new Rectangle(1025, 445, 28, 18)), full, 0.9, 1.0);

            var s = new ScreenReader(lib).Read(shot, 1.0, lobby: true);

            Assert.NotNull(s.Start);
            Assert.NotNull(s.Festival);
            Assert.Null(s.Solo);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NotFestivalWindow_ReturnsNull()
        => Assert.Null(FestivalPanel.Read(CardLayoutTests.Fixture("升級選卡2"), StartAt, 1.0));
}
