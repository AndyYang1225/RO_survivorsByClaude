using System.Drawing.Imaging;
using System.Text.Json;

using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>一個範本：圖片、搜尋範圍（佔截圖的比例 0～1）、門檻、截圖當下的 dpr</summary>
public sealed record TemplateInfo(string Name, Frame Image, RectangleF Region, double Threshold, double Dpr);

/// <summary>
/// 範本庫：資料夾裡每個範本是「名稱.png」＋「名稱.json」。JSON 壞掉或不存在時用預設值（整張截圖、門檻 0.9、dpr 1），不讓單一範本擋住其他範本。
/// 為什麼要記 dpr：遊戲的按鈕和文字是固定 CSS 大小，Windows 顯示縮放（100%／125%／150%）改了，截圖上的像素大小就跟著變；
/// 比對前依「目前 dpr ÷ 截圖當下的 dpr」縮放範本，換一台電腦或改了縮放，範本照樣能用。
/// 搜尋範圍用比例而不是像素：視窗大小會變，比例是比較穩定的近似值，實際值由計畫 2 起依各個按鈕校正。
/// </summary>
public sealed class TemplateLibrary
{
    public const double DefaultThreshold = 0.9;

    private const string BuiltInPrefix = "RoHelper.Templates.";

    private readonly string _dir;
    private readonly bool _builtIn;

    /// <summary>
    /// 範本清單。刻意不直接改這份字典：視覺引擎在背景執行緒比對範本（見 VisionEngine.TickAsync），
    /// 同時 UI 執行緒可能在重新載入、存新範本；改的時候一律複製一份改好再整份換上去，讀的人拿到的永遠是完整的一份
    /// </summary>
    private volatile Dictionary<string, TemplateInfo> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="builtIn">先載入 exe 內建的範本（專案的 Templates 資料夾），資料夾裡同名的範本蓋過內建的</param>
    public TemplateLibrary(string dir, bool builtIn = false)
    {
        _dir = dir;
        _builtIn = builtIn;
        Reload();
    }

    public IReadOnlyCollection<string> Names => _items.Keys;

    public TemplateInfo? Get(string name) => _items.GetValueOrDefault(name);

