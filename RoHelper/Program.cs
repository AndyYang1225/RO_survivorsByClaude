using RoHelper.Bridge;
using RoHelper.Forms;
using RoHelper.Storage;

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
        var bridge = new EngineBridge(config, log, runs);
        using var panel = new PanelForm(config, log, runs, bridge, settings);
        Application.Run(new GameForm(settings, bridge, panel));
    }
}
