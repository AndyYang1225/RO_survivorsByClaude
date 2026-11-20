using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoHelper.Storage;

/// <summary>
/// JSON 檔讀寫的共用工具。寫入一律先寫暫存檔再取代（atomic），避免寫到一半當機把設定檔弄壞。
/// 讀取失敗（檔案不存在、格式壞掉）時回 null，由呼叫端用預設值，不讓程式因為設定檔啟動失敗。
/// </summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 刻意不跳脫中文：設定檔要讓人直接打開看得懂
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    public static JsonNode? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Write(string path, JsonNode node)
        => WriteText(path, node.ToJsonString(Options));

    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
