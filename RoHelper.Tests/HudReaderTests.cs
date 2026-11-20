using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class HudReaderTests
{
    [Theory]
    [InlineData("野外3錢不夠", 1537, 3009)]
    [InlineData("野外4", 36, 30)]
    [InlineData("野外2寶箱", 0, 30)]
    public async Task ReadAsync_ReadsMoneyAndChestCost(string fixture, int money, int cost)
    {
        var reader = new HudReader(OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error));

        Assert.Equal(new Hud(money, cost), await reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Fact]
    public async Task ReadAsync_NoHud_ReturnsNull()
    {
        var reader = new HudReader(OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error));

        Assert.Null(await reader.ReadAsync(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0));
    }

    [Theory]
    // 實測 OCR 輸出（擊殺數和金錢是不同行，用空白接起來）
    [InlineData("6738 1537 z 寶 3009 ~", 1537, 3009)]
    [InlineData("29 ~ 36z 寶 30 ~", 36, 30)]
    [InlineData("45 0 z 30 z", 0, 30)]
    [InlineData("1,537 z 寶箱 3,009 z", 1537, 3009)]
    [InlineData("12 1537Z 寶箱 3009 Z", 1537, 3009)]
    public void Parse_MoneyIsNumberBeforeZ_CostIsNextNumber(string text, int money, int cost)
    {
        Assert.Equal(new Hud(money, cost), HudReader.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("6738")]
    [InlineData("6738 1537 z")]
    [InlineData("寶箱 3009")]
    public void Parse_Incomplete_ReturnsNull(string text)
    {
        Assert.Null(HudReader.Parse(text));
    }
}
