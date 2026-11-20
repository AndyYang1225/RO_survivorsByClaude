namespace RoHelper.Vision;

/// <summary>
/// 存範本時決定搜尋範圍：框選範圍往四周各擴大「框的長邊＋畫面長邊的 5%」，換算成佔截圖的比例。
/// 為什麼要擴大：視窗大小改變時，置中或靠右的 UI 在比例座標上會移動一點；擴大到這個程度，常見的視窗大小變化都還在範圍內，
/// 又比整張找快很多（實測整張找一個按鈕 723 ms）。會到處移動的東西（NPC 名字）由除錯視窗的「整張畫面找」改用整張。
/// </summary>
public static class TemplateRegion
{
    public static RectangleF Around(Rectangle selection, Size frame)
    {
        var pad = Math.Max(selection.Width, selection.Height) + (int)(Math.Max(frame.Width, frame.Height) * 0.05);
        var r = Rectangle.Intersect(Rectangle.Inflate(selection, pad, pad), new Rectangle(Point.Empty, frame));
        return new RectangleF(
            (float)r.X / frame.Width, (float)r.Y / frame.Height,
            (float)r.Width / frame.Width, (float)r.Height / frame.Height);
    }
}
