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

    /// <summary>「未完成的一局」的捨棄按鈕、「已捨棄本局」的 OK 按鈕（截圖像素），範本從真實截圖裁出來，同 Claude 存的範本（整張畫面找、門檻 0.98）</summary>
    private static readonly Rectangle DiscardArea = new(1012, 370, 56, 26);
    private static readonly Rectangle DiscardOkArea = new(1092, 381, 50, 26);

    private TemplateLibrary DiscardLibrary()
    {
        var lib = new TemplateLibrary(_dir);
        lib.Save(ScreenTemplates.Discard, CardLayoutTests.Fixture("未完成的一局").Crop(DiscardArea), new RectangleF(0, 0, 1, 1), 0.98, 1.0);
        lib.Save(ScreenTemplates.DiscardOk, CardLayoutTests.Fixture("已捨棄本局").Crop(DiscardOkArea), new RectangleF(0, 0, 1, 1), 0.98, 1.0);
        return lib;
    }

    [Fact]
    public void Read_FindsDiscardDialogs_OnRealScreens()
    {
        var reader = new ScreenReader(DiscardLibrary());

        var unfinished = reader.Read(CardLayoutTests.Fixture("未完成的一局"), 1.0, lobby: true);
        Assert.Equal(DiscardArea, unfinished.Discard);
        Assert.Null(unfinished.DiscardOk);

        var discarded = reader.Read(CardLayoutTests.Fixture("已捨棄本局"), 1.0, lobby: true);
        Assert.Equal(DiscardOkArea, discarded.DiscardOk);
        Assert.Null(discarded.Discard);
    }

    [Theory]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("野外3錢不夠")]
    [InlineData("升級選卡2")]
    public void Read_NoDiscardDialogs_OnOtherScreens(string fixture)
    {
        var s = new ScreenReader(DiscardLibrary()).Read(CardLayoutTests.Fixture(fixture), 1.0, lobby: true);

        Assert.Null(s.Discard);
        Assert.Null(s.DiscardOk);
    }

    [Fact]
    public void Read_DiscardTemplatesAreOptional_NotListedAsMissing()
    {
        var s = new ScreenReader(Library()).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.DoesNotContain(ScreenTemplates.Discard, s.Missing);
        Assert.DoesNotContain(ScreenTemplates.DiscardOk, s.Missing);
    }

    /// <summary>
    /// 首頁「開始遊戲！」、角色選擇「開始遊戲」按鈕（截圖像素），範本從真實截圖裁出來，範圍和門檻同 Claude 存的範本。
    /// 首頁刻意用 y437（和 Claude 存的範本逐點相同），不用計畫寫的 y395：y395 起的那一塊橫向幾乎沒有變化（實測左右平移 20 px 差異都一樣），找到的 x 會偏掉，在升級選卡畫面也會誤判。
    /// </summary>
    private static readonly Rectangle TitleArea = new(1500, 437, 160, 34);
    private static readonly Rectangle CharSelectArea = new(1230, 628, 76, 26);

    private TemplateLibrary TitleLibrary()
    {
        var lib = new TemplateLibrary(_dir);
        lib.Save(ScreenTemplates.Title, CardLayoutTests.Fixture("首頁").Crop(TitleArea), new RectangleF(0.65f, 0.5f, 0.35f, 0.3f), 0.95, 1.0);
        lib.Save(ScreenTemplates.CharSelect, CardLayoutTests.Fixture("選角色").Crop(CharSelectArea), new RectangleF(0.55f, 0.82f, 0.2f, 0.18f), 0.95, 1.0);
        return lib;
    }

    [Fact]
    public void Read_FindsTitleAndCharSelect_OnRealScreens()
    {
        var reader = new ScreenReader(TitleLibrary());

        var title = reader.Read(CardLayoutTests.Fixture("首頁"), 1.0, lobby: true);
        Assert.Equal(TitleArea, title.Title);
        Assert.Null(title.CharSelect);

        var select = reader.Read(CardLayoutTests.Fixture("選角色"), 1.0, lobby: true);
        Assert.Equal(CharSelectArea, select.CharSelect);
        Assert.Null(select.Title);
    }

    [Theory]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("前往野外")]
    [InlineData("未完成的一局")]
    [InlineData("升級選卡2")]
    public void Read_NoTitleOrCharSelect_OnOtherScreens(string fixture)
    {
        var s = new ScreenReader(TitleLibrary()).Read(CardLayoutTests.Fixture(fixture), 1.0, lobby: true);

        Assert.Null(s.Title);
        Assert.Null(s.CharSelect);
    }

    [Fact]
    public void Read_TitleTemplatesAreOptional_NotListedAsMissing()
    {
        var s = new ScreenReader(Library()).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.DoesNotContain(ScreenTemplates.Title, s.Missing);
        Assert.DoesNotContain(ScreenTemplates.CharSelect, s.Missing);
    }
}
