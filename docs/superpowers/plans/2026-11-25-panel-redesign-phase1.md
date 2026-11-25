# 控制面板改版 第 1 階段 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 面板改用 WebView2 + HTML/CSS 畫，左側導覽、遊戲風外觀，所有設定功能跟舊面板完全相同；總覽頁先只放現有的狀態資訊（儀表板是第 2 階段）。

**Architecture:** `PanelForm` 保留為外框（F9、置頂、記住位置不變），新版模式下把內容換成一個 WebView2，跟遊戲共用同一個 `CoreWebView2Environment`。介面檔案打包進 exe，由 `WebResourceRequested` 在 `https://panel.rohelper/` 回應。C# 和網頁用 WebMessage 互傳 JSON：C# 送 schema／config／status／window／saved，網頁送 ready／patch／各種指令；改設定的邏輯（技能狀態、神祇、道具清單）放在 C# 的純邏輯類別 `PanelActions`，網頁只負責畫面。舊的 WinForms 面板保留，遊戲視窗「選項」可以切回（重新啟動後生效）。

**Tech Stack:** .NET 8 WinForms、Microsoft.Web.WebView2 1.0.2903.40、原生 HTML/CSS/JavaScript（不用任何框架或 CDN）、xUnit。

**設計文件：** `docs/superpowers/specs/2026-11-25-panel-redesign-design.md`

## Global Constraints

