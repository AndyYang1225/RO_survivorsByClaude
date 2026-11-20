using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

/// <summary>還沒有真實的鐘截圖：鐘用合成畫面（JS 註解的金色 (1.0, 0.8, 0.42) ≈ (255,204,107)），燈籠、HUD、特效用真實 fixture</summary>
public class BellFinderTests
{
    private static Frame Ground() => TestFrames.Solid(1904, 682, 60, 55, 50);

    [Theory]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("野外3錢不夠")]
    [InlineData("野外4")]
    [InlineData("野外5傍晚")]
    [InlineData("野外6扭蛋")]
    [InlineData("野外7夜晚")]
    [InlineData("撿取倒數")]
    public void Find_RealFieldWithLanternsHudAndEffects_NoBell(string fixture)
        => Assert.Null(BellFinder.Find(CardLayoutTests.Fixture(fixture), 1.0));

    [Fact]
    public void Find_GoldBell_BottomCenter()
    {
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(1300, 300, 100, 90), 255, 204, 107);

        var b = BellFinder.Find(f, 1.0);

        Assert.NotNull(b);
        Assert.InRange(b.X, 1345, 1355);
        Assert.InRange(b.Y, 384, 390);
        Assert.InRange(b.Width, 92, 100);
        Assert.Equal(b.N, b.Gold);
    }

    [Fact]
    public void Find_BellOverCharacter_NotExcluded()
    {
        // 站在鐘底下時鐘蓋住畫面中心（自己的角色），不能像扭蛋那樣排除
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(900, 280, 100, 90), 255, 204, 107);

        Assert.NotNull(BellFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_TakesLargest()
    {
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(400, 300, 60, 60), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(1300, 300, 100, 90), 255, 204, 107);

        Assert.InRange(BellFinder.Find(f, 1.0)!.X, 1340, 1360);
    }

    [Fact]
    public void Find_GoldInsideHud_Ignored()
    {
        var f = Ground();
        // 左上角色框、上方計時器、系統公告、右上小地圖、下方技能列
        TestFrames.Fill(f, new Rectangle(100, 10, 80, 80), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(900, 5, 80, 80), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(800, 100, 300, 50), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(1760, 40, 120, 150), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(830, 560, 240, 120), 255, 204, 107);

        Assert.Null(BellFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_HollowGoldRing_Ignored()
    {
        // 野外5傍晚 點亮的燈籠外圈光環：金色、很大，但是空心
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(400, 200, 100, 80), 255, 204, 107);
        TestFrames.Fill(f, new Rectangle(404, 204, 92, 72), 60, 55, 50);

        Assert.Null(BellFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_TooSmall_Ignored()
    {
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(1300, 300, 16, 16), 255, 204, 107);

        Assert.Null(BellFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_RedLanternSized_IgnoredButBigSealedBellFound()
    {
        var f = Ground();
        TestFrames.Fill(f, new Rectangle(500, 300, 40, 40), 230, 60, 50);
        Assert.Null(BellFinder.Find(f, 1.0));

        TestFrames.Fill(f, new Rectangle(1300, 300, 100, 90), 230, 60, 50);
        var b = BellFinder.Find(f, 1.0);
        Assert.NotNull(b);
        Assert.Equal(b.N, b.Red);
    }

    [Fact]
    public void Find_SmallRedWhereGoldBellWas_FoundAsSealedBell()
    {
        var gold = Ground();
        TestFrames.Fill(gold, new Rectangle(1300, 300, 48, 48), 255, 204, 107);
        var last = BellFinder.Find(gold, 1.0);
        Assert.NotNull(last);

        var red = Ground();
        TestFrames.Fill(red, new Rectangle(1302, 300, 48, 48), 230, 60, 50);
        Assert.Null(BellFinder.Find(red, 1.0));
        Assert.NotNull(BellFinder.Find(red, 1.0, last));

        // 離上次的鐘很遠的紅色（燈籠）還是不算
        var far = Ground();
        TestFrames.Fill(far, new Rectangle(500, 300, 48, 48), 230, 60, 50);
        Assert.Null(BellFinder.Find(far, 1.0, last));
    }

    [Fact]
    public void Find_Dpr2_SameSampleCount()
    {
        var f = TestFrames.Solid(3808, 1364, 60, 55, 50);
        TestFrames.Fill(f, new Rectangle(2600, 600, 200, 180), 255, 204, 107);

        var b = BellFinder.Find(f, 2.0);

        Assert.NotNull(b);
        Assert.InRange(b.X, 2690, 2710);
    }

    [Fact]
    public void Scan_IsCheap()
    {
        var f = CardLayoutTests.Fixture("野外5傍晚");
        BellFinder.Scan(f, 1.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
            BellFinder.Scan(f, 1.0);
        Assert.True(sw.Elapsed.TotalMilliseconds / 20 < 10, $"{sw.Elapsed.TotalMilliseconds / 20} ms");
    }
}
