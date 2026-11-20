using System.Text.RegularExpressions;

using RoHelper.Vision.Field;

namespace RoHelper.Vision;

/// <summary>遠征清單上看得到的一張地圖卡：地名、要點的位置（OCR 到的那一行，截圖像素）</summary>
public sealed record StageCard(string Stage, Rectangle Bounds);

/// <summary>
/// 遠征視窗讀到的內容：清單上看得到的地圖卡（由上到下）、右邊詳細資料的地名（＝目前選中的地圖，讀不到是空字串）、
/// 清單的範圍（捲動滑鼠滾輪的位置用）。
/// </summary>
public sealed record StageListView(IReadOnlyList<StageCard> Cards, string Selected, Rectangle List);

/// <summary>
/// OCR 遠征視窗（JS stageStep 讀 #stages 的卡片；視覺模式只能 OCR 畫面）。位置都以「遠征標題」範本找到的位置為準。
/// 實測（2026-11-21，遠征清單、遠征選好地圖，dpr 1，視窗 1904×682）：標題範本（「遠征」兩個字）在 (522, 32)；
/// 左邊清單 x 505～795、y 95～582（到畫面底部 −100，對話框高度跟著視窗），每張卡 88 px 高，一次看得到 6 張、可以捲動；
/// 右邊詳細資料的大地名在 x 822～1122、y 205～255。
/// 清單上的地名是 14 px 粗體，放大 2 倍讀常漏字（「普隆德拉原野」→「德拉原野」、「斐揚洞穴」→「斐穴」），所以用 <see cref="JobNames.Closest"/> 對已知地名；
/// 每張卡第二行「MVP 名字」是英文、讀得很穩，地名對不到時用 MVP 名字（<see cref="StageBosses"/>）對。
/// 右邊的大地名放大 2 倍讀全部正確（「普隆德拉城」「斐揚森林」）。
/// </summary>
public sealed partial class ExpeditionReader(OcrReader ocr, IReadOnlyList<string> stages)
{
    /// <summary>約 2 次 OCR（100～200 ms），遠征視窗開著時每輪讀</summary>
    public async Task<StageListView> ReadAsync(Frame f, double dpr, Rectangle title)
    {
        var list = ListArea(f, dpr, title);
        var lines = await ocr.ReadAsync(f, list, 2);
        var cards = new List<StageCard>();
        foreach (var line in lines)
            if (MatchLine(line.Text, stages) is { } stage && cards.All(c => c.Stage != stage))
                cards.Add(new StageCard(stage, line.Bounds));
        var sel = Rectangle.Intersect(new Rectangle(title.X + (int)(300 * dpr), title.Y + (int)(173 * dpr), (int)(300 * dpr), (int)(50 * dpr)), Bounds(f));
        var selLines = sel.Width > 0 && sel.Height > 0 ? await ocr.ReadAsync(f, sel, 2) : [];
        var selected = selLines.Select(l => JobNames.Closest(NonCjk().Replace(l.Text, ""), stages)).FirstOrDefault(s => s is not null) ?? "";
        return new StageListView(cards, selected, list);
    }

    /// <summary>清單的 OCR 範圍：標題左邊 17·dpr 起、寬 290·dpr，從標題下方 63·dpr 到畫面底部 −100·dpr</summary>
    internal static Rectangle ListArea(Frame f, double dpr, Rectangle title)
    {
        var top = title.Y + (int)(63 * dpr);
        var bottom = f.Height - (int)(100 * dpr);
        return Rectangle.Intersect(new Rectangle(title.X - (int)(17 * dpr), top, (int)(290 * dpr), Math.Max(0, bottom - top)), Bounds(f));
    }

    /// <summary>
    /// 一行 OCR 文字 → 地名：中文字至少 2 個就對已知地名（至少對到 2 個字、唯一最高）；
    /// 對不到時，一行裡有某張地圖 MVP 名字最長的那個英文字（不分大小寫，例如「golden」「moonlight」）就是那張地圖。都不是回 null。
    /// </summary>
    internal static string? MatchLine(string text, IReadOnlyList<string> stages)
    {
        var cjk = NonCjk().Replace(text, "");
        if (cjk.Length >= 2 && JobNames.Closest(cjk, stages) is { } stage)
            return stage;
        var lower = text.ToLowerInvariant();
        foreach (var (name, bosses) in StageBosses.All)
        {
            if (!stages.Contains(name) || bosses.FirstOrDefault(b => b.Kind == "MVP") is not { } mvp)
                continue;
            var word = mvp.Name.Split(' ').OrderByDescending(w => w.Length).First().ToLowerInvariant();
            if (lower.Contains(word))
                return name;
        }
        return null;
    }

    private static Rectangle Bounds(Frame f) => new(0, 0, f.Width, f.Height);

    [GeneratedRegex(@"[^一-鿿]")]
    private static partial Regex NonCjk();
}
