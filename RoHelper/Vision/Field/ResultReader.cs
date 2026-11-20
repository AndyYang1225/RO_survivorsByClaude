using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>
/// 結算畫面讀到的內容（同 JS recFinish 的 title／result／exp／dps／dpsTotal）。
/// 本局五格讀不到的欄位是 null（Job、Time 是空字串），同 JS 讀不到時的 null。
/// Exp、Dps、DpsTotal 是 OCR 原文（遊戲小字常漏字，給人看、分析用，不拿來判斷）。
/// </summary>
public sealed record RunResult(
    string Title, string Outcome, string Job, string Time, int? Secs, int? Level, int? Kills, int? Bosses,
    string Exp, IReadOnlyList<string> Dps, string DpsTotal);

/// <summary>
/// 讀結算視窗（「你存活下來了！」「你死了」）。視窗在畫面水平中央（實測 dpr 1、寬 1904：x 693～1212，寬 520）。
/// 版面（實測，相對於標題那行的垂直中心 ty）：本局五格的數值列約 ty+81～ty+101，五格左緣 = 畫面中心 −236 + 92.4·i、寬 88（× dpr）。
///
/// 為什麼五格要讀很多次再投票：五格的數值是 12 px 粗體小字，Windows OCR 單獨一個字（頭目「7」）、兩位數（等級「58」）常整個漏掉，
/// 或把「9214」讀成「92 4」「92t4」，而且漏哪個跟放大倍數、裁切範圍有關，沒有一種讀法三張 fixture 全對（實測見 ResultReaderTests）。
/// 所以同一格用好幾種讀法（整個視窗 ×2／×3、數值列 ×3／×4、數值列紅色通道 ×3、每一格 ×3／×4／紅色通道 ×5），
/// 每種讀法讀到「格式正確」的值就投一票，取票數最多的（同票取先讀到的）。總共約 15 次 OCR、幾百毫秒，一局只讀一次，可以接受。
/// 標題、經驗、傷害用整個視窗 ×3 那次（實測比 ×2 少漏字）。
/// </summary>
public sealed partial class ResultReader(OcrReader ocr, IReadOnlyList<string>? jobs = null)
{
    /// <summary>結算視窗寬度（× dpr），以畫面水平中心為準</summary>
    private const int WindowWidth = 520;

    /// <summary>五格：第一格左緣相對畫面中心的位移、格距、格寬（× dpr）</summary>
    private const double ChipLeft = -236, ChipStep = 92.4, ChipWidth = 88;

    /// <summary>數值列相對標題那行垂直中心的上下緣（× dpr）</summary>
    private const int RowTop = 81, RowBottom = 101;

    private static readonly double[] WindowScales = [3, 2];
    private static readonly double[] RowScales = [3, 4];
    private static readonly double[] ChipScales = [3, 4];

