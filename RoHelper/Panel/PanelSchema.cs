using System.Text.Json.Nodes;

using RoHelper.Bridge;

namespace RoHelper.Panel;

/// <summary>
/// 把規則清單（<see cref="EngineSchema"/>）轉成新版面板網頁用的 JSON。
/// 刻意由 C# 先展開職業技能（含二轉帶的一轉技能，見 <see cref="EngineSchema.SkillsOf"/>）：網頁不用知道 jobBase 的規則。
/// 沒有值的欄位不輸出，網頁用「有沒有這個欄位」判斷規則種類（同 <see cref="RuleDef"/> 的分類方式）。
/// </summary>
public static class PanelSchema
{
    public static JsonObject ToJson(EngineSchema schema) => new()
    {
        ["tabs"] = Strings(schema.Tabs),
        ["rules"] = new JsonArray(schema.Rules.Select(Rule).ToArray()),
        ["jobs"] = new JsonArray(schema.JobSkills.Select(j => (JsonNode?)new JsonObject
        {
            ["job"] = j.Key,
            ["skills"] = Strings(schema.SkillsOf(j.Key)),
        }).ToArray()),
        ["deityAttr"] = Strings(schema.DeityAttr),
        ["items"] = new JsonArray(schema.Items.Select(g => (JsonNode?)new JsonObject
        {
            ["category"] = g.Key,
            ["names"] = Strings(g.Value),
        }).ToArray()),
        ["skillStates"] = Pairs(schema.SkillStates),
    };

    private static JsonNode? Rule(RuleDef r)
    {
        var o = new JsonObject
        {
            ["tab"] = r.Tab,
            ["label"] = r.Label,
            ["sub"] = r.Sub,
            ["noCheck"] = r.NoCheck,
            ["min"] = r.Min,
            ["max"] = r.Max,
        };
        if (r.Group is { } group)
            o["group"] = group;
        if (r.Key is { } key)
            o["key"] = key;
        if (r.Num is { } num)
            o["num"] = num;
        if (r.Text is not null)
            o["text"] = true;
        if (r.Options is { } options)
            o["options"] = Pairs(options);
        if (r.StageOptions)
            o["stageOptions"] = true;
        if (r.Excl is { } excl)
            o["excl"] = excl;
        return o;
    }

    private static JsonArray Strings(IEnumerable<string> list)
        => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    private static JsonArray Pairs(IEnumerable<Choice> choices)
        => new(choices.Select(c => (JsonNode?)new JsonArray(JsonValue.Create(c.Value), JsonValue.Create(c.Text))).ToArray());
}
