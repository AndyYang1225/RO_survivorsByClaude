namespace RoHelper.Vision;

/// <summary>一塊符合顏色條件的區域（座標都是截圖像素，X1／Y1 含端點）</summary>
public sealed class Blob
{
    internal Blob(int x, int y)
    {
        X0 = X1 = x;
        Y0 = Y1 = y;
    }

    public int X0 { get; private set; }
    public int Y0 { get; private set; }
    public int X1 { get; private set; }
    public int Y1 { get; private set; }

    /// <summary>命中的取樣點數（step 大於 1 時是取樣後的點數，不是實際像素數）</summary>
    public int Count { get; private set; }

    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;
    public Point Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);

    internal void Add(int x, int y)
    {
        Count++;
        X0 = Math.Min(X0, x);
        Y0 = Math.Min(Y0, y);
        X1 = Math.Max(X1, x);
        Y1 = Math.Max(Y1, y);
    }
}

/// <summary>
/// 依顏色條件找色塊：小地圖的磁鐵／寶箱／自己、3D 畫面的扭蛋和鐘都用這個。
/// 演算法照 JS 版（scanBalls、scanBell）：逐點掃，命中的點如果落在既有色塊外框 gap 格以內就併進去，否則開新的一塊。
/// 刻意不做完整的連通元件標記：目標都是小而分散的色塊，這個寫法夠用，而且結果跟 JS 版一致，移植判斷門檻時才對得起來。
/// </summary>
public static class ColorBlobs
{
    /// <param name="step">取樣間隔：3 表示每 3 點取 1 點（相當於 JS 版把畫面縮成 1/3）</param>
    /// <param name="gap">距離既有色塊外框幾個取樣格以內算同一塊</param>
    public static List<Blob> Find(Frame f, Rectangle region, Func<byte, byte, byte, bool> match, int step = 1, int gap = 3)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, f.Width, f.Height));
        var blobs = new List<Blob>();
        var px = f.Pixels;
        var reach = gap * step;
        for (var y = region.Top; y < region.Bottom; y += step)
            for (var x = region.Left; x < region.Right; x += step)
            {
                var i = (y * f.Width + x) * 4;
                if (!match(px[i + 2], px[i + 1], px[i]))
                    continue;
                var blob = blobs.Find(b => x >= b.X0 - reach && x <= b.X1 + reach && y >= b.Y0 - reach && y <= b.Y1 + reach);
                if (blob is null)
                {
                    blob = new Blob(x, y);
                    blobs.Add(blob);
                }
                blob.Add(x, y);
            }
        return blobs;
    }
}
