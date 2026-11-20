using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("RoHelper.Tests")]

namespace RoHelper.Vision.Picker;

/// <summary>
/// 把一張截圖讀成選卡資訊（<see cref="PickerInfo"/>）。
/// 判讀方式：
/// 1. <see cref="CardLayout.FindCards"/> 找卡片，少於 2 張就不是選卡畫面（回 null）。
/// 2. 只做「一次」大範圍 OCR（所有卡片的聯集，左右各擴 40、往上擴 250、往下擴 180）：每次 OCR 有固定開銷，分卡各做一次會慢約 3 倍。
/// 3. 卡片上緣以上的 OCR 行含「寶箱」或「金幣」＝寶箱，否則是升級。
/// 4. 中心點落在某張卡內的 OCR 行屬於那張卡：卡名＝其中最高、含 2 個以上中文字的一行（圖示會被讀成單一怪字；大範圍 OCR 偶爾漏掉卡名，漏掉的那張再單獨 OCR 一次）；Text＝這些行依 Y 排序用空格接起來。
/// 5. 稀有度、「新！」、按鈕位置和能不能按都靠 <see cref="CardLayout"/> 的像素判讀（OCR 讀不到這些）。
///    按鈕剩餘次數：按鈕右上角的藍色小圓圈裡的白色數字。實測 Windows OCR 讀不了這種 5×8 px 的單一數字，所以改成找圓、切出數字、跟系統字型畫的 0～9 比字形。
///    讀不到時，能按就當 99、不能按就當 0（同 JS 的 actLeft），避免因為讀不準而誤以為次數用完。
/// 6. 「重抽全部」：卡片下緣以下、文字含「全部」的 OCR 行（OCR 常讀成「重擒全部」，但一定含「全部」），按鈕範圍由該行往外擴；沒有這一行就是 null。
/// </summary>
public sealed partial class PickerReader(OcrReader ocr)
{
    private static readonly string[] ChestWords = ["寶箱", "金幣"];

    /// <summary>讀選卡畫面；不是選卡畫面回 null</summary>
    public async Task<PickerInfo?> ReadAsync(Frame f, double dpr)
    {
        // 步驟 1：找卡片
        var cards = CardLayout.FindCards(f, dpr);
        if (cards.Count < 2)
            return null;

        // 步驟 2：一次 OCR 整個選卡區
        var union = cards.Aggregate(Rectangle.Union);
        var region = Rectangle.FromLTRB(
            union.Left - (int)(40 * dpr), union.Top - (int)(250 * dpr),
            union.Right + (int)(40 * dpr), union.Bottom + (int)(180 * dpr));
        region = Rectangle.Intersect(region, new Rectangle(0, 0, f.Width, f.Height));
        var lines = await ocr.ReadAsync(f, region);

        // 步驟 3：寶箱或升級
        var top = cards.Min(c => c.Top);
        var isChest = lines.Any(l => Center(l.Bounds).Y < top && ChestWords.Any(w => l.Text.Contains(w)));

        // 步驟 4、5：每張卡
        var infos = new List<CardInfo>();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var mine = lines.Where(l => card.Contains(Center(l.Bounds))).OrderBy(l => l.Bounds.Y).ToList();
            var name = PickName(mine);
            if (name == "")
                name = PickName(await ocr.ReadAsync(f, card));
            var text = string.Join(" ", mine.Select(l => l.Text));

            var (rarity, skill) = CardLayout.ReadBadge(f, card, dpr);
            var buttons = new List<ActButton>();
            foreach (var (kind, bounds, enabled) in CardLayout.FindButtons(f, card, dpr))
            {
                // Bounds 只是按鈕的下半截，完整按鈕的上緣約在它上方 12
                var fullTop = bounds.Y - (int)(12 * dpr);
                buttons.Add(new ActButton(kind, bounds, enabled, ReadLeft(f, bounds.Right, fullTop, enabled, dpr)));
            }
            infos.Add(new CardInfo(i, card, name, text, skill ? 0 : rarity, skill, CardLayout.HasNewRibbon(f, card, dpr), buttons));
        }

        // 步驟 6：重抽全部
        var bottom = cards.Max(c => c.Bottom);
        ActButton? rerollAll = null;
        var line = lines.Where(l => Center(l.Bounds).Y > bottom && l.Text.Contains("全部")).OrderBy(l => l.Bounds.Y).FirstOrDefault();
        if (line is not null)
        {
            var rect = Rectangle.Inflate(line.Bounds, (int)(16 * dpr), (int)(10 * dpr));
            var enabled = CardLayout.IsEnabled(f, rect);
            rerollAll = new ActButton(ActKind.Reroll, rect, enabled, ReadLeft(f, rect.Right, rect.Top, enabled, dpr));
        }

