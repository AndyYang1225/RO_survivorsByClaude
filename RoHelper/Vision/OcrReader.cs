using System.Drawing.Drawing2D;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RoHelper.Vision;

/// <summary>OCR 讀到的一行字（Bounds 是截圖座標）</summary>
public sealed record OcrLine(string Text, Rectangle Bounds);

/// <summary>
/// Windows 內建的繁體中文 OCR（Windows.Media.Ocr）。
/// 刻意不用 Tesseract：要另外帶 chi_tra 語言檔（數十 MB），而且繁中辨識率不如 Windows 內建的。
/// 建立失敗（沒裝繁中 OCR 語言套件）時 <see cref="TryCreate"/> 回 null、不拋例外：OCR 只是視覺功能的一部分，沒有它其他功能照用。
/// </summary>
public sealed partial class OcrReader
{
    public const string LanguageTag = "zh-Hant-TW";

    private readonly OcrEngine _engine;

    private OcrReader(OcrEngine engine) => _engine = engine;

    public static OcrReader? TryCreate(out string error)
    {
        var lang = new Language(LanguageTag);
        if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } engine)
        {
            error = "";
            return new OcrReader(engine);
        }
        error = "這台電腦沒有繁體中文 OCR：Windows 設定 → 時間與語言 → 語言與地區 → 新增「中文（台灣）」，並勾選「光學字元辨識」。";
        return null;
    }

    /// <summary>
    /// 讀 region 裡的文字。
    /// upscale：先放大幾倍再辨識。遊戲字體小，實測放大 2 倍辨識率明顯較好；放大後超過 OCR 的尺寸上限會自動降低倍數。
    /// </summary>
    public async Task<IReadOnlyList<OcrLine>> ReadAsync(Frame frame, Rectangle region, double upscale = 2)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if (region.Width == 0 || region.Height == 0)
            return [];

        // 步驟 1：裁切並平滑放大（bicubic；Frame.Resize 是最近鄰，字的邊緣會鋸齒化）
        var crop = frame.Crop(region);
        var scale = Math.Min(upscale, (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
        var image = Math.Abs(scale - 1) < 0.01 ? crop : SmoothResize(crop, scale);

        // 步驟 2：辨識
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);

        // 步驟 3：座標換回截圖座標（除以放大倍數、加上裁切起點）
        var actual = (double)image.Width / crop.Width;
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
                continue;
            var x0 = line.Words.Min(w => w.BoundingRect.X);
            var y0 = line.Words.Min(w => w.BoundingRect.Y);
            var x1 = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            var y1 = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            var bounds = Rectangle.FromLTRB(
                region.X + (int)(x0 / actual), region.Y + (int)(y0 / actual),
                region.X + (int)Math.Ceiling(x1 / actual), region.Y + (int)Math.Ceiling(y1 / actual));
            lines.Add(new OcrLine(Compact(line.Text), Rectangle.Intersect(bounds, region)));
        }
        return lines;
    }

    /// <summary>
    /// Windows OCR 辨識中文時，每個字之間會插空白（「單 人 大 廳」）。只拿掉「中文字跟中文字之間」的空白，英數之間的保留（「HP 120 / 300」）。
    /// </summary>
    public static string Compact(string text) => CjkGap().Replace(text, "");

    // 中日韓文字、全形符號
    [GeneratedRegex(@"(?<=[　-鿿＀-￯])\s+(?=[　-鿿＀-￯])")]
    private static partial Regex CjkGap();

    private static Frame SmoothResize(Frame src, double scale)
    {
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        using var from = src.ToBitmap();
        using var to = new Bitmap(w, h);
        using (var g = Graphics.FromImage(to))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(from, 0, 0, w, h);
        }
        return Frame.FromBitmap(to);
    }
}
