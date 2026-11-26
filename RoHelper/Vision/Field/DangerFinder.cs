namespace RoHelper.Vision.Field;

/// <summary>畫面上一塊王的攻擊警示範圍（截圖像素）：橢圓中心 X、Y，半徑 Rx、Ry（已加上安全距離）；Kind＝紅圈／粉紅圓</summary>
public sealed record DangerZone(double X, double Y, double Rx, double Ry, string Kind)
{
    /// <summary>點 (x, y) 在範圍裡</summary>
    public bool Contains(double x, double y)
    {
        double dx = (x - X) / Rx, dy = (y - Y) / Ry;
        return dx * dx + dy * dy <= 1;
    }
}

/// <summary>這一輪看到的攻擊警示：範圍、截圖寬高、dpr</summary>
public sealed record DangerView(IReadOnlyList<DangerZone> Zones, int Width, int Height, double Dpr);

/// <summary>
/// MVP／小頭目攻擊前地上的紅色警示範圍（使用者 2026-11-27：不要踏在上面）。
/// 實測（2026-11-27 斐揚洞穴月夜貓，dpr 1；fixture 王攻擊紅圈、王攻擊粉紅圓）兩種：
/// - 紅色圓環（王腳下，只有外圈）：(169～228, 40～71, 40～69)，外框約 180×150；
/// - 粉紅色實心圓（半透明）：(187～209, 131～142, 135～142)，外框約 200×180，內圈粉紅填滿約 0.7。
/// 會誤判的東西：紅燈籠、角色身上的紅色、火焰爆炸、傷害數字、血條。這些沒有「沿著一整圈」也不是「實心一大片粉紅」，用形狀分開（<see cref="IsZone"/>）。
/// 鐘的紅色虛線圈也符合，但那是躲鐘要站進去的地方：呼叫端躲鐘時不找（見 <see cref="FieldPlanner"/>）。
/// </summary>
public static class DangerFinder
{
    /// <summary>取樣間隔、併成同一塊的距離（取樣格；圓環是斷斷續續的弧，要能跨過缺口）</summary>
    private const double Step = 4;
    private const int Gap = 4;

    /// <summary>外框至少這麼大才算（dpr 1 的 px）：圓環、粉紅圓都 ≥ 110；燈籠、爆炸字 ≤ 70 px，血條高 ≤ 15 px</summary>
    private const double MinW = 110;
    private const double MinH = 70;

    /// <summary>範圍外面再留這麼多安全距離（dpr 1 的 px）</summary>
    private const double Margin = 30;

    /// <summary>紅色圓環：亮紅，綠藍都低</summary>
    public static bool IsRed(byte r, byte g, byte b) => r >= 150 && r - g >= 70 && r - b >= 60 && g < 120;

    /// <summary>粉紅色實心圓：偏紅的淺粉，綠藍差不多</summary>
    public static bool IsPink(byte r, byte g, byte b) => r >= 170 && g >= 105 && g <= 180 && r - g >= 40 && Math.Abs(g - b) <= 22;

    /// <summary>
    /// 候選色塊的形狀：Red／Pink＝紅、粉紅取樣點數；RedRing＝紅點落在外圈（正規化半徑 0.55～1.15）的比例；
    /// RedCenter＝紅點落在中心（&lt; 0.45）的比例；Sectors＝外圈的紅點分布在 12 個方位裡的幾個；PinkFill＝內圈（0.35～0.85，避開中間的王）粉紅的比例
    /// </summary>
    public sealed record Shape(Rectangle Box, int Red, int Pink, double RedRing, double RedCenter, int Sectors, double PinkFill);

    /// <summary>
    /// 找攻擊警示範圍。鐘的紅色圈（鐘的範圍）長得跟王的攻擊紅圈一樣，而且不在躲鐘時也會出現
    /// （實測 2026-11-26～27：MVP 戰鬥中、躲鐘結束後的截圖都有）：畫面上有鐘（<see cref="BellFinder.Find"/>）時，包住鐘的圈不算
    /// </summary>
    public static IReadOnlyList<DangerZone> Find(Frame f, double dpr)
    {
        var m = Margin * dpr;
        var zones = Shapes(f, dpr).Where(IsZone).Select(s => new DangerZone(
            s.Box.X + s.Box.Width / 2.0, s.Box.Y + s.Box.Height / 2.0, s.Box.Width / 2.0 + m, s.Box.Height / 2.0 + m,
            s.PinkFill >= 0.4 ? "粉紅圓" : "紅圈")).ToList();
        if (zones.Count > 0 && BellFinder.Find(f, dpr) is { } bell)
            zones.RemoveAll(z => z.Contains(bell.Mouth.X, bell.Mouth.Y) || z.Contains(bell.X, (bell.Y0 + bell.Y1) / 2.0));
        return zones;
    }

