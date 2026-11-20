using System.Diagnostics;
using System.Reflection;

using RoHelper.Storage;

namespace RoHelper.Engine;

/// <summary>
/// 決定要注入哪一份 JS 引擎，並從 Gist 下載新版（取代 Tampermonkey 的 @updateURL 自動更新）。
/// 選擇順序：
/// 1. exe 旁邊的 engine.js（開發用）：有它就只用它，也不檢查更新，免得改到一半被線上版蓋掉。
/// 2. 內建版本 vs 上次下載的快取，取版本較新、而且這支 exe 跑得動的那份。
/// 3. 線上版比 2. 新而且相容 → 存進快取並使用。
/// 下載失敗（沒網路、逾時、內容不對）一律安靜地退回本機版本，不擋遊戲啟動。
/// </summary>
public sealed class EngineUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly AppSettings _settings;

    public EngineUpdater(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>是否正在用 exe 旁邊的 engine.js（開發模式）</summary>
    public static bool UsingOverride => File.Exists(AppPaths.EngineOverrideFile);

    /// <summary>不連網，選出本機最適合的引擎</summary>
    public EngineScript LoadLocal()
    {
        if (UsingOverride
            && EngineScript.Parse(File.ReadAllText(AppPaths.EngineOverrideFile), "外部檔案") is { } dev)
            return dev;

        var embedded = LoadEmbedded();
        var cached = LoadCache();
        return cached is not null && cached.CompatibleWithExe && cached.Version > embedded.Version ? cached : embedded;
    }

    /// <summary>
    /// 檢查線上版本。比 <paramref name="current"/> 新而且相容時存進快取並回 <see cref="UpdateStatus.Available"/>。
    /// 失敗不拋例外，回 <see cref="UpdateStatus.Failed"/>：更新只是加分，不能讓它擋住遊戲。
    /// </summary>
    public async Task<UpdateResult> CheckAsync(EngineScript current, CancellationToken ct = default)
    {
        if (UsingOverride || string.IsNullOrWhiteSpace(_settings.EngineUpdateUrl))
            return new UpdateResult(UpdateStatus.Skipped);

        string source;
        try
        {
            // 加時間參數繞過中間的快取（GitHub raw 本身仍可能延遲幾分鐘）
            var url = _settings.EngineUpdateUrl + (_settings.EngineUpdateUrl.Contains('?') ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
            source = await Http.GetStringAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Debug.WriteLine($"檢查引擎更新失敗 : {ex.Message}");
            return new UpdateResult(UpdateStatus.Failed, Error: ex.Message);
        }

        // 內容不是引擎（例如 Gist 裡還沒放 ro_engine.js，回的是 404 頁面）也算失敗
        if (EngineScript.Parse(source, "已更新") is not { } remote)
            return new UpdateResult(UpdateStatus.Failed, Error: "下載到的內容不是小幫手引擎（缺少 // @version 檔頭）");
        if (remote.Version <= current.Version)
            return new UpdateResult(UpdateStatus.UpToDate, Remote: remote);
        if (!remote.CompatibleWithExe)
            return new UpdateResult(UpdateStatus.NeedNewerExe, Remote: remote);

        JsonFile.WriteText(AppPaths.EngineCacheFile, source);
        return new UpdateResult(UpdateStatus.Available, Remote: remote);
    }

    private static EngineScript LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoHelper.engine.js")
            ?? throw new InvalidOperationException("找不到內嵌的 engine.js，請確認 csproj 的 EmbeddedResource 設定");
        using var reader = new StreamReader(stream);
        return EngineScript.Parse(reader.ReadToEnd(), "內建")
            ?? throw new InvalidOperationException("內嵌的 engine.js 缺少 // @version 檔頭");
    }

    private static EngineScript? LoadCache()
    {
        try
        {
            return File.Exists(AppPaths.EngineCacheFile)
                ? EngineScript.Parse(File.ReadAllText(AppPaths.EngineCacheFile), "已更新")
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

public enum UpdateStatus
{
    /// <summary>沒檢查（關閉自動更新、或正在用外部 engine.js）</summary>
    Skipped,

    /// <summary>連不上或內容不對</summary>
    Failed,

    UpToDate,

    /// <summary>有新版，已下載到快取</summary>
    Available,

    /// <summary>有新版，但要先更新 exe（新引擎的 @min-exe 比這支 exe 高）</summary>
    NeedNewerExe,
}

/// <summary>檢查更新的結果。Remote＝線上那份（UpToDate、Available、NeedNewerExe 時有值）</summary>
public sealed record UpdateResult(UpdateStatus Status, EngineScript? Remote = null, string? Error = null);
