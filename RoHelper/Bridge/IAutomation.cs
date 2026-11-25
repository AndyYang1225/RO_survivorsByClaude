using System.Text.Json.Nodes;

namespace RoHelper.Bridge;

/// <summary>
/// 自動化引擎的介面（實作是 VisionEngine）。面板只透過它拿規則清單和狀態、送出設定修改。
/// </summary>
public interface IAutomation
{
    /// <summary>規則清單；還沒準備好時是 null</summary>
    EngineSchema? Schema { get; }

    event Action<EngineSchema>? SchemaReceived;

    /// <summary>狀態（runs、runLimit、treasure、next、action、paused），格式沿用舊 JS 引擎的 status 訊息</summary>
    event Action<JsonObject>? StatusReceived;

    /// <summary>面板改了幾個設定值</summary>
    void SendPatch(JsonObject patch);

    /// <summary>匯入設定：整份換掉</summary>
    void SendReplace(JsonObject cfg);
}
