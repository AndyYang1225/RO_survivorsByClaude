namespace RoHelper.Vision.Field;

/// <summary>
/// 畫面上一塊符合鐘顏色的色塊（截圖像素）：X＝外框水平中間、Y＝外框底部（同 JS bell.pos：站點是鐘底部中間）；
/// X0～X1、Y0～Y1 是外框；Width／Height 同 JS（x1 − x0，取樣格換成像素）；N 是取樣點數；Gold／Red 是其中金色、封印紅的點數；
/// Fill 是外框（取樣格）裡命中的比例。
/// </summary>
public sealed record BellBlob(int X, int Y, int X0, int X1, int Y0, int Y1, int N, int Width, int Height, int Gold, int Red, double Fill, int Dim = 0, int BX = 0, int BY = 0)
{
    /// <summary>鐘身的鐘口（鐘身底部中間，見 <see cref="BellFinder.Scan"/>）；合成的色塊沒給時用外框底部中間</summary>
    public (int X, int Y) Mouth => BX == 0 && BY == 0 ? (X, Y) : (BX, BY);
}

/// <summary>
/// 用畫面顏色找王放的鐘（移植 engine.js scanBell，只做「② 備用：看畫面顏色找鐘」）。
/// 鐘是自己發光的材質，白天晚上顏色都一樣（JS 註解）：金色 (1.0, 0.8, 0.42)，封印時偏紅。
/// JS 的實測值（照搬）：金色規則、封印紅規則、每 4 px 取一點、最大一塊至少 25 點。
/// 待實機校正（還沒在實機看過鐘，2026-11-21）：取樣間隔與 25 點的下限（JS 是遊戲畫布、鏡頭距離不同）、
/// 封印紅的鐘長什麼樣（整塊紅？還是金色裡帶紅？）、燈籠與特效的排除條件（見 <see cref="IsBell"/>）。
/// 跟 JS 的差別（都是為了在截圖上更準）：
/// 1. 排除 HUD 與系統公告（同 <see cref="BallFinder"/> 的範圍，但不排除自己的角色：要站到鐘底下，鐘會蓋住畫面中心）。
/// 2. 真正的連通元件（可跨 4 格的縫，同 JS 的「外框 ±4 格內就併進去」；JS 依掃描順序併到第一個符合的外框，可能把一塊切成兩塊）。
/// 3. 排除紅燈籠與特效：燈籠的紅色符合封印紅、點亮的燈籠外圈光環符合金色（見 <see cref="IsBell"/>）。
/// 實測（2026-11-21，dpr 1，視窗 1904×682）：一張截圖掃一次約 0.5～0.9 ms；野外1～7、撿取倒數 上除了燈籠、特效，最大的金色塊是自己角色身上的 18 點（&lt; 25）。
/// </summary>
public static class BellFinder
{
    /// <summary>取樣間隔（dpr 1 時每 4 px 取一點，同 JS 視窗寬 &gt; 1100 時的 S = 4；取樣點數跟 dpr 無關）</summary>
    private const double Step = 4;

    /// <summary>連通時可以跨過的距離（取樣格）：同 JS 的「外框 ±4 格內就算同一塊」</summary>
    private const int Reach = 4;

    /// <summary>最大一塊至少這麼多點才算鐘（同 JS，待實機校正）</summary>
    private const int MinN = 25;

    /// <summary>
    /// 填滿比例下限（exe 新增）：野外5傍晚 點亮的燈籠外面有一圈金白色光環（x 392～492、y 196～272，72 點全是金色），是空心的，填滿只有 0.14。
    /// 實測（2026-11-21 斐揚洞穴 月夜貓，洞穴的鐘）：鐘連同底座圓盤一起算，外框 200×252、填滿 0.24，原本的 0.3 把真的鐘排除了，改成 0.2
    /// </summary>
    private const double MinFill = 0.2;

    /// <summary>
    /// 寬度上限（dpr 1 的 px，exe 新增）：實測鐘＋底座 200 px 寬；洛陽傍晚的地面顏色接近暗金色時會連成上千 px 的大片色塊，不是鐘
    /// </summary>
    private const int MaxWidth = 400;

    /// <summary>暗金色為主的色塊高度下限（dpr 1 的 px）：見 <see cref="IsBell"/></summary>
    private const int DimMinHeight = 90;

