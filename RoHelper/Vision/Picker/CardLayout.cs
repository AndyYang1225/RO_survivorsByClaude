namespace RoHelper.Vision.Picker;

/// <summary>
/// 選卡畫面的純像素判讀（不用 OCR）：卡片位置、稀有度標籤底色、「新！」粉紅斜條、卡片下方的動作按鈕。
/// 為什麼不用 OCR 讀這些：實測（2026-11-20）標籤是白字壓彩色底、「新！」是斜的、按鈕字很小，Windows OCR 都讀不到；顏色和位置反而很穩定。
/// 常數是用 RoHelper.Tests/Fixtures 的真實截圖（dpr 1）校正的，距離都乘 dpr。
/// </summary>
public static class CardLayout
{
    // 卡片本體底緣（亮列結束處）再往下取的像素：把邊框和陰影也算進卡片，高度才落在 320～380（實測本體含標籤約 311～313）。
    // 按鈕上緣實測在本體底緣 + 10 左右，所以卡片下緣會略蓋到按鈕上緣，掃按鈕時要往下跳（見 FindButtons）。
    private const int CardBottomPad = 10;

    // 一列裡亮像素佔卡片寬度的比例 ≥ 這個值才算「卡片本體」：實測本體列 ≈ 100%，3 顆按鈕最多佔約 70%，按鈕列過不了這關。
    private const double BodyRowCoverage = 0.85;

    // 校正：標籤底色參考值（實測 dpr 1）。優良 ≈ (72,218,98)、稀有 ≈ (65,132,249)、技能 ≈ (132,178,250)；
    // 史詩／傳說／神話還沒有真實截圖，先用紫／橘／紅的估計值。
    private static readonly (int Rarity, bool Skill, int R, int G, int B)[] BadgeRefs =
    [
        (0, true, 132, 178, 250),
        (1, false, 72, 218, 98),
        (2, false, 65, 132, 249),
        (3, false, 160, 80, 220),
        (4, false, 240, 150, 40),
        (5, false, 230, 60, 60),
    ];

    // 校正：標籤區飽和像素 ≥ 這個數才算有標籤（實測有標籤 ≈ 500～580 個，灰色的普通標籤 ≤ 3 個）
    private const int BadgeMinPixels = 60;

    // 校正：粉紅像素 ≥ 這個數才算有「新！」（實測有 ≈ 1500～1600 個，沒有 0 個）
    private const int RibbonMinPixels = 200;

    // 校正：按鈕平均亮度 ≥ 這個值算能按（實測（下半截區域的平均）能按 ≈ 199～208，不能按 ≈ 102～107）
    private const int EnabledMinLuma = 170;

    /// <summary>
    /// 找卡片：很亮（r、g、b 都 > 215）的大色塊，寬 180～260、高 300～400（× dpr），而且至少 2 張、上緣差不到 25 px。
    /// 少於 2 張回空清單＝不是選卡畫面（大廳的對話框也是亮的，但只有一塊、大小也不同）。
    /// 色塊底部可能連著白色的按鈕列（按鈕能按時），所以不直接用色塊高度：從色塊底往上找亮像素佔滿卡片寬度的最後一列當本體底緣，再加 <see cref="CardBottomPad"/>。
    /// 按鈕全灰（不能按）時色塊本來就只有本體，兩種情況得到同樣的矩形。
    /// </summary>
    public static IReadOnlyList<Rectangle> FindCards(Frame f, double dpr)
    {
        var blobs = ColorBlobs.Find(f, new Rectangle(0, 0, f.Width, f.Height), (r, g, b) => r > 215 && g > 215 && b > 215, 3, 3)
            .Where(b => b.Width >= 180 * dpr && b.Width <= 260 * dpr && b.Height >= 300 * dpr && b.Height <= 400 * dpr)
            .OrderBy(b => b.X0)
            .ToList();

        // 以每張為基準，找上緣對齊的那一組，取最多張的
        var best = blobs.Select(a => blobs.Where(b => Math.Abs(b.Y0 - a.Y0) < 25 * dpr).ToList())
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();
        if (best is null || best.Count < 2)
            return [];

        var pad = (int)Math.Round(CardBottomPad * dpr);
        return best.Select(b => new Rectangle(b.X0, b.Y0, b.Width, BodyBottom(f, b) + pad - b.Y0)).ToList();
    }

    // 卡片本體的下緣（不含）：從色塊底往上，第一個亮像素佔滿寬度的列 + 1；找不到就退回色塊下緣
    private static int BodyBottom(Frame f, Blob b)
    {
        var need = b.Width * BodyRowCoverage / 2;
        for (var y = b.Y1; y >= b.Y0; y--)
        {
            var bright = 0;
            for (var x = b.X0; x <= b.X1; x += 2)
            {
                var (r, g, bl) = f.At(x, y);
                if (r > 215 && g > 215 && bl > 215)
                    bright++;
            }
            if (bright >= need)
                return y + 1;
        }
        return b.Y1 + 1;
    }

