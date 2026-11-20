using RoHelper.Bridge;
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
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\RoHelper.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("RO Survivors 小幫手已經在執行了。", "RO Survivors 小幫手", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var settings = AppSettings.Load();
        var config = new ConfigStore(AppPaths.ConfigFile);
        using var log = new LogStore(AppPaths.LogFile);
        var runs = new RunStore(AppPaths.RunsFile);
        // 【JS 引擎已停用（2026-11-20，使用者決定）】一律用視覺模式，不在遊戲網頁注入任何 JS。
        // EngineBridge 仍建立（GameForm 的建構子要它），但不會接到網頁上。要恢復 JS 模式：還原下面註解掉的兩行，或看 git tag js-engine-last。
        var bridge = new EngineBridge(config, log, runs);
        using var vision = new VisionEngine(config, log, BuiltInSchema.Load());
        // using var vision = settings.VisionMode ? new VisionEngine(config, BuiltInSchema.Load()) : null;
        // IAutomation automation = vision is null ? bridge : vision;
        using var panel = new PanelForm(config, log, runs, vision, settings);
        Application.Run(new GameForm(settings, bridge, vision, panel));
    }
}
