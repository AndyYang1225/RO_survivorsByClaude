using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// 選卡除錯紀錄（面板勾「記錄每次選卡」時，JS 每選一次卡送一筆過來）。最多保留 300 筆，跟原腳本一樣。
/// 寫檔延遲 2 秒合併：選卡時可能一秒好幾筆，不必每筆都寫硬碟。
/// </summary>
public sealed class LogStore : IDisposable
{
    private const int MaxEntries = 300;
    private readonly string _path;
    private readonly List<JsonNode> _entries;
    private readonly System.Windows.Forms.Timer _flushTimer = new() { Interval = 2000 };

    public LogStore(string path)
    {
        _path = path;
        _entries = (JsonFile.Read(path) as JsonArray)?.Where(x => x is not null).Select(x => x!.DeepClone()).ToList() ?? [];
        _flushTimer.Tick += (_, _) => Flush();
    }

    public int Count => _entries.Count;

    public event Action? Changed;

    public void Add(JsonNode entry)
    {
        _entries.Add(entry.DeepClone());
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);
        _flushTimer.Stop();
        _flushTimer.Start();
        Changed?.Invoke();
    }

    public void Clear()
    {
        _entries.Clear();
        Flush();
        Changed?.Invoke();
    }

    /// <summary>複製用的純文字格式（跟原腳本「複製紀錄」一樣）</summary>
    public string ToText()
    {
        var lines = _entries.Select(l =>
        {
            var cards = ConfigStore.ToStringList(l["cards"]).Select((c, i) => $"{i + 1}. {c}");
            return $"{Str(l, "t")} [{Str(l, "job")}] {Str(l, "title")} 尋寶+{Str(l, "treasure")}%\n  "
                 + string.Join("\n  ", cards)
                 + $"\n  → {Str(l, "act")}（{Str(l, "why")}）";
        });
        return string.Join("\n", lines);
    }

    public void Flush()
    {
        _flushTimer.Stop();
        JsonFile.Write(_path, new JsonArray(_entries.Select(e => e.DeepClone()).ToArray()));
    }

    public void Dispose()
    {
        Flush();
        _flushTimer.Dispose();
    }

    private static string Str(JsonNode n, string key) => n[key]?.ToString() ?? "";
}
