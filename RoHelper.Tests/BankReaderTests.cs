using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class BankReaderTests
{
    [Fact]
    public void Look_WhiteLabel_Ready()
    {
        Assert.Equal(BankLook.Ready, BankReader.Look(CardLayoutTests.Fixture("升級累積3"), 1.0));
    }

    [Fact]
    public void Look_GrayLabel_Empty()
    {
        Assert.Equal(BankLook.Empty, BankReader.Look(CardLayoutTests.Fixture("升級累積0"), 1.0));
    }

    /// <summary>立即模式的野外（含系統維護公告的白框：野外1、野外2寶箱）、結算、選卡、首頁、大廳都沒有標籤</summary>
    [Theory]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("野外3錢不夠")]
    [InlineData("野外4")]
    [InlineData("野外5傍晚")]
    [InlineData("野外6扭蛋")]
    [InlineData("野外7夜晚")]
    [InlineData("野外8寶箱像紅球")]
    [InlineData("撿取倒數")]
    [InlineData("結算存活1")]
    [InlineData("結算死亡")]
    [InlineData("升級選卡2")]
    [InlineData("升級選卡3")]
    [InlineData("寶箱選卡3")]
    [InlineData("首頁")]
    [InlineData("選角色")]
    [InlineData("活動升級累積")]
    [InlineData("大廳遠征隊長")]
    public void Look_NoLabel_Null(string fixture)
    {
        Assert.Null(BankReader.Look(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Fact]
    public void Look_SolidFrame_Null()
    {
        Assert.Null(BankReader.Look(TestFrames.Solid(1904, 682, 240, 240, 245), 1.0));
    }

    [Theory]
    [InlineData("升級累積3", 3)]
    [InlineData("升級累積0", 0)]
    [InlineData("野外4", null)]
    public async Task ReadAsync_Count(string fixture, int? expected)
    {
        var ocr = OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error);

        Assert.Equal(expected, await new BankReader(ocr).ReadAsync(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Theory]
    [InlineData("升 3 B", 3)]
    [InlineData("升級 12 B", 12)]
    [InlineData("升級 B", null)]
    public void ParseCount(string text, int? expected)
    {
        Assert.Equal(expected, BankReader.ParseCount(text));
    }
}
