using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>
/// OCR 讀到的職業文字 → 職業清單（面板的職業、JS 的 JOB_SKILLS 鍵）裡的名字。
/// 為什麼要模糊比對：遊戲的職業字是 12 px 粗體小字，Windows OCR 常漏字（「魔法師」讀成「法師」「廗法」、「巫師」讀成「用巫」，「用」是角色名的最後一個字）。
/// 職業是固定清單，所以用「最長共同子序列」配對，取唯一最高分的；分數一樣（分不出來）就當讀不到。
/// 職業只要對到 1 個字就算：「師」「士」「人」這種很多職業都有的字一定同分（讀不到），會唯一對到的只有「法」「巫」這種只有一個職業有的字。
/// 寧可讀不到（下次再讀）也不要寫錯職業：寫錯的話技能規則整套會套到別的職業上。
/// 「吟遊詩人／舞孃」同 JS detectJob：讀到其中一個就是清單裡的「吟遊詩人/舞孃」。
/// </summary>
public static partial class JobNames
{
    /// <summary>只比中文字（OCR 常混進角色名的英數、標點）；配對不到回 null</summary>
    public static string? Match(string ocr, IReadOnlyList<string> jobs)
    {
        ocr = NonCjk().Replace(ocr, "");
        if (ocr.Length == 0)
            return null;
        if (ocr.Contains("吟遊詩人") || ocr.Contains("舞孃"))
            return jobs.FirstOrDefault(j => j.Contains("吟遊詩人") || j.Contains("舞孃"));
        return Closest(ocr, jobs, minScore: 1);
    }

    /// <summary>
    /// 清單裡最像 ocr 的名字（名字裡的「/」分隔的每一個別名都比）：完全相同直接回；否則最長共同子序列唯一最高、而且至少 minScore 個字。對不到回 null。
    /// 地名（<see cref="FieldInfoReader"/>）也用這個（地名要對到 2 個字：地名清單不完整，1 個字太容易對錯）。
    /// </summary>
    public static string? Closest(string ocr, IReadOnlyList<string> names, int minScore = 2)
    {
        if (ocr.Length == 0)
            return null;
        if (names.Contains(ocr))
            return ocr;
        var scored = names.Select(n => (Name: n, Score: n.Split('/').Max(alt => Lcs(ocr, alt)))).OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0 || scored[0].Score < minScore || (scored.Count > 1 && scored[1].Score == scored[0].Score))
            return null;
        return scored[0].Name;
    }

    /// <summary>最長共同子序列的長度</summary>
    private static int Lcs(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }

    [GeneratedRegex(@"[^一-鿿]")]
    private static partial Regex NonCjk();
}
