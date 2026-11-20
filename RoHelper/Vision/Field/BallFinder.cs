namespace RoHelper.Vision.Field;

/// <summary>
/// 畫面上一塊符合扭蛋顏色的色塊。Kind 是命中最多的顏色規則（白／橘／紅／粉紅／綠）；X、Y 是外框中心、Width／Height 是外框寬高（截圖像素）；
/// N 是取樣點數；Rgb 是平均色；D 是離畫面中心的距離（截圖像素）；Fill 是外框裡命中的比例；
/// Click 是要點的位置（世界畫面比例 0～1，同 JS：球的位置往下 8 px，點球腳下的地面）。
/// </summary>
public sealed record BallBlob(string Kind, int X, int Y, int N, int Width, int Height, (int R, int G, int B) Rgb, double D, double Fill, PointF Click);

/// <summary>
/// 用畫面顏色找節慶扭蛋（移植 engine.js BALL、scanBalls）。顏色規則照搬 JS；大小門檻用真實截圖重新校正（JS 掃的是遊戲畫布、鏡頭距離不同）。
/// 實測（2026-11-21，dpr 1，視窗 1904×682）：野外6扭蛋 角色右下的珊瑚粉紅扭蛋直徑約 18 px（x 997～1020、y 352～371），
/// 上半部是亮面 (230,165,135)～(244,195,175)（不符合粉紅規則），下半部 (221,142,119) 符合；每 2 px 取樣得到一塊 18×12、34 點、填滿 0.63 的粉紅色塊。
/// 跟 JS 的差別（都是為了在截圖上更準）：
/// 1. 每 2 px 取樣（JS 縮成 1/3）：截圖上的扭蛋只有約 18 px，縮 1/3 只剩 6×6 格（粉紅只有 16 點），跟雜點分不開。
/// 2. 五種顏色先合成一張遮罩再分群（JS 每種顏色各自分群）：紅燈籠的中心是「紅」、邊緣是「粉紅／橘」，
///    各自分群的話邊緣會變成一塊扭蛋大小的粉紅色塊（野外5傍晚 實測 7×9 格）；合在一起才看得出整塊是燈籠的大小（42～66 px）。色塊的顏色取命中最多的規則。
/// 3. 真正的連通元件（8 方向、可跨 1 格的縫；JS 依掃描順序併到附近的外框）：不會因為掃描順序把一塊大的切成好幾塊扭蛋大小的碎片。
/// 4. 自己的角色用長方形排除（JS 是半徑 72 px 的圓）：扭蛋會掉在角色腳邊（野外6扭蛋 的扭蛋中心離畫面中心只有約 57 px），圓形會把它排除掉。
/// 5. 排除 HUD 與系統公告（見 <see cref="Excluded"/>）：HUD 上有粉紅、橘色的圖示。
/// 6. 多了形狀條件（填滿比例、長寬比）：傷害數字的碎片、技能光圈、經驗寶石也符合「白」，但比較細長或比較小（見 <see cref="IsBall"/>）。
/// 已知限制：「白」沒有真的白色扭蛋截圖可以校正；野外2寶箱 右側的閃電火花（(1672,464) 14×14、(1601,247) 16×12，平均色 (190,200,224)）
/// 大小形狀跟扭蛋一樣、會被當成「白」（JS 也一樣，所以 JS 的 colorBall 說「容易被技能特效誤判」）；追同一顆超過 6 秒會先忽略 15 秒（<see cref="FieldPlanner"/>）。
/// </summary>
public static class BallFinder
{
    private static readonly string[] Kinds = ["白", "橘", "紅", "粉紅", "綠"];

    /// <summary>取樣間隔（dpr 1 時每 2 px 取一點；取樣點數跟 dpr 無關）</summary>
    private const double Step = 2;

    /// <summary>連通時可以跨過的距離（取樣格）：2＝中間隔 1 格沒命中也算同一塊</summary>
    private const int Reach = 2;

    /// <summary>
    /// 扭蛋的取樣點數下限：野外6扭蛋 的扭蛋 34 點；經驗寶石最多 24 點、系統提示文字 20 點，取 26 排除它們
    /// </summary>
    private const int MinN = 26;

    /// <summary>外框寬高範圍（dpr 1 的 px）：扭蛋 18×12；燈籠 42 px 以上、柱子上的小光點 6～8 px</summary>
    private const int MinSide = 12;
    private const int MaxSide = 30;

    /// <summary>外框裡命中的比例下限：扭蛋 0.63；角色、怪物身上的零碎膚色 0.24～0.47、傷害星星的碎片 0.47</summary>
    private const double MinFill = 0.55;

    /// <summary>長寬比上限：扭蛋 1.5；傷害數字的碎片 20×10～22×10（2.0～2.2）</summary>
    private const double MaxAspect = 1.7;

    /// <summary>找出所有像扭蛋的色塊，離畫面中心近的在前面（同 JS 的排序）</summary>
    public static IReadOnlyList<BallBlob> Find(Frame f, double dpr)
        => Scan(f, dpr).Where(b => IsBall(b, dpr)).OrderBy(b => b.D).ToList();

    /// <summary>大小、形狀像不像扭蛋（門檻見各常數的實測值）</summary>
    private static bool IsBall(BallBlob b, double dpr)
    {
        int lo = (int)Math.Round(MinSide * dpr), hi = (int)Math.Round(MaxSide * dpr);
        return b.N >= MinN && b.Width >= lo && b.Height >= lo && b.Width <= hi && b.Height <= hi && b.Fill >= MinFill
            && Math.Max(b.Width, b.Height) <= MaxAspect * Math.Min(b.Width, b.Height);
    }

