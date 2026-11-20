using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>畫面正上方的金錢與開寶箱的價格（單位 z）</summary>
public sealed record Hud(int Money, int ChestCost);

/// <summary>
/// 讀畫面正上方計時器下面那排小框：「擊殺數」「金錢 z」「寶箱 價格 z」（實測 dpr 1、寬 1904：約 x 860～1060、y 45～72，整排以畫面水平中心為準）。
/// 只做一次 OCR（以 W/2 為中心、寬 260·dpr、y 42～76·dpr），解析見 <see cref="Parse"/>。
/// </summary>
public sealed partial class HudReader(OcrReader ocr)
{
    /// <summary>讀不到金錢或價格回 null（呼叫端當成買得起，同 JS canAffordChest 讀不到時的保守行為）</summary>
    public async Task<Hud?> ReadAsync(Frame f, double dpr)
    {
        var lines = await ocr.ReadAsync(f, Area(f, dpr));
        return Parse(string.Join(" ", lines.Select(l => l.Text)));
    }

    /// <summary>OCR 範圍：以畫面水平中心為準、寬 260·dpr、y 42～76·dpr，夾在畫面內</summary>
    internal static Rectangle Area(Frame f, double dpr)
    {
        var w = (int)(260 * dpr);
        var r = new Rectangle(f.Width / 2 - w / 2, (int)(42 * dpr), w, (int)(34 * dpr));
        return Rectangle.Intersect(r, new Rectangle(0, 0, f.Width, f.Height));
    }

    /// <summary>
    /// 解析 OCR 文字（每行用空白接起來）：後面接著 z 的數字＝金錢，金錢之後的下一個數字＝價格；任一讀不到回 null。
    /// 跟計畫寫的「寶箱後面的數字＝價格、其他第一個數字＝金錢」不同，原因是實測（放大 2 倍）：
    /// 1. 同一排最左邊還有擊殺數（「6738」），「第一個數字」會是擊殺數；金錢一定以 z 結尾、擊殺數沒有。
    /// 2. 「寶箱」常讀成「寶」或整個漏掉（「1537 z 寶 3009 ~」「0 z 30 z」），價格最後的 z 也常讀成「~」，所以價格只認位置不認字。
    /// 刻意不去掉空白：擊殺數和金錢是不同行，去掉空白會黏成一個數字（「67381537」）；數字和 z 之間的空白由規則式容許。
    /// 千分位逗號只拿掉數字之間的。
    /// </summary>
    internal static Hud? Parse(string text)
    {
        var m = MoneyThenCost().Match(Thousands().Replace(text, ""));
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var money) || !int.TryParse(m.Groups[2].Value, out var cost))
            return null;
        return new Hud(money, cost);
    }

    // 數字之間的逗號（千分位，半形或全形）
    [GeneratedRegex(@"(?<=\d)[,，](?=\d)")]
    private static partial Regex Thousands();

    // 金錢＋z，之後（中間可以是「寶箱」或任何非數字）的下一個數字是價格
    [GeneratedRegex(@"(\d+)\s*[zZ]\D*?(\d+)")]
    private static partial Regex MoneyThenCost();
}
