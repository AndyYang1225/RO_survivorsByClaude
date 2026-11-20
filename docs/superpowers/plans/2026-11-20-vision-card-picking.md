# 視覺模式：選卡 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 視覺模式在升級／寶箱選卡畫面跳出時，從截圖讀出每張卡（卡名、稀有度、新技能、按鈕），照原本 JS 的選卡規則決定「選／放逐／專注／重抽全部」，用 CDP 真實輸入執行。

**Architecture:** 分四層，每層可單獨測試：
1. `PickerReader`：截圖 → `PickerInfo`。卡片位置用「很亮的大色塊」找（不需要範本）；卡名和其他文字用一次 OCR 讀整個選卡區，再依位置分給各張卡；稀有度看標籤底色、「新！」看右上角的粉紅色、按鈕看亮度和左右順序；按鈕上剩幾次用 OCR 讀小圓圈裡的數字。
2. `CardDecider`：`PickerInfo` ＋ `PickSettings` ＋ `RunPickState` → `PickDecision`。逐行移植 engine.js 的 `decide()`（純邏輯）。
3. `PickerFlow`：等待時間（tPick、動作後等畫面更新、選卡按鍵沒生效時改點卡片）與每局狀態（已專注、已學、鎖定的神祇）。純邏輯。
4. `VisionEngine`：每輪先看有沒有選卡畫面，有就交給上面三層，沒有才跑大廳流程。

**Tech Stack:** .NET 8 WinForms、C# 12、Windows.Media.Ocr（zh-Hant-TW）、xUnit 2.9.2

## Global Constraints

- 全程不在遊戲網頁注入或執行 JS；CDP 只允許 `Input.*`。JS 引擎已停用（見 `Program.cs` 的【JS 引擎已停用】），不要恢復。
- 選卡規則以 `RoHelper/Engine/engine.js` 的 `decide()`（約 448～559 行）為準，判斷順序和理由文字（`why`）要一致。engine.js 只當參考，不編進 exe、不修改。
- **尋寶率先跳過**（使用者決定）：不讀卡片或道具的尋寶率；`treasureCap`、`hpAfterCap` 在視覺模式不生效（等同尋寶率永遠 0、沒超過上限）；`pickOrder = 'treasure'` 時等同「最稀有」（JS 在沒有尋寶卡時本來就是這樣）。
- **不做「累積升級按 B」**（使用者把遊戲設成升級立即跳出選卡），`autoUpgrade`、`preMvp` 在視覺模式不生效。
- 不新增 NuGet 套件。
- 寫作風格依 `coding-style` skill：識別字英文；註解、XML doc、log、UI 文字繁體中文；每個 public class／record 要有 `<summary>`；單一語句的 if 不加大括號；刻意不做的事寫原因。
- 測試專案 `C:\Ro\RoHelper.Tests`；執行測試前 RoHelper.exe 必須關閉。git repo 在 `C:\Ro`，分支 `vision-lobby-flow`（計畫 2 的分支，沿用），每個 Task 一個 commit，訊息結尾加 `Co-Authored-By:` 行。
- 實測數據（2026-11-20，dpr 1）：卡片 ≈ 214×352 px；OCR 整個選卡區 226～450 ms；卡名 OCR 全對；稀有度標籤字、「新！」、卡片下方按鈕文字 OCR 讀不到；「重抽全部」會讀成「重擒全部／垔擒全部／重全部」，但一定含「全部」。

## 不在這份計畫的範圍

- 尋寶率（卡片上的 % 和道具欄 hover 讀數）——之後另做。
- 職業自動偵測：沿用設定裡的 `curJob`（使用者也可以在面板手動選職業）。
- 對局紀錄 `runs.jsonl` 的選卡明細。
- 野外走位（計畫 4）。

---

## File Structure

| 檔案 | 責任 |
|---|---|
| `RoHelper.Tests/Fixtures/*.png`（新） | 使用者截的真實選卡畫面（測試用） |
| `RoHelper/Vision/Picker/PickerModels.cs`（新） | `ActKind`、`ActButton`、`CardInfo`、`PickerInfo`、`PickDo`、`PickDecision`、`RunPickState` |
| `RoHelper/Vision/Picker/PickSettings.cs`（新） | 從 cfg 讀出選卡用的設定快照（含目前職業的技能／神祇清單） |
| `RoHelper/Vision/Picker/CardDecider.cs`（新） | engine.js `decide()` 的 C# 版 |
| `RoHelper/Vision/Picker/CardLayout.cs`（新） | 純像素判讀：找卡片、稀有度底色、「新！」、按鈕 |
| `RoHelper/Vision/Picker/PickerReader.cs`（新） | 截圖 → `PickerInfo`（CardLayout ＋ OCR） |
| `RoHelper/Vision/Picker/PickerFlow.cs`（新） | 選卡的等待時間與每局狀態 |
| `RoHelper/Vision/LobbyFlow.cs`（改） | `FlowDecision.StartsRun`（按「前往野外」時＝新的一局） |
| `RoHelper/Vision/VisionEngine.cs`（改） | 選卡優先、執行選卡動作、選卡紀錄 |
| `RoHelper/Program.cs`（改） | `VisionEngine` 多傳 `LogStore` |
| `RoHelper/Forms/VisionDebugForm.cs`（改） | 「讀選卡」按鈕 |
| `RoHelper.Tests/*`（新） | 各 Task 的測試 |

---

### Task 1: 測試素材與資料模型

**Files:**
- Create: `RoHelper.Tests/Fixtures/升級選卡2.png`、`升級選卡3.png`、`寶箱選卡3.png`（複製）
- Modify: `RoHelper.Tests/RoHelper.Tests.csproj`
- Create: `RoHelper/Vision/Picker/PickerModels.cs`
- Test: `RoHelper.Tests/PickerModelsTests.cs`

**Interfaces:**
- Produces（後面所有 Task 都用）：
  - `enum ActKind { Reroll, Focus, Banish }`
  - `record ActButton(ActKind Kind, Rectangle Bounds, bool Enabled, int Left)`，`bool Usable => Enabled && Left > 0`
  - `record CardInfo(int Index, Rectangle Bounds, string Name, string Text, int Rarity, bool SkillBadge, bool IsNew, IReadOnlyList<ActButton> Buttons)`，`ActButton? Button(ActKind kind)`
  - `record PickerInfo(bool IsChest, IReadOnlyList<CardInfo> Cards, ActButton? RerollAll)`
  - `enum PickDo { Pick, Banish, Focus, Reroll }`
  - `record PickDecision(PickDo Do, int? Index, string Why, string? FocusKey = null)`
  - `class RunPickState { HashSet<string> Picked; HashSet<string> Focused; string DeityLock }`

- [ ] **Step 1: 複製測試素材**

使用者的截圖在 `%AppData%\RoHelper\templates\` 或 `%AppData%\RoHelper\shots\`（使用者可能已經搬過），兩個地方都找。在 `C:\Ro` 執行：

```bash
mkdir -p RoHelper.Tests/Fixtures
for n in 升級選卡2 升級選卡3 寶箱選卡3; do
  for d in "$APPDATA/RoHelper/shots" "$APPDATA/RoHelper/templates"; do
    [ -f "$d/$n.png" ] && cp "$d/$n.png" RoHelper.Tests/Fixtures/ && break
  done
done
ls -la RoHelper.Tests/Fixtures
```

Expected: 三個檔案，尺寸 755×508、777×678、802×631。找不到任何一個 → 回報 BLOCKED（請使用者重新截圖），不要用別的圖代替。

`RoHelper.Tests/RoHelper.Tests.csproj`：在 engine.js 那個 ItemGroup 後面加：

```xml
  <!-- 真實選卡畫面（使用者 2026-11-20 截的，dpr 1）：PickerReader／CardLayout 的測試素材 -->
  <ItemGroup>
    <None Include="Fixtures\*.png" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: 寫失敗的測試 `RoHelper.Tests/PickerModelsTests.cs`**

```csharp
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class PickerModelsTests
{
    [Fact]
    public void ActButton_UsableNeedsEnabledAndLeft()
    {
        Assert.True(new ActButton(ActKind.Banish, Rectangle.Empty, true, 3).Usable);
        Assert.False(new ActButton(ActKind.Banish, Rectangle.Empty, true, 0).Usable);
        Assert.False(new ActButton(ActKind.Banish, Rectangle.Empty, false, 3).Usable);
    }

    [Fact]
    public void CardInfo_ButtonByKind()
    {
        var focus = new ActButton(ActKind.Focus, new Rectangle(1, 2, 3, 4), true, 1);
        var card = new CardInfo(0, Rectangle.Empty, "雷爆術", "雷爆術", 0, true, true, [focus]);

        Assert.Same(focus, card.Button(ActKind.Focus));
        Assert.Null(card.Button(ActKind.Reroll));
    }

    [Fact]
    public void Fixtures_AreCopiedToOutput()
    {
        foreach (var n in new[] { "升級選卡2", "升級選卡3", "寶箱選卡3" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Fixtures", n + ".png")), n);
    }
}
```

- [ ] **Step 3: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter PickerModelsTests`
Expected: 編譯失敗，找不到 `RoHelper.Vision.Picker`。

- [ ] **Step 4: 實作 `RoHelper/Vision/Picker/PickerModels.cs`**

```csharp
namespace RoHelper.Vision.Picker;

/// <summary>卡片下方的動作按鈕種類</summary>
public enum ActKind
{
    Reroll,
    Focus,
    Banish,
}

/// <summary>
/// 卡片下方的一顆動作按鈕（截圖像素座標）。Left＝按鈕上小圓圈的剩餘次數；讀不到數字時，能按就當 99、不能按就當 0（同 JS 的 actLeft）。
/// </summary>
public sealed record ActButton(ActKind Kind, Rectangle Bounds, bool Enabled, int Left)
{
    public bool Usable => Enabled && Left > 0;
}

/// <summary>
/// 選卡畫面上的一張卡。Name＝卡名；Text＝卡片上讀到的所有文字（卡名、副標、效果），判斷神祇等關鍵字用。
/// Rarity：0 普通、1 優良、2 稀有、3 史詩、4 傳說、5 神話（同 JS 的 RARITY 順序）。SkillBadge＝標籤是「技能」（新技能卡），這種卡的稀有度一律當 0。
/// </summary>
public sealed record CardInfo(int Index, Rectangle Bounds, string Name, string Text, int Rarity, bool SkillBadge, bool IsNew, IReadOnlyList<ActButton> Buttons)
{
    public ActButton? Button(ActKind kind) => Buttons.FirstOrDefault(b => b.Kind == kind);
}

