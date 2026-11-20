using System.Globalization;
using System.Text.Json.Nodes;

using RoHelper.Bridge;

namespace RoHelper.Engine;

/// <summary>
/// 規則設定的預設值與修正，移植自 engine.js 的 cfg 初始值、applyStored、clampTimes（邏輯一行對一行）。
/// JS 模式下設定由 JS 擁有，不用這裡；視覺模式沒有 JS，由 VisionEngine 用這裡補預設值、處理面板的修改和匯入。
/// 刻意回傳新物件、不改傳入的物件：ConfigStore.Current 會被面板讀，就地改到一半被讀到會不一致。
/// </summary>
public static class ConfigDefaults
{
    // 魔法師／巫師的預設技能和神祇順序（同 JS 的 MAGE、MAGE_DEITY）
    private static readonly string[] MageOrder = ["雷爆術", "冰箭術", "雷擊術", "火箭術"];
    private static readonly string[] MageAfter = ["雷爆術"];
    private static readonly string[] MageDeity = ["雷爆術", "冰箭術", "雷擊術", "火箭術", "思想之神", "束縛之神", "戰爭之神"];

    /// <summary>全新的預設設定（JS 的 DEFAULT_CFG）</summary>
    public static JsonObject Build(EngineSchema schema)
    {
        var cfg = new JsonObject
        {
            ["paused"] = false,
            ["collapsed"] = false,
            ["tab"] = "升級",
            ["runs"] = 0,
            ["expeList"] = ToArray(schema.Stages),
            ["jobSel"] = "auto",
            ["curJob"] = "",
            ["itemPrio"] = new JsonArray(),
            ["skills"] = new JsonObject { ["魔法師"] = Mage(), ["巫師"] = Mage() },
            ["deity"] = new JsonObject { ["魔法師"] = ToArray(MageDeity), ["巫師"] = ToArray(MageDeity) },
        };
        foreach (var r in schema.Rules)
        {
            if (r.Key is not { } k)
                continue;
            if (!r.NoCheck)
                cfg[k] = r.Default?.DeepClone();
            if (r.Num is { } n)
                cfg[k + "N"] = n;
            if (r.Text is { } t)
                cfg[k + "T"] = t;
        }
        return cfg;
    }

    /// <summary>
    /// 套用存檔或匯入的設定（JS 的 applyStored）。reset＝先清回預設值再套（匯入時用，避免殘留舊鍵）。
    /// </summary>
    public static JsonObject Apply(JsonObject current, JsonObject? stored, EngineSchema schema, bool reset)
    {
        var cfg = reset ? Build(schema) : (JsonObject)current.DeepClone();
        if (stored is not null)
            foreach (var (k, v) in stored)
                cfg[k] = v?.DeepClone();

        // 舊版設定：deityJob 改名成 jobSel
        if (EngineSchema.Str(cfg["deityJob"]) is { } dj && EngineSchema.Str(cfg["jobSel"]) == "auto" && dj != "auto")
            cfg["jobSel"] = dj;
        // 活動和遠征互斥
        if (IsTrue(cfg["autoEvent"]) && IsTrue(cfg["autoExpe"]))
            cfg["autoEvent"] = false;
        // v7.4：預設改成神祇不特別優先
        if (ToNumber(cfg["deityOrderV"]) != 2)
        {
            cfg["deityOrder"] = "none";
            cfg["deityOrderV"] = 2;
        }
        if (!schema.Tabs.Contains(EngineSchema.Str(cfg["tab"]) ?? ""))
            cfg["tab"] = "升級";
        ClampTimes(cfg, schema);
        return cfg;
    }

    /// <summary>
    /// 時間設定一定要是範圍內的整數（JS 的 clampTimes）：不是數字時主迴圈的比較全部變 false，自動化會整個卡住。
    /// </summary>
    public static void ClampTimes(JsonObject cfg, EngineSchema schema)
    {
        foreach (var r in schema.Rules)
        {
            if (!r.NoCheck || r.Key is null)
                continue;
            var key = r.Key + "N";
            cfg[key] = ToNumber(cfg[key]) is { } v
                ? Math.Min(r.Max, Math.Max(r.Min, Math.Round(v, MidpointRounding.AwayFromZero)))
                : r.Num;
        }
    }

    private static JsonObject Mage() => new()
    {
        ["order"] = ToArray(MageOrder),
        ["after"] = ToArray(MageAfter),
        ["ban"] = new JsonArray(),
    };

    private static JsonArray ToArray(IEnumerable<string> list)
        => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    private static bool IsTrue(JsonNode? n) => n is JsonValue v && v.TryGetValue(out bool b) && b;

    /// <summary>
    /// 同 JS 的 +x：數字直接用、數字字串轉成數字、其他回 null。
    /// int／long 要分開試：在 C# 裡直接放的數字，JsonValue 包的是原本的型別，TryGetValue&lt;decimal&gt; 不會幫忙轉。
    /// </summary>
    private static decimal? ToNumber(JsonNode? n) => n switch
    {
        JsonValue v when v.TryGetValue(out decimal d) => d,
        JsonValue v when v.TryGetValue(out int i) => i,
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out double f) && double.IsFinite(f) => (decimal)f,
        JsonValue v when v.TryGetValue(out string? s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        _ => null,
    };
}
