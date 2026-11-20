using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>
/// 自動遠征的範本（從真實截圖裁出來，裁切、門檻同報告裡要 Claude 用開發介面存的；範圍一律整張畫面，同開發介面存的）
/// 和遠征清單的 OCR（<see cref="ExpeditionReader"/>）。
/// </summary>
public sealed class ExpeditionScreenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));

    /// <summary>標題只裁字（「遠征」「難度」），不含前面的 ◆：含 ◆ 的話別的對話框標題（升級、選擇大廳、結算）也有 0.91～0.92</summary>
    internal static readonly Rectangle StageListArea = new(522, 32, 36, 18);
    internal static readonly Rectangle ExpeGoArea = new(1280, 612, 70, 28);
    internal static readonly Rectangle DiffTitleArea = new(772, 168, 36, 20);
    internal static readonly Rectangle LeaderArea = new(1074, 294, 62, 20);
    internal static readonly Rectangle LeaderPromptArea = new(892, 610, 76, 22);

    /// <summary>活動的「前往野外」：跟 Claude 存的範本一樣只在 (0.5, 0.45, 0.2, 0.25) 找，門檻 0.95</summary>
    private static readonly Rectangle GoFieldArea = new(1061, 384, 82, 28);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private TemplateLibrary Library()
    {
        var lib = new TemplateLibrary(_dir);
        var full = new RectangleF(0, 0, 1, 1);
        lib.Save(ScreenTemplates.StageList, CardLayoutTests.Fixture("遠征清單").Crop(StageListArea), full, 0.95, 1.0);
        lib.Save(ScreenTemplates.ExpeGo, CardLayoutTests.Fixture("遠征選好地圖").Crop(ExpeGoArea), full, 0.9, 1.0);
        lib.Save(ScreenTemplates.DiffTitle, CardLayoutTests.Fixture("遠征難度").Crop(DiffTitleArea), full, 0.95, 1.0);
        lib.Save(ScreenTemplates.ExpeLeader, CardLayoutTests.Fixture("大廳遠征隊長").Crop(LeaderArea), full, 0.9, 1.0);
        lib.Save(ScreenTemplates.LeaderPrompt, CardLayoutTests.Fixture("大廳遠征隊長").Crop(LeaderPromptArea), full, 0.9, 1.0);
        lib.Save(ScreenTemplates.GoField, CardLayoutTests.Fixture("前往野外").Crop(GoFieldArea), new RectangleF(0.5f, 0.45f, 0.2f, 0.25f), 0.95, 1.0);
        return lib;
    }

    private ScreenState Read(string fixture) => new ScreenReader(Library()).Read(CardLayoutTests.Fixture(fixture), 1.0, lobby: false, expe: true);

    private static Point Center(Rectangle? r) => new(r!.Value.X + r.Value.Width / 2, r.Value.Y + r.Value.Height / 2);

    [Fact]
    public void StageList_Found_GoOnlyWhenBlue()
    {
        var gray = Read("遠征清單");
        Assert.Equal(StageListArea, gray.StageList);
        Assert.Null(gray.ExpeGo);

        var blue = Read("遠征選好地圖");
        Assert.Equal(StageListArea, blue.StageList);
        Assert.Equal(ExpeGoArea, blue.ExpeGo);
    }

    [Fact]
    public void Difficulty_FoundWithGoButton()
    {
        var s = Read("遠征難度");

        Assert.Equal(DiffTitleArea, s.DiffTitle);
        // 實測按鈕 x 1060～1144、y 476～508
        var go = Center(s.DiffGo);
        Assert.InRange(go.X, 1090, 1115);
        Assert.InRange(go.Y, 485, 500);
        // 活動的「前往野外」範本在自己的範圍裡找不到（難度對話框比較高），所以要用 FindIn
        Assert.Null(s.GoField);
        Assert.Null(s.StageList);
    }

    [Fact]
    public void FestivalUpgradeDialog_NotDifficulty()
    {
        var s = Read("活動升級累積");

        Assert.Null(s.DiffTitle);
        Assert.Null(s.DiffGo);
        Assert.NotNull(s.GoField);
    }

    [Fact]
    public void Lobby_FindsLeaderAndPrompt()
    {
        var s = Read("大廳遠征隊長");

        Assert.Equal(LeaderArea, s.ExpeLeader);
        Assert.Equal(LeaderPromptArea, s.LeaderPrompt);
        Assert.Null(s.StageList);
        Assert.Null(s.DiffTitle);
    }

    [Theory]
    [InlineData("前往野外")]
    [InlineData("活動升級累積")]
    [InlineData("野外1")]
    [InlineData("升級選卡2")]
    [InlineData("結算存活1")]
    [InlineData("首頁")]
    public void OtherScreens_NoExpeditionTemplates(string fixture)
    {
        var s = Read(fixture);

        Assert.Null(s.StageList);
        Assert.Null(s.ExpeGo);
        Assert.Null(s.DiffTitle);
        Assert.Null(s.LeaderPrompt);
    }

    [Fact]
    public void SoloLobbyDialog_DimmedPromptNotFound()
    {
        // 選擇大廳的對話框後面是變暗的「與隊長交談」：不能當成可以按的提示
        Assert.Null(Read("選擇大廳").LeaderPrompt);
    }

    [Fact]
    public void ExpeOff_NotSearched_NotMissing()
    {
        var s = new ScreenReader(Library()).Read(CardLayoutTests.Fixture("遠征選好地圖"), 1.0, lobby: false);

        Assert.Null(s.StageList);
        Assert.DoesNotContain(ScreenTemplates.ExpeLeader, s.Missing);
    }

    [Fact]
    public void ExpeOn_ListsMissingExpeditionTemplates()
    {
        var s = new ScreenReader(new TemplateLibrary(_dir)).Read(TestFrames.Pattern(), 1.0, lobby: false, expe: true);

        Assert.Contains(ScreenTemplates.StageList, s.Missing);
        Assert.Contains(ScreenTemplates.LeaderPrompt, s.Missing);
    }

    private static ExpeditionReader Reader()
        => new(OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error), RoHelper.Engine.BuiltInSchema.Load().Stages);

    [Fact]
    public async Task ReadAsync_ListAndSelected()
    {
        var v = await Reader().ReadAsync(CardLayoutTests.Fixture("遠征清單"), 1.0, StageListArea);

        Assert.Equal(["普隆德拉原野", "普隆德拉地下水道", "失事輪", "柏伊亞嵐島", "斐揚森林", "斐揚洞穴"], v.Cards.Select(c => c.Stage).ToArray());
        Assert.Equal("普隆德拉城", v.Selected);
        // 斐揚洞穴是最下面那張（只露出上半，y 541～580），點的位置要在卡片上
        var cave = v.Cards[^1].Bounds;
        Assert.InRange(cave.Y + cave.Height / 2, 545, 580);
        Assert.InRange(cave.X + cave.Width / 2, 520, 790);
    }

    [Fact]
    public async Task ReadAsync_SelectedForest()
    {
        var v = await Reader().ReadAsync(CardLayoutTests.Fixture("遠征選好地圖"), 1.0, StageListArea);

        Assert.Equal("斐揚森林", v.Selected);
        Assert.Contains(v.Cards, c => c.Stage == "斐揚森林");
    }

    [Theory]
    [InlineData("德拉原野", "普隆德拉原野")]
    [InlineData("斐穴", "斐揚洞穴")]
    [InlineData("斐森林", "斐揚森林")]
    [InlineData("怕伊亞嵐島", "柏伊亞嵐島")]
    [InlineData("MVP Golden Thief 8u9", "普隆德拉地下水道")]
    [InlineData("MVP Moonlight Flower", "斐揚洞穴")]
    [InlineData("MVP High 0", "普隆德拉城")]
    [InlineData("Lv 21 一 25", null)]
    [InlineData("MVP 卜 , in", null)]
    public void MatchLine(string text, string? stage)
    {
        Assert.Equal(stage, ExpeditionReader.MatchLine(text, RoHelper.Engine.BuiltInSchema.Load().Stages));
    }
}
