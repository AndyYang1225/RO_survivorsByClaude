using System.Globalization;
using System.Text.Json.Nodes;

using RoHelper.Vision.Field;
using RoHelper.Vision.Picker;

namespace RoHelper.Vision;

/// <summary>
/// 一局的對局紀錄（給分析用），移植 JS 的 recStart／recPick／recSample／recFinish（engine.js 1177～1240），
/// 格式盡量相同（v: 1），結算時由 VisionEngine 寫進 runs.jsonl（同 JS 送給 exe 的 run 訊息，同一個檔案）。
///
/// 跟 JS 不同的地方（視覺模式讀不到、或讀法不同）：
/// - picks 的 gt（遊戲時間）是 OCR 計時器推算的「mm:ss」（<see cref="GameClock"/>），讀不到時空字串。
/// - last 只有 hp、action：JS 的 gt、phase（計時器顏色）、nextBoss 視覺模式讀不到。
/// - 刻意沒有 diff、loot、cards、serverLoot、got、deity：遠征難度、戰利品圖示（要認圖）、撿到幾個、神祇格都還沒讀。
/// - treasure、items 是最後一次讀道具欄的結果（<see cref="TreasureReader"/>，<see cref="SetTreasure"/>）；items 是 OCR 的說明框第一行，小字偶爾漏字。
/// - result／exp／dps／dpsTotal 來自 <see cref="ResultReader"/> 的 OCR（dps 每一筆只有 text，沒有 JS 的 key、name：那兩個是網頁屬性）。
/// - 只記小幫手自己的選卡動作：JS 另外記使用者自己點的卡（manual: true，靠網頁的 isTrusted 點擊事件），視覺模式分不出來。
/// - mode 是 "event"（活動）或 "expe"（自動遠征按的前往野外）。
/// 純邏輯：時間由呼叫端傳入，不截圖、不寫檔。
/// </summary>
public sealed class RunRecorder
{
    private JsonObject? _rec;
    private DateTime _start;
    private readonly List<JsonObject> _picks = [];
    private readonly List<JsonObject> _bells = [];
    private int _hpMin = 100;
    private double _treasure;
    private IReadOnlyList<string> _items = [];

    /// <summary>這一局正在記錄（Start 之後、Finish 之前）</summary>
    public bool Active => _rec is not null;

    /// <summary>新的一局（按了「前往野外」）：同 JS recStart，mode＝event（活動）或 expe（遠征）。上一局沒結算就開新局時，上一局直接丟掉（同 JS 覆蓋 st.rec）</summary>
    public void Start(DateTime now, string job, bool paused, string mode = "event")
    {
        _start = now;
        _picks.Clear();
        _bells.Clear();
        _hpMin = 100;
        _treasure = 0;
        _items = [];
        _rec = new JsonObject
        {
            ["v"] = 1,
            ["start"] = Iso(now),
            ["stage"] = "",
            ["job"] = job,
            ["mode"] = mode,
            ["autoAtStart"] = !paused,
            ["pausedAny"] = paused,
        };
    }

    /// <summary>進野外後才讀得到的地名、職業（<see cref="FieldInfoReader"/>）；空字串＝讀不到，不覆蓋</summary>
    public void SetInfo(string stage, string job)
    {
        if (_rec is null)
            return;
        if (stage.Length > 0)
            _rec["stage"] = stage;
        if (job.Length > 0)
            _rec["job"] = job;
    }

    /// <summary>讀到地名後更正這一局是活動還是遠征（開局時可能猜錯，見 VisionEngine.ReadInfoAsync）</summary>
    public void SetMode(string mode)
    {
        if (_rec is not null)
            _rec["mode"] = mode;
    }

    /// <summary>讀到道具欄：累計尋寶率（%）與每個道具說明框的第一行（同 JS st.treasure、st.items）</summary>
    public void SetTreasure(double treasure, IReadOnlyList<string> items)
    {
        _treasure = treasure;
        _items = items;
    }

    /// <summary>一次躲鐘的摘要（<see cref="Field.BellTrace"/>），結算時放在 bells</summary>
    public void Bell(JsonObject b)
    {
        if (_rec is not null)
            _bells.Add(b);
    }

    /// <summary>
    /// 一次選卡動作（同 JS recPick）：kind＝pick／banish／reroll／focus／gameAuto，有卡片時附卡片資訊；gt＝遊戲時間「mm:ss」；
    /// treasure＝這次選完後的尋寶率（含選尋寶卡的估算），查「超過上限還選尋寶卡」用
    /// </summary>
    public void Pick(PickDecision d, PickerInfo info, string gt = "", double? treasure = null)
    {
        if (_rec is null)
            return;
        var p = new JsonObject
        {
            ["gt"] = gt,
            ["kind"] = Kind(d.Do),
            ["chest"] = info.IsChest,
        };
        if (d.Index is { } i && i < info.Cards.Count)
        {
            var c = info.Cards[i];
            p["name"] = c.Name;
            p["rarity"] = c.SkillBadge ? "技能" : RarityName(c.Rarity);
            p["isNew"] = c.IsNew;
            p["deity"] = CardDecider.IsDeity(c);
        }
        p["why"] = d.Why;
        if (treasure is { } t)
            p["treasure"] = (int)Math.Round(t);
        _picks.Add(p);
    }

