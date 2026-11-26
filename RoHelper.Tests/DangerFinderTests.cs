using RoHelper.Vision.Field;

namespace RoHelper.Tests;

/// <summary>王的攻擊警示範圍（實測 2026-11-27 斐揚洞穴月夜貓）</summary>
public class DangerFinderTests
{
    /// <summary>紅色圓環：王在 (1240,330) 附近，圓環外框約 x 1000～1180、y 280～420</summary>
    [Fact]
    public void Find_RedRing()
    {
        var zones = DangerFinder.Find(CardLayoutTests.Fixture("王攻擊紅圈"), 1.0);

        var z = Assert.Single(zones);
        Assert.True(z.Contains(1080, 350));
        // 角色（畫面中心）不在範圍裡
        Assert.False(z.Contains(952, 356));
    }

    /// <summary>粉紅色實心圓：約 x 920～1130、y 320～500</summary>
    [Fact]
    public void Find_PinkDisc()
    {
        var zones = DangerFinder.Find(CardLayoutTests.Fixture("王攻擊粉紅圓"), 1.0);

        var z = Assert.Single(zones);
        Assert.True(z.Contains(1020, 420));
        Assert.True(z.Contains(940, 420));
    }

    /// <summary>扇形（月夜貓：火焰扇）：紅框＋半透明紅，角色（畫面中心）站在裡面</summary>
    [Fact]
    public void Find_Fan_PlayerInside()
    {
        var zones = DangerFinder.Find(CardLayoutTests.Fixture("王攻擊扇形"), 1.0);

        Assert.Contains(zones, z => z.Contains(952, 356));
    }

    /// <summary>鐘的大紅圈（鐘在畫面上，或被切成左右兩條弧）不算攻擊警示</summary>
    [Theory]
    [InlineData("鐘的紅圈")]
    [InlineData("躲到鐘下")]
    [InlineData("洞穴的鐘")]
    [InlineData("鐘開啟開始")]
    public void Find_BellRing_NotDanger(string fixture)
    {
        Assert.Empty(DangerFinder.Find(CardLayoutTests.Fixture(fixture), 1.0));
    }

    /// <summary>沒有攻擊警示：紅燈籠、傷害數字、血條、傍晚的紅光都不算</summary>
    [Theory]
    [InlineData("野外1")]
    [InlineData("野外4")]
    [InlineData("野外5傍晚")]
    [InlineData("野外7夜晚")]
    [InlineData("野外8寶箱像紅球")]
    [InlineData("野外6扭蛋")]
    [InlineData("洞穴升級17")]
    [InlineData("MVP出現")]
    public void Find_NoWarning_Empty(string fixture)
    {
        Assert.Empty(DangerFinder.Find(CardLayoutTests.Fixture(fixture), 1.0));
    }
}
