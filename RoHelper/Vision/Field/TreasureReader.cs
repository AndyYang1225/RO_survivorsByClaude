using System.Text.RegularExpressions;

namespace RoHelper.Vision.Field;

/// <summary>一個道具的說明框：Head＝第一行（道具名稱與等級，例如「範圍藥水 +0」），Treasure＝說明裡所有「N% 尋寶」加總</summary>
public sealed record TreasureTip(string Head, double Treasure);

/// <summary>畫面上的一個說明框（截圖像素）：Box＝整個框、BodyTop＝白色內文的上緣（上面是標題）</summary>
public sealed record TipBox(Rectangle Box, int BodyTop);

/// <summary>讀一次道具欄的結果：Total＝累計尋寶率（%）、Items＝每個道具說明框的第一行、DelayMs＝移過去到看到說明框最久花了多久（校正用）</summary>
public sealed record TreasureRead(double Total, IReadOnlyList<string> Items, long DelayMs);

/// <summary>
/// 累計尋寶率（移植 engine.js readTreasure 365～388）：JS 對道具欄每一格送 mouseover 事件、讀 #skill-tip；
/// 視覺模式用 CDP 把滑鼠真的移到每個有東西的格子上，等說明框出現、截圖、OCR，加總所有「N% 尋寶」，讀完把滑鼠移回畫面中間。
///
/// 實測（2026-11-21，dpr 1，視窗 1904×682）：
/// - 道具欄 5 格，中心 x＝畫面水平中心 −72／−36／0／+36／+72（880／916／952／988／1024），y＝畫面底部往上 102（580）；
///   格子約 32×32（第 1 格 x 865～897、y 564～596）。道具欄貼齊畫面下方、水平置中，所以 y 以底部為準。
/// - 有道具的格子是白底彩色圖示（格子中央 25×25 取樣：亮度 &gt; 235 的點 40～62%，亮度標準差 40～60）；
///   空格是半透明灰色（亮點 ≤ 4%，標準差 ≤ 19；野外1、野外4、升級累積0、野外2寶箱）。判斷見 <see cref="FilledSlots"/>。
/// - 說明框（道具說明框：滑鼠在第 1 格）x 795～966、y 476～553：水平置中於格子（中心 880.5），下緣在格子上緣上方約 11 px；
///   上半部是藍色漸層標題（「範圍藥水 +0／藥水・僅限本局」，RGB 約 150,176,229 → 59,86,155），下半部是近白色內文（約 245,247,252）。
///   找說明框見 <see cref="FindTip"/>：先找格子正上方的白色內文，再往左右找邊、沿左邊往上找藍色標題的上緣。
/// - 說明框的出現延遲截圖看不出來（fixture 是靜態的）：每 <see cref="PollMs"/> 截一次圖直到找到為止，最多等 <see cref="TipTimeout"/>，
///   實際花了多久記在 <see cref="TreasureRead.DelayMs"/>（開發介面 /field 的 treasure.delayMs）。
///   說明框若有淡入動畫，半透明時內文不夠白、不會被當成找到，會自然等到完全出現。
///
/// 跟 JS 不同：任一格等不到說明框（例如中途跳出選卡畫面）就整次放棄（回 null，呼叫端保留上次的值）——只加總一部分會讓尋寶率變少，
/// 可能誤判「沒超過上限」。百分比符號也接受全形「％」（OCR 有時讀成全形）。
/// 刻意不做：JS 順便從神祇格讀目前的神祇（st.deityLock）；視覺模式分不出哪一格是神祇，OCR 讀錯神祇名稱會讓神祇鎖定失效，鎖定交給選卡時記。
/// </summary>
public sealed partial class TreasureReader(OcrReader ocr)
{
    /// <summary>道具欄格數</summary>
    /// <remarks>遊戲 2026.11.23 版起 6 格（第 6 格上鎖時是灰色鎖頭，判斷成空格），整列連同右邊的「≡」按鈕水平置中</remarks>
    public const int SlotCount = 6;

    /// <summary>第 1 格中心離畫面水平中心多遠（dpr 1 的 px；實測 2026-11-24 第 1 格中心 x 843、畫面中心 952）</summary>
    private const double FirstSlotX = -109;

    /// <summary>格子中心間隔、格子中心離畫面底部多遠、格子半寬（dpr 1 的 px）</summary>
    private const double SlotStep = 36;
    private const double SlotFromBottom = 102;
    private const double SlotHalf = 16;

    /// <summary>判斷有沒有東西時，格子中心 ±12、每 2 px 取樣（13×13 點）</summary>
    private const double SampleHalf = 12;
    private const double SampleStep = 2;

    /// <summary>有東西：亮點（亮度 &gt; 235）至少 20%、亮度標準差至少 25（實測有東西 ≥ 40%、≥ 40；空格 ≤ 4%、≤ 19）</summary>
    private const double MinWhite = 0.2;
    private const double MinStdDev = 25;