    /// <summary>
    /// 幾乎全是封印紅（金色不到 1/4）的色塊，寬度至少這麼多（dpr 1 的 px）才算鐘（exe 新增，待實機校正）：
    /// 燈籠符合封印紅的部分實測 36 px 寬（野外5傍晚 x 1652～1688，31 點），技能的紅色爆炸 56 px（野外3錢不夠 畫面頂端）；
    /// JS 量到鐘在小視窗、鏡頭拉遠時 50～130 px 寬，這裡的視窗比較大，鐘應該更大
    /// </summary>
    private const int MinRedWidth = 64;

    /// <summary>
    /// 鐘身大小（dpr 1 的 px）：色塊裡最密的這麼大一塊＝鐘身，它的底部＝鐘口（要站的地方）。
    /// 實測（2026-11-26 斐揚洞穴）鐘身約 100×135 px，底座圓盤約 215 px 寬、是空心的圈，旁邊的火盆、光暈會跟鐘連成一塊，
    /// 用外框底部當目標會偏到圓盤邊緣（105343：腳在鐘口下方 100 px，站在圓盤上還是被鐘打死）
    /// </summary>
    private const int BodyW = 96, BodyH = 128;

    /// <summary>
    /// 找畫面上最大的一塊鐘（同 JS：取最大一塊、至少 <see cref="MinN"/> 點），排除燈籠與特效（見 <see cref="IsBell"/>）；找不到回 null。
    /// last＝上次找到的鐘：封印後變紅的鐘在原地，比燈籠小也認得出來。
    /// </summary>
    public static BellBlob? Find(Frame f, double dpr, BellBlob? last = null)
        => Scan(f, dpr).Where(b => IsBell(b, dpr, last, f)).OrderByDescending(b => IsSureBell(b, dpr, f)).ThenByDescending(b => b.N).FirstOrDefault();

    /// <summary>
    /// 像不像鐘：至少 25 點（JS）、填滿 ≥ 0.3（排除燈籠的光環）；幾乎全是封印紅的話（燈籠的紅色、紅色特效也符合封印紅），
    /// 要比燈籠寬（≥ 64 px），或在上次找到的鐘附近（中心距離不超過上次的鐘寬，封印後原地變紅）
    /// </summary>
    /// <summary>
    /// 大鐘（鐘身＋底座圓盤）：實測（2026-11-26 斐揚洞穴，4 張 MVP 戰鬥截圖）鐘 N 721～903、高 252～260 px；
    /// 火盆的光最多 N 539、高 160 px。鐘旁邊有技能的火焰特效時會連成一塊（寬 328、填滿只有 0.17），所以這裡填滿只要 0.12。
    /// 「沒讀到公告、看到鐘就躲」只認這種，免得跑去火盆底下
    /// </summary>
    public static bool IsBigBell(BellBlob b, double dpr)
        => b.N >= BigMinN && b.Height >= BigMinHeight * dpr && b.Fill >= BigMinFill && b.Width <= MaxWidth * dpr;

    private const int BigMinN = 650;
    private const int BigMinHeight = 220;
    private const double BigMinFill = 0.12;

    /// <summary>
    /// 被畫面邊緣切掉一半的鐘（只看得到底座）：貼著畫面上下左右任一邊、寬高都 ≥ 100 px、N ≥ 100，填滿只要 0.1。
    /// 實測（2026-11-26 斐揚洞穴）喊躲鐘時鐘在畫面最上緣（y 0～124），又被閃電特效切開，填滿只有 0.11～0.18，原本的規則認不出來，原地等了 4.5 秒被秒殺。
    /// 火盆、地上的火焰特效都沒有貼邊又這麼大的
    /// </summary>
    private static bool IsEdgeBell(BellBlob b, double dpr, Frame? f)
    {
        if (f is null)
            return false;
        var edge = (int)(8 * dpr);
        var touches = b.Y0 <= edge || b.X0 <= edge || b.X1 >= f.Width - edge || b.Y1 >= f.Height - edge;
        if (touches && b.N >= 100 && b.Width >= 100 * dpr && b.Height >= 100 * dpr && b.Fill >= 0.1 && b.Width <= MaxWidth * dpr)
            return true;
        // 被下方技能列擋住的鐘（技能列那塊不掃，見 Excluded）：實測（2026-11-26 第 3 局）鐘在畫面正下方，
        // 只露出鐘頂 96×72～76 px、N 269～309、填滿 0.57～0.62，被「暗金色塊至少 90 px 高」排除，原地等了 4.5 秒
        var hud = Excluded(f, dpr)[^1];
        var onHud = b.Y1 >= hud.Top - edge && b.X1 >= hud.Left && b.X0 <= hud.Right;
        return onHud && b.N >= 150 && b.Width >= 60 * dpr && b.Height >= 50 * dpr && b.Fill >= 0.3 && b.Width <= MaxWidth * dpr;
    }

