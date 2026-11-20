# 視覺模式：大廳流程 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 加一個「視覺模式」：完全不在遊戲網頁注入 JS，只靠截圖＋範本認出大廳流程的按鈕，用 CDP 真實點擊自動跑「單人大廳 → 走到節慶主持人 → 交談 → 開始遊戲 → 前往野外 → 結算返回角色」。

**Architecture:** 新增 `IAutomation` 介面，面板只透過它跟引擎溝通；JS 模式用既有的 `EngineBridge`，視覺模式用新的 `VisionEngine`，由 `app.json` 的 `VisionMode` 決定（同一時間只有一個在跑）。視覺模式沒有 JS 送規則清單，改用從 `engine.js` 抽出來、內建在 exe 裡的 `schema.json`；設定的預設值與修正（原本 JS 的 `applyStored`／`clampTimes`）移植到 C# 的 `ConfigDefaults`。流程判斷 `LobbyFlow` 是純函式（截圖辨識結果＋設定＋時間 → 要點哪裡），可以單元測試；`ScreenReader` 負責把截圖變成辨識結果。F8／F9 改由 exe 的 `ProcessCmdKey` 處理（兩種模式通用）。

**Tech Stack:** .NET 8 WinForms、C# 12、WebView2 1.0.2903.40、xUnit 2.9.2、Node.js（只用來產生 `schema.json`，不進 exe）

## Global Constraints

- 視覺模式下**不得**呼叫 `AddScriptToExecuteOnDocumentCreatedAsync`、`ExecuteScriptAsync`、`PostWebMessage*`，也不得接 `WebMessageReceived`；CDP 只允許 `Input.*`。截圖只用 `ScreenGrabber`（PrintWindow）。
- JS 模式的行為不能變（規則、面板、自動更新照舊）。
- 不新增 NuGet 套件。
- 寫作風格依 `coding-style` skill：識別字英文；註解、XML doc、log、UI 文字繁體中文；每個 public class 要有 `<summary>`；單一語句的 if 不加大括號；刻意不做的事要寫原因。
- 測試專案在 `C:\Ro\RoHelper.Tests`（跟 RoHelper 平行）。執行測試前 RoHelper.exe 必須關閉（執行中會鎖住 exe，建置失敗）。
- `C:\Ro` 目前不是 git repo：使用者同意 `git init` 之前，各 Task 的 commit 步驟跳過。
- 範本名稱固定（`ScreenTemplates`）：`單人大廳`、`開始遊戲`、`前往野外`、`返回角色`、`節慶主持人`、`交談提示`。
- 實測數據（計畫 1）：遊戲畫面 1904 寬、dpr 1、PrintWindow 截圖約 50 ms；整張畫面找一個 135×47 的範本要 723 ms。

## 不在這份計畫的範圍

- 遠征（走到遠征隊長、用 OCR 選地圖、調難度滑桿）：之後另寫計畫。視覺模式下勾了「自動遠征」只會顯示「視覺模式還不支援遠征」。
- 選卡（計畫 3）、野外走位與拉遠鏡頭（計畫 4）。所以視覺模式進野外後，選卡要自己選。
- 職業自動偵測（JS 讀網頁）：視覺模式沿用設定裡上次偵測到的職業。
- 戰績（地圖、撿到幾個東西）：視覺模式只記局數。

---

## File Structure

| 檔案 | 責任 |
|---|---|
| `RoHelper/Vision/Frame.cs`（改） | 加 `Half`（縮一半、快取） |
| `RoHelper/Vision/TemplateMatcher.cs`（改） | 大範圍搜尋時先在半尺寸粗找（金字塔） |
| `RoHelper/Vision/TemplateRegion.cs`（新） | 框選範圍 → 範本的搜尋範圍（比例） |
| `RoHelper/Forms/VisionDebugForm.cs`（改） | 存範本時自動設定搜尋範圍、「整張畫面找」、「全部找」 |
| `C:\Ro\tools\gen-schema.js`（新） | 從 `engine.js` 抽出規則清單 → `schema.json` |
| `RoHelper/Engine/schema.json`（新，產生的） | 內建規則清單（視覺模式的面板用） |
| `RoHelper/Engine/BuiltInSchema.cs`（新） | 讀內建的 `schema.json` |
| `RoHelper/Bridge/EngineSchema.cs`（改） | `RuleDef.Default`、`EngineSchema.Stages` |
| `RoHelper/Engine/ConfigDefaults.cs`（新） | C# 版的設定預設值、`applyStored`、`clampTimes` |
| `RoHelper/Bridge/IAutomation.cs`（新） | 面板 ↔ 引擎的共同介面 |
| `RoHelper/Bridge/EngineBridge.cs`（改） | 實作 `IAutomation` |
| `RoHelper/Forms/PanelForm*.cs`（改） | 改用 `IAutomation`；加 `TogglePause()` |
| `RoHelper/Vision/ScreenState.cs`（新） | 一張截圖的辨識結果、範本名稱 |
| `RoHelper/Vision/LobbyFlow.cs`（新） | 大廳流程判斷（純邏輯）＋ `FlowSettings`、`FlowDecision` |
| `RoHelper/Vision/ScreenReader.cs`（新） | 截圖 → `ScreenState` |
| `RoHelper/Vision/VisionEngine.cs`（新） | 視覺模式主迴圈，實作 `IAutomation` |
| `RoHelper/Storage/AppSettings.cs`（改） | `VisionMode` |
| `RoHelper/Program.cs`（改） | 依模式建立引擎 |
| `RoHelper/Forms/GameForm.cs`（改） | 視覺模式不注入 JS、模式切換選單、F8／F9 |
| `RoHelper.Tests/*`（新） | 各 Task 的測試 |

---

### Task 1: 範本比對加速（金字塔粗找）

**Files:**
- Modify: `RoHelper/Vision/Frame.cs`
- Modify: `RoHelper/Vision/TemplateMatcher.cs`
- Test: `RoHelper.Tests/TemplateMatcherTests.cs`、`RoHelper.Tests/FrameTests.cs`

**Interfaces:**
- Produces: `Frame.Half`（`Frame`，快取）；`TemplateMatcher.Find(Frame, Frame, Rectangle)` 簽章不變。

- [ ] **Step 1: 寫失敗的測試**

`RoHelper.Tests/FrameTests.cs` 加：

```csharp
    [Fact]
    public void Half_IsHalfSize_AndCached()
    {
        var f = TestFrames.Pattern();

        Assert.Equal(100, f.Half.Width);
        Assert.Equal(50, f.Half.Height);
        Assert.Same(f.Half, f.Half);
    }
```

`RoHelper.Tests/TemplateMatcherTests.cs` 加：

```csharp
    [Fact]
    public void Find_LargeRegion_UsesPyramid_AndStillExact()
    {
        // 800×600 > 25 萬像素，會走半尺寸粗找；位置刻意用奇數，驗證回到原尺寸後精找得回來
        var f = TestFrames.Pattern(800, 600);
        var t = f.Crop(new Rectangle(457, 331, 60, 40));

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, f.Width, f.Height));

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(457, 331, 60, 40), m.Value.Bounds);
        Assert.Equal(1.0, m.Value.Score, 3);
    }
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter "Half_IsHalfSize_AndCached|Find_LargeRegion_UsesPyramid_AndStillExact"`
Expected: 編譯失敗，`Frame` 沒有 `Half`。

- [ ] **Step 3: 實作**

`RoHelper/Vision/Frame.cs`：在 `private byte[]? _gray;` 下一行加 `private Frame? _half;`，在 `Gray` 屬性下面加：

```csharp
    /// <summary>縮成一半（最近鄰），第一次讀時計算後快取：範本比對的金字塔粗找用，同一張截圖會比好幾個範本</summary>
    public Frame Half => _half ??= Resize(0.5);
```

`RoHelper/Vision/TemplateMatcher.cs` 整個換成：