/// <summary>一個選卡畫面：升級或寶箱、各張卡、下方的「重抽全部」（沒有這顆按鈕時是 null）</summary>
public sealed record PickerInfo(bool IsChest, IReadOnlyList<CardInfo> Cards, ActButton? RerollAll);

/// <summary>選卡的動作種類（同 JS decide() 回傳的 do）</summary>
public enum PickDo
{
    Pick,
    Banish,
    Focus,
    Reroll,
}

/// <summary>
/// 選卡決定。Index＝第幾張（0 起算；重抽全部時是 null）。Why＝理由（同 JS 的 why 文字，顯示在面板、寫進選卡紀錄）。
/// FocusKey＝專注時要記住的鍵：技能名稱，或「item:道具名」（同 JS 的 st.focused）。
/// </summary>
public sealed record PickDecision(PickDo Do, int? Index, string Why, string? FocusKey = null);

/// <summary>
/// 一局內的選卡狀態（同 JS 的 st.picked／st.focused／st.deityLock），每局開始時換一個新的。
/// Picked＝已經學過的「學到後放逐」技能；Focused＝已經專注過的技能或道具；DeityLock＝這局選定的神祇（空字串＝還沒選）。
/// </summary>
public sealed class RunPickState
{
    public HashSet<string> Picked { get; } = [];
    public HashSet<string> Focused { get; } = [];
    public string DeityLock { get; set; } = "";
}
```

- [ ] **Step 5: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 69 通過（原 66 ＋ 3）。

- [ ] **Step 6: Commit**

```bash
git add RoHelper.Tests/Fixtures RoHelper.Tests/RoHelper.Tests.csproj RoHelper.Tests/PickerModelsTests.cs RoHelper/Vision/Picker/PickerModels.cs
git commit -m "feat: 選卡資料模型與測試素材"
```

---

### Task 2: 選卡設定快照（PickSettings）

**Files:**
- Create: `RoHelper/Vision/Picker/PickSettings.cs`
- Test: `RoHelper.Tests/PickSettingsTests.cs`

**Interfaces:**
- Consumes: `ConfigStore`（`Current`、`GetBool`、`GetInt`、`GetString`、`ToStringList`）
- Produces:
  - `record SkillCfg(IReadOnlyList<string> Order, IReadOnlyList<string> After, IReadOnlyList<string> Ban, IReadOnlyList<string> Focus, IReadOnlyList<string> Skip)`，`static SkillCfg Empty`
  - `record PickSettings(...)`（欄位見下方程式碼）＋ `static PickSettings From(ConfigStore)`

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/PickSettingsTests.cs`**

```csharp
using System.Text.Json.Nodes;

using RoHelper.Storage;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public sealed class PickSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private ConfigStore Store(JsonObject cfg)
    {
        var store = new ConfigStore(_path);
        store.Replace(cfg);
        return store;
    }

    [Fact]
    public void From_ReadsJobSkillsAndDeity_FromCurrentJob()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["jobSel"] = "auto",
            ["curJob"] = "巫師",
            ["skillOn"] = true,
            ["skills"] = new JsonObject
            {
                ["巫師"] = new JsonObject
                {
                    ["order"] = new JsonArray("雷爆術", "冰箭術"),
                    ["after"] = new JsonArray("雷爆術"),
                    ["ban"] = new JsonArray("火箭術"),
                    ["focus"] = new JsonArray("冰箭術"),
                    ["skip"] = new JsonArray("聖靈召喚"),
                },
            },
            ["deity"] = new JsonObject { ["巫師"] = new JsonArray("思想之神") },
            ["deityBan"] = new JsonObject { ["巫師"] = new JsonArray("貪婪之神") },
        }));

        Assert.Equal("巫師", s.Job);
        Assert.Equal(["雷爆術", "冰箭術"], s.Skills.Order);
        Assert.Equal(["雷爆術"], s.Skills.After);
        Assert.Equal(["火箭術"], s.Skills.Ban);
        Assert.Equal(["冰箭術"], s.Skills.Focus);
        Assert.Equal(["聖靈召喚"], s.Skills.Skip);
        Assert.Equal(["思想之神"], s.DeityList);
        Assert.Equal(["貪婪之神"], s.DeityBan);
    }

    [Fact]
    public void From_SkillOff_UsesEmptySkills()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["jobSel"] = "魔法師",
            ["skillOn"] = false,
            ["skills"] = new JsonObject { ["魔法師"] = new JsonObject { ["order"] = new JsonArray("火箭術") } },
        }));

        Assert.Equal("魔法師", s.Job);
        Assert.Empty(s.Skills.Order);
    }

    [Fact]
    public void From_SplitsBanExtraWords_LikeJs()
    {
        var s = PickSettings.From(Store(new JsonObject { ["banExtra"] = true, ["banExtraT"] = "屬性附加，普攻強化、 毒" }));

        Assert.Equal(["屬性附加", "普攻強化", "毒"], s.BanExtraWords);
    }

    [Fact]
    public void From_ReadsFlagsAndNumbers()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["pickSkill"] = true, ["pickChest"] = false, ["autoFocus"] = true, ["autoFocusN"] = 2,
            ["banishKeep"] = true, ["banishKeepN"] = 1, ["pickOrder"] = "rarity", ["deityOrder"] = "first",
            ["tPickN"] = 350, ["debugLog"] = true,
        }));

        Assert.True(s.PickSkill);
        Assert.False(s.PickChest);
        Assert.Equal(2, s.AutoFocusN);
        Assert.Equal(1, s.BanishKeepN);
        Assert.Equal("rarity", s.PickOrder);
        Assert.Equal("first", s.DeityOrder);
        Assert.Equal(350, s.PickDelay);
        Assert.True(s.DebugLog);
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter PickSettingsTests`
Expected: 編譯失敗，找不到 `PickSettings`。

- [ ] **Step 3: 實作 `RoHelper/Vision/Picker/PickSettings.cs`**

```csharp
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
    bool PickSkill, bool PickChest, int PickDelay, bool DebugLog)
{
    public static PickSettings From(ConfigStore c)
    {
        // 目前職業：面板選「自動」時用上次偵測到的職業（視覺模式不偵測職業，沿用設定裡的 curJob）
        var sel = c.GetString("jobSel", "auto");
        var job = sel == "auto" ? c.GetString("curJob") : sel;
        var skillOn = c.GetBool("skillOn");
        var skills = skillOn && c.Current["skills"]?[job] is JsonObject o
            ? new SkillCfg(List(o["order"]), List(o["after"]), List(o["ban"]), List(o["focus"]), List(o["skip"]))
            : SkillCfg.Empty;

        return new PickSettings(
            job, skillOn, skills,
            c.GetBool("banExtra"), Words(c.GetString("banExtraT")),
            c.GetBool("deityPrio"), c.GetString("deityOrder", "none"), c.GetBool("deityLock"),
            List(c.Current["deity"]?[job]), List(c.Current["deityBan"]?[job]),
            c.GetBool("autoFocus"), c.GetInt("autoFocusN", 1),
            c.GetStringList("itemFocus"), c.GetStringList("itemPrio"), c.GetStringList("itemBan"),
            c.GetBool("rerollAll"), c.GetBool("avoidNew"), c.GetBool("banishKeep"), c.GetInt("banishKeepN", 2),
            c.GetString("pickOrder", "treasure"),
            c.GetBool("pickSkill"), c.GetBool("pickChest"), c.GetInt("tPickN", 350), c.GetBool("debugLog"));
    }

    private static List<string> List(JsonNode? node) => ConfigStore.ToStringList(node);

    /// <summary>同 JS 的 words()：用逗號、頓號、分號、空白切開</summary>
    private static List<string> Words(string text) => [.. WordSplit().Split(text).Where(w => w.Length > 0)];

    [GeneratedRegex(@"[,，、;；\s]+")]
    private static partial Regex WordSplit();
}
```

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 73 通過。

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Vision/Picker/PickSettings.cs RoHelper.Tests/PickSettingsTests.cs
git commit -m "feat: 選卡設定快照"
```

---

### Task 3: 選卡決策（CardDecider，移植 decide()）

**Files:**
- Create: `RoHelper/Vision/Picker/CardDecider.cs`
- Test: `RoHelper.Tests/CardDeciderTests.cs`

**Interfaces:**
- Consumes: Task 1 的模型、Task 2 的 `PickSettings`／`SkillCfg`
- Produces: `CardDecider.Decide(PickerInfo, PickSettings, RunPickState) → PickDecision`；`CardDecider.IsDeity(CardInfo) → bool`；`CardDecider.DeityName(string name) → string`（去掉「+N」，鎖定神祇用）；`CardDecider.SkillOf(CardInfo, IReadOnlyList<string>) → string?`

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/CardDeciderTests.cs`**

