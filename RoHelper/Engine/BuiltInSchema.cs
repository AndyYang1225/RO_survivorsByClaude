using System.Reflection;
using System.Text.Json.Nodes;

using RoHelper.Bridge;

namespace RoHelper.Engine;

/// <summary>
/// 內建在 exe 裡的規則清單（Engine/schema.json，由 tools/gen-schema.js 從 engine.js 產生）。
/// 視覺模式不在網頁執行 JS，收不到 JS 的 hello 訊息，面板改用這份產生。
/// 過渡期間 engine.js 和 schema.json 各有一份，由 BuiltInSchemaTests 檢查兩邊一致；計畫 5 拿掉 JS 後只剩這份。
/// </summary>
public static class BuiltInSchema
{
    public static EngineSchema Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoHelper.schema.json")
            ?? throw new InvalidOperationException("找不到內建的 schema.json（檢查 csproj 的 EmbeddedResource）");
        var node = JsonNode.Parse(stream) as JsonObject
            ?? throw new InvalidOperationException("內建的 schema.json 格式錯誤");
        return EngineSchema.Parse(node);
    }
}
