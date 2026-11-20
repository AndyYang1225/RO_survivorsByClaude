using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RoHelper.DevApi;

/// <summary>
/// 開發介面的 HTTP 伺服器：只聽 <c>http://127.0.0.1:{port}/</c>（不需要系統管理員權限或 urlacl，而且只有本機程式連得到）。
/// 刻意不加驗證：只在使用者自己開發、用 <c>--dev-api</c> 啟動時才開。
/// 每個請求獨立處理，單一請求失敗不影響接下一個。接請求的迴圈在執行緒集區跑；碰到畫面的事由 <see cref="IDevApiHost"/> 的實作切回 UI 執行緒。
/// </summary>
public sealed class DevApiServer(DevApiRouter router, int port) : IDisposable
{
    /// <summary>縮排、中文直接輸出（不轉成 \uXXXX），用 curl 看得懂</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpListener _listener = new();

    public int Port => port;

    /// <summary>開始聽。port 被占用時丟 <see cref="HttpListenerException"/>，由呼叫端接住顯示</summary>
    public void Start()
    {
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        // 用 Task.Run：在 UI 執行緒呼叫 Start 時，迴圈的 await 才不會一直回到 UI 執行緒
        _ = Task.Run(LoopAsync);
    }

    public void Dispose() => _listener.Close();

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // Dispose 關掉 listener 時 GetContextAsync 會丟這幾種，正常結束
                return;
            }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var result = await router.HandleAsync(req.HttpMethod, req.Url?.AbsolutePath ?? "/", req.QueryString).ConfigureAwait(false);
            var bytes = Encoding.UTF8.GetBytes(result.Body.ToJsonString(JsonOptions));
            ctx.Response.StatusCode = result.Status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
        catch
        {
            // 為什麼吞掉：用戶端先斷線、伺服器關閉中時寫回應會失敗，這個請求放棄就好，不能讓例外變成未觀察的 Task 例外
            try
            {
                ctx.Response.Abort();
            }
            catch
            {
                // 已經關掉了，沒有別的能做
            }
        }
    }
}
