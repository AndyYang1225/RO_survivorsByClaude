namespace RoHelper.Vision;

/// <summary>
/// OCR 前處理：遊戲的小字（11～12 px、粗體、有陰影、底色是藍色漸層）直接丟給 Windows OCR 常漏字，
/// 實測只取紅色通道再拉開對比、四周補一圈空白，讀得比原圖穩定（藍底的紅色通道很暗，白字或灰字的紅色通道很亮，對比最大）。
/// 刻意不二值化：實測二值化後 Windows OCR 反而幾乎什麼都讀不到（筆畫邊緣的反鋸齒被切掉）。
/// </summary>
public static class OcrPrep
{
    /// <summary>
    /// 裁切 region → 取紅色通道 → 第 2～98 百分位拉到 0～255（invert＝反相，變成白底黑字）→ 四周補 pad 像素的底色（中位數的顏色）。
    /// 回傳新的 Frame（整張就是要讀的範圍）；region 完全在畫面外時回 null。
    /// </summary>
    public static Frame? RedStretch(Frame f, Rectangle region, int pad, bool invert)
    {
        var c = f.Crop(region);
        if (c.Width == 0 || c.Height == 0)
            return null;
        var red = new int[c.Width * c.Height];
        for (var i = 0; i < red.Length; i++)
            red[i] = c.Pixels[i * 4 + 2];
        var sorted = (int[])red.Clone();
        Array.Sort(sorted);
        int lo = sorted[(int)(sorted.Length * 0.02)], hi = sorted[(int)(sorted.Length * 0.98)];
        var range = Math.Max(1, hi - lo);
        byte Map(int r)
        {
            var v = (byte)Math.Clamp((r - lo) * 255 / range, 0, 255);
            return invert ? (byte)(255 - v) : v;
        }

        int w = c.Width + pad * 2, h = c.Height + pad * 2;
        var px = new byte[w * h * 4];
        // 補的邊用拉開後的「底色」（中位數：字只佔一小部分，中位數就是底色）
        var bg = Map(sorted[sorted.Length / 2]);
        Array.Fill(px, bg);
        for (var i = 3; i < px.Length; i += 4)
            px[i] = 255;
        for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var v = Map(red[y * c.Width + x]);
                var i = ((y + pad) * w + x + pad) * 4;
                px[i] = px[i + 1] = px[i + 2] = v;
            }
        return new Frame(w, h, px);
    }
}