```csharp
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class CardDeciderTests
{
    private static ActButton Btn(ActKind k, bool enabled = true, int left = 3) => new(k, new Rectangle(0, 0, 10, 10), enabled, left);

    /// <summary>升級卡：預設有能用的專注和放逐（各 3 次），沒有重抽</summary>
    private static CardInfo Card(int i, string name, int rarity = 0, bool isNew = false, bool skillBadge = false, string? text = null, ActButton[]? buttons = null)
        => new(i, new Rectangle(i * 100, 0, 90, 150), name, text ?? name, rarity, skillBadge, isNew, buttons ?? [Btn(ActKind.Focus), Btn(ActKind.Banish)]);

    private static PickerInfo Picker(bool chest, params CardInfo[] cards) => new(chest, cards, Btn(ActKind.Reroll, left: 5));

    private static PickSettings Settings(SkillCfg? skills = null, bool autoFocus = false, int autoFocusN = 1, string pickOrder = "treasure",
        bool deityPrio = false, string deityOrder = "none", bool deityLock = false, string[]? deity = null, string[]? deityBan = null,
        string[]? itemPrio = null, string[]? itemBan = null, string[]? itemFocus = null, bool avoidNew = true, bool rerollAll = true,
        bool banishKeep = true, int banishKeepN = 2, bool banExtra = false, string[]? banExtraWords = null)
        => new("巫師", true, skills ?? SkillCfg.Empty, banExtra, banExtraWords ?? [], deityPrio, deityOrder, deityLock, deity ?? [], deityBan ?? [],
            autoFocus, autoFocusN, itemFocus ?? [], itemPrio ?? [], itemBan ?? [], rerollAll, avoidNew, banishKeep, banishKeepN, pickOrder,
            true, true, 350, false);

    private static SkillCfg Skills(string[]? order = null, string[]? after = null, string[]? ban = null, string[]? focus = null, string[]? skip = null)
        => new(order ?? [], after ?? [], ban ?? [], focus ?? [], skip ?? []);

    [Fact]
    public void FirstPrioritySkill_IsPicked()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Skills(order: ["雷爆術", "火箭術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "技能第 1 優先"), d);
    }

    [Fact]
    public void OtherPrioritySkills_RarityThenOrder()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "冰箭術", rarity: 0), Card(1, "火箭術", rarity: 1)),
            Settings(Skills(order: ["雷爆術", "冰箭術", "火箭術"])), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
        Assert.Equal(1, d.Index);
        Assert.Equal("技能優先（稀有度 > 順序）", d.Why);
    }

    [Fact]
    public void BanList_IsBanishedFirst()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "雷爆術"), Card(1, "火箭術")), Settings(Skills(order: ["雷爆術"], ban: ["火箭術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 1, "放逐清單"), d);
    }

    [Fact]
    public void AfterSkill_NotNew_IsBanished_ButNewIsPicked()
    {
        var learned = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: false)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), new RunPickState());
        var fresh = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: true)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), new RunPickState());

        Assert.Equal(PickDo.Banish, learned.Do);
        Assert.Equal(PickDo.Pick, fresh.Do);
    }

    [Fact]
    public void AfterSkill_PickedThisRun_IsBanishedEvenIfNew()
    {
        var st = new RunPickState();
        st.Picked.Add("雷爆術");

        var d = CardDecider.Decide(Picker(false, Card(0, "雷爆術", isNew: true)), Settings(Skills(order: ["雷爆術"], after: ["雷爆術"])), st);

        Assert.Equal(PickDo.Banish, d.Do);
    }

    [Fact]
    public void BanExtraWords_AreBanished()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "攻擊：屬性附加"), Card(1, "火箭術")),
            Settings(Skills(order: ["火箭術"]), banExtra: true, banExtraWords: ["屬性附加"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 0, "放逐清單"), d);
    }

    [Fact]
    public void AutoFocus_FirstNonAfterPriority_Once()
    {
        // 雷爆術是「學到後放逐」：要標成新卡，否則會先被放逐
        var picker = Picker(false, Card(0, "冰箭術"), Card(1, "雷爆術", isNew: true));
        var settings = Settings(Skills(order: ["雷爆術", "冰箭術"], after: ["雷爆術"]), autoFocus: true, autoFocusN: 1);
        var st = new RunPickState();

        var first = CardDecider.Decide(picker, settings, st);
        st.Focused.Add(first.FocusKey!);
        var second = CardDecider.Decide(picker, settings, st);

        Assert.Equal(new PickDecision(PickDo.Focus, 0, "專注：冰箭術", "冰箭術"), first);
        Assert.Equal(PickDo.Pick, second.Do);
    }

    [Fact]
    public void AutoFocus_SkippedWhenFocusButtonUnusable()
    {
        var card = Card(0, "冰箭術", buttons: [Btn(ActKind.Focus, enabled: false, left: 0), Btn(ActKind.Banish)]);

        var d = CardDecider.Decide(Picker(false, card), Settings(Skills(order: ["冰箭術"]), autoFocus: true), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ItemFocus_WorksOnChestToo()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "傷害祝福"), Card(1, "傷害 秘藥")), Settings(autoFocus: true, itemFocus: ["傷害秘藥"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Focus, 1, "道具專注：傷害秘藥", "item:傷害秘藥"), d);
    }

    [Fact]
    public void Chest_DoesNotBanishOrUseSkillPriority()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "雷爆術"), Card(1, "傷害秘藥", rarity: 2)), Settings(Skills(order: ["雷爆術"], ban: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void UnwantedNewSkill_Avoided_WhenOtherCardExists()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "聖靈召喚", isNew: true, skillBadge: true), Card(1, "傷害祝福")),
            Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void OnlyUnwantedNewSkills_RerollAll()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "聖靈召喚", isNew: true, skillBadge: true), Card(1, "火焰之壁", isNew: true, skillBadge: true)),
            Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "只有不要的新技能"), d);
    }

    [Fact]
    public void OnlyUnwantedNewSkills_NoReroll_BanishKeepsTwo()
    {
        var noReroll = new ActButton(ActKind.Reroll, Rectangle.Empty, false, 0);
        var few = Card(0, "聖靈召喚", isNew: true, skillBadge: true, buttons: [Btn(ActKind.Banish, left: 2)]);
        var many = Card(1, "火焰之壁", isNew: true, skillBadge: true, buttons: [Btn(ActKind.Banish, left: 3)]);

        var d = CardDecider.Decide(new PickerInfo(false, [few, many], noReroll), Settings(Skills(order: ["雷爆術"])), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Banish, 1, "不要的新技能（重抽用完）"), d);
    }

    [Fact]
    public void AllForcedSkip_RerollAll()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "回復藥水"), Card(1, "冷卻藥水")), Settings(itemBan: ["回復藥水", "冷卻藥水"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "全部都是強制不選的卡"), d);
    }

    [Fact]
    public void FavItem_PickedRarestAmongFavs()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "傷害祝福", rarity: 1), Card(1, "傷害秘藥", rarity: 2), Card(2, "堡壘之神", rarity: 0, text: "堡壘之神 神祇")),
            Settings(itemPrio: ["傷害祝福", "傷害秘藥"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "勾選的道具"), d);
    }

    [Fact]
    public void PickOrder_FirstAndRarity()
    {
        var picker = Picker(true, Card(0, "回復藥水", rarity: 0), Card(1, "傷害秘藥", rarity: 2), Card(2, "傷害祝福", rarity: 2));

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "第一張"), CardDecider.Decide(picker, Settings(pickOrder: "first"), new RunPickState()));
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "稀有度優先"), CardDecider.Decide(picker, Settings(pickOrder: "rarity"), new RunPickState()));
    }

    [Fact]
    public void DeityPriority_FirstMode_PicksDeity()
    {
        var picker = Picker(true, Card(0, "傷害秘藥", rarity: 2), Card(1, "思想之神", text: "思想之神 神祇"));

        var d = CardDecider.Decide(picker, Settings(deityPrio: true, deityOrder: "first", deity: ["思想之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "神祇優先：思想之神"), d);
    }

    [Fact]
    public void DeityPriority_RarityMode_YieldsToRarerCard()
    {
        var picker = Picker(true, Card(0, "傷害秘藥", rarity: 2), Card(1, "思想之神", rarity: 0, text: "思想之神 神祇"));

        var d = CardDecider.Decide(picker, Settings(deityPrio: true, deityOrder: "rarity", deity: ["思想之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 0, "最稀有"), d);
    }

    [Fact]
    public void DeityLock_RefinesSameDeity_AndIgnoresOthers()
    {
        var st = new RunPickState { DeityLock = "思想之神" };
        var picker = Picker(true, Card(0, "堡壘之神", rarity: 3, text: "堡壘之神 神祇"), Card(1, "思想之神 +2", text: "思想之神 +2 神祇"), Card(2, "回復藥水"));

        var refine = CardDecider.Decide(picker, Settings(deityLock: true, deityOrder: "first"), st);
        var otherOnly = CardDecider.Decide(Picker(true, Card(0, "堡壘之神", rarity: 3, text: "堡壘之神 神祇"), Card(1, "回復藥水")), Settings(deityLock: true), st);

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "神祇精煉：思想之神"), refine);
        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), otherOnly);
    }

    [Fact]
    public void BannedDeity_NotPicked()
    {
        var d = CardDecider.Decide(Picker(true, Card(0, "貪婪之神", rarity: 3, text: "貪婪之神 神祇"), Card(1, "回復藥水")), Settings(deityBan: ["貪婪之神"]), new RunPickState());

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "最稀有"), d);
    }

    [Fact]
    public void Helpers_DeityDetectionAndName()
    {
        Assert.True(CardDecider.IsDeity(Card(0, "堡壘之神")));
        Assert.True(CardDecider.IsDeity(Card(0, "雷爆術 Deity")));
        Assert.True(CardDecider.IsDeity(Card(0, "某某", text: "某某 神祇")));
        Assert.False(CardDecider.IsDeity(Card(0, "傷害祝福")));
        Assert.Equal("思想之神", CardDecider.DeityName("思想之神 +3"));
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter CardDeciderTests`
Expected: 編譯失敗，找不到 `CardDecider`。

- [ ] **Step 3: 實作 `RoHelper/Vision/Picker/CardDecider.cs`**

