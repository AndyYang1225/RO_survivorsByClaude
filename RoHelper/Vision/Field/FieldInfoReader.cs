using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>
/// 野外畫面上的地名、小地圖座標、職業。Stage／Job 讀不到是空字串，Tile 讀不到是 null。
/// Tile 是小地圖標題上的格子座標（之後 bossSpot 可以改用格子；目前只記下來）。
/// </summary>
public sealed record FieldInfo(string Stage, Point? Tile, string Job, string Name = "");

/// <summary>
/// 讀小地圖標題列「洛陽 (136, 107)」（右上角面板最上面，實測 dpr 1：x W−173～W−13、y 12～37）與
/// 左上角角色框第一行「&lt;角色名&gt; &lt;職業&gt;」（實測 dpr 1：x 86～330、y 14～34；結算畫面上會被半透明遮罩壓暗一半）。
///
/// 兩個都是 11～12 px 粗體小字，Windows OCR 直接讀常漏字，實測做法：
/// - 標題列：紅色通道前處理（<see cref="OcrPrep.RedStretch"/>）後用幾種放大倍數讀、投票。座標常漏一位或少右括號（那次不算），
///   地名「洛陽」七張 fixture 一律讀成「陷」（任何放大倍數、前處理都一樣），所以地名用已知地名表配對，再加一張實測誤讀表（<see cref="Misreads"/>）。
/// - 角色框：整行一起讀時職業常漏字（「魔法師」→「法」、「巫師」→「巫」，還會跟角色名黏在一起），
///   所以先用像素找出最後一段文字（職業前面有一個空白，字距最多 2～3 px、空白約 5 px），裁職業那一段（往左多帶一點角色名當上下文），
///   用幾種讀法讀、各自用 <see cref="JobNames"/> 對職業清單，再投票。
/// </summary>
public sealed partial class FieldInfoReader(OcrReader ocr, IReadOnlyList<string> jobs, IReadOnlyList<string> stages)
{
    /// <summary>
    /// 實測（2026-11-21，野外1／野外4／結算存活1／結算存活2／結算死亡）OCR 一律讀錯的地名 → 正確地名。
    /// 只收「穩定讀錯成同一個字」的：偶爾讀錯的不收，以免把別的地名對錯。用「包含」比對：前後常多讀到標題列邊框（「一陷」「陷緆」）。
    /// </summary>
    private static readonly Dictionary<string, string> Misreads = new() { ["陷"] = "洛陽" };

    /// <summary>職業前面的空白至少這麼寬（× dpr）才算「名字和職業之間的空白」：實測空白 5 px，字距 1～2 px</summary>
    private const int WordGap = 4;

    /// <summary>標題列的讀法（前處理時四周補的空白 px、放大倍數）：每種讀法漏的字不一樣，投票（實測見 FieldInfoReaderTests）</summary>
    private static readonly (int Pad, double Scale)[] TitleReads = [(0, 3), (6, 3), (12, 3), (0, 4), (6, 4), (12, 4)];

    /// <summary>
    /// 職業的讀法（往左多帶幾 px 的角色名當上下文（× dpr）、放大倍數、要不要前處理）：
    /// 只裁職業那一段時 Windows OCR 幾乎什麼都讀不到，多帶一點左邊的字才讀得出來；帶多少讀得最好每張圖不一樣，所以投票。
    /// </summary>
    private static readonly (int Context, double Scale, bool Prep)[] JobReads =
        [(10, 3, true), (20, 3, true), (30, 3, true), (10, 4, true), (20, 4, true), (30, 4, true), (20, 3, false), (20, 4, false)];

    /// <summary>約 14 次 OCR（幾百毫秒）；進野外後讀到為止，不是每輪都讀</summary>
    public async Task<FieldInfo> ReadAsync(Frame f, double dpr)
    {
        var (stage, tile) = await ReadTitleAsync(f, dpr);
        return new FieldInfo(stage, tile, await ReadJobAsync(f, dpr), await ReadNameAsync(f, dpr));
    }

    /// <summary>
    /// 角色名（組隊用：隊長邀請、隊員確認邀請是自己的帳號）：角色框第一行職業前面那一段，兩種讀法投票；讀不到或兩次不一致回空字串。
    /// 只拿來跟好友名單、邀請對話框做模糊比對（<see cref="JobNames.Closest"/>），讀錯一兩個字沒關係。
    /// </summary>
    private async Task<string> ReadNameAsync(Frame f, double dpr)
    {
        var band = Rectangle.Intersect(new Rectangle((int)(86 * dpr), (int)(14 * dpr), (int)(244 * dpr), (int)(20 * dpr)), new Rectangle(0, 0, f.Width, f.Height));
        if (band.Width == 0 || band.Height == 0 || LastWord(f, band, dpr) is not { } word)
            return "";
        var r = Rectangle.FromLTRB(band.X, band.Y, word.X - (int)(2 * dpr), band.Bottom);
        if (r.Width < 10 * dpr)
            return "";
        var votes = new List<string>();
        foreach (var scale in new[] { 3.0, 4.0 })
        {
            IReadOnlyList<OcrLine> lines = OcrPrep.RedStretch(f, r, 10) is { } p
                ? await ocr.ReadAsync(p, new Rectangle(0, 0, p.Width, p.Height), scale)
                : await ocr.ReadAsync(f, r, scale);
            var name = OcrReader.Compact(string.Concat(lines.Select(l => l.Text))).Trim();
            if (name.Length >= 2)
                votes.Add(name);
        }
        return votes.Count == 2 && votes[0] == votes[1] ? votes[0] : "";
    }

