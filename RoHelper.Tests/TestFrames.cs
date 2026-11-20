using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>
/// 測試用的假截圖。alpha 一律 255：Frame 轉 Bitmap 給 OCR 時，alpha 0 會變成全黑。
/// </summary>
public static class TestFrames
{
    public static Frame Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = 255;
        }
        return new Frame(w, h, px);
    }

    /// <summary>塗一塊實心方塊，超出圖片的部分自動裁掉（Pattern 用小尺寸時方塊會在圖外）</summary>
    public static void Fill(Frame f, Rectangle rect, byte r, byte g, byte b)
    {
        rect = Rectangle.Intersect(rect, new Rectangle(0, 0, f.Width, f.Height));
        for (var y = rect.Top; y < rect.Bottom; y++)
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var i = (y * f.Width + x) * 4;
                f.Pixels[i] = b;
                f.Pixels[i + 1] = g;
                f.Pixels[i + 2] = r;
                f.Pixels[i + 3] = 255;
            }
    }

    /// <summary>
    /// 平滑的放射狀漸層加兩塊實心方塊。範本比對的粗找每 2 點試一次，背景要平滑，差一點的位置分數才會接近；
    /// 純雜訊的圖差一點就跟隨機位置一樣差，粗找會找錯地方（真實的遊戲 UI 是平滑的，不會有這個問題）。
    /// 刻意不用線性漸層：線性漸層沿等高線平移後完全一樣（取餘數折回時還會出現 255 的落差），粗找會被假的重複位置騙走。
    /// </summary>
    public static Frame Pattern(int w = 200, int h = 100)
    {
        var f = Solid(w, h, 0, 0, 0);
        var max = (w / 2) * (w / 2) + 4 * (h / 2) * (h / 2);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = x - w / 2;
                var dy = y - h / 2;
                var v = (byte)Math.Min(255, (dx * dx + 4 * dy * dy) * 255 / max);
                var i = (y * w + x) * 4;
                f.Pixels[i] = v;
                f.Pixels[i + 1] = v;
                f.Pixels[i + 2] = v;
            }
        Fill(f, new Rectangle(60, 35, 10, 8), 255, 255, 255);
        Fill(f, new Rectangle(20, 70, 15, 10), 0, 0, 0);
        return f;
    }
}
