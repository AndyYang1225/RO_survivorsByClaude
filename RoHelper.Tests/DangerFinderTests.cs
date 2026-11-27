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

/// <summary>條紋警示（實測 2026-11-27 洛陽：邪骸浪人的斜條紋、舞獅的直條紋）</summary>
public class StripeFinderTests
{
    /// <summary>直條紋：帶子約 x 350～415、505～575、665～730（填色）；中間是空地</summary>
    [Fact]
    public void FindStripes_Vertical()
    {
        var m = DangerFinder.FindStripes(CardLayoutTests.Fixture("王攻擊直條紋"), 1.0);

        Assert.NotNull(m);
        Assert.True(m.Bands >= 3, $"bands {m.Bands}");
        Assert.True(m.Hit(385, 450));
        Assert.True(m.Hit(540, 450));
        Assert.False(m.Hit(465, 450));
        Assert.False(m.Hit(620, 450));
    }

    /// <summary>斜條紋：y 340 這一列帶子在 x 608～664、808～872、1024～1088；空地在 680～800</summary>
    [Fact]
    public void FindStripes_Diagonal()
    {
        var m = DangerFinder.FindStripes(CardLayoutTests.Fixture("王攻擊斜條紋"), 1.0);

        Assert.NotNull(m);
        Assert.True(m.Bands >= 3, $"bands {m.Bands}");
        Assert.True(m.Hit(636, 340));
        Assert.True(m.Hit(840, 340));
        Assert.False(m.Hit(740, 340));
    }

    [Theory]
    [InlineData("洛陽白天地板")]
    [InlineData("洛陽夜晚MVP")]
    [InlineData("野外1")]
    [InlineData("野外4")]
    [InlineData("野外5傍晚")]
    [InlineData("野外7夜晚")]
    [InlineData("野外8寶箱像紅球")]
    [InlineData("野外6扭蛋")]
    [InlineData("洞穴升級17")]
    [InlineData("MVP出現")]
    [InlineData("王攻擊紅圈")]
    [InlineData("王攻擊粉紅圓")]
    [InlineData("鐘的紅圈")]
    [InlineData("洞穴的鐘")]
    [InlineData("躲到鐘下")]
    public void FindStripes_NoStripes_Null(string fixture)
    {
        Assert.Null(DangerFinder.FindStripes(CardLayoutTests.Fixture(fixture), 1.0));
    }
}

public class StripeGridTests
{
    /// <summary>橫條紋＋直線的格子（舞獅）：橫條約 y 345～395（中間被石台擋住），左邊 x &lt; 480 沒有格子</summary>
    [Fact]
    public void FindStripes_HorizontalGrid()
    {
        var m = DangerFinder.FindStripes(CardLayoutTests.Fixture("王攻擊橫條紋"), 1.0);

        Assert.NotNull(m);
        Assert.True(m.Hit(650, 370));
        Assert.True(m.Hit(1500, 240));
        Assert.False(m.Hit(250, 450));
    }
}

public class StripeSpriteTests
{
    /// <summary>角色站在兩條橫帶中間（紅衣服碰到上面那條帶子）：腳下不算在條紋上，帶子本身有抓到</summary>
    [Theory]
    [InlineData("王攻擊橫條紋2", 700, 300)]

    public void FindStripes_PlayerBetweenBands_FeetFree(string fixture, int bandX, int bandY)
    {
        var m = DangerFinder.FindStripes(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(m);
        Assert.True(m.Hit(bandX, bandY));
        Assert.False(m.Hit(952, 356));
    }
}

public class StripeGridLineTests
{
    /// <summary>格子（舞獅：橫帶＋發光的直線）：直線正好穿過角色，算在條紋上</summary>
    [Fact]
    public void FindStripes_Grid_LineThroughPlayer()
    {
        var m = DangerFinder.FindStripes(CardLayoutTests.Fixture("王攻擊格子"), 1.0);

        Assert.NotNull(m);
        Assert.True(m.Hit(700, 285));
        Assert.True(m.Hit(952, 356));
    }
}