    /// <summary>大鐘、被邊緣（含技能列）切掉的鐘：比一般的金色色塊可靠，Find 優先選它們（不然火盆的光比較大塊時會被選走）</summary>
    private static bool IsSureBell(BellBlob b, double dpr, Frame? f) => IsBigBell(b, dpr) || IsEdgeBell(b, dpr, f);

    private static bool IsBell(BellBlob b, double dpr, BellBlob? last, Frame? f = null)
    {
        if (IsSureBell(b, dpr, f))
            return true;
        if (b.N < MinN || b.Fill < MinFill || b.Width > MaxWidth * dpr)
            return false;
        // 暗金色為主的色塊要夠高才算：實測洞穴的鐘身 148 px 高；洛陽的燈籠、地面被暗金規則誤認的色塊最高 72 px
        if (b.Dim * 2 >= b.N && b.Height < DimMinHeight * dpr)
            return false;
        if (b.Gold * 4 >= b.N)
            return true;
        if (b.Width >= MinRedWidth * dpr)
            return true;
        if (last is null)
            return false;
        double dx = (b.X0 + b.X1) / 2.0 - (last.X0 + last.X1) / 2.0, dy = (b.Y0 + b.Y1) / 2.0 - (last.Y0 + last.Y1) / 2.0;
        return Math.Sqrt(dx * dx + dy * dy) <= Math.Max(last.Width, 30 * dpr);
    }