```csharp
using System.Text.RegularExpressions;

namespace RoHelper.Vision.Picker;

/// <summary>
/// 選卡決策：engine.js decide()（約 448～559 行）的 C# 版，判斷順序和理由文字一行對一行。
/// 順序：1 神祇 → 2 放逐（升級卡）→ 3 技能專注（升級卡）→ 道具專注 → 可選的卡（排除放逐／強制不選／別的神祇）
/// → 4 技能優先（升級卡）→ 5 不學清單外的新技能（升級卡）→ 6 勾選的道具 → 選法（第一張／稀有度／最稀有）。
/// 刻意沒有尋寶率的分支（JS 的 treasureCapped、hasTreasure、hpAfterCap）：視覺模式先不讀尋寶率（使用者決定），
/// 等同「尋寶率 0、沒超過上限」，所以「尋寶率優先」在這裡就是「最稀有」，跟 JS 在沒有尋寶卡時一樣。
/// 純函式：不改 RunPickState（選了之後要記什麼由 PickerFlow 負責）。
/// </summary>
public static partial class CardDecider
{
    public static PickDecision Decide(PickerInfo p, PickSettings s, RunPickState st)
    {
        var cards = p.Cards;
        var all = Enumerable.Range(0, cards.Count).ToList();

        // 1. 神祇（寶箱第五欄）
        PickDecision? deityPick = null;
        if (s.DeityLock && st.DeityLock.Length > 0)
        {
            var same = all.FirstOrDefault(i => IsDeity(cards[i]) && SameDeity(cards[i].Name, st.DeityLock), -1);
            if (same >= 0)
                deityPick = new(PickDo.Pick, same, "神祇精煉：" + st.DeityLock);
        }
        else if (s.DeityPrio)
        {
            foreach (var w in s.DeityList)
            {
                var i = all.FirstOrDefault(i => IsDeity(cards[i]) && cards[i].Name.Contains(w), -1);
                if (i < 0)
                    continue;
                deityPick = new(PickDo.Pick, i, "神祇優先：" + w);
                break;
            }
        }
        var skipDeity = false;
        // 不特別優先：交給後面的一般規則（道具勾選、稀有度）
        if (deityPick is not null && s.DeityOrder == "none")
            deityPick = null;
        if (deityPick is { Index: { } di })
        {
            // 「稀有度比較」模式：同一次選卡裡有別張卡比神祇卡更稀有 → 這次不選神祇
            var better = s.DeityOrder == "rarity" && all.Any(i => !IsDeity(cards[i]) && !IsSkipped(cards[i], s)
                && !ShouldBanish(cards[i], s, st) && cards[i].Rarity > cards[di].Rarity);
            if (!better)
                return deityPick;
            skipDeity = true;
        }

        if (!p.IsChest)
        {
            // 2. 放逐（一次一張）
            var b = all.FirstOrDefault(i => ShouldBanish(cards[i], s, st) && Usable(cards[i], ActKind.Banish), -1);
            if (b >= 0)
                return new(PickDo.Banish, b, "放逐清單");

            // 3. 專注：前 N 個「優先」技能（不含學到後放逐的），每個只專注一次
            if (s.AutoFocus)
            {
                var fl = s.Skills.Focus.Where(w => s.Skills.Order.Contains(w)).ToList();
                var targets = fl.Count > 0 ? fl : s.Skills.Order.Where(w => !s.Skills.After.Contains(w)).Take(s.AutoFocusN).ToList();
                var f = all.FirstOrDefault(i => SkillOf(cards[i], targets) is { } w && !st.Focused.Contains(w) && Usable(cards[i], ActKind.Focus), -1);
                if (f >= 0)
                {
                    var w = SkillOf(cards[f], targets)!;
                    return new(PickDo.Focus, f, "專注：" + w, w);
                }
            }
        }

        // 道具專注：勾了 ★ 的道具出現時先按「專注」（升級卡、寶箱卡都適用；每個道具每局只專注一次）
        if (s.AutoFocus && s.ItemFocus.Count > 0)
        {
            string? ItemOf(CardInfo c) => s.ItemFocus.FirstOrDefault(x => Squash(c.Name).Contains(Squash(x)));
            var f = all.FirstOrDefault(i => ItemOf(cards[i]) is { } w && !st.Focused.Contains("item:" + w) && Usable(cards[i], ActKind.Focus), -1);
            if (f >= 0)
            {
                var w = ItemOf(cards[f])!;
                return new(PickDo.Focus, f, "道具專注：" + w, "item:" + w);
            }
        }

        // 可以考慮的卡：不含要放逐的；強制不選（神祇 ✕、技能「不選」、道具 ✕）不考慮；已鎖定神祇時，不同的神祇也不考慮
        var pool = all.Where(i => !ShouldBanish(cards[i], s, st) && !IsBannedDeity(cards[i], s) && !IsSkipped(cards[i], s)
            && !(skipDeity && IsDeity(cards[i]))
            && !(s.DeityLock && st.DeityLock.Length > 0 && IsDeity(cards[i]) && !SameDeity(cards[i].Name, st.DeityLock))).ToList();
        if (pool.Count == 0)
        {
            // 全是不要的 → 能重抽就重抽；不能才只好挑一張（遊戲規定一定要選）
            if (p.RerollAll is { Usable: true })
                return new(PickDo.Reroll, null, "全部都是強制不選的卡");
            pool = all.Where(i => !IsBannedDeity(cards[i], s) && !IsSkipped(cards[i], s)).ToList();
            if (pool.Count == 0)
                pool = all;
        }

        if (!p.IsChest)
        {
            var order = s.Skills.Order;
            int Rank(int i) => order.ToList().FindIndex(w => cards[i].Name.Contains(w));

            // 4. 技能優先：第一個有出現就選；其餘的看稀有度，同稀有度照順序
            var first = pool.FirstOrDefault(i => Rank(i) == 0, -1);
            if (first >= 0)
                return new(PickDo.Pick, first, "技能第 1 優先");
            var hits = pool.Where(i => Rank(i) > 0).OrderByDescending(i => cards[i].Rarity).ThenBy(Rank).ToList();
            if (hits.Count > 0)
                return new(PickDo.Pick, hits[0], "技能優先（稀有度 > 順序）");

            // 5. 不學清單外的新技能
            var bad = pool.Where(i => IsUnwantedNew(cards[i], s)).ToList();
            if (s.AvoidNew && bad.Count > 0)
            {
                var good = pool.Where(i => !IsUnwantedNew(cards[i], s)).ToList();
                if (good.Count > 0)
                    pool = good;
                else
                {
                    if (s.RerollAll && p.RerollAll is { Usable: true })
                        return new(PickDo.Reroll, null, "只有不要的新技能");
                    var keep = s.BanishKeep ? s.BanishKeepN : 0;
                    var bi = bad.FirstOrDefault(i => cards[i].Button(ActKind.Banish) is { Usable: true } bb && bb.Left > keep, -1);
                    if (bi >= 0)
                        return new(PickDo.Banish, bi, "不要的新技能（重抽用完）");
                }
            }
            else if (bad.Count == pool.Count && s.RerollAll && p.RerollAll is { Usable: true })
                return new(PickDo.Reroll, null, "只有不要的新技能");
        }

        // 6. 道具／寶箱／其他
        if (s.ItemPrio.Count > 0)
        {
            var fav = pool.Where(i => IsFavItem(cards[i], s)).ToList();
            if (fav.Count > 0)
                return new(PickDo.Pick, RarestIndex(cards, fav), "勾選的道具");
        }
        if (s.PickOrder == "first")
            return new(PickDo.Pick, pool[0], "第一張");
        if (s.PickOrder == "rarity")
        {
            var top = pool.Max(i => cards[i].Rarity);
            return new(PickDo.Pick, pool.First(i => cards[i].Rarity == top), "稀有度優先");
        }
        return new(PickDo.Pick, RarestIndex(cards, pool), "最稀有");
    }

    /// <summary>神祇卡（同 JS isDeity：卡片文字有 Deity／之神／神祇）</summary>
    public static bool IsDeity(CardInfo c) => DeityWord().IsMatch(c.Name + " " + c.Text);

    /// <summary>神祇名稱去掉精煉等級（「思想之神 +3」→「思想之神」），鎖定神祇時記這個</summary>
    public static string DeityName(string name) => PlusLevel().Replace(name, "").Trim();

    /// <summary>卡名裡有沒有清單裡的某個技能（同 JS skillOfCard：卡名可能是「雷爆術」或「攻擊：屬性附加」）</summary>
    public static string? SkillOf(CardInfo c, IReadOnlyList<string> list) => list.FirstOrDefault(w => c.Name.Contains(w));

    private static bool Usable(CardInfo c, ActKind kind) => c.Button(kind) is { Usable: true };

    private static bool ShouldBanish(CardInfo c, PickSettings s, RunPickState st)
    {
        if (IsDeity(c))
            return false;
        if (s.BanExtra && SkillOf(c, s.BanExtraWords) is not null)
            return true;
        if (SkillOf(c, s.Skills.Ban) is not null)
            return true;
        // 有「新！」＝還沒學 → 要選；沒有＝已經學了 → 放逐
        var w = SkillOf(c, s.Skills.After);
        return w is not null && (!c.IsNew || st.Picked.Contains(w));
    }

    /// <summary>清單外的新技能（技能欄還沒滿時不想學的）</summary>
    private static bool IsUnwantedNew(CardInfo c, PickSettings s)
        => s.SkillOn && c.IsNew && !IsDeity(c) && c.SkillBadge && SkillOf(c, s.Skills.Order) is null;

    /// <summary>強制不選：技能「不選」、道具 ✕（神祇不算，神祇另有 IsBannedDeity）</summary>
    private static bool IsSkipped(CardInfo c, PickSettings s)
        => !IsDeity(c) && (SkillOf(c, s.Skills.Skip) is not null || s.ItemBan.Any(x => Squash(c.Name).Contains(Squash(x))));

    private static bool IsBannedDeity(CardInfo c, PickSettings s) => IsDeity(c) && s.DeityBan.Any(n => SameDeity(c.Name, n));

    private static bool IsFavItem(CardInfo c, PickSettings s)
    {
        var n = Squash(c.Name);
        return n.Length > 0 && s.ItemPrio.Any(w => n.Contains(Squash(w)));
    }

    /// <summary>神祇名稱比對（卡片可能寫「雷爆術 Deity」、道具欄寫「雷爆術 Deity +3」）</summary>
    private static bool SameDeity(string a, string b)
    {
        var (x, y) = (DeityKey(a), DeityKey(b));
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    private static string DeityKey(string n) => Spaces().Replace(DeityNoise().Replace(n, ""), "");

    /// <summary>同 JS rarestIndex：最稀有的；一樣稀有取前面的</summary>
    private static int RarestIndex(IReadOnlyList<CardInfo> cards, List<int> pool)
    {
        var top = pool.Max(i => cards[i].Rarity);
        return pool.First(i => cards[i].Rarity == top);
    }

    private static string Squash(string s) => Spaces().Replace(s, "");

    [GeneratedRegex("Deity|之神|神祇", RegexOptions.IgnoreCase)]
    private static partial Regex DeityWord();

    [GeneratedRegex(@"Deity|\+\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex DeityNoise();

    [GeneratedRegex(@"\s*\+\s*\d+.*$")]
    private static partial Regex PlusLevel();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
```

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 94 通過（73 ＋ 21）。若某個測試跟 engine.js 的行為不一致，以 engine.js 為準：回報 DONE_WITH_CONCERNS，寫出 JS 行號與差異，不要自行改測試的預期值。

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Vision/Picker/CardDecider.cs RoHelper.Tests/CardDeciderTests.cs
git commit -m "feat: 選卡決策（移植 engine.js decide）"
```

---

### Task 4: 像素判讀（CardLayout）

**Files:**
- Create: `RoHelper/Vision/Picker/CardLayout.cs`
- Test: `RoHelper.Tests/CardLayoutTests.cs`

**Interfaces:**
- Consumes: `Frame`（`At(x,y) → (R,G,B)`、`Width`、`Height`）、`ColorBlobs.Find(frame, region, match, step, gap) → List<Blob>`（`X0/Y0/X1/Y1` 含端點、`Width`、`Height`）
- Produces（全部 static、純像素）：
  - `CardLayout.FindCards(Frame f, double dpr) → IReadOnlyList<Rectangle>`（由左到右；少於 2 張回空清單＝不是選卡畫面）
  - `CardLayout.ReadBadge(Frame f, Rectangle card, double dpr) → (int Rarity, bool Skill)`
  - `CardLayout.HasNewRibbon(Frame f, Rectangle card, double dpr) → bool`
  - `CardLayout.FindButtons(Frame f, Rectangle card, double dpr) → IReadOnlyList<(ActKind Kind, Rectangle Bounds, bool Enabled)>`
  - `CardLayout.IsEnabled(Frame f, Rectangle button) → bool`（重抽全部也用）

**真實畫面上的已知事實（測試的預期值，不能改）：**

| 素材 | 卡片 | 標籤（稀有度／技能） | 「新！」 | 卡片下方按鈕（左→右，能不能按） |
|---|---|---|---|---|
| 升級選卡2 | 雷爆術、聖靈召喚、火箭術 | 技能、技能、優良 | 有、有、沒有 | 卡1：專注✔ 放逐✔；卡2：專注✔ 放逐✔；卡3：重抽✔ 專注✘ 放逐✔ |
| 升級選卡3 | 雷爆術、雷擊術、火箭術 | 優良、優良、普通 | 都沒有 | 每張：重抽✘ 專注✘ 放逐✔ |
| 寶箱選卡3 | 傷害祝福、傷害秘藥、堡壘之神 | 優良、稀有、普通 | 都有 | 每張：重抽✔ 專注✘ 放逐✔ |

卡片位置（試跑結果，`r,g,b > 215` 的色塊，step 3）：升級選卡2 ≈ (24,42)-(237,393)、(261,42)-(474,396)、(498,39)-(711,393)；升級選卡3 ≈ (72,231)-(285,582)、(309,225)-(519,579)、(543,222)-(759,573)；寶箱選卡3 ≈ (63,159)-(276,513)、(300,156)-(510,510)、(534,150)-(747,504)。

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/CardLayoutTests.cs`**

