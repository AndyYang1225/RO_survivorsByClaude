using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// 遊戲規則設定（JS 引擎的 cfg）的鏡像與存檔。
/// JS 模式：設定由 JS 引擎擁有，面板要改設定時送 patch 給 JS，JS 改完把整份 cfg 回傳，這裡再 <see cref="Replace"/> 並存檔。
/// 視覺模式：設定由 VisionEngine 擁有，它改完整份 cfg 後直接 <see cref="Replace"/>。
/// 刻意不在引擎以外的地方直接改 <see cref="Current"/>：兩邊各改各的會不同步（例如自動偵測到職業、局數 +1 的同時，面板也在改）。
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

    /// <summary>引擎（JS 或 VisionEngine）換上新的整份設定後觸發</summary>
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

    /// <summary>
    /// 讀數字。int／long 要分開試：從 JSON 檔解析來的值 TryGetValue&lt;decimal&gt; 讀得到，
    /// 但在 C# 裡直接放的數字（視覺模式的局數 +1），JsonValue 包的是 int，不會幫忙轉成 decimal。
    /// </summary>
    public decimal GetNumber(string key, decimal fallback = 0)
    {
        if (Current[key] is not JsonValue v)
            return fallback;
        if (v.TryGetValue(out decimal d))
            return d;
        if (v.TryGetValue(out int i))
            return i;
        if (v.TryGetValue(out long l))
            return l;
        if (v.TryGetValue(out double f) && double.IsFinite(f))
            return ToDecimal(f);
        return fallback;
    }

    /// <summary>
    /// 讀整數，超出 int 範圍的夾到 int.MinValue／MaxValue。
    /// 匯入的設定可能有 1e10 這種數字，直接 (int) 轉型會拋 OverflowException，而主迴圈每輪都會讀設定，不能讓它掛掉。
    /// </summary>
    public int GetInt(string key, int fallback = 0)
    {
        var n = GetNumber(key, fallback);
        if (n >= int.MaxValue)
            return int.MaxValue;
        if (n <= int.MinValue)
            return int.MinValue;
        return (int)n;
    }

    /// <summary>
    /// double 轉 decimal，超出 decimal 範圍的夾到 MaxValue／MinValue。
    /// 超出範圍的數字直接轉型會拋 OverflowException；匯入壞掉的設定時要夾到上下限（之後由 ClampTimes 夾到規則的上下限），不能讓程式掛掉。
    /// </summary>
    public static decimal ToDecimal(double f)
    {
        if (f >= (double)decimal.MaxValue)
            return decimal.MaxValue;
        if (f <= (double)decimal.MinValue)
            return decimal.MinValue;
        return (decimal)f;
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
