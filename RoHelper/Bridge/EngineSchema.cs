using System.Text.Json.Nodes;

using RoHelper.Storage;

namespace RoHelper.Bridge;

/// <summary>
/// JS 引擎啟動時送來的「規則清單＋遊戲資料」（hello 訊息）。面板完全照這份產生，
/// 刻意不在 C# 重複定義規則：以後 JS 新增或改一條規則，面板自動跟上，不用改 C#。
/// </summary>
public sealed class EngineSchema
{
    public string Version { get; private init; } = "";
    public List<string> Tabs { get; private init; } = [];
    public List<RuleDef> Rules { get; private init; } = [];

    /// <summary>職業 → 該職業自己的技能（順序同遊戲）。二轉職業的一轉技能另由 <see cref="JobBase"/> 補上</summary>
    public List<KeyValuePair<string, List<string>>> JobSkills { get; private init; } = [];
    public Dictionary<string, string> JobBase { get; private init; } = [];
    public List<string> DeityAttr { get; private init; } = [];

    /// <summary>道具分類（藥水、秘藥…）→ 道具名稱</summary>
    public List<KeyValuePair<string, List<string>>> Items { get; private init; } = [];

    /// <summary>技能設定的狀態選項（值, 顯示文字）</summary>
    public List<Choice> SkillStates { get; private init; } = [];

    /// <summary>預設的遠征地圖清單（JS 的 DEFAULT_STAGES）。只有內建 schema.json 有；JS 的 hello 訊息不帶，JS 模式下是空的</summary>
    public List<string> Stages { get; private init; } = [];

    /// <summary>二轉職業也會帶一轉技能（同 JS 的 skillsOf）</summary>
    public List<string> SkillsOf(string job)
    {
        var list = new List<string>();
        if (JobBase.TryGetValue(job, out var baseJob))
            list.AddRange(SkillsOfJobOnly(baseJob));
        list.AddRange(SkillsOfJobOnly(job));
        return list;
    }

    private List<string> SkillsOfJobOnly(string job)
        => JobSkills.FirstOrDefault(x => x.Key == job).Value ?? [];

    public static EngineSchema Parse(JsonObject o)
    {
        // rules 裡的 tab 只寫在每組第一條，後面的沿用（同 JS 面板的 byTab 分組方式）
        var rules = new List<RuleDef>();
        var tab = "";
        foreach (var node in o["rules"] as JsonArray ?? [])
        {
            if (node is not JsonObject r)
                continue;
            tab = Str(r["tab"]) ?? tab;
            rules.Add(RuleDef.Parse(r, tab));
        }

        return new EngineSchema
        {
            Version = Str(o["version"]) ?? "",
            Tabs = ConfigStore.ToStringList(o["tabs"]),
            Rules = rules,
            JobSkills = ToGroups(o["jobSkills"]),
            JobBase = (o["jobBase"] as JsonObject ?? []).ToDictionary(x => x.Key, x => Str(x.Value) ?? ""),
            DeityAttr = ConfigStore.ToStringList(o["deityAttr"]),
            Items = ToGroups(o["items"]),
            SkillStates = Choice.ParsePairs(o["skStates"]),
            Stages = ConfigStore.ToStringList(o["stages"]),
        };
    }

    private static List<KeyValuePair<string, List<string>>> ToGroups(JsonNode? node)
        => (node as JsonObject ?? []).Select(x => KeyValuePair.Create(x.Key, ConfigStore.ToStringList(x.Value))).ToList();

    internal static string? Str(JsonNode? n)
        => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}

/// <summary>
/// 一條規則（對應 JS 的 RULES 陣列一個元素）。
/// 種類由欄位決定：有 Group＝分組標題；有 Options＝下拉選單；有 Text＝勾選＋文字框；有 Num＝勾選＋數字；其他＝單純勾選。
/// </summary>
public sealed class RuleDef
{
    public string Tab { get; private init; } = "";
    public string? Group { get; private init; }
    public string? Key { get; private init; }
    public string Label { get; private init; } = "";
    public decimal? Num { get; private init; }
    public decimal Min { get; private init; }
    public decimal Max { get; private init; } = 9999;
    public string? Text { get; private init; }
    public List<Choice>? Options { get; private init; }

    /// <summary>options: 'stages' ＝ 選項是遠征地圖清單（從設定的 expeList 動態來）</summary>
    public bool StageOptions { get; private init; }

    /// <summary>縮排層級 0、1、2</summary>
    public int Sub { get; private init; }

    /// <summary>互斥的另一條規則（勾這條時把那條取消）</summary>
    public string? Excl { get; private init; }

    /// <summary>只有數字、沒有勾選框（例如時間設定，一定生效）</summary>
    public bool NoCheck { get; private init; }

    /// <summary>勾選或下拉的預設值（JS 的 def）。視覺模式由 C# 補預設值時用</summary>
    public JsonNode? Default { get; private init; }

    public static RuleDef Parse(JsonObject r, string tab)
    {
        var opts = r["options"];
        return new RuleDef
        {
            Tab = tab,
            Group = EngineSchema.Str(r["group"]),
            Key = EngineSchema.Str(r["k"]),
            Label = EngineSchema.Str(r["label"]) ?? "",
            Num = Dec(r["num"]),
            Min = Dec(r["min"]) ?? 0,
            Max = Dec(r["max"]) ?? 9999,
            Text = EngineSchema.Str(r["text"]),
            Options = opts is JsonArray ? Choice.ParsePairs(opts) : EngineSchema.Str(opts) == "stages" ? [] : null,
            StageOptions = EngineSchema.Str(opts) == "stages",
            Sub = r["sub"] switch
            {
                JsonValue v when v.TryGetValue(out bool b) => b ? 1 : 0,
                JsonValue v when v.TryGetValue(out int i) => i,
                _ => 0,
            },
            Excl = EngineSchema.Str(r["excl"]),
            NoCheck = r["nocheck"] is JsonValue nc && nc.TryGetValue(out bool b2) && b2,
            Default = r["def"]?.DeepClone(),
        };
    }

    private static decimal? Dec(JsonNode? n)
        => n is JsonValue v && v.TryGetValue(out decimal d) ? d : null;
}

/// <summary>下拉選單的一個選項（ComboBox 顯示 Text，存 Value）</summary>
public sealed record Choice(string Value, string Text)
{
    public override string ToString() => Text;

    /// <summary>解析 JS 的 [['值', '文字'], ...] 格式</summary>
    public static List<Choice> ParsePairs(JsonNode? node)
        => (node as JsonArray ?? [])
            .OfType<JsonArray>()
            .Where(p => p.Count >= 2)
            .Select(p => new Choice(EngineSchema.Str(p[0]) ?? "", EngineSchema.Str(p[1]) ?? ""))
            .ToList();
}