```csharp
namespace RoHelper.Vision;

/// <summary>範本比對結果。Score＝1 − 平均灰階差／255，1 代表完全一樣</summary>
public readonly record struct MatchResult(Rectangle Bounds, double Score);

/// <summary>
/// 在截圖的指定範圍裡找跟範本最像的位置（灰階、平均絕對差）。
/// 一般範圍：粗找（每 2 點試一個位置、範本每 2 點取 1 點）→ 在最佳點 ±2 逐點精找。
/// 大範圍（超過 <see cref="PyramidArea"/>）：先在縮一半的圖上做上面的找法，再回原尺寸在 ±3 逐點精找。
/// 實測 1904×706 整張找 135×47 的範本要 723 ms，NPC 名字這種會移動的東西只能整張找，所以需要金字塔。
/// 刻意不用 OpenCV：OpenCvSharp 的原生 dll 約 60MB，單一 exe 會大很多。
/// 刻意只比灰階：按鈕的形狀和文字才是辨識重點；需要分辨顏色（例如按鈕反灰）時，由呼叫端另外檢查顏色。
/// </summary>
public static class TemplateMatcher
{
    /// <summary>搜尋範圍超過這個面積（像素）才用金字塔；小範圍直接找比較準，也夠快</summary>
    private const int PyramidArea = 250_000;

    /// <summary>範本太小時縮一半就看不出形狀了，不用金字塔</summary>
    private const int PyramidMinSize = 24;

    public static MatchResult? Find(Frame frame, Frame template, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if ((long)region.Width * region.Height <= PyramidArea || template.Width < PyramidMinSize || template.Height < PyramidMinSize)
            return FindDirect(frame, template, region);

        // 步驟 1：在一半大小的圖上找大概位置；範本縮一半後比範圍大（不太可能）就退回直接找
        var half = new Rectangle(region.X / 2, region.Y / 2, region.Width / 2, region.Height / 2);
        if (FindDirect(frame.Half, template.Half, half) is not { } coarse)
            return FindDirect(frame, template, region);

        // 步驟 2：回原尺寸，在 ±3 逐點精找（縮圖是最近鄰，位置最多偏 1～2 點）
        var near = Rectangle.Intersect(region,
            new Rectangle(coarse.Bounds.X * 2 - 3, coarse.Bounds.Y * 2 - 3, template.Width + 6, template.Height + 6));
        var (bx, by, bd) = Scan(frame, template, near, 1, 1);
        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
    }

    private static MatchResult? FindDirect(Frame frame, Frame template, Rectangle region)
    {
        if (template.Width == 0 || template.Height == 0 || template.Width > region.Width || template.Height > region.Height)
            return null;

        // 步驟 1：粗找
        var (cx, cy, _) = Scan(frame, template, region, 2, 2);

        // 步驟 2：精找（取樣密度不同，分數不能跟粗找比，所以重新算）
        var fine = Rectangle.Intersect(region, new Rectangle(cx - 2, cy - 2, template.Width + 4, template.Height + 4));
        var (bx, by, bd) = Scan(frame, template, fine, 1, 1);
        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
    }

    /// <summary>在 region 裡每隔 step 點試一個位置（範本每 sample 點取 1 點），回傳最小差的位置</summary>
    private static (int X, int Y, double D) Scan(Frame frame, Frame template, Rectangle region, int step, int sample)
    {
        var f = frame.Gray;
        var t = template.Gray;
        var maxX = region.Right - template.Width;
        var maxY = region.Bottom - template.Height;
        var best = (X: region.X, Y: region.Y, D: double.MaxValue);
        for (var y = region.Y; y <= maxY; y += step)
            for (var x = region.X; x <= maxX; x += step)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, sample, best.D);
                if (d < best.D)
                    best = (x, y, d);
            }
        return best;
    }

    /// <summary>
    /// 平均絕對差。累積的差已經超過 limit（目前最佳）就提早回傳 MaxValue：這個位置不可能更好了，省下大部分的計算。
    /// </summary>
    private static double Diff(byte[] f, int fw, byte[] t, int tw, int th, int ox, int oy, int sample, double limit)
    {
        long total = (long)((tw + sample - 1) / sample) * ((th + sample - 1) / sample);
        var cap = limit == double.MaxValue ? long.MaxValue : (long)(limit * total);
        long sum = 0;
        for (var y = 0; y < th; y += sample)
        {
            var fr = (oy + y) * fw + ox;
            var tr = y * tw;
            for (var x = 0; x < tw; x += sample)
                sum += Math.Abs(f[fr + x] - t[tr + x]);
            if (sum > cap)
                return double.MaxValue;
        }
        return (double)sum / total;
    }
}
```

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 29 通過（原 27 ＋ 2），0 失敗。

- [ ] **Step 5: Commit**（git init 之後才做）

```bash
git add RoHelper/Vision/Frame.cs RoHelper/Vision/TemplateMatcher.cs RoHelper.Tests/FrameTests.cs RoHelper.Tests/TemplateMatcherTests.cs
git commit -m "perf: 範本比對大範圍時先在半尺寸粗找"
```

---

### Task 2: 存範本時自動設定搜尋範圍、「全部找」

**Files:**
- Create: `RoHelper/Vision/TemplateRegion.cs`
- Modify: `RoHelper/Forms/VisionDebugForm.cs`
- Test: `RoHelper.Tests/TemplateRegionTests.cs`

**Interfaces:**
- Produces: `TemplateRegion.Around(Rectangle selection, Size frame) → RectangleF`（比例 0～1）

- [ ] **Step 1: 寫失敗的測試**

`RoHelper.Tests/TemplateRegionTests.cs`：

```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public class TemplateRegionTests
{
    [Fact]
    public void Around_InflatesByLongSidePlusFivePercent()
    {
        // 長邊 40 ＋ 畫面長邊 1000 的 5%（50）＝ 每邊擴大 90
        var r = TemplateRegion.Around(new Rectangle(100, 100, 40, 20), new Size(1000, 500));

        Assert.Equal(0.01f, r.X, 3);
        Assert.Equal(0.02f, r.Y, 3);
        Assert.Equal(0.22f, r.Width, 3);
        Assert.Equal(0.40f, r.Height, 3);
    }

    [Fact]
    public void Around_ClampsToFrame()
    {
        var r = TemplateRegion.Around(new Rectangle(0, 0, 10, 10), new Size(100, 100));

        Assert.Equal(new RectangleF(0, 0, 0.25f, 0.25f), r);
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter TemplateRegionTests`
Expected: 編譯失敗，找不到 `TemplateRegion`。

- [ ] **Step 3: 實作 `RoHelper/Vision/TemplateRegion.cs`**

```csharp
namespace RoHelper.Vision;

/// <summary>
/// 存範本時決定搜尋範圍：框選範圍往四周各擴大「框的長邊＋畫面長邊的 5%」，換算成佔截圖的比例。
/// 為什麼要擴大：視窗大小改變時，置中或靠右的 UI 在比例座標上會移動一點；擴大到這個程度，常見的視窗大小變化都還在範圍內，
/// 又比整張找快很多（實測整張找一個按鈕 723 ms）。會到處移動的東西（NPC 名字）由除錯視窗的「整張畫面找」改用整張。
/// </summary>
public static class TemplateRegion
{
    public static RectangleF Around(Rectangle selection, Size frame)
    {
        var pad = Math.Max(selection.Width, selection.Height) + (int)(Math.Max(frame.Width, frame.Height) * 0.05);
        var r = Rectangle.Intersect(Rectangle.Inflate(selection, pad, pad), new Rectangle(Point.Empty, frame));
        return new RectangleF(
            (float)r.X / frame.Width, (float)r.Y / frame.Height,
            (float)r.Width / frame.Width, (float)r.Height / frame.Height);
    }
}
```

- [ ] **Step 4: 執行測試**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter TemplateRegionTests`
Expected: 2 通過。

- [ ] **Step 5: 除錯視窗改用自動範圍，加「整張畫面找」和「全部找」**

`RoHelper/Forms/VisionDebugForm.cs`：

1. 欄位區（`_templates` 那行下面）加：

```csharp
    private readonly CheckBox _wholeFrame = new() { Text = "整張畫面找", AutoSize = true };
```

2. 建構子：`var find = new Button { Text = "找範本", AutoSize = true };` 下面加 `var findAll = new Button { Text = "全部找", AutoSize = true };`，把 `bar.Controls.AddRange([...])` 那行換成：

```csharp
        bar.Controls.AddRange([_live, snap, _clickMode, _selectMode, ocr, _templateName, _wholeFrame, save, _templates, find, findAll]);
```

並在 `find.Click += ...` 下面加：

```csharp
        findAll.Click += (_, _) => FindAllTemplates();
```

3. `SaveTemplate()` 整個換成：

```csharp
    /// <summary>
    /// 存成範本。搜尋範圍：預設是框選範圍往外擴大（<see cref="TemplateRegion.Around"/>）；
    /// 勾「整張畫面找」時用整張，給會到處移動的東西（NPC 名字、交談提示）。門檻用預設值，要調直接改範本旁邊的 JSON。
    /// </summary>
    private void SaveTemplate()
    {
        var name = _templateName.Text.Trim();
        if (_frame is null || _selection is not { } r || name.Length == 0)
        {
            Log("要先框選範圍，並填範本名稱");
            return;
        }
        var region = _wholeFrame.Checked ? new RectangleF(0, 0, 1, 1) : TemplateRegion.Around(r, new Size(_frame.Width, _frame.Height));
        try
        {
            _kit.Templates.Save(name, _frame.Crop(r), region, TemplateLibrary.DefaultThreshold, _kit.Viewport.Dpr);
        }
        catch (ArgumentException ex)
        {
            Log(ex.Message);
            return;
        }
        Log($"已存範本「{name}」（{r.Width}×{r.Height}，搜尋範圍 {region.X:0.00},{region.Y:0.00} {region.Width:0.00}×{region.Height:0.00}）");
        RefreshTemplates();
        _templates.SelectedItem = name;
    }
```

4. `FindTemplate()` 後面加：

```csharp
    /// <summary>每個範本都找一次，列出分數和耗時：校正門檻、確認搜尋範圍夠不夠快</summary>
    private void FindAllTemplates()
    {
        if (_frame is null)
        {
            Log("要先有截圖");
            return;
        }
        var total = Stopwatch.StartNew();
        foreach (var name in _kit.Templates.Names.Order())
        {
            var sw = Stopwatch.StartNew();
            var m = _kit.Templates.Match(_frame, name, _kit.Viewport.Dpr);
            var threshold = _kit.Templates.Get(name)!.Threshold;
            Log(m is { } r
                ? $"  {name}：分數 {r.Score:0.000}／門檻 {threshold:0.00} {(r.Score >= threshold ? "找到" : "沒找到")}，{sw.ElapsedMilliseconds} ms"
                : $"  {name}：範本比搜尋範圍還大");
        }
        Log($"全部找完，共 {total.ElapsedMilliseconds} ms");
    }