- 程式碼識別字英文；註解、XML doc、介面文字一律繁體中文（遵守 coding-style skill）；中英文之間留半形空格。
- 介面不載入任何外部網址、CDN、網路字型；字型用 `"Microsoft JhengHei UI", "Microsoft JhengHei", sans-serif`。
- 發佈仍是單一 exe：介面檔案用 EmbeddedResource（LogicalName `RoHelper.Panel.<檔名>`）。
- 面板的 WebView2 必須用遊戲已經建好的同一個 `CoreWebView2Environment`（同一個使用者資料夾、同樣的瀏覽器參數），不可自己建立。
- 資料流向維持單向：網頁的操作 → `IAutomation.SendPatch` → 引擎改設定 → `ConfigStore.Changed` → 送 `config` 給網頁 → 網頁照新設定重畫。網頁不自己保存設定。
- 遊戲風配色（取自已確認的草圖）：標題列 `linear-gradient(#5c7fc4, #3d5fa8)`、背景 `#e9edf5`、卡片 `#fff` 框線 `#c7d2e8`、選中 `linear-gradient(#6f8fd0, #4a6db4)`、執行中綠燈 `#7dffb2`、暫停黃燈 `#ffd36b`。
- 程式在執行時會鎖住 bin，跑測試加 `--artifacts-path`：`dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test"`。
- Commit 訊息沿用 repo 風格（`feat 中文說明`、`fix …`、`modify …`），結尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`。這台電腦的 git 沒設定作者，commit 時用 `git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit …`。
- 不動 `RoHelper/Forms/PanelForm.Lists.cs` 裡使用者自己還沒 commit 的修改（`QueueListRebuild`），也不要把它加進 commit。

## 跟設計文件不同的地方（實作時發現，已在這份計畫裡處理）

1. **目前分頁不存進設定的 `tab`，改存在網頁的 localStorage**：每次 `SendPatch` 都會呼叫 `LobbyFlow.Reset()`，切個分頁就重設大廳流程，而且辨識在背景時改設定會讓那一輪結果被丟掉。localStorage 存在 WebView2 的使用者資料夾裡，本來就是每個 profile 各一份。
2. **紀錄筆數放在 `status` 訊息裡**（`logCount`、`runsRecorded`），不另開訊息類型。
3. **不做 `openFolder` 指令**：遊戲視窗選單已經有「開啟資料夾」。
4. **改技能、神祇、道具不送整份 patch，改送指令**（`setSkill`、`deity`、`deityBan`、`deityClear`、`item`），由 C# 的 `PanelActions` 沿用既有的 `SkillSetting` 算出 patch，避免在 JavaScript 重寫一份規則。

## 檔案結構

| 檔案 | 動作 | 職責 |
|---|---|---|
| `RoHelper/Panel/PanelSchema.cs` | 新增 | `EngineSchema` → 給網頁的 JSON（規則、職業技能、神祇、道具、技能狀態選項） |
| `RoHelper/Panel/PanelActions.cs` | 新增 | 網頁送來的改設定訊息 → 引擎的 patch（純邏輯） |
| `RoHelper/Panel/PanelAssets.cs` | 新增 | 依網址取出打包進 exe 的介面檔案 |
| `RoHelper/Panel/web/index.html` | 新增 | 介面骨架 |
| `RoHelper/Panel/web/panel.css` | 新增 | 遊戲風樣式 |
| `RoHelper/Panel/web/panel.js` | 新增 | 畫面、導覽、規則頁、與 C# 溝通 |
| `RoHelper/Forms/PanelForm.Web.cs` | 新增 | 新版模式：建 WebView2、回應檔案請求、收發訊息 |
| `RoHelper/Forms/PanelForm.cs` | 修改 | 新版模式時 `OnSchema`、`ShowStatus`、`ApplyConfig` 改走網頁 |
| `RoHelper/Forms/GameForm.cs` | 修改 | 建好環境後交給面板；「選項」加新版面板開關 |
| `RoHelper/Storage/AppSettings.cs` | 修改 | 新增 `WebPanel`（預設 true） |
| `RoHelper/RoHelper.csproj` | 修改 | 打包 `Panel\web\*` |
| `RoHelper.Tests/PanelSchemaTests.cs` | 新增 | |
| `RoHelper.Tests/PanelActionsTests.cs` | 新增 | |
| `RoHelper.Tests/PanelAssetsTests.cs` | 新增 | |
| `tools/winshot.ps1` | 新增 | 截 RoHelper 所有可見視窗（驗收用） |
| `README.md`、`RoHelper/README.md` | 修改 | 新版面板說明 |

---

### Task 1: PanelSchema（規則清單轉成網頁用的 JSON）

**Files:**
- Create: `RoHelper/Panel/PanelSchema.cs`
- Test: `RoHelper.Tests/PanelSchemaTests.cs`

**Interfaces:**
- Consumes: `RoHelper.Bridge.EngineSchema`、`RuleDef`、`Choice`（既有）、`RoHelper.Engine.BuiltInSchema.Load()`（既有）
- Produces: `public static JsonObject RoHelper.Panel.PanelSchema.ToJson(EngineSchema schema)`，輸出格式：
  ```json
  {
    "tabs": ["升級", ...],
    "rules": [ { "tab": "升級", "group": "選卡", "label": "", "sub": 0, "noCheck": false, "min": 0, "max": 9999 },
               { "tab": "升級", "key": "preMvp", "label": "MVP 出現前 N 秒…", "num": 15, "min": 5, "max": 60, "sub": 0, "noCheck": false },
               { ..., "options": [["treasure","尋寶率優先"], ...] }, { ..., "stageOptions": true, "options": [] },
               { ..., "text": true }, { ..., "excl": "autoExpe" } ],
    "jobs": [ { "job": "賢者", "skills": ["（一轉技能…）", "（賢者技能…）"] } ],
    "deityAttr": ["貪婪之神", ...],
    "items": [ { "category": "藥水", "names": [...] } ],
    "skillStates": [["", "—"], ["prio", "優先"], ...]
  }
  ```
  沒有值的欄位（group、key、num、options、stageOptions、text、excl）不輸出。

- [ ] **Step 1: 寫失敗的測試**

建立 `RoHelper.Tests/PanelSchemaTests.cs`：

```csharp
using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelSchemaTests
{
    private static readonly EngineSchema Schema = BuiltInSchema.Load();
    private static readonly JsonObject Json = PanelSchema.ToJson(Schema);

    private static IEnumerable<JsonObject> Rules => Json["rules"]!.AsArray().OfType<JsonObject>();

    private static JsonObject Rule(string key) => Rules.First(r => (string?)r["key"] == key);

    [Fact]
    public void NumberRule_KeepsTabRangeAndDefault()
    {
        var r = Rule("preMvp");

        Assert.Equal("升級", (string?)r["tab"]);
        Assert.Equal(15m, r["num"]!.GetValue<decimal>());
        Assert.Equal(5m, r["min"]!.GetValue<decimal>());
        Assert.Equal(60m, r["max"]!.GetValue<decimal>());
        Assert.False(r["noCheck"]!.GetValue<bool>());
    }

    [Fact]
    public void TimeRule_IsNoCheck()
        => Assert.True(Rule("tTick")["noCheck"]!.GetValue<bool>());

    [Fact]
    public void GroupRule_HasGroupAndTab_NoKey()
    {
        var g = Rules.First(r => r["group"] is not null);

        Assert.Null(g["key"]);
        Assert.False(string.IsNullOrEmpty((string?)g["tab"]));
    }

    [Fact]
    public void StageRule_FlagsStageOptions()
    {
        var r = Rule("expeStage");

        Assert.True(r["stageOptions"]!.GetValue<bool>());
        Assert.Equal(2, r["sub"]!.GetValue<int>());
    }

    [Fact]
    public void OptionsRule_KeepsValueTextPairs()
    {
        var opts = Rule("pickOrder")["options"]!.AsArray();

        Assert.Equal("treasure", (string?)opts[0]![0]);
        Assert.Equal("尋寶率優先", (string?)opts[0]![1]);
    }

    [Fact]
    public void TextRule_FlagsText()
        => Assert.True(Rule("banExtra")["text"]!.GetValue<bool>());

    [Fact]
    public void ExclusiveRule_KeepsExcl()
        => Assert.Equal("autoExpe", (string?)Rule("autoEvent")["excl"]);

    [Fact]
    public void Jobs_IncludeBaseJobSkills()
    {
        var job = Schema.JobBase.Keys.First();
        var skills = Json["jobs"]!.AsArray().OfType<JsonObject>()
            .First(j => (string?)j["job"] == job)["skills"]!.AsArray().Select(x => (string?)x).ToList();

        Assert.Equal(Schema.SkillsOf(job), skills);
    }

    [Fact]
    public void ItemsAndStates_AreCopied()
    {
        Assert.Equal(Schema.Items.Count, Json["items"]!.AsArray().Count);
        Assert.Equal("prio", (string?)Json["skillStates"]!.AsArray()[1]![0]);
        Assert.Equal(Schema.DeityAttr.Count, Json["deityAttr"]!.AsArray().Count);
    }
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelSchemaTests"`
Expected: 編譯失敗，`找不到類型或命名空間名稱 'Panel'`（或 `PanelSchema`）。

- [ ] **Step 3: 實作**

建立 `RoHelper/Panel/PanelSchema.cs`：

```csharp
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
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelSchemaTests"`
Expected: `已通過! - 失敗: 0，通過: 9`

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Panel/PanelSchema.cs RoHelper.Tests/PanelSchemaTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 新版面板：規則清單轉成網頁用的 JSON" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: PanelActions（網頁的改設定訊息 → patch）

**Files:**
- Create: `RoHelper/Panel/PanelActions.cs`
- Test: `RoHelper.Tests/PanelActionsTests.cs`

**Interfaces:**
- Consumes: `RoHelper.Forms.SkillSetting`（既有，在 `PanelForm.Lists.cs`：`From(JsonNode?)`、`SetState(string, string)`、`ToJson()`）、`ConfigStore.ToStringList(JsonNode?)`（既有）
- Produces: `public static JsonObject? RoHelper.Panel.PanelActions.ToPatch(JsonObject message, JsonObject config)`。處理的訊息（`type` 欄位）：
  - `{ "type": "patch", "patch": { ... } }` → 原樣（深拷貝）
  - `{ "type": "setSkill", "job", "skill", "state" }` → `{ "skills": { ...所有職業..., [job]: 新設定 } }`
  - `{ "type": "deity", "job", "name", "on" }` → `{ "deity": { ..., [job]: [...] } }`
  - `{ "type": "deityBan", "job", "name", "on" }` → `{ "deityBan": {...} }`，on＝true 時另外帶 `"deity"`（把該神祇從優先拿掉）
  - `{ "type": "deityClear", "job" }` → `{ "deity": { ..., [job]: [] } }`
  - `{ "type": "item", "list": "itemPrio"|"itemFocus"|"itemBan", "name", "on" }` → `{ [list]: [...] }`
  - 其他 `type`、缺欄位、job 是空字串、state 不是已知值、list 不是那三個 → `null`

- [ ] **Step 1: 寫失敗的測試**

建立 `RoHelper.Tests/PanelActionsTests.cs`：

```csharp
using System.Text.Json.Nodes;

using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelActionsTests
{
    private static JsonObject Msg(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static List<string> List(JsonNode? node) => node!.AsArray().Select(x => (string)x!).ToList();

    [Fact]
    public void Patch_IsCopied()
    {
        var msg = Msg("""{"type":"patch","patch":{"tTickN":100,"pickSkill":true}}""");

        var p = PanelActions.ToPatch(msg, [])!;

        Assert.Equal(100, p["tTickN"]!.GetValue<int>());
        Assert.True(p["pickSkill"]!.GetValue<bool>());
        p["tTickN"] = 1;
        Assert.Equal(100, msg["patch"]!["tTickN"]!.GetValue<int>());
    }

    [Fact]
    public void SetSkill_ToFocus_KeepsRank_AndOtherJobs()
    {
        var cfg = Msg("""{"skills":{"賢者":{"order":["火箭術","冰箭術"]},"騎士":{"order":["怒爆"]}}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"冰箭術","state":"focus"}"""), cfg)!;

        Assert.Equal(["火箭術", "冰箭術"], List(p["skills"]!["賢者"]!["order"]));
        Assert.Equal(["冰箭術"], List(p["skills"]!["賢者"]!["focus"]));
        Assert.Equal(["怒爆"], List(p["skills"]!["騎士"]!["order"]));
    }

    [Fact]
    public void SetSkill_ToBan_RemovesFromOrder()
    {
        var cfg = Msg("""{"skills":{"賢者":{"order":["火箭術"]}}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"火箭術","state":"ban"}"""), cfg)!;

        Assert.Empty(List(p["skills"]!["賢者"]!["order"]));
        Assert.Equal(["火箭術"], List(p["skills"]!["賢者"]!["ban"]));
    }

    [Fact]
    public void SetSkill_UnknownState_IsNull()
        => Assert.Null(PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"火箭術","state":"hack"}"""), []));

    [Fact]
    public void Deity_OnAppends_OffRemoves()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神"],"騎士":["貪婪之神"]}}""");

        var on = PanelActions.ToPatch(Msg("""{"type":"deity","job":"賢者","name":"處刑之神","on":true}"""), cfg)!;
        var off = PanelActions.ToPatch(Msg("""{"type":"deity","job":"賢者","name":"戰爭之神","on":false}"""), cfg)!;

        Assert.Equal(["戰爭之神", "處刑之神"], List(on["deity"]!["賢者"]));
        Assert.Equal(["貪婪之神"], List(on["deity"]!["騎士"]));
        Assert.Empty(List(off["deity"]!["賢者"]));
    }

    [Fact]
    public void DeityBan_On_AlsoRemovesFromPriority()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神","處刑之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityBan","job":"賢者","name":"戰爭之神","on":true}"""), cfg)!;

        Assert.Equal(["戰爭之神"], List(p["deityBan"]!["賢者"]));
        Assert.Equal(["處刑之神"], List(p["deity"]!["賢者"]));
    }

    [Fact]
    public void DeityBan_Off_OnlyTouchesBanList()
    {
        var cfg = Msg("""{"deityBan":{"賢者":["戰爭之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityBan","job":"賢者","name":"戰爭之神","on":false}"""), cfg)!;

        Assert.Empty(List(p["deityBan"]!["賢者"]));
        Assert.Null(p["deity"]);
    }

    [Fact]
    public void DeityClear_EmptiesThatJob()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神"],"騎士":["貪婪之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityClear","job":"賢者"}"""), cfg)!;

        Assert.Empty(List(p["deity"]!["賢者"]));
        Assert.Equal(["貪婪之神"], List(p["deity"]!["騎士"]));
    }

    [Fact]
    public void Item_ToggleOnOff()
    {
        var cfg = Msg("""{"itemPrio":["紅藥水"]}""");

        var on = PanelActions.ToPatch(Msg("""{"type":"item","list":"itemPrio","name":"白藥水","on":true}"""), cfg)!;
        var off = PanelActions.ToPatch(Msg("""{"type":"item","list":"itemPrio","name":"紅藥水","on":false}"""), cfg)!;

        Assert.Equal(["紅藥水", "白藥水"], List(on["itemPrio"]));
        Assert.Empty(List(off["itemPrio"]));
    }

    [Theory]
    [InlineData("""{"type":"item","list":"skills","name":"x","on":true}""")]
    [InlineData("""{"type":"deity","job":"","name":"x","on":true}""")]
    [InlineData("""{"type":"setSkill","skill":"x","state":"prio"}""")]
    [InlineData("""{"type":"export"}""")]
    [InlineData("""{"type":"patch","patch":5}""")]
    public void Invalid_IsNull(string json)
        => Assert.Null(PanelActions.ToPatch(Msg(json), []));
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelActionsTests"`
Expected: 編譯失敗，`'PanelActions' 不存在`。

- [ ] **Step 3: 實作**

建立 `RoHelper/Panel/PanelActions.cs`：

```csharp
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
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelActionsTests"`
Expected: `已通過! - 失敗: 0，通過: 14`

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Panel/PanelActions.cs RoHelper.Tests/PanelActionsTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 新版面板：技能、神祇、道具的修改由 C# 算出 patch" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: PanelAssets（介面檔案打包進 exe）

**Files:**
- Create: `RoHelper/Panel/PanelAssets.cs`
- Create: `RoHelper/Panel/web/index.html`
- Create: `RoHelper/Panel/web/panel.css`（這個 task 先放一行註解，Task 5 補完整內容）
- Create: `RoHelper/Panel/web/panel.js`（同上）
- Modify: `RoHelper/RoHelper.csproj`（EmbeddedResource 區塊）
- Test: `RoHelper.Tests/PanelAssetsTests.cs`

**Interfaces:**
- Produces:
  - `public const string RoHelper.Panel.PanelAssets.Origin = "https://panel.rohelper/"`
  - `public static bool PanelAssets.TryGet(string uri, out byte[] data, out string contentType)`：網址在 Origin 底下、檔名是單層（不含 `/`、`\`、`..`）、副檔名是 .html／.css／.js、而且有打包，才回 true。網址只有 Origin（或帶 `?`、`#`）時視為 `index.html`。

- [ ] **Step 1: 寫失敗的測試**

建立 `RoHelper.Tests/PanelAssetsTests.cs`：

```csharp
using System.Text;

using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelAssetsTests
{
    [Theory]
    [InlineData("https://panel.rohelper/index.html", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/index.html?v=1", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/panel.css", "text/css; charset=utf-8")]
    [InlineData("https://panel.rohelper/panel.js", "text/javascript; charset=utf-8")]
    public void KnownFiles_AreServed(string uri, string type)
    {
        Assert.True(PanelAssets.TryGet(uri, out var data, out var contentType));
        Assert.Equal(type, contentType);
        Assert.NotEmpty(data);
    }

    [Fact]
    public void Index_LinksStyleAndScript()
    {
        PanelAssets.TryGet(PanelAssets.Origin + "index.html", out var data, out _);
        var html = Encoding.UTF8.GetString(data);

        Assert.Contains("panel.css", html);
        Assert.Contains("panel.js", html);
    }

    [Theory]
    [InlineData("https://panel.rohelper/missing.html")]
    [InlineData("https://panel.rohelper/../RoHelper.schema.json")]
    [InlineData("https://panel.rohelper/sub/index.html")]
    [InlineData("https://panel.rohelper/schema.json")]
    [InlineData("https://ro-survivors.com/index.html")]
    public void OtherRequests_AreRejected(string uri)
        => Assert.False(PanelAssets.TryGet(uri, out _, out _));
}
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelAssetsTests"`
Expected: 編譯失敗，`'PanelAssets' 不存在`。

- [ ] **Step 3: 建立介面骨架與暫時的樣式、程式檔**

建立 `RoHelper/Panel/web/index.html`：

```html
<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <title>RO 小幫手 面板</title>
  <link rel="stylesheet" href="panel.css">
</head>
<body>
  <header id="bar">
    <span id="dot" class="dot"></span>
    <span id="title" class="title">等待遊戲載入…</span>
    <button id="pin" class="btn" title="面板置頂">📌</button>
    <button id="pause" class="btn">⏸ 暫停 F8</button>
  </header>
  <div id="body">
    <nav id="nav"></nav>
    <main id="main"></main>
  </div>
  <div id="toast" class="toast">已儲存 ✓</div>
  <script src="panel.js"></script>
</body>
</html>
```

建立 `RoHelper/Panel/web/panel.css`，內容一行：

```css
/* 遊戲風樣式（Task 5 補上） */
```

建立 `RoHelper/Panel/web/panel.js`，內容一行：

```js
// 新版面板（Task 5 補上）
```

- [ ] **Step 4: 打包進 exe**

修改 `RoHelper/RoHelper.csproj`，在既有的 `<EmbeddedResource Include="Templates\*.png;Templates\*.json" ... />` 下一行加上：

```xml
    <!-- 新版面板的介面檔案：由 PanelAssets 依網址取出（WebView2 的 WebResourceRequested），發佈仍是單一 exe -->
    <EmbeddedResource Include="Panel\web\*.html;Panel\web\*.css;Panel\web\*.js" LogicalName="RoHelper.Panel.%(Filename)%(Extension)" />
```

- [ ] **Step 5: 實作 PanelAssets**

建立 `RoHelper/Panel/PanelAssets.cs`：

```csharp
namespace RoHelper.Panel;

/// <summary>
/// 新版面板的介面檔案（index.html、panel.css、panel.js），打包在 exe 裡（csproj 的 EmbeddedResource，LogicalName 是 RoHelper.Panel.檔名）。
/// 面板的 WebView2 開 <see cref="Origin"/>，所有請求由 WebResourceRequested 交給 <see cref="TryGet"/>，不碰檔案系統、不連網路。
/// 只接受單層檔名和三種副檔名：網頁只能拿到面板自己的檔案，拿不到 exe 裡其他資源（schema.json、範本圖片）。
/// </summary>
public static class PanelAssets
{
    public const string Origin = "https://panel.rohelper/";

    private const string ResourcePrefix = "RoHelper.Panel.";

    public static bool TryGet(string uri, out byte[] data, out string contentType)
    {
        data = [];
        contentType = "";
        if (!uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
            return false;
        var name = uri[Origin.Length..];
        var cut = name.IndexOfAny(['?', '#']);
        if (cut >= 0)
            name = name[..cut];
        if (name.Length == 0)
            name = "index.html";
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            return false;

        var type = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            _ => "",
        };
        if (type.Length == 0)
            return false;

        using var stream = typeof(PanelAssets).Assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
            return false;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        data = buffer.ToArray();
        contentType = type;
        return true;
    }
}
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test" --filter "FullyQualifiedName~PanelAssetsTests"`
Expected: `已通過! - 失敗: 0，通過: 11`

- [ ] **Step 7: Commit**

```bash
git add RoHelper/Panel/PanelAssets.cs RoHelper/Panel/web RoHelper/RoHelper.csproj RoHelper.Tests/PanelAssetsTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 新版面板：介面檔案打包進 exe" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 面板視窗改用 WebView2（新版模式、訊息管道、開關）

**Files:**
- Create: `RoHelper/Forms/PanelForm.Web.cs`
- Modify: `RoHelper/Forms/PanelForm.cs`（`OnSchema`、`ShowStatus`、`ApplyConfig`）
- Modify: `RoHelper/Forms/GameForm.cs`（`OnLoad`、建構子的「選項」選單）
- Modify: `RoHelper/Storage/AppSettings.cs`
- Create: `tools/winshot.ps1`

**Interfaces:**
- Consumes: `PanelSchema.ToJson`（Task 1）、`PanelActions.ToPatch`（Task 2）、`PanelAssets.Origin`／`TryGet`（Task 3）；`PanelForm` 既有的 `TogglePause()`、`ExportConfig()`、`ImportConfig()`、`PushToProfiles()`、`CopyText(string, string)`、`Stats()`、`FormatStat(JsonObject)`（在 `PanelForm.Other.cs`）
- Produces:
  - `public Task PanelForm.UseWebPanelAsync(CoreWebView2Environment env)`：呼叫當下（第一個 await 之前）就進入新版模式。
  - `AppSettings.WebPanel`（bool，預設 true）
  - C# → 網頁的訊息（`PostWebMessageAsJson`）：
    - `{ "type": "schema", "schema": <PanelSchema.ToJson> }`
    - `{ "type": "config", "config": <整份設定> }`
    - `{ "type": "status", "status": { "runs", "runLimit", "treasure", "next", "action", "paused", "logCount", "runsRecorded" } }`
    - `{ "type": "window", "topMost": bool }`
    - `{ "type": "saved" }`
  - 網頁 → C# 的訊息：`ready`、`pause`、`hide`、`topMost {on}`、`export`、`import`、`applyOthers`、`copyLog`、`clearLog`、`copyStats`，其餘交給 `PanelActions.ToPatch`。

這個 task 沒有自動測試（WinForms 加 WebView2 的接線），用實際啟動驗收。

- [ ] **Step 1: AppSettings 加開關**

修改 `RoHelper/Storage/AppSettings.cs`，在 `public bool StallRecovery { get; set; } = true;` 的下一行（保留一個空行）加上：

```csharp

    /// <summary>
    /// 用新版面板（WebView2 + HTML）。關掉＝用舊的 WinForms 面板（改版第 3 階段刪除舊面板前的退路）。
    /// 切換要重新啟動才生效：面板的內容在啟動時就決定了
    /// </summary>
    public bool WebPanel { get; set; } = true;
```

- [ ] **Step 2: 新增 PanelForm.Web.cs**

建立 `RoHelper/Forms/PanelForm.Web.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Panel;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace RoHelper.Forms;

/// <summary>
/// 新版面板：視窗內容換成 WebView2，畫面由 Panel/web 的 HTML／CSS／JS 負責，這裡只收發訊息（格式見 docs/superpowers/plans/2026-11-25-panel-redesign-phase1.md）。
/// WebView2 環境由遊戲視窗交過來（同一個使用者資料夾的第二個 WebView2 必須用同一個環境，選項不同會建立失敗），不自己建。
/// 網頁送 ready 之前（還在載入、或面板一直隱藏所以還沒初始化）不送任何訊息；收到 ready 再一次補送 schema、設定、狀態。
/// 改設定的訊息交給 <see cref="PanelActions"/> 算出 patch，再走跟舊面板一樣的 <see cref="IAutomation.SendPatch"/>。
/// </summary>
public sealed partial class PanelForm
{
    private WebView2? _web;
    private bool _webReady;

    /// <summary>引擎最近一次的狀態（<see cref="ShowStatus"/> 收到的）；網頁 ready 時補送</summary>
    private JsonObject? _lastStatus;

    /// <summary>新版模式：<see cref="UseWebPanelAsync"/> 之後為 true，OnSchema、ShowStatus、ApplyConfig 改走網頁，不建 WinForms 控制項</summary>
    private bool WebMode => _web is not null;

    /// <summary>
    /// 切換成新版面板。第一個 await 之前就進入新版模式，所以要在引擎 Start（送出規則清單）之前呼叫。
    /// 呼叫端不要 await：面板隱藏時 WebView2 要等視窗第一次顯示才初始化，await 會卡住遊戲視窗的啟動流程。
    /// 初始化失敗時在面板上顯示原因和退路（遊戲視窗「選項」關掉新版面板），不讓例外逃出去。
    /// </summary>
    public async Task UseWebPanelAsync(CoreWebView2Environment env)
    {
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0xE9, 0xED, 0xF5) };
        SuspendLayout();
        foreach (var old in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(old);
            old.Dispose();
        }
        Controls.Add(_web);
        ResumeLayout();
        _config.Changed += PostConfig;
        _log.Changed += PostStatus;
        _runs.Changed += PostStatus;

        try
        {
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.AddWebResourceRequestedFilter(PanelAssets.Origin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                e.Response = PanelAssets.TryGet(e.Request.Uri, out var data, out var type)
                    ? env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", "Content-Type: " + type)
                    : env.CreateWebResourceResponse(null, 404, "Not Found", "");
            };
            core.WebMessageReceived += OnWebMessage;
            core.Navigate(PanelAssets.Origin + "index.html");
        }
        catch (Exception ex)
        {
            Controls.Remove(_web);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"新版面板載入失敗 : {ex.Message}\n可以在遊戲視窗「選項」關掉「新版面板」後重新啟動，改用舊面板。",
            });
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // 只收面板自己的網頁送來的訊息
        if (!e.Source.StartsWith(PanelAssets.Origin, StringComparison.OrdinalIgnoreCase))
            return;
        JsonObject? msg;
        try
        {
            msg = JsonNode.Parse(e.WebMessageAsJson) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (msg is null)
            return;

        switch (EngineSchema.Str(msg["type"]))
        {
            case "ready":
                _webReady = true;
                PostWindow();
                PostSchema();
                PostConfig();
                PostStatus();
                return;
            case "pause":
                TogglePause();
                return;
            case "hide":
                Hide();
                return;
            case "topMost":
                TopMost = msg["on"] is JsonValue v && v.TryGetValue(out bool on) && on;
                PostWindow();
                return;
            case "export":
                ExportConfig();
                return;
            case "import":
                ImportConfig();
                return;
            case "applyOthers":
                PushToProfiles();
                return;
            case "copyLog":
                CopyText(_log.ToText(), "紀錄");
                return;
            case "clearLog":
                _log.Clear();
                return;
            case "copyStats":
                CopyText(string.Join("\n", Stats().Select(FormatStat)), "戰績");
                return;
        }

        if (PanelActions.ToPatch(msg, _config.Current) is { } patch)
        {
            _automation.SendPatch(patch);
            Post(new JsonObject { ["type"] = "saved" });
        }
    }

    private void Post(JsonObject msg)
    {
        if (_webReady && _web?.CoreWebView2 is { } core)
            core.PostWebMessageAsJson(msg.ToJsonString());
    }

    private void PostSchema()
    {
        if (_schema is { } schema)
            Post(new JsonObject { ["type"] = "schema", ["schema"] = PanelSchema.ToJson(schema) });
    }

    private void PostConfig() => Post(new JsonObject { ["type"] = "config", ["config"] = _config.Current.DeepClone() });

    private void PostStatus()
    {
        var status = _lastStatus?.DeepClone() as JsonObject ?? [];
        status["logCount"] = _log.Count;
        status["runsRecorded"] = _runs.Count;
        Post(new JsonObject { ["type"] = "status", ["status"] = status });
    }

    private void PostWindow() => Post(new JsonObject { ["type"] = "window", ["topMost"] = TopMost });
}
```

- [ ] **Step 3: PanelForm.cs 在新版模式下改走網頁**

修改 `RoHelper/Forms/PanelForm.cs`。

`ShowStatus` 方法開頭（`var runs = ...` 之前）加上：

```csharp
        if (WebMode)
        {
            _lastStatus = s;
            PostStatus();
            return;
        }
