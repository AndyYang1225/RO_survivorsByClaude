using RoHelper.DevApi;
using RoHelper.Engine;
using RoHelper.Forms;
using RoHelper.Storage;
using RoHelper.Vision;

namespace RoHelper;

/// <summary>
/// 進入點。同一時間只允許開一個：兩個程式共用同一份設定檔和 WebView2 資料夾，會互相蓋掉設定。
/// </summary>
internal static class Program
{
    /// <param name="args"><c>--dev-api[=port]</c>：開本機開發介面並自動存證據（給 Claude 開發用，見 README）；沒帶時行為完全不變</param>
    [STAThread]
    private static void Main(string[] args)
    {
        // 多開：--profile=名稱 讓每個視窗有自己的設定、戰績、截圖、登入狀態（AppPaths.ProfileDir），要在任何路徑被用到之前決定
        if (ProfileOptions.Parse(args, out var profileError) is not { } profile)
        {
            MessageBox.Show(profileError, "RO Survivors 小幫手", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        AppPaths.UseProfile(profile);

        // 同一個 profile 同一時間只允許開一個：兩個程式共用同一份設定檔和 WebView2 資料夾，會互相蓋掉設定。不同 profile 可以同時開
        var mutexName = profile.Length == 0 ? @"Local\RoHelper.SingleInstance" : @"Local\RoHelper.SingleInstance." + profile;
        using var mutex = new Mutex(true, mutexName, out var isFirst);
        if (!isFirst)
        {
            var who = profile.Length == 0 ? "" : $"（profile {profile}）";
            MessageBox.Show($"RO Survivors 小幫手{who}已經在執行了。", "RO Survivors 小幫手", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        // 新的 profile 第一次開：複製一份預設 profile 的規則設定當起點（職業、技能優先、放逐清單不用重設），之後各改各的。刻意不複製戰績
        var rootConfig = Path.Combine(AppPaths.DataDir, "config.json");
        if (profile.Length > 0 && !File.Exists(AppPaths.ConfigFile) && File.Exists(rootConfig))
        {
            Directory.CreateDirectory(AppPaths.ProfileDir);
            File.Copy(rootConfig, AppPaths.ConfigFile);
        }
        // 視窗大小也照預設 profile：遊戲介面會跟著視窗高度縮放，範本是在預設視窗的大小下存的，
        // 實測（2026-11-21）新 profile 用預設大小（高 960）開，選角色的按鈕放大一圈，範本比對不上。位置往右下錯開 30，免得完全疊在一起
        if (profile.Length > 0 && !File.Exists(AppPaths.AppSettingsFile))
            AppSettings.SeedFrom(Path.Combine(AppPaths.DataDir, "app.json"), AppPaths.AppSettingsFile, offset: 30);

        var settings = AppSettings.Load();
        var config = new ConfigStore(AppPaths.ConfigFile);
        using var log = new LogStore(AppPaths.LogFile);
        var runs = new RunStore(AppPaths.RunsFile);
        var devApi = DevApiOptions.Parse(args);
        var evidence = devApi is null ? null : new EvidenceStore(AppPaths.EvidenceDir);
        // 只用視覺模式：不在遊戲網頁注入任何 JS（舊的 JS 引擎版本在 git tag js-engine-last）
        using var vision = new VisionEngine(config, log, runs, BuiltInSchema.Load(), evidence);
        using var panel = new PanelForm(config, log, runs, vision, settings);
        Application.Run(new GameForm(settings, vision, panel, config, devApi, evidence));
    }
}