    /// <summary>等說明框：每幾毫秒截一次圖、最多等多久</summary>
    public const int PollMs = 50;
    public const long TipTimeout = 800;

    /// <summary>說明框標題的高度（實測 476～522，約 46 px）：標題不是藍色（認不出來）時，OCR 範圍從內文上緣再往上這麼多</summary>
    private const double HeadHeight = 48;

    /// <summary>第 i 格（0 起算）的中心（截圖像素）</summary>
    public static PointF SlotCenter(Frame f, double dpr, int i)
        => new((float)(f.Width / 2.0 + (FirstSlotX + i * SlotStep) * dpr), (float)(f.Height - SlotFromBottom * dpr));

    /// <summary>有東西的格子（0 起算，由左到右）</summary>
    public static IReadOnlyList<int> FilledSlots(Frame f, double dpr)
    {
        var list = new List<int>();
        for (var i = 0; i < SlotCount; i++)
        {
            if (IsFilled(f, dpr, SlotCenter(f, dpr, i)))
                list.Add(i);
        }
        return list;
    }

    private static bool IsFilled(Frame f, double dpr, PointF c)
    {
        var step = Math.Max(1, (int)Math.Round(SampleStep * dpr));
        var half = (int)Math.Round(SampleHalf * dpr);
        int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);
        if (cx - half < 0 || cy - half < 0 || cx + half >= f.Width || cy + half >= f.Height)
            return false;
        int n = 0, white = 0;
        double sum = 0, sq = 0;
        for (var y = cy - half; y <= cy + half; y += step)
        {
            for (var x = cx - half; x <= cx + half; x += step)
            {
                var (r, g, b) = f.At(x, y);
                var l = (r + g + b) / 3.0;
                n++;
                sum += l;
                sq += l * l;
                if (l > 235)
                    white++;
            }
        }
        var mean = sum / n;
        var sd = Math.Sqrt(Math.Max(0, sq / n - mean * mean));
        return white >= MinWhite * n && sd >= MinStdDev;
    }

    /// <summary>
    /// 第 slot 格正上方的說明框（截圖像素；找不到回 null）。
    /// 1. 從格子上緣往上 30 px 內，找格子中心 ±30 都是白色內文的那一列（說明框的下緣附近）。
    /// 2. 在那一列往上 4 px 處往左右走到不是內文為止＝左右邊；寬度要 ≥ 80、中心要在格子中心 ±14 內（別格的說明框不算）。
    /// 3. 沿左邊往內 4 px 那一行往上走，是內文或藍色標題就繼續＝上緣；認不出標題（不是藍色）時上緣再往上 <see cref="HeadHeight"/>。
    /// </summary>
    public static TipBox? FindTip(Frame f, double dpr, int slot)
    {
        int S(double v) => (int)Math.Round(v * dpr);
        var c = SlotCenter(f, dpr, slot);
        var cx = (int)Math.Round(c.X);
        var slotTop = (int)Math.Round(c.Y - SlotHalf * dpr);
        if (cx - S(30) < 0 || cx + S(30) >= f.Width)
            return null;

        int? bottom = null;
        for (var y = slotTop - S(2); y >= Math.Max(0, slotTop - S(30)); y--)
        {
            var row = y;
            if (Enumerable.Range(-2, 5).All(k => IsBody(f.At(cx + k * S(15), row))))
            {
                bottom = y;
                break;
            }
        }
        if (bottom is not { } yb)
            return null;

        var ye = Math.Max(0, yb - S(4));
        var left = cx;
        while (left > 0 && IsBody(f.At(left - 1, ye)))
            left--;
        var right = cx;
        while (right < f.Width - 1 && IsBody(f.At(right + 1, ye)))
            right++;
        if (right - left + 1 < S(80) || Math.Abs((left + right) / 2.0 - cx) > S(14))
            return null;

        var xs = left + S(4);
        var top = ye;
        var bodyTop = ye;
        var limit = Math.Max(0, yb - S(300));
        while (top > limit)
        {
            var p = f.At(xs, top - 1);
            if (IsBody(p))
                bodyTop = top - 1;
            else if (!IsHead(p))
                break;
            top--;
        }
        // 標題不是藍色（其他種類的道具可能不同色）：至少把標題的高度包進來
        if (top == bodyTop)
            top = Math.Max(0, bodyTop - S(HeadHeight));
        return new TipBox(Rectangle.FromLTRB(left, top, right + 1, yb + 1), bodyTop);
    }

    /// <summary>說明框內文：近白色、偏藍（實測約 241～247, 244～248, 251～253）</summary>
    private static bool IsBody((byte R, byte G, byte B) p) => p.R >= 225 && p.G >= 225 && p.B >= 225 && p.B >= p.R;

    /// <summary>說明框標題：藍色漸層（實測 150,176,229 → 59,86,155；外框 107,123,168）</summary>
    private static bool IsHead((byte R, byte G, byte B) p) => p.B >= 140 && p.B - p.R >= 30;

    /// <summary>
    /// OCR 一個說明框（tip 由 <see cref="FindTip"/> 找到）。字很小（約 11 px），整框一次 OCR 時標題常漏字，所以分兩次讀：
    /// 標題（跳過左邊 38 px 的道具圖示）用 <see cref="OcrPrep.RedStretch"/> 拉開藍底白字的對比、放大 2 倍；
    /// 內文（跳過左邊 14 px 的項目符號「•」）直接放大 4 倍。
    /// 實測（道具說明框）：標題讀成「範藥水 +0」（「圍」常漏）、內文讀成「+ 18 % 範」；其他組合（整框、放大 2～5 倍、補白邊 0～20）都比較差。
    /// </summary>
    public async Task<TreasureTip> ReadTipAsync(Frame f, TipBox tip, double dpr)
    {
        int S(double v) => (int)Math.Round(v * dpr);
        var b = tip.Box;
        var head = new List<string>();
        var headArea = Rectangle.FromLTRB(Math.Min(b.Right, b.Left + S(38)), b.Top, b.Right, tip.BodyTop);
        if (headArea.Width > 0 && headArea.Height > 0 && OcrPrep.RedStretch(f, headArea, 10) is { } h)
            head.AddRange((await ocr.ReadAsync(h, new Rectangle(0, 0, h.Width, h.Height), 2)).Select(l => l.Text));
        var bodyArea = Rectangle.FromLTRB(Math.Min(b.Right, b.Left + S(14)), tip.BodyTop, b.Right, b.Bottom);
        var body = (await ocr.ReadAsync(f, bodyArea, 4)).Select(l => l.Text).ToList();
        return Parse(head, body);
    }

    /// <summary>
    /// 解析說明框文字：標題第一行＝道具名稱（去頭尾空白）；內文每一行的「N%」後面有「尋」或「寶」就算尋寶率（% 可以是全形），全部加總。
    /// 跟 JS 不同（JS 要求「N% 尋寶」）：小字 OCR 常把筆畫多的字讀錯或漏掉一個（實測「範圍」讀成「範」「範囹」），只要對到一個字就算。
    /// </summary>
    public static TreasureTip Parse(IReadOnlyList<string> head, IReadOnlyList<string> body)
    {
        var name = head.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return new TreasureTip(name, body.Sum(SumTreasure));
    }

    /// <summary>一行文字裡所有「N% 尋寶」的加總（規則同 <see cref="Parse"/>；選卡後估算尋寶率也用它）</summary>
    public static double SumTreasure(string line)
    {
        var sum = 0.0;
        foreach (Match m in TreasurePct().Matches(line))
        {
            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                sum += v;
        }
        return sum;
    }

    /// <summary>
    /// 讀一次道具欄：沒有道具時不移滑鼠，直接回 0。否則依序移到每個有東西的格子（moveTo 收截圖像素座標），
    /// 每 <see cref="PollMs"/> 截一次圖（capture）等說明框出現，OCR 後加總；任一格等不到說明框就回 null（整次放棄）。
    /// 不管成功與否，有移過滑鼠就移回畫面中間（同 JS 讀完送 mouseout）。clock＝現在的毫秒數（量延遲用）。
    /// </summary>
    public async Task<TreasureRead?> ReadAsync(Frame first, double dpr, Func<Task<Frame>> capture, Func<PointF, Task> moveTo, Func<long> clock)
    {
        var slots = FilledSlots(first, dpr);
        if (slots.Count == 0)
            return new TreasureRead(0, [], 0);
        var items = new List<string>();
        var sum = 0.0;
        long maxDelay = 0;
        try
        {
            foreach (var slot in slots)
            {
                await moveTo(SlotCenter(first, dpr, slot));
                var t0 = clock();
                Frame f;
                TipBox? box;
                while (true)
                {
                    await Task.Delay(PollMs);
                    f = await capture();
                    box = FindTip(f, dpr, slot);
                    if (box is not null)
                        break;
                    if (clock() - t0 >= TipTimeout)
                        return null;
                }
                maxDelay = Math.Max(maxDelay, clock() - t0);
                var tip = await ReadTipAsync(f, box, dpr);
                if (tip.Head.Length > 0)
                    items.Add(tip.Head);
                sum += tip.Treasure;
            }
        }
        finally
        {
            await moveTo(new PointF(first.Width / 2f, first.Height / 2f));
        }
        return new TreasureRead(sum, items, maxDelay);
    }

    /// <summary>「N%」後面（同一行、中間最多 3 個字）有「尋」或「寶」</summary>
    [GeneratedRegex(@"([\d.]+)\s*[%％]\s*[^\d%％]{0,3}?[尋寶]")]
    private static partial Regex TreasurePct();
}
