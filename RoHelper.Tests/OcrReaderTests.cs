using System.Drawing.Text;

using RoHelper.Vision;

namespace RoHelper.Tests;

public class OcrReaderTests
{
    [Theory]
    [InlineData("單 人 大 廳", "單人大廳")]
    [InlineData("返回 角色", "返回角色")]
    [InlineData("HP 120 / 300", "HP 120 / 300")]
    [InlineData("雷 爆 術 Deity + 3", "雷爆術 Deity + 3")]
    public void Compact_RemovesSpacesBetweenCjkOnly(string input, string expected)
    {
        Assert.Equal(expected, OcrReader.Compact(input));
    }

    [Fact]
    public async Task ReadAsync_ReadsRenderedTraditionalChinese()
    {
        var reader = OcrReader.TryCreate(out var error);
        Assert.True(reader is not null, error);

        var frame = Render("單人大廳", 360, 80);
        var lines = await reader!.ReadAsync(frame, new Rectangle(0, 0, frame.Width, frame.Height));

        Assert.Contains(lines, l => l.Text.Contains("單人大廳"));
        Assert.All(lines, l => Assert.True(new Rectangle(0, 0, frame.Width, frame.Height).Contains(l.Bounds)));
    }

    [Fact]
    public async Task ReadAsync_ReportsBoundsInFrameCoordinates()
    {
        var reader = OcrReader.TryCreate(out _)!;
        var text = Render("返回角色", 300, 70);
        var frame = TestFrames.Solid(800, 400, 255, 255, 255);
        Paste(text, frame, 400, 200);

        var lines = await reader.ReadAsync(frame, new Rectangle(350, 150, 400, 200));

        var line = Assert.Single(lines, l => l.Text.Contains("返回角色"));
        Assert.InRange(line.Bounds.X, 400, 470);
        Assert.InRange(line.Bounds.Y, 200, 250);
    }

    private static Frame Render(string text, int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Microsoft JhengHei", 28, GraphicsUnit.Pixel))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, Brushes.Black, 10, 15);
        }
        return Frame.FromBitmap(bmp);
    }

    private static void Paste(Frame src, Frame dst, int ox, int oy)
    {
        for (var y = 0; y < src.Height; y++)
            Buffer.BlockCopy(src.Pixels, y * src.Width * 4, dst.Pixels, ((oy + y) * dst.Width + ox) * 4, src.Width * 4);
    }
}