```

`OnSchema` 方法裡，把：

```csharp
        _schema = schema;

        SuspendLayout();
```

改成：

```csharp
        _schema = schema;
        // 新版面板：規則清單交給網頁畫，不建 WinForms 控制項
        if (WebMode)
        {
            PostSchema();
            return;
        }

        SuspendLayout();
```

`ApplyConfig` 方法開頭的：

```csharp
        if (_schema is null)
            return;
```

改成：

```csharp
        // 新版面板的設定由 PostConfig 送給網頁
        if (_schema is null || WebMode)
            return;
```

- [ ] **Step 4: GameForm 把環境交給面板、加開關**

修改 `RoHelper/Forms/GameForm.cs`。

建構子裡，在 `options.DropDownItems.Add(Toggle("卡住時自動重新整理", ...));` 的下一行加上：

```csharp
        options.DropDownItems.Add(Toggle("新版面板（重新啟動後生效）", settings.WebPanel, on => _settings.WebPanel = on));
```

`OnLoad` 裡，把：

```csharp
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, options);
            await _webView.EnsureCoreWebView2Async(env);
```

改成：

```csharp
            var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDir, options);
            await _webView.EnsureCoreWebView2Async(env);
            // 新版面板跟遊戲共用同一個環境；要在引擎 Start（送規則清單）之前切換。刻意不 await（見 UseWebPanelAsync）
            if (_settings.WebPanel)
                _ = _panel.UseWebPanelAsync(env);