    /// <summary>
    /// 每秒一次（同 JS recSample）：暫停過就記 pausedAny；hp（0～1，null＝讀不到）更新最低血量與最後狀態。
    /// 暫停時 VisionEngine 不截圖，傳 hp null、paused true 只記 pausedAny。
    /// </summary>
    public void Sample(double? hp, string action, bool paused)
    {
        if (_rec is null)
            return;
        if (paused)
            _rec["pausedAny"] = true;
        if (hp is not { } h)
            return;
        var pct = (int)Math.Round(h * 100);
        _hpMin = Math.Min(_hpMin, pct);
        _rec["last"] = new JsonObject { ["hp"] = pct, ["action"] = action };
    }

    /// <summary>
    /// 結算（同 JS recFinish）：補上結束時間、結果、結算畫面的內容，回傳整筆紀錄並結束這一局；沒有正在記錄的局回 null。
    /// result 是 null（結算畫面讀不到）時 outcome 是 "unknown"、result 各欄是 null，其他照記。
    /// </summary>
    public JsonObject? Finish(RunResult? r, DateTime now)
    {
        if (_rec is not { } run)
            return null;
        _rec = null;
        run["picks"] = new JsonArray(_picks.Select(p => (JsonNode?)p.DeepClone()).ToArray());
        run["bells"] = new JsonArray(_bells.Select(b => (JsonNode?)b.DeepClone()).ToArray());
        run["hpMin"] = _hpMin;
        run["end"] = Iso(now);
        run["wallSecs"] = (int)Math.Round((now - _start).TotalSeconds);
        run["outcome"] = r?.Outcome ?? "unknown";
        run["title"] = r?.Title ?? "";
        run["result"] = new JsonObject
        {
            ["job"] = r?.Job ?? "",
            ["time"] = r?.Time ?? "",
            ["secs"] = r?.Secs,
            ["level"] = r?.Level,
            ["kills"] = r?.Kills,
            ["bosses"] = r?.Bosses,
        };
        run["exp"] = r?.Exp ?? "";
        run["dps"] = new JsonArray((r?.Dps ?? []).Select(t => (JsonNode?)new JsonObject { ["text"] = t }).ToArray());
        run["dpsTotal"] = r?.DpsTotal ?? "";
        run["treasure"] = (int)Math.Round(_treasure);
        run["items"] = new JsonArray(_items.Select(i => (JsonNode?)i).ToArray());
        run["counts"] = new JsonObject
        {
            ["pick"] = _picks.Count(p => (string?)p["kind"] == "pick"),
            ["banish"] = _picks.Count(p => (string?)p["kind"] == "banish"),
            ["reroll"] = _picks.Count(p => (string?)p["kind"] == "reroll"),
            ["focus"] = _picks.Count(p => (string?)p["kind"] == "focus"),
        };
        return run;
    }

    /// <summary>
    /// 面板「戰績」的一筆（同 JS cfg.stats 的格式 { t, stage, secs, loot, balls, chests }）。
    /// secs 優先用結算畫面的遊戲時間（JS 用的是牆上時間：從開局到結算，含載入和選卡暫停，比較不準）；讀不到才用牆上時間。
    /// loot、balls、chests 視覺模式還沒算，一律 0。result＝勝／敗／？（<see cref="ResultText"/>）。
    /// </summary>
    public static JsonObject Stat(DateTime now, string stage, int? gameSecs, int wallSecs, string? outcome = null) => new()
    {
        ["result"] = ResultText(outcome),
        ["t"] = now.ToString("yyyy/M/d tt h:mm:ss", CultureInfo.GetCultureInfo("zh-TW")),
        ["stage"] = stage.Length > 0 ? stage : "?",
        ["secs"] = gameSecs ?? wallSecs,
        ["loot"] = 0,
        ["balls"] = 0,
        ["chests"] = 0,
    };

    /// <summary>結算標題的結果（ResultReader.OutcomeOf）換成戰績顯示的字：存活＝勝、死亡＝敗、讀不到＝？（使用者 2026-11-25）</summary>
    public static string ResultText(string? outcome) => outcome switch
    {
        "clear" => "勝",
        "death" => "敗",
        _ => "？",
    };

    /// <summary>cfg.stats 加一筆，只留最後 50 筆（同 JS .slice(-50)）</summary>
    public static JsonArray AppendStat(JsonNode? stats, JsonObject stat)
    {
        var list = (stats as JsonArray ?? []).Select(x => x?.DeepClone()).Append(stat).ToList();
        return new JsonArray(list.Skip(Math.Max(0, list.Count - 50)).ToArray());
    }

    private static string Kind(PickDo d) => d switch
    {
        PickDo.Pick => "pick",
        PickDo.Banish => "banish",
        PickDo.Reroll => "reroll",
        PickDo.Focus => "focus",
        _ => "gameAuto",
    };

    internal static string RarityName(int r) => r is >= 0 and <= 5 ? new[] { "普通", "優良", "稀有", "史詩", "傳說", "神話" }[r] : "";

    /// <summary>同 JS Date.toISOString（UTC、毫秒、Z 結尾）</summary>
    private static string Iso(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
