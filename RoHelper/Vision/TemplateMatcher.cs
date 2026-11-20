namespace RoHelper.Vision;

/// <summary>範本比對結果。Score＝1 − 平均灰階差／255，1 代表完全一樣</summary>
public readonly record struct MatchResult(Rectangle Bounds, double Score);

/// <summary>
/// 在截圖的指定範圍裡找跟範本最像的位置（灰階、平均絕對差）。
/// 一般範圍：粗找（每 2 點試一個位置、範本每 2 點取 1 點）→ 在最佳點 ±2 逐點精找。
/// 大範圍（超過 <see cref="PyramidArea"/>）：先在縮一半的圖上做上面的找法，再回原尺寸在 ±3 逐點精找。
/// 實測 1904×706 整張找 135×47 的範本要 723 ms，NPC 名字這種會移動的東西只能整張找，所以需要金字塔。
/// 刻意不用 OpenCV：OpenCvSharp 的原生 dll 約 60MB，單一 exe 會大很多。
/// 刻意只比灰階：按鈕的形狀和文字才是辨識重點；需要分辨顏色（例如按鈕反灰）時，由呼叫端另外檢查顏色。
/// </summary>
public static class TemplateMatcher
{
    /// <summary>搜尋範圍超過這個面積（像素）才用金字塔；小範圍直接找比較準，也夠快</summary>
    private const int PyramidArea = 250_000;

    /// <summary>範本太小時縮一半就看不出形狀了，不用金字塔</summary>
    private const int PyramidMinSize = 24;

    public static MatchResult? Find(Frame frame, Frame template, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if ((long)region.Width * region.Height <= PyramidArea || template.Width < PyramidMinSize || template.Height < PyramidMinSize)
            return FindDirect(frame, template, region);

        // 步驟 1：在一半大小的圖上找大概位置；範本縮一半後比範圍大（不太可能）就退回直接找
        var half = new Rectangle(region.X / 2, region.Y / 2, region.Width / 2, region.Height / 2);
        if (FindDirect(frame.Half, template.Half, half) is not { } coarse)
            return FindDirect(frame, template, region);

        // 步驟 2：回原尺寸，在 ±3 逐點精找（縮圖是最近鄰，位置最多偏 1～2 點）
        var near = Rectangle.Intersect(region,
            new Rectangle(coarse.Bounds.X * 2 - 3, coarse.Bounds.Y * 2 - 3, template.Width + 6, template.Height + 6));
        var (bx, by, bd) = Scan(frame, template, near, 1, 1);
        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
    }

    private static MatchResult? FindDirect(Frame frame, Frame template, Rectangle region)
    {
        if (template.Width == 0 || template.Height == 0 || template.Width > region.Width || template.Height > region.Height)
            return null;

        // 步驟 1：粗找
        var (cx, cy, _) = Scan(frame, template, region, 2, 2);

        // 步驟 2：精找（取樣密度不同，分數不能跟粗找比，所以重新算）
        var fine = Rectangle.Intersect(region, new Rectangle(cx - 2, cy - 2, template.Width + 4, template.Height + 4));
        var (bx, by, bd) = Scan(frame, template, fine, 1, 1);
        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
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