    /// <summary>
    /// 掃整張截圖（排除 HUD 與自己的角色），回傳所有色塊（不管大小；給 <see cref="Find"/> 篩選，也給開發介面校正門檻用）。
    /// 實測一張 1904×682 約 10 ms 以內（見測試），呼叫端每 0.5 秒掃一次（同 JS）。
    /// </summary>
    public static List<BallBlob> Scan(Frame f, double dpr)
    {
        var step = Math.Max(1, (int)Math.Round(Step * dpr));
        int gw = (f.Width + step - 1) / step, gh = (f.Height + step - 1) / step;
        // 先把不掃的格子標起來（每格比對五個長方形太慢）
        var skip = new bool[gw * gh];
        foreach (var e in Excluded(f, dpr))
            for (var gy = Math.Max(0, (e.Top + step - 1) / step); gy < gh && gy * step < e.Bottom; gy++)
                for (var gx = Math.Max(0, (e.Left + step - 1) / step); gx < gw && gx * step < e.Right; gx++)
                    skip[gy * gw + gx] = true;

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

        double cx = f.Width / 2.0, cy = f.Height / 2.0;
        var found = new List<BallBlob>();
        var seen = new bool[hits.Length];
        var stack = new Stack<int>();
        var counts = new int[Kinds.Length];
        for (var start = 0; start < hits.Length; start++)
        {
            if (hits[start] == 0 || seen[start])
                continue;
            Array.Clear(counts);
            int n = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            long sr = 0, sg = 0, sb = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                int gx = c % gw, gy = c / gw;
                n++;
                x0 = Math.Min(x0, gx);
                x1 = Math.Max(x1, gx);
                y0 = Math.Min(y0, gy);
                y1 = Math.Max(y1, gy);
                var i = (gy * step * f.Width + gx * step) * 4;
                sr += px[i + 2];
                sg += px[i + 1];
                sb += px[i];
                for (var k = 0; k < Kinds.Length; k++)
                    if ((hits[c] & (1 << k)) != 0)
                        counts[k]++;
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
            var kind = 0;
            for (var k = 1; k < Kinds.Length; k++)
                if (counts[k] > counts[kind])
                    kind = k;
            int bx = (x0 + x1) * step / 2, by = (y0 + y1) * step / 2;
            var click = new PointF((float)bx / f.Width, (float)((by + 8 * dpr) / f.Height));
            found.Add(new BallBlob(Kinds[kind], bx, by, n, (x1 - x0 + 1) * step, (y1 - y0 + 1) * step,
                ((int)(sr / n), (int)(sg / n), (int)(sb / n)), Math.Sqrt((bx - cx) * (bx - cx) + (by - cy) * (by - cy)),
                (double)n / ((x1 - x0 + 1) * (y1 - y0 + 1)), click));
        }
        return found;
    }

    /// <summary>
    /// 顏色規則（同 JS BALL，照搬）：回傳命中哪幾種顏色（第 k 位＝<see cref="Kinds"/>[k]），一個點可以同時命中好幾種（例如橘和粉紅有重疊）
    /// </summary>
    private static byte Match(int r, int g, int b)
    {
        var mask = 0;
        if (b >= r + 15 && b >= g + 12 && b >= 115)
            mask |= 1;
        if (r >= 180 && g >= 90 && g <= 190 && b <= 110 && r - b >= 90)
            mask |= 2;
        if (r >= 150 && g <= 80 && b <= 90 && r - g >= 90)
            mask |= 4;
        // 節慶扭蛋（珊瑚粉紅）
        if (r >= 200 && g >= 90 && g <= 160 && b >= 80 && b <= 160 && r - g >= 60)
            mask |= 8;
        // 蛇王扭蛋
        if (g >= 140 && g - r >= 40 && g - b >= 30)
            mask |= 16;
        return (byte)mask;
    }

    /// <summary>
    /// 不掃的範圍（截圖像素）。實測（2026-11-21，dpr 1，視窗 1904×682，畫面中心 (952,341)）：
    /// 左上角色框 x 10～322、y 10～90；上方計時器、金錢、天氣文字 x 845～1060、y 8～90；系統公告橫幅（野外2寶箱）x 742～1162、y 98～148；
    /// 右上小地圖 x 1731～1893、y 10～198；下方道具列 x 863～1041、y 563～597，技能列 x 813～1092、y 608～670；
    /// 自己的角色在畫面中心 x ±30、y −50～+42（拉遠前腳下的綠色血條在 y≈380、拉遠後 y≈363）。
    /// 左上、右上的 HUD 貼齊畫面角落；上方、下方的 HUD 與公告以畫面水平中央對齊。每塊都多留幾 px。
    /// </summary>
    private static Rectangle[] Excluded(Frame f, double dpr)
    {
        int w = f.Width, h = f.Height, cx = w / 2, cy = h / 2;
        int S(double v) => (int)Math.Round(v * dpr);
        return
        [
            new(0, 0, S(335), S(100)),
            new(cx - S(130), 0, S(260), S(100)),
            new(cx - S(215), S(95), S(430), S(60)),
            new(w - S(185), 0, S(185), S(210)),
            new(cx - S(150), h - S(130), S(300), S(130)),
            new(cx - S(36), cy - S(62), S(72), S(110)),
        ];
    }
}