```

- [ ] **Step 5: 新增截圖工具**

建立 `tools/winshot.ps1`：

```powershell
# 截 RoHelper 所有可見視窗（含面板）存成 PNG，給驗收、除錯用。
# 用 PrintWindow（PW_RENDERFULLCONTENT）：視窗被擋住也截得到，WebView2 的內容也會畫出來。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\winshot.ps1 -OutDir <資料夾>
param([Parameter(Mandatory = $true)][string]$OutDir)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class WinShot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
"@

New-Item -ItemType Directory -Force $OutDir | Out-Null
$ids = @(Get-Process RoHelper -ErrorAction Stop | ForEach-Object { $_.Id })
$handles = New-Object System.Collections.Generic.List[IntPtr]
[WinShot]::EnumWindows({ param($h, $l)
    $p = 0
    [void][WinShot]::GetWindowThreadProcessId($h, [ref]$p)
    if ($ids -contains $p -and [WinShot]::IsWindowVisible($h)) { $handles.Add($h) }
    $true }, [IntPtr]::Zero) | Out-Null

$i = 0
foreach ($h in $handles) {
    $r = New-Object WinShot+Rect
    [void][WinShot]::GetWindowRect($h, [ref]$r)
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -lt 50 -or $hh -lt 50) { continue }
    $title = New-Object System.Text.StringBuilder 256
    [void][WinShot]::GetWindowText($h, $title, 256)
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [void][WinShot]::PrintWindow($h, $dc, 2)
    $g.ReleaseHdc($dc); $g.Dispose()
    $file = Join-Path $OutDir "win$i.png"
    $bmp.Save($file); $bmp.Dispose()
    "$file  ${w}x$hh  $($title.ToString())"
    $i++
}
```

- [ ] **Step 6: 建置確認沒有錯誤**

Run: `dotnet build RoHelper/RoHelper.csproj -c Release`
Expected: `0 個錯誤`；`PanelForm.Web.cs`、`PanelForm.cs`、`GameForm.cs` 沒有新的警告。

- [ ] **Step 7: 實際啟動，確認管道通了**

Run（背景執行）：`dotnet run -c Release --project RoHelper -- --dev-api`

確認：
1. 面板視窗是藍色標題列、淺藍灰背景（panel.js 還是空的，所以只有骨架，標題寫「等待遊戲載入…」）。如果面板是隱藏的，在遊戲視窗按 F9 叫出來。
2. 遊戲照常運作：`curl -s "http://127.0.0.1:8765/status?n=3"` 的 `history` 有在前進。
3. 截圖：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\winshot.ps1 -OutDir "%TEMP%\rohelper-shots"`，看面板那張。
4. 遊戲視窗「選項」裡有「新版面板（重新啟動後生效）」且是勾選的。關掉、重開程式 → 面板變回舊的 WinForms 面板，功能正常；再勾回去、重開 → 新版骨架。

