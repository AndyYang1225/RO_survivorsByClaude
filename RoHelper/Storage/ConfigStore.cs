using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// 遊戲規則設定（JS 引擎的 cfg）的鏡像與存檔。
/// 設定的唯一擁有者是 JS 引擎：面板要改設定時送 patch 給 JS，JS 改完把整份 cfg 回傳，這裡再 <see cref="Replace"/> 並存檔。
/// 刻意不在 C# 直接改 <see cref="Current"/>：兩邊各改各的會不同步（例如 JS 自動偵測到職業、局數 +1 的同時，面板也在改）。
/// </summary>
public sealed class ConfigStore
{
    private readonly string _path;

    public ConfigStore(string path)
    {
        _path = path;
        Current = JsonFile.Read(path) as JsonObject ?? new JsonObject();
    }

    /// <summary>目前的設定（唯讀用途；要改請透過引擎送 patch）</summary>
    public JsonObject Current { get; private set; }

    /// <summary>JS 回傳新的整份設定後觸發</summary>
    public event Action? Changed;

    public void Replace(JsonObject cfg)
    {
        Current = cfg;
        JsonFile.Write(_path, cfg);
        Changed?.Invoke();
    }

    #region 讀值小工具（設定裡的值型別不固定：數字可能是 int 或 double、陣列可能不存在）

    public bool GetBool(string key)
        => Current[key] is JsonValue v && v.TryGetValue(out bool b) && b;

    public string GetString(string key, string fallback = "")
        => Current[key] is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : fallback;

    public decimal GetNumber(string key, decimal fallback = 0)
    {
        if (Current[key] is not JsonValue v)
            return fallback;
        if (v.TryGetValue(out decimal d))
            return d;
        if (v.TryGetValue(out double f))
            return (decimal)f;
        return fallback;
    }

    public List<string> GetStringList(string key)
        => ToStringList(Current[key]);

    public static List<string> ToStringList(JsonNode? node)
        => node is JsonArray arr
            ? arr.Select(x => x is JsonValue v && v.TryGetValue(out string? s) ? s : null).OfType<string>().ToList()
            : [];

    /// <summary>目前生效的職業：面板選「自動」時用偵測到的角色職業</summary>
    public string CurrentJob()
    {
        var sel = GetString("jobSel", "auto");
        return sel == "auto" ? GetString("curJob") : sel;
    }

    #endregion
}