    /// <summary>
    /// 稀有度：卡片上緣中間的標籤底色。取標籤區裡飽和度高的像素平均色，跟參考色比最接近的；飽和像素太少（灰色標籤）＝普通。
    /// 技能標籤是淺藍、稀有是深藍，兩者用參考色區分。史詩／傳說／神話還沒有真實截圖，先用紫／橘／紅的估計值。
    /// </summary>
    public static (int Rarity, bool Skill) ReadBadge(Frame f, Rectangle card, double dpr)
    {
        var cx = card.X + card.Width / 2;
        var half = (int)(30 * dpr);
        var pad = (int)(14 * dpr);
        long sr = 0, sg = 0, sb = 0;
        var n = 0;
        foreach (var (x, y) in Points(f, new Rectangle(cx - half, card.Y - pad, half * 2 + 1, pad * 2 + 1)))
        {
            var (r, g, b) = f.At(x, y);
            if (Saturation(r, g, b) <= 60)
                continue;
            n++;
            sr += r;
            sg += g;
            sb += b;
        }
        if (n < BadgeMinPixels * dpr * dpr)
            return (0, false);

        var ar = (int)(sr / n);
        var ag = (int)(sg / n);
        var ab = (int)(sb / n);
        // 刻意不設距離上限：史詩／傳說／神話還沒有真實截圖，參考色只是估計值，寧可歸到最接近的一級，也不要讀不到；有真實截圖後再校正或加上限
        var near = BadgeRefs.OrderBy(t => Dist2(ar, ag, ab, t.R, t.G, t.B)).First();
        return (near.Rarity, near.Skill);
    }

    /// <summary>「新！」：卡片右上角的粉紅斜條，粉紅像素夠多就算有</summary>
    public static bool HasNewRibbon(Frame f, Rectangle card, double dpr)
    {
        var w = (int)(70 * dpr);
        var top = (int)(6 * dpr);
        var count = 0;
        foreach (var (x, y) in Points(f, new Rectangle(card.Right - w, card.Y - top, w, (int)(76 * dpr))))
        {
            var (r, g, b) = f.At(x, y);
            if (r > 220 && g >= 70 && g <= 160 && b >= 110 && b <= 190)
                count++;
        }
        return count >= RibbonMinPixels * dpr * dpr;
    }

    /// <summary>
    /// 卡片下方的動作按鈕：亮度 > 90、飽和度低的色塊（藍色的次數小圓圈飽和度高，會被排除），寬 30～70、高 10～45（× dpr），由左到右。
    /// 高度下限 10 偏離計畫書的 20：右上角的次數圓圈會跟按鈕連成一塊，所以只掃按鈕的下半截（高約 15～18），詳見程式內註解。
    /// 種類看順序：3 顆＝重抽、專注、放逐；2 顆＝專注、放逐（新技能卡沒有重抽）；1 顆＝放逐。能不能按看 <see cref="IsEnabled"/>。
    /// </summary>
    public static IReadOnlyList<(ActKind Kind, Rectangle Bounds, bool Enabled)> FindButtons(Frame f, Rectangle card, double dpr)
    {
        var margin = (int)(20 * dpr);
        // 只掃按鈕的下半截：次數小圓圈（藍底、白框）壓在按鈕右上角，白框的飽和度低，會跟按鈕連成一塊而把上緣拉高；
        // 實測圓圈最低到卡片底 + 16～17（dpr 1），從這裡往下掃就只剩按鈕本身。所以回傳的 Bounds 是按鈕的下半截（寬度正確、高約 18），點它的中心仍在按鈕內
        var region = new Rectangle(card.X - margin, card.Bottom + (int)(18 * dpr), card.Width + margin * 2, (int)(22 * dpr));
        var found = ColorBlobs.Find(f, region, (r, g, b) => Luma(r, g, b) > 90 && Saturation(r, g, b) < 60, 1, 1)
            .Where(b => b.Width >= 30 * dpr && b.Width <= 70 * dpr && b.Height >= 10 * dpr && b.Height <= 45 * dpr)
            .OrderBy(b => b.X0)
            .Select(b => new Rectangle(b.X0, b.Y0, b.Width, b.Height))
            .ToList();

        // 順序決定種類：從右邊算起，最後一顆一定是放逐，倒數第二是專注，再前面是重抽
        ActKind[] order = [ActKind.Reroll, ActKind.Focus, ActKind.Banish];
        var skip = order.Length - found.Count;
        if (found.Count > order.Length || found.Count == 0)
            return [];
        return found.Select((rect, i) => (order[skip + i], rect, IsEnabled(f, rect))).ToList();
    }

    /// <summary>按鈕能不能按：能按是白底（平均亮度高），不能按是灰底</summary>
    public static bool IsEnabled(Frame f, Rectangle button)
    {
        long sum = 0;
        var n = 0;
        foreach (var (x, y) in Points(f, button))
        {
            var (r, g, b) = f.At(x, y);
            sum += Luma(r, g, b);
            n++;
        }
        return n > 0 && sum / n >= EnabledMinLuma;
    }

    // 範圍內（裁到畫面內）的每個像素座標
    private static IEnumerable<(int X, int Y)> Points(Frame f, Rectangle area)
    {
        area = Rectangle.Intersect(area, new Rectangle(0, 0, f.Width, f.Height));
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                yield return (x, y);
    }

    private static int Saturation(byte r, byte g, byte b) => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    private static int Luma(byte r, byte g, byte b) => (r * 299 + g * 587 + b * 114) / 1000;

    private static int Dist2(int r1, int g1, int b1, int r2, int g2, int b2)
        => (r1 - r2) * (r1 - r2) + (g1 - g2) * (g1 - g2) + (b1 - b2) * (b1 - b2);
}
