namespace RoHelper.Vision;

/// <summary>範本比對結果。Score＝1 − 平均灰階差／255，1 代表完全一樣</summary>
public readonly record struct MatchResult(Rectangle Bounds, double Score);

/// <summary>
/// 在截圖的指定範圍裡找跟範本最像的位置（灰階、平均絕對差）。
/// 一般範圍：粗找（逐點試位置、範本每 2 點取 1 點）留最好的幾個候選 → 各自在 ±1 逐點精找，取最好的。
/// 大範圍（超過 <see cref="PyramidArea"/>）：先在縮一半的圖上粗找候選，再回原尺寸在 ±4 逐點精找。
/// 實測 1904×706 整張找 135×47 的範本要 723 ms，NPC 名字這種會移動的東西只能整張找，所以需要金字塔。
/// 刻意不用 OpenCV：OpenCvSharp 的原生 dll 約 60MB，單一 exe 會大很多。
/// 刻意只比灰階：按鈕的形狀和文字才是辨識重點；需要分辨顏色（例如按鈕反灰）時，由呼叫端另外檢查顏色。
/// </summary>
public static class TemplateMatcher
{
    /// <summary>搜尋範圍超過這個面積（像素）才用金字塔；小範圍直接找比較準，也夠快</summary>
    private const int PyramidArea = 250_000;

    /// <summary>範本短邊至少多少 px 才用金字塔：縮一半後至少 6 px 還看得出形狀；名字標籤這種又扁又寬的也要走金字塔</summary>
    private const int PyramidMinSide = 12;

    public static MatchResult? Find(Frame frame, Frame template, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if ((long)region.Width * region.Height <= PyramidArea || Math.Min(template.Width, template.Height) < PyramidMinSide)
            return FindDirect(frame, template, region);

        // 步驟 1：在一半大小的圖上找幾個大概位置；範本縮一半後比範圍大（不太可能）就退回直接找
        var half = new Rectangle(region.X / 2, region.Y / 2, region.Width / 2, region.Height / 2);
        var th = template.Half;
        if (th.Width == 0 || th.Height == 0 || th.Width > half.Width || th.Height > half.Height)
            return FindDirect(frame, template, region);
        // 限制：縮圖是最近鄰取樣，按鈕位置的奇偶跟範本不同時，按鈕上的細字會糊掉。實測「捨棄」按鈕（56×26）在整張畫面找時，
        // 縮圖上逐點找、範本每點都取也排不進前 40 名。所以固定位置的按鈕要把範本的搜尋範圍限縮（小範圍不走金字塔）；
        // 金字塔只適合會移動、範本夠大的東西（NPC 名字）
        var coarse = Candidates(frame.Half, th, Rectangle.Intersect(half, new Rectangle(0, 0, frame.Half.Width, frame.Half.Height)), 2, 2);

        // 步驟 2：每個候選回原尺寸，在 ±4 逐點精找（縮圖是最近鄰，加上粗找每 2 點一格，位置最多偏 3～4 點），取最好的
        return Best(frame, template, region, coarse.Select(c => (c.X * 2, c.Y * 2)), 4);
    }

    private static MatchResult? FindDirect(Frame frame, Frame template, Rectangle region)
    {
        if (template.Width == 0 || template.Height == 0 || template.Width > region.Width || template.Height > region.Height)
            return null;

        // 步驟 1：粗找幾個候選；步驟 2：各自精找（取樣密度不同，分數不能跟粗找比，所以重新算）
        // 逐點試位置（step 1），範本隔點取樣（sample 2）：實測每 2 點一格時，位置奇偶跟範本錯開 1 px 的按鈕（細字對不上）
        // 會輸給對話框裡的大片白色（白底按鈕範本跟白色平面很像）
        return Best(frame, template, region, Candidates(frame, template, region, 1, 2).Select(c => (c.X, c.Y)), 1);
    }

    /// <summary>
    /// 粗找時保留的候選數。只留最佳一點時，實測整張畫面找按鈕會被別處相似的色塊搶走
    /// （「前往野外」被對話框的藍色標題列搶走：粗找時按鈕上的字幾乎看不出來），真正的位置要到精找才分得出來。
    /// </summary>
    private const int CandidateCount = 10;