- [ ] **Step 8: Commit**

```bash
git add RoHelper/Forms/PanelForm.Web.cs RoHelper/Forms/PanelForm.cs RoHelper/Forms/GameForm.cs RoHelper/Storage/AppSettings.cs tools/winshot.ps1
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 面板可切換成新版（WebView2），跟遊戲共用環境；選項可切回舊面板" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

注意：`git add RoHelper/Forms/PanelForm.cs` 只加這個檔案；`PanelForm.Lists.cs` 不要加。

---

### Task 5: 介面本體（遊戲風樣式、導覽、所有設定頁）

**Files:**
- Modify: `RoHelper/Panel/web/panel.css`（整份取代）
- Modify: `RoHelper/Panel/web/panel.js`（整份取代）

**Interfaces:**
- Consumes: Task 4 的訊息格式；`PanelSchema` 的 JSON 格式（Task 1）；`PanelActions` 認得的訊息（Task 2）
- Produces: 完整的新版面板畫面

這個 task 沒有自動測試（專案沒有 JavaScript 測試環境），Step 3 用實際操作驗收。

- [ ] **Step 1: 寫樣式**

`RoHelper/Panel/web/panel.css` 整份換成：

```css
/* 新版面板：遊戲風（藍色漸層標題列、淺色卡片，仿遊戲結算視窗）。配色見 docs/superpowers/mockups/2026-11-25-panel-style-options.html */
:root {
  --bg: #e9edf5;
  --card: #fff;
  --line: #c7d2e8;
  --soft: #f2f5fb;
  --text: #23304a;
  --muted: #6b7894;
  --accent: #3d5fa8;
  --accent-light: #6f8fd0;
}

* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; }
body {
  font-family: "Microsoft JhengHei UI", "Microsoft JhengHei", sans-serif;
  font-size: 14px;
  color: var(--text);
  background: var(--bg);
  display: flex;
  flex-direction: column;
  user-select: none;
}