```

- [ ] **Step 6: 建置並執行全部測試**

Run: `dotnet build C:\Ro\RoHelper\RoHelper.csproj` → Expected: 0 個錯誤
Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 31 通過

- [ ] **Step 7: Commit**（git init 之後才做）

```bash
git add RoHelper/Vision/TemplateRegion.cs RoHelper/Forms/VisionDebugForm.cs RoHelper.Tests/TemplateRegionTests.cs
git commit -m "feat: 存範本時自動設定搜尋範圍，除錯視窗加全部找"
```

---

### Task 3: 內建規則清單（schema.json）

**Files:**
- Create: `C:\Ro\tools\gen-schema.js`
- Create（產生）: `RoHelper/Engine/schema.json`
- Create: `RoHelper/Engine/BuiltInSchema.cs`
- Modify: `RoHelper/Bridge/EngineSchema.cs`
- Modify: `RoHelper/RoHelper.csproj`、`RoHelper.Tests/RoHelper.Tests.csproj`
- Test: `RoHelper.Tests/BuiltInSchemaTests.cs`

**Interfaces:**
- Produces: `BuiltInSchema.Load() → EngineSchema`；`RuleDef.Default`（`JsonNode?`，JS 的 `def`）；`EngineSchema.Stages`（`List<string>`，JS 的 `DEFAULT_STAGES`；JS 的 hello 訊息沒有這個欄位，所以 JS 模式下是空的）。

- [ ] **Step 1: 寫產生器 `C:\Ro\tools\gen-schema.js`**

```js
// 從 engine.js 抽出規則清單與遊戲資料，輸出成 schema.json（格式同 JS 引擎 hello 訊息的 schema，另加 stages）。
// 用法：node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json
// 改了 engine.js 的 RULES／JOB_SKILLS／ITEMS 等資料後要重新產生；BuiltInSchemaTests 會檢查兩邊有沒有對齊。
const fs = require('fs');
const src = fs.readFileSync(process.argv[2], 'utf8');
const a = src.indexOf('// ================= 遊戲資料');
const b = src.indexOf('// 各種等待時間');
if (a < 0 || b < 0) throw new Error('engine.js 找不到資料區段的標記註解');
const body = src.slice(a, b);
const schema = new Function(body + `
  return { version: '8.0', tabs: TABS, rules: RULES, jobSkills: JOB_SKILLS, jobBase: JOB_BASE,
           deityAttr: DEITY_ATTR, items: ITEMS, skStates: SK_STATES, stages: DEFAULT_STAGES };`)();
process.stdout.write(JSON.stringify(schema, null, 2) + '\n');
```

- [ ] **Step 2: 產生 schema.json**

Run（在 `C:\Ro`）: `node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json`
Expected: 檔案約 13 KB，開頭是 `"version": "8.0"`、`"tabs": ["升級", ...]`。

- [ ] **Step 3: 設定內嵌資源與測試用檔案**

`RoHelper/RoHelper.csproj`：在 `<EmbeddedResource Include="Engine\engine.js" ... />` 下一行加：

```xml
    <EmbeddedResource Include="Engine\schema.json" LogicalName="RoHelper.schema.json" />
```

`RoHelper.Tests/RoHelper.Tests.csproj`：在 `<ProjectReference ...>` 那個 ItemGroup 後面加：

```xml
  <!-- BuiltInSchemaTests 要比對 engine.js 的 RULES 和內建的 schema.json 有沒有對齊 -->
  <ItemGroup>
    <None Include="..\RoHelper\Engine\engine.js" Link="engine.js" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 4: 寫失敗的測試 `RoHelper.Tests/BuiltInSchemaTests.cs`**

```csharp
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RoHelper.Engine;

namespace RoHelper.Tests;

public class BuiltInSchemaTests
{
    [Fact]
    public void Load_HasTabsRulesDefaultsAndStages()
    {
        var s = BuiltInSchema.Load();

        Assert.Equal(["升級", "技能", "道具", "神祇", "走位", "出發", "其他"], s.Tabs);
        Assert.Contains(s.Rules, r => r.Key == "autoEvent" && r.Default is JsonValue v && v.GetValue<bool>());
        Assert.Contains(s.Rules, r => r.Key == "tTick" && r.NoCheck && r.Num == 250);
        Assert.Equal(7, s.Stages.Count);
        Assert.Equal("魔法師", s.JobBase["巫師"]);
    }

    /// <summary>engine.js 改了規則卻忘了重新產生 schema.json 時，這個測試會失敗（產生方式見 tools/gen-schema.js）</summary>
    [Fact]
    public void Rules_MatchEngineJs()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "engine.js"));
        var section = js[js.IndexOf("const RULES = [", StringComparison.Ordinal)..js.IndexOf("const STAGE_BOSS", StringComparison.Ordinal)];
        var keys = Regex.Matches(section, @"\bk:\s*'(\w+)'").Select(m => m.Groups[1].Value).ToList();
        var labels = Regex.Matches(section, @"label:\s*'([^']*)'").Select(m => m.Groups[1].Value).ToList();

        var rules = BuiltInSchema.Load().Rules.Where(r => r.Key is not null).ToList();

        Assert.Equal(keys, rules.Select(r => r.Key!));
        Assert.Equal(labels, rules.Select(r => r.Label));
    }
}
```

- [ ] **Step 5: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter BuiltInSchemaTests`
Expected: 編譯失敗，找不到 `BuiltInSchema`、`RuleDef.Default`、`EngineSchema.Stages`。

- [ ] **Step 6: 實作**

`RoHelper/Bridge/EngineSchema.cs`：

1. `EngineSchema` 類別裡，`SkillStates` 屬性下面加：

```csharp
    /// <summary>預設的遠征地圖清單（JS 的 DEFAULT_STAGES）。只有內建 schema.json 有；JS 的 hello 訊息不帶，JS 模式下是空的</summary>
    public List<string> Stages { get; private init; } = [];
```

2. `Parse` 的物件初始化最後（`SkillStates = ...,` 下面）加：

```csharp
            Stages = ConfigStore.ToStringList(o["stages"]),
```

3. `RuleDef` 類別裡，`NoCheck` 屬性下面加：

```csharp
    /// <summary>勾選或下拉的預設值（JS 的 def）。視覺模式由 C# 補預設值時用</summary>
    public JsonNode? Default { get; private init; }
```

4. `RuleDef.Parse` 的物件初始化最後（`NoCheck = ...,` 下面）加：

```csharp
            Default = r["def"]?.DeepClone(),
```

`RoHelper/Engine/BuiltInSchema.cs`：

```csharp
using System.Reflection;
using System.Text.Json.Nodes;

using RoHelper.Bridge;

namespace RoHelper.Engine;

/// <summary>
/// 內建在 exe 裡的規則清單（Engine/schema.json，由 tools/gen-schema.js 從 engine.js 產生）。
/// 視覺模式不在網頁執行 JS，收不到 JS 的 hello 訊息，面板改用這份產生。
/// 過渡期間 engine.js 和 schema.json 各有一份，由 BuiltInSchemaTests 檢查兩邊一致；計畫 5 拿掉 JS 後只剩這份。
/// </summary>
public static class BuiltInSchema
{
    public static EngineSchema Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoHelper.schema.json")
            ?? throw new InvalidOperationException("找不到內建的 schema.json（檢查 csproj 的 EmbeddedResource）");
        var node = JsonNode.Parse(stream) as JsonObject
            ?? throw new InvalidOperationException("內建的 schema.json 格式錯誤");
        return EngineSchema.Parse(node);
    }
}
```

- [ ] **Step 7: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 33 通過。

- [ ] **Step 8: Commit**（git init 之後才做）

```bash
git add tools/gen-schema.js RoHelper/Engine/schema.json RoHelper/Engine/BuiltInSchema.cs RoHelper/Bridge/EngineSchema.cs RoHelper/RoHelper.csproj RoHelper.Tests/RoHelper.Tests.csproj RoHelper.Tests/BuiltInSchemaTests.cs
git commit -m "feat: 內建規則清單 schema.json，給視覺模式的面板用"
```

---

### Task 4: 設定預設值與修正（C# 版 applyStored）

**Files:**
- Create: `RoHelper/Engine/ConfigDefaults.cs`
- Modify: `RoHelper/Storage/ConfigStore.cs`（`GetNumber` 讀得到 int／long）
- Test: `RoHelper.Tests/ConfigDefaultsTests.cs`、`RoHelper.Tests/ConfigStoreTests.cs`

> 為什麼要改 `ConfigStore.GetNumber`：目前設定都是從 JSON 解析來的（JsonElement），`TryGetValue<decimal>` 讀得到；
> 視覺模式會在 C# 裡直接放數字（例如局數 `cfg["runs"] = 3`），那種 JsonValue 包的是 int，`TryGetValue<decimal>`／`<double>` 都會失敗、回傳預設值 0，局數會永遠停在 1。

**Interfaces:**
- Consumes: `BuiltInSchema.Load()`、`RuleDef.Default`、`EngineSchema.Stages`（Task 3）
- Produces:
  - `ConfigDefaults.Build(EngineSchema) → JsonObject`（JS 的 DEFAULT_CFG）
  - `ConfigDefaults.Apply(JsonObject current, JsonObject? stored, EngineSchema, bool reset) → JsonObject`（JS 的 applyStored，回傳新物件，不改傳入的物件）
  - `ConfigDefaults.ClampTimes(JsonObject cfg, EngineSchema)`（就地修改）

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/ConfigDefaultsTests.cs`**