    public void Reload()
    {
        var items = new Dictionary<string, TemplateInfo>(StringComparer.OrdinalIgnoreCase);
        if (_builtIn)
            LoadBuiltIn(items);
        if (Directory.Exists(_dir))
        {
            foreach (var png in Directory.EnumerateFiles(_dir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(png);
                // 刻意先讀成 byte[] 再建 Bitmap：new Bitmap(path) 會鎖住檔案，之後就不能用同一個名稱覆蓋存檔
                Add(items, name, File.ReadAllBytes(png), ReadMeta(Path.ChangeExtension(png, ".json")));
            }
        }
        _items = items;
    }

    private static void LoadBuiltIn(Dictionary<string, TemplateInfo> items)
    {
        var asm = typeof(TemplateLibrary).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith(BuiltInPrefix) && r.EndsWith(".png")))
        {
            var name = res[BuiltInPrefix.Length..^".png".Length];
            using var png = asm.GetManifestResourceStream(res)!;
            using var buf = new MemoryStream();
            png.CopyTo(buf);
            var meta = TemplateMeta.Default;
            if (asm.GetManifestResourceStream(BuiltInPrefix + name + ".json") is { } js)
            {
                using (js)
                {
                    try { meta = JsonSerializer.Deserialize<TemplateMeta>(js, JsonFile.Options) ?? meta; }
                    catch (JsonException) { }
                }
            }
            Add(items, name, buf.ToArray(), meta);
        }
    }

    private static void Add(Dictionary<string, TemplateInfo> items, string name, byte[] png, TemplateMeta meta)
    {
        using var ms = new MemoryStream(png);
        using var bmp = new Bitmap(ms);
        items[name] = new TemplateInfo(name, Warm(Frame.FromBitmap(bmp)), new RectangleF(meta.X, meta.Y, meta.W, meta.H), meta.Threshold, meta.Dpr);
    }

    /// <summary>載入時先算好比對要用的灰階和縮圖（範本很小，幾乎不花時間），比對時不用在多個執行緒之間第一次計算</summary>
    private static Frame Warm(Frame image)
    {
        _ = image.Gray;
        _ = image.Half.Gray;
        return image;
    }

    /// <summary>exe 內建的範本名稱（測試用）</summary>
    public static IReadOnlyList<string> BuiltInNames()
        => typeof(TemplateLibrary).Assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(BuiltInPrefix) && r.EndsWith(".png"))
            .Select(r => r[BuiltInPrefix.Length..^".png".Length]).ToList();

    public void Save(string name, Frame image, RectangleF region, double threshold, double dpr)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("範本名稱不能是空的，也不能有 \\ / : * ? \" < > | 這些字元", nameof(name));
        Directory.CreateDirectory(_dir);
        using (var bmp = image.ToBitmap())
            bmp.Save(Path.Combine(_dir, name + ".png"), ImageFormat.Png);
        var meta = new TemplateMeta(region.X, region.Y, region.Width, region.Height, threshold, dpr);
        JsonFile.WriteText(Path.Combine(_dir, name + ".json"), JsonSerializer.Serialize(meta, JsonFile.Options));
        _items = new Dictionary<string, TemplateInfo>(_items, StringComparer.OrdinalIgnoreCase)
        {
            [name] = new TemplateInfo(name, Warm(image), region, threshold, dpr),
        };
    }

    /// <summary>找最像的位置，不管分數有沒有過門檻（除錯視窗要顯示分數）。範本不存在或比搜尋範圍大時回 null</summary>
    public MatchResult? Match(Frame frame, string name, double dpr)
        => Get(name) is { } t ? Match(frame, t, dpr) : null;

    private static MatchResult? Match(Frame frame, TemplateInfo t, double dpr)
    {
        var image = Math.Abs(dpr - t.Dpr) < 0.01 ? t.Image : t.Image.Resize(dpr / t.Dpr);
        var region = new Rectangle(
            (int)(t.Region.X * frame.Width),
            (int)(t.Region.Y * frame.Height),
            (int)Math.Ceiling(t.Region.Width * frame.Width),
            (int)Math.Ceiling(t.Region.Height * frame.Height));
        return TemplateMatcher.Find(frame, image, region);
    }

    /// <summary>找範本，分數達到門檻才算找到（範本只查一次：重新載入可能剛好在中間把字典換掉）</summary>
    public MatchResult? Find(Frame frame, string name, double dpr)
        => Get(name) is { } t && Match(frame, t, dpr) is { } m && m.Score >= t.Threshold ? m : null;

    /// <summary>
    /// 在指定範圍（截圖像素，不用範本自己存的搜尋範圍）找範本，分數達到門檻才算找到。
    /// 給「同一個按鈕出現在不同對話框」用：例如難度對話框的「前往野外」比活動的低，不在「前往野外」範本的搜尋範圍裡。
    /// </summary>
    public MatchResult? FindIn(Frame frame, string name, double dpr, Rectangle region)
    {
        if (Get(name) is not { } t)
            return null;
        var image = Math.Abs(dpr - t.Dpr) < 0.01 ? t.Image : t.Image.Resize(dpr / t.Dpr);
        return TemplateMatcher.Find(frame, image, Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height))) is { } m && m.Score >= t.Threshold ? m : null;
    }

    private static TemplateMeta ReadMeta(string path)
    {
        try
        {
            return JsonFile.Read(path)?.Deserialize<TemplateMeta>(JsonFile.Options) ?? TemplateMeta.Default;
        }
        catch (JsonException)
        {
            return TemplateMeta.Default;
        }
    }

    /// <summary>範本的 JSON 設定（X、Y、W、H 是佔截圖的比例）。用 internal 不用 private：保險起見讓 System.Text.Json 一定能用反射建構</summary>
    internal sealed record TemplateMeta(float X, float Y, float W, float H, double Threshold, double Dpr)
    {
        public static TemplateMeta Default { get; } = new(0, 0, 1, 1, DefaultThreshold, 1.0);
    }
}
