using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class HpReaderTests
{
    [Theory]
    [InlineData("野外1")]
    [InlineData("野外4")]
    [InlineData("野外2寶箱")]
    public void Read_FullHp_NearlyOne(string fixture)
    {
        var hp = HpReader.Read(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(hp);
        Assert.InRange(hp.Value, 0.97, 1.0);
    }

    [Fact]
    public void Read_RightSixtyPercentEmpty_AboutFortyPercent()
    {
        // HP 條實測外框 x 94～310、y 43～58（裡面 x 95～309、y 44～57）；右邊 60% 塗成空條的深灰（實測下面那條空條的中間色 (69,75,89)）
        var f = CardLayoutTests.Fixture("野外1");
        TestFrames.Fill(f, Rectangle.FromLTRB(95 + (int)(215 * 0.4), 44, 310, 58), 69, 75, 89);

        var hp = HpReader.Read(f, 1.0);

        Assert.NotNull(hp);
        Assert.InRange(hp.Value, 0.35, 0.45);
    }

    [Fact]
    public void Read_SolidFrame_ReturnsNull()
    {
        Assert.Null(HpReader.Read(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0));
    }
}
