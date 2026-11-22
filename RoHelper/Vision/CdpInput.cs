using System.Text.Json.Nodes;

using Microsoft.Web.WebView2.Core;

namespace RoHelper.Vision;

/// <summary>
/// 透過 DevTools Protocol 的 Input 網域送出滑鼠、滾輪、鍵盤輸入（座標是 CSS 像素）。
/// 這是瀏覽器層級的輸入，跟實體滑鼠走同一條輸入管線：網頁收到的事件 isTrusted = true，而且不會在頁面執行任何 JS。
/// 刻意不用 Windows 的 SendInput：那要搶前景視窗、真的移動游標，掛機時會干擾使用者正在做的事；視窗移到螢幕外時也點不到。
/// </summary>
public sealed class CdpInput
{
    private readonly CoreWebView2 _core;

    public CdpInput(CoreWebView2 core) => _core = core;

    public Task MoveAsync(double x, double y) => MouseAsync("mouseMoved", x, y, "none", 0, 0);

    /// <summary>移過去、按下、放開。先送 mouseMoved：有些遊戲 UI 要先收到 hover 才接受點擊</summary>
    public async Task ClickAsync(double x, double y)
    {
        await MouseAsync("mouseMoved", x, y, "none", 0, 0);
        await MouseAsync("mousePressed", x, y, "left", 1, 1);
        await MouseAsync("mouseReleased", x, y, "left", 0, 1);
    }

    /// <summary>滾輪。deltaY 正數＝往下滾（遊戲裡是把鏡頭拉遠）</summary>
    public Task WheelAsync(double x, double y, double deltaY)
        => SendAsync("Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = "mouseWheel",
            ["x"] = x,
            ["y"] = y,
            ["deltaX"] = 0,
            ["deltaY"] = deltaY,
        });

    /// <summary>
    /// 按一個鍵（數字 0～9 或英文字母）。keyDown 要帶 text，網頁才會收到 keypress／input，跟實體鍵盤一樣。
    /// </summary>
    public async Task KeyAsync(char key)
    {
        var upper = char.ToUpperInvariant(key);
        var code = char.IsDigit(key) ? $"Digit{key}" : $"Key{upper}";
        var text = key.ToString();
        await SendAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyDown",
            ["key"] = text,
            ["code"] = code,
            ["text"] = text,
            ["windowsVirtualKeyCode"] = (int)upper,
            ["nativeVirtualKeyCode"] = (int)upper,
        });
        await SendAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyUp",
            ["key"] = text,
            ["code"] = code,
            ["windowsVirtualKeyCode"] = (int)upper,
            ["nativeVirtualKeyCode"] = (int)upper,
        });
    }

    /// <summary>
    /// 在目前有焦點的輸入框打一段文字（CDP Input.insertText，等同輸入法送出文字，網頁會收到 input 事件）。
    /// 只給開發介面用（例如使用者要 Claude 幫忙登入測試帳號）；自動化流程本身不輸入文字。
    /// </summary>
    public Task TypeAsync(string text) => SendAsync("Input.insertText", new JsonObject { ["text"] = text });

    private Task MouseAsync(string type, double x, double y, string button, int buttons, int clickCount)
        => SendAsync("Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["button"] = button,
            ["buttons"] = buttons,
            ["clickCount"] = clickCount,
        });

    /// <summary>
    /// 最多等 10 秒：網頁被原生對話框擋住時 CDP 輸入可能一直不回應，主迴圈就卡在這一輪（_busy 不會清掉）。
    /// 逾時丟例外讓這一輪結束，卡住偵測（StallWatch）才看得到「畫面沒變」並重新整理
    /// </summary>
    private Task SendAsync(string method, JsonObject args)
        => _core.CallDevToolsProtocolMethodAsync(method, args.ToJsonString()).WaitAsync(TimeSpan.FromSeconds(10));
}
