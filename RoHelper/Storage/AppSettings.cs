using System.Text.Json;

namespace RoHelper.Storage;

/// <summary>
/// exe 自己的設定（視窗位置、面板置頂），跟遊戲規則設定分開存：
/// 匯入規則設定時整份會被換掉，不應該連視窗位置都被蓋掉。
/// </summary>
public sealed class AppSettings
{
    public WindowBounds? GameBounds { get; set; }
    public bool GameMaximized { get; set; }
    public WindowBounds? PanelBounds { get; set; }
    public bool PanelTopMost { get; set; } = true;
    public bool PanelVisible { get; set; } = true;

    /// <summary>按最小化時改成把遊戲視窗移到螢幕外，讓遊戲繼續跑（真的最小化會暫停）</summary>
    public bool KeepRunningWhenMinimized { get; set; } = true;

    /// <summary>遊戲靜音（WebView2 的 IsMuted）：掛機、多開時聲音用不到，省下音效解碼和混音。遊戲視窗選單「靜音」切換</summary>
    public bool MuteAudio { get; set; } = true;

    /// <summary>
    /// 開啟時自動解除暫停。暫停狀態存在規則設定裡，關掉時是暫停（例如等打完這局才關），下次開還是暫停，
    /// 多開重開時每個視窗都要手動按繼續。遊戲視窗選單「選項 → 啟動時自動繼續」切換
    /// </summary>
    public bool ResumeOnStart { get; set; } = true;

    /// <summary>卡住時自動重新整理（見 StallWatch）。遊戲視窗選單「選項 → 卡住時自動重新整理」切換</summary>
    public bool StallRecovery { get; set; } = true;

    /// <summary>
    /// 用新版面板（WebView2 + HTML）。關掉＝用舊的 WinForms 面板（改版第 3 階段刪除舊面板前的退路）。
    /// 切換要重新啟動才生效：面板的內容在啟動時就決定了
    /// </summary>
    public bool WebPanel { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            var node = JsonFile.Read(AppPaths.AppSettingsFile);
            return node?.Deserialize<AppSettings>(JsonFile.Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save() => JsonFile.WriteText(AppPaths.AppSettingsFile, JsonSerializer.Serialize(this, JsonFile.Options));

    /// <summary>
    /// 多開：新 profile 第一次開時，照另一份 app.json 的遊戲視窗大小（位置往右下錯開 offset），面板位置也錯開。
    /// 來源不存在、讀不到或沒有視窗大小時什麼都不做（用預設大小）
    /// </summary>
    public static void SeedFrom(string sourceFile, string targetFile, int offset)
    {
        try
        {
            if (JsonFile.Read(sourceFile)?.Deserialize<AppSettings>(JsonFile.Options) is not { GameBounds: { } g } src)
                return;
            var seeded = new AppSettings
            {
                GameBounds = g with { X = g.X + offset, Y = g.Y + offset },
                PanelBounds = src.PanelBounds is { } p ? p with { X = p.X + offset, Y = p.Y + offset } : null,
                PanelTopMost = src.PanelTopMost,
                KeepRunningWhenMinimized = src.KeepRunningWhenMinimized,
                MuteAudio = src.MuteAudio,
                ResumeOnStart = src.ResumeOnStart,
                StallRecovery = src.StallRecovery,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            JsonFile.WriteText(targetFile, JsonSerializer.Serialize(seeded, JsonFile.Options));
        }
        catch (JsonException)
        {
            // 來源壞掉就算了，用預設大小開
        }
    }

    /// <summary>
    /// 存下來的位置如果已經不在任何螢幕上（例如拔掉外接螢幕），回 null 讓視窗用預設位置，免得視窗開在看不到的地方。
    /// </summary>
    public static Rectangle? OnScreen(WindowBounds? b)
    {
        if (b is null || b.Width < 100 || b.Height < 100)
            return null;
        var rect = new Rectangle(b.X, b.Y, b.Width, b.Height);
        return Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)) ? rect : null;
    }
}

/// <summary>
/// 視窗位置。刻意不直接存 <see cref="Rectangle"/>：它有 Location、Size、Left、Right… 一堆重複屬性，序列化出來又長又容易互相蓋掉。
/// </summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height)
{
    public static WindowBounds From(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
}
