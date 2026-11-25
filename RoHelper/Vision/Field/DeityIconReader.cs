namespace RoHelper.Vision.Field;

/// <summary>
/// 找道具欄的技能神祇「!」（遊戲 2026.11.23 版起：角色約 20 等時，神器右邊的格子變成白底紫色「!」，點開選技能神祇，每局只選一次）。
/// 實測（2026-11-25，dpr 1，視窗 1904×682，Lv 21）：「!」在道具欄第 6 格（中心 1023, 580），格子中央 ±12、每 2 px 取樣：
/// 白色（RGB 都 &gt; 235）155/169 點，紫色筆畫（約 164,133,224）集中在中心左右 2 px 內；
/// 有道具的格子白色 56～82/169（33～49%）、空格白色 0。所以要求白色 ≥ 70%、中央有紫色筆畫、旁邊幾乎沒有紫色。
/// 純像素判斷，每輪都看也很便宜。格子位置沿用 <see cref="TreasureReader.SlotCenter"/>；
/// 道具欄 6 格都滿時「!」會在哪裡還不知道（實測只有 5 格道具＋「!」），所以掃全部 6 格。
/// </summary>
public static class DeityIconReader
{
    /// <summary>格子中心 ±12、每 2 px 取樣（同 TreasureReader 判斷有沒有道具的範圍）</summary>
    private const double SampleHalf = 12;
    private const double SampleStep = 2;

    /// <summary>白色點至少 70%（實測「!」92%、有道具的格子最多 49%）</summary>
    private const double MinWhite = 0.7;

    /// <summary>中央的紫色點至少幾個（實測 11 個）；中央＝離格子中心水平 5 px 內（實測筆畫在 −2～0）</summary>
    private const int MinGlyph = 4;
    private const double GlyphHalfWidth = 5;

    /// <summary>有「!」回那一格的中心（截圖像素）；沒有回 null</summary>
    public static PointF? Find(Frame f, double dpr)
    {
        for (var i = 0; i < TreasureReader.SlotCount; i++)
        {
            var c = TreasureReader.SlotCenter(f, dpr, i);
            if (IsIcon(f, dpr, c))
                return c;
        }
        return null;
    }

    private static bool IsIcon(Frame f, double dpr, PointF c)
    {
        var step = Math.Max(1, (int)Math.Round(SampleStep * dpr));
        var half = (int)Math.Round(SampleHalf * dpr);
        int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);
        if (cx - half < 0 || cy - half < 0 || cx + half >= f.Width || cy + half >= f.Height)
            return false;
        var glyphHalf = GlyphHalfWidth * dpr;
        int n = 0, white = 0, glyph = 0, offCenter = 0;
        for (var y = cy - half; y <= cy + half; y += step)
        {
            for (var x = cx - half; x <= cx + half; x += step)
            {
                var (r, g, b) = f.At(x, y);
                n++;
                if (r > 235 && g > 235 && b > 235)
                    white++;
                else if (IsPurple(r, g, b))
                {
                    if (Math.Abs(x - cx) <= glyphHalf)
                        glyph++;
                    else
                        offCenter++;
                }
            }
        }
        return white >= MinWhite * n && glyph >= MinGlyph && offCenter <= glyph / 2;
    }

    // 「!」的紫色（實測 164,133,224）：藍最多、紅次之、綠明顯少
    private static bool IsPurple(int r, int g, int b) => b > 150 && r > 110 && g < r - 25 && b > g + 50;
}
