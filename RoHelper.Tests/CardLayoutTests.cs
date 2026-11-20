using RoHelper.Vision;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class CardLayoutTests
{
    internal static Frame Fixture(string name)
    {
        using var bmp = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".png"));
        return Frame.FromBitmap(bmp);
    }

    [Theory]
    [InlineData("升級選卡2", 24, 498)]
    [InlineData("升級選卡3", 72, 543)]
    [InlineData("寶箱選卡3", 63, 534)]
    public void FindCards_ThreeCardsLeftToRight(string fixture, int firstX, int lastX)
    {
        var cards = CardLayout.FindCards(Fixture(fixture), 1.0);

        Assert.Equal(3, cards.Count);
        Assert.InRange(cards[0].X, firstX - 8, firstX + 8);
        Assert.InRange(cards[2].X, lastX - 8, lastX + 8);
        Assert.All(cards, c => Assert.InRange(c.Width, 195, 235));
        Assert.All(cards, c => Assert.InRange(c.Height, 320, 380));
    }

    [Fact]
    public void FindCards_NoCards_OnPlainScreen()
    {
        Assert.Empty(CardLayout.FindCards(TestFrames.Solid(800, 600, 20, 25, 40), 1.0));
    }

    [Theory]
    [InlineData("升級選卡2", new[] { 0, 0, 1 }, new[] { true, true, false })]
    [InlineData("升級選卡3", new[] { 1, 1, 0 }, new[] { false, false, false })]
    [InlineData("寶箱選卡3", new[] { 1, 2, 0 }, new[] { false, false, false })]
    public void ReadBadge_RarityAndSkill(string fixture, int[] rarity, bool[] skill)
    {
        var f = Fixture(fixture);
        var cards = CardLayout.FindCards(f, 1.0);

        for (var i = 0; i < 3; i++)
        {
            var (r, s) = CardLayout.ReadBadge(f, cards[i], 1.0);
            Assert.True(rarity[i] == r && skill[i] == s, $"卡 {i + 1}：預期 ({rarity[i]}, {skill[i]})，實際 ({r}, {s})");
        }
    }

    [Theory]
    [InlineData("升級選卡2", new[] { true, true, false })]
    [InlineData("升級選卡3", new[] { false, false, false })]
    [InlineData("寶箱選卡3", new[] { true, true, true })]
    public void HasNewRibbon(string fixture, bool[] expected)
    {
        var f = Fixture(fixture);
        var cards = CardLayout.FindCards(f, 1.0);

        Assert.Equal(expected, cards.Select(c => CardLayout.HasNewRibbon(f, c, 1.0)).ToArray());
    }

    [Theory]
    [InlineData("升級選卡2", 0, "FB", "11")]
    [InlineData("升級選卡2", 2, "RFB", "101")]
    [InlineData("升級選卡3", 1, "RFB", "001")]
    [InlineData("寶箱選卡3", 2, "RFB", "101")]
    public void FindButtons_KindsAndEnabled(string fixture, int card, string kinds, string enabled)
    {
        var f = Fixture(fixture);
        var c = CardLayout.FindCards(f, 1.0)[card];

        var buttons = CardLayout.FindButtons(f, c, 1.0);

        Assert.Equal(kinds, string.Concat(buttons.Select(b => b.Kind switch { ActKind.Reroll => "R", ActKind.Focus => "F", _ => "B" })));
        Assert.Equal(enabled, string.Concat(buttons.Select(b => b.Enabled ? "1" : "0")));
        Assert.All(buttons, b => Assert.True(b.Bounds.Y > c.Bottom, "按鈕要在卡片下方"));
    }
}
