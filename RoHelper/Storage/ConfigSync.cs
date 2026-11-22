using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// 「套用設定到其他分身」：把這個視窗的規則（選卡、走位…）送給其他 profile。
/// 不能直接改對方的 config.json：對方如果正在執行，設定在記憶體裡，下一次存檔（例如局數 +1）就把我們寫的蓋掉。
/// 所以改成在對方資料夾放一份 config.pending.json，對方主迴圈每輪看一下，有就併進自己的設定（走正常的 SendPatch）再刪掉；
/// 對方沒在執行的話，下次開啟後第一輪就會收到。
/// </summary>
public static class ConfigSync
{
    public const string PendingFile = "config.pending.json";

    /// <summary>
    /// 不送的鍵：各帳號自己的狀態（暫停、局數、戰績、偵測到的職業、面板分頁），
    /// 以及玩法選擇（活動或遠征、哪張地圖、局數上限）：實測時就有一個帳號跑遠征、其他跑活動
    /// </summary>
    private static readonly HashSet<string> PerAccount =
        ["paused", "collapsed", "tab", "runs", "stats", "curJob", "lastStage", "autoEvent", "autoExpe", "runLimit", "runLimitN"];

    /// <summary>要送出去的部分（深拷貝）。遠征的設定（expe 開頭）也算玩法選擇，不送</summary>
    public static JsonObject Shareable(JsonObject cfg)
    {
        var result = new JsonObject();
        foreach (var (k, v) in cfg)
            if (!PerAccount.Contains(k) && !k.StartsWith("expe", StringComparison.Ordinal))
                result[k] = v?.DeepClone();
        return result;
    }

    /// <summary>送到指定 profile 的資料夾。已經有一份還沒收的就整份換掉（新的比較新）</summary>
    public static void Push(JsonObject cfg, string profileDir)
        => JsonFile.Write(Path.Combine(profileDir, PendingFile), Shareable(cfg));

    /// <summary>收下送給自己的設定並刪掉檔案；沒有（或壞掉）回 null。壞掉的也刪，免得每輪都讀一次</summary>
    public static JsonObject? TakePending(string profileDir)
    {
        var path = Path.Combine(profileDir, PendingFile);
        if (!File.Exists(path))
            return null;
        var node = JsonFile.Read(path) as JsonObject;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 對方剛好在寫：下一輪再收
            return null;
        }
        return node is null ? null : Shareable(node);
    }
}
