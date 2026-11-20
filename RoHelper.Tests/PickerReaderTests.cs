using RoHelper.Vision;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

/// <summary>用真實截圖跑完整判讀（需要這台電腦有繁中 OCR；計畫 1 已確認有）</summary>
public class PickerReaderTests
{
    private static readonly PickerReader Reader = new(OcrReader.TryCreate(out var e) ?? throw new InvalidOperationException(e));

    [Theory]
    [InlineData("升級選卡2", false, "雷爆術|聖靈召喚|火箭術")]
    [InlineData("升級選卡3", false, "雷爆術|雷擊術|火箭術")]
    [InlineData("寶箱選卡3", true, "傷害祝福|傷害秘藥|堡壘之神")]
    public async Task ReadAsync_NamesAndChest(string fixture, bool chest, string names)
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(p);
        Assert.Equal(chest, p.IsChest);
        Assert.Equal(names, string.Join("|", p.Cards.Select(c => c.Name)));
    }

    [Fact]
    public async Task ReadAsync_UpgradeWithNewSkills()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡2"), 1.0))!;

        Assert.Equal([true, true, false], p.Cards.Select(c => c.IsNew));
        Assert.Equal([true, true, false], p.Cards.Select(c => c.SkillBadge));
        Assert.Equal(1, p.Cards[2].Rarity);
        Assert.Equal(1, p.Cards[0].Button(ActKind.Focus)!.Left);
        Assert.Equal(4, p.Cards[0].Button(ActKind.Banish)!.Left);
        Assert.Equal(1, p.Cards[2].Button(ActKind.Reroll)!.Left);
        Assert.False(p.Cards[2].Button(ActKind.Focus)!.Usable);
        Assert.Equal(new { Usable = true, Left = 5 }, new { p.RerollAll!.Usable, p.RerollAll.Left });
    }

    [Fact]
    public async Task ReadAsync_UpgradeAllUsedUp()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡3"), 1.0))!;

        Assert.All(p.Cards, c => Assert.False(c.Button(ActKind.Reroll)!.Usable));
        Assert.All(p.Cards, c => Assert.Equal(3, c.Button(ActKind.Banish)!.Left));
        Assert.False(p.RerollAll!.Usable);
    }

    [Fact]
    public async Task ReadAsync_ChestWithDeity()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("寶箱選卡3"), 1.0))!;

        Assert.True(CardDecider.IsDeity(p.Cards[2]));
        Assert.False(CardDecider.IsDeity(p.Cards[0]));
        Assert.Equal([1, 2, 0], p.Cards.Select(c => c.Rarity));
        Assert.All(p.Cards, c => Assert.Equal(3, c.Button(ActKind.Banish)!.Left));
        Assert.Equal(5, p.RerollAll!.Left);
    }

    /// <summary>遊戲改版後「重抽全部」右邊多了「自動」按鈕：重抽全部的範圍不能連到自動按鈕（自動按鈕左緣實測約 x=970）</summary>
    [Fact]
    public async Task ReadAsync_RerollAllNotMergedWithAutoButton()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡自動鈕"), 1.0))!;

        Assert.Equal("冰箭術|火箭術|雷擊術", string.Join("|", p.Cards.Select(c => c.Name)));
        Assert.True(p.RerollAll!.Usable);
        Assert.Equal(5, p.RerollAll.Left);
        Assert.True(p.RerollAll.Bounds.Right < 970);
        Assert.False(p.Cards[0].Button(ActKind.Focus)!.Usable);

        // 「自動」按鈕實測約 x 970～1026、y 647～681
        Assert.NotNull(p.Auto);
        Assert.True(p.Auto.Usable);
        Assert.InRange(p.Auto.Bounds.X + p.Auto.Bounds.Width / 2, 975, 1020);
        Assert.InRange(p.Auto.Bounds.Y + p.Auto.Bounds.Height / 2, 650, 678);
    }

    /// <summary>升級選卡2 的截圖也有「自動」（實測約 x 386～442、y 455～488）</summary>
    [Fact]
    public async Task ReadAsync_SmallWindow_FindsAutoButton()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡2"), 1.0))!;

        Assert.NotNull(p.Auto);
        Assert.InRange(p.Auto.Bounds.X + p.Auto.Bounds.Width / 2, 395, 435);
        Assert.InRange(p.Auto.Bounds.Y + p.Auto.Bounds.Height / 2, 458, 486);
    }

    [Fact]
    public async Task ReadAsync_ChestScreenshot_NoAutoButton()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("寶箱選卡3"), 1.0))!;

        Assert.Null(p.Auto);
    }

    [Fact]
    public async Task ReadAsync_NotAPicker_ReturnsNull()
    {
        Assert.Null(await Reader.ReadAsync(TestFrames.Solid(800, 600, 20, 25, 40), 1.0));
    }
}
