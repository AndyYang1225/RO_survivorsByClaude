using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class BallFinderTests
{
    [Fact]
    public void Find_FestivalBall_FoundNearItsCenter()
    {
        // 野外6扭蛋：角色右下的珊瑚粉紅扭蛋，中心約 (1005,362)、直徑約 18 px
        var balls = BallFinder.Find(CardLayoutTests.Fixture("野外6扭蛋"), 1.0);

        var b = Assert.Single(balls);
        Assert.Equal("粉紅", b.Kind);
        Assert.InRange(b.X, 1005 - 6, 1005 + 6);
        Assert.InRange(b.Y, 362 - 6, 362 + 6);
        // 點球腳下（往下 8 px）
        Assert.Equal(b.X / 1904.0, b.Click.X, 3);
        Assert.Equal((b.Y + 8) / 682.0, b.Click.Y, 3);
    }

    [Theory]
    [InlineData("撿取倒數")]
    [InlineData("野外5傍晚")]
    [InlineData("野外7夜晚")]
    [InlineData("野外1")]
    [InlineData("野外3錢不夠")]
    [InlineData("野外4")]
    public void Find_LanternsHudAndEffects_NotBalls(string fixture)
    {
        Assert.Empty(BallFinder.Find(CardLayoutTests.Fixture(fixture), 1.0));
    }

    [Fact]
    public void Find_Field2_OnlyLightningSparklesAsWhite()
    {
        // 已知限制：野外2寶箱 右側的閃電火花大小形狀跟扭蛋一樣，會被當成「白」；燈籠色、HUD、經驗寶石、傷害數字都不會
        var balls = BallFinder.Find(CardLayoutTests.Fixture("野外2寶箱"), 1.0);

        Assert.All(balls, b =>
        {
            Assert.Equal("白", b.Kind);
            Assert.True(b.X > 1500, $"{b}");
        });
    }

    [Theory]
    [InlineData(150, 170, 230, "白")]
    [InlineData(230, 140, 60, "橘")]
    [InlineData(200, 40, 40, "紅")]
    [InlineData(221, 142, 119, "粉紅")]
    [InlineData(60, 200, 80, "綠")]
    public void Find_SyntheticBall_EachColorRule(byte r, byte g, byte b, string kind)
    {
        var f = TestFrames.Solid(1904, 682, 30, 30, 40);
        TestFrames.Fill(f, new Rectangle(1300, 400, 18, 18), r, g, b);

        var ball = Assert.Single(BallFinder.Find(f, 1.0));

        Assert.Equal(kind, ball.Kind);
        Assert.InRange(ball.X, 1305, 1313);
        Assert.InRange(ball.Y, 405, 413);
    }

    [Fact]
    public void Find_SortedByDistanceFromCenter()
    {
        var f = TestFrames.Solid(1904, 682, 30, 30, 40);
        TestFrames.Fill(f, new Rectangle(1500, 400, 18, 18), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(1100, 400, 18, 18), 60, 200, 80);

        var balls = BallFinder.Find(f, 1.0);

        Assert.Equal(["綠", "粉紅"], balls.Select(b => b.Kind));
    }

    [Fact]
    public void Find_TooSmallTooBigOrStretched_Ignored()
    {
        var f = TestFrames.Solid(1904, 682, 30, 30, 40);
        // 雜點、燈籠大小、細長的光束
        TestFrames.Fill(f, new Rectangle(500, 400, 6, 6), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(700, 400, 45, 45), 200, 40, 40);
        TestFrames.Fill(f, new Rectangle(1300, 400, 28, 10), 150, 170, 230);

        Assert.Empty(BallFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_LanternWithPinkRim_IgnoredAsOneBigBlob()
    {
        // 燈籠：紅色中心＋一圈粉紅邊緣；邊緣自己的大小像扭蛋，但跟中心連在一起是一整塊燈籠
        var f = TestFrames.Solid(1904, 682, 30, 30, 40);
        TestFrames.Fill(f, new Rectangle(700, 400, 45, 45), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(706, 406, 33, 33), 200, 40, 40);

        Assert.Empty(BallFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_InsideHudOrOnSelf_Ignored()
    {
        var f = TestFrames.Solid(1904, 682, 30, 30, 40);
        // 左上角色框、右上小地圖、下方技能列、自己的角色（畫面中心）
        TestFrames.Fill(f, new Rectangle(100, 40, 18, 18), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(1800, 100, 18, 18), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(940, 630, 18, 18), 221, 142, 119);
        TestFrames.Fill(f, new Rectangle(943, 330, 18, 18), 221, 142, 119);

        Assert.Empty(BallFinder.Find(f, 1.0));
    }

    [Fact]
    public void Find_Dpr2_ScalesSizes()
    {
        var f = TestFrames.Solid(3808, 1364, 30, 30, 40);
        TestFrames.Fill(f, new Rectangle(2600, 800, 36, 36), 221, 142, 119);

        var ball = Assert.Single(BallFinder.Find(f, 2.0));

        Assert.InRange(ball.X, 2610, 2626);
        Assert.InRange(ball.Y, 810, 826);
        // 往下 8 CSS px＝16 截圖像素
        Assert.Equal((ball.Y + 16) / 1364.0, ball.Click.Y, 3);
    }
}
