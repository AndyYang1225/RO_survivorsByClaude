namespace RoHelper;

/// <summary>
/// 程式用到的檔案位置。設定、紀錄放 %AppData%\RoHelper（會跟著使用者漫遊、檔案小）；
/// WebView2 的瀏覽器資料（登入狀態、快取）放 %LocalAppData%\RoHelper\WebView2（快取可能很大，刻意不放漫遊資料夾）。
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoHelper");

    public static string WebViewDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoHelper", "WebView2");

    /// <summary>遊戲規則設定（JS 引擎的 cfg 原封不動存下來）</summary>
    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    /// <summary>選卡除錯紀錄</summary>
    public static string LogFile => Path.Combine(DataDir, "picklog.json");

    /// <summary>完整對局紀錄（每局一行 JSON，給分析用）</summary>
    public static string RunsFile => Path.Combine(DataDir, "runs.jsonl");

    /// <summary>視窗位置、面板置頂等 exe 自己的設定</summary>
    public static string AppSettingsFile => Path.Combine(DataDir, "app.json");

    /// <summary>
    /// 開發用：exe 旁邊放一個 engine.js 就會優先用它（改 JS 不用重新編譯），沒有就用內嵌在 exe 裡的版本。
    /// </summary>
    public static string EngineOverrideFile => Path.Combine(AppContext.BaseDirectory, "engine.js");

    /// <summary>自動更新下載到的最新引擎（下次啟動沒網路時也能用）</summary>
    public static string EngineCacheFile => Path.Combine(DataDir, "engine-cache.js");

    /// <summary>視覺判斷用的範本圖片（每個範本一個 PNG，加一個同名 JSON 記搜尋範圍、門檻、截圖時的 dpr）</summary>
    public static string TemplatesDir => Path.Combine(DataDir, "templates");

    /// <summary>視覺除錯視窗「存截圖」的輸出位置（原尺寸 PNG，跟引擎看到的畫面一模一樣，設計判讀方式用）</summary>
    public static string ShotsDir => Path.Combine(DataDir, "shots");

    /// <summary>自動存證據（異常與定時截圖＋紀錄，<c>--dev-api</c> 啟動時才存）</summary>
    public static string EvidenceDir => Path.Combine(ShotsDir, "auto");

    /// <summary>開發介面 /shot 的輸出位置</summary>
    public static string ApiShotsDir => Path.Combine(ShotsDir, "api");

    public const string GameUrl = "https://ro-survivors.com/";

    /// <summary>
    /// 自動更新的來源：跟使用者腳本同一個 Gist 裡的 ro_engine.js。
    /// 網址不帶 commit 版本，永遠拿到最新一版（GitHub 的快取最多延遲約 5 分鐘）。可在 app.json 的 EngineUpdateUrl 改掉。
    /// </summary>
    public const string DefaultEngineUpdateUrl =
        "https://gist.githubusercontent.com/AndyYang1225/eae1593b1faf106d4e8de3c22fb8a5ba/raw/ro_engine.js";
}
