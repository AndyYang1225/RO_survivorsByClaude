namespace RoHelper.Vision;

/// <summary>範本比對結果。Score＝1 − 平均灰階差／255，1 代表完全一樣</summary>
public readonly record struct MatchResult(Rectangle Bounds, double Score);

/// <summary>
/// 在截圖的指定範圍裡找跟範本最像的位置（灰階、平均絕對差）。
/// 步驟 1：粗找，每 2 點試一個位置、範本每 2 點取 1 點。
/// 步驟 2：精找，在粗找最佳點的 ±2 範圍逐點、範本逐點重算。
/// 刻意不用 OpenCV：OpenCvSharp 的原生 dll 約 60MB，單一 exe 會大很多；搜尋範圍都是按鈕、卡片框這種小區域，純 C# 就夠快。
/// 刻意只比灰階：按鈕的形狀和文字才是辨識重點；需要分辨顏色（例如按鈕反灰）時，由呼叫端另外檢查顏色。
/// </summary>
public static class TemplateMatcher
{
    public static MatchResult? Find(Frame frame, Frame template, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if (template.Width == 0 || template.Height == 0 || template.Width > region.Width || template.Height > region.Height)
            return null;

        var f = frame.Gray;
        var t = template.Gray;
        var maxX = region.Right - template.Width;
        var maxY = region.Bottom - template.Height;

        // 步驟 1：粗找
        var (cx, cy, cd) = (region.X, region.Y, double.MaxValue);
        for (var y = region.Y; y <= maxY; y += 2)
            for (var x = region.X; x <= maxX; x += 2)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, 2, cd);
                if (d < cd)
                    (cx, cy, cd) = (x, y, d);
            }

        // 步驟 2：精找（取樣密度不同，分數不能跟粗找比，所以重新從最大值開始）
        var (bx, by, bd) = (cx, cy, double.MaxValue);
        for (var y = Math.Max(region.Y, cy - 2); y <= Math.Min(maxY, cy + 2); y++)
            for (var x = Math.Max(region.X, cx - 2); x <= Math.Min(maxX, cx + 2); x++)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, 1, bd);
                if (d < bd)
                    (bx, by, bd) = (x, y, d);
            }

        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
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