```csharp
using System.Text.Json.Nodes;

using RoHelper.Engine;

namespace RoHelper.Tests;

public class ConfigDefaultsTests
{
    private static readonly RoHelper.Bridge.EngineSchema Schema = BuiltInSchema.Load();

    [Fact]
    public void Build_HasRuleDefaults()
    {
        var cfg = ConfigDefaults.Build(Schema);

        Assert.True(cfg["autoEvent"]!.GetValue<bool>());
        Assert.False(cfg["autoExpe"]!.GetValue<bool>());
        Assert.Equal(250m, cfg["tTickN"]!.GetValue<decimal>());
        Assert.Equal("屬性附加", cfg["banExtraT"]!.GetValue<string>());
        Assert.Equal(7, cfg["expeList"]!.AsArray().Count);
        Assert.Equal("雷爆術", cfg["skills"]!["巫師"]!["order"]![0]!.GetValue<string>());
        Assert.Equal(0, cfg["runs"]!.GetValue<int>());
    }

    [Fact]
    public void Apply_StoredOverridesDefaults_AndKeepsMissingDefaults()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["autoReturn"] = false }, Schema, reset: false);

        Assert.False(cfg["autoReturn"]!.GetValue<bool>());
        Assert.True(cfg["soloLobby"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_EventAndExpeditionBothOn_TurnsEventOff()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["autoEvent"] = true, ["autoExpe"] = true }, Schema, reset: false);

        Assert.False(cfg["autoEvent"]!.GetValue<bool>());
        Assert.True(cfg["autoExpe"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_ClampsTimes_AndReplacesNonNumbers()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["tTickN"] = 5, ["tPickN"] = "abc" }, Schema, reset: false);

        Assert.Equal(100m, cfg["tTickN"]!.GetValue<decimal>());
        Assert.Equal(350m, cfg["tPickN"]!.GetValue<decimal>());
    }

    [Fact]
    public void Apply_InvalidTab_ResetsToFirstTab()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["tab"] = "不存在" }, Schema, reset: false);

        Assert.Equal("升級", cfg["tab"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_Reset_DropsOldKeys_AndDoesNotTouchInput()
    {
        var current = ConfigDefaults.Build(Schema);
        current["junk"] = 1;

        var cfg = ConfigDefaults.Apply(current, new JsonObject(), Schema, reset: true);

        Assert.False(cfg.ContainsKey("junk"));
        Assert.True(current.ContainsKey("junk"));
    }
}
```

`RoHelper.Tests/ConfigStoreTests.cs`：

```csharp
using System.Text.Json.Nodes;

using RoHelper.Storage;

namespace RoHelper.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void GetNumber_ReadsIntSetInCode()
    {
        var store = new ConfigStore(_path);

        store.Replace(new JsonObject { ["runs"] = 3, ["big"] = 5_000_000_000L });

        Assert.Equal(3m, store.GetNumber("runs"));
        Assert.Equal(5_000_000_000m, store.GetNumber("big"));
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter "ConfigDefaultsTests|ConfigStoreTests"`
Expected: 編譯失敗，找不到 `ConfigDefaults`。（`ConfigStoreTests` 在 Step 3 之後才會單獨失敗：`GetNumber` 回傳 0。）

- [ ] **Step 3: 實作 `RoHelper/Engine/ConfigDefaults.cs`**

```csharp
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
```

`RoHelper/Storage/ConfigStore.cs` 的 `GetNumber` 換成：

```csharp
    /// <summary>
    /// 讀數字。int／long 要分開試：從 JSON 檔解析來的值 TryGetValue&lt;decimal&gt; 讀得到，
    /// 但在 C# 裡直接放的數字（視覺模式的局數 +1），JsonValue 包的是 int，不會幫忙轉成 decimal。
    /// </summary>
    public decimal GetNumber(string key, decimal fallback = 0)
    {
        if (Current[key] is not JsonValue v)
            return fallback;
        if (v.TryGetValue(out decimal d))
            return d;
        if (v.TryGetValue(out int i))
            return i;
        if (v.TryGetValue(out long l))
            return l;
        if (v.TryGetValue(out double f))
            return (decimal)f;
        return fallback;
    }
```

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 40 通過（33 ＋ ConfigDefaults 6 ＋ ConfigStore 1）。

- [ ] **Step 5: Commit**（git init 之後才做）

```bash
git add RoHelper/Engine/ConfigDefaults.cs RoHelper/Storage/ConfigStore.cs RoHelper.Tests/ConfigDefaultsTests.cs RoHelper.Tests/ConfigStoreTests.cs
git commit -m "feat: C# 版設定預設值與修正（移植 applyStored/clampTimes）"
```

---

### Task 5: 面板改用 IAutomation 介面

**Files:**
- Create: `RoHelper/Bridge/IAutomation.cs`
- Modify: `RoHelper/Bridge/EngineBridge.cs`
- Modify: `RoHelper/Forms/PanelForm.cs`、`RoHelper/Forms/PanelForm.Other.cs`（以及其他用到 `_bridge` 的 PanelForm 分檔）

**Interfaces:**
- Produces: `IAutomation`（`Schema`、`SchemaReceived`、`StatusReceived`、`SendPatch`、`SendReplace`）；`PanelForm(ConfigStore, LogStore, RunStore, IAutomation, AppSettings)`；`PanelForm.TogglePause()`。

- [ ] **Step 1: 建立 `RoHelper/Bridge/IAutomation.cs`**

```csharp
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
```

- [ ] **Step 2: `EngineBridge` 實作介面**

`RoHelper/Bridge/EngineBridge.cs`：`public sealed class EngineBridge` 改成 `public sealed class EngineBridge : IAutomation`。其餘不動（成員名稱和簽章已經跟介面一致）。

- [ ] **Step 3: 面板改用介面**

在 `C:\Ro\RoHelper` 執行，把所有 PanelForm 分檔裡的 `_bridge` 改名為 `_automation`：

```bash
sed -i 's/\b_bridge\b/_automation/g' Forms/PanelForm.cs Forms/PanelForm.Other.cs Forms/PanelForm.Lists.cs Forms/PanelForm.Rules.cs
grep -n "_bridge\|EngineBridge" Forms/PanelForm*.cs
```

Expected: grep 只剩 `PanelForm.cs` 的欄位宣告、建構子參數，以及 class summary 裡的 `<see cref="EngineBridge.SendPatch"/>`。

`RoHelper/Forms/PanelForm.cs`：

1. 欄位 `private readonly EngineBridge _automation;` 改成 `private readonly IAutomation _automation;`
2. 建構子參數 `EngineBridge bridge` 改成 `IAutomation automation`，`_automation = bridge;` 改成 `_automation = automation;`
3. class summary 裡的 `<see cref="EngineBridge.SendPatch"/> → JS 改 cfg → 回傳整份 cfg` 改成 `<see cref="IAutomation.SendPatch"/> → 引擎改 cfg → 存回 ConfigStore`。
4. `OnKeyDown` 的 `case Keys.F8:` 裡 `Send("paused", !_config.GetBool("paused"));` 改成 `TogglePause();`
5. `SaveWindowState()` 上面加：

```csharp
    /// <summary>暫停／繼續（F8）。遊戲視窗攔到 F8 時也呼叫這裡，兩個視窗的行為一致</summary>
    public void TogglePause() => Send("paused", !_config.GetBool("paused"));
```

`RoHelper/Program.cs`：`new PanelForm(config, log, runs, bridge, settings)` 不用改（`EngineBridge` 會自動轉成 `IAutomation`）。

- [ ] **Step 4: 建置並執行全部測試**

Run: `dotnet build C:\Ro\RoHelper\RoHelper.csproj` → Expected: 0 個錯誤、0 個警告
Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 40 通過

- [ ] **Step 5: Commit**（git init 之後才做）

```bash
git add RoHelper/Bridge/IAutomation.cs RoHelper/Bridge/EngineBridge.cs RoHelper/Forms/PanelForm*.cs
git commit -m "refactor: 面板改用 IAutomation，準備接視覺引擎"
```

---

### Task 6: 大廳流程判斷（LobbyFlow，純邏輯）

**Files:**
- Create: `RoHelper/Vision/ScreenState.cs`
- Create: `RoHelper/Vision/LobbyFlow.cs`
- Test: `RoHelper.Tests/LobbyFlowTests.cs`

