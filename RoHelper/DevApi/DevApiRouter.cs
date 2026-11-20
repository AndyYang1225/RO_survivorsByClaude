using System.Collections.Specialized;
using System.Globalization;
using System.Text.Json.Nodes;

namespace RoHelper.DevApi;

/// <summary>開發介面能做的事。座標都是截圖像素。實作（<c>DevApiHost</c>）負責切回 UI 執行緒</summary>
public interface IDevApiHost
{
    /// <summary>暫停狀態、局數、目前動作、最近 n 筆狀態紀錄、本局選卡狀態</summary>
    Task<JsonObject> StatusAsync(int n);

    /// <summary>截一張存到 shots/api（name 空的就用時間），回 { path, width, height, dpr }</summary>
    Task<JsonObject> ShotAsync(string? name);

    /// <summary>截一張，回每個大廳範本的最佳位置與分數、門檻，以及 ScreenReader 的判讀結果</summary>
    Task<JsonObject> ScreenAsync();

    /// <summary>截一張讀選卡；不是選卡畫面回 { picker: null }。有的話附上「現在設定下會怎麼選」</summary>
    Task<JsonObject> PickerAsync();

    /// <summary>在截圖像素 (x, y) 送一次真實點擊</summary>
    Task ClickAsync(int x, int y);

    /// <summary>按一個鍵（數字或英文字母）</summary>
    Task KeyAsync(char key);

    /// <summary>暫停或繼續（跟按 F8 一樣，面板會跟著更新）</summary>
    Task SetPausedAsync(bool paused);

    /// <summary>從範本資料夾重新讀所有範本（手動改過範本檔之後用）</summary>
    Task ReloadTemplatesAsync();

    /// <summary>從 shots 底下的一張 PNG（相對 shots 的路徑）裁一塊存成範本；已有同名範本時沿用它的搜尋範圍，沒有就整張畫面找</summary>
    Task<JsonObject> SaveTemplateAsync(string name, string shot, Rectangle crop, double threshold);

    /// <summary>最近 n 張自動存的證據（PNG 路徑，新的在前）</summary>
    Task<JsonObject> EvidenceAsync(int n);
}

/// <summary>一個請求的結果：HTTP 狀態碼與 JSON 內容</summary>
public sealed record DevApiResult(int Status, JsonNode Body);

/// <summary>
/// 開發介面的路由（純邏輯，不碰 HTTP）：方法＋路徑＋查詢參數 → 檢查參數 → 呼叫 <see cref="IDevApiHost"/> → 回 JSON。
/// 參數一律放在查詢字串（POST 也是），不讀 body：用 curl 打最簡單，而且所有參數都很短。
/// 錯誤：沒有這個路徑 404、方法不對 405、參數缺少或格式錯 400、host 丟例外 500，body 都是 { error: "…" }。
/// </summary>
public sealed class DevApiRouter(IDevApiHost host)
{
    /// <summary>端點清單（GET / 回這個；也用來分辨 404 和 405）</summary>
    private static readonly (string Method, string Path, string Help)[] Endpoints =
    [
        ("GET", "/", "這份清單"),
        ("GET", "/status", "暫停、局數、目前動作、最近 n 筆狀態紀錄（n 選填，預設 30，1～200）、本局選卡狀態"),
        ("GET", "/shot", "截一張存到 shots/api（name 選填，預設用時間），回路徑"),
        ("GET", "/screen", "截一張，回每個大廳範本的分數與 ScreenReader 判讀"),
        ("GET", "/picker", "截一張讀選卡，附上現在設定下會怎麼選（只預覽，不點）"),
        ("POST", "/click", "點擊截圖像素 x、y（必填）"),
        ("POST", "/key", "按一個鍵 k（必填，一個英數字元）"),
        ("POST", "/pause", "暫停"),
        ("POST", "/resume", "繼續"),
        ("POST", "/templates/reload", "重新讀範本"),
        ("POST", "/templates/save", "從 shots 底下的截圖裁一塊存成範本：name、shot、x、y、w、h（必填），threshold（選填，預設 0.9，0.5～1）"),
        ("GET", "/evidence", "最近 n 張自動存的證據（n 選填，預設 20，1～200）"),
    ];

    public async Task<DevApiResult> HandleAsync(string method, string path, NameValueCollection query)
    {
        method = method.ToUpperInvariant();
        // 結尾的斜線不算（/status/ 跟 /status 一樣），但根目錄就是 "/"
        if (path.Length > 1)
            path = path.TrimEnd('/');
        try
        {
            return await DispatchAsync(method, path, query);
        }
        catch (Exception ex)
        {
            return Error(500, ex.Message);
        }
    }

