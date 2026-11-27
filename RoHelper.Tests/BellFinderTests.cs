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

    /// <summary>
    /// 實測（2026-11-26 斐揚洞穴 月夜貓，MVP 戰鬥中的診斷截圖）：公告沒認出來、沒躲鐘被秒殺。
    /// 鐘在畫面上（012815 鐘在 x 930～1150、012819 鐘在 x 1380～1600），要找得到才能靠「看到鐘」開始躲
    /// </summary>
    [Theory]
    [InlineData("MVP鐘-012815", 930, 1160)]
    [InlineData("MVP鐘-012819", 1370, 1610)]
    [InlineData("MVP鐘-012822", 1000, 1240)]
    public void Find_RealMvpBell_IsBigBell(string fixture, int x0, int x1)
    {
        var b = BellFinder.Find(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(b);
        Assert.InRange(b.X, x0, x1);
        Assert.True(BellFinder.IsBigBell(b, 1.0));
    }

    /// <summary>MVP 剛出現、鐘還沒放的畫面（012810，只有火盆）：不能當成大鐘（「看到鐘就躲」會跑去火盆）</summary>
    [Fact]
    public void Find_MvpBeforeBell_NoBigBell()
    {
        var b = BellFinder.Find(CardLayoutTests.Fixture("MVP鐘-012810"), 1.0);

        Assert.True(b is null || !BellFinder.IsBigBell(b, 1.0));
    }

    /// <summary>
    /// 實測（2026-11-26 斐揚洞穴第 2 局）：喊躲鐘時鐘在畫面最上緣、被閃電特效切開（015209、015212），
    /// 要認得出被切掉的鐘，不能把地上的火焰特效（x 1008～1096、y 320～392）當成鐘
    /// </summary>
    [Theory]
    [InlineData("MVP鐘-015209", 1250, 1395)]
    [InlineData("MVP鐘-015212", 1190, 1395)]
    [InlineData("MVP鐘-015215", 1060, 1280)]
    // 第 3 局：鐘在畫面正下方被技能列擋住（021806、021810，只露出鐘頂），畫面上緣有火盆；021813 鐘在左下貼著畫面底邊
    [InlineData("MVP鐘-021806", 850, 980)]
    [InlineData("MVP鐘-021810", 820, 940)]
    [InlineData("MVP鐘-021813", 650, 780)]
    public void Find_BellCutByScreenTop(string fixture, int x0, int x1)
    {
        var b = BellFinder.Find(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(b);
        Assert.InRange(b.X, x0, x1);
    }

    /// <summary>火盆的光（012819 左上、中上兩個）符合一般的鐘規則，但不是大鐘</summary>
    [Fact]
    public void Braziers_AreNotBigBells()
    {
        var blobs = BellFinder.Scan(CardLayoutTests.Fixture("MVP鐘-012819"), 1.0).Where(x => x.X1 < 1200 && x.N >= 100).ToList();

        Assert.NotEmpty(blobs);
        Assert.All(blobs, x => Assert.False(BellFinder.IsBigBell(x, 1.0)));
    }

    /// <summary>
    /// 鐘口＝鐘身底部（要站的地方），不能被連在一起的火盆、圓盤撐偏。
    /// 實測（2026-11-26 斐揚洞穴）：105343 外框連著右下的火盆，用外框算目標在 (958,373) 圓盤邊緣，站那裡被鐘打死；
    /// 鐘身在 x 880～985、鐘口 y 約 250。095616、103549 是躲成功時的鐘
    /// </summary>
    [Theory]
    [InlineData("MVP鐘-105343", 900, 950, 240, 300)]
    [InlineData("MVP鐘-095616", 935, 985, 295, 345)]
    [InlineData("MVP鐘-103549", 965, 1015, 340, 390)]
    [InlineData("MVP鐘-012815", 1025, 1075, 370, 420)]
    public void Find_Mouth_IsUnderBellBody(string fixture, int x0, int x1, int y0, int y1)
    {
        var b = BellFinder.Find(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(b);
        Assert.InRange(b.Mouth.X, x0, x1);
        Assert.InRange(b.Mouth.Y, y0, y1);
    }

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

/// <summary>鐘在上方計時器後面（實測 2026-11-27 sabc1228 斐揚洞穴：只露出底座圓盤下半圈，x 816～1030、y 0～132）</summary>
public class BellBehindHudTests
{
    [Theory]
    [InlineData("鐘在計時器後0s", 937)]
    [InlineData("鐘在計時器後4s", 911)]
    [InlineData("鐘在計時器後7s", 910)]
    public void FindBehindHud_DiscAboveTimer_WalkUpBelowMvpBar(string fixture, int x)
    {
        var f = CardLayoutTests.Fixture(fixture);

        Assert.Null(BellFinder.Find(f, 1.0));
        var b = BellFinder.FindBehindHud(f, 1.0);

        Assert.NotNull(b);
        Assert.InRange(b.Mouth.X, x - 15, x + 15);
        Assert.Equal(165, b.Mouth.Y);
    }

    /// <summary>沒有鐘：大廳的隊伍面板、野外草地、洛陽上方的土牆都不算</summary>
    [Theory]
    [InlineData("隊伍面板3人")]
    [InlineData("大廳遠征隊長")]
    [InlineData("野外1")]
    [InlineData("野外2寶箱")]
    [InlineData("野外4")]
    [InlineData("洛陽白天地板")]
    [InlineData("洛陽夜晚MVP")]
    [InlineData("躲到鐘下")]
    [InlineData("MVP鐘-015215")]
    [InlineData("升級累積0")]
    public void FindBehindHud_NoDisc_Null(string fixture)
    {
        Assert.Null(BellFinder.FindBehindHud(CardLayoutTests.Fixture(fixture), 1.0));
    }
}