    /// <summary>粗找（每 step 點一個位置、範本每 sample 點取 1 點），回傳最好的幾個互不重疊的位置</summary>
    private static List<(int X, int Y, double D)> Candidates(Frame frame, Frame template, Rectangle region, int step, int sample)
    {
        var f = frame.Gray;
        var t = template.Gray;
        var maxX = region.Right - template.Width;
        var maxY = region.Bottom - template.Height;
        // 兩個候選離太近（同一個峰附近的鄰點）只留比較好的那個，不然名額會被同一處佔滿
        var sepX = Math.Max(2, template.Width / 2);
        var sepY = Math.Max(2, template.Height / 2);
        var top = new List<(int X, int Y, double D)>();
        var limit = double.MaxValue;
        for (var y = region.Y; y <= maxY; y += step)
            for (var x = region.X; x <= maxX; x += step)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, sample, limit);
                if (d >= limit)
                    continue;
                var i = top.FindIndex(c => Math.Abs(c.X - x) < sepX && Math.Abs(c.Y - y) < sepY);
                if (i >= 0)
                {
                    if (d >= top[i].D)
                        continue;
                    top.RemoveAt(i);
                }
                var at = top.FindIndex(c => c.D > d);
                top.Insert(at < 0 ? top.Count : at, (x, y, d));
                if (top.Count > CandidateCount)
                    top.RemoveAt(top.Count - 1);
                limit = top.Count == CandidateCount ? top[^1].D : double.MaxValue;
            }
        return top;
    }

    /// <summary>每個候選在 ±pad 逐點精找，回傳分數最好的</summary>
    private static MatchResult? Best(Frame frame, Frame template, Rectangle region, IEnumerable<(int X, int Y)> candidates, int pad)
    {
        MatchResult? best = null;
        foreach (var (cx, cy) in candidates)
        {
            var near = Rectangle.Intersect(region,
                new Rectangle(cx - pad, cy - pad, template.Width + pad * 2, template.Height + pad * 2));
            if (near.Width < template.Width || near.Height < template.Height)
                continue;
            var (bx, by, bd) = Scan(frame, template, near, 1, 1);
            var score = 1 - bd / 255;
            if (best is null || score > best.Value.Score)
                best = new MatchResult(new Rectangle(bx, by, template.Width, template.Height), score);
        }
        return best;
    }

    /// <summary>在 region 裡每隔 step 點試一個位置（範本每 sample 點取 1 點），回傳最小差的位置</summary>
    private static (int X, int Y, double D) Scan(Frame frame, Frame template, Rectangle region, int step, int sample)
    {
        var f = frame.Gray;
        var t = template.Gray;
        var maxX = region.Right - template.Width;
        var maxY = region.Bottom - template.Height;
        var best = (X: region.X, Y: region.Y, D: double.MaxValue);
        for (var y = region.Y; y <= maxY; y += step)
            for (var x = region.X; x <= maxX; x += step)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, sample, best.D);
                if (d < best.D)
                    best = (x, y, d);
            }
        return best;
    }

    /// <summary>
    /// 平均絕對差。累積的差已經超過 limit（目前最佳）就提早回傳 MaxValue：這個位置不可能更好了，省下大部分的計算。
    /// </summary>
    private static double Diff(byte[] f, int fw, byte[] t, int tw, int th, int ox, int oy, int sample, double limit)
    {
        long total = (long)((tw + sample - 1) / sample) * ((th + sample - 1) / sample);
        var cap = limit == double.MaxValue ? long.MaxValue : (long)(limit * total);
        long sum = 0;
        for (var y = 0; y < th; y += sample)
        {
            var fr = (oy + y) * fw + ox;
            var tr = y * tw;
            for (var x = 0; x < tw; x += sample)
                sum += Math.Abs(f[fr + x] - t[tr + x]);
            if (sum > cap)
                return double.MaxValue;
        }
        return (double)sum / total;
    }
}