/* 頂端列 */
#bar { display: flex; align-items: center; gap: 8px; padding: 8px 12px; background: linear-gradient(#5c7fc4, #3d5fa8); color: #fff; flex: none; }
.dot { width: 10px; height: 10px; border-radius: 50%; background: #aab4c8; flex: none; }
.dot.running { background: #7dffb2; box-shadow: 0 0 6px #7dffb2; }
.dot.paused { background: #ffd36b; box-shadow: 0 0 6px #ffd36b; }
.title { flex: 1; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.btn { font: inherit; font-size: 13px; border: 1px solid #a9bde6; background: #fff; color: #2f4f94; border-radius: 6px; padding: 3px 10px; cursor: pointer; }
.btn:hover { background: #eef3ff; }
#pause.on { background: #ffd36b; border-color: #e0b040; color: #5a4100; }
#pin { opacity: .55; }
#pin.on { opacity: 1; background: #dbe7ff; }

/* 左側導覽＋內容 */
#body { flex: 1; display: flex; min-height: 0; }
#nav { width: 72px; flex: none; background: #f7f8fc; border-right: 1px solid var(--line); padding: 8px 6px; display: flex; flex-direction: column; gap: 4px; overflow-y: auto; }
.nav-item { font: inherit; font-size: 12px; border: 0; background: transparent; color: #4a5a7a; border-radius: 8px; padding: 8px 0; display: flex; flex-direction: column; align-items: center; gap: 2px; cursor: pointer; }
.nav-item:hover { background: #e6ecf8; }
.nav-item.on { background: linear-gradient(#6f8fd0, #4a6db4); color: #fff; font-weight: 700; }
.nav-icon { font-size: 18px; }
#main { flex: 1; overflow-y: auto; padding: 10px; }
.empty { color: var(--muted); text-align: center; margin-top: 40%; }

/* 卡片 */
.card { background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 10px 12px; margin-bottom: 10px; }
.card h3 { margin: 0 0 8px; font-size: 13px; color: var(--accent); }
.card h4 { margin: 10px 0 4px; font-size: 12px; color: var(--muted); }

/* 一列設定 */
.row { display: flex; align-items: center; flex-wrap: wrap; gap: 6px; padding: 4px 0; }
label.row { cursor: pointer; }
.label { line-height: 1.5; }
.note { color: var(--muted); font-size: 12px; margin: 0 0 4px; line-height: 1.5; }
.note.inline { margin: 0; }

/* 開關（勾選框畫成滑動開關） */
input.switch { appearance: none; width: 34px; height: 20px; border-radius: 10px; background: #c9d2e3; position: relative; flex: none; cursor: pointer; transition: background .15s; margin: 0 4px 0 0; }
input.switch::after { content: ''; position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: #fff; transition: left .15s; box-shadow: 0 1px 2px rgba(0, 0, 0, .2); }
input.switch:checked { background: var(--accent-light); }
input.switch:checked::after { left: 16px; }

input.num { width: 76px; font: inherit; padding: 2px 4px; border: 1px solid var(--line); border-radius: 6px; }
input.text, select { font: inherit; padding: 3px 6px; border: 1px solid var(--line); border-radius: 6px; background: #fff; }
input.text { width: calc(100% - 40px); margin-bottom: 6px; }

/* 選卡頁的小分頁 */
.subtabs { display: flex; gap: 4px; margin-bottom: 10px; }
.subtab { flex: 1; font: inherit; font-size: 13px; border: 1px solid var(--line); background: #fff; color: #4a5a7a; border-radius: 8px; padding: 6px 0; cursor: pointer; }
.subtab.on { background: linear-gradient(#6f8fd0, #4a6db4); color: #fff; border-color: #4a6db4; font-weight: 700; }

/* 技能、神祇、道具 */
.skill .label { min-width: 170px; }
.skill.ranked .label, .cell.ranked > label:first-child { font-weight: 700; }
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 4px 8px; }
.cell { display: flex; align-items: center; gap: 6px; }
.cell label { display: flex; align-items: center; gap: 3px; cursor: pointer; }
.cell.banned > label:first-child { text-decoration: line-through; color: #9aa3b8; }
.x { color: #c0392b; }
.star { color: #b8860b; }

/* 按鈕列、連結、清單 */
.btns { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin: 6px 0; }
button.link { font: inherit; font-size: 12px; border: 0; background: none; color: var(--accent); text-decoration: underline; cursor: pointer; padding: 0; }
.stats { margin: 0; padding-left: 18px; font-size: 12px; line-height: 1.7; }

/* 總覽（第 1 階段先放現有的狀態） */
.now { font-size: 16px; font-weight: 700; }
.kv { display: flex; justify-content: space-between; background: var(--soft); border: 1px solid #e0e6f2; border-radius: 6px; padding: 5px 8px; margin-top: 6px; }
.next { margin-top: 6px; color: #b86a00; }

/* 已儲存提示 */
.toast { position: fixed; right: 14px; bottom: 14px; background: #2f8f5b; color: #fff; padding: 6px 12px; border-radius: 8px; opacity: 0; transform: translateY(8px); transition: all .2s; pointer-events: none; }
.toast.show { opacity: 1; transform: none; }
```

- [ ] **Step 2: 寫介面程式**

`RoHelper/Panel/web/panel.js` 整份換成：

```js
'use strict';
// 新版控制面板。C# 送來 schema／config／status／window／saved（見 PanelForm.Web.cs），這裡只負責畫面。
// 改設定一律送回 C#（patch 或指令），畫面永遠照 C# 回傳的 config 重畫，不自己保存設定（同舊面板的單向資料流）。
// 目前頁面存在 localStorage，不存進設定：每次改設定引擎都會重設大廳流程，切個分頁不該影響自動化。

const $ = id => document.getElementById(id);
const $main = $('main');
const $nav = $('nav');

const state = {
  schema: null,
  cfg: {},
  status: {},
  topMost: false,
  page: load('page', 'overview'),
  sub: load('sub', 'rules'),
};

// 左側導覽。tab＝這一頁顯示 schema 裡哪個分頁的規則
const PAGES = [
  { id: 'overview', icon: '📊', name: '總覽' },
  { id: 'pick', icon: '🃏', name: '選卡' },
  { id: 'walk', icon: '🚶', name: '走位', tab: '走位' },
  { id: 'start', icon: '🗺', name: '出發', tab: '出發' },
  { id: 'records', icon: '📜', name: '紀錄' },
  { id: 'other', icon: '⚙', name: '其他' },
];
// 選卡頁的小分頁（原本的升級、技能、神祇、道具四個分頁）
const PICK_SUBS = [
  { id: 'rules', name: '規則', tab: '升級' },
  { id: 'skills', name: '技能', tab: '技能' },
  { id: 'deity', name: '神祇', tab: '神祇' },
  { id: 'items', name: '道具', tab: '道具' },
];
// 上面已經安排位置的 schema 分頁；以後新增的分頁自動補在導覽最後
const KNOWN_TABS = new Set(['升級', '技能', '神祇', '道具', '走位', '出發', '其他']);
// 「其他」分頁裡改放到「紀錄」頁的規則
const RECORD_KEYS = new Set(['debugLog']);
// 規則說明裡的「 N 」或「 % 」是數字欄位的位置（同舊面板的 NumSlot）
const SLOT = / (N|%) /;

// ── 共用小工具 ──

function load(key, fallback) {
  try { return localStorage.getItem('panel.' + key) || fallback; } catch { return fallback; }
}

function save(key, value) {
  try { localStorage.setItem('panel.' + key, value); } catch { /* 存不了就算了，下次回到總覽 */ }
}

function send(msg) { window.chrome.webview.postMessage(msg); }

function patch(p) { send({ type: 'patch', patch: p }); }

/** 建立元素：props 的 on* 是事件，false／null／undefined 的屬性略過 */
function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'class') el.className = v;
    else if (k === 'style') el.style.cssText = v;
    else if (k in el) el[k] = v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const c of kids.flat()) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : String(c));
  }
  return el;
}

const bool = k => state.cfg[k] === true;
const str = (k, d = '') => (typeof state.cfg[k] === 'string' ? state.cfg[k] : d);
const num = (k, d = 0) => (typeof state.cfg[k] === 'number' ? state.cfg[k] : d);
const list = k => (Array.isArray(state.cfg[k]) ? state.cfg[k].filter(x => typeof x === 'string') : []);
const jobList = (k, job) => (Array.isArray(state.cfg[k]?.[job]) ? state.cfg[k][job] : []);

/** 目前生效的職業（同 ConfigStore.CurrentJob）：選「自動」時用偵測到的 */
function currentJob() {
  const sel = str('jobSel', 'auto');
  return sel === 'auto' ? str('curJob') : sel;
}

function skillsOf(job) {
  return state.schema.jobs.find(j => j.job === job)?.skills || [];
}

function card(title, ...kids) {
  return h('section', { class: 'card' }, title ? h('h3', {}, title) : null, ...kids);
}

function btn(text, onclick) {
  return h('button', { class: 'btn', onclick }, text);
}

function note(text) {
  return h('div', { class: 'note' }, text);
}

// ── 頂端列 ──

function renderHeader() {
  const paused = bool('paused');
  const limit = bool('runLimit') ? num('runLimitN') : 0;
  $('dot').className = 'dot ' + (!state.schema ? '' : paused ? 'paused' : 'running');
  $('title').textContent = !state.schema
    ? '等待遊戲載入…'
    : `${paused ? '已暫停' : '運行中'} · 第 ${num('runs')}${limit ? ' / ' + limit : ''} 局`;
  $('pause').textContent = paused ? '▶ 繼續 F8' : '⏸ 暫停 F8';
  $('pause').classList.toggle('on', paused);
  $('pin').classList.toggle('on', state.topMost);
}

$('pause').addEventListener('click', () => send({ type: 'pause' }));
$('pin').addEventListener('click', () => send({ type: 'topMost', on: !state.topMost }));

// WebView2 有焦點時 WinForms 收不到按鍵，F8／F9 由這裡轉給 C#
document.addEventListener('keydown', e => {
  if (e.key === 'F8') {
    e.preventDefault();
    send({ type: 'pause' });
  } else if (e.key === 'F9') {
    e.preventDefault();
    send({ type: 'hide' });
  }
});

// ── 導覽 ──

function pages() {
  const extra = (state.schema?.tabs || [])
    .filter(t => !KNOWN_TABS.has(t))
    .map(t => ({ id: 'tab:' + t, icon: '📄', name: t, tab: t }));
  return [...PAGES, ...extra];
}

function currentPage() {
  return pages().find(p => p.id === state.page) || PAGES[0];
}

function renderNav() {
  const cur = currentPage().id;
  $nav.replaceChildren(...pages().map(p =>
    h('button', { class: 'nav-item' + (p.id === cur ? ' on' : ''), onclick: () => go(p.id) },
      h('span', { class: 'nav-icon' }, p.icon), h('span', {}, p.name))));
}

function go(id) {
  state.page = id;
  save('page', id);
  renderNav();
  render();
  $main.scrollTop = 0;
}

// ── 重畫 ──

// 正在輸入文字或數字時不重畫（會把打到一半的內容蓋掉），離開欄位後再補畫
let renderPending = false;

function typing() {
  const a = document.activeElement;
  return a && $main.contains(a) && a.tagName === 'INPUT' && (a.type === 'text' || a.type === 'number');
}

function renderSoon() {
  if (typing()) {
    renderPending = true;
    return;
  }
  render();
}

$main.addEventListener('focusout', () => setTimeout(() => {
  if (renderPending && !typing()) {
    renderPending = false;
    render();
  }
}, 0));

function render() {
  const top = $main.scrollTop;
  $main.replaceChildren(...pageContent());
  $main.scrollTop = top;
}

function pageContent() {
  if (!state.schema) return [h('div', { class: 'empty' }, '等待遊戲頁面載入…')];
  const p = currentPage();
  switch (p.id) {
    case 'overview': return overviewPage();
    case 'pick': return pickPage();
    case 'records': return recordsPage();
    case 'other': return otherPage();
    default: return rulesOf(p.tab);
  }
}

// ── 規則（依 schema 產生，同舊面板 AddRule） ──

/** 某個 schema 分頁的規則，依分組標題切成卡片；keep 可以濾掉某些規則 */
function rulesOf(tab, keep = () => true) {
  const cards = [];
  let cur = null;
  for (const r of state.schema.rules.filter(x => x.tab === tab)) {
    if (r.group) {
      cur = card(r.group);
      cards.push(cur);
      continue;
    }
    if (!r.key || !keep(r)) continue;
    if (!cur) {
      cur = card(null);
      cards.push(cur);
    }
    cur.append(...ruleRow(r));
  }
  // 規則都被濾掉的卡片不顯示
  return cards.filter(c => [...c.children].some(e => e.tagName !== 'H3'));
}

/** 說明裡的括號內容移到下面當灰色小字（括號裡有數字欄位的不拆） */
function splitNote(label) {
  const m = label.match(/^(.*?)（([^（）]*)）(.*)$/);
  if (!m || SLOT.test(m[2])) return { title: label, note: '' };
  return { title: (m[1] + m[3]).trim(), note: m[2] };
}

function ruleRow(r) {
  const indent = (r.sub || 0) * 18;

  // 下拉選單（遠征地圖的選項是遊戲裡看到的地圖清單，目前選的不在清單裡也要留著，同舊面板）
  if (r.options) {
    let opts = r.options;
    if (r.stageOptions) {
      const stages = list('expeList');
      const cur = str(r.key);
      if (cur && !stages.includes(cur)) stages.push(cur);
      opts = stages.map(n => [n, n]);
    }
    const sel = h('select', { onchange: e => patch({ [r.key]: e.target.value }) },
      opts.map(([v, t]) => h('option', { value: v }, t)));
    sel.value = str(r.key);
    return [h('div', { class: 'row', style: `margin-left:${indent}px` }, h('span', { class: 'label' }, r.label), sel)];
  }

  const { title, note: extra } = splitNote(r.label);
  const row = h('label', { class: 'row', style: `margin-left:${indent}px` });
  // 開關（時間設定這類 noCheck 的沒有）；互斥：開這條就把另一條關掉
  if (!r.noCheck) {
    row.append(h('input', {
      type: 'checkbox',
      class: 'switch',
      checked: bool(r.key),
      onchange: e => {
        const p = { [r.key]: e.target.checked };
        if (e.target.checked && r.excl) p[r.excl] = false;
        patch(p);
      },
    }));
  }
  // 數字欄位放在說明文字裡「 N 」的位置
  const m = r.num != null ? title.match(SLOT) : null;
  if (m) {
    const before = title.slice(0, m.index);
    const after = (m[1] === '%' ? '% ' : '') + title.slice(m.index + m[0].length);
    const input = h('input', {
      type: 'number',
      class: 'num',
      min: r.min,
      max: r.max,
      value: Math.min(r.max, Math.max(r.min, num(r.key + 'N', r.min))),
      onchange: e => {
        const v = Math.round(Number(e.target.value));
        if (Number.isFinite(v)) patch({ [r.key + 'N']: Math.min(r.max, Math.max(r.min, v)) });
      },
    });
    row.append(h('span', { class: 'label' }, before), input, h('span', { class: 'label' }, after));
  } else {
    row.append(h('span', { class: 'label' }, title));
  }

  const out = [row];
  const sub = indent + (r.noCheck ? 0 : 42);
  if (extra) out.push(h('div', { class: 'note', style: `margin-left:${sub}px` }, extra));
  if (r.text) {
    out.push(h('input', {
      type: 'text',
      class: 'text',
      value: str(r.key + 'T'),
      style: `margin-left:${sub}px`,
      onchange: e => patch({ [r.key + 'T']: e.target.value.trim() }),
    }));
  }
  return out;
}

// ── 總覽（第 1 階段：先放舊面板上方的三行資訊，儀表板是第 2 階段） ──

function overviewPage() {
  const s = state.status;
  const action = String(s.action || '').replace('［視覺］', '');
  return [
    card('現在在做什麼', h('div', { class: 'now' }, action || '—')),
    card('本局',
      h('div', { class: 'kv' }, h('span', {}, '尋寶率'), h('b', {}, `+${s.treasure ?? 0}%`)),
      s.next ? h('div', { class: 'next' }, s.next) : null),
  ];
}

// ── 選卡 ──

function pickPage() {
  const tabs = h('div', { class: 'subtabs' }, PICK_SUBS.map(s =>
    h('button', {
      class: 'subtab' + (s.id === state.sub ? ' on' : ''),
      onclick: () => {
        state.sub = s.id;
        save('sub', s.id);
        render();
        $main.scrollTop = 0;
      },
    }, s.name)));
  const sub = PICK_SUBS.find(s => s.id === state.sub) || PICK_SUBS[0];
  const body = rulesOf(sub.tab);
  if (sub.id === 'skills') body.push(skillsCard());
  if (sub.id === 'deity') body.push(deityCard());
  if (sub.id === 'items') body.push(...itemCards());
  return [tabs, ...body];
}

function jobRow() {
  const sel = h('select', { onchange: e => patch({ jobSel: e.target.value }) },
    h('option', { value: 'auto' }, '自動（目前角色）'),
    state.schema.jobs.map(j => h('option', { value: j.job }, j.job)));
  sel.value = str('jobSel', 'auto');
  const cur = str('curJob');
  const hint = str('jobSel', 'auto') === 'auto' ? (cur ? '→ ' + cur : '（尚未偵測）') : '';
  return h('div', { class: 'row' }, h('span', { class: 'label' }, '職業：'), sel, h('span', { class: 'note inline' }, hint));
}

function skillsCard() {
  const job = currentJob();
  const c = card('技能', jobRow(), note('優先的順序＝設定的先後（數字＝順位）。第 1 優先出現就選，其餘看稀有度。'));
  if (!job) {
    c.append(note('回選角畫面點一下角色，或手動選職業'));
    return c;
  }
  const s = state.cfg.skills?.[job] || {};
  const arr = k => (Array.isArray(s[k]) ? s[k] : []);
  // 只用來顯示目前狀態（同 SkillSetting.StateOf）；改狀態的計算在 C#（PanelActions）
  const stateOf = n => arr('after').includes(n) ? 'after'
    : arr('focus').includes(n) && arr('order').includes(n) ? 'focus'
    : arr('order').includes(n) ? 'prio'
    : arr('ban').includes(n) ? 'ban'
    : arr('skip').includes(n) ? 'skip'
    : '';
  for (const n of skillsOf(job)) {
    const rank = arr('order').indexOf(n);
    const sel = h('select', { onchange: e => send({ type: 'setSkill', job, skill: n, state: e.target.value }) },
      state.schema.skillStates.map(([v, t]) => h('option', { value: v }, t)));
    sel.value = stateOf(n);
    const name = (rank >= 0 ? `${rank + 1}. ` : '') + n + (rank >= 0 && arr('focus').includes(n) ? ' ★' : '');
    c.append(h('div', { class: 'row skill' + (rank >= 0 ? ' ranked' : '') }, h('span', { class: 'label' }, name), sel));
  }
  return c;
}

function deityCard() {
  const job = currentJob();
  const c = card('神祇', jobRow(), note('依勾選順序優先（數字＝順位）；紅色 ✕＝強制不選'));
  if (!job) {
    c.append(note('回選角畫面點一下角色，或手動選職業'));
    return c;
  }
  const prio = jobList('deity', job);
  const ban = jobList('deityBan', job);
  const grid = names => h('div', { class: 'grid' }, names.map(n => {
    const rank = prio.indexOf(n);
    const banned = ban.includes(n);
    return h('div', { class: 'cell' + (banned ? ' banned' : rank >= 0 ? ' ranked' : '') },
      h('label', {},
        h('input', { type: 'checkbox', checked: rank >= 0, disabled: banned, onchange: e => send({ type: 'deity', job, name: n, on: e.target.checked }) }),
        (rank >= 0 ? `${rank + 1}. ` : '') + n),
      h('label', { class: 'x', title: '強制不選' },
        h('input', { type: 'checkbox', checked: banned, onchange: e => send({ type: 'deityBan', job, name: n, on: e.target.checked }) }),
        '✕'));
  }));
  c.append(
    h('h4', {}, '技能神祇'), grid(skillsOf(job)),
    h('h4', {}, '屬性神祇'), grid(state.schema.deityAttr),
    h('div', { class: 'btns' }, h('button', { class: 'link', onclick: () => send({ type: 'deityClear', job }) }, '清除這個職業')));
  return c;
}

function itemCards() {
  const lists = { itemPrio: list('itemPrio'), itemFocus: list('itemFocus'), itemBan: list('itemBan') };
  const toggle = (name, key, text, title, cls) => h('label', { class: cls, title },
    h('input', { type: 'checkbox', checked: lists[key].includes(name), onchange: e => send({ type: 'item', list: key, name, on: e.target.checked }) }),
    text);
  const cards = [card(null, note('左邊勾選＝優先選（同時出現選最稀有的）；★＝出現時先按「專注」；紅色 ✕＝強制不選。尋寶率滿了以後，尋寶道具一律跳過。'))];
  for (const g of state.schema.items) {
    cards.push(card(g.category, h('div', { class: 'grid' }, g.names.map(n => h('div', { class: 'cell' },
      toggle(n, 'itemPrio', n),
      toggle(n, 'itemFocus', '★', '出現時先按「專注」', 'star'),
      toggle(n, 'itemBan', '✕', '強制不選', 'x'))))));
  }
  return cards;
}

// ── 紀錄 ──

function recordsPage() {
  const s = state.status;
  const stats = (Array.isArray(state.cfg.stats) ? state.cfg.stats : []).filter(x => x && typeof x === 'object');
  // 同舊面板 FormatStat
  const fmt = x => {
    const secs = Number.isInteger(x.secs) ? x.secs : 0;
    return `${x.t ?? ''}｜${x.stage ?? ''}｜${Math.floor(secs / 60)}分${secs % 60}秒｜王掉落 ${x.loot ?? ''}｜扭蛋 ${x.balls ?? ''}｜寶箱 ${x.chests ?? ''}`;
  };
  const last10 = stats.slice(-10).reverse();
  const limit = bool('runLimit') ? ` / ${num('runLimitN')}` : '';
  const debugRule = state.schema.rules.find(r => r.key === 'debugLog');
  return [
    card('戰績（最近 10 局）',
      last10.length ? h('ul', { class: 'stats' }, last10.map(x => h('li', {}, fmt(x)))) : note('（還沒有）'),
      h('div', { class: 'btns' },
        btn('複製全部戰績', () => send({ type: 'copyStats' })),
        btn('清除戰績', () => patch({ stats: [] })))),
    card('局數',
      h('div', { class: 'row' },
        h('span', { class: 'label' }, `已完成 ${num('runs')}${limit} 局`),
        h('button', { class: 'link', onclick: () => patch({ runs: 0 }) }, '歸零'))),
    card('選卡紀錄',
      ...(debugRule ? ruleRow(debugRule) : []),
      h('div', { class: 'btns' },
        btn('複製紀錄', () => send({ type: 'copyLog' })),
        btn('清除紀錄', () => send({ type: 'clearLog' })),
        h('span', { class: 'note inline' }, `${s.logCount ?? 0} 筆`))),
    card('完整對局紀錄（分析用）',
      h('div', {}, `已記錄 ${s.runsRecorded ?? 0} 局`),
      note('每局自動記錄（暫停小幫手自己玩也會記）：地圖、難度、職業、結果、等級、擊殺、每次選卡、各技能傷害、獲得物品。存在資料夾裡的 runs.jsonl。')),
  ];
}

// ── 其他 ──

function otherPage() {
  return [
    ...rulesOf('其他', r => !RECORD_KEYS.has(r.key)),
    card('設定備份',
      h('div', { class: 'btns' },
        btn('匯出設定（複製）', () => send({ type: 'export' })),
        btn('匯入設定', () => send({ type: 'import' }))),
      note('Tampermonkey 版面板「匯出設定」複製出來的內容，也可以直接貼進「匯入設定」。'),
      h('div', { class: 'btns' }, btn('套用到其他分身…', () => send({ type: 'applyOthers' }))),
      note('把這個視窗的選卡、走位等規則複製給其他分身（正在執行的幾秒內生效）。不會動到對方的局數、戰績、活動／遠征選擇和局數上限。')),
  ];
}

// ── 已儲存提示 ──

let toastTimer = 0;

function toast() {
  $('toast').classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $('toast').classList.remove('show'), 1200);
}

// ── 收 C# 的訊息 ──

window.chrome.webview.addEventListener('message', e => {
  const m = e.data || {};
  switch (m.type) {
    case 'schema':
      state.schema = m.schema;
      renderHeader();
      renderNav();
      render();
      break;
    case 'config':
      state.cfg = m.config || {};
      renderHeader();
      renderSoon();
      break;
    case 'status':
      state.status = m.status || {};
      // 狀態一秒變好幾次，只有會顯示狀態的頁面才重畫
      if (state.page === 'overview' || state.page === 'records') renderSoon();
      break;
    case 'window':
      state.topMost = !!m.topMost;
      renderHeader();
      break;
    case 'saved':
      toast();
      break;
  }
});

renderHeader();
renderNav();
render();
send({ type: 'ready' });
```

- [ ] **Step 3: 實際操作驗收**

先關掉正在執行的 RoHelper，再背景執行：`dotnet run -c Release --project RoHelper -- --dev-api`；面板隱藏的話在遊戲視窗按 F9。每一項做完用 `tools\winshot.ps1` 截圖確認畫面，並打開 `%AppData%\RoHelper\config.json` 確認值真的改了：

1. 頂端列：綠燈「運行中 · 第 N 局」；按「⏸ 暫停 F8」→ 黃燈「已暫停」，按鈕變「▶ 繼續 F8」，config 的 `paused` 變 true；再按一次恢復。在面板裡按 F8 同樣有效，按 F9 面板隱藏。
2. 📌：按了變亮、面板置頂；再按取消。重開程式後置頂狀態跟關掉前一樣。
3. 總覽：顯示目前動作和尋寶率，會跟著遊戲變化。
4. 選卡 → 規則：開關一個規則 → 右下角「已儲存 ✓」、config 改了；改 MVP 前 N 秒的數字 → config 的 `preMvpN` 改了；超出範圍（例如輸入 999）會被夾到上限 60。
5. 選卡 → 技能：職業下拉選「自動」時顯示「→ 目前職業」；把一個技能改成「優先＋專注」→ 名稱前出現順位、後面出現 ★、粗體；改回「—」→ 順位消失。config 的 `skills.<職業>` 跟舊面板做同樣操作的結果相同。
6. 選卡 → 神祇：勾一個 → 出現順位；按 ✕ → 劃掉、不能勾、從優先清單移除；「清除這個職業」→ 全部取消。
7. 選卡 → 道具：勾優先、★、✕ 各一個 → config 的 `itemPrio`、`itemFocus`、`itemBan` 對應改變。
8. 走位、出發：每一種控制項各改一個（開關、數字、下拉）；遠征「地圖」下拉的選項是遊戲裡看到的地圖；「自動參加活動」和「自動遠征」互斥（開一個另一個自動關）。
9. 有文字框的規則（選卡 → 技能 的「其他一出現就放逐（普攻強化等）：」，key 是 `banExtra`）：在文字框打字時畫面不會跳掉、打的字不會被蓋掉，離開欄位後 config 的 `banExtraT` 改了。
10. 紀錄：戰績最近 10 局跟舊面板一樣；複製全部戰績、複製紀錄會跳「已複製」；選卡紀錄開關有效；「歸零」把局數歸零（測完再用匯入或手動改回來，或先記下原本的局數）。
11. 其他：時間設定一組，數字可以改；匯出設定 → 剪貼簿有整份 JSON；匯入設定 → 跳出貼上視窗（按取消不改任何東西）；套用到其他分身 → 跳出選分身視窗（按取消）。
12. 切換頁面、小分頁後重開程式，回到同一頁。
13. 改了設定，遊戲行為照新設定走（例如暫停真的停、`tTickN` 改了主迴圈間隔跟著變）。

任何一項跟舊面板行為不同，修好再往下。

- [ ] **Step 4: Commit**

```bash
git add RoHelper/Panel/web/panel.css RoHelper/Panel/web/panel.js
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 新版面板介面：遊戲風、左側導覽、所有設定頁" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 整體驗收、記憶體實測、文件

**Files:**
- Modify: `README.md`
- Modify: `RoHelper/README.md`

- [ ] **Step 1: 全部測試**

Run: `dotnet test RoHelper.Tests --artifacts-path "%TEMP%\rohelper-test"`
Expected: 除了既有、會因 CPU 忙碌而偶爾超時的 `BellFinderTests.Scan_IsCheap` 之外全部通過。`Scan_IsCheap` 失敗時單獨再跑一次（`--filter Scan_IsCheap`）要通過。

- [ ] **Step 2: 多開驗收**

Run（背景）：`dotnet run -c Release --project RoHelper -- --profile=B`
確認 B 的面板標題列顯示 `[B]`（視窗標題）、設定是 B 自己的（改 B 的一個設定，主視窗的 config.json 不變）。關掉 B。

- [ ] **Step 3: 記憶體實測**

程式在跑、面板顯示中，量一次：

```powershell
$ids = (Get-Process RoHelper).Id
Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*RoHelper*" } | ForEach-Object { (Get-Process -Id $_.ProcessId).WorkingSet64 } | Measure-Object -Sum | ForEach-Object { "WebView2 合計 {0:N0} MB" -f ($_.Sum / 1MB) }
"RoHelper {0:N0} MB" -f ((Get-Process RoHelper).WorkingSet64 / 1MB)
```

到遊戲視窗「選項」關掉新版面板、重開，再量一次。兩次的差就是新版面板的記憶體成本，記在 commit 訊息和回報裡。量完把新版面板開回來、重開。

- [ ] **Step 4: 更新文件**

`RoHelper/README.md`：把

```
- 「學到後放逐」和「一出現就放逐」的卡都放逐過後，會按遊戲的「自動」，這局之後交給遊戲選（面板「升級」分頁可關）。
```

開頭那句的「（面板「升級」分頁可關）」改成「（面板「選卡 → 規則」可關）」（這一行後面的文字不變）。

在 `RoHelper/README.md` 的 `- **F9**：顯示／隱藏面板（面板按 X 也只是隱藏）` 下一行加上：

```
- 面板（2026-11-25 改版）：左側導覽「總覽／選卡／走位／出發／紀錄／其他」，選卡底下再分「規則／技能／神祇／道具」。改了設定右下角會出現「已儲存 ✓」。舊版面板可在遊戲視窗「選項 → 新版面板」關掉後重新啟動切回（第 3 階段會移除）。
```

`README.md`：把 `- **F9**：顯示／隱藏控制面板（規則設定都在面板裡）` 改成：

```
- **F9**：顯示／隱藏控制面板（規則設定都在面板裡；左側導覽：總覽、選卡、走位、出發、紀錄、其他）
```

- [ ] **Step 5: Commit**

```bash
git add README.md RoHelper/README.md
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "modify 更新 README 的新版面板說明" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## 之後的階段（各自另寫計畫）

- **第 2 階段：總覽儀表板**。新增狀態快照組裝器與三個純邏輯元件（步驟判斷、今天累計、警示清單），各有單元測試；`status` 訊息擴充成設計文件「狀態快照」一節的欄位；總覽頁換成四區（異常警示、現在在哪一步、本局／上一局、今天累計）。要等第 1 階段的訊息管道驗收後再寫，才能照實際的資料來源（VisionEngine、RunRecorder、StallWatch）定義輸入。
- **第 3 階段：移除舊面板**。刪除 `PanelForm.Rules.cs`、`PanelForm.Lists.cs` 的 WinForms 建置程式（`SkillSetting` 移到 `RoHelper/Panel/`）、`PanelForm.Other.cs` 的分頁建置（保留匯入、匯出、套用到其他分身的對話框）、`AppSettings.WebPanel` 與選單開關，更新 README 與 HANDOFF。
