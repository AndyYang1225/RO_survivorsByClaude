namespace RoHelper.Vision.Field;

/// <summary>小地圖上的一群同色點：中心（截圖像素）與點數</summary>
public sealed record MinimapBlob(double X, double Y, int N);

/// <summary>
/// 一張截圖的小地圖判讀結果（同 JS readMinimap 的回傳值）。座標都是截圖像素。
/// Map＝地圖本體的外框（小地圖外圍是黑色），用來算地圖中心。
/// 刻意沒有 JS 的 tile（右上角的格子座標）：要 OCR，第一批不做（王死掉的地點才用得到）。
/// </summary>
public sealed record MinimapInfo(
    MinimapBlob Me, IReadOnlyList<MinimapBlob> Magnets, IReadOnlyList<MinimapBlob> Chests,
    IReadOnlyList<MinimapBlob> Loot, IReadOnlyList<MinimapBlob> Bosses, RectangleF Map);

/// <summary>
/// 讀截圖右上角的小地圖：自己、磁鐵、寶箱、王的掉落物、王（移植 engine.js readMinimap）。
/// 顏色常數與分群規則照抄 JS；差別只是 JS 讀遊戲的小地圖畫布，這裡讀截圖上的同一塊。
/// </summary>
public static class MinimapReader
{
    // 小地圖顏色（同 JS COL）：磁鐵 #ff3b4a、自己 #c0182a、寶箱 #ffc83c、掉落物外框 #1d2d57、MVP 紅圈、小頭目橘圈
    private static readonly (int R, int G, int B) Player = (192, 26, 43);
    private static readonly (int R, int G, int B) Magnet = (255, 59, 74);
    private static readonly (int R, int G, int B) Chest = (255, 200, 60);
    private static readonly (int R, int G, int B) LootEdge = (31, 50, 92);
    private static readonly (int R, int G, int B) Mvp = (255, 42, 42);
    private static readonly (int R, int G, int B) MiniBoss = (255, 154, 46);

    /// <summary>找不到自己時回 null（同 JS：不是野外畫面、或小地圖被擋住）</summary>
    public static MinimapInfo? Read(Frame f, double dpr)
    {
        var area = Area(f, dpr);
        List<(int X, int Y)> me = [], magnets = [], chests = [], loot = [], bosses = [];
        int bx0 = int.MaxValue, bx1 = int.MinValue, by0 = int.MaxValue, by1 = int.MinValue;
        var px = f.Pixels;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                var i = (y * f.Width + x) * 4;
                int r = px[i + 2], g = px[i + 1], b = px[i];
                if (r + g + b > 45)
                {
                    bx0 = Math.Min(bx0, x);
                    bx1 = Math.Max(bx1, x);
                    by0 = Math.Min(by0, y);
                    by1 = Math.Max(by1, y);
                }
                // 同 JS 的 else-if 順序：一個點只歸一類
                if (Near(r, g, b, Player, 10))
                    me.Add((x, y));
                else if (Near(r, g, b, Magnet, 12))
                    magnets.Add((x, y));
                else if (Near(r, g, b, Chest, 12))
                    chests.Add((x, y));
                else if (Near(r, g, b, LootEdge, 14))
                    loot.Add((x, y));
                else if (Near(r, g, b, Mvp, 14) || Near(r, g, b, MiniBoss, 14))
                    bosses.Add((x, y));
            }

        var self = Clusters(me, 3, 4).OrderByDescending(c => c.N).FirstOrDefault();
        if (self is null)
            return null;
        var magnetList = Clusters(magnets, 4, 4);
        // 掉落物：品質色填滿、深藍外框的菱形。填色會跟地形混在一起，所以只認外框：
        // 6 px 內歸同一群、一群至少 4 點，而且不在磁鐵 7 px 內（磁鐵也有深藍外框）
        var lootList = Clusters(loot, 4, 6)
            .Where(l => !magnetList.Any(m => Math.Sqrt((m.X - l.X) * (m.X - l.X) + (m.Y - l.Y) * (m.Y - l.Y)) < 7))
            .ToList();
        var map = bx1 > bx0
            ? new RectangleF(bx0, by0, bx1 - bx0, by1 - by0)
            : new RectangleF(area.X, area.Y, area.Width, area.Height);
        return new MinimapInfo(self, magnetList, Clusters(chests, 3, 4), lootList, Clusters(bosses, 4, 4), map);
    }

    /// <summary>
    /// 小地圖本體在截圖上的範圍：右上角固定一塊（只含地圖本體，不含上面的標題列和下面的按鈕列），夾在畫面內。
    /// 刻意不用找的：面板一定固定在右上角；而且下方按鈕列（統計按鈕）的深藍色跟掉落物外框同色，一定要排除，
    /// 用找的反而容易把按鈕列框進來。實測（dpr 1、視窗 1904×682）：面板 x≈1731～1891、地圖本體 y≈40～170、按鈕列 y≈172～190。
    /// </summary>
    internal static Rectangle Area(Frame f, double dpr)
    {
        var r = new Rectangle(
            f.Width - (int)Math.Round(173 * dpr), (int)Math.Round(40 * dpr),
            (int)Math.Round(158 * dpr), (int)Math.Round(130 * dpr));
        return Rectangle.Intersect(r, new Rectangle(0, 0, f.Width, f.Height));
    }

    private static bool Near(int r, int g, int b, (int R, int G, int B) c, int tol)
        => Math.Abs(r - c.R) <= tol && Math.Abs(g - c.G) <= tol && Math.Abs(b - c.B) <= tol;

    /// <summary>
    /// 同 JS clusters()：逐點找中心在 reach px 內（x、y 各自比）的群併進去並更新平均，否則開新群；只留點數 ≥ minSize 的。
    /// 刻意照抄這個簡單寫法而不做連通元件標記：結果跟 JS 一致，移植的門檻才對得起來。
    /// </summary>
    private static List<MinimapBlob> Clusters(List<(int X, int Y)> points, int minSize, int reach)
    {
        var groups = new List<(double Sx, double Sy, int N, double Cx, double Cy)>();
        foreach (var (x, y) in points)
        {
            var k = groups.FindIndex(g => Math.Abs(g.Cx - x) <= reach && Math.Abs(g.Cy - y) <= reach);
            if (k < 0)
            {
                groups.Add((0, 0, 0, x, y));
                k = groups.Count - 1;
            }
            var (sx, sy, n, _, _) = groups[k];
            sx += x;
            sy += y;
            n++;
            groups[k] = (sx, sy, n, sx / n, sy / n);
        }
        return [.. groups.Where(g => g.N >= minSize).Select(g => new MinimapBlob(g.Cx, g.Cy, g.N))];
    }
}
