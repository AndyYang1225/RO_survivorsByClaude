using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class PickerModelsTests
{
    [Fact]
    public void ActButton_UsableNeedsEnabledAndLeft()
    {
        Assert.True(new ActButton(ActKind.Banish, Rectangle.Empty, true, 3).Usable);
        Assert.False(new ActButton(ActKind.Banish, Rectangle.Empty, true, 0).Usable);
        Assert.False(new ActButton(ActKind.Banish, Rectangle.Empty, false, 3).Usable);
    }

    [Fact]
    public void CardInfo_ButtonByKind()
    {
        var focus = new ActButton(ActKind.Focus, new Rectangle(1, 2, 3, 4), true, 1);
        var card = new CardInfo(0, Rectangle.Empty, "雷爆術", "雷爆術", 0, true, true, [focus]);

        Assert.Same(focus, card.Button(ActKind.Focus));
        Assert.Null(card.Button(ActKind.Reroll));
    }

    [Fact]
    public void Fixtures_AreCopiedToOutput()
    {
        foreach (var n in new[] { "升級選卡2", "升級選卡3", "寶箱選卡3" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", n + ".png")), n);
    }
}