    private async Task<(string Stage, Point? Tile)> ReadTitleAsync(Frame f, double dpr)
    {
        var area = new Rectangle(f.Width - (int)(173 * dpr), (int)(12 * dpr), (int)(160 * dpr), (int)(25 * dpr));
        var stageVotes = new List<string>();
        var tileVotes = new List<string>();
        foreach (var (pad, scale) in TitleReads)
        {
            if (OcrPrep.RedStretch(f, area, pad) is not { } prep)
                return ("", null);
            var lines = await ocr.ReadAsync(prep, new Rectangle(0, 0, prep.Width, prep.Height), scale);
            var (stage, tile) = ParseTitle(string.Join(" ", lines.Select(l => l.Text)), stages);
            if (stage.Length > 0)
                stageVotes.Add(stage);
            if (tile is { } t)
                tileVotes.Add($"{t.X},{t.Y}");
        }
        var tileWin = ResultReader.Winner(tileVotes)?.Split(',');
        return (ResultReader.Winner(stageVotes) ?? "", tileWin is null ? null : new Point(int.Parse(tileWin[0]), int.Parse(tileWin[1])));
    }

    /// <summary>
    /// 「地名 (x, y)」→ 地名、座標。座標的 t、I、l 當 1，O、o 當 0（實測讀錯的樣子）；括號（含右括號）、逗號不完整時座標當讀不到。
    /// 地名：已知地名（完全相同或對到 2 個字以上）→ 實測誤讀表 → 至少 2 個中文字就照原文 → 都不是就空字串。
    /// </summary>
    internal static (string Stage, Point? Tile) ParseTitle(string text, IReadOnlyList<string> stages)
    {
        var paren = text.IndexOf('(');
        var rawName = Regex.Replace(paren >= 0 ? text[..paren] : text, @"[^一-鿿]", "");
        Point? tile = null;
        if (paren >= 0)
        {
            var digits = text[paren..].Replace('t', '1').Replace('I', '1').Replace('l', '1').Replace('O', '0').Replace('o', '0');
            var m = TileRe().Match(digits);
            if (m.Success)
                tile = new Point(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
        }
        string stage;
        if (rawName.Length == 0)
            stage = "";
        else if (JobNames.Closest(rawName, stages) is { } known)
            stage = known;
        else if (Misreads.FirstOrDefault(m => rawName.Contains(m.Key)) is { Key: not null } fix)
            stage = fix.Value;
        else
            stage = rawName.Length >= 2 ? rawName : "";
        return (stage, tile);
    }

    private async Task<string> ReadJobAsync(Frame f, double dpr)
    {
        var band = Rectangle.Intersect(new Rectangle((int)(86 * dpr), (int)(14 * dpr), (int)(244 * dpr), (int)(20 * dpr)), new Rectangle(0, 0, f.Width, f.Height));
        if (band.Width == 0 || band.Height == 0 || LastWord(f, band, dpr) is not { } word)
            return "";
        var votes = new List<string>();
        foreach (var (context, scale, prep) in JobReads)
        {
            var r = Rectangle.Intersect(Rectangle.FromLTRB(word.X - (int)(context * dpr), word.Y, word.Right, word.Bottom), band);
            IReadOnlyList<OcrLine> lines;
            if (!prep)
                lines = await ocr.ReadAsync(f, r, scale);
            else if (OcrPrep.RedStretch(f, r, 10) is { } p)
                lines = await ocr.ReadAsync(p, new Rectangle(0, 0, p.Width, p.Height), scale);
            else
                continue;
            if (JobNames.Match(string.Concat(lines.Select(l => l.Text)), jobs) is { } job)
                votes.Add(job);
        }
        return ResultReader.Winner(votes) ?? "";
    }

    /// <summary>
    /// 在 band 裡找最後一段文字（截圖座標，左右各多留 3·dpr）：每一欄取「三個通道最小值」的最大值（白字、灰字三個通道都亮，藍底的紅色很暗），
    /// 比底色（所有欄的中位數）亮出一截的欄＝有字；從最右邊有字的欄往左走，遇到連續 <see cref="WordGap"/>·dpr 欄沒字＝職業前面的空白。
    /// 字和底色的亮度差太小（沒有字）、或找不到空白（分不出名字和職業）回 null。
    /// </summary>
    internal static Rectangle? LastWord(Frame f, Rectangle band, double dpr)
    {
        var col = new int[band.Width];
        for (var x = 0; x < band.Width; x++)
            for (var y = band.Top; y < band.Bottom; y++)
            {
                var (r, g, b) = f.At(band.X + x, y);
                col[x] = Math.Max(col[x], Math.Min(r, Math.Min(g, b)));
            }
        var sorted = (int[])col.Clone();
        Array.Sort(sorted);
        int bg = sorted[sorted.Length / 2], top = sorted[^1];
        if (top - bg < 30)
            return null;
        var threshold = bg + Math.Max(20, (top - bg) * 0.4);
        bool Ink(int x) => col[x] > threshold;

        var end = band.Width - 1;
        while (end >= 0 && !Ink(end))
            end--;
        if (end < 0)
            return null;
        var gap = Math.Max(2, (int)Math.Round(WordGap * dpr));
        for (int x = end, blank = 0; x >= 0; x--)
        {
            blank = Ink(x) ? 0 : blank + 1;
            if (blank < gap)
                continue;
            var start = x + blank;
            var pad = (int)(3 * dpr);
            return Rectangle.FromLTRB(band.X + start - pad, band.Y, band.X + end + 1 + pad, band.Bottom);
        }
        return null;
    }

    // (x, y)：分隔可能讀成逗號、頓號、點。右括號一定要讀到：實測沒讀到右括號時最後一個數字常少一位（「( 136 , 10」）
    [GeneratedRegex(@"\(\s*(\d{1,4})\s*[,，、.]\s*(\d{1,4})\s*\)")]
    private static partial Regex TileRe();
}