**Interfaces:**
- Produces:
  - `ScreenTemplates`（常數：`Solo`、`Start`、`GoField`、`Return`、`FestivalHost`、`TalkPrompt`、`All`）
  - `record ScreenState(Size FrameSize, double Dpr)`，init 屬性 `Solo`、`Start`、`GoField`、`Return`、`FestivalHost`、`TalkPrompt`（`Rectangle?`）、`Missing`（`IReadOnlyList<string>`）
  - `record FlowSettings(bool Paused, bool SoloLobby, bool AutoEvent, bool AutoExpe, bool AutoReturn, bool RunLimit, int RunLimitN, int Runs, int TStart, int TReturn, int TLobby)`＋`FlowSettings.From(ConfigStore)`
  - `record FlowDecision(string Action, Point? Click = null, bool CountRun = false)`
  - `LobbyFlow.Decide(ScreenState, FlowSettings, long nowMs) → FlowDecision`；`LobbyFlow.WalkTarget(ScreenState, Rectangle label) → Point`

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/LobbyFlowTests.cs`**

```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public class LobbyFlowTests
{
    /// <summary>按鈕位置，中心是 (450, 320)</summary>
    private static readonly Rectangle Btn = new(400, 300, 100, 40);
    private static readonly Point BtnCenter = new(450, 320);

    private static FlowSettings Cfg(bool paused = false, bool autoEvent = true, bool autoExpe = false, bool autoReturn = true,
        bool runLimit = false, int runLimitN = 10, int runs = 0)
        => new(paused, SoloLobby: true, autoEvent, autoExpe, autoReturn, runLimit, runLimitN, runs, TStart: 800, TReturn: 1500, TLobby: 700);

    private static ScreenState Screen() => new(new Size(1000, 600), 1.0);

    [Fact]
    public void Paused_DoesNothing()
    {
        var d = new LobbyFlow().Decide(Screen() with { Start = Btn }, Cfg(paused: true), 0);

        Assert.Equal("已暫停", d.Action);
        Assert.Null(d.Click);
    }

    [Fact]
    public void Solo_ClicksAfterHalfSecond()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Solo = Btn };

        Assert.Null(flow.Decide(s, Cfg(), 0).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 600).Click);
    }

    [Fact]
    public void Return_CountsRunOnce_ThenClicksAfterDelay()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { Return = Btn };

        var first = flow.Decide(s, Cfg(), 0);
        var waiting = flow.Decide(s, Cfg(), 1000);
        var click = flow.Decide(s, Cfg(), 1600);

        Assert.True(first.CountRun);
        Assert.Null(first.Click);
        Assert.False(waiting.CountRun);
        Assert.Null(waiting.Click);
        Assert.False(click.CountRun);
        Assert.Equal(BtnCenter, click.Click);
    }

    [Fact]
    public void Return_GoneThenBack_CountsAgain()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { Return = Btn }, Cfg(), 0);
        flow.Decide(Screen(), Cfg(), 2000);

        Assert.True(flow.Decide(Screen() with { Return = Btn }, Cfg(), 3000).CountRun);
    }

    [Fact]
    public void Start_ThenGoField_EachClickedAfterTStart()
    {
        var flow = new LobbyFlow();

        Assert.Null(flow.Decide(Screen() with { Start = Btn }, Cfg(), 0).Click);
        Assert.Equal(BtnCenter, flow.Decide(Screen() with { Start = Btn }, Cfg(), 900).Click);
        Assert.Null(flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2000).Click);
        var go = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 2900);

        Assert.Equal("準備前往野外", go.Action);
        Assert.Equal(BtnCenter, go.Click);
    }

    [Fact]
    public void GoField_WithoutRecentStart_IsIgnored()
    {
        var flow = new LobbyFlow();

        flow.Decide(Screen() with { GoField = Btn }, Cfg(), 0);
        var d = flow.Decide(Screen() with { GoField = Btn }, Cfg(), 900);

        Assert.Null(d.Click);
        Assert.Equal("待機", d.Action);
    }

    [Fact]
    public void RunLimit_BlocksStart()
    {
        var d = new LobbyFlow().Decide(Screen() with { Start = Btn }, Cfg(runLimit: true, runLimitN: 10, runs: 10), 5000);

        Assert.Null(d.Click);
        Assert.Equal("已達局數上限，停止自動開始", d.Action);
    }

    [Fact]
    public void TalkPrompt_ClicksOnce_RetriesAfterFiveSeconds()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { TalkPrompt = Btn };

        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 0).Click);
        Assert.Null(flow.Decide(s, Cfg(), 1000).Click);
        Assert.Equal(BtnCenter, flow.Decide(s, Cfg(), 5100).Click);
    }

    [Fact]
    public void FestivalHost_WalksTowardLabel_Throttled()
    {
        var flow = new LobbyFlow();
        var s = Screen() with { FestivalHost = new Rectangle(800, 100, 80, 20) };

        var first = flow.Decide(s, Cfg(), 0);

        Assert.Equal("走向節慶主持人", first.Action);
        Assert.NotNull(first.Click);
        Assert.Null(flow.Decide(s, Cfg(), 300).Click);
        Assert.NotNull(flow.Decide(s, Cfg(), 800).Click);
    }

    [Fact]
    public void WalkTarget_LimitsStepToThirtyFivePercentOfShortSide()
    {
        // 畫面中心 (500,300)；目標 (970, 580+45)；一步最多 600×0.35＝210
        var p = LobbyFlow.WalkTarget(Screen(), new Rectangle(950, 560, 40, 20));

        var dist = Math.Sqrt(Math.Pow(p.X - 500, 2) + Math.Pow(p.Y - 300, 2));
        Assert.InRange(dist, 209, 211);
        Assert.True(p.X > 500 && p.Y > 300);
    }

    [Fact]
    public void WalkTarget_ClampsAwayFromEdges()
    {
        // 100×100：中心 (50,50)；目標 (50,140)；走 35 → (50,85)；離下緣至少 20 → (50,80)
        var p = LobbyFlow.WalkTarget(new ScreenState(new Size(100, 100), 1.0), new Rectangle(40, 90, 20, 5));

        Assert.Equal(new Point(50, 80), p);
    }

    [Fact]
    public void NothingFound_ReportsMissingTemplates()
    {
        var d = new LobbyFlow().Decide(Screen() with { Missing = ["交談提示"] }, Cfg(), 0);

        Assert.Equal("缺少範本：交談提示", d.Action);
    }

    [Fact]
    public void Expedition_NotSupportedYet()
    {
        var d = new LobbyFlow().Decide(Screen(), Cfg(autoEvent: false, autoExpe: true), 0);

        Assert.Equal("視覺模式還不支援遠征", d.Action);
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter LobbyFlowTests`
Expected: 編譯失敗，找不到 `LobbyFlow`、`ScreenState`、`FlowSettings`。

- [ ] **Step 3: 實作 `RoHelper/Vision/ScreenState.cs`**

```csharp
namespace RoHelper.Vision;

/// <summary>
/// 大廳流程用到的範本名稱。除錯視窗存範本時要用一模一樣的名字，引擎才找得到。
/// </summary>
public static class ScreenTemplates
{
    /// <summary>「選擇大廳」視窗的「單人大廳」按鈕</summary>
    public const string Solo = "單人大廳";

    /// <summary>節慶主持人對話裡的「開始遊戲」</summary>
    public const string Start = "開始遊戲";

    /// <summary>「前往野外」按鈕</summary>
    public const string GoField = "前往野外";

    /// <summary>結算畫面的「返回角色」</summary>
    public const string Return = "返回角色";

    /// <summary>大廳裡節慶主持人頭上的名字（會跟著畫面移動，存範本時要勾「整張畫面找」）</summary>
    public const string FestivalHost = "節慶主持人";

    /// <summary>走到 NPC 旁邊時出現、點了會開始交談的提示（存範本時要勾「整張畫面找」）</summary>
    public const string TalkPrompt = "交談提示";

    public static readonly string[] All = [Solo, Start, GoField, Return, FestivalHost, TalkPrompt];
}

/// <summary>
/// 一張截圖裡認出來的東西。座標是截圖像素，沒找到是 null。
/// Missing＝範本庫裡還沒存的範本（顯示在面板上，提醒使用者去除錯視窗補）。
/// </summary>
public sealed record ScreenState(Size FrameSize, double Dpr)
{
    public Rectangle? Solo { get; init; }
    public Rectangle? Start { get; init; }
    public Rectangle? GoField { get; init; }
    public Rectangle? Return { get; init; }
    public Rectangle? FestivalHost { get; init; }
    public Rectangle? TalkPrompt { get; init; }
    public IReadOnlyList<string> Missing { get; init; } = [];
}
```

- [ ] **Step 4: 實作 `RoHelper/Vision/LobbyFlow.cs`**

```csharp
using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>大廳流程用到的設定（每一輪從 cfg 讀一次的快照）。時間都是毫秒</summary>
public sealed record FlowSettings(
    bool Paused, bool SoloLobby, bool AutoEvent, bool AutoExpe, bool AutoReturn,
    bool RunLimit, int RunLimitN, int Runs, int TStart, int TReturn, int TLobby)
{
    public static FlowSettings From(ConfigStore c) => new(
        c.GetBool("paused"), c.GetBool("soloLobby"), c.GetBool("autoEvent"), c.GetBool("autoExpe"), c.GetBool("autoReturn"),
        c.GetBool("runLimit"), (int)c.GetNumber("runLimitN", 10), (int)c.GetNumber("runs"),
        (int)c.GetNumber("tStartN", 800), (int)c.GetNumber("tReturnN", 1500), (int)c.GetNumber("tLobbyN", 700));
}

/// <summary>這一輪的決定：顯示在面板上的動作文字、要點的位置（截圖像素，null＝不點）、要不要把局數 +1</summary>
public sealed record FlowDecision(string Action, Point? Click = null, bool CountRun = false);

/// <summary>
/// 大廳流程（視覺模式）：選單人大廳 → 走到節慶主持人 → 交談 → 開始遊戲 → 前往野外；結算時記一局並返回角色。
/// 判斷順序和等待時間照 engine.js 的 step()／lobbyStep()，差別只在「看到什麼」改由截圖辨識（<see cref="ScreenState"/>）提供。
/// 純邏輯：不截圖、不點擊、時間由呼叫端傳入，所以可以單元測試；實際截圖和點擊由 VisionEngine 負責。
/// 刻意不處理遠征（要用 OCR 選地圖、調難度滑桿），之後另寫計畫。
/// </summary>
public sealed class LobbyFlow
{
    /// <summary>「選擇大廳」出現後等多久才按（同 JS：500 ms，等視窗動畫跑完）</summary>
    private const long SoloDelay = 500;

    /// <summary>按「開始遊戲」後多久內出現的「前往野外」算活動流程（同 JS 的 festAt 10 秒；遠征的「前往野外」不能亂按）</summary>
    private const long FestivalWindow = 10_000;

    /// <summary>按了交談提示後，提示還在就隔多久再按一次（點擊沒生效時重試）</summary>
    private const long TalkRetry = 5_000;

    /// <summary>名字標籤底部往下多少 CSS px 是 NPC 腳下（同 JS 的 +45）</summary>
    private const int LabelToFeet = 45;

    /// <summary>點地面時離畫面邊緣至少多少 CSS px（邊緣是 UI，點到會開別的東西）</summary>
    private const int EdgeMargin = 20;

    private long? _soloAt;
    private long? _returnAt;
    private long? _startAt;
    private long? _talkedAt;
    private long _festAt = long.MinValue / 2;
    private long _lastWalk = long.MinValue / 2;

    public FlowDecision Decide(ScreenState s, FlowSettings c, long now)
    {
        if (c.Paused)
            return new("已暫停");

        // 步驟 1：「選擇大廳」→ 等 0.5 秒按「單人大廳」
        if (c.SoloLobby && s.Solo is { } solo)
        {
            _soloAt ??= now;
            if (now - _soloAt < SoloDelay)
                return new("選單人大廳");
            _soloAt = null;
            return new("選單人大廳", Center(solo));
        }
        _soloAt = null;

        // 步驟 2：結算畫面 → 第一次看到時記一局；等 tReturn 後按「返回角色」
        if (s.Return is { } back)
        {
            var first = _returnAt is null;
            _returnAt ??= now;
            if (first)
                _talkedAt = null;
            if (!c.AutoReturn)
                return new("結算中", CountRun: first);
            return new("準備返回角色", now - _returnAt > c.TReturn ? Center(back) : null, first);
        }
        _returnAt = null;

        // 步驟 3：「開始遊戲」／活動的「前往野外」→ 等 tStart 後按
        var limit = c.RunLimit && c.Runs >= c.RunLimitN;
        Rectangle? button = null;
        var isStart = false;
        if (c.AutoEvent && !limit)
        {
            if (s.GoField is { } go && now - _festAt < FestivalWindow)
                button = go;
            else if (s.Start is { } start)
                (button, isStart) = (start, true);
        }
        if (button is { } b)
        {
            var action = isStart ? "準備開始遊戲" : "準備前往野外";
            _startAt ??= now;
            if (now - _startAt <= c.TStart)
                return new(action);
            _startAt = null;
            if (isStart)
                _festAt = now;
            return new(action, Center(b));
        }
        _startAt = null;
        if (limit && (s.GoField is not null || s.Start is not null))
            return new("已達局數上限，停止自動開始");

        // 步驟 4：大廳 → 有交談提示就按；否則看得到節慶主持人就往那邊走
        if (c.AutoExpe)
            return new("視覺模式還不支援遠征");
        if (!c.AutoEvent)
            return new("待機");
        if (limit)
            return new("已達局數上限");
        if (s.TalkPrompt is { } prompt)
        {
            if (_talkedAt is { } t && now - t < TalkRetry)
                return new("和節慶主持人交談");
            _talkedAt = now;
            return new("和節慶主持人交談", Center(prompt));
        }
        if (s.FestivalHost is { } host)
        {
            if (now - _lastWalk < c.TLobby)
                return new("走向節慶主持人");
            _lastWalk = now;
            return new("走向節慶主持人", WalkTarget(s, host));
        }
        return new(s.Missing.Count > 0 ? "缺少範本：" + string.Join("、", s.Missing) : "待機");
    }

    /// <summary>
    /// 往 NPC 走的點擊位置（同 JS lobbyStep 的算法）。鏡頭跟著角色，所以角色當作在畫面正中間；
    /// 目標是名字標籤正下方 45 CSS px（NPC 腳下），一次最多走畫面短邊的 35%，點的位置離畫面邊緣至少 20 CSS px。
    /// 刻意不辨識自己角色的名字：每個玩家的名字不同，沒辦法做成固定範本。
    /// </summary>
    public static Point WalkTarget(ScreenState s, Rectangle label)
    {
        var (w, h) = (s.FrameSize.Width, s.FrameSize.Height);
        double cx = w / 2.0, cy = h / 2.0;
        var dx = label.X + label.Width / 2.0 - cx;
        var dy = label.Bottom + LabelToFeet * s.Dpr - cy;
        var dist = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        var k = Math.Min(1, Math.Min(w, h) * 0.35 / dist);
        var margin = EdgeMargin * s.Dpr;
        return new Point(
            (int)Math.Round(Math.Clamp(cx + dx * k, margin, w - margin)),
            (int)Math.Round(Math.Clamp(cy + dy * k, margin, h - margin)));
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
}
```

- [ ] **Step 5: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 53 通過（40 ＋ 13）。

- [ ] **Step 6: Commit**（git init 之後才做）

```bash
git add RoHelper/Vision/ScreenState.cs RoHelper/Vision/LobbyFlow.cs RoHelper.Tests/LobbyFlowTests.cs
git commit -m "feat: 視覺模式的大廳流程判斷"
```

---

### Task 7: 截圖 → 辨識結果（ScreenReader）

**Files:**
- Create: `RoHelper/Vision/ScreenReader.cs`
- Test: `RoHelper.Tests/ScreenReaderTests.cs`

**Interfaces:**
- Consumes: `TemplateLibrary.Find/Get`（計畫 1）、`ScreenTemplates`、`ScreenState`（Task 6）
- Produces: `ScreenReader(TemplateLibrary)`；`ScreenReader.Read(Frame frame, double dpr, bool lobby) → ScreenState`

- [ ] **Step 1: 寫失敗的測試 `RoHelper.Tests/ScreenReaderTests.cs`**

```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public sealed class ScreenReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));

    /// <summary>Pattern 圖上的白色方塊附近（當作按鈕）</summary>
    private static readonly Rectangle ButtonArea = new(57, 31, 30, 20);

    /// <summary>Pattern 圖上的黑色方塊附近（當作 NPC 名字）</summary>
    private static readonly Rectangle LabelArea = new(15, 65, 25, 20);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private TemplateLibrary Library(params (string Name, Rectangle Area)[] items)
    {
        var lib = new TemplateLibrary(_dir);
        var f = TestFrames.Pattern();
        foreach (var (name, area) in items)
            lib.Save(name, f.Crop(area), new RectangleF(0, 0, 1, 1), 0.9, 1.0);
        return lib;
    }

    [Fact]
    public void Read_FindsSavedButton_AndListsMissing()
    {
        var s = new ScreenReader(Library((ScreenTemplates.Start, ButtonArea))).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.Equal(ButtonArea, s.Start);
        Assert.Contains(ScreenTemplates.Return, s.Missing);
        Assert.DoesNotContain(ScreenTemplates.Start, s.Missing);
    }

    [Fact]
    public void Read_SkipsLobbyTemplates_WhenAButtonIsVisible()
    {
        var lib = Library((ScreenTemplates.Start, ButtonArea), (ScreenTemplates.FestivalHost, LabelArea));

        var s = new ScreenReader(lib).Read(TestFrames.Pattern(), 1.0, lobby: true);

        Assert.NotNull(s.Start);
        Assert.Null(s.FestivalHost);
    }

    [Fact]
    public void Read_FindsLobbyTemplates_OnlyWhenLobbyRequested()
    {
        var reader = new ScreenReader(Library((ScreenTemplates.FestivalHost, LabelArea)));

        Assert.Equal(LabelArea, reader.Read(TestFrames.Pattern(), 1.0, lobby: true).FestivalHost);
        Assert.Null(reader.Read(TestFrames.Pattern(), 1.0, lobby: false).FestivalHost);
    }
}
```

- [ ] **Step 2: 執行，確認失敗**

Run: `dotnet test C:\Ro\RoHelper.Tests --filter ScreenReaderTests`
Expected: 編譯失敗，找不到 `ScreenReader`。

- [ ] **Step 3: 實作 `RoHelper/Vision/ScreenReader.cs`**

```csharp
namespace RoHelper.Vision;

