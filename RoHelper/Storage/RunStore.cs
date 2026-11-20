using System.Text;
using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// 完整對局紀錄（給分析用），每局一行 JSON 附加到 runs.jsonl，不限筆數。
/// 刻意用 JSON Lines 而不是一整個 JSON 陣列：每局只要附加一行，不用每次重寫整個檔案，檔案再大也不會變慢；
/// 寫到一半當機最多壞最後一行，前面的紀錄都還在。
/// 跟 config.json 的 stats（面板顯示的最近 50 局摘要）是兩回事：那份會被截斷，這份完整保留。
/// </summary>
public sealed class RunStore
{
    private readonly string _path;

    public RunStore(string path)
    {
        _path = path;
        Count = File.Exists(path) ? File.ReadLines(path).Count(l => l.Length > 0) : 0;
    }

    public int Count { get; private set; }

    public event Action? Changed;

    public void Append(JsonNode run)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // 一行一筆：不縮排；中文不跳脫，方便直接打開看
        var line = run.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = JsonFile.Options.Encoder });
        File.AppendAllText(_path, line + "\n", new UTF8Encoding(false));
        Count++;
        Changed?.Invoke();
    }
}
