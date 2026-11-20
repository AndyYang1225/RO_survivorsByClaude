using System.Drawing.Drawing2D;
using RoHelper.Vision;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

/// <summary>剩餘次數的字形比對：用合成的圓圈測信心門檻（不需要 OCR）</summary>
public class PickerDigitTests
{
    // 在 32×32 白底上畫一個藍色圓底（跟遊戲一樣的 (111,143,216)），再由 draw 畫上內容
    private static Frame Circle(Action<Graphics> draw)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(111, 143, 216));
            g.FillEllipse(fill, 6, 6, 20, 20);
            draw(g);
        }
        return Frame.FromBitmap(bmp);
    }

    private static Frame DigitCircle(string text)
    {
        return Circle(g =>
        {
            using var font = new Font("Arial", 11, FontStyle.Bold, GraphicsUnit.Pixel);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, Brushes.White, 16 - size.Width / 2, 16 - size.Height / 2);
        });
    }

    [Fact]
    public void ReadDigits_GarbageBlob_ReturnsNull()
    {
        var f = Circle(g => g.FillRectangle(Brushes.White, 11, 11, 10, 10));

        Assert.Null(PickerReader.ReadDigits(f, 1.0));
    }

    [Fact]
    public void ReadLeft_EnabledRecognizedZero_FallsBackTo99()
    {
        var f = DigitCircle("0");

        Assert.Equal(99, PickerReader.ReadLeft(f, 16 + 2, 16 + 4, true, 1.0));
    }

    [Fact]
    public void ReadDigits_TooWideSegment_ReturnsNull()
    {
        // 一條很寬的白條：寬高比遠超過單一數字（像兩個數字黏在一起）
        var f = Circle(g => g.FillRectangle(Brushes.White, 8, 13, 16, 6));

        Assert.Null(PickerReader.ReadDigits(f, 1.0));
    }
}
