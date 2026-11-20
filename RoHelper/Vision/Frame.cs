using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RoHelper.Vision;

/// <summary>
/// 一張截圖的像素：BGRA，每點 4 bytes，記憶體排列跟 GDI+ 的 Format32bppArgb 相同。
/// 刻意不直接拿 Bitmap 做判斷：Bitmap 每次讀像素都要 LockBits，也不是執行緒安全；
/// 判斷邏輯只吃這個純資料，才能用假圖寫單元測試。
/// </summary>
public sealed class Frame
{
    private byte[]? _gray;
    private Frame? _half;

    public Frame(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("像素陣列長度跟寬高不符", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA 像素。測試可以直接改；改了之後不要再讀 <see cref="Gray"/>（已快取的灰階不會更新）</summary>
    public byte[] Pixels { get; }

    /// <summary>灰階（每點 1 byte），第一次讀時計算後快取：同一張截圖常要比對好幾個範本</summary>
    public byte[] Gray => _gray ??= ComputeGray();

    /// <summary>縮成一半（最近鄰），第一次讀時計算後快取：範本比對的金字塔粗找用，同一張截圖會比好幾個範本</summary>
    public Frame Half => _half ??= Resize(0.5);

    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public static Frame FromBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = bmp.Width * 4;
            var px = new byte[row * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * row, row);
            return new Frame(bmp.Width, bmp.Height, px);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = Width * 4;
            for (var y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    /// <summary>裁切。超出畫面的部分自動裁掉；完全在畫面外時回傳 0×0 的 Frame</summary>
    public Frame Crop(Rectangle r)
    {
        r = Rectangle.Intersect(r, new Rectangle(0, 0, Width, Height));
        var row = r.Width * 4;
        var px = new byte[row * r.Height];
        for (var y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Pixels, ((r.Y + y) * Width + r.X) * 4, px, y * row, row);
        return new Frame(r.Width, r.Height, px);
    }

    /// <summary>
    /// 最近鄰縮放。範本比對要求「同一個來源像素放大後仍是同一個值」，用內插會把邊緣糊掉、分數變低；
    /// OCR 需要平滑放大，由 <see cref="OcrReader"/> 自己用 GDI+ 的 bicubic 處理。
    /// </summary>
    public Frame Resize(double scale)
    {
        var w = Math.Max(1, (int)Math.Round(Width * scale));
        var h = Math.Max(1, (int)Math.Round(Height * scale));
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(Height - 1, (int)(y / scale));
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(Width - 1, (int)(x / scale));
                Buffer.BlockCopy(Pixels, (sy * Width + sx) * 4, px, (y * w + x) * 4, 4);
            }
        }
        return new Frame(w, h, px);
    }

    // 整數版的 Rec.601 亮度：0.299 R + 0.587 G + 0.114 B
    private byte[] ComputeGray()
    {
        var g = new byte[Width * Height];
        for (int i = 0, p = 0; i < g.Length; i++, p += 4)
            g[i] = (byte)((Pixels[p + 2] * 299 + Pixels[p + 1] * 587 + Pixels[p] * 114) / 1000);
        return g;
    }
}
