using Microsoft.Web.WebView2.Core;

namespace RoHelper.Vision;

/// <summary>
/// 視覺自動化的工具組：截圖、輸入、範本、OCR、座標換算。除錯視窗用它；計畫 2 起的視覺引擎也用同一組。
/// dpr 用委派即時讀：使用者把視窗拖到另一個縮放比例不同的螢幕時，dpr 會跟著變。
/// </summary>
public sealed class VisionKit
{
    private readonly Func<double> _dpr;

    /// <param name="hwnd">WebView2 控制項的視窗代碼（截圖用）</param>
    public VisionKit(CoreWebView2 core, IntPtr hwnd, Func<double> dpr)
    {
        _dpr = dpr;
        Grabber = new ScreenGrabber(hwnd);
        Input = new CdpInput(core);
        Templates = new TemplateLibrary(AppPaths.TemplatesDir);
        Ocr = OcrReader.TryCreate(out var error);
        OcrError = error;
    }

    public ScreenGrabber Grabber { get; }
    public CdpInput Input { get; }
    public TemplateLibrary Templates { get; }

    /// <summary>沒有繁中 OCR 時是 null，原因看 <see cref="OcrError"/></summary>
    public OcrReader? Ocr { get; }

    public string OcrError { get; }

    public ViewportMapper Viewport => new(_dpr());
}
