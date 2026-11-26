using System.Text.Json;

namespace RoHelper.Storage;

/// <summary>
/// 每個視窗定時寫一份自己的狀態到 profile 資料夾的 status.json，給「多開總覽」讀。
/// 刻意用檔案不用開發介面：開發介面要帶 --dev-api 才有，總覽一般使用也要能用。
/// Args＝這個視窗啟動時的命令列參數，總覽要重開沒在執行的分身時照原樣帶。
/// Hwnd＝遊戲視窗的 handle（總覽切換時要用；程序的 MainWindowHandle 有時是面板，不是遊戲視窗）；舊版沒寫是 0。
/// </summary>
public sealed record InstanceStatus(int Pid, string[] Args, int Runs, string Action, bool Paused, string Warning, DateTime Updated, long Hwnd = 0)
{
    public const string FileName = "status.json";

    /// <summary>多久沒更新就當作沒在執行（遊戲視窗每 15 秒至少寫一次）</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(60);

    public bool IsAlive(DateTime now) => now - Updated < StaleAfter;

    public void Save(string profileDir)
        => JsonFile.WriteText(Path.Combine(profileDir, FileName), JsonSerializer.Serialize(this, JsonFile.Options));

    public static InstanceStatus? Load(string profileDir)
    {
        try
        {
            return JsonFile.Read(Path.Combine(profileDir, FileName))?.Deserialize<InstanceStatus>(JsonFile.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
