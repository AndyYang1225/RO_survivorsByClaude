namespace RoHelper.Vision.Field;

/// <summary>
/// 讀畫面左上角角色框裡的 HP 條，回傳 0～1（同 JS hpPct ÷ 100）。純像素判斷，不做 OCR（每輪都讀也很便宜）。
/// 實測（2026-11-21，dpr 1，野外1／野外4／野外2寶箱，三張都是滿血）：
/// HP 條是圓角長條，深色外框 (42,46,56)，外框 y 43、58，中間那幾列外框 x 94～310（上下兩列因為圓角只有 x≈101～303）；
/// 填滿部分是藍色漸層：上淺下深、左深右淺，最深 (42,115,224)、最淺 (187,223,252)；條上面印著「112 / 112」白字（約 x 180～225、y 46～55）。
/// 框外是角色框的淺色底 (235,237,242)。下面另有一條短的深色條（y 67～73、x≈120～265，沒填滿），
/// 它的空條顏色是深色漸層 (39,42,51)～(80,86,100)，HP 條空的部分應該也是這個樣子（三張 fixture 都滿血，看不到）。
/// </summary>
public static class HpReader
{
    /// <summary>外框顏色（實測 (42,46,56)）與容許誤差</summary>
    private static readonly (int R, int G, int B) Border = (42, 46, 56);
    private const int BorderTol = 12;

    /// <summary>
    /// 上外框的深色連續長度至少要這麼長（× dpr）才算 HP 條。實測 HP 條上外框約 203 px，
    /// 下面那條短條約 145 px，門檻取中間把它排除；只當下限用，條的實際寬度由像素找。
    /// </summary>
    private const int MinRun = 170;

    /// <summary>找不到 HP 條（不在野外、角色框被擋住）回 null；找到但完全沒有藍色回 0</summary>
    public static double? Read(Frame f, double dpr)
    {
        if (FindBar(f, dpr) is not { } bar)
            return null;
        var (top, bottom, left, right) = bar;
        // 每一欄只要外框內任何一列是藍色就算填滿：白字印在條的正中間，只看中間那列會被白字切斷；
        // 空的部分是深灰，白字的反鋸齒（白混深灰）也是灰的，不會被當成藍色
        var filled = -1;
        for (var x = right - 1; x > left && filled < 0; x--)
            for (var y = top + 1; y < bottom; y++)
                if (IsFill(f.At(x, y)))
                {
                    filled = x;
                    break;
                }
        return filled < 0 ? 0 : (double)(filled - left) / (right - left - 1);
    }

    /// <summary>
    /// 找 HP 條的外框（截圖像素）：在左上角（x 0～420·dpr、y 0～120·dpr）由上往下找第一列有夠長深色外框的列＝上外框；
    /// 從外框左段往下走到淺色底的前一列＝下外框（不用找下外框的深色列：HP 空的時候條裡面也是深色）；
    /// 再從上下的中間那列往左右走到淺色底，得到圓角兩端的外框。高度不合理（不在 8～24·dpr）就當找不到。
    /// </summary>
    private static (int Top, int Bottom, int Left, int Right)? FindBar(Frame f, double dpr)
    {
        int w = Math.Min(f.Width, (int)(420 * dpr)), h = Math.Min(f.Height, (int)(120 * dpr));
        var minRun = (int)(MinRun * dpr);
        for (var y = 0; y < h; y++)
        {
            if (LongestRun(f, y, w) is not { } run || run.End - run.Start + 1 < minRun)
                continue;
            var probe = run.Start + (int)(5 * dpr);
            var bottom = y + 1;
            while (bottom + 1 < f.Height && !IsLight(f.At(probe, bottom + 1)))
                bottom++;
            if (bottom - y < 8 * dpr || bottom - y > 24 * dpr)
                return null;
            var mid = (y + bottom) / 2;
            int left = run.Start, right = run.End;
            while (left > 0 && !IsLight(f.At(left - 1, mid)))
                left--;
            while (right + 1 < f.Width && !IsLight(f.At(right + 1, mid)))
                right++;
            return right - left > 2 ? (y, bottom, left, right) : null;
        }
        return null;
    }

    /// <summary>這一列（x 0～w）最長的一段外框色</summary>
    private static (int Start, int End)? LongestRun(Frame f, int y, int w)
    {
        (int Start, int End)? best = null;
        var start = -1;
        for (var x = 0; x <= w; x++)
        {
            if (x < w && Near(f.At(x, y), Border, BorderTol))
            {
                if (start < 0)
                    start = x;
                continue;
            }
            if (start >= 0 && (best is null || x - 1 - start > best.Value.End - best.Value.Start))
                best = (start, x - 1);
            start = -1;
        }
        return best;
    }

    /// <summary>
    /// 填滿的藍色：實測漸層從 (42,115,224) 到 (187,223,252)，B 都 ≥ 219、B−R 都 ≥ 65；
    /// 門檻放寬到 B ≥ 170、B−R ≥ 50，白字邊緣混到藍色的點也算（它們本來就在填滿的範圍裡）。空條的深灰 B 最多 100，不會誤認。
    /// </summary>
    private static bool IsFill((byte R, byte G, byte B) c) => c.B >= 170 && c.B - c.R >= 50;

    /// <summary>角色框的淺色底（實測 (235,237,242) 左右）；填滿的藍色 R 最多 187，不會被當成底</summary>
    private static bool IsLight((byte R, byte G, byte B) c) => c.R > 200 && c.G > 200 && c.B > 200;

    private static bool Near((byte R, byte G, byte B) c, (int R, int G, int B) t, int tol)
        => Math.Abs(c.R - t.R) <= tol && Math.Abs(c.G - t.G) <= tol && Math.Abs(c.B - t.B) <= tol;
}
