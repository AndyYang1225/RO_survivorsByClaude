using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

/// <summary>
/// 實測（2026-11-21 斐揚洞穴遠征，月夜貓）：MVP 戰鬥中計時器也是紅字倒數，被當成撿取倒數而關掉躲鐘；
/// 「躲到鐘下！」在畫面中央 y 220，公告範圍沒涵蓋；洞穴的鐘是暗金色，JS 的亮金規則找不到。角色連續好幾局在 14:0x 被鐘秒殺。
/// </summary>
public class MvpFightTests
{
    [Theory]
    [InlineData("MVP出現", TimerPhase.Mvp)]
    [InlineData("躲到鐘下", TimerPhase.Mvp)]
    [InlineData("洞穴的鐘", TimerPhase.Mvp)]
    [InlineData("撿取倒數", TimerPhase.Loot)]
    [InlineData("野外1", TimerPhase.Normal)]
    [InlineData("洞穴升級17", TimerPhase.Normal)]
    public void Timer_MvpBarSeparatesFightFromLootCountdown(string fixture, TimerPhase phase)
        => Assert.Equal(phase, TimerReader.Read(CardLayoutTests.Fixture(fixture), 1.0));

    [Theory]
    [InlineData("躲鐘下 !")]
    [InlineData("躲到钀下 !")]
    [InlineData("躲到鐘下 ·")]
    public void Banner_OcrVariantsOfHideUnderBell_Trigger(string text)
        => Assert.Equal(15_000, BannerReader.DodgeMs(text));

    [Fact]
    public async Task Banner_CenterBigText_IsRead()
    {
        var text = await new BannerReader(OcrReader.TryCreate(out _)!).ReadAsync(CardLayoutTests.Fixture("MVP出現"), 1.0);

        Assert.Contains("出現", text);
    }

    [Fact]
    public void Bell_DimGoldInCave_Found()
    {
        var bell = BellFinder.Find(CardLayoutTests.Fixture("洞穴的鐘"), 1.0);

        Assert.NotNull(bell);
        Assert.InRange(bell.X, 1300, 1360);
        Assert.InRange(bell.Y1, 320, 460);
    }

    /// <summary>洛陽的燈籠、地面偶爾會被暗金規則當成小鐘（不會遇到：只有喊「躲到鐘下」後 15 秒才找鐘，洛陽沒有會喊的王）；這兩張不能有</summary>
    [Theory]
    [InlineData("撿取倒數")]
    [InlineData("野外7夜晚")]
    public void Bell_NotFoundOnOtherScreens(string fixture)
        => Assert.Null(BellFinder.Find(CardLayoutTests.Fixture(fixture), 1.0));
}
