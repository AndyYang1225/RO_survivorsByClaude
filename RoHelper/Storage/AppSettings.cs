using System.Text.Json;

namespace RoHelper.Storage;

/// <summary>
/// exe 自己的設定（視窗位置、面板置頂），跟遊戲規則設定分開存：
/// 規則設定是 JS 的資料，匯入舊版設定時整份會被換掉，不應該連視窗位置都被蓋掉。
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

    /// <summary>啟動時自動檢查引擎更新</summary>
    public bool AutoUpdateEngine { get; set; } = true;

    public string EngineUpdateUrl { get; set; } = AppPaths.DefaultEngineUpdateUrl;

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
