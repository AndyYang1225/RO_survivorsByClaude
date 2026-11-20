using RoHelper.Engine;
using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class ResultReaderTests
{
    private static readonly List<string> Jobs = BuiltInSchema.Load().JobSkills.Select(j => j.Key).ToList();

    private static ResultReader Reader()
        => new(OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error), Jobs);

    [Theory]
    [InlineData("結算存活1", "你存活下來了！", "clear", "魔法師", "14:20", 860, 57, 9254, 7)]
    [InlineData("結算存活2", "你存活下來了！", "clear", "巫師", "14:18", 858, 58, 9214, 7)]
    [InlineData("結算死亡", "你死了", "death", "魔法師", "7:10", 430, 29, 3272, 2)]
    public async Task ReadAsync_ReadsTitleAndFiveChips(string fixture, string title, string outcome, string job, string time, int secs, int level, int kills, int bosses)
    {
        var r = await Reader().ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(r);
        Assert.Equal(title, r.Title);
        Assert.Equal(outcome, r.Outcome);
        Assert.Equal(job, r.Job);
        Assert.Equal(time, r.Time);
        Assert.Equal(secs, r.Secs);
        Assert.Equal(level, r.Level);
        Assert.Equal(kills, r.Kills);
        Assert.Equal(bosses, r.Bosses);
    }

    [Theory]
    [InlineData("結算存活1")]
    [InlineData("結算存活2")]
    [InlineData("結算死亡")]
    public async Task ReadAsync_KeepsExpAndDpsText(string fixture)
    {
        var r = await Reader().ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(r);
        Assert.Contains("Lv", r.Exp);
        Assert.Contains("DPS", r.DpsTotal);
        Assert.InRange(r.Dps.Count, 5, 6);
    }

    [Fact]
    public async Task ReadAsync_SolidFrame_ReturnsNull()
    {
        Assert.Null(await Reader().ReadAsync(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0));
    }

    [Theory]
    [InlineData("你存活下來了！", "clear")]
    [InlineData("你死了", "death")]
    [InlineData("放棄了本局", "abandoned")]
    [InlineData("遊戲結束", "ended")]
    [InlineData("???", "unknown")]
    public void OutcomeOf_SameAsJs(string title, string outcome)
    {
        Assert.Equal(outcome, ResultReader.OutcomeOf(title));
    }

    [Theory]
    [InlineData("你存活下來了 !", "你存活下來了！")]
    [InlineData("◆ 你死了", "你死了")]
    public void CleanTitle_RemovesSpacesAndMarker(string raw, string title)
    {
        Assert.Equal(title, ResultReader.CleanTitle(raw));
    }

    [Theory]
    // 實測 OCR 讀錯的樣子
    [InlineData(1, "14 : 20", "14:20")]
    [InlineData(1, "7 : IO", "7:10")]
    [InlineData(1, "7 0", null)]
    [InlineData(1, "740", null)]
    [InlineData(3, "92t4", "9214")]
    [InlineData(3, "92 4", null)]
    [InlineData(3, "92R4", null)]
    [InlineData(2, "57", "57")]
    [InlineData(0, "法師", "魔法師")]
    [InlineData(0, "巫師", "巫師")]
    [InlineData(0, "Job", null)]
    public void Normalize_ToleratesKnownMisreads(int chip, string raw, string? value)
    {
        Assert.Equal(value, new ResultReader(null!, Jobs).Normalize(chip, raw));
    }

    [Fact]
    public void Winner_MostVotesThenFirst()
    {
        Assert.Equal("14:20", ResultReader.Winner(["4:20", "14:20", "14:20"]));
        Assert.Equal("57", ResultReader.Winner(["57", "51"]));
        Assert.Null(ResultReader.Winner([]));
    }
}