    /// <summary>
    /// 是攻擊警示：粉紅圓＝內圈粉紅填滿 ≥ 0.4（實測 0.69；誤判 ≤ 0.14）；
    /// 紅圈＝紅點 ≥ 30、八成以上在外圈、中心 ≤ 15%、分布在 ≥ 8 個方位（實測紅圈 0.94、11 個方位；燈籠＋角色 0.85、5 個方位；火焰爆炸 0.63、10 個方位）
    /// </summary>
    public static bool IsZone(Shape s)
        => Round(s) && (s.PinkFill >= 0.4 || s.Red >= 30 && s.RedRing >= 0.8 && s.RedCenter <= 0.15 && s.Sectors >= 8);

    /// <summary>
    /// 外框接近斜看的圓（寬高比 0.8～2.0；實測紅圈 169×145、粉紅圓 209×233）。
    /// 鐘的大紅圈上下被掃描範圍切掉時，會剩左右兩條窄長的弧（約 130×380）或一條扁的弧（約 640×100），形狀也像「沿著外圈」，用這個排除
    /// </summary>
    private static bool Round(Shape s) => s.Box.Width >= s.Box.Height * 0.8 && s.Box.Width <= s.Box.Height * 2.0;

    public static IReadOnlyList<Shape> Shapes(Frame f, double dpr)
    {
        // 上面是血條、時間、王的血條，下面是道具欄、技能列：不掃（王的血條是又長又紅的一條）
        int top = (int)Math.Round(170 * dpr), bottom = f.Height - (int)Math.Round(130 * dpr);
        if (bottom <= top)
            return [];
        var region = new Rectangle(0, top, f.Width, bottom - top);
        var step = Math.Max(1, (int)Math.Round(Step * dpr));
        var rects = ColorBlobs.Find(f, region, (r, g, b) => IsRed(r, g, b) || IsPink(r, g, b), step, Gap)
            .Where(b => b.Count >= 3)
            .Select(b => Rectangle.FromLTRB(b.X0, b.Y0, b.X1 + 1, b.Y1 + 1)).ToList();
        // ColorBlobs 不會把長大後碰在一起的兩塊合併：圓環常被切成左右兩塊，這裡把距離 Gap 格內的外框併起來
        var reach = Gap * step;
        for (var merged = true; merged;)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
                for (var j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i];
                    a.Inflate(reach, reach);
                    if (!a.IntersectsWith(rects[j]))
                        continue;
                    rects[i] = Rectangle.Union(rects[i], rects[j]);
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }
        }
        return rects.Where(r => r.Width >= MinW * dpr && r.Height >= MinH * dpr).Select(r => Measure(f, r, step)).ToList();
    }

    private static Shape Measure(Frame f, Rectangle box, int step)
    {
        double cx = box.X + box.Width / 2.0, cy = box.Y + box.Height / 2.0, hw = box.Width / 2.0, hh = box.Height / 2.0;
        int red = 0, pink = 0, ring = 0, center = 0, inner = 0, innerPink = 0;
        var sectors = new bool[12];
        for (var y = box.Top; y < box.Bottom; y += step)
            for (var x = box.Left; x < box.Right; x += step)
            {
                var (r, g, b) = f.At(x, y);
                double dx = (x - cx) / hw, dy = (y - cy) / hh, rr = Math.Sqrt(dx * dx + dy * dy);
                var pk = IsPink(r, g, b);
                if (rr >= 0.35 && rr <= 0.85)
                {
                    inner++;
                    if (pk)
                        innerPink++;
                }
                if (pk)
                    pink++;
                else if (IsRed(r, g, b))
                {
                    red++;
                    if (rr >= 0.55 && rr <= 1.15)
                    {
                        ring++;
                        var k = (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 12) % 12;
                        sectors[k] = true;
                    }
                    if (rr < 0.45)
                        center++;
                }
            }
        return new Shape(box, red, pink, red == 0 ? 0 : (double)ring / red, red == 0 ? 0 : (double)center / red,
            sectors.Count(s => s), inner == 0 ? 0 : (double)innerPink / inner);
    }
}