    private async Task<DevApiResult> DispatchAsync(string method, string path, NameValueCollection q)
    {
        string? error;
        switch ((method, path))
        {
            case ("GET", "/"):
                return Success(new JsonObject
                {
                    ["endpoints"] = new JsonArray(Endpoints.Select(e => (JsonNode?)JsonValue.Create($"{e.Method} {e.Path}：{e.Help}")).ToArray()),
                    ["note"] = "座標都是截圖像素；參數一律放在查詢字串",
                });
            case ("GET", "/status"):
                if (!TryInt(q, "n", 1, 200, 30, out var n, out error))
                    return Error(400, error);
                return Success(await host.StatusAsync(n));
            case ("GET", "/shot"):
                var name = q["name"];
                if (!string.IsNullOrEmpty(name) && !ValidName(name, "name", out error))
                    return Error(400, error);
                return Success(await host.ShotAsync(string.IsNullOrEmpty(name) ? null : name));
            case ("GET", "/screen"):
                return Success(await host.ScreenAsync());
            case ("GET", "/picker"):
                return Success(await host.PickerAsync());
            case ("POST", "/click"):
                if (!TryInt(q, "x", 0, int.MaxValue, null, out var x, out error) || !TryInt(q, "y", 0, int.MaxValue, null, out var y, out error))
                    return Error(400, error);
                await host.ClickAsync(x, y);
                return Ok();
            case ("POST", "/key"):
                var k = q["k"];
                if (k is not { Length: 1 } || !char.IsAsciiLetterOrDigit(k[0]))
                    return Error(400, "k 必須剛好是 1 個英文字母或數字");
                await host.KeyAsync(k[0]);
                return Ok();
            case ("POST", "/pause"):
                await host.SetPausedAsync(true);
                return Ok();
            case ("POST", "/resume"):
                await host.SetPausedAsync(false);
                return Ok();
            case ("POST", "/templates/reload"):
                await host.ReloadTemplatesAsync();
                return Ok();
            case ("POST", "/templates/save"):
                return await SaveTemplateAsync(q);
            case ("GET", "/evidence"):
                if (!TryInt(q, "n", 1, 200, 20, out var count, out error))
                    return Error(400, error);
                return Success(await host.EvidenceAsync(count));
        }
        return Endpoints.Any(e => e.Path == path)
            ? Error(405, $"{path} 不接受 {method}")
            : Error(404, $"沒有這個端點：{method} {path}");
    }

    private async Task<DevApiResult> SaveTemplateAsync(NameValueCollection q)
    {
        var name = q["name"];
        var shot = q["shot"];
        if (string.IsNullOrEmpty(name))
            return Error(400, "缺少 name");
        if (!ValidName(name, "name", out var error))
            return Error(400, error);
        if (string.IsNullOrEmpty(shot))
            return Error(400, "缺少 shot");
        // 防止讀到 shots 資料夾以外的檔案
        if (shot.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(shot) || shot.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return Error(400, "shot 必須是 shots 資料夾底下的相對路徑，不能有 ..");
        if (!TryInt(q, "x", 0, int.MaxValue, null, out var x, out error)
            || !TryInt(q, "y", 0, int.MaxValue, null, out var y, out error)
            || !TryInt(q, "w", 1, int.MaxValue, null, out var w, out error)
            || !TryInt(q, "h", 1, int.MaxValue, null, out var h, out error))
            return Error(400, error);
        if (!TryDouble(q, "threshold", 0.5, 1, 0.9, out var threshold, out error))
            return Error(400, error);
        return Success(await host.SaveTemplateAsync(name, shot, new Rectangle(x, y, w, h), threshold));
    }

    /// <summary>讀整數參數。fallback 是 null＝必填</summary>
    private static bool TryInt(NameValueCollection q, string key, int min, int max, int? fallback, out int value, out string error)
    {
        error = "";
        var raw = q[key];
        if (string.IsNullOrEmpty(raw))
        {
            value = fallback ?? 0;
            if (fallback is null)
                error = $"缺少 {key}";
            return fallback is not null;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < min || value > max)
        {
            error = max == int.MaxValue ? $"{key} 必須是 ≥ {min} 的整數（收到 {raw}）" : $"{key} 必須是 {min}～{max} 的整數（收到 {raw}）";
            return false;
        }
        return true;
    }

    /// <summary>讀小數參數（選填）</summary>
    private static bool TryDouble(NameValueCollection q, string key, double min, double max, double fallback, out double value, out string error)
    {
        error = "";
        var raw = q[key];
        value = fallback;
        if (string.IsNullOrEmpty(raw))
            return true;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < min || value > max)
        {
            error = $"{key} 必須是 {min}～{max} 的數字（收到 {raw}）";
            return false;
        }
        return true;
    }

    /// <summary>當檔名用的參數不能有 \ / : * ? " &lt; &gt; | 這些字元</summary>
    private static bool ValidName(string value, string key, out string error)
    {
        error = value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? $"{key} 不能有 \\ / : * ? \" < > | 這些字元" : "";
        return error.Length == 0;
    }

    private static DevApiResult Success(JsonObject body) => new(200, body);

    private static DevApiResult Ok() => new(200, new JsonObject { ["ok"] = true });

    private static DevApiResult Error(int status, string message) => new(status, new JsonObject { ["error"] = message });
}
