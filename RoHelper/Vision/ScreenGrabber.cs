using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RoHelper.Vision;

/// <summary>
/// 截下 WebView2 目前顯示的畫面：Windows 的 PrintWindow（PW_RENDERFULLCONTENT），由 DWM 直接給視窗內容。
/// 頁面完全不知道被截圖，也不用執行任何 JS。
/// 實測（2026-11-20，1904 寬）比較過三種方式，PrintWindow 最快，視窗移到螢幕外（掛機）時也截得到：
/// - 刻意不用 WebView2 的 CapturePreviewAsync：每次都讓瀏覽器壓一張 PNG，實測要 1.5～2.2 秒
/// - 刻意不用 DevTools 的 Page.captureScreenshot：一樣要壓 PNG 再 base64，比 PrintWindow 慢
/// - 刻意不用螢幕截圖（Graphics.CopyFromScreen）：視窗被擋住或在螢幕外時，截到的是擋在前面的東西
/// </summary>
public sealed class ScreenGrabber
{
    private readonly IntPtr _hwnd;

    /// <param name="hwnd">WebView2 控制項的視窗代碼</param>
    public ScreenGrabber(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>
    /// 同步完成，包成 Task 是讓呼叫端不用管截圖方式（之後若要換回非同步的方式，呼叫端不用改）。
    /// 必須在 UI 執行緒呼叫：PrintWindow 會對自己的視窗送訊息，在別的執行緒呼叫時要等 UI 執行緒處理。
    /// </summary>
    public Task<Frame> CaptureAsync() => Task.FromResult(Capture());

    /// <summary>
    /// 用 Format32bppRgb：GDI 不寫 alpha，用 Argb 的話整張會是透明的；Rgb 在 LockBits 轉 Argb 時 alpha 會補成 255。
    /// </summary>
    private Frame Capture()
    {
        if (!NativeMethods.GetClientRect(_hwnd, out var rc) || rc.Right <= 0 || rc.Bottom <= 0)
            throw new InvalidOperationException("取不到遊戲畫面的大小");
        using var bmp = new Bitmap(rc.Right, rc.Bottom, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(_hwnd, hdc, NativeMethods.PwClientOnly | NativeMethods.PwRenderFullContent))
                    throw new InvalidOperationException("PrintWindow 失敗");
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }
        return Frame.FromBitmap(bmp);
    }

    private static class NativeMethods
    {
        public const uint PwClientOnly = 1;
        public const uint PwRenderFullContent = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    }
}
