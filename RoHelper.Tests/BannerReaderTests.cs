using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class BannerReaderTests
{
    [Theory]
    [InlineData("月夜花：躲到鐘下！", 15_000)]
    [InlineData("躲 到 鐘 下 ！", 15_000)]
    [InlineData("鐘開啟了", 15_000)]
    [InlineData("快躲到下面", 15_000)]
    [InlineData("鐘被封印了！", 15_000)]
    [InlineData("太遲了，鐘已經響了", 4000)]
    [InlineData("躲到鐘下！太遲了", 4000)]
    [InlineData("月夜 、到鐘下 ,", 15_000)]
    [InlineData("月夜 躲到鐘", 15_000)]
    public void DodgeMs_Announcements(string text, int ms)
        => Assert.Equal(ms, BannerReader.DodgeMs(text));

    [Theory]
    [InlineData("")]
    [InlineData("下午11:45 [GMT+8]（6:03後）進行維護：伺服器將進行更新。")]
    [InlineData("太遲了")]
    public void DodgeMs_Others_Null(string text)
        => Assert.Null(BannerReader.DodgeMs(text));

    [Fact]
    public void Area_CenteredBelowTimer()
    {
        var a = BannerReader.Area(TestFrames.Solid(1904, 682, 0, 0, 0), 1.0);

        Assert.Equal(new Rectangle(532, 90, 840, 190), a);
    }

    [Fact]
    public async Task ReadAsync_SystemBanner_ReadButNoDodge()
    {
        // 野外2寶箱 上方的維護公告（x 742～1162、y 98～148）
        var ocr = OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error);
        var text = await new BannerReader(ocr).ReadAsync(CardLayoutTests.Fixture("野外2寶箱"), 1.0);

        Assert.Contains("伺服器", text);
        Assert.Null(BannerReader.DodgeMs(text));
    }
}