    /// <summary>讀不到標題、或五格一格都讀不到（多半不是結算畫面）回 null</summary>
    public async Task<RunResult?> ReadAsync(Frame f, double dpr)
    {
        var win = Rectangle.Intersect(
            new Rectangle(f.Width / 2 - (int)(WindowWidth / 2 * dpr), 0, (int)(WindowWidth * dpr), f.Height),
            new Rectangle(0, 0, f.Width, f.Height));
        var passes = new List<IReadOnlyList<OcrLine>>();
        foreach (var s in WindowScales)
            passes.Add(await ocr.ReadAsync(f, win, s));
        var text = passes[0];
        if (text.Count == 0)
            return null;
        var titleLine = text.MinBy(l => l.Bounds.Y)!;
        var title = CleanTitle(titleLine.Text);
        if (title.Length == 0)
            return null;

        // 五格的位置（以標題那行為準：視窗在矮的畫面會貼著上緣、在高的畫面可能置中）
        var ty = titleLine.Bounds.Y + titleLine.Bounds.Height / 2;
        var row = Rectangle.FromLTRB(
            f.Width / 2 + (int)(ChipLeft * dpr), ty + (int)(RowTop * dpr),
            f.Width / 2 + (int)((ChipLeft + ChipStep * 4 + ChipWidth) * dpr), ty + (int)(RowBottom * dpr));
        var chips = Enumerable.Range(0, 5)
            .Select(i => new Rectangle(f.Width / 2 + (int)((ChipLeft + ChipStep * i) * dpr), row.Y, (int)(ChipWidth * dpr), row.Height))
            .ToArray();

        // 每種讀法的候選值（每格一份清單，依讀到的順序）
        var votes = Enumerable.Range(0, 5).Select(_ => new List<string>()).ToArray();
        void Vote(IEnumerable<OcrLine> lines)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = chips[i];
                var inChip = lines.Where(l => Center(l.Bounds) is var p && c.Contains(p)).OrderBy(l => l.Bounds.X);
                var v = Normalize(i, string.Join(" ", inChip.Select(l => l.Text)));
                if (v is not null)
                    votes[i].Add(v);
            }
        }
        foreach (var p in passes)
            Vote(p);
        foreach (var s in RowScales)
            Vote(await ocr.ReadAsync(f, row, s));
        if (OcrPrep.RedStretch(f, row, Pad, invert: false) is { } prepRow)
            Vote(Shift(await ocr.ReadAsync(prepRow, new Rectangle(0, 0, prepRow.Width, prepRow.Height), 3), row));
        for (var i = 0; i < 5; i++)
        {
            var c = chips[i];
            var reads = new List<IReadOnlyList<OcrLine>>();
            foreach (var s in ChipScales)
                reads.Add(await ocr.ReadAsync(f, c, s));
            if (OcrPrep.RedStretch(f, c, Pad, invert: false) is { } prep)
                reads.Add(await ocr.ReadAsync(prep, new Rectangle(0, 0, prep.Width, prep.Height), 5));
            foreach (var r in reads)
                if (Normalize(i, string.Join(" ", r.OrderBy(l => l.Bounds.X).Select(l => l.Text))) is { } v)
                    votes[i].Add(v);
        }
        var pick = votes.Select(Winner).ToArray();
        if (pick.All(v => v is null))
            return null;

        var time = pick[1] ?? "";
        var m = TimeRe().Match(time);
        var (exp, dps, total) = ParseText(text, win, dpr);
        return new RunResult(
            title, OutcomeOf(title), pick[0] ?? "", time,
            m.Success ? int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value) : null,
            ToInt(pick[2]), ToInt(pick[3]), ToInt(pick[4]),
            exp, dps, total);
    }

    /// <summary>紅色通道前處理時四周補的空白（實測補 10 px 讀得比較穩）</summary>
    private const int Pad = 10;

    /// <summary>前處理過的圖讀到的座標換回截圖座標</summary>
    private static IEnumerable<OcrLine> Shift(IEnumerable<OcrLine> lines, Rectangle origin)
        => lines.Select(l => l with { Bounds = new Rectangle(l.Bounds.X - Pad + origin.X, l.Bounds.Y - Pad + origin.Y, l.Bounds.Width, l.Bounds.Height) });

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>票數最多的值，同票取先讀到的；沒有票回 null</summary>
    internal static string? Winner(List<string> votes)
        => votes.Count == 0 ? null : votes.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => votes.IndexOf(g.Key)).First().Key;

    private static int? ToInt(string? s) => s is not null && int.TryParse(s, out var v) ? v : null;

    /// <summary>
    /// 一格讀到的文字 → 正規化的值；格式不對（漏字、混了別的字）回 null，不投票。
    /// 0：職業（有職業清單時要對得上清單，回清單裡的名字；沒清單時要是純中文）；1：時間「分:秒」（冒號必須讀到，否則「7 0」會被當成 70 秒）；
    /// 2～4：純數字（「92 4」這種中間有空白的不算：分不出漏了幾個字）。
    /// 時間和數字格裡的 I、l、|、t 當 1，O、o 當 0（這幾格本來就只有數字）：實測 Windows OCR 常把這套字型的 1 讀成 t、0 讀成 O（「92t4」「7 : IO」）。
    /// </summary>
    internal string? Normalize(int chip, string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0)
            return null;
        if (chip == 0)
        {
            var job = OcrReader.Compact(s).Replace(" ", "");
            if (jobs is { Count: > 0 })
                return JobNames.Match(job, jobs);
            return CjkOnly().IsMatch(job) ? job : null;
        }
        var digits = s.Replace('I', '1').Replace('l', '1').Replace('|', '1').Replace('t', '1').Replace('O', '0').Replace('o', '0');
        if (chip == 1)
        {
            var m = TimeRe().Match(digits);
            return m.Success && int.Parse(m.Groups[2].Value) < 60 ? $"{int.Parse(m.Groups[1].Value)}:{m.Groups[2].Value}" : null;
        }
        var n = digits.Replace(",", "");
        return Number().IsMatch(n) ? int.Parse(n).ToString() : null;
    }

    /// <summary>
    /// 經驗、傷害（整個視窗那次的 OCR 原文）：
    /// Exp＝含「Lv」的行（基本經驗、職業經驗），用「 / 」接起來；
    /// 同一列（垂直中心差 ≤ 6·dpr）的字依 x 排好接成一列，含「DPS」的第一列是「本局傷害」標題列，取右半邊＝DpsTotal，之後含 DPS 的每一列＝Dps 一筆。
    /// </summary>
    internal static (string Exp, List<string> Dps, string Total) ParseText(IReadOnlyList<OcrLine> lines, Rectangle win, double dpr)
    {
        var exp = string.Join(" / ", lines.Where(l => l.Text.Contains("Lv")).OrderBy(l => l.Bounds.Y).Select(l => l.Text));
        var rows = new List<List<OcrLine>>();
        foreach (var l in lines.OrderBy(l => Center(l.Bounds).Y))
        {
            var last = rows.LastOrDefault();
            if (last is not null && Math.Abs(Center(l.Bounds).Y - Center(last[0].Bounds).Y) <= 6 * dpr)
                last.Add(l);
            else
                rows.Add([l]);
        }
        var dpsRows = rows.Where(r => r.Any(l => l.Text.Contains("DPS", StringComparison.OrdinalIgnoreCase))).ToList();
        var total = dpsRows.Count > 0
            ? string.Join(" ", dpsRows[0].Where(l => Center(l.Bounds).X > win.X + win.Width / 2).OrderBy(l => l.Bounds.X).Select(l => l.Text))
            : "";
        var dps = dpsRows.Skip(1).Select(r => string.Join(" ", r.OrderBy(l => l.Bounds.X).Select(l => l.Text))).ToList();
        return (exp, dps, total);
    }

    /// <summary>標題：去掉空白與開頭的「◆」，半形驚嘆號換成全形（遊戲原文是「你存活下來了！」）</summary>
    internal static string CleanTitle(string raw)
        => Regex.Replace(raw, @"\s+", "").TrimStart('◆', '♦', '·', '•').Replace('!', '！');

    /// <summary>結果分類，同 JS outcomeOf（規則與順序照抄）</summary>
    public static string OutcomeOf(string title)
    {
        if (Regex.IsMatch(title, "放棄|abandon", RegexOptions.IgnoreCase))
            return "abandoned";
        if (Regex.IsMatch(title, "活下|存活|生還|倖存|勝利|通關|突破|超越|sobreviv|superad|surviv|clear", RegexOptions.IgnoreCase))
            return "clear";
        if (Regex.IsMatch(title, "死|陣亡|倒下|落敗|muert|caíd|died|dead|fallen", RegexOptions.IgnoreCase))
            return "death";
        if (Regex.IsMatch(title, "結束|terminad|ended|over", RegexOptions.IgnoreCase))
            return "ended";
        return "unknown";
    }

    // 分:秒（冒號可能讀成全形、分號或點，前後可能有空白）
    [GeneratedRegex(@"^(\d{1,3})\s*[:：;.]\s*(\d{2})$")]
    private static partial Regex TimeRe();

    [GeneratedRegex(@"^\d{1,7}$")]
    private static partial Regex Number();

    [GeneratedRegex(@"^[一-鿿]+$")]
    private static partial Regex CjkOnly();
}
