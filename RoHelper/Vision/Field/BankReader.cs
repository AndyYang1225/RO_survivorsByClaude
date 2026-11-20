using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>計時器下方「升級 N [B]」標籤的樣子：Empty＝N 是 0（灰色）、Ready＝N &gt; 0（白底藍字，可以按 B）</summary>
public enum BankLook
{
    Empty,
    Ready,
}

/// <summary>
/// 讀累積升級的「升級 N [B]」標籤（遊戲「前往野外」前選「升級：累積」時才有；選「立即」時沒有）。
/// 實測（2026-11-21，dpr 1，寬 1904）：標籤是圓角框，x 909～996（以畫面水平中心為準 −43～+44）、y 100～128。
/// N＝0（升級累積0）：半透明灰底 (200～215)、深灰字 (69,80,85)；N＝3（升級累積3）：白底 (230～254，B ≥ R)、深藍字 (54,69,115)。
/// 純像素判斷（<see cref="Look"/>，每輪都讀也很便宜）；N 的數字要 OCR（<see cref="ReadAsync"/>，只給開發介面看，按 B 只要知道 N &gt; 0）。
/// </summary>
public sealed partial class BankReader(OcrReader ocr)
{
    /// <summary>框內取樣範圍（dpr 1）：水平中心 ±40、y 102～126</summary>
    private const int HalfWidth = 40;
    private const int Top = 102;
    private const int Bottom = 127;

    /// <summary>框的上方（y 90～96）、下方（y 133～140）：標籤外面是野外畫面，不會是一整片白</summary>
    private const int AboveTop = 90, AboveBottom = 97, BelowTop = 133, BelowBottom = 141;

    /// <summary>
    /// 看標籤的樣子；沒有標籤（立即模式、不在野外、被擋住）回 null。
    /// 實測比例（框內取樣範圍）：N＞0 白 0.76；N＝0 灰 0.83；其他野外畫面白、灰都 ≤ 0.05。
    /// 會被誤認成白框的：系統維護公告（野外1、野外2寶箱：白 0.76，但框下方白 0.61）、結算畫面（白 0.86，框上方白 1.0）、選卡畫面（框上下也白），
    /// 所以要求框的上方和下方白的比例都 &lt; 0.3。另外框裡要有字（深色點 ≥ 15·dpr²，實測 56、139）。
    /// </summary>
    public static BankLook? Look(Frame f, double dpr)
    {
        int cx = f.Width / 2, half = (int)(HalfWidth * dpr);
        int x0 = Math.Max(0, cx - half), x1 = Math.Min(f.Width, cx + half + 1);
        int y0 = (int)(Top * dpr), y1 = Math.Min(f.Height, (int)(Bottom * dpr));
        if (x1 <= x0 || y1 <= y0)
            return null;
        int white = 0, gray = 0, ink = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var (r, g, b) = f.At(x, y);
                int mn = Math.Min(r, Math.Min(g, b)), mx = Math.Max(r, Math.Max(g, b));
                if (mn >= 225 && b >= r - 5)
                    white++;
                else if (mn >= 170 && mx <= 228 && mx - mn <= 30)
                    gray++;
                else if (mx <= 140)
                    ink++;
            }
        double area = (x1 - x0) * (y1 - y0);
        if (ink < 15 * dpr * dpr)
            return null;
        if (WhiteShare(f, x0, x1, (int)(AboveTop * dpr), (int)(AboveBottom * dpr)) >= 0.3
            || WhiteShare(f, x0, x1, (int)(BelowTop * dpr), (int)(BelowBottom * dpr)) >= 0.3)
            return null;
        if (white / area >= 0.5)
            return BankLook.Ready;
        if (gray / area >= 0.5)
            return BankLook.Empty;
        return null;
    }

    /// <summary>
    /// 累積了幾次升級：沒有標籤回 null、灰色回 0、白色就 OCR 數字（範圍水平中心 ±50、y 96～130，放大 2 倍；實測讀成「升 3 B」，「級」常漏）。
    /// 白色但 OCR 讀不到數字時回 1（至少有 1 次）。
    /// </summary>
    public async Task<int?> ReadAsync(Frame f, double dpr)
    {
        var look = Look(f, dpr);
        if (look != BankLook.Ready)
            return look is null ? null : 0;
        var half = (int)(50 * dpr);
        var area = Rectangle.Intersect(new Rectangle(f.Width / 2 - half, (int)(96 * dpr), half * 2, (int)(34 * dpr)), new Rectangle(0, 0, f.Width, f.Height));
        var lines = await ocr.ReadAsync(f, area, 2);
        return ParseCount(string.Join(" ", lines.Select(l => l.Text))) ?? 1;
    }

    /// <summary>OCR 文字裡第一個數字（「升 3 B」→ 3）；沒有數字回 null</summary>
    internal static int? ParseCount(string text) => Number().Match(text) is { Success: true } m && int.TryParse(m.Value, out var n) ? n : null;

    private static double WhiteShare(Frame f, int x0, int x1, int y0, int y1)
    {
        y0 = Math.Max(0, y0);
        y1 = Math.Min(f.Height, y1);
        if (y1 <= y0)
            return 0;
        var white = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var (r, g, b) = f.At(x, y);
                if (Math.Min(r, Math.Min(g, b)) >= 225)
                    white++;
            }
        return white / (double)((x1 - x0) * (y1 - y0));
    }

    [GeneratedRegex(@"\d{1,3}")]
    private static partial Regex Number();
}
