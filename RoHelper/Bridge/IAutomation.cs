using System.Text.Json.Nodes;

namespace RoHelper.Bridge;

/// <summary>
/// 自動化引擎的共同介面。面板只透過它拿規則清單和狀態、送出設定修改，不需要知道背後是 JS 引擎（<see cref="EngineBridge"/>）
/// 還是視覺引擎（VisionEngine）。兩種引擎同一時間只會有一個（app.json 的 VisionMode 決定），避免兩邊同時點畫面。
/// </summary>
public interface IAutomation
{
    /// <summary>規則清單；還沒準備好（JS 模式下遊戲頁面還沒載入）時是 null</summary>
    EngineSchema? Schema { get; }

    event Action<EngineSchema>? SchemaReceived;

    /// <summary>狀態（runs、runLimit、treasure、next、action、paused），格式同 JS 的 status 訊息</summary>
    event Action<JsonObject>? StatusReceived;

    /// <summary>面板改了幾個設定值</summary>
    void SendPatch(JsonObject patch);

    /// <summary>匯入設定：整份換掉</summary>
    void SendReplace(JsonObject cfg);
}