/// <summary>
/// 把一張截圖變成 <see cref="ScreenState"/>：逐一用範本找大廳流程的按鈕。
/// 刻意先找按鈕、都沒有才找 NPC 名字和交談提示：後兩個要整張畫面找（比較花時間），而有按鈕的畫面一定不是在大廳走路。
/// lobby＝false（沒開活動）時也不找後兩個。
/// </summary>
public sealed class ScreenReader
{
    private readonly TemplateLibrary _templates;

    public ScreenReader(TemplateLibrary templates) => _templates = templates;

    public ScreenState Read(Frame frame, double dpr, bool lobby)
    {
        Rectangle? Find(string name) => _templates.Find(frame, name, dpr)?.Bounds;

        var s = new ScreenState(new Size(frame.Width, frame.Height), dpr)
        {
            Solo = Find(ScreenTemplates.Solo),
            Return = Find(ScreenTemplates.Return),
            Start = Find(ScreenTemplates.Start),
            GoField = Find(ScreenTemplates.GoField),
            Missing = ScreenTemplates.All.Where(n => _templates.Get(n) is null).ToList(),
        };
        if (!lobby || s.Solo is not null || s.Return is not null || s.Start is not null || s.GoField is not null)
            return s;
        return s with
        {
            TalkPrompt = Find(ScreenTemplates.TalkPrompt),
            FestivalHost = Find(ScreenTemplates.FestivalHost),
        };
    }
}
```

- [ ] **Step 4: 執行全部測試**

Run: `dotnet test C:\Ro\RoHelper.Tests`
Expected: 56 通過。

- [ ] **Step 5: Commit**（git init 之後才做）

```bash
git add RoHelper/Vision/ScreenReader.cs RoHelper.Tests/ScreenReaderTests.cs
git commit -m "feat: 截圖辨識大廳流程的按鈕"
```

---

### Task 8: 視覺引擎與模式切換、F8／F9

**Files:**
- Create: `RoHelper/Vision/VisionEngine.cs`
- Modify: `RoHelper/Storage/AppSettings.cs`
- Modify: `RoHelper/Program.cs`
- Modify: `RoHelper/Forms/GameForm.cs`

**Interfaces:**
- Consumes: `IAutomation`、`PanelForm.TogglePause()`（Task 5）；`ConfigDefaults`（Task 4）；`BuiltInSchema`（Task 3）；`LobbyFlow`、`FlowSettings`（Task 6）；`ScreenReader`（Task 7）；`VisionKit`（計畫 1）
- Produces: `VisionEngine(ConfigStore, EngineSchema) : IAutomation, IDisposable`、`VisionEngine.Start(VisionKit)`；`AppSettings.VisionMode`；`GameForm(AppSettings, EngineBridge, VisionEngine?, PanelForm)`

- [ ] **Step 1: 實作 `RoHelper/Vision/VisionEngine.cs`**

```csharp
using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>
/// 視覺模式的引擎：每 tTick 毫秒截一張圖 → <see cref="ScreenReader"/> 辨識 → <see cref="LobbyFlow"/> 決定 → CDP 真實點擊。
/// 全程不在遊戲網頁執行任何 JS。設定由這裡擁有（JS 模式是 JS 擁有）：面板的修改、匯入都在這裡套用後存回 <see cref="ConfigStore"/>。
/// 失敗處理：截圖或點擊丟例外時（頁面重新載入中、視窗大小是 0）這一輪跳過，把原因顯示在面板上，下一輪照常再試，不停掉主迴圈。
/// 暫停時連截圖都不做：省 CPU，使用者暫停後畫面完全不受干擾。
/// </summary>
public sealed class VisionEngine : IAutomation, IDisposable
{
    private readonly ConfigStore _config;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly LobbyFlow _flow = new();
    private VisionKit? _kit;
    private ScreenReader? _reader;

