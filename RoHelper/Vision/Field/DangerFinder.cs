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

/// <summary>這一輪看到的攻擊警示：圓形範圍、條紋（null＝沒掃）、截圖寬高、dpr</summary>
public sealed record DangerView(IReadOnlyList<DangerZone> Zones, int Width, int Height, double Dpr, StripeMask? Stripes = null)
{
    /// <summary>有任何警示</summary>
    public bool Any => Zones.Count > 0 || Stripes is { Bands: > 0 };

    /// <summary>點 (x, y) 在警示範圍裡（圓形或條紋）</summary>
    public bool Hit(double x, double y) => Zones.Any(z => z.Contains(x, y)) || Stripes?.Hit(x, y) == true;
}

/// <summary>
/// 條紋警示（洛陽的王：整片平行的紅色長條，直的或斜的）的格子遮罩：Cell＝一格幾 px、Top＝第一列的 y；
/// 已經往外擴了安全距離。Bands＝幾條帶子
/// </summary>
public sealed class StripeMask(bool[] cells, int cols, int rows, int cell, int top, int bands)
{
    public int Bands { get; } = bands;

    public bool Hit(double x, double y)
    {
        int c = (int)Math.Floor(x / cell), r = (int)Math.Floor((y - top) / cell);
        return c >= 0 && c < cols && r >= 0 && r < rows && cells[r * cols + c];
    }
}

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

    /// <summary>
    /// 條紋的顏色：半透明紅蓋在地板上（實測 2026-11-27 洛陽：黃昏地板 (78,63,66) 上 (105～125, 53～72, 49～76)、
    /// 夜晚地板 (55,52,67) 上 (139～170, 42～63, 36～60)），加上亮紅的邊（<see cref="IsRed"/>）。
    /// 夜晚藍地板 (58,39,65) 上偏紫 (106～116, 58～67, 74～86)；發動中的亮橘 (213～248, 102～123, 81～88)。
    /// 白天的磚地 (118～167, 78～115, 55～81) 綠比藍多 25 以上、或紅綠差不到 40，不算
    /// </summary>
    public static bool IsStripe(byte r, byte g, byte b)
        => IsRed(r, g, b) || r >= 95 && r - g >= 40 && r - b >= 20 && g <= 110 && Math.Abs(g - b) <= 25
            || r >= 200 && r - g >= 90 && g <= 130 && b <= 110;

    /// <summary>條紋：一條帶子至少這麼長、這麼寬（dpr 1 的 px；實測帶寬約 60～80、長度跨過整個畫面），長寬比至少這樣</summary>
    private const double StripeMinLen = 250;
    private const double StripeMinWidth = 25;
    private const double StripeMinRatio = 3;

    /// <summary>帶子填滿沿主軸外框的比例至少這樣（直的帶子 ≥ 0.5；彎的弧大半是空的）</summary>
    private const double StripeMinFill = 0.45;

    /// <summary>超過這麼大面積（dpr 1 的 px²）的紅色一大片，不管形狀都算（帶子連在一起時長寬比會變小）</summary>
    private const double StripeHugeArea = 60_000;

    /// <summary>角色本身的範圍（dpr 1 的 px，從畫面中心算）：左右各 28、往上 75、往下 25（腳下 +15）</summary>
    private const double SpriteHalfW = 28;
    private const double SpriteUp = 75;
    private const double SpriteDown = 25;

    /// <summary>條紋外面留的安全距離（dpr 1 的 px）</summary>
    private const double StripeMargin = 20;

    /// <summary>
    /// 找條紋警示（使用者 2026-11-27：洛陽的王「邪骸浪人」「舞獅」會在整個畫面鋪平行紅色長條）：
    /// 每 4 px 取樣，條紋色的格子做連通（容許 1 格缺口，角色和特效會切斷帶子），
    /// 留下又長又窄的（主軸長 ≥ 250、寬 ≥ 25、長寬比 ≥ 3）或特別大的一片；紅色的怪、燈籠、火焰都是小塊或圓的。
    /// 沒有條紋回 null
    /// </summary>
    public static StripeMask? FindStripes(Frame f, double dpr)
    {
        int top = (int)Math.Round(170 * dpr), bottom = f.Height - (int)Math.Round(130 * dpr);
        var cell = Math.Max(1, (int)Math.Round(Step * dpr));
        if (bottom <= top)
            return null;
        int cols = f.Width / cell, rows = (bottom - top) / cell;
        var hit = new bool[cols * rows];
        var px = f.Pixels;
        // 角色自己（紅衣服）會跟旁邊的帶子連成一塊、讓腳下被當成在條紋上：角色這一塊不取樣，最後再看帶子有沒有穿過去
        int sc0 = (int)((f.Width / 2.0 - SpriteHalfW * dpr) / cell), sc1 = (int)Math.Ceiling((f.Width / 2.0 + SpriteHalfW * dpr) / cell);
        int sr0 = (int)((f.Height / 2.0 - SpriteUp * dpr - top) / cell), sr1 = (int)Math.Ceiling((f.Height / 2.0 + SpriteDown * dpr - top) / cell);
        sc0 = Math.Clamp(sc0, 1, cols - 2);
        sc1 = Math.Clamp(sc1, sc0, cols - 2);
        sr0 = Math.Clamp(sr0, 1, rows - 2);
        sr1 = Math.Clamp(sr1, sr0, rows - 2);
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                if (r >= sr0 && r <= sr1 && c >= sc0 && c <= sc1)
                    continue;
                var i = ((top + r * cell) * f.Width + c * cell) * 4;
                hit[r * cols + c] = IsStripe(px[i + 2], px[i + 1], px[i]);
            }

        var keep = new bool[hit.Length];
        var label = new int[hit.Length];
        var queue = new Queue<int>();
        var members = new List<int>();
        var bands = 0;
        for (var start = 0; start < hit.Length; start++)
        {
            if (!hit[start] || label[start] != 0)
                continue;
            members.Clear();
            label[start] = 1;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var k = queue.Dequeue();
                members.Add(k);
                int kr = k / cols, kc = k % cols;
                for (var dr = -2; dr <= 2; dr++)
                    for (var dc = -2; dc <= 2; dc++)
                    {
                        int nr = kr + dr, nc = kc + dc;
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                            continue;
                        var n = nr * cols + nc;
                        if (hit[n] && label[n] == 0)
                        {
                            label[n] = 1;
                            queue.Enqueue(n);
                        }
                    }
            }
            if (!IsBand(members, cols, cell, dpr))
                continue;
            bands++;
            foreach (var k in members)
                keep[k] = true;
        }
        if (bands == 0)
            return null;
        // 帶子穿過角色（擴過安全距離之後看）：同一列角色左右兩邊都是 → 補滿這一列；同一欄上下兩邊都是 → 補滿這一欄。
        // 直線常常只有 1～2 格寬、上下兩端不一定落在同一欄，所以用擴過的
        var m = Dilate(keep, cols, rows, (int)Math.Ceiling(StripeMargin * dpr / cell));
        var fill = new List<int>();
        for (var r = sr0; r <= sr1; r++)
            if (m[r * cols + sc0 - 1] && m[r * cols + sc1 + 1])
                for (var c = sc0; c <= sc1; c++)
                    fill.Add(r * cols + c);
        for (var c = sc0; c <= sc1; c++)
            if (m[(sr0 - 1) * cols + c] && m[(sr1 + 1) * cols + c])
                for (var r = sr0; r <= sr1; r++)
                    fill.Add(r * cols + c);
        foreach (var k in fill)
            m[k] = true;
        return new StripeMask(m, cols, rows, cell, top, bands);
    }

    /// <summary>一塊格子是條紋：用座標的共變異數算主軸長、寬（均勻長條：長 ≈ √(12λ)）</summary>
    private static bool IsBand(List<int> members, int cols, int cell, double dpr)
    {
        var n = members.Count;
        var area = (double)n * cell * cell;
        if (area >= StripeHugeArea * dpr * dpr)
            return true;
        if (n < 30)
            return false;
        double sx = 0, sy = 0;
        foreach (var k in members)
        {
            sx += k % cols;
            sy += k / cols;
        }
        double mx = sx / n, my = sy / n, xx = 0, yy = 0, xy = 0;
        foreach (var k in members)
        {
            double dx = k % cols - mx, dy = k / cols - my;
            xx += dx * dx;
            yy += dy * dy;
            xy += dx * dy;
        }
        xx /= n;
        yy /= n;
        xy /= n;
        double tr = xx + yy, det = xx * yy - xy * xy, d = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + d, l2 = Math.Max(tr / 2 - d, 0);
        double len = Math.Sqrt(12 * l1) * cell, width = Math.Sqrt(12 * l2) * cell;
        if (len < StripeMinLen * dpr || width < StripeMinWidth * dpr || len < width * StripeMinRatio)
            return false;
        // 直的帶子把沿主軸的外框填滿；鐘的大紅圈被切成的左右兩條弧（130×380）是彎的，外框大半是空的
        double ux, uy;
        if (Math.Abs(xy) > 1e-9)
            (ux, uy) = (l1 - yy, xy);
        else
            (ux, uy) = xx >= yy ? (1.0, 0.0) : (0.0, 1.0);
        var un = Math.Sqrt(ux * ux + uy * uy);
        ux /= un;
        uy /= un;
        double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
        foreach (var k in members)
        {
            double dx = k % cols - mx, dy = k / cols - my, u = dx * ux + dy * uy, v = -dx * uy + dy * ux;
            u0 = Math.Min(u0, u);
            u1 = Math.Max(u1, u);
            v0 = Math.Min(v0, v);
            v1 = Math.Max(v1, v);
        }
        var fill = n / ((u1 - u0 + 1) * (v1 - v0 + 1));
        return fill >= StripeMinFill;
    }

    /// <summary>往外擴 r 格（先橫向再縱向）</summary>
    private static bool[] Dilate(bool[] m, int cols, int rows, int r)
    {
        var h = new bool[m.Length];
        for (var y = 0; y < rows; y++)
        {
            var last = int.MinValue / 2;
            for (var x = 0; x < cols; x++)
                if (m[y * cols + x])
                    last = x;
                else if (x - last <= r)
                    h[y * cols + x] = true;
            last = int.MaxValue / 2;
            for (var x = cols - 1; x >= 0; x--)
                if (m[y * cols + x])
                {
                    last = x;
                    h[y * cols + x] = true;
                }
                else if (last - x <= r)
                    h[y * cols + x] = true;
        }
        var v = new bool[m.Length];
        for (var x = 0; x < cols; x++)
        {
            var last = int.MinValue / 2;
            for (var y = 0; y < rows; y++)
                if (h[y * cols + x])
                    last = y;
                else if (y - last <= r)
                    v[y * cols + x] = true;
            last = int.MaxValue / 2;
            for (var y = rows - 1; y >= 0; y--)
                if (h[y * cols + x])
                {
                    last = y;
                    v[y * cols + x] = true;
                }
                else if (last - y <= r)
                    v[y * cols + x] = true;
        }
        return v;
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
