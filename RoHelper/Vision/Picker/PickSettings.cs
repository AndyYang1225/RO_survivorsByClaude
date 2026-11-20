using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RoHelper.Storage;

namespace RoHelper.Vision.Picker;

/// <summary>
/// 一個職業的技能設定（同 JS 的 cfg.skills[職業]）：Order＝優先順序、After＝學到後放逐、Ban＝直接放逐、Focus＝優先＋專注、Skip＝不選。
/// </summary>
public sealed record SkillCfg(IReadOnlyList<string> Order, IReadOnlyList<string> After, IReadOnlyList<string> Ban, IReadOnlyList<string> Focus, IReadOnlyList<string> Skip)
{
    public static SkillCfg Empty { get; } = new([], [], [], [], []);
}

/// <summary>
/// 選卡用的設定快照（每次選卡從 cfg 讀一次）。欄位名稱對應 engine.js 的 cfg 鍵（DeityLock＝cfg.deityLock 這個勾選，不是這局鎖定的神祇）。
/// 刻意沒有尋寶率相關欄位（treasureCap、hpAfterCap）：視覺模式先不讀尋寶率（使用者決定），那兩條規則不生效。
/// 暫停不在這裡判斷：VisionEngine 暫停時根本不截圖。
/// </summary>
public sealed partial record PickSettings(
    string Job, bool SkillOn, SkillCfg Skills,
    bool BanExtra, IReadOnlyList<string> BanExtraWords,
    bool DeityPrio, string DeityOrder, bool DeityLock, IReadOnlyList<string> DeityList, IReadOnlyList<string> DeityBan,
    bool AutoFocus, int AutoFocusN, IReadOnlyList<string> ItemFocus, IReadOnlyList<string> ItemPrio, IReadOnlyList<string> ItemBan,
    bool RerollAll, bool AvoidNew, bool BanishKeep, int BanishKeepN, string PickOrder,
    bool PickSkill, bool PickChest, int PickDelay, bool DebugLog, bool AutoAfterBan = false)
{
    public static PickSettings From(ConfigStore c)
    {
        // 目前職業：面板選「自動」時用上次偵測到的職業（視覺模式不偵測職業，沿用設定裡的 curJob）
        var job = c.CurrentJob();
        // JS 一定偵測得到職業；視覺模式還不能偵測，沒有職業時技能規則不能生效，否則每張新技能都會被當成「清單外」而重抽／放逐
        var skillOn = job.Length > 0 && c.GetBool("skillOn");
        var skills = skillOn && JobNode(c, "skills", job) is JsonObject o
            ? new SkillCfg(List(o["order"]), List(o["after"]), List(o["ban"]), List(o["focus"]), List(o["skip"]))
            : SkillCfg.Empty;

        return new PickSettings(
            job, skillOn, skills,
            c.GetBool("banExtra"), Words(c.GetString("banExtraT")),
            c.GetBool("deityPrio"), c.GetString("deityOrder", "none"), c.GetBool("deityLock"),
            List(JobNode(c, "deity", job)), List(JobNode(c, "deityBan", job)),
            c.GetBool("autoFocus"), c.GetInt("autoFocusN", 1),
            c.GetStringList("itemFocus"), c.GetStringList("itemPrio"), c.GetStringList("itemBan"),
            c.GetBool("rerollAll"), c.GetBool("avoidNew"), c.GetBool("banishKeep"), c.GetInt("banishKeepN", 2),
            c.GetString("pickOrder", "treasure"),
            c.GetBool("pickSkill"), c.GetBool("pickChest"), c.GetInt("tPickN", 350), c.GetBool("debugLog"),
            c.GetBool("autoAfterBan"));
    }

    private static List<string> List(JsonNode? node) => ConfigStore.ToStringList(node);

    /// <summary>
    /// 取出設定裡某個鍵下、某職業的節點。鍵的值不是物件（例如匯入壞掉的設定把 skills 存成陣列）就當沒有。
    /// 匯入壞掉的設定不能讓選卡掛掉，同 JS 讀不到就當沒有（cfg.skills[職業] 是 undefined）。
    /// </summary>
    private static JsonNode? JobNode(ConfigStore c, string key, string job)
        => c.Current[key] is JsonObject o ? o[job] : null;

    /// <summary>同 JS 的 words()：用逗號、頓號、分號、空白切開</summary>
    private static List<string> Words(string text) => [.. WordSplit().Split(text).Where(w => w.Length > 0)];

    [GeneratedRegex(@"[,，、;；\s]+")]
    private static partial Regex WordSplit();
}