    /// <summary>這一輪還沒跑完（截圖＋找範本偶爾會超過間隔），下一輪直接跳過，不重疊</summary>
    private bool _busy;

    /// <summary>上次送給面板的狀態；沒變就不送，避免每 250 ms 重畫一次面板</summary>
    private string _lastStatus = "";

    public VisionEngine(ConfigStore config, EngineSchema schema)
    {
        _config = config;
        Schema = schema;
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public EngineSchema Schema { get; }

    public event Action<EngineSchema>? SchemaReceived;
    public event Action<JsonObject>? StatusReceived;

    /// <summary>WebView2 初始化完成後由遊戲視窗呼叫：補齊設定預設值、通知面板產生分頁、開始主迴圈</summary>
    public void Start(VisionKit kit)
    {
        _kit = kit;
        _reader = new ScreenReader(kit.Templates);
        _config.Replace(ConfigDefaults.Apply(ConfigDefaults.Build(Schema), _config.Current, Schema, reset: false));
        SchemaReceived?.Invoke(Schema);
        _timer.Interval = TickMs();
        _timer.Start();
    }

    public void SendPatch(JsonObject patch)
    {
        var cfg = (JsonObject)_config.Current.DeepClone();
        foreach (var (k, v) in patch)
            cfg[k] = v?.DeepClone();
        ConfigDefaults.ClampTimes(cfg, Schema);
        _config.Replace(cfg);
        UpdateInterval();
    }

    public void SendReplace(JsonObject cfg)
    {
        _config.Replace(ConfigDefaults.Apply(_config.Current, cfg, Schema, reset: true));
        UpdateInterval();
    }

    public void Dispose() => _timer.Dispose();

    private async Task TickAsync()
    {
        if (_busy || _kit is null || _reader is null)
            return;
        _busy = true;
        try
        {
            var settings = FlowSettings.From(_config);
            if (settings.Paused)
            {
                Report("已暫停");
                return;
            }

            // 步驟 1：截圖並辨識
            var frame = await _kit.Grabber.CaptureAsync();
            var dpr = _kit.Viewport.Dpr;
            var screen = _reader.Read(frame, dpr, lobby: settings.AutoEvent);

            // 步驟 2：決定並執行（點擊座標是截圖像素，換成網頁 CSS 座標再送）
            var decision = _flow.Decide(screen, settings, Environment.TickCount64);
            if (decision.CountRun)
                CountRun();
            if (decision.Click is { } p)
            {
                var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
                await _kit.Input.ClickAsync(x, y);
            }
            Report(decision.Action);
        }
        catch (Exception ex)
        {
            Report($"視覺判斷失敗 : {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>結算畫面第一次出現：局數 +1（同 JS）。戰績的地圖、撿到的東西要等計畫 4、5 才讀得到，先只記局數</summary>
    private void CountRun()
    {
        var cfg = (JsonObject)_config.Current.DeepClone();
        cfg["runs"] = (int)_config.GetNumber("runs") + 1;
        _config.Replace(cfg);
    }

    /// <summary>送狀態給面板（格式同 JS 的 status 訊息；尋寶率、下一隻王要等計畫 3、4）</summary>
    private void Report(string action)
    {
        var s = FlowSettings.From(_config);
        var status = new JsonObject
        {
            ["runs"] = s.Runs,
            ["runLimit"] = s.RunLimit ? s.RunLimitN : 0,
            ["treasure"] = 0,
            ["next"] = "",
            ["action"] = "［視覺］" + action,
            ["paused"] = s.Paused,
        };
        var json = status.ToJsonString();
        if (json == _lastStatus)
            return;
        _lastStatus = json;
        StatusReceived?.Invoke(status);
    }

    private int TickMs() => (int)_config.GetNumber("tTickN", 250);

    /// <summary>間隔有變才設：設定 Interval 會讓計時器重新計時，面板每切一次分頁都重設的話主迴圈會被拖慢</summary>
    private void UpdateInterval()
    {
        if (_timer.Interval != TickMs())
            _timer.Interval = TickMs();
    }
}
```

- [ ] **Step 2: `AppSettings` 加 `VisionMode`**

`RoHelper/Storage/AppSettings.cs`：`EngineUpdateUrl` 屬性下面加：

```csharp
    /// <summary>
    /// 視覺模式：不在遊戲網頁注入 JS，只靠截圖判斷、用瀏覽器層級的真實輸入操作。開發中（目前只有大廳流程），預設關閉。
    /// 從遊戲視窗選單切換，重新開啟後生效：兩種模式不能同時跑，否則兩邊會同時點畫面。
    /// </summary>
    public bool VisionMode { get; set; }
```

- [ ] **Step 3: `Program.cs` 依模式建立引擎**

`RoHelper/Program.cs`：

1. using 區加 `using RoHelper.Engine;` 和 `using RoHelper.Vision;`
2. 把

```csharp
        var bridge = new EngineBridge(config, log, runs);
        using var panel = new PanelForm(config, log, runs, bridge, settings);
        Application.Run(new GameForm(settings, bridge, panel));
```

換成：

```csharp
        var bridge = new EngineBridge(config, log, runs);
        // 視覺模式：面板改接視覺引擎；EngineBridge 仍建立，但遊戲視窗不會把它接到網頁上
        using var vision = settings.VisionMode ? new VisionEngine(config, BuiltInSchema.Load()) : null;
        IAutomation automation = vision is null ? bridge : vision;
        using var panel = new PanelForm(config, log, runs, automation, settings);
        Application.Run(new GameForm(settings, bridge, vision, panel));
```

- [ ] **Step 4: `GameForm` 接上視覺模式、模式切換、F8／F9**

`RoHelper/Forms/GameForm.cs`：

1. 欄位：`private readonly PanelForm _panel;` 下面加：

```csharp
    /// <summary>視覺模式的引擎；JS 模式是 null</summary>
    private readonly VisionEngine? _visionEngine;
```

2. 建構子簽章改成 `public GameForm(AppSettings settings, EngineBridge bridge, VisionEngine? visionEngine, PanelForm panel)`，`_panel = panel;` 下面加 `_visionEngine = visionEngine;`。

3. 選單：`menu.Items.Add(new ToolStripMenuItem("視覺除錯", ...));` 下面加：

```csharp
        menu.Items.Add(new ToolStripMenuItem(settings.VisionMode ? "模式：視覺" : "模式：JS", null, (_, _) => ToggleMode()));
```

4. `OnLoad`：在 `core.DocumentTitleChanged += ...;` 這行下面、`_bridge.TogglePanelRequested += TogglePanel;` 上面插入：

```csharp
        // 視覺模式：完全不注入 JS、不接網頁訊息、不檢查引擎更新，只截圖和送輸入
        if (_visionEngine is not null)
        {
            _engineItem.Text = "視覺模式（開發中）";
            _visionEngine.Start(VisionKitFor(core));
            core.Navigate(AppPaths.GameUrl);
            return;
        }
```

5. `CheckUpdateManuallyAsync` 最前面（`if (_engine is null || ...)` 上面）加：

```csharp
        if (_visionEngine is not null)
        {
            MessageBox.Show(this, "視覺模式不使用 JS 引擎，不需要更新。", Text);
            return;
        }
```

6. `OpenVisionDebug()` 裡的 `_vision ??= new VisionKit(core, _webView.Handle, () => _webView.DeviceDpi / 96.0 * _webView.ZoomFactor);` 改成 `var kit = VisionKitFor(core);`，下面的 `new VisionDebugForm(_vision)` 改成 `new VisionDebugForm(kit)`；在 `OpenVisionDebug()` 下面加：

```csharp
    /// <summary>
    /// 視覺工具組（視覺引擎和除錯視窗共用一份）。dpr＝控制項的 DPI 縮放 × 頁面縮放：WebView2 依控制項的 DPI 決定實體像素，
    /// 刻意不從頁面讀 window.devicePixelRatio，那要在頁面執行 JS。
    /// </summary>
    private VisionKit VisionKitFor(CoreWebView2 core)
        => _vision ??= new VisionKit(core, _webView.Handle, () => _webView.DeviceDpi / 96.0 * _webView.ZoomFactor);

    /// <summary>切換 JS／視覺模式。重新開啟後生效：執行中切換的話，已經注入的 JS 拿不掉，兩邊會同時點畫面</summary>
    private void ToggleMode()
    {
        var toVision = !_settings.VisionMode;
        var message = toVision
            ? "切換到視覺模式：不在遊戲網頁注入任何 JS，只看截圖判斷。\n\n目前只會：選單人大廳、走到節慶主持人、開始遊戲、前往野外、返回角色。\n選卡和野外走位還沒做，進野外後要自己選卡。\n\n要切換嗎？重新開啟小幫手後生效。"
            : "切換回 JS 模式（原本的全自動）？重新開啟小幫手後生效。";
        if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _settings.VisionMode = toVision;
        _settings.Save();
    }
```

`OpenVisionDebug()` 的 XML doc 改成 `/// <summary>開視覺除錯視窗（截圖、點擊測試、OCR、存範本、找範本）</summary>`。

7. F8／F9：在 `#region 最小化時繼續掛機` 上面加：

```csharp
    /// <summary>
    /// F8 暫停、F9 面板。焦點在遊戲畫面（WebView2）時，WebView2 控制項會把功能鍵當加速鍵轉給 WinForms 的 ProcessCmdKey，
    /// 所以兩種模式都在這裡處理（視覺模式沒有 JS 可以聽按鍵）。回傳 true＝已處理，網頁不會再收到這個按鍵，
    /// JS 模式下 engine.js 自己的 F8／F9 監聽也就不會重複觸發。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F8:
                _panel.TogglePause();
                return true;
            case Keys.F9:
                TogglePanel();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
```

- [ ] **Step 5: 建置並執行全部測試**

Run: `dotnet build C:\Ro\RoHelper\RoHelper.csproj` → Expected: 0 個錯誤、0 個警告
Run: `dotnet test C:\Ro\RoHelper.Tests` → Expected: 56 通過

- [ ] **Step 6: 檢查視覺模式沒有碰網頁**

Run（在 `C:\Ro\RoHelper`）: `grep -n "ExecuteScript\|AddScriptToExecute\|PostWebMessage\|WebMessageReceived\|Runtime\.\|DOM\." Vision/*.cs`
Expected: 沒有任何輸出。

- [ ] **Step 7: Commit**（git init 之後才做）

```bash
git add RoHelper/Vision/VisionEngine.cs RoHelper/Storage/AppSettings.cs RoHelper/Program.cs RoHelper/Forms/GameForm.cs
git commit -m "feat: 視覺模式引擎、模式切換、F8/F9 由 exe 處理"
```

---

### Task 9: 實機驗證與文件

**Files:**
- Modify: `RoHelper/README.md`
- Modify: 本計畫書（文末加「實機驗證結果」）

- [ ] **Step 1: 使用者實機驗證（JS 模式，確認沒有退步）**

使用者關掉 RoHelper 後執行 `dotnet run --project C:\Ro\RoHelper`，在 JS 模式確認：
1. 焦點在遊戲畫面時按 F8：面板的「暫停」切換**一次**（不是切兩次又變回來）。
2. 按 F9：面板顯示／隱藏。
3. 原本的自動化照常運作。

若第 1 項變成切兩次：表示 WebView2 沒有攔下按鍵，engine.js 也處理了一次 → 停下來回報，改成在 engine.js 拿掉 F8 監聽並發佈新版引擎（需使用者同意）。

- [ ] **Step 2: 使用者重新收集範本（除錯視窗）**

在 JS 模式下按 F8 暫停，開「視覺除錯」，取消「即時更新」，依序到對應畫面框選並存檔（名稱要一字不差）：

| 範本名稱 | 框什麼 | 「整張畫面找」 |
|---|---|---|
| 單人大廳 | 「選擇大廳」視窗的「單人大廳」按鈕 | 不勾 |
| 開始遊戲 | 節慶主持人對話的「開始遊戲」按鈕 | 不勾 |
| 前往野外 | 「前往野外」按鈕 | 不勾 |
| 返回角色 | 結算畫面的「返回角色」按鈕 | 不勾 |
| 節慶主持人 | 大廳裡節慶主持人頭上的名字 | 勾 |
| 交談提示 | 走到節慶主持人旁邊時出現的交談提示 | 勾 |

存完在每個畫面按「全部找」：該畫面的範本要「找到」（分數 ≥ 0.9），其他範本「沒找到」。記下「全部找完，共 N ms」，目標 200 ms 以下。

- [ ] **Step 3: 使用者實機驗證（視覺模式）**

1. 選單「模式：JS」→ 確認切換 → 關掉重開。選單應顯示「模式：視覺」，引擎版本處顯示「視覺模式（開發中）」。
2. 面板有分頁（來自內建規則清單），設定值跟 JS 模式時一樣。
3. 開發者工具（F12）的 Console 輸入 `document.querySelector('#pk-bar')`，Sources 看不到 RoHelper 注入的腳本（確認沒有注入 JS）。這是使用者手動檢查，不影響遊戲。
4. 面板狀態顯示「［視覺］…」；按 F8 變「［視覺］已暫停」，再按一次恢復。
5. 在大廳不動：角色自己走向節慶主持人 → 交談 → 按「開始遊戲」→ 按「前往野外」。
6. 進野外後自己選卡玩完（或讓角色死掉），結算畫面：局數 +1，約 1.5 秒後自動按「返回角色」。
7. 遊戲視窗最小化（移到螢幕外）重複第 5 項，流程照常。

任一項失敗：記下面板狀態文字和除錯視窗「全部找」的結果回報，不要自己猜原因改程式。

- [ ] **Step 4: 切回 JS 模式**

選單「模式：視覺」→ 確認 → 重開，確認原本的自動化恢復。

- [ ] **Step 5: 更新 README**

`RoHelper/README.md` 的「## 視覺判斷（開發中，不碰網頁）」段落，把第二行（`目標：...目前完成第 1 階段...`）換成：

```markdown
目標：完全不在遊戲頁面注入 JS，只看截圖判斷、用瀏覽器層級的真實輸入操作。目前完成第 2 階段：視覺模式能自動跑大廳流程（單人大廳、走到節慶主持人、開始遊戲、前往野外、返回角色）；選卡、野外走位、遠征還是要用 JS 模式。
- 切換模式：遊戲視窗選單「模式：JS／視覺」，重新開啟後生效（存在 `app.json` 的 `VisionMode`）
- F8／F9 由 exe 處理，兩種模式都能用
- 視覺模式需要的範本（名稱要一字不差）：單人大廳、開始遊戲、前往野外、返回角色、節慶主持人、交談提示。後兩個存範本時要勾「整張畫面找」；缺哪個面板會顯示「缺少範本」
- 規則清單：視覺模式用內建的 `Engine/schema.json`。改了 `engine.js` 的規則要重新產生：`node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json`（在 `C:\Ro` 執行；`BuiltInSchemaTests` 會檢查有沒有忘記）
```

- [ ] **Step 6: 計畫書記錄實機結果**

在本計畫書最後加「## 實機驗證結果（日期）」：Step 1～3 每一項的結果、「全部找」的總耗時、遇到的問題與修正。

- [ ] **Step 7: Commit**（git init 之後才做）

```bash
git add RoHelper/README.md docs/superpowers/plans/2026-11-20-vision-lobby-flow.md
git commit -m "docs: 視覺模式大廳流程的說明與實機結果"
```

---

## Self-Review 紀錄

- **涵蓋範圍**：路線表第 2 階段的四項——模式切換（Task 8）、規則清單搬到 C#（Task 3、4）、F8／F9 由 exe 處理（Task 5 的 `TogglePause`＋Task 8 的 `ProcessCmdKey`）、流程按鈕（Task 6、7、8）。另外補了計畫 1 實測發現的範本比對太慢（Task 1、2）。遠征明確排除在「不在這份計畫的範圍」。
- **型別一致性**：`RuleDef.Default`／`EngineSchema.Stages`（Task 3）在 Task 4 使用；`IAutomation`（Task 5）在 Task 8 的 `VisionEngine`、`Program` 使用；`PanelForm.TogglePause`（Task 5）在 Task 8 使用；`ScreenState`／`ScreenTemplates`／`FlowSettings`／`FlowDecision`／`LobbyFlow.Decide`（Task 6）在 Task 7、8 使用；`ScreenReader.Read(Frame, double, bool)`（Task 7）在 Task 8 使用；`VisionKit` 建構子 `(CoreWebView2, IntPtr, Func<double>)` 同計畫 1 實作後的版本。
- **測試數量**：計畫 1 結束時 27；Task 1：＋2＝29；Task 2：＋2＝31；Task 3：＋2＝33；Task 4：＋7＝40；Task 6：＋13＝53；Task 7：＋3＝56。

## 實作時的修正（2026-11-20）

- Task 4 審查：超出 decimal 範圍的數字（例如 1e30）轉型會拋例外 → 夾到上下限（83e14f1）。
- Task 6 審查（使用者同意）：WalkTarget 改成跟 JS 一樣「先算步長再加 45px」；按交談提示後 5 秒內不走路（對應 JS 的 npcDone）；單人大廳等待改成 >500 ms（5459f01）。
- 最終審查：int 溢位會讓 async void 計時器掛掉程式 → `ConfigStore.GetInt` 飽和讀取；F8／F9 改成只在視覺模式由 exe 處理（使用者決定，JS 模式不變）；返回角色漏認一張就重複記局 → 2 秒寬限；短寬範本（NPC 名字）也走金字塔（短邊 ≥ 12）；`LobbyFlow.Reset()` 只清等待中的計時（2ae7412、c0c87ad）。

## 實機驗證結果（2026-11-20）

| 項目 | 結果 |
|---|---|
| 範本收集 | 存了單人大廳、開始遊戲、前往野外、節慶主持人、交談提示；返回角色先跳過。節慶主持人／交談提示存的時候沒勾「整張畫面找」，手動改成整張 |
| 單人大廳範本 | 37 ms，分數 1.000 |
| 視覺模式大廳流程 | 單人大廳 → 走到節慶主持人 → 交談 → 開始遊戲 → 前往野外，全部自動完成 |
| 暫停狀態 | 從 JS 模式暫停後切到視覺模式，重開仍是暫停（設定共用，預期行為）；F8 解除後正常 |
| 尚未驗證 | 返回角色與局數、最小化時的流程、切回 JS 模式、全部找的總耗時、每輪耗時 |