```csharp
using RoHelper.Vision;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class CardLayoutTests
{
    internal static Frame Fixture(string name)
    {
        using var bmp = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".png"));
        return Frame.FromBitmap(bmp);
    }

    [Theory]
    [InlineData("升級選卡2", 24, 498)]
    [InlineData("升級選卡3", 72, 543)]
    [InlineData("寶箱選卡3", 63, 534)]
    public void FindCards_ThreeCardsLeftToRight(string fixture, int firstX, int lastX)
    {
        var cards = CardLayout.FindCards(Fixture(fixture), 1.0);

        Assert.Equal(3, cards.Count);
        Assert.InRange(cards[0].X, firstX - 8, firstX + 8);
        Assert.InRange(cards[2].X, lastX - 8, lastX + 8);
        Assert.All(cards, c => Assert.InRange(c.Width, 195, 235));
        Assert.All(cards, c => Assert.InRange(c.Height, 320, 380));
    }

    [Fact]
    public void FindCards_NoCards_OnPlainScreen()
    {
        Assert.Empty(CardLayout.FindCards(TestFrames.Solid(800, 600, 20, 25, 40), 1.0));
    }

    [Theory]
    [InlineData("升級選卡2", new[] { 0, 0, 1 }, new[] { true, true, false })]
    [InlineData("升級選卡3", new[] { 1, 1, 0 }, new[] { false, false, false })]
    [InlineData("寶箱選卡3", new[] { 1, 2, 0 }, new[] { false, false, false })]
    public void ReadBadge_RarityAndSkill(string fixture, int[] rarity, bool[] skill)
    {
        var f = Fixture(fixture);
        var cards = CardLayout.FindCards(f, 1.0);

        for (var i = 0; i < 3; i++)
        {
            var (r, s) = CardLayout.ReadBadge(f, cards[i], 1.0);
            Assert.True(rarity[i] == r && skill[i] == s, $"卡 {i + 1}：預期 ({rarity[i]}, {skill[i]})，實際 ({r}, {s})");
        }
    }

    [Theory]
    [InlineData("升級選卡2", new[] { true, true, false })]
    [InlineData("升級選卡3", new[] { false, false, false })]
    [InlineData("寶箱選卡3", new[] { true, true, true })]
    public void HasNewRibbon(string fixture, bool[] expected)
    {
        var f = Fixture(fixture);
        var cards = CardLayout.FindCards(f, 1.0);

        Assert.Equal(expected, cards.Select(c => CardLayout.HasNewRibbon(f, c, 1.0)).ToArray());
    }

    [Theory]
    [InlineData("升級選卡2", 0, "FB", "11")]
    [InlineData("升級選卡2", 2, "RFB", "101")]
    [InlineData("升級選卡3", 1, "RFB", "001")]
    [InlineData("寶箱選卡3", 2, "RFB", "101")]
    public void FindButtons_KindsAndEnabled(string fixture, int card, string kinds, string enabled)
    {
        var f = Fixture(fixture);
        var c = CardLayout.FindCards(f, 1.0)[card];

        var buttons = CardLayout.FindButtons(f, c, 1.0);

        Assert.Equal(kinds, string.Concat(buttons.Select(b => b.Kind switch { ActKind.Reroll => "R", ActKind.Focus => "F", _ => "B" })));
        Assert.Equal(enabled, string.Concat(buttons.Select(b => b.Enabled ? "1" : "0")));
        Assert.All(buttons, b => Assert.True(b.Bounds.Y > c.Bottom, "按鈕要在卡片下方"));
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter CardLayoutTests`
Expected: 編譯失敗，找不到 `CardLayout`。

- [ ] **Step 3: 量真實顏色（校正用，不進 commit）**

先在 `CardLayoutTests` 暫時加一個會印出數值的測試（`ITestOutputHelper`），對三個素材各張卡印出：
- 卡片上緣中間標籤區（x：卡片中心 ±30，y：卡片上緣 −14～+14）裡「飽和度高」（max−min > 60）像素的平均 RGB 與數量；
- 卡片右上角（x：右緣 −70～右緣，y：上緣 −6～上緣 +70）粉紅像素（R > 220、70 ≤ G ≤ 160、110 ≤ B ≤ 190）的數量；
- 卡片下方（x：左緣 −20～右緣 +20，y：下緣 +4～下緣 +60）每個「亮度 > 90 且飽和度 < 60」色塊的位置、大小、平均亮度。

用 `dotnet test --filter <名稱> --logger "console;verbosity=detailed"` 看輸出，依實際數值決定下一步的常數。量完把這個暫時測試刪掉。

- [ ] **Step 4: 實作 `RoHelper/Vision/Picker/CardLayout.cs`**

以下是結構和判斷方式；`// 校正：` 標記的常數依 Step 3 量到的數值填，並在註解寫下量到的值（例如「優良 ≈ (52,199,89)」）。所有像素距離都乘 dpr。

```csharp
namespace RoHelper.Vision.Picker;

/// <summary>
/// 選卡畫面的純像素判讀（不用 OCR）：卡片位置、稀有度標籤底色、「新！」粉紅斜條、卡片下方的動作按鈕。
/// 為什麼不用 OCR 讀這些：實測（2026-11-20）標籤是白字壓彩色底、「新！」是斜的、按鈕字很小，Windows OCR 都讀不到；顏色和位置反而很穩定。
/// 常數是用 RoHelper.Tests/Fixtures 的真實截圖（dpr 1）校正的，距離都乘 dpr。
/// </summary>
public static class CardLayout
{
    /// <summary>
    /// 找卡片：很亮（r、g、b 都 > 215）的大色塊，寬 180～260、高 300～400（× dpr），而且至少 2 張、上緣差不到 25 px。
    /// 少於 2 張回空清單＝不是選卡畫面（大廳的對話框也是亮的，但只有一塊、大小也不同）。
    /// </summary>
    public static IReadOnlyList<Rectangle> FindCards(Frame f, double dpr) { /* ColorBlobs.Find(step 3, gap 3) → 篩大小 → 依 X0 排序 → 檢查上緣對齊 */ }

    /// <summary>
    /// 稀有度：卡片上緣中間的標籤底色。取標籤區裡飽和度高的像素平均色，跟參考色比最接近的；飽和像素太少（灰色標籤）＝普通。
    /// 技能標籤是淺藍、稀有是深藍，兩者用參考色區分。史詩／傳說／神話還沒有真實截圖，先用紫／橘／紅的估計值。
    /// </summary>
    public static (int Rarity, bool Skill) ReadBadge(Frame f, Rectangle card, double dpr) { /* 校正：參考色表 */ }

    /// <summary>「新！」：卡片右上角的粉紅斜條，粉紅像素夠多就算有</summary>
    public static bool HasNewRibbon(Frame f, Rectangle card, double dpr) { /* 校正：門檻 */ }

    /// <summary>
    /// 卡片下方的動作按鈕：亮度 > 90、飽和度低的色塊（藍色的次數小圓圈飽和度高，會被排除），寬 30～70、高 20～45（× dpr），由左到右。
    /// 種類看順序：3 顆＝重抽、專注、放逐；2 顆＝專注、放逐（新技能卡沒有重抽）；1 顆＝放逐。能不能按看 <see cref="IsEnabled"/>。
    /// </summary>
    public static IReadOnlyList<(ActKind Kind, Rectangle Bounds, bool Enabled)> FindButtons(Frame f, Rectangle card, double dpr) { /* ... */ }

    /// <summary>按鈕能不能按：能按是白底（平均亮度高），不能按是灰底</summary>
    public static bool IsEnabled(Frame f, Rectangle button) { /* 校正：亮度門檻 */ }
}
```

（`/* ... */` 處由實作者依上面的判斷方式寫完整程式；這些函式的判斷規則和預期結果已經完整列在 XML doc 和測試裡。）

