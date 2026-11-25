using System.Text.Json.Nodes;

using RoHelper.Forms;
using RoHelper.Storage;

namespace RoHelper.Panel;

/// <summary>
/// 新版面板送來的「改設定」訊息 → 給引擎的 patch（<see cref="Bridge.IAutomation.SendPatch"/>）。純邏輯，以傳入的目前設定為準計算。
/// 刻意把技能、神祇、道具的計算放在 C#：技能狀態的規則已經在 <see cref="SkillSetting"/>（同舊面板），網頁不重寫一份，兩邊才不會不一致。
/// 訊息不合法（缺欄位、職業空白、不認得的狀態或清單）一律回 null，不改設定。
/// 跟舊面板一樣，patch 帶的是「整個 key」（例如整份 skills），只換掉其中一個職業。
/// </summary>
public static class PanelActions
{
    private static readonly HashSet<string> SkillStates = ["", "prio", "focus", "after", "ban", "skip"];
    private static readonly HashSet<string> ItemLists = ["itemPrio", "itemFocus", "itemBan"];

    public static JsonObject? ToPatch(JsonObject message, JsonObject config)
        => Str(message["type"]) switch
        {
            "patch" => message["patch"] is JsonObject p ? (JsonObject)p.DeepClone() : null,
            "setSkill" => SetSkill(config, Job(message), Str(message["skill"]), Str(message["state"])),
            "deity" => Deity(config, Job(message), Str(message["name"]), Bool(message["on"])),
            "deityBan" => DeityBan(config, Job(message), Str(message["name"]), Bool(message["on"])),
            "deityClear" => Job(message) is { } job ? new JsonObject { ["deity"] = WithJob(config, "deity", job, []) } : null,
            "item" => Item(config, Str(message["list"]), Str(message["name"]), Bool(message["on"])),
            _ => null,
        };

    private static JsonObject? SetSkill(JsonObject config, string? job, string? skill, string? state)
    {
        if (job is null || string.IsNullOrEmpty(skill) || state is null || !SkillStates.Contains(state))
            return null;
        var setting = SkillSetting.From(config["skills"]?[job]);
        setting.SetState(skill, state);
        var all = config["skills"]?.DeepClone() as JsonObject ?? [];
        all[job] = setting.ToJson();
        return new JsonObject { ["skills"] = all };
    }

    private static JsonObject? Deity(JsonObject config, string? job, string? name, bool? on)
    {
        if (job is null || string.IsNullOrEmpty(name) || on is null)
            return null;
        var list = ConfigStore.ToStringList(config["deity"]?[job]).Where(v => v != name).ToList();
        if (on.Value)
            list.Add(name);
        return new JsonObject { ["deity"] = WithJob(config, "deity", job, list) };
    }

    /// <summary>✕＝強制不選：勾了同時從優先清單拿掉（同舊面板）</summary>
    private static JsonObject? DeityBan(JsonObject config, string? job, string? name, bool? on)
    {
        if (job is null || string.IsNullOrEmpty(name) || on is null)
            return null;
        var ban = ConfigStore.ToStringList(config["deityBan"]?[job]).Where(v => v != name).ToList();
        var patch = new JsonObject();
        if (on.Value)
        {
            ban.Add(name);
            patch["deity"] = WithJob(config, "deity", job, ConfigStore.ToStringList(config["deity"]?[job]).Where(v => v != name));
        }
        patch["deityBan"] = WithJob(config, "deityBan", job, ban);
        return patch;
    }

    private static JsonObject? Item(JsonObject config, string? listKey, string? name, bool? on)
    {
        if (listKey is null || !ItemLists.Contains(listKey) || string.IsNullOrEmpty(name) || on is null)
            return null;
        var list = ConfigStore.ToStringList(config[listKey]).Where(x => x != name).ToList();
        if (on.Value)
            list.Add(name);
        return new JsonObject { [listKey] = Strings(list) };
    }

    /// <summary>複製整個「職業 → 清單」物件，只換掉某個職業的清單</summary>
    private static JsonObject WithJob(JsonObject config, string key, string job, IEnumerable<string> list)
    {
        var all = config[key]?.DeepClone() as JsonObject ?? [];
        all[job] = Strings(list);
        return all;
    }

    private static string? Job(JsonObject message) => Str(message["job"]) is { Length: > 0 } job ? job : null;

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static bool? Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    private static JsonArray Strings(IEnumerable<string> list) => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
}
