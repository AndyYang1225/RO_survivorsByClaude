using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class TreasureReaderTests
{
    private static OcrReader Ocr() => OcrReader.TryCreate(out var error) ?? throw new InvalidOperationException(error);

    [Fact]
    public void SlotCenter_CenteredOnScreen_FromBottom()
    {
        var f = TestFrames.Solid(1904, 682, 0, 0, 0);

        Assert.Equal(new PointF(880, 580), TreasureReader.SlotCenter(f, 1.0, 0));
        Assert.Equal(new PointF(1024, 580), TreasureReader.SlotCenter(f, 1.0, 4));
    }

    [Theory]
    [InlineData("道具說明框", 5)]
    [InlineData("野外3錢不夠", 5)]
    [InlineData("野外6扭蛋", 5)]
    [InlineData("撿取倒數", 5)]
    [InlineData("野外1", 0)]
    [InlineData("野外2寶箱", 0)]
    [InlineData("野外4", 0)]
    [InlineData("升級累積0", 0)]
    [InlineData("前往野外", 0)]
    public void FilledSlots_WhiteIconsCount_GrayEmptyDoNot(string fixture, int count)
    {
        Assert.Equal(count, TreasureReader.FilledSlots(CardLayoutTests.Fixture(fixture), 1.0).Count);
    }

    [Fact]
    public void FindTip_Fixture_BoxAboveFirstSlot()
    {
        var f = CardLayoutTests.Fixture("道具說明框");

        var box = TreasureReader.FindTip(f, 1.0, 0);

        // 實測說明框 x 795～966、y 476～553（外框各 1 px 不算）
        Assert.NotNull(box);
        Assert.InRange(box.Box.Left, 794, 799);
        Assert.InRange(box.Box.Right, 960, 968);
        Assert.InRange(box.Box.Top, 475, 479);
        Assert.InRange(box.Box.Bottom, 550, 555);
        Assert.InRange(box.BodyTop, 521, 525);
    }

    [Theory]
    [InlineData("道具說明框", 1)]
    [InlineData("道具說明框", 4)]
    [InlineData("野外3錢不夠", 0)]
    [InlineData("野外1", 0)]
    [InlineData("野外2寶箱", 2)]
    public void FindTip_NoTooltipAboveSlot_ReturnsNull(string fixture, int slot)
    {
        Assert.Null(TreasureReader.FindTip(CardLayoutTests.Fixture(fixture), 1.0, slot));
    }

    [Fact]
    public async Task ReadTipAsync_Fixture_RangePotionIsNotTreasure()
    {
        var f = CardLayoutTests.Fixture("道具說明框");

        var tip = await new TreasureReader(Ocr()).ReadTipAsync(f, TreasureReader.FindTip(f, 1.0, 0)!, 1.0);

        // 實測讀成「範藥水 +0」（「圍」太小常漏掉）；「+18% 範圍」不是尋寶率
        Assert.Contains("藥水", tip.Head);
        Assert.Equal(0, tip.Treasure);
    }

    [Theory]
    [InlineData("+5% 尋寶率", 5)]
    [InlineData("+ 5 % 尋寶率", 5)]
    [InlineData("+12.5 % 尋寶率|+10%尋寶率", 22.5)]
    [InlineData("+8％ 尋寶率", 8)]
    // OCR 漏字：只讀到「寶」或「尋」也算
    [InlineData("+ 6 % 寶率", 6)]
    [InlineData("+ 6 % 尋", 6)]
    // 實測「+18% 範圍」的 OCR 結果：不是尋寶率
    [InlineData("+ 18 % 範", 0)]
    [InlineData("+18% 範圍|+3% 攻擊", 0)]
    public void Parse_SumsTreasurePercent(string body, double sum)
    {
        var tip = TreasureReader.Parse(["尋寶藥水 +3", "藥水・僅限本局"], body.Split('|'));

        Assert.Equal(sum, tip.Treasure, 3);
        Assert.Equal("尋寶藥水 +3", tip.Head);
    }

    [Fact]
    public void Parse_Empty_HeadIsEmpty()
    {
        Assert.Equal(new TreasureTip("", 0), TreasureReader.Parse(["", "  "], []));
    }

    [Fact]
    public async Task ReadAsync_NoItems_ZeroWithoutMovingMouse()
    {
        var moves = 0;
        var r = await new TreasureReader(Ocr()).ReadAsync(CardLayoutTests.Fixture("野外1"), 1.0,
            () => throw new InvalidOperationException("不該截圖"), _ => { moves++; return Task.CompletedTask; }, () => 0);

        Assert.NotNull(r);
        Assert.Equal(0, r.Total);
        Assert.Empty(r.Items);
        Assert.Equal(0, moves);
    }

    [Fact]
    public async Task ReadAsync_SecondSlotNoTooltip_GivesUpAndMovesBackToCenter()
    {
        // 每次截圖都是「滑鼠在第 1 格」的畫面：第 1 格讀得到，第 2 格等不到自己的說明框 → 整次放棄
        var f = CardLayoutTests.Fixture("道具說明框");
        var moves = new List<PointF>();
        var t = 0L;

        var r = await new TreasureReader(Ocr()).ReadAsync(f, 1.0, () => Task.FromResult(f),
            p => { moves.Add(p); return Task.CompletedTask; }, () => t += 100);

        Assert.Null(r);
        Assert.Equal([new PointF(880, 580), new PointF(916, 580), new PointF(952, 341)], moves);
    }
}
