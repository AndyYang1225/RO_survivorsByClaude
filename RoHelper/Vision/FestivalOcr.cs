namespace RoHelper.Vision;

/// <summary>
/// 範本認不到節慶主持人時的備案（實測 2026-11-27 A 單人：廣場變亮，「節慶主持人」「交談提示」範本都只有 0.878，門檻 0.9，
/// 每局之間在大廳待機 10～17 分鐘）。用 OCR：
/// 交談提示：只裁按鈕那一塊讀（整張畫面一起讀時白字藍底讀不到；放大 2 倍讀得到「與主持人交談」，3 倍有時只讀到「中持人交談」，兩種都試）。
/// 站在別的 NPC 旁邊時同一顆按鈕是「與隊長交談」「與守護者交談」，所以要讀到「主持人」才算。
/// 名字標籤：整張畫面讀，常讀成「節慶主人」，認「節慶」。
/// </summary>
public static class FestivalOcr
{
    /// <summary>交談提示按鈕（dpr 1，1904×681 實測按鈕約 x 865～1040、y 600～642）</summary>
    public static Rectangle PromptArea(int w, int h, double dpr)
        => Rectangle.Intersect(new(w / 2 - (int)(90 * dpr), h - (int)(82 * dpr), (int)(180 * dpr), (int)(44 * dpr)), new Rectangle(0, 0, w, h));

    public static bool IsHostPrompt(string text) => Compact(text) is var t && (t.Contains("主持人") || t.Contains("持人交"));

    public static bool IsHostLabel(string text) => Compact(text) is var t && (t.Contains("節慶") || t.Contains("慶主持"));

    public static async Task<(Rectangle? Talk, Rectangle? Host)> FindAsync(OcrReader ocr, Frame f, double dpr)
    {
        var area = PromptArea(f.Width, f.Height, dpr);
        foreach (var scale in new[] { 2.0, 3.0 })
            if ((await ocr.ReadAsync(f, area, scale)).Any(l => IsHostPrompt(l.Text)))
                return (area, null);
        var host = (await ocr.ReadAsync(f, new Rectangle(0, 0, f.Width, f.Height), 1.5)).FirstOrDefault(l => IsHostLabel(l.Text));
        return (null, host?.Bounds);
    }

    private static string Compact(string s) => OcrReader.Compact(s).Replace(" ", "");
}
