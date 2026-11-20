using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class TimerReaderTests
{
    [Fact]
    public void Read_RedCountdown_Loot()
    {
        Assert.Equal(TimerPhase.Loot, TimerReader.Read(CardLayoutTests.Fixture("撿取倒數"), 1.0));
    }

    [Theory]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("野外3錢不夠")]
    [InlineData("野外4")]
    [InlineData("野外5傍晚")]
    [InlineData("野外6扭蛋")]
    [InlineData("野外7夜晚")]
    public void Read_NormalTimer_Normal(string fixture)
    {
        Assert.Equal(TimerPhase.Normal, TimerReader.Read(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Theory]
    [InlineData("結算存活1")]
    [InlineData("首頁")]
    [InlineData("選角色")]
    [InlineData("升級選卡2")]
    public void Read_NoTimer_Null(string fixture)
    {
        Assert.Null(TimerReader.Read(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Fact]
    public void Read_SolidFrame_Null()
    {
        Assert.Null(TimerReader.Read(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0));
    }

    /// <summary>實測：放大 2 倍讀計時器，這幾張全部讀對（截圖上看得到的數字）</summary>
    [Theory]
    [InlineData("升級累積0", 35)]
    [InlineData("升級累積3", 137)]
    [InlineData("野外3錢不夠", 703)]
    [InlineData("野外5傍晚", 754)]
    [InlineData("野外8寶箱像紅球", 519)]
    public async Task ReadClockAsync_ReadsSeconds(string fixture, int secs)
    {
        var ocr = OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error);

        Assert.Equal(secs, await TimerReader.ReadClockAsync(ocr, CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Theory]
    [InlineData("02 : 17", 137)]
    [InlineData("12:34", 754)]
    [InlineData("O8 ： 39", 519)]
    [InlineData("00.35", 35)]
    [InlineData(": 43", null)]
    [InlineData("02 : 75", null)]
    [InlineData("", null)]
    public void ParseClock(string text, int? secs)
    {
        Assert.Equal(secs, TimerReader.ParseClock(text));
    }
}
