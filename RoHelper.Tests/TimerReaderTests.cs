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
}