- [ ] **Step 5: 執行測試，調整到全部通過**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter CardLayoutTests`
Expected: 全部通過。只能調常數和判斷細節，**不能改測試的預期值**（那是真實畫面上的事實）。如果某一項怎麼調都會讓別的素材出錯，回報 DONE_WITH_CONCERNS，附上量到的數值。

- [ ] **Step 6: 執行全部測試並 Commit**

Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 108 通過（94 ＋ 14）。

```bash
git add RoHelper/Vision/Picker/CardLayout.cs RoHelper.Tests/CardLayoutTests.cs
git commit -m "feat: 選卡畫面的像素判讀（卡片、稀有度、新、按鈕）"
```

---

### Task 5: 截圖 → 選卡資訊（PickerReader）

**Files:**
- Create: `RoHelper/Vision/Picker/PickerReader.cs`
- Test: `RoHelper.Tests/PickerReaderTests.cs`

**Interfaces:**
- Consumes: `CardLayout`（Task 4）、`OcrReader.ReadAsync(Frame, Rectangle, double upscale = 2) → IReadOnlyList<OcrLine>`（`OcrLine(string Text, Rectangle Bounds)`，座標是截圖座標；中文字之間的空白已拿掉）、`OcrReader.TryCreate(out string)`
- Produces: `PickerReader(OcrReader ocr)`；`Task<PickerInfo?> ReadAsync(Frame f, double dpr)`（不是選卡畫面回 null）

**判讀方式：**
1. `CardLayout.FindCards`；少於 2 張回 null。
2. 一次 OCR：範圍＝所有卡片的聯集往左右各擴 40、往上擴 250、往下擴 180（× dpr），夾在畫面內。
3. 寶箱／升級：卡片上緣以上的 OCR 行裡有「寶箱」或「金幣」＝寶箱。
4. 每張卡：中心點在卡片內的 OCR 行；**卡名**＝其中高度最大、含中文字的一行（排除只有數字或符號的行）；**Text**＝這些行依 Y 排序後用空格接起來。讀不到卡名時用空字串（決策會當成普通卡）。
5. 標籤、「新！」、按鈕：`CardLayout`。按鈕剩餘次數：對按鈕右上角的小圓圈（按鈕右緣 −8～+14、上緣 −14～+8，× dpr）各做一次 OCR（upscale 4），取第一串數字；讀不到時能按＝99、不能按＝0。
6. 重抽全部：卡片下緣以下、文字含「全部」的 OCR 行；按鈕範圍＝該行往外擴 (16, 10)（× dpr）；能不能按用 `CardLayout.IsEnabled`；剩餘次數同第 5 點。沒有這一行＝null。

**真實畫面上的已知事實（測試預期值）：** 除了 Task 4 的表，還有：
- 卡名：升級選卡2＝雷爆術、聖靈召喚、火箭術；升級選卡3＝雷爆術、雷擊術、火箭術；寶箱選卡3＝傷害祝福、傷害秘藥、堡壘之神。
- 是否寶箱：升級選卡2 否（圖上沒有標題）、升級選卡3 否（標題「升級！」）、寶箱選卡3 是（標題「寶箱」）。
- 剩餘次數：升級選卡2 卡1 專注 1、放逐 4；卡3 重抽 1、放逐 4；重抽全部 5（能按）。升級選卡3 每張放逐 3；重抽全部 0（不能按）。寶箱選卡3 每張重抽 1、放逐 3；重抽全部 5（能按）。

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/PickerReaderTests.cs`**

```csharp
using RoHelper.Vision;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

/// <summary>用真實截圖跑完整判讀（需要這台電腦有繁中 OCR；計畫 1 已確認有）</summary>
public class PickerReaderTests
{
    private static readonly PickerReader Reader = new(OcrReader.TryCreate(out var e) ?? throw new InvalidOperationException(e));

    [Theory]
    [InlineData("升級選卡2", false, "雷爆術|聖靈召喚|火箭術")]
    [InlineData("升級選卡3", false, "雷爆術|雷擊術|火箭術")]
    [InlineData("寶箱選卡3", true, "傷害祝福|傷害秘藥|堡壘之神")]
    public async Task ReadAsync_NamesAndChest(string fixture, bool chest, string names)
    {
        var p = await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0);

        Assert.NotNull(p);
        Assert.Equal(chest, p.IsChest);
        Assert.Equal(names, string.Join("|", p.Cards.Select(c => c.Name)));
    }

    [Fact]
    public async Task ReadAsync_UpgradeWithNewSkills()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡2"), 1.0))!;

        Assert.Equal([true, true, false], p.Cards.Select(c => c.IsNew));
        Assert.Equal([true, true, false], p.Cards.Select(c => c.SkillBadge));
        Assert.Equal(1, p.Cards[2].Rarity);
        Assert.Equal(1, p.Cards[0].Button(ActKind.Focus)!.Left);
        Assert.Equal(4, p.Cards[0].Button(ActKind.Banish)!.Left);
        Assert.Equal(1, p.Cards[2].Button(ActKind.Reroll)!.Left);
        Assert.False(p.Cards[2].Button(ActKind.Focus)!.Usable);
        Assert.Equal(new { Usable = true, Left = 5 }, new { p.RerollAll!.Usable, p.RerollAll.Left });
    }

    [Fact]
    public async Task ReadAsync_UpgradeAllUsedUp()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("升級選卡3"), 1.0))!;

        Assert.All(p.Cards, c => Assert.False(c.Button(ActKind.Reroll)!.Usable));
        Assert.All(p.Cards, c => Assert.Equal(3, c.Button(ActKind.Banish)!.Left));
        Assert.False(p.RerollAll!.Usable);
    }

    [Fact]
    public async Task ReadAsync_ChestWithDeity()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("寶箱選卡3"), 1.0))!;

        Assert.True(CardDecider.IsDeity(p.Cards[2]));
        Assert.False(CardDecider.IsDeity(p.Cards[0]));
        Assert.Equal([1, 2, 0], p.Cards.Select(c => c.Rarity));
        Assert.All(p.Cards, c => Assert.Equal(3, c.Button(ActKind.Banish)!.Left));
        Assert.Equal(5, p.RerollAll!.Left);
    }

    [Fact]
    public async Task ReadAsync_NotAPicker_ReturnsNull()
    {
        Assert.Null(await Reader.ReadAsync(TestFrames.Solid(800, 600, 20, 25, 40), 1.0));
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter PickerReaderTests`
Expected: 編譯失敗，找不到 `PickerReader`。

- [ ] **Step 3: 實作 `RoHelper/Vision/Picker/PickerReader.cs`**

依上面「判讀方式」1～6 寫完整程式。class summary 要寫：判讀方式、為什麼只做一次大範圍 OCR（每次 OCR 有固定開銷，分卡各做一次會慢 3 倍）、剩餘次數讀不到時的退路（能按＝99，同 JS actLeft）。

- [ ] **Step 4: 執行測試，調整到全部通過**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter PickerReaderTests`
Expected: 全部通過。**不能改預期值**。剩餘次數的 OCR 如果怎麼調都讀不準（例如小圓圈太小），可以：(a) 改變裁切範圍、放大倍數、或先把圓圈裡的白字二值化再 OCR；(b) 仍然不行就回報 DONE_WITH_CONCERNS，附上讀到的原始文字，並把相關斷言標成 `Skip = "剩餘次數 OCR 讀不準：<原因>"`（只限 `.Left` 的斷言，其他斷言不能跳過）。

- [ ] **Step 5: 執行全部測試並 Commit**

Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 115 通過（108 ＋ 7）。

```bash
git add RoHelper/Vision/Picker/PickerReader.cs RoHelper.Tests/PickerReaderTests.cs
git commit -m "feat: 截圖讀出選卡資訊（卡名、寶箱、按鈕次數）"
```

---

### Task 6: 選卡流程（PickerFlow）

**Files:**
- Create: `RoHelper/Vision/Picker/PickerFlow.cs`
- Test: `RoHelper.Tests/PickerFlowTests.cs`

**Interfaces:**
- Consumes: 模型（Task 1）、`PickSettings`（Task 2）、`CardDecider`（Task 3）
- Produces:
  - `record PickerAction(string Status, char? Key = null, Point? Click = null, PickDecision? Decision = null)`：`Key`＝要按的數字鍵（選卡），`Click`＝要點的位置（截圖座標）
  - `PickerFlow.State`（`RunPickState`）、`PickerFlow.NewRun()`
  - `PickerFlow.Gone()`：畫面上沒有選卡時呼叫
  - `PickerFlow.Waiting(long now, int pickDelay) → bool`：還在等（剛出現未滿 tPick、或動作後畫面還沒更新）就回 true，這一輪不用 OCR
  - `PickerFlow.Act(PickerInfo p, PickSettings s, long now) → PickerAction`

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/PickerFlowTests.cs`**

```csharp
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class PickerFlowTests
{
    private static ActButton Btn(ActKind k, int x) => new(k, new Rectangle(x, 400, 40, 30), true, 3);

    private static CardInfo Card(int i, string name, bool isNew = true)
        => new(i, new Rectangle(i * 240, 40, 214, 352), name, name, 0, false, isNew, [Btn(ActKind.Focus, i * 240), Btn(ActKind.Banish, i * 240 + 60)]);

    private static PickerInfo Upgrade(params CardInfo[] cards) => new(false, cards, new ActButton(ActKind.Reroll, new Rectangle(300, 470, 80, 30), true, 5));

    private static PickSettings Settings(SkillCfg skills, bool pickSkill = true, bool autoFocus = false)
        => new("巫師", true, skills, false, [], false, "none", true, [], [], autoFocus, 1, [], [], [], true, true, true, 2, "treasure",
            pickSkill, true, 350, false);

    private static readonly SkillCfg Mage = new(["雷爆術", "冰箭術"], ["雷爆術"], [], [], []);

    [Fact]
    public void Waiting_UntilPickDelay()
    {
        var flow = new PickerFlow();

        Assert.True(flow.Waiting(0, 350));
        Assert.True(flow.Waiting(300, 350));
        Assert.False(flow.Waiting(400, 350));
    }

    [Fact]
    public void Pick_PressesDigit_AndRemembersAfterSkill()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Mage), 400);

        Assert.Equal('2', a.Key);
        Assert.Null(a.Click);
        Assert.Equal("選升級卡：雷爆術（技能第 1 優先）", a.Status);
        Assert.Contains("雷爆術", flow.State.Picked);
    }

    [Fact]
    public void Banish_ClicksBanishButton()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "雷爆術", isNew: false), Card(1, "冰箭術")), Settings(Mage), 400);

        Assert.Equal(PickDo.Banish, a.Decision!.Do);
        Assert.Equal(new Point(80, 415), a.Click);
        Assert.Equal("放逐：雷爆術（放逐清單）", a.Status);
    }

    [Fact]
    public void Focus_ClicksFocusButton_AndRemembers()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "冰箭術")), Settings(Mage, autoFocus: true), 400);

        Assert.Equal(new Point(20, 415), a.Click);
        Assert.Contains("冰箭術", flow.State.Focused);
    }

    [Fact]
    public void AfterAction_WaitsForScreenToUpdate()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        flow.Act(Upgrade(Card(0, "雷爆術", isNew: false), Card(1, "冰箭術")), Settings(Mage), 400);

        Assert.True(flow.Waiting(900, 350));
        Assert.False(flow.Waiting(1200, 350));
    }

    [Fact]
    public void PickKeyIgnored_ClicksCardAfterTimeout()
    {
        var flow = new PickerFlow();
        var picker = Upgrade(Card(0, "火箭術"), Card(1, "雷爆術"));
        flow.Waiting(0, 350);
        flow.Act(picker, Settings(Mage), 400);

        Assert.True(flow.Waiting(1500, 350));
        Assert.False(flow.Waiting(2000, 350));
        var retry = flow.Act(picker, Settings(Mage), 2000);

        Assert.Null(retry.Key);
        Assert.Equal(new Point(240 + 107, 40 + 176), retry.Click);
        Assert.Equal("選卡按鍵沒反應，改點卡片：雷爆術", retry.Status);
    }

    [Fact]
    public void PickKeyIgnored_ButNextPickerAlreadyShown_DecidesAgain()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        flow.Act(Upgrade(Card(0, "火箭術"), Card(1, "雷爆術")), Settings(Mage), 400);
        flow.Waiting(2000, 350);

        var next = flow.Act(Upgrade(Card(0, "冰箭術"), Card(1, "火焰之壁")), Settings(Mage), 2000);

        Assert.Equal('1', next.Key);
        Assert.Null(next.Click);
    }

    [Fact]
    public void PickSkillOff_WaitsForUser()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(Upgrade(Card(0, "雷爆術")), Settings(Mage, pickSkill: false), 400);

        Assert.Equal("等你選卡", a.Status);
        Assert.Null(a.Key);
        Assert.Null(a.Click);
    }

    [Fact]
    public void Deity_LocksOnFirstPick()
    {
        var flow = new PickerFlow();
        flow.Waiting(0, 350);
        var deity = new CardInfo(0, new Rectangle(0, 40, 214, 352), "思想之神 +1", "思想之神 +1 神祇", 0, false, true, []);

        flow.Act(new PickerInfo(true, [deity], null), Settings(SkillCfg.Empty), 400);

        Assert.Equal("思想之神", flow.State.DeityLock);
    }

    [Fact]
    public void NewRun_ClearsState_GoneResetsDelay()
    {
        var flow = new PickerFlow();
        flow.State.Picked.Add("雷爆術");
        flow.Waiting(0, 350);

        flow.NewRun();
        flow.Gone();

        Assert.Empty(flow.State.Picked);
        Assert.True(flow.Waiting(5000, 350));
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter PickerFlowTests`
Expected: 編譯失敗，找不到 `PickerFlow`。

