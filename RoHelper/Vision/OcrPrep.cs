namespace RoHelper.Vision;

/// <summary>
/// OCR 前處理：遊戲的小字（11～12 px、粗體、有陰影、底色是藍色漸層）直接丟給 Windows OCR 常漏字，
/// 實測只取紅色通道、以底色為起點拉開對比、做成白底黑字、四周補一圈空白，讀得比原圖穩定
/// （藍底的紅色通道很暗，白字或灰字的紅色通道很亮，對比最大；白底黑字的地方反過來也成立）。
/// 刻意不二值化：實測二值化後 Windows OCR 反而幾乎什麼都讀不到（筆畫邊緣的反鋸齒被切掉）。
/// </summary>
public static class OcrPrep
{
    /// <summary>
    /// 裁切 region → 取紅色通道 → 中位數當底色（字只佔一小部分），字比底色亮還是暗看哪一邊離得遠（第 1／99 百分位）→
    /// 底色到字的那一段拉到白～黑（陰影等比底色更「反方向」的顏色夾成白）→ 四周補 pad 像素的白。
    /// 回傳新的 Frame（整張就是要讀的範圍）；region 完全在畫面外時回 null。
    /// </summary>
    public static Frame? RedStretch(Frame f, Rectangle region, int pad)
    {
        var c = f.Crop(region);
        if (c.Width == 0 || c.Height == 0)
            return null;
        var red = new int[c.Width * c.Height];
        for (var i = 0; i < red.Length; i++)
            red[i] = c.Pixels[i * 4 + 2];
        var sorted = (int[])red.Clone();
        Array.Sort(sorted);
        int bg = sorted[sorted.Length / 2], lo = sorted[(int)(sorted.Length * 0.01)], hi = sorted[(int)(sorted.Length * 0.99)];
        var bright = hi - bg >= bg - lo;
        var range = Math.Max(1, bright ? hi - bg : bg - lo);

        int w = c.Width + pad * 2, h = c.Height + pad * 2;
        var px = new byte[w * h * 4];
        Array.Fill(px, (byte)255);
        for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var r = red[y * c.Width + x];
                var ink = Math.Clamp((bright ? r - bg : bg - r) * 255 / range, 0, 255);
                var i = ((y + pad) * w + x + pad) * 4;
                px[i] = px[i + 1] = px[i + 2] = (byte)(255 - ink);
            }
        return new Frame(w, h, px);
    }
}
