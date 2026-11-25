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

    /// <summary>
    /// 技能神祇選擇畫面（2026-11-25 實測，Lv 21 點道具欄紫色「!」）：標題「選擇你的神祇」，4 張「X Deity」卡。
    /// OCR 把卡名讀成「冰箾術 De」（Deity 只剩 De、箭讀成箾），名字比對交給 CardDecider 的 OcrFix
    /// </summary>
    [Fact]
    public async Task ReadAsync_DeityPick()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("技能神祇選擇"), 1.0))!;

        Assert.True(p.IsDeityPick);
        Assert.False(p.IsChest);
        Assert.Equal(4, p.Cards.Count);
        Assert.Contains("雷擊術", p.Cards[1].Name);
        // 測試圖截在卡片淡入動畫中（第 3、4 張半透明），第 4 張讀成「火壁 Deity」；這裡只確認讀得出是神祇卡
        Assert.EndsWith("Deity", p.Cards[3].Name);
    }

    [Theory]
    [InlineData("升級選卡2")]
    [InlineData("升級選卡3")]
    [InlineData("寶箱選卡3")]
    [InlineData("寶箱改版神器")]
    [InlineData("升級改版")]
    public async Task ReadAsync_OtherPickers_NotDeityPick(string fixture)
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0))!;

        Assert.False(p.IsDeityPick);
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

/// <summary>遊戲 2026.11.23 版：神祇改名「神器」、底下按鈕分「自動技能／自動寶箱」、組隊時標題附近被「正在選卡」小視窗蓋住</summary>
public class PickerReaderV1123Tests
{
    private static readonly PickerReader Reader = new(OcrReader.TryCreate(out var e) ?? throw new InvalidOperationException(e));

    [Fact]
    public async Task Chest_DetectedEvenWithPartyPopup_ArtifactIsDeity()
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture("寶箱改版神器"), 1.0);

        Assert.NotNull(p);
        Assert.True(p!.IsChest);
        Assert.Equal(3, p.Cards.Count);
    }

    /// <summary>正常（沒變淡）的神器卡：分類「神器」要讀得到、算神祇類</summary>
    [Fact]
    public async Task Artifact_IsDeity()
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture("寶箱改版處刑神器"), 1.0);

        Assert.NotNull(p);
        Assert.True(p!.IsChest);
        Assert.True(CardDecider.IsDeity(p.Cards[2]), p.Cards[2].Name + " | " + p.Cards[2].Text);
        Assert.False(CardDecider.IsDeity(p.Cards[1]));
    }

    [Fact]
    public async Task LevelUp_NotChest()
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture("升級改版"), 1.0);

        Assert.NotNull(p);
        Assert.False(p!.IsChest);
    }

    /// <summary>卡片上方的欄位數：升級「技能 4/4」（OCR 讀成「技 4 / 4」）、寶箱「道具 0/6」</summary>
    [Theory]
    [InlineData("升級改版", 4, 4)]
    [InlineData("寶箱改版神器", 0, 6)]
    [InlineData("寶箱改版處刑神器", 0, 6)]
    public async Task Slots_Read(string fixture, int used, int max)
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.Equal((used, max), p!.Slots);
    }

    /// <summary>「自動寶箱」按鈕寬約 81 px：舊的寬度上限 80 會把它濾掉，放逐完也按不到</summary>
    [Theory]
    [InlineData("寶箱改版神器")]
    [InlineData("寶箱改版處刑神器")]
    public async Task Chest_AutoButtonFound(string fixture)
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(p?.RerollAll);
        Assert.NotNull(p!.Auto);
        Assert.True(p.Auto!.Usable);
        Assert.True(p.Auto.Bounds.Left > p.RerollAll!.Bounds.Right, $"自動 {p.Auto.Bounds} 要在重抽全部 {p.RerollAll.Bounds} 右邊");
    }

    /// <summary>升級畫面的按鈕列被畫面下緣切掉（只露出約 16 px、讀不到字）：還是要找到「重抽全部」和「自動技能」</summary>
    [Fact]
    public async Task LevelUp_CutOffButtons_StillFindsAuto()
    {
        var f = CardLayoutTests.Fixture("升級改版");
        var p = await Reader.ReadAsync(f, 1.0);

        Assert.NotNull(p?.RerollAll);
        Assert.NotNull(p!.Auto);
        Assert.True(p.Auto!.Usable);
        Assert.InRange(p.Auto.Bounds.Left, 940, 980);
        Assert.InRange(p.Auto.Bounds.Top, 655, f.Height);
    }
}
