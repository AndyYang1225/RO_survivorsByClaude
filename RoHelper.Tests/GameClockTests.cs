using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class GameClockTests
{
    [Fact]
    public void Now_NothingRead_Null()
    {
        Assert.Null(new GameClock().Now(1000));
    }

    [Fact]
    public void Now_ExtrapolatesBetweenReads()
    {
        var c = new GameClock();
        c.Read(100, 10_000);

        Assert.Equal(100, c.Now(10_500));
        Assert.Equal(102, c.Now(12_100));
        Assert.Equal("01:42", c.Text(12_100));
    }

    [Fact]
    public void Read_PausedClock_Accepted()
    {
        // 選卡時遊戲暫停：10 秒後還是讀到 101（比推算值少很多，但沒有比上次讀到的少）
        var c = new GameClock();
        c.Read(100, 10_000);
        c.Read(101, 20_000);

        Assert.Equal(101, c.Now(20_000));
        Assert.False(c.Countdown);
    }

    [Fact]
    public void Read_SingleMisreadLow_Ignored()
    {
        var c = new GameClock();
        c.Read(137, 10_000);
        c.Read(17, 11_000);
        c.Read(139, 12_000);

        Assert.Equal(139, c.Now(12_000));
        Assert.False(c.Countdown);
    }

    [Fact]
    public void Read_FirstReadTooHigh_ReanchorsOnConsistentReads()
    {
        // 第一次讀錯成 12:17，之後一直讀到 02:18、02:19 → 改用新的值
        var c = new GameClock();
        c.Read(737, 10_000);
        c.Read(138, 11_000);
        c.Read(139, 12_000);

        Assert.Equal(139, c.Now(12_000));
        Assert.False(c.Countdown);
    }

    [Fact]
    public void Read_FirstReadTooLow_ReanchorsOnConsistentReads()
    {
        var c = new GameClock();
        c.Read(17, 10_000);
        c.Read(138, 11_000);
        c.Read(139, 12_000);

        Assert.Equal(139, c.Now(12_000));
    }

    [Fact]
    public void Read_CountingDown_Countdown()
    {
        // MVP 出現後計時器變成倒數
        var c = new GameClock();
        c.Read(840, 10_000);
        c.Read(180, 11_000);
        c.Read(179, 12_000);

        Assert.True(c.Countdown);
        Assert.Null(c.Now(12_000));
        Assert.Equal("", c.Text(12_000));

        c.Read(900, 13_000);
        Assert.Null(c.Now(13_000));
    }

    [Fact]
    public void Loot_StopsClock_ResetRestarts()
    {
        var c = new GameClock();
        c.Read(100, 10_000);
        c.Loot();
        Assert.Null(c.Now(10_000));

        c.Reset();
        c.Read(5, 20_000);
        Assert.Equal(5, c.Now(20_000));
    }
}
