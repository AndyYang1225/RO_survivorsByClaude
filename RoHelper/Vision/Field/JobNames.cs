namespace RoHelper.Vision.Field;

/// <summary>
/// OCR 讀到的職業文字 → 職業清單（面板的職業、JS 的 JOB_SKILLS 鍵）裡的名字。
/// 為什麼要模糊比對：遊戲的職業字是 12 px 粗體小字，Windows OCR 常漏字（「魔法師」讀成「法師」「廗法師」、「巫師」讀成「巫」）。
/// 職業是固定清單，所以用「最長共同子序列」配對，取唯一最高分而且至少對到 2 個字的；分數一樣（分不出來）或太少就當讀不到，
/// 寧可讀不到（下次再讀）也不要寫錯職業：寫錯的話技能規則整套會套到別的職業上。
/// 「吟遊詩人／舞孃」同 JS detectJob：讀到其中一個就是清單裡的「吟遊詩人/舞孃」。
/// </summary>
public static class JobNames
{
    /// <summary>配對不到回 null</summary>
    public static string? Match(string ocr, IReadOnlyList<string> jobs)
    {
        if (ocr.Length == 0)
            return null;
        if (ocr.Contains("吟遊詩人") || ocr.Contains("舞孃"))
            return jobs.FirstOrDefault(j => j.Contains("吟遊詩人") || j.Contains("舞孃"));
        if (jobs.Contains(ocr))
            return ocr;
        var scored = jobs.Select(j => (Job: j, Score: j.Split('/').Max(alt => Lcs(ocr, alt)))).OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0 || scored[0].Score < 2 || (scored.Count > 1 && scored[1].Score == scored[0].Score))
            return null;
        return scored[0].Job;
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
}
