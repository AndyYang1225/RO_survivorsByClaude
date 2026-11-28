namespace RoHelper.Vision;

/// <summary>
/// 燈籠節視窗（跟節慶主持人交談後、有「開始遊戲」的那個視窗）上的按鈕狀態。純看顏色，位置都從「開始遊戲」按鈕往回推（視窗置中、大小跟著 dpr）。
/// 實測（2026-11-28 B，1904×682、dpr 1，開始遊戲範本 (1224, 535)）：
/// 分頁「扭蛋」(559, 167)、「香」(689, 167)，選中的分頁是藍底；
/// 扭蛋分頁三列（節慶、白素貞、蛇王）的「打開」(824, 227/277/327)、「×9」(869, 同)；
/// 香分頁三列的「製作」(824, 228/281/334)，按鈕左邊是香的圖示，只看右邊的字那一塊；
/// 「點香」勾選框 (1285, 503)：勾了是深藍 (0, 92, 200)，沒勾是白的。
/// 能按的按鈕是藍色（約 (135, 168, 235)），不能按是白的，抽扭蛋動畫中全部變灰藍（約 (200, 205, 220)，抽完約 6 秒恢復）。
/// 抽完不跳結果視窗：九個結果直接顯示在視窗右半邊。
/// </summary>
public sealed record FestivalPanel(
    bool GachaTab, bool IncenseTab, Point GachaTabAt, Point IncenseTabAt,
    IReadOnlyList<Point> Nine, IReadOnlyList<Point> Craft, bool IncenseChecked, Point IncenseBox)
{
    private static readonly int[] GachaRows = [-308, -258, -208];
    private static readonly int[] CraftRows = [-307, -254, -201];

    /// <summary>start＝「開始遊戲」範本的位置。兩個分頁都不是藍底＝不是燈籠節視窗，回 null</summary>
    public static FestivalPanel? Read(Frame f, Rectangle start, double dpr)
    {
        Point At(int dx, int dy) => new(start.X + (int)Math.Round(dx * dpr), start.Y + (int)Math.Round(dy * dpr));
        bool Blue(Point c, int w, int h) => BlueShare(f, c, (int)Math.Round(w * dpr), (int)Math.Round(h * dpr)) >= 0.3;

        var gachaTab = At(-665, -368);
        var incenseTab = At(-535, -368);
        var onGacha = Blue(gachaTab, 40, 14);
        var onIncense = Blue(incenseTab, 40, 14);
        if (onGacha == onIncense)
            return null;
        var nine = onGacha ? GachaRows.Select(dy => At(-355, dy)).Where(p => Blue(p, 22, 14)).ToList() : [];
        var craft = onIncense ? CraftRows.Select(dy => At(-397, dy)).Where(p => Blue(p, 24, 12)).ToList() : [];
        var box = At(61, -32);
        var checkedBox = DarkBlueShare(f, box, Math.Max(4, (int)Math.Round(10 * dpr))) >= 0.2;
        return new(onGacha, onIncense, gachaTab, incenseTab, nine, craft, checkedBox, box);
    }

    /// <summary>以 c 為中心 w×h 的範圍裡，能按的藍色按鈕色（B ≥ 200、B − R ≥ 60）佔多少</summary>
    internal static double BlueShare(Frame f, Point c, int w, int h)
        => Share(f, c, w, h, (r, g, b) => b >= 200 && b - r >= 60);

    /// <summary>勾選框打勾的深藍（R ≤ 60、B ≥ 150）佔多少</summary>
    private static double DarkBlueShare(Frame f, Point c, int size)
        => Share(f, c, size, size, (r, g, b) => r <= 60 && b >= 150);

    private static double Share(Frame f, Point c, int w, int h, Func<int, int, int, bool> hit)
    {
        var area = Rectangle.Intersect(new Rectangle(c.X - w / 2, c.Y - h / 2, w, h), new Rectangle(0, 0, f.Width, f.Height));
        if (area.Width <= 0 || area.Height <= 0)
            return 0;
        var (n, px) = (0, f.Pixels);
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                var i = (y * f.Width + x) * 4;
                if (hit(px[i + 2], px[i + 1], px[i]))
                    n++;
            }
        return (double)n / (area.Width * area.Height);
    }
}
