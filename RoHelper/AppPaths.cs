namespace RoHelper;

/// <summary>
/// 程式用到的檔案位置。設定、紀錄放 %AppData%\RoHelper（會跟著使用者漫遊、檔案小）；
/// WebView2 的瀏覽器資料（登入狀態、快取）放 %LocalAppData%\RoHelper\WebView2（快取可能很大，刻意不放漫遊資料夾）。
/// </summary>
public static class AppPaths
{
    /// <summary>所有 profile 共用的根資料夾（範本、引擎快取放這裡）</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoHelper");

    private static readonly string LocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoHelper");

    /// <summary>
    /// 多開用的 profile 名稱（命令列 <c>--profile=名稱</c>）；空字串＝沒帶，路徑跟以前完全一樣。
    /// 啟動時（任何路徑被用到之前）由 <see cref="UseProfile"/> 設一次。
    /// </summary>
    public static string Profile { get; private set; } = "";

    /// <summary>設定 profile。只在程式啟動時呼叫一次（名稱要先用 <see cref="ProfileOptions.Parse"/> 檢查過）</summary>
    public static void UseProfile(string name) => Profile = name;

    /// <summary>
    /// 這個 profile 自己的資料夾：設定、戰績、截圖、視窗位置。沒帶 profile 時就是 <see cref="DataDir"/>；
    /// 有的話是 <c>%AppData%\RoHelper\profiles\名稱</c>。刻意不分 profile 的：範本（遊戲畫面一樣）、引擎快取
    /// </summary>
    public static string ProfileDir => ProfileDirOf(Profile);

    /// <summary>指定 profile 的資料夾（多開總覽、套用設定到其他分身用）；空字串＝預設 profile</summary>
    public static string ProfileDirOf(string profile) => profile.Length == 0 ? DataDir : Path.Combine(DataDir, "profiles", profile);

    /// <summary>
    /// 所有 profile：預設（空字串）在最前面，接著是 profiles 底下有 config.json 的資料夾（照名稱排）。
    /// 只列「開過的」：有 config.json 才算，沒開過的空資料夾不列
    /// </summary>
    public static IReadOnlyList<string> AllProfiles()
    {
        var list = new List<string> { "" };
        var root = Path.Combine(DataDir, "profiles");
        if (Directory.Exists(root))
            list.AddRange(Directory.GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, "config.json")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>給人看的 profile 名稱：預設 profile 叫「主視窗」</summary>
    public static string DisplayName(string profile) => profile.Length == 0 ? "主視窗" : profile;

    /// <summary>
    /// WebView2 的瀏覽器資料（Cookie＝登入狀態、快取）。每個 profile 分開，才能同時登入不同帳號。
    /// 小幫手不讀這個資料夾，只是讓內嵌瀏覽器存自己的東西
    /// </summary>
    public static string WebViewDir => Profile.Length == 0
        ? Path.Combine(LocalRoot, "WebView2")
        : Path.Combine(LocalRoot, "profiles", Profile, "WebView2");

    /// <summary>遊戲規則設定（JS 引擎的 cfg 原封不動存下來）</summary>
    public static string ConfigFile => Path.Combine(ProfileDir, "config.json");

    /// <summary>選卡除錯紀錄</summary>
    public static string LogFile => Path.Combine(ProfileDir, "picklog.json");

    /// <summary>完整對局紀錄（每局一行 JSON，給分析用）</summary>
    public static string RunsFile => Path.Combine(ProfileDir, "runs.jsonl");

    /// <summary>視窗位置、面板置頂等 exe 自己的設定</summary>
    public static string AppSettingsFile => Path.Combine(ProfileDir, "app.json");

    /// <summary>
    /// 開發用：exe 旁邊放一個 engine.js 就會優先用它（改 JS 不用重新編譯），沒有就用內嵌在 exe 裡的版本。
    /// </summary>
    public static string EngineOverrideFile => Path.Combine(AppContext.BaseDirectory, "engine.js");

    /// <summary>自動更新下載到的最新引擎（下次啟動沒網路時也能用）</summary>
    public static string EngineCacheFile => Path.Combine(DataDir, "engine-cache.js");

    /// <summary>視覺判斷用的範本圖片（每個範本一個 PNG，加一個同名 JSON 記搜尋範圍、門檻、截圖時的 dpr）</summary>
    public static string TemplatesDir => Path.Combine(DataDir, "templates");

    /// <summary>視覺除錯視窗「存截圖」的輸出位置（原尺寸 PNG，跟引擎看到的畫面一模一樣，設計判讀方式用）</summary>
    public static string ShotsDir => Path.Combine(ProfileDir, "shots");

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
