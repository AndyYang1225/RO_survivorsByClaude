namespace RoHelper.Vision;

/// <summary>
/// 截圖像素座標 → 網頁 CSS 座標。截圖是實體像素，CDP 的輸入座標是 CSS 像素，兩者差一個 dpr
/// （= Windows 顯示縮放 × 頁面縮放，例如 125% 縮放時 1.25）。
/// dpr 由呼叫端提供（WebView2 控制項的 DeviceDpi／96 × ZoomFactor），刻意不從網頁讀 window.devicePixelRatio：那要在頁面執行 JS。
/// </summary>
public readonly record struct ViewportMapper(double Dpr)
{
    public (double X, double Y) ToCss(double x, double y) => (x / Dpr, y / Dpr);
}