- [ ] **Step 3: 實作 `RoHelper/Vision/Picker/PickerFlow.cs`**

```csharp
namespace RoHelper.Vision.Picker;

/// <summary>這一輪選卡要做的事：面板狀態文字、要按的數字鍵或要點的位置（截圖座標），以及決策本身（寫選卡紀錄用）</summary>
public sealed record PickerAction(string Status, char? Key = null, Point? Click = null, PickDecision? Decision = null);

/// <summary>
/// 選卡流程（同 engine.js pickerStep）：選卡畫面出現後等 tPick 再判斷；做完放逐／專注／重抽後等畫面更新（同 JS 的 +400 ms，
/// 視覺模式多算截圖和 OCR 的時間，用 700 ms）；選卡用數字鍵（同 JS pressKey），按了 1.5 秒畫面還在就改點卡片（同 JS 的 click 補救）。
/// 也負責一局內的狀態（<see cref="RunPickState"/>）：專注過的、學過的「學到後放逐」技能、鎖定的神祇。
/// 純邏輯：時間由呼叫端傳入，不截圖、不送輸入。
/// </summary>
public sealed class PickerFlow
{
    /// <summary>放逐／專注／重抽之後，等畫面換成新卡片的時間</summary>
    private const long Settle = 700;

    /// <summary>按了選卡數字鍵之後，畫面還在就改點卡片的等待時間</summary>
    private const long KeyTimeout = 1_500;

    private long? _seenAt;
    private long _readyAt;
    private (int Index, string Name, Rectangle Card)? _pendingPick;

    public RunPickState State { get; private set; } = new();

    /// <summary>新的一局：清掉專注過、學過、鎖定的神祇（同 JS 進野外時重設 st.picked／st.focused／st.deityLock）</summary>
    public void NewRun() => State = new RunPickState();

    /// <summary>畫面上沒有選卡：下次出現時重新等 tPick</summary>
    public void Gone()
    {
        _seenAt = null;
        _readyAt = 0;
        _pendingPick = null;
    }

    /// <summary>還在等（剛出現未滿 tPick、動作後畫面還沒更新）就回 true：這一輪不用花時間 OCR</summary>
    public bool Waiting(long now, int pickDelay)
    {
        _seenAt ??= now;
        return now - _seenAt < pickDelay || now < _readyAt;
    }

    public PickerAction Act(PickerInfo p, PickSettings s, long now)
    {
        var kind = p.IsChest ? "選寶箱卡" : "選升級卡";
        if (!(p.IsChest ? s.PickChest : s.PickSkill))
            return new("等你選卡");

        // 選卡的數字鍵沒反應（同一張卡還在原位）：改點那張卡（同 JS 按鍵後 250 ms 補一次 click）。
        // 要比對卡名：連續升級時下一個選卡畫面可能已經出現（主迴圈間隔長時看不到中間沒有選卡的那一刻），那時不能點舊位置
        if (_pendingPick is { } pending)
        {
            _pendingPick = null;
            if (pending.Index < p.Cards.Count && p.Cards[pending.Index].Name == pending.Name)
            {
                _readyAt = now + Settle;
                var card = pending.Card;
                return new($"選卡按鍵沒反應，改點卡片：{pending.Name}", Click: new Point(card.X + card.Width / 2, card.Y + card.Height / 2));
            }
        }

        var d = CardDecider.Decide(p, s, State);
        var name = d.Index is { } i ? p.Cards[i].Name : "";
        switch (d.Do)
        {
            case PickDo.Banish:
                _readyAt = now + Settle;
                return new($"放逐：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Banish)!.Bounds), Decision: d);
            case PickDo.Focus:
                State.Focused.Add(d.FocusKey!);
                _readyAt = now + Settle;
                return new($"專注：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Focus)!.Bounds), Decision: d);
            case PickDo.Reroll:
                _readyAt = now + Settle;
                return new($"重抽全部（{d.Why}）", Click: Center(p.RerollAll!.Bounds), Decision: d);
        }

        // 選卡：記住學過的「學到後放逐」技能、第一次選的神祇（之後只精煉同一個）
        var idx = d.Index!.Value;
        var picked = p.Cards[idx];
        if (CardDecider.IsDeity(picked) && State.DeityLock.Length == 0)
            State.DeityLock = CardDecider.DeityName(picked.Name);
        if (CardDecider.SkillOf(picked, s.Skills.After) is { } w)
            State.Picked.Add(w);
        _pendingPick = (idx, picked.Name, picked.Bounds);
        _readyAt = now + KeyTimeout;
        var label = name.Length > 0 ? name : $"第 {idx + 1} 張";
        return new($"{kind}：{label}（{d.Why}）", Key: (char)('1' + idx), Decision: d);
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
```

注意：`Pick_PressesDigit_AndRemembersAfterSkill` 期望 `Act` 回傳後 `_pendingPick` 已設定；但只要畫面消失（`Gone()`），下一次出現就是新的選卡，不會誤點。`PickKeyIgnored_ClicksCardAfterTimeout` 驗證的就是「同一個選卡畫面 1.5 秒後還在」的補救。

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 125 通過（115 ＋ 10）。

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Vision/Picker/PickerFlow.cs RoHelper.Tests/PickerFlowTests.cs
git commit -m "feat: 選卡流程（等待、動作、每局狀態）"
```

---

### Task 7: 接進視覺引擎、除錯視窗「讀選卡」

**Files:**
- Modify: `RoHelper/Vision/LobbyFlow.cs`（`FlowDecision.StartsRun`）
- Modify: `RoHelper/Vision/VisionEngine.cs`
- Modify: `RoHelper/Program.cs`
- Modify: `RoHelper/Forms/VisionDebugForm.cs`
- Test: `RoHelper.Tests/LobbyFlowTests.cs`（加一個）

**Interfaces:**
- Consumes: `PickerReader`、`PickerFlow`、`PickSettings`、`CardLayout.FindCards`；`LogStore.Add(JsonNode)`；`CdpInput.KeyAsync(char)`、`ClickAsync(x, y)`；`VisionKit.Ocr`（可能是 null）
- Produces: `FlowDecision(string Action, Point? Click = null, bool CountRun = false, bool StartsRun = false)`；`VisionEngine(ConfigStore config, LogStore log, EngineSchema schema)`

- [ ] **Step 1: LobbyFlow 標記「新的一局」（TDD）**

`RoHelper.Tests/LobbyFlowTests.cs` 加：

```csharp
    [Fact]
    public void GoField_Click_StartsRun()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Start = Btn }, Cfg(), 0);
        var start = flow.Decide(Screen() with { Start = Btn }, Cfg(), 900);
        flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2000);
        var go = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2900);

        Assert.False(start.StartsRun);
        Assert.True(go.StartsRun);
    }
```

執行確認編譯失敗後，`RoHelper/Vision/LobbyFlow.cs`：
- `FlowDecision` 改成 `public sealed record FlowDecision(string Action, Point? Click = null, bool CountRun = false, bool StartsRun = false);`，summary 補一句「StartsRun＝這一輪按了活動的『前往野外』，新的一局開始（選卡要清掉上一局的狀態）」。
- 步驟 3 按下按鈕那行 `return new(action, Center(b));` 改成 `return new(action, Center(b), StartsRun: !isStart);`

- [ ] **Step 2: VisionEngine 接上選卡**

`RoHelper/Vision/VisionEngine.cs`：
1. using 加 `using RoHelper.Vision.Picker;`。
2. 欄位加 `private readonly LogStore _log;`、`private readonly PickerFlow _picker = new();`、`private PickerReader? _pickerReader;`。
3. 建構子改成 `public VisionEngine(ConfigStore config, LogStore log, EngineSchema schema)`，存 `_log = log;`。
4. `Start` 裡 `_reader = ...` 下面加：`_pickerReader = kit.Ocr is { } ocr ? new PickerReader(ocr) : null;`
5. `TickAsync` 的「步驟 1：截圖並辨識」改成先處理選卡（放在 `var frame = ...; var dpr = ...;` 之後、`_reader.Read` 之前）：

```csharp
            // 步驟 1：選卡畫面優先（選卡會擋住整局，大廳流程的按鈕這時也不會出現）
            if (CardLayout.FindCards(frame, dpr).Count >= 2)
            {
                await PickAsync(frame, dpr);
                return;
            }
            _picker.Gone();
```

6. 原本的步驟 1、2 改名為步驟 2、3；`if (decision.CountRun) CountRun();` 下面加：

```csharp
            if (decision.StartsRun)
                _picker.NewRun();
