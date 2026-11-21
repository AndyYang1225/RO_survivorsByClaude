using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>
/// 讀畫面上方的公告（王的喊話、系統公告），給躲鐘用（JS bellAnnounce 讀 #hud 裡含「鐘」的文字；視覺模式只能 OCR 畫面）。
/// 實測（2026-11-21，dpr 1，視窗 1904×682）：系統公告是白底圓角框、深藍字，在 x 742～1162、y 98～148（野外2寶箱 上方的維護公告）。
/// 待實機校正：王的喊話還沒在實機看過，位置、顏色都不知道，所以範圍取大一點（畫面水平中央 ±420、y 90～200）。
/// </summary>
public sealed partial class BannerReader(OcrReader ocr)
{
    /// <summary>OCR 公告範圍，回傳每行文字用空白接起來（讀不到字回空字串）</summary>
    public async Task<string> ReadAsync(Frame f, double dpr)
    {
        var lines = await ocr.ReadAsync(f, Area(f, dpr));
        return string.Join(" ", lines.Select(l => l.Text));
    }

    /// <summary>
    /// OCR 範圍：畫面水平中央左右各 420·dpr、y 90～280·dpr，夾在畫面內。
    /// 實測（2026-11-21 斐揚洞穴）：「MVP 月夜貓 出現了！」「躲到鐘下！」是畫面中央的大白字，在 y 205～240；原本的 y 90～200 剛好漏掉
    /// </summary>
    internal static Rectangle Area(Frame f, double dpr)
    {
        var half = (int)(420 * dpr);
        var r = new Rectangle(f.Width / 2 - half, (int)(90 * dpr), half * 2, (int)(190 * dpr));
        return Rectangle.Intersect(r, new Rectangle(0, 0, f.Width, f.Height));
    }

    /// <summary>
    /// 公告要躲鐘多久（毫秒，同 JS bellStep）：含「躲到鐘下／鐘開啟／躲到下面／被封印」→ 15 秒；含「鐘」又含「太遲」→ 4 秒（後面的優先，同 JS）；都沒有回 null。
    /// 先拿掉所有空白（OCR 會在字之間插空白）。
    /// 跟 JS 小差異：JS 只看含「鐘」的那段文字；OCR 可能把筆畫多的「鐘」讀錯，所以前四個詞不要求另外有「鐘」（這四個詞本身夠特別）；
    /// 「太遲」很普通，照 JS 要求同時有「鐘」。
    /// </summary>
    public static int? DodgeMs(string text)
    {
        var t = Regex.Replace(text, @"\s+", "");
        int? ms = null;
        if (Start().IsMatch(t))
            ms = 15_000;
        if (t.Contains('鐘') && t.Contains("太遲"))
            ms = 4000;
        return ms;
    }

    // 「躲.{0,2}鐘下」：實測 OCR 把「躲到鐘下！」讀成「躲鐘下 !」「躲到钀下 !」「躲到釒 …」（2026-11-21 斐揚洞穴），漏字、錯字都有；
    // 「躲到.下」：「鐘」讀錯成別的字時
    [GeneratedRegex("躲.{0,2}鐘下|躲到.下|鐘開啟|躲到下面|被封印")]
    private static partial Regex Start();
}
