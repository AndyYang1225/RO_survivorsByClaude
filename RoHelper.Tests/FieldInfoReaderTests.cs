using RoHelper.Engine;
using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class FieldInfoReaderTests
{
    private static readonly RoHelper.Bridge.EngineSchema Schema = BuiltInSchema.Load();
    private static readonly List<string> Jobs = Schema.JobSkills.Select(j => j.Key).ToList();

    private static FieldInfoReader Reader()
        => new(OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error), Jobs, Schema.Stages);

    [Theory]
    [InlineData("野外1", "洛陽", "魔法師")]
    [InlineData("野外4", "洛陽", "魔法師")]
    [InlineData("結算存活2", "洛陽", "巫師")]
    [InlineData("結算死亡", "洛陽", "魔法師")]
    public async Task ReadAsync_ReadsStageAndJob(string fixture, string stage, string job)
    {
        var info = await Reader().ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.Equal(stage, info.Stage);
        Assert.Equal(job, info.Job);
    }

    [Theory]
    // 座標只是記下來（還沒拿來用），讀得到的才驗；野外4 實測投票結果是錯的 (5, 2)，所以不列
    [InlineData("野外1", 136, 107)]
    [InlineData("結算存活2", 153, 174)]
    public async Task ReadAsync_ReadsTile(string fixture, int x, int y)
    {
        Assert.Equal(new Point(x, y), (await Reader().ReadAsync(CardLayoutTests.Fixture(fixture), 1.0)).Tile);
    }

    [Fact]
    public async Task ReadAsync_NoFrame_ReadsNothing()
    {
        var info = await Reader().ReadAsync(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0);

        Assert.Equal(new FieldInfo("", null, ""), info);
    }

    [Theory]
    [InlineData("野外1", 175, 212)]
    [InlineData("結算存活2", 182, 209)]
    public void LastWord_FindsJobAfterSpace(string fixture, int left, int right)
    {
        var word = FieldInfoReader.LastWord(CardLayoutTests.Fixture(fixture), new Rectangle(86, 14, 244, 20), 1.0);

        Assert.NotNull(word);
        Assert.InRange(word.Value.Left, left - 2, left + 2);
        Assert.InRange(word.Value.Right, right - 2, right + 2);
    }

    [Theory]
    // 實測 OCR 輸出
    [InlineData("陷 ( 136 、 107 )", "洛陽", 136, 107)]
    [InlineData("一陷 ( 1t4 , 94 )", "洛陽", 114, 94)]
    [InlineData("陷緆 ( 153 , 174 )", "洛陽", 153, 174)]
    [InlineData("斐揚洞 ( 10 , 20 )", "斐揚洞穴", 10, 20)]
    public void ParseTitle_StageAndTile(string text, string stage, int x, int y)
    {
        Assert.Equal((stage, (Point?)new Point(x, y)), FieldInfoReader.ParseTitle(text, Schema.Stages));
    }

    [Theory]
    [InlineData("陷 ( 136 、 10")]
    [InlineData("( 163 41 )")]
    public void ParseTitle_IncompleteTile_IsNull(string text)
    {
        Assert.Null(FieldInfoReader.ParseTitle(text, Schema.Stages).Tile);
    }

    [Theory]
    [InlineData("3廗法", "魔法師")]
    [InlineData("用巫", "巫師")]
    [InlineData("沒用巫", "巫師")]
    [InlineData("舞孃", "吟遊詩人/舞孃")]
    [InlineData("師", null)]
    [InlineData("人", null)]
    [InlineData("Job", null)]
    public void JobNames_Match(string ocr, string? job)
    {
        Assert.Equal(job, JobNames.Match(ocr, Jobs));
    }
}
