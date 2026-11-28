using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>範本認不到節慶主持人時的 OCR 備案（字串都是 2026-11-27 A 的截圖實際讀到的）</summary>
public sealed class FestivalOcrTests
{
    [Theory]
    [InlineData("與主持人交談", true)]
    [InlineData("中持人交談", true)]
    [InlineData("1|隊長交談", false)]
    [InlineData("一守護者交談", false)]
    [InlineData("", false)]
    public void IsHostPrompt(string text, bool expected) => Assert.Equal(expected, FestivalOcr.IsHostPrompt(text));

    [Theory]
    [InlineData("節慶主人", true)]
    [InlineData("節慶主持人", true)]
    [InlineData("遠征隊長", false)]
    [InlineData("寵物管理員", false)]
    public void IsHostLabel(string text, bool expected) => Assert.Equal(expected, FestivalOcr.IsHostLabel(text));

    [Fact]
    public void PromptArea_CoversButton()
    {
        // 實測 1904×681 按鈕約 x 865～1040、y 600～642，中心 (950, 621)
        var r = FestivalOcr.PromptArea(1904, 681, 1.0);
        Assert.True(r.Contains(950, 621));
        Assert.True(r.Left <= 870 && r.Right >= 1030);
    }
}