        return new PickerInfo(isChest, infos, rerollAll);
    }

    // 讀按鈕右上角小圓圈的數字；讀不到時能按＝99、不能按＝0。
    // 範圍大致是右緣 -8～+14、上緣 -14～+8，但實測圓圈位置跟按鈕有幾 px 差距，所以範圍取大一點（右緣 -18～+14、上緣 -24～+8），
    // 先找出範圍內的藍色圓底，再看圓裡的白色數字。
    // 為什麼不用 OCR：實測 Windows OCR 讀不了這種 5×8 px 的單一數字（原圖、放大 2～12 倍、二值化、補上下文字都試過，幾乎都讀成空白或「0」），
    // 所以改成比對字形：把圓內的白字切出來，縮成固定格子後跟系統字型畫的 0～9 比對。
    internal static int ReadLeft(Frame f, int right, int top, bool enabled, double dpr)
    {
        var area = Rectangle.Intersect(
            new Rectangle(right - (int)(18 * dpr), top - (int)(24 * dpr), (int)(32 * dpr), (int)(32 * dpr)),
            new Rectangle(0, 0, f.Width, f.Height));
        // 能按的按鈕不可能剩 0 次（剩 0 次按鈕會是灰的），讀到 0 代表讀錯，當讀不到
        if (area.Width > 0 && area.Height > 0 && ReadDigits(f.Crop(area), dpr) is { } n && (n > 0 || !enabled))
            return n;
        return enabled ? 99 : 0;
    }

    // 圓底的標準色（實測 ≈ (111,143,216)），容許誤差
    private const int FillR = 111, FillG = 143, FillB = 216, FillTolerance = 30;

    // 格子大小：每個數字縮成 GlyphW × GlyphH 再比對
    private const int GlyphW = 8, GlyphH = 12;

    private static readonly Lazy<double[][]> Templates = new(BuildTemplates);

    // 找藍色圓底（以重心為圓心、面積換算半徑），圓內的白字依紅色通道的對比算出「白的程度」（0～1），再依欄位的空隙切成一個個數字比對。
    // 找不到圓或讀不出就回 null
    internal static int? ReadDigits(Frame crop, double dpr)
    {
        long sx = 0, sy = 0;
        var count = 0;
        for (var y = 0; y < crop.Height; y++)
            for (var x = 0; x < crop.Width; x++)
            {
                var (r, g, b) = crop.At(x, y);
                if (Math.Abs(r - FillR) > FillTolerance || Math.Abs(g - FillG) > FillTolerance || Math.Abs(b - FillB) > FillTolerance)
                    continue;
                sx += x;
                sy += y;
                count++;
            }
        // 圓底太小：沒有圓（例如不能按的灰色圓圈）
        if (count < 20 * dpr * dpr)
            return null;

        double cx = (double)sx / count, cy = (double)sy / count;
        var radius = Math.Sqrt(count / Math.PI);
        var white = new double[crop.Width, crop.Height];
        for (var y = 0; y < crop.Height; y++)
            for (var x = 0; x < crop.Width; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d > radius * 0.95)
                    continue;
                var (r, _, _) = crop.At(x, y);
                white[x, y] = Math.Clamp((r - FillR - 20) / (255.0 - FillR - 20), 0, 1);
            }

        // 依欄位切數字：有白字的連續欄位算一個數字
        var colHasInk = Enumerable.Range(0, crop.Width).Select(x => Enumerable.Range(0, crop.Height).Any(y => white[x, y] > 0.4)).ToArray();
        var result = 0;
        var digits = 0;
        for (var x = 0; x < crop.Width; x++)
        {
            if (!colHasInk[x])
                continue;
            var x1 = x;
            while (x1 + 1 < crop.Width && colHasInk[x1 + 1])
                x1++;
            var digit = Classify(white, x, x1, crop.Height);
            x = x1;
            if (digit is null || ++digits > 3)
                return null;
            result = result * 10 + digit.Value;
        }
        return digits == 0 ? null : result;
    }

    // 信心門檻（實測 13 個真實圓圈：最佳字形的距離 3.1～9.6，最佳與次佳數字的差距最小 1.5）：
    // 最佳距離超過 MaxBestDist，或最佳與「另一個數字」的最佳距離差不到 MinMargin，就當讀不準（回 null，由呼叫端走 99／0 的退路）。
    // 門檻抓在實測範圍外一點，合成的雜訊色塊（實心方塊、寬白條）的距離遠大於此，會被擋下。
    private const double MaxBestDist = 12.0;
    private const double MinMargin = 1.0;

    // 單一數字的寬高比上限（實測 0.6～0.9）：超過代表像兩個數字黏在一起，不硬猜
    private const double MaxAspect = 1.3;

    // 把欄位 x0～x1 內的白字（裁到有字的列）縮成固定格子，跟 0～9 的字形比，取差最小的；信心不夠回 null
    private static int? Classify(double[,] white, int x0, int x1, int height)
    {
        int y0 = height, y1 = -1;
        for (var y = 0; y < height; y++)
            for (var x = x0; x <= x1; x++)
                if (white[x, y] > 0.4)
                {
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                }
        // 太矮：是雜點不是數字
        if (y1 - y0 + 1 < 4)
            return null;
        if ((x1 - x0 + 1) / (double)(y1 - y0 + 1) > MaxAspect)
            return null;

        double[][] templates;
        try
        {
            templates = Templates.Value;
        }
        catch (Exception)
        {
            // 系統字型畫不出來（沒有字型、GDI+ 失敗）：Lazy 會把例外快取起來每次都重丟，這裡吃掉並改回傳讀不到，讓呼叫端走退路
            return null;
        }

        var cell = Sample((x, y) => white[x, y], x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        var perDigit = new double[10];
        Array.Fill(perDigit, double.MaxValue);
        for (var i = 0; i < templates.Length; i++)
        {
            var dist = 0.0;
            for (var k = 0; k < cell.Length; k++)
                dist += (cell[k] - templates[i][k]) * (cell[k] - templates[i][k]);
            perDigit[i % 10] = Math.Min(perDigit[i % 10], dist);
        }
        var best = Array.IndexOf(perDigit, perDigit.Min());
        var second = perDigit.Where((_, d) => d != best).Min();
        if (perDigit[best] > MaxBestDist || second - perDigit[best] < MinMargin)
            return null;
        return best;
    }

    // 把 (x0,y0,w,h) 範圍縮成 GlyphW × GlyphH 格（每格取 3×3 個點的雙線性取樣平均，來源比格子大時也不會失真）
    private static double[] Sample(Func<int, int, double> at, int x0, int y0, int w, int h)
    {
        const int sub = 3;
        var cell = new double[GlyphW * GlyphH];
        double Px(int px, int py) => at(x0 + Math.Clamp(px, 0, w - 1), y0 + Math.Clamp(py, 0, h - 1));
        for (var gy = 0; gy < GlyphH; gy++)
            for (var gx = 0; gx < GlyphW; gx++)
            {
                double sum = 0;
                for (var j = 0; j < sub; j++)
                    for (var i = 0; i < sub; i++)
                    {
                        // 子取樣點對應到來源座標（像素中心在 +0.5）
                        var sx = (gx + (i + 0.5) / sub) * w / GlyphW - 0.5;
                        var sy = (gy + (j + 0.5) / sub) * h / GlyphH - 0.5;
                        var ix = (int)Math.Floor(sx);
                        var iy = (int)Math.Floor(sy);
                        var fx = sx - ix;
                        var fy = sy - iy;
                        sum += Px(ix, iy) * (1 - fx) * (1 - fy) + Px(ix + 1, iy) * fx * (1 - fy) +
                               Px(ix, iy + 1) * (1 - fx) * fy + Px(ix + 1, iy + 1) * fx * fy;
                    }
                cell[gy * GlyphW + gx] = sum / (sub * sub);
            }
        return cell;
    }

    // 用系統字型畫 0～9（粗體；遊戲的數字很粗，所以每個字型再畫一組加粗外框的），每個字取有字的範圍縮成同樣的格子。
    // 每組 10 個、索引 % 10 就是數字
    private static double[][] BuildTemplates()
    {
        var list = new List<double[]>();
        foreach (var name in new[] { "Arial", "Segoe UI", "Verdana" })
            foreach (var outline in new[] { 0f, 5f })
            {
                using var font = new Font(name, 48, FontStyle.Bold, GraphicsUnit.Pixel);
                for (var d = 0; d < 10; d++)
                {
                    using var bmp = new Bitmap(90, 90);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        using var path = new System.Drawing.Drawing2D.GraphicsPath();
                        path.AddString(d.ToString(), font.FontFamily, (int)FontStyle.Bold, 48, new PointF(10, 10), StringFormat.GenericDefault);
                        g.FillPath(Brushes.White, path);
                        if (outline > 0)
                        {
                            using var pen = new Pen(Color.White, outline) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                            g.DrawPath(pen, path);
                        }
                    }
                    var frame = Frame.FromBitmap(bmp);
                    int x0 = 90, y0 = 90, x1 = -1, y1 = -1;
                    for (var y = 0; y < 90; y++)
                        for (var x = 0; x < 90; x++)
                            if (frame.At(x, y).R > 128)
                            {
                                x0 = Math.Min(x0, x);
                                x1 = Math.Max(x1, x);
                                y0 = Math.Min(y0, y);
                                y1 = Math.Max(y1, y);
                            }
                    list.Add(Sample((x, y) => frame.At(x, y).R / 255.0, x0, y0, x1 - x0 + 1, y1 - y0 + 1));
                }
            }
        return [.. list];
    }

    // 卡名：含至少 2 個中文字、高度最大的一行（圖示常被 OCR 讀成單一個怪字，所以要求 2 字以上）
    private static string PickName(IEnumerable<OcrLine> lines)
        => lines.Where(l => CjkRegex().Matches(l.Text).Count >= 2).OrderByDescending(l => l.Bounds.Height).FirstOrDefault()?.Text ?? "";

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    [GeneratedRegex(@"[一-鿿]")]
    private static partial Regex CjkRegex();
}