    /// <summary>掃整張截圖（排除 HUD），回傳所有鐘色的色塊（不管大小；給 <see cref="Find"/> 篩選，也給開發介面校正用）</summary>
    public static List<BellBlob> Scan(Frame f, double dpr)
    {
        var step = Math.Max(1, (int)Math.Round(Step * dpr));
        int gw = (f.Width + step - 1) / step, gh = (f.Height + step - 1) / step;
        var skip = new bool[gw * gh];
        foreach (var e in Excluded(f, dpr))
            for (var gy = Math.Max(0, (e.Top + step - 1) / step); gy < gh && gy * step < e.Bottom; gy++)
                for (var gx = Math.Max(0, (e.Left + step - 1) / step); gx < gw && gx * step < e.Right; gx++)
                    skip[gy * gw + gx] = true;

        // 1＝金色、2＝封印紅
        var hits = new byte[gw * gh];
        var px = f.Pixels;
        for (var gy = 0; gy < gh; gy++)
        {
            var row = gy * step * f.Width;
            for (var gx = 0; gx < gw; gx++)
            {
                if (skip[gy * gw + gx])
                    continue;
                var i = (row + gx * step) * 4;
                hits[gy * gw + gx] = Match(px[i + 2], px[i + 1], px[i]);
            }
        }

        // 前綴和：找鐘身用（見下面）
        var sum = new int[(gw + 1) * (gh + 1)];
        for (var gy = 0; gy < gh; gy++)
            for (var gx = 0; gx < gw; gx++)
                sum[(gy + 1) * (gw + 1) + gx + 1] = (hits[gy * gw + gx] != 0 ? 1 : 0) + sum[gy * (gw + 1) + gx + 1] + sum[(gy + 1) * (gw + 1) + gx] - sum[gy * (gw + 1) + gx];
        int Count(int ax, int ay, int bx, int by) => sum[by * (gw + 1) + bx] - sum[ay * (gw + 1) + bx] - sum[by * (gw + 1) + ax] + sum[ay * (gw + 1) + ax];
        int bodyW = Math.Max(1, (int)Math.Round(BodyW * dpr / step)), bodyH = Math.Max(1, (int)Math.Round(BodyH * dpr / step));

        var found = new List<BellBlob>();
        var seen = new bool[hits.Length];
        var stack = new Stack<int>();
        for (var start = 0; start < hits.Length; start++)
        {
            if (hits[start] == 0 || seen[start])
                continue;
            int n = 0, gold = 0, red = 0, dim = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                int gx = c % gw, gy = c / gw;
                n++;
                if (hits[c] == 2)
                    red++;
                else
                    gold++;
                if (hits[c] == 3)
                    dim++;
                x0 = Math.Min(x0, gx);
                x1 = Math.Max(x1, gx);
                y0 = Math.Min(y0, gy);
                y1 = Math.Max(y1, gy);
                for (var ny = Math.Max(0, gy - Reach); ny <= Math.Min(gh - 1, gy + Reach); ny++)
                    for (var nx = Math.Max(0, gx - Reach); nx <= Math.Min(gw - 1, gx + Reach); nx++)
                    {
                        var j = ny * gw + nx;
                        if (hits[j] == 0 || seen[j])
                            continue;
                        seen[j] = true;
                        stack.Push(j);
                    }
            }
            found.Add(new BellBlob((x0 + x1) * step / 2, y1 * step, x0 * step, x1 * step, y0 * step, y1 * step, n,
                (x1 - x0) * step, (y1 - y0) * step, gold, red, (double)n / ((x1 - x0 + 1) * (y1 - y0 + 1)), dim,
                0, 0) is var blob && n >= MinN ? Body(blob, x0, y0, x1, y1) : blob);
            BellBlob Body(BellBlob blob, int x0, int y0, int x1, int y1)
            {
                int best = -1, bx = x0, by = y0;
                for (var wy = y0; wy <= Math.Min(y0 + bodyH / 3, Math.Max(y0, y1 + 1 - bodyH)); wy++)
                    for (var wx = x0; wx <= Math.Max(x0, x1 + 1 - bodyW); wx++)
                    {
                        var c = Count(wx, wy, Math.Min(wx + bodyW, x1 + 1), Math.Min(wy + bodyH, y1 + 1));
                        if (c > best)
                            (best, bx, by) = (c, wx, wy);
                    }
                var mx = (bx + Math.Min(bx + bodyW, x1 + 1)) * step / 2;
                var my = Math.Min(by + bodyH, y1 + 1) * step;
                return blob with { BX = mx, BY = my };
            }
        }
        return found;
    }

    /// <summary>顏色規則（同 JS scanBell）：1＝金色、2＝封印中的紅色、3＝洞穴裡的暗金色（exe 新增，算金色）、0＝都不是</summary>
    private static byte Match(int r, int g, int b)
    {
        if (r >= 225 && g >= 165 && g <= 240 && b >= 70 && b <= 200 && r - b >= 55)
            return 1;
        // 洞穴裡的暗金色鐘（實測 2026-11-21 斐揚洞穴 月夜貓：鐘身 (80～120,66～92,47～66)、底座金邊 (153,139,104)）：
        // JS 的金色規則（r ≥ 225）完全找不到，加一條暗金色。只在躲鐘那 15 秒才掃，誤判的機會小；灰色岩石 r−b 不到 30、洞穴地面太暗
        // 洛陽傍晚的地面 (110,80,70)、(90,70,60) 很接近，差在鐘身 g 比 b 多 15 以上、b 比較低，所以要求 g − b ≥ 15、b ≤ 75
        if (r >= 75 && r <= 175 && g >= 55 && g <= 150 && b >= 35 && b <= 75 && r - b >= 30 && r >= g && g - b >= 15)
            return 3;
        if (r >= 220 && g <= 120 && b <= 110)
            return 2;
        return 0;
    }

    /// <summary>
    /// 不掃的範圍（截圖像素，實測值見 <see cref="BallFinder"/> 的排除範圍）：左上角色框、上方計時器與金錢、系統公告橫幅、右上小地圖、下方道具列與技能列。
    /// 跟 BallFinder 不同：不排除自己的角色（站到鐘下時鐘會蓋住畫面中心）。
    /// </summary>
    private static Rectangle[] Excluded(Frame f, double dpr)
    {
        int w = f.Width, h = f.Height, cx = w / 2;
        int S(double v) => (int)Math.Round(v * dpr);
        return
        [
            new(0, 0, S(335), S(100)),
            new(cx - S(130), 0, S(260), S(100)),
            new(cx - S(215), S(95), S(430), S(60)),
            new(w - S(185), 0, S(185), S(210)),
            new(cx - S(150), h - S(130), S(300), S(130)),
        ];
    }
}