```

7. 加方法：

```csharp
    /// <summary>
    /// 選卡：等 tPick（剛出現、或動作後畫面還沒更新時不做 OCR）→ 讀卡 → 決定 → 按數字鍵或點按鈕。
    /// 沒有繁中 OCR 時只顯示「等你選卡」，不亂點。
    /// </summary>
    private async Task PickAsync(Frame frame, double dpr)
    {
        var s = PickSettings.From(_config);
        if (_pickerReader is null)
        {
            Report("等你選卡（沒有繁中 OCR）");
            return;
        }
        if (_picker.Waiting(Environment.TickCount64, s.PickDelay))
        {
            Report("選卡中…");
            return;
        }
        if (await _pickerReader.ReadAsync(frame, dpr) is not { } info)
            return;
        var action = _picker.Act(info, s, Environment.TickCount64);
        if (action.Key is { } key)
            await _kit!.Input.KeyAsync(key);
        if (action.Click is { } p)
        {
            var (x, y) = _kit!.Viewport.ToCss(p.X, p.Y);
            await _kit.Input.ClickAsync(x, y);
        }
        if (s.DebugLog && action.Decision is { } d)
            LogPick(info, s, action.Status, d);
        Report(action.Status);
    }

    /// <summary>選卡紀錄（面板「除錯」勾選時）：格式同 JS 的 logPick，exe 寫進 picklog.json</summary>
    private void LogPick(PickerInfo info, PickSettings s, string status, PickDecision d)
    {
        var cards = new JsonArray(info.Cards.Select(c => (JsonNode?)JsonValue.Create(
            string.Join(" | ", new[] { c.Name, c.SkillBadge ? "技能" : RarityName(c.Rarity), c.IsNew ? "新！" : "", c.Text }.Where(x => x.Length > 0)))).ToArray());
        _log.Add(new JsonObject
        {
            ["t"] = DateTime.Now.ToString("HH:mm:ss"),
            ["job"] = s.Job,
            ["title"] = info.IsChest ? "寶箱" : "升級",
            ["treasure"] = 0,
            ["cards"] = cards,
            ["act"] = status,
            ["why"] = d.Why,
        });
    }

    private static string RarityName(int r) => r is >= 0 and <= 5 ? new[] { "普通", "優良", "稀有", "史詩", "傳說", "神話" }[r] : "";
```

8. `CountRun()` 裡面最後加 `_picker.NewRun();`（結算＝這局結束；下一局的選卡從乾淨狀態開始，即使「前往野外」是使用者自己按的）。
9. 更新 class summary：加一句「選卡畫面優先於大廳流程（<see cref="PickAsync"/>）」。

- [ ] **Step 3: Program 傳入 LogStore**

`RoHelper/Program.cs`：`new VisionEngine(config, BuiltInSchema.Load())` 改成 `new VisionEngine(config, log, BuiltInSchema.Load())`（註解掉的那行不用改）。

- [ ] **Step 4: 除錯視窗「讀選卡」**

`RoHelper/Forms/VisionDebugForm.cs`：
1. using 加 `using RoHelper.Vision.Picker;`。
2. 加按鈕 `var readPicker = new Button { Text = "讀選卡", AutoSize = true };`，放進 `bar.Controls.AddRange([...])` 的最後，`readPicker.Click += async (_, _) => await ReadPickerAsync();`。
3. 加方法：

```csharp
    /// <summary>讀目前截圖的選卡畫面，把每張卡讀到的內容印在記錄欄（校正判讀用；不做決定、不點擊）</summary>
    private async Task ReadPickerAsync()
    {
        if (_frame is null || _kit.Ocr is null)
        {
            Log(_kit.Ocr is null ? _kit.OcrError : "要先有截圖");
            return;
        }
        var sw = Stopwatch.StartNew();
        var p = await new PickerReader(_kit.Ocr).ReadAsync(_frame, _kit.Viewport.Dpr);
        if (p is null)
        {
            Log($"不是選卡畫面（{sw.ElapsedMilliseconds} ms）");
            return;
        }
        Log($"{(p.IsChest ? "寶箱" : "升級")}選卡，{p.Cards.Count} 張（{sw.ElapsedMilliseconds} ms）；重抽全部：{Describe(p.RerollAll)}");
        foreach (var c in p.Cards)
            Log($"  {c.Index + 1}. {c.Name}｜稀有度 {c.Rarity}{(c.SkillBadge ? "｜技能" : "")}{(c.IsNew ? "｜新！" : "")}｜{string.Join(" ", c.Buttons.Select(Describe))}｜{c.Text}");
    }

    private static string Describe(ActButton? b)
        => b is null ? "無" : $"{b.Kind switch { ActKind.Reroll => "重抽", ActKind.Focus => "專注", _ => "放逐" }}{(b.Enabled ? "" : "✘")}{b.Left}";
```

- [ ] **Step 5: 建置、測試、檢查沒有碰網頁**

Run: `dotnet build C:\Ro\RoHelper\RoHelper.csproj` → Expected: 0 個錯誤、0 個警告
Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 126 通過
Run（在 `C:\Ro\RoHelper`）: `grep -rn '"Runtime\.\|"DOM\.\|ExecuteScript\|AddScriptToExecute\|PostWebMessage\|WebMessageReceived' Vision/` → Expected: 沒有輸出

- [ ] **Step 6: Commit**

```bash
git add RoHelper/Vision/LobbyFlow.cs RoHelper/Vision/VisionEngine.cs RoHelper/Program.cs RoHelper/Forms/VisionDebugForm.cs RoHelper.Tests/LobbyFlowTests.cs
git commit -m "feat: 視覺模式自動選卡，除錯視窗加讀選卡"
```

---

### Task 8: 實機驗證與文件

**Files:**
- Modify: `RoHelper/README.md`
- Modify: 本計畫書（文末加「實機驗證結果」）

- [ ] **Step 1: 使用者實機驗證**

使用者關掉 RoHelper 後說「重新啟動」。確認：
1. 面板「升級」分頁的「升級卡自動選」「寶箱卡自動選」要勾起來（之前為了截圖取消過）；「其他」分頁勾「記錄每次選卡」（除錯用）。
2. 進野外自己走位，升級時：面板顯示「［視覺］選升級卡：<卡名>（<理由>）」，遊戲裡選了那張卡。
3. 遇到要放逐／專注／重抽全部的情況，看是否點到正確的按鈕。
4. 撿到寶箱：面板顯示「［視覺］選寶箱卡：…」。
5. 任何一次選得跟預期不同：在選卡畫面暫停（F8），開「視覺除錯」→「截一張」→「讀選卡」，把記錄欄貼回來；也可以看 `%AppData%\RoHelper\picklog.json`。
6. 記下「讀選卡」顯示的耗時。
7. 主迴圈間隔（面板「其他 → 時間」）目前是 1500 ms，建議改回 250，選卡和大廳流程的反應會快很多。

- [ ] **Step 2: 更新 README**

`RoHelper/README.md` 的「## 目前狀態（2026-11-20）：JS 引擎已停用」段落：把「視覺模式目前只會跑大廳流程；**選卡（計畫 3）和野外走位（計畫 4）完成前，進野外要自己玩**。」換成：

```markdown
- 視覺模式會跑大廳流程和**自動選卡**（升級／寶箱，規則同面板設定）；**野外走位（計畫 4）完成前，進野外要自己走**。
- 選卡先不讀尋寶率：「尋寶率超過 N% 就不選尋寶卡」「超過後優先選生命」在視覺模式不生效，「尋寶率優先」等同「最稀有」。
- 選卡判讀有問題時：在選卡畫面按 F8 暫停 →「視覺除錯」→「截一張」→「讀選卡」，記錄欄會列出每張卡讀到的內容。
```

- [ ] **Step 3: 計畫書記錄實機結果並 Commit**

在本計畫書最後加「## 實機驗證結果（日期）」：Step 1 每一項的結果、讀選卡耗時、遇到的問題與修正。

```bash
git add RoHelper/README.md docs/superpowers/plans/2026-11-20-vision-card-picking.md
git commit -m "docs: 視覺模式選卡的說明與實機結果"
```

---

## Self-Review 紀錄

- **涵蓋範圍**：路線表第 3 階段「選卡（OCR ＋ decide() 移植）」——讀卡（Task 4、5）、決策（Task 3）、流程與每局狀態（Task 6）、接進引擎（Task 7）。使用者決定的「先跳過尋寶率」「不做累積升級按 B」寫進 Global Constraints，並在 `PickSettings`／`CardDecider` 的 summary 說明原因。
- **型別一致性**：`ActButton`／`CardInfo`／`PickerInfo`／`PickDecision`／`RunPickState`（Task 1）在 Task 3～7 使用；`PickSettings` 欄位順序在 Task 2 定義，Task 3、6 的測試用同樣的位置參數（24 個）建構；`CardLayout.FindCards` 在 Task 5、7 使用；`FlowDecision.StartsRun` 在 Task 7 加入，既有呼叫端用具名參數不受影響。
- **刻意留給實作者的部分**：Task 4 的顏色／亮度常數和 Task 5 的 OCR 細節，因為要對真實截圖校正；預期結果已經完整寫成測試（真實畫面上的事實），實作者只能調判斷方式，不能改預期值。
- **測試數量**：計畫 2 結束 66；Task 1：＋3＝69；Task 2：＋4＝73；Task 3：＋21＝94；Task 4：＋14＝108（FindCards 3＋1、ReadBadge 3、HasNewRibbon 3、FindButtons 4）；Task 5：＋7＝115；Task 6：＋10＝125；Task 7：＋1＝126。各 Task 的「預期通過數」以前一個 Task 的實際數字＋本 Task 新增數為準。

## 實機驗證結果（2026-11-20，換電腦後）

- 誤判檢查（大廳／節慶主持人／結算按「讀選卡」）：使用者未特別回報，實際跑局沒有誤點。
- 升級卡：技能選卡正常（使用者確認）。
- 寶箱卡：正常（使用者確認）。
- 放逐／專注／重抽全部：實機尚未遇到要觸發的情況；用真實截圖確認按鈕位置與剩餘次數讀取正確（「重抽全部」5 次、灰色「專注」判為不能按）。
- 遊戲改版：「重抽全部」右邊新增「自動」按鈕。OCR 讀不到「自動」兩字，改用像素找按鈕；新增規則「放逐清單都放逐過後改按遊戲『自動』」（83a5e31），實機確認勾選項出現、放逐完後有按到。
- 結算畫面（「你死了」）：舊「返回角色」範本分數 0.86 未過門檻 0.9，用新截圖重存整顆按鈕的範本（自身 1.0、其他畫面最高 0.82），實機確認會自動返回角色。舊範本備份在 `%AppData%\RoHelper\templates-backup\`。
- 小問題：第 1 張卡「重抽」的剩餘次數（實際 1）讀不到，當成 99；目前規則用不到這個數字。
