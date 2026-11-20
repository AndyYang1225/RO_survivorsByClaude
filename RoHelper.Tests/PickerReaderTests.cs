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

    [Fact]
    public async Task ReadAsync_NotAPicker_ReturnsNull()
    {
        Assert.Null(await Reader.ReadAsync(TestFrames.Solid(800, 600, 20, 25, 40), 1.0));
    }
}
