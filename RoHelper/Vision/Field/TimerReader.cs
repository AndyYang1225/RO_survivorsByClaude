using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>畫面正上方計時器的狀態：平常（深藍字，倒數到下一隻王）或 MVP 死後的撿取倒數（紅字，同 JS 的 #timer.loot）</summary>
public enum TimerPhase
{
    Normal,
    Loot,
}

/// <summary>
/// 讀畫面正上方的計時器顏色，判斷是不是 MVP 死後的 15 秒撿取倒數。純像素判斷，不做 OCR（每輪都讀也很便宜）。
/// 實測（2026-11-21，dpr 1，視窗 1904×682）：計時器是白底圓角框，在畫面水平中央（框約 x 896～1005、y 11～34，白色底的範圍），
/// 外框是灰藍 (107,123,168)。平常是深藍字 (36,65,142)～(49,76,149)（野外1～7 在框附近量到 560～890 點）；
/// 撿取倒數是紅字 (192,58,44)～(196,76,64)，反鋸齒邊緣 (211,122,115)（撿取倒數量到 624 點紅字、深藍字 0 點）。
/// 白底（三色都 &gt; 235）在野外量到 355～940 點（字越多白底越少；野外7 有技能光圈蓋到框的上緣也有 564）；
/// 結算、首頁、選角色畫面在這一塊 0 點，選卡畫面 29 點。
/// 遊戲時間（「mm:ss」，平常往上數）用 OCR 讀（<see cref="ReadClockAsync"/>）。
/// </summary>
public static partial class TimerReader
{
    /// <summary>掃描範圍（dpr 1）：畫面水平中央左右各 70 px、y 4～48，把框包進來並留一點餘裕</summary>
    private const int HalfWidth = 70;
    private const int Top = 4;
    private const int Bottom = 48;

    /// <summary>白底至少這麼多點（× dpr²）才算看到計時器框：野外最少量到 355，選卡畫面 29</summary>
    private const int MinWhite = 150;

    /// <summary>字的點數至少這麼多（× dpr²）才判斷顏色：野外的字量到 560 點以上</summary>
    private const int MinText = 100;

    /// <summary>找不到計時器框（不在野外、被畫面擋住）或框裡看不到字時回 null</summary>
    public static TimerPhase? Read(Frame f, double dpr)
    {
        int cx = f.Width / 2, half = (int)(HalfWidth * dpr);
        int x0 = Math.Max(0, cx - half), x1 = Math.Min(f.Width, cx + half);
        int y0 = Math.Min(f.Height, (int)(Top * dpr)), y1 = Math.Min(f.Height, (int)(Bottom * dpr));
        int white = 0, red = 0, blue = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var c = f.At(x, y);
                if (c.R > 235 && c.G > 235 && c.B > 235)
                    white++;
                else if (IsRed(c))
                    red++;
                else if (IsBlue(c))
                    blue++;
            }
        var area = dpr * dpr;
        if (white < MinWhite * area || Math.Max(red, blue) < MinText * area)
            return null;
        return red > blue ? TimerPhase.Loot : TimerPhase.Normal;
    }

    /// <summary>紅字：實測 (192,58,44)～(211,122,115)，R 都 ≥ 190、比 G、B 都多 ≥ 89；門檻放寬到 R ≥ 150、多 ≥ 70</summary>
    private static bool IsRed((byte R, byte G, byte B) c) => c.R >= 150 && c.R - c.G >= 70 && c.R - c.B >= 70;

    /// <summary>
    /// 深藍字：實測 (36,65,142)～(49,76,149)，B−R ≥ 100。框的灰藍外框 (107,123,168) B−R 只有 61，
    /// 門檻取 B−R ≥ 80 把外框排除（外框兩種狀態都有，算進去會沖淡紅字）
    /// </summary>
    private static bool IsBlue((byte R, byte G, byte B) c) => c.B >= 120 && c.B - c.R >= 80 && c.R <= 120;

    /// <summary>
    /// OCR 計時器的「mm:ss」回傳秒數（讀不到回 null）。約 100 ms，呼叫端每 1 秒讀一次就好。
    /// 實測（2026-11-21，野外1～8、撿取倒數、升級累積0／3，dpr 1）：範圍 W/2 ± 60、y 8～38，放大 2 倍直接讀全部正確（「02 : 17」）；
    /// 不放大什麼都讀不到；紅色通道前處理反而會漏字（「: 43」），所以不做前處理。
    /// 撿取倒數（紅字）和 MVP 出現後也是「mm:ss」但是倒數，呼叫端要自己排除（見 <see cref="GameClock"/>）。
    /// </summary>
    public static async Task<int?> ReadClockAsync(OcrReader ocr, Frame f, double dpr)
    {
        var half = (int)(60 * dpr);
        var area = Rectangle.Intersect(new Rectangle(f.Width / 2 - half, (int)(8 * dpr), half * 2, (int)(30 * dpr)), new Rectangle(0, 0, f.Width, f.Height));
        if (area.Width == 0 || area.Height == 0)
            return null;
        var lines = await ocr.ReadAsync(f, area, 2);
        return ParseClock(string.Join(" ", lines.Select(l => l.Text)));
    }

    /// <summary>「mm:ss」→ 秒數：冒號可能讀成全形、分號、點；O、o 當 0；秒數要 00～59；讀不到回 null</summary>
    internal static int? ParseClock(string text)
    {
        var m = ClockRe().Match(text.Replace('O', '0').Replace('o', '0'));
        if (!m.Success)
            return null;
        int min = int.Parse(m.Groups[1].Value), sec = int.Parse(m.Groups[2].Value);
        return sec < 60 ? min * 60 + sec : null;
    }

    [GeneratedRegex(@"(\d{1,2})\s*[:：;.]\s*(\d{2})(?!\d)")]
    private static partial Regex ClockRe();
}
