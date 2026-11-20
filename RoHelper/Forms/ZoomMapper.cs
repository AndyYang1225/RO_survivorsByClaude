namespace RoHelper.Forms;

/// <summary>
/// PictureBox 的 SizeMode = Zoom（圖片等比縮放、置中）下，控制項座標 ↔ 圖片座標的換算。WinForms 沒有內建這個換算。
/// </summary>
public static class ZoomMapper
{
    /// <summary>控制項座標 → 圖片座標；點在圖片外面（上下或左右留白處）回 null</summary>
    public static Point? ToImage(Size box, Size image, Point p)
    {
        if (image.Width == 0 || image.Height == 0)
            return null;
        var (s, ox, oy) = Fit(box, image);
        var x = (p.X - ox) / s;
        var y = (p.Y - oy) / s;
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            return null;
        return new Point((int)x, (int)y);
    }

    /// <summary>圖片座標的矩形 → 控制項座標（畫框選、比對結果用）</summary>
    public static Rectangle ToBox(Size box, Size image, Rectangle r)
    {
        var (s, ox, oy) = Fit(box, image);
        return new Rectangle((int)(ox + r.X * s), (int)(oy + r.Y * s), (int)(r.Width * s), (int)(r.Height * s));
    }

    private static (double Scale, double OffsetX, double OffsetY) Fit(Size box, Size image)
    {
        var s = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height);
        return (s, (box.Width - image.Width * s) / 2, (box.Height - image.Height * s) / 2);
    }
}
