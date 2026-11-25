using RoHelper.Vision.Field;

namespace RoHelper.Vision;

/// <summary>
/// 「選擇大廳」視窗的多人大廳清單（組隊用）：遊戲 2026.11.23 版起進大廳先選大廳，單人大廳不能組隊。
/// 實測（dpr 1，1904×682）：「單人大廳」按鈕 x 1059～1144、y 522～552；六個大廳一列一個，在按鈕左上方 x 755～1148、y 236～494
/// （上次選的那一列多一行「上次」，比較高，所以用 OCR 找名字，不用固定間距）。
/// </summary>
public static class LobbyList
{
    /// <summary>遊戲裡的大廳（照畫面順序）</summary>
    public static readonly string[] Names = ["阿斯嘉特", "米德加爾特", "亞爾夫海姆", "華納海姆", "約頓海姆", "尼達維勒"];

    /// <summary>OCR 範圍：單人大廳按鈕右緣往左 420·dpr、上緣往上 320·dpr 到按鈕上方 10·dpr</summary>
    internal static Rectangle Area(Frame f, double dpr, Rectangle solo)
    {
        int x = solo.Right - (int)(420 * dpr), y = solo.Y - (int)(320 * dpr);
        return Rectangle.Intersect(new Rectangle(x, y, (int)(420 * dpr), (int)(310 * dpr)), new Rectangle(0, 0, f.Width, f.Height));
    }

    /// <summary>找名字最接近 want 的那一列（回該行文字的範圍，點它就選到那個大廳）；找不到回 null</summary>
    public static async Task<Rectangle?> FindAsync(OcrReader ocr, Frame f, double dpr, Rectangle solo, string want)
    {
        var area = Area(f, dpr, solo);
        if (area.Width == 0 || area.Height == 0)
            return null;
        var lines = await ocr.ReadAsync(f, area, 2);
        return Match(lines, want);
    }

    /// <summary>OCR 的每一行對到大廳名（讀錯一兩個字也認得，同遠征地名），回 want 那一行</summary>
    internal static Rectangle? Match(IEnumerable<OcrLine> lines, string want)
    {
        foreach (var line in lines)
        {
            var text = OcrReader.Compact(line.Text).Replace("正常", "").Replace("上次", "");
            if (text.Length >= 2 && JobNames.Closest(text, Names) == want)
                return line.Bounds;
        }
        return null;
    }
}
