namespace RoHelper.Vision;

/// <summary>狀態紀錄的一筆：時間與面板上顯示的動作文字（不含「［視覺］」前綴）</summary>
public sealed record StatusEntry(DateTime Time, string Action);

/// <summary>
/// 視覺引擎的狀態紀錄（最近 N 筆，舊的丟掉）。給開發介面看「剛剛發生了什麼」、自動存證據時附上前後文。
/// 跟上一筆一樣就不記：主迴圈每 250 ms 回報一次，大部分時間都是同一句。
/// 執行緒：寫入在 UI 執行緒，讀取（開發介面）也切回 UI 執行緒，所以不加鎖。
/// </summary>
public sealed class StatusHistory(int capacity = 200)
{
    private readonly Queue<StatusEntry> _items = new();

    /// <summary>最後一筆的動作文字；還沒有紀錄時是空字串</summary>
    public string Last { get; private set; } = "";

    public void Add(DateTime time, string action)
    {
        if (action == Last && _items.Count > 0)
            return;
        Last = action;
        _items.Enqueue(new StatusEntry(time, action));
        while (_items.Count > capacity)
            _items.Dequeue();
    }

    /// <summary>最近 n 筆，舊的在前</summary>
    public IReadOnlyList<StatusEntry> Recent(int n) => [.. _items.Skip(Math.Max(0, _items.Count - n))];
}
