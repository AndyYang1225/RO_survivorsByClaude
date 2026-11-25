# 技能神祇自動選＋寶箱卡單張重抽 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) 野外看到道具欄的紫色「!」就點開，在「選擇你的神祇」畫面依設定選一張；(2) 寶箱卡都不是要的時，單張重抽「同類抽得出要的」那張，用完改重抽全部，組隊時每畫面最多 3 次。

**Architecture:** 偵測「!」是純像素判斷（新類別 `DeityIconReader`，沿用 `TreasureReader.SlotCenter` 的格子位置），由 `VisionEngine.FieldStepAsync` 點擊。選擇畫面沿用既有選卡流程：`PickerReader` 標記 `PickerInfo.IsDeityPick`，`PickerFlow.Act` 對它走新的 `CardDecider.DecideDeity`。寶箱重抽加在 `CardDecider.Decide`（新的 `PickDo.RerollCard`），由 `PickerFlow` 點單張重抽按鈕、記這個畫面重抽了幾次與這局拿過的道具。

**Tech Stack:** .NET 8 WinForms、xUnit、Windows OCR（既有）。

**設計文件：** `docs/superpowers/specs/2026-11-25-skill-deity-design.md`、`docs/superpowers/specs/2026-11-25-chest-reroll-design.md`

## Global Constraints

- 程式碼識別字英文；註解、XML doc、狀態文字一律繁體中文（遵守 coding-style skill）；中英文、數字之間留半形空格；單一語句的 if 不加大括號。
- 註解寫「為什麼」，實測數據寫進註解（日期 2026-11-25、dpr 1、視窗 1904×682）。
- 程式在執行時會鎖住 bin，跑測試加 `--artifacts-path "$TEMP/rohelper-test"`。`BellFinderTests.Scan_IsCheap` 是純計時測試，遊戲在背景跑時會因 CPU 忙而失敗，跟這次無關：它是唯一失敗時照實回報即可。
- Commit 訊息沿用 repo 風格（`feat 中文說明`），結尾加 `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`；git 沒設定作者，用 `git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit …`。
- 不動、不 stage `RoHelper/Forms/PanelForm.Lists.cs`（使用者自己還沒 commit 的修改）。每次只 `git add` 該 task 列出的檔案。
- 既有行為不能變：既有測試全部要過；新參數都有預設值，預設值等於舊行為（`ChestReroll`、`Party` 在 `PickSettings` 建構子預設 false；`CardDecider.Decide` 新參數預設「沒有分類、不限次數」）。

## 檔案結構

| 檔案 | 動作 | 職責 |
|---|---|---|
| `RoHelper/Vision/Field/DeityIconReader.cs` | 新增 | 找道具欄的紫色「!」 |
| `RoHelper.Tests/Fixtures/技能神祇圖示.png`、`技能神祇選擇.png` | 新增 | 測試圖 |
| `RoHelper.Tests/DeityIconReaderTests.cs` | 新增 | |
| `RoHelper/Vision/Picker/PickerModels.cs` | 修改 | `PickerInfo.IsDeityPick`、`PickDo.RerollCard`、`RunPickState.Owned` |
| `RoHelper/Vision/Picker/PickerReader.cs` | 修改 | 認出神祇選擇畫面 |
| `RoHelper/Vision/Picker/CardDecider.cs` | 修改 | `DecideDeity`、寶箱單張重抽 |
| `RoHelper/Vision/Picker/PickerFlow.cs` | 修改 | 神祇畫面、單張重抽、組隊次數、記拿過的道具、道具分類 |
| `RoHelper/Vision/Picker/PickSettings.cs` | 修改 | `ChestReroll`、`Party` |
| `RoHelper/Vision/VisionEngine.cs` | 修改 | 點「!」、把道具分類交給 PickerFlow |
| `RoHelper/Forms/DevApiHost.cs` | 修改 | `/picker` 預覽也走新邏輯 |
| `RoHelper/Engine/engine.js`、`RoHelper/Engine/schema.json` | 修改 | 新規則 `chestReroll` |
| `RoHelper.Tests/PickerReaderTests.cs`、`CardDeciderTests.cs`、`PickerFlowTests.cs` | 修改 | |
| `RoHelper/README.md` | 修改 | 功能說明 |

---

### Task 1: DeityIconReader（找紫色「!」）

**Files:**
- Create: `RoHelper/Vision/Field/DeityIconReader.cs`
- Create: `RoHelper.Tests/Fixtures/技能神祇圖示.png`（從 `%APPDATA%\RoHelper\shots\api\deity-icon.png` 複製）
- Create: `RoHelper.Tests/Fixtures/技能神祇選擇.png`（從 `%APPDATA%\RoHelper\shots\api\deity-pick3.png` 複製；Task 2 用）
- Test: `RoHelper.Tests/DeityIconReaderTests.cs`

**Interfaces:**
- Consumes: `TreasureReader.SlotCount`（6）、`TreasureReader.SlotCenter(Frame f, double dpr, int i)`（既有，回 `PointF`）
- Produces: `public static PointF? RoHelper.Vision.Field.DeityIconReader.Find(Frame f, double dpr)`：有「!」回那一格的中心（截圖像素），沒有回 null。

- [ ] **Step 1: 複製測試圖**

```bash
cp "$APPDATA/RoHelper/shots/api/deity-icon.png" "RoHelper.Tests/Fixtures/技能神祇圖示.png"
cp "$APPDATA/RoHelper/shots/api/deity-pick3.png" "RoHelper.Tests/Fixtures/技能神祇選擇.png"
```

- [ ] **Step 2: 寫失敗的測試**

建立 `RoHelper.Tests/DeityIconReaderTests.cs`：

```csharp
using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class DeityIconReaderTests
{
    /// <summary>實測（2026-11-25，Lv 21 斐揚森林）：道具欄 5 格道具，第 6 格是白底紫色「!」，中心 (1023, 580)</summary>
    [Fact]
    public void Find_IconInSixthSlot()
    {
        var p = DeityIconReader.Find(CardLayoutTests.Fixture("技能神祇圖示"), 1.0);

        Assert.NotNull(p);
        Assert.InRange(p.Value.X, 1020, 1026);
        Assert.InRange(p.Value.Y, 577, 583);
    }

    /// <summary>其他所有測試圖（野外、大廳、選卡、結算、說明框…）都不能誤認成「!」</summary>
    [Fact]
    public void Find_NoIconInAnyOtherFixture()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var hits = Directory.EnumerateFiles(dir, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != "技能神祇圖示")
            .Where(n => DeityIconReader.Find(CardLayoutTests.Fixture(n!), 1.0) is not null)
            .ToList();

        Assert.Empty(hits);
    }
}
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~DeityIconReaderTests"`
Expected: 編譯失敗，`DeityIconReader` 不存在。

- [ ] **Step 4: 實作**

建立 `RoHelper/Vision/Field/DeityIconReader.cs`：

```csharp
namespace RoHelper.Vision.Field;

/// <summary>
/// 找道具欄的技能神祇「!」（遊戲 2026.11.23 版起：角色約 20 等時，神器右邊的格子變成白底紫色「!」，點開選技能神祇，每局只選一次）。
/// 實測（2026-11-25，dpr 1，視窗 1904×682，Lv 21）：「!」在道具欄第 6 格（中心 1023, 580），格子中央 ±12、每 2 px 取樣：
/// 白色（RGB 都 &gt; 235）155/169 點，紫色筆畫（約 164,133,224）集中在中心左右 2 px 內；
/// 有道具的格子白色 56～82/169（33～49%）、空格白色 0。所以要求白色 ≥ 70%、中央有紫色筆畫、旁邊幾乎沒有紫色。
/// 純像素判斷，每輪都看也很便宜。格子位置沿用 <see cref="TreasureReader.SlotCenter"/>；
/// 道具欄 6 格都滿時「!」會在哪裡還不知道（實測只有 5 格道具＋「!」），所以掃全部 6 格。
/// </summary>
public static class DeityIconReader
{
    /// <summary>格子中心 ±12、每 2 px 取樣（同 TreasureReader 判斷有沒有道具的範圍）</summary>
    private const double SampleHalf = 12;
    private const double SampleStep = 2;

    /// <summary>白色點至少 70%（實測「!」92%、有道具的格子最多 49%）</summary>
    private const double MinWhite = 0.7;

    /// <summary>中央的紫色點至少幾個（實測 11 個）；中央＝離格子中心水平 5 px 內（實測筆畫在 −2～0）</summary>
    private const int MinGlyph = 4;
    private const double GlyphHalfWidth = 5;

    /// <summary>有「!」回那一格的中心（截圖像素）；沒有回 null</summary>
    public static PointF? Find(Frame f, double dpr)
    {
        for (var i = 0; i < TreasureReader.SlotCount; i++)
        {
            var c = TreasureReader.SlotCenter(f, dpr, i);
            if (IsIcon(f, dpr, c))
                return c;
        }
        return null;
    }

    private static bool IsIcon(Frame f, double dpr, PointF c)
    {
        var step = Math.Max(1, (int)Math.Round(SampleStep * dpr));
        var half = (int)Math.Round(SampleHalf * dpr);
        int cx = (int)Math.Round(c.X), cy = (int)Math.Round(c.Y);
        if (cx - half < 0 || cy - half < 0 || cx + half >= f.Width || cy + half >= f.Height)
            return false;
        var glyphHalf = GlyphHalfWidth * dpr;
        int n = 0, white = 0, glyph = 0, offCenter = 0;
        for (var y = cy - half; y <= cy + half; y += step)
        {
            for (var x = cx - half; x <= cx + half; x += step)
            {
                var (r, g, b) = f.At(x, y);
                n++;
                if (r > 235 && g > 235 && b > 235)
                    white++;
                else if (IsPurple(r, g, b))
                {
                    if (Math.Abs(x - cx) <= glyphHalf)
                        glyph++;
                    else
                        offCenter++;
                }
            }
        }
        return white >= MinWhite * n && glyph >= MinGlyph && offCenter <= glyph / 2;
    }

    // 「!」的紫色（實測 164,133,224）：藍最多、紅次之、綠明顯少
    private static bool IsPurple(int r, int g, int b) => b > 150 && r > 110 && g < r - 25 && b > g + 50;
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~DeityIconReaderTests"`
Expected: 2 個都通過。如果 `Find_NoIconInAnyOtherFixture` 列出誤判的測試圖：不要自己調門檻，回報 DONE_WITH_CONCERNS 並附上那些圖名與該格的白色、中央紫色、旁邊紫色點數。

- [ ] **Step 6: 全部測試後 Commit**

```bash
git add RoHelper/Vision/Field/DeityIconReader.cs RoHelper.Tests/DeityIconReaderTests.cs "RoHelper.Tests/Fixtures/技能神祇圖示.png" "RoHelper.Tests/Fixtures/技能神祇選擇.png"
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 找道具欄的技能神祇「!」（純像素）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: 認出神祇選擇畫面（PickerReader）

**Files:**
- Modify: `RoHelper/Vision/Picker/PickerModels.cs`（`PickerInfo` 加屬性）
- Modify: `RoHelper/Vision/Picker/PickerReader.cs`（`ReadAsync` 結尾、新增 regex）
- Test: `RoHelper.Tests/PickerReaderTests.cs`

**Interfaces:**
- Produces: `PickerInfo.IsDeityPick`（`public bool IsDeityPick { get; init; }`，預設 false）。神祇選擇畫面時 `IsChest` 一律 false。

- [ ] **Step 1: 寫失敗的測試**

在 `RoHelper.Tests/PickerReaderTests.cs` 的 `PickerReaderTests` 類別裡（`ReadAsync_NamesAndChest` 之後）加上：

```csharp
    /// <summary>
    /// 技能神祇選擇畫面（2026-11-25 實測，Lv 21 點道具欄紫色「!」）：標題「選擇你的神祇」，4 張「X Deity」卡。
    /// OCR 把卡名讀成「冰箾術 De」（Deity 只剩 De、箭讀成箾），名字比對交給 CardDecider 的 OcrFix
    /// </summary>
    [Fact]
    public async Task ReadAsync_DeityPick()
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture("技能神祇選擇"), 1.0))!;

        Assert.True(p.IsDeityPick);
        Assert.False(p.IsChest);
        Assert.Equal(4, p.Cards.Count);
        Assert.Contains("雷擊術", p.Cards[1].Name);
        Assert.Contains("火焰之壁", p.Cards[3].Name);
    }

    [Theory]
    [InlineData("升級選卡2")]
    [InlineData("升級選卡3")]
    [InlineData("寶箱選卡3")]
    [InlineData("寶箱改版神器")]
    [InlineData("升級改版")]
    public async Task ReadAsync_OtherPickers_NotDeityPick(string fixture)
    {
        var p = (await Reader.ReadAsync(CardLayoutTests.Fixture(fixture), 1.0))!;

        Assert.False(p.IsDeityPick);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~PickerReaderTests"`
Expected: 編譯失敗，`PickerInfo` 沒有 `IsDeityPick`。

- [ ] **Step 3: PickerInfo 加屬性**

`RoHelper/Vision/Picker/PickerModels.cs` 的 `PickerInfo` 裡，在 `SlotsFull` 屬性之後加上：

```csharp

    /// <summary>
    /// 技能神祇選擇畫面（遊戲 2026.11.23 版起，點道具欄紫色「!」打開）：「選擇你的神祇」4 張「X Deity」卡，數字鍵 1–4 選，沒有放逐／專注／重抽。
    /// 每局只選一次，之後神祇自己升級。選法見 <see cref="CardDecider.DecideDeity"/>
    /// </summary>
    public bool IsDeityPick { get; init; }
```

- [ ] **Step 4: PickerReader 認出畫面**

`RoHelper/Vision/Picker/PickerReader.cs` 的 `ReadAsync` 最後一行

```csharp
        return new PickerInfo(isChest, infos, rerollAll, auto, slots);
```

改成：

```csharp
        // 技能神祇選擇畫面：標題「選擇你的神祇」，或至少兩張卡名以 Deity 結尾（OCR 常只讀到「De」，實測 2026-11-25）。不是寶箱
        var isDeity = Has("選擇你的") || infos.Count(c => DeitySuffix().IsMatch(c.Name)) >= 2;
        return new PickerInfo(isChest && !isDeity, infos, rerollAll, auto, slots) { IsDeityPick = isDeity };
```

並在 `SlotCount()` 的 regex 宣告之後加上：

```csharp

    [GeneratedRegex(@"(^|\s)De(ity)?\s*$")]
    private static partial Regex DeitySuffix();
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~PickerReaderTests"`
Expected: 全部通過（含新增的 6 個）。

- [ ] **Step 6: 全部測試後 Commit**

```bash
git add RoHelper/Vision/Picker/PickerModels.cs RoHelper/Vision/Picker/PickerReader.cs RoHelper.Tests/PickerReaderTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 選卡辨識認出技能神祇選擇畫面" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: 技能神祇怎麼選（DecideDeity＋PickerFlow＋預覽）

**Files:**
- Modify: `RoHelper/Vision/Picker/CardDecider.cs`
- Modify: `RoHelper/Vision/Picker/PickerFlow.cs`
- Modify: `RoHelper/Forms/DevApiHost.cs`
- Test: `RoHelper.Tests/CardDeciderTests.cs`、`RoHelper.Tests/PickerFlowTests.cs`

**Interfaces:**
- Consumes: `PickerInfo.IsDeityPick`（Task 2）；`PickSettings.DeityList`／`DeityBan`／`Skills.Order`（既有）
- Produces: `public static PickDecision CardDecider.DecideDeity(PickerInfo p, PickSettings s)`，回 `PickDo.Pick`，Why 為「技能神祇優先：X」／「技能優先：X」／「第一張」。

- [ ] **Step 1: 寫失敗的測試**

`RoHelper.Tests/CardDeciderTests.cs` 類別最後加上：

```csharp
    private static PickerInfo DeityPick(params string[] names)
        => new(false, names.Select((n, i) => Card(i, n, buttons: [])).ToArray(), null) { IsDeityPick = true };

    [Fact]
    public void Deity_FollowsDeityList_IgnoringOcrSuffix()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 De", "火箾術 De"), Settings(Skills(order: ["冰箭術"]), deity: ["火箭術", "雷擊術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "技能神祇優先：火箭術"), d);
    }

    [Fact]
    public void Deity_BannedSkipped_ThenSkillOrder()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 Deity", "火焰之壁 De"),
            Settings(Skills(order: ["火焰之壁", "雷擊術"]), deity: ["冰箭術"], deityBan: ["冰箭術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 2, "技能優先：火焰之壁"), d);
    }

    [Fact]
    public void Deity_NothingMatches_FirstNotBanned()
    {
        var d = CardDecider.DecideDeity(DeityPick("冰箾術 De", "雷擊術 De"), Settings(deityBan: ["冰箭術"]));

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "第一張"), d);
    }
```

`RoHelper.Tests/PickerFlowTests.cs` 類別最後加上：

```csharp
    [Fact]
    public void DeityPick_PressesKeyOfChosenCard_IgnoresGameAuto()
    {
        var flow = new PickerFlow();
        flow.State.GameAutoSkill = true;
        var cards = new[] { "冰箾術 De", "雷擊術 De", "火箾術 De" }
            .Select((n, i) => new CardInfo(i, new Rectangle(i * 240, 40, 214, 352), n, n, 0, false, false, [])).ToArray();
        var deity = new PickerInfo(false, cards, null) { IsDeityPick = true };
        var s = new PickSettings("巫師", true, Mage, false, [], false, "none", true, ["火箭術"], [], false, 1, [], [], [], true, true, true, 2, "treasure",
            true, true, 350, false);
        flow.Waiting(0, 350);

        var early = flow.Act(deity, s, 1500);
        var a = flow.Act(deity, s, 2100);

        Assert.Null(early.Key);
        Assert.Equal("等技能神祇卡片出現…", early.Status);
        Assert.Equal('3', a.Key);
        Assert.Equal("選技能神祇：火箾術 De（技能神祇優先：火箭術）", a.Status);
        Assert.Equal("", flow.State.DeityLock);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~CardDeciderTests|FullyQualifiedName~PickerFlowTests"`
Expected: 編譯失敗，`DecideDeity` 不存在。

- [ ] **Step 3: 實作 DecideDeity**

`RoHelper/Vision/Picker/CardDecider.cs`，在 `Decide` 方法結束之後（`HasTreasure` 的 XML doc 之前）加上：

```csharp

    /// <summary>
    /// 技能神祇選擇畫面（<see cref="PickerInfo.IsDeityPick"/>）的選法，使用者 2026-11-25 確認：
    /// 1. 「神祇」頁該職業的技能神祇優先順序（DeityList），跳過打 ✕ 的；2. 都沒有 → 「技能」頁優先順序；3. 還是沒有 → 第一張（沒打 ✕ 的）。
    /// 每局只選一次（之後神祇自己升級），所以不鎖定、不跟寶箱神器的鎖定（RunPickState.DeityLock）混在一起。
    /// 卡名是「火箭術 Deity」，OCR 常讀成「火箾術 De」：比對前去掉 Deity／De、套用 OcrFix。
    /// </summary>
    public static PickDecision DecideDeity(PickerInfo p, PickSettings s)
    {
        var cards = p.Cards;
        var all = Enumerable.Range(0, cards.Count).ToList();
        bool Is(int i, string w) => w.Length > 0 && DeitySkill(cards[i].Name).Contains(Squash(w));
        var ok = all.Where(i => !s.DeityBan.Any(b => Is(i, b))).ToList();
        if (ok.Count == 0)
            ok = all;
        foreach (var w in s.DeityList)
        {
            var i = ok.FirstOrDefault(i => Is(i, w), -1);
            if (i >= 0)
                return new(PickDo.Pick, i, "技能神祇優先：" + w);
        }
        foreach (var w in s.Skills.Order)
        {
            var i = ok.FirstOrDefault(i => Is(i, w), -1);
            if (i >= 0)
                return new(PickDo.Pick, i, "技能優先：" + w);
        }
        return new(PickDo.Pick, ok[0], "第一張");
    }

    /// <summary>技能神祇卡名去掉結尾的 Deity／De 和空白（「火箾術 De」→「火箭術」）</summary>
    private static string DeitySkill(string name) => Squash(DeitySuffix().Replace(name, ""));
```

並在檔案最後的 regex 宣告區（`Spaces()` 之前）加上：

```csharp
    [GeneratedRegex(@"\s*De(ity)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DeitySuffix();

```

- [ ] **Step 4: PickerFlow 處理神祇畫面**

`RoHelper/Vision/Picker/PickerFlow.cs` 的 `Act` 裡，在「選卡的數字鍵沒反應」那個 `if (_pendingPick is { } pending) { … }` 區塊結束之後、`// 已經按過這種畫面的遊戲「自動」` 註解之前，加上：

```csharp
        // 技能神祇選擇畫面：沒有放逐／專注／重抽，也不受遊戲「自動」影響（自動技能不會幫你選神祇）；用數字鍵選，沒反應同樣會改點卡片。
        // 出現後至少等 DeitySettle 才選，不管 tPick 設多少：卡片是一張張淡入的，實測（2026-11-25）打開 1.5 秒時第 3、4 張還半透明，
        // OCR 把「火焰之壁 Deity」讀成「火壁 Deity」，太早讀會對不上設定的順序。每局只選一次，多等一點沒關係
        if (p.IsDeityPick)
        {
            if (now - (_seenAt ?? now) < DeitySettle)
                return new("等技能神祇卡片出現…");
            var dd = CardDecider.DecideDeity(p, s);
            var chosen = p.Cards[dd.Index!.Value];
            _pendingPick = (dd.Index.Value, chosen.Name, chosen.Bounds);
            _readyAt = now + KeyTimeout;
            return new($"選技能神祇：{chosen.Name}（{dd.Why}）", Key: (char)('1' + dd.Index.Value), Decision: dd);
        }
```

並在 `private const long GameAutoTimeout = 5_000;` 之後加上：

```csharp

    /// <summary>技能神祇選擇畫面出現後至少等多久才選（卡片淡入動畫，見 Act 裡的註解）</summary>
    private const long DeitySettle = 2_000;
```

`Act` 開頭的 `var kind = p.IsChest ? "選寶箱卡" : "選升級卡";` 保持不變（神祇畫面 `IsChest` 是 false，受「升級卡自動選」`PickSkill` 開關控制，跟設計一致）。

- [ ] **Step 5: 開發介面的預覽**

`RoHelper/Forms/DevApiHost.cs` 把

```csharp
        var d = CardDecider.Decide(info, PickSettings.From(config), engine.PickState);
```

改成：

```csharp
        var d = info.IsDeityPick
            ? CardDecider.DecideDeity(info, PickSettings.From(config))
            : CardDecider.Decide(info, PickSettings.From(config), engine.PickState);
```

- [ ] **Step 6: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~CardDeciderTests|FullyQualifiedName~PickerFlowTests"`
Expected: 全部通過。

- [ ] **Step 7: 全部測試後 Commit**

```bash
git add RoHelper/Vision/Picker/CardDecider.cs RoHelper/Vision/Picker/PickerFlow.cs RoHelper/Forms/DevApiHost.cs RoHelper.Tests/CardDeciderTests.cs RoHelper.Tests/PickerFlowTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 技能神祇照神祇頁順序選，沒有再照技能順序" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: 野外點「!」（VisionEngine）

**Files:**
- Modify: `RoHelper/Vision/VisionEngine.cs`

**Interfaces:**
- Consumes: `DeityIconReader.Find`（Task 1）

這個 task 沒有單元測試（VisionEngine 沒有測試環境），建置通過即可；實機驗證由控制者做。

- [ ] **Step 1: 新增欄位**

`VisionEngine.cs` 裡 `private int _treasureSlots = -1;` 的下一行（空一行）加上：

```csharp

    /// <summary>點了技能神祇「!」之後，這麼久之內不再點（等選擇畫面打開，打開後由選卡流程接手）</summary>
    private const long DeityClickGap = 3000;
    private long _deityClickAt = long.MinValue / 2;
```

- [ ] **Step 2: 在走位那一輪點「!」**

`FieldStepAsync` 裡，把

```csharp
        // 讀尋寶率要移滑鼠：躲鐘、撿取倒數時不讀，讀的這一輪不走位
        if (bell is null && !lootPhase && await TreasureAsync(frame, dpr, now))
            return "讀道具欄（尋寶率）";
```

改成：

```csharp
        // 技能神祇：道具欄出現紫色「!」就點開（選擇畫面由選卡流程處理）。躲鐘、撿取倒數時不點，點的這一輪不走位
        if (bell is null && !lootPhase && await DeityIconAsync(frame, dpr, now))
            return "打開技能神祇";
        // 讀尋寶率要移滑鼠：躲鐘、撿取倒數時不讀，讀的這一輪不走位
        if (bell is null && !lootPhase && await TreasureAsync(frame, dpr, now))
            return "讀道具欄（尋寶率）";
```

並在 `TreasureAsync` 方法之前加上：

```csharp
    /// <summary>
    /// 技能神祇「!」（<see cref="DeityIconReader"/>）：有開「升級卡自動選」才點；點了之後 <see cref="DeityClickGap"/> 內不重點。
    /// 有點回 true（這一輪不走位）。
    /// </summary>
    private async Task<bool> DeityIconAsync(Frame frame, double dpr, long now)
    {
        if (!_config.GetBool("pickSkill") || now - _deityClickAt < DeityClickGap || DeityIconReader.Find(frame, dpr) is not { } icon)
            return false;
        _deityClickAt = now;
        var (x, y) = _kit!.Viewport.ToCss(icon.X, icon.Y);
        await _kit.Input.ClickAsync(x, y);
        return true;
    }
```

- [ ] **Step 3: 更新 FieldStepAsync 的說明**

`FieldStepAsync` 的 XML doc 裡，把

```
    /// 接著是讀尋寶率（<see cref="TreasureAsync"/>，躲鐘、撿取倒數時不讀）：有移滑鼠讀的那一輪不走位。
```

改成：

```
    /// 接著是點技能神祇「!」（<see cref="DeityIconAsync"/>）、讀尋寶率（<see cref="TreasureAsync"/>），躲鐘、撿取倒數時都不做：有點或移滑鼠的那一輪不走位。
```

- [ ] **Step 4: 建置、全部測試、Commit**

Run: `dotnet build RoHelper/RoHelper.csproj -c Release --artifacts-path "$TEMP/rohelper-build"` → 0 個錯誤、`VisionEngine.cs` 沒有新警告；再跑全部測試。

```bash
git add RoHelper/Vision/VisionEngine.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 野外看到技能神祇「!」就點開" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: 寶箱重抽的設定與資料（規則、PickSettings、模型）

**Files:**
- Modify: `RoHelper/Engine/engine.js`（RULES）
- Modify: `RoHelper/Engine/schema.json`（重新產生）
- Modify: `RoHelper/Vision/Picker/PickSettings.cs`
- Modify: `RoHelper/Vision/Picker/PickerModels.cs`（`PickDo.RerollCard`、`RunPickState.Owned`）

**Interfaces:**
- Produces:
  - 設定 `chestReroll`（bool，預設 true，升級分頁）
  - `PickSettings` 建構子最後新增 `bool ChestReroll = false, bool Party = false`；`From` 讀 `chestReroll`（沒有這個鍵時當 true）與 `partyRole`（leader／member＝true）
  - `PickDo.RerollCard`
  - `RunPickState.Owned`（`HashSet<string>`，存 `CardDecider.SkillKey(卡名)`）

- [ ] **Step 1: 新增規則**

`RoHelper/Engine/engine.js`，在

```js
    { k: 'autoChest',    label: '道具欄六格都有了以後，寶箱卡按遊戲的「自動寶箱」（不看放逐；關掉的話寶箱卡照下面的規則選：尋寶率、神祇、道具）', def: true, sub: true },
```

的下一行加上：

```js
    // 寶箱卡單張重抽（只有視覺模式用；使用者 2026-11-25）：單張重抽只會換成同一類，所以挑同類有要的東西的那張
    { k: 'chestReroll',  label: '寶箱卡都不是要的（勾選的道具、優先的神器、尋寶率卡、這局拿過的道具）時，單張重抽同類有要的那張，直到出現（單張用完改按重抽全部；組隊時每個畫面最多 3 次）', def: true },
```

重新產生 schema：

```bash
node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json
```

- [ ] **Step 2: PickSettings**

`RoHelper/Vision/Picker/PickSettings.cs`：建構子最後一個參數 `bool AutoChest = true)` 改成：

```csharp
    bool TreasureCap = false, int TreasureCapN = 300, bool HpAfterCap = false, bool AutoChest = true,
    bool ChestReroll = false, bool Party = false)
```

（只改最後一行；前面 `bool TreasureCap = false, …` 那一行原本就是 `bool TreasureCap = false, int TreasureCapN = 300, bool HpAfterCap = false, bool AutoChest = true)`，把它換成上面兩行。）

`From` 裡 `return new PickSettings(` 的最後一個引數

```csharp
            c.Current["autoChest"] is null || c.GetBool("autoChest"));
```

改成：

```csharp
            c.Current["autoChest"] is null || c.GetBool("autoChest"),
            c.Current["chestReroll"] is null || c.GetBool("chestReroll"),
            c.GetString("partyRole", "off") is "leader" or "member");
```

並在 record 的 XML doc 最後一行（`/// 暫停不在這裡判斷：…`）之前加上：

```csharp
/// ChestReroll＝寶箱卡都不是要的時單張重抽（見 CardDecider）；Party＝組隊中（隊長或隊員），組隊時選卡有秒數限制，每個寶箱畫面的重抽次數有上限。
```

- [ ] **Step 3: 模型**

`RoHelper/Vision/Picker/PickerModels.cs`：

`PickDo` 列舉的 `GameAuto,` 之後加上：

```csharp
    /// <summary>按某一張卡的「重抽卡片」（單張重抽，只會換成同一類；目前只用在寶箱卡）</summary>
    RerollCard,
```

`RunPickState` 裡 `public HashSet<string> Focused { get; } = [];` 的下一行加上：

```csharp

    /// <summary>這局小幫手選過的寶箱卡（<see cref="CardDecider.SkillKey"/> 後的卡名）：再出現就是「已經有的道具的強化」，算要的卡（不會為它單張重抽）</summary>
    public HashSet<string> Owned { get; } = [];
```

- [ ] **Step 4: 跑全部測試**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test"`
Expected: 全部通過（`BuiltInSchemaTests.Rules_MatchEngineJs` 確認 schema 跟 engine.js 一致；既有測試不受影響，因為新參數預設 false）。

- [ ] **Step 5: Commit**

```bash
git add RoHelper/Engine/engine.js RoHelper/Engine/schema.json RoHelper/Vision/Picker/PickSettings.cs RoHelper/Vision/Picker/PickerModels.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 寶箱單張重抽的設定與資料（chestReroll、組隊、拿過的道具）" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: 寶箱單張重抽的決策（CardDecider）

**Files:**
- Modify: `RoHelper/Vision/Picker/CardDecider.cs`
- Test: `RoHelper.Tests/CardDeciderTests.cs`

**Interfaces:**
- Consumes: Task 5 的 `PickSettings.ChestReroll`、`PickDo.RerollCard`、`RunPickState.Owned`
- Produces: `CardDecider.Decide(PickerInfo p, PickSettings s, RunPickState st, IReadOnlyList<KeyValuePair<string, List<string>>>? itemGroups = null, Func<int, int>? random = null, int rerollBudget = int.MaxValue)`。`random(n)` 回 0～n−1；`rerollBudget` ≤ 0 時不重抽（寶箱重抽這一段）。

- [ ] **Step 1: 寫失敗的測試**

`RoHelper.Tests/CardDeciderTests.cs`：

先把 `Settings(…)` 輔助方法改成多兩個參數（加在 `bool hpAfterCap = false` 之後），並在建構時傳入：

```csharp
    private static PickSettings Settings(SkillCfg? skills = null, bool autoFocus = false, int autoFocusN = 1, string pickOrder = "treasure",
        bool deityPrio = false, string deityOrder = "none", bool deityLock = false, string[]? deity = null, string[]? deityBan = null,
        string[]? itemPrio = null, string[]? itemBan = null, string[]? itemFocus = null, bool avoidNew = true, bool rerollAll = true,
        bool banishKeep = true, int banishKeepN = 2, bool banExtra = false, string[]? banExtraWords = null,
        bool treasureCap = false, int treasureCapN = 300, bool hpAfterCap = false, bool chestReroll = false)
        => new("巫師", true, skills ?? SkillCfg.Empty, banExtra, banExtraWords ?? [], deityPrio, deityOrder, deityLock, deity ?? [], deityBan ?? [],
            autoFocus, autoFocusN, itemFocus ?? [], itemPrio ?? [], itemBan ?? [], rerollAll, avoidNew, banishKeep, banishKeepN, pickOrder,
            true, true, 350, false, TreasureCap: treasureCap, TreasureCapN: treasureCapN, HpAfterCap: hpAfterCap, ChestReroll: chestReroll);
```

再在類別最後加上：

```csharp
    // ── 寶箱卡單張重抽 ──

    private static readonly List<KeyValuePair<string, List<string>>> Groups =
    [
        new("藥水", ["回復藥水", "冷卻藥水", "尋寶藥水"]),
        new("秘藥", ["傷害秘藥", "驚奇秘藥"]),
        new("祝福", ["傷害祝福", "智慧祝福"]),
    ];

    /// <summary>寶箱卡：有單張重抽（5 次）、專注、放逐</summary>
    private static CardInfo Chest(int i, string name, bool reroll = true, string? text = null)
        => Card(i, name, text: text, buttons: reroll ? [Btn(ActKind.Reroll), Btn(ActKind.Focus), Btn(ActKind.Banish)] : [Btn(ActKind.Focus), Btn(ActKind.Banish)]);

    /// <summary>尋寶率已經超過上限：尋寶卡不算要的</summary>
    private static PickSettings Capped(string[]? itemPrio = null)
        => Settings(itemPrio: itemPrio, treasureCap: true, treasureCapN: 100, chestReroll: true);

    [Fact]
    public void ChestReroll_RerollsCardWhoseCategoryHasWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水"), Chest(2, "傷害祝福")),
            Capped(itemPrio: ["冷卻藥水"]), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.RerollCard, d.Do);
        Assert.Equal(1, d.Index);
    }

    [Fact]
    public void ChestReroll_SeveralHopeful_UsesRandom()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水"), Chest(2, "傷害祝福")),
            Capped(itemPrio: ["冷卻藥水", "智慧祝福"]), Treasure(200), Groups, n => n - 1);

        Assert.Equal(PickDo.RerollCard, d.Do);
        Assert.Equal(2, d.Index);
    }

    [Fact]
    public void ChestReroll_NoHopeful_RandomAmongAll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")),
            Capped(), Treasure(200), Groups, n => n - 1);

        Assert.Equal(new PickDecision(PickDo.RerollCard, 1, "寶箱都不是要的：單張重抽"), d);
    }

    [Fact]
    public void ChestReroll_NoCardReroll_UsesRerollAll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥", reroll: false), Chest(1, "回復藥水", reroll: false)),
            Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(new PickDecision(PickDo.Reroll, null, "寶箱都不是要的：重抽全部"), d);
    }

    [Fact]
    public void ChestReroll_NothingLeft_PicksByRules()
    {
        var p = new PickerInfo(true, [Chest(0, "傷害秘藥", reroll: false), Chest(1, "回復藥水", reroll: false)], Btn(ActKind.Reroll, enabled: false, left: 0));

        var d = CardDecider.Decide(p, Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_FavItemPresent_Picks()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "冷卻藥水")),
            Capped(itemPrio: ["冷卻藥水"]), Treasure(200), Groups, _ => 0);

        Assert.Equal(new PickDecision(PickDo.Pick, 1, "勾選的道具"), d);
    }

    [Fact]
    public void ChestReroll_TreasureCardBelowCap_IsWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "回復藥水"), Chest(1, "尋寶藥水")),
            Settings(treasureCap: true, treasureCapN: 300, chestReroll: true), Treasure(100), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
        Assert.Equal(1, d.Index);
    }

    [Fact]
    public void ChestReroll_TreasureCardAboveCap_NotWanted()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "回復藥水"), Chest(1, "尋寶藥水")),
            Capped(), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.RerollCard, d.Do);
    }

    [Fact]
    public void ChestReroll_OwnedItem_IsWanted()
    {
        var st = Treasure(200);
        st.Owned.Add("回復藥水");

        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水 + 1")), Capped(), st, Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    /// <summary>deityOrder「none」＝神祇步驟不直接選（交給後面），這樣才會走到寶箱重抽的「要的」判斷：優先神器在場就不重抽</summary>
    [Fact]
    public void ChestReroll_PriorityArtifact_IsWanted()
    {
        var s = Settings(deityPrio: true, deityOrder: "none", deity: ["戰爭之神"], treasureCap: true, treasureCapN: 100, chestReroll: true);

        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "戰爭神器", text: "戰爭神器 神器")), s, Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_UpgradeCards_NeverRerollCard()
    {
        var d = CardDecider.Decide(Picker(false, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")), Capped(), Treasure(200), Groups, _ => 0);

        Assert.NotEqual(PickDo.RerollCard, d.Do);
    }

    [Fact]
    public void ChestReroll_Disabled_NoReroll()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")),
            Settings(treasureCap: true, treasureCapN: 100), Treasure(200), Groups, _ => 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }

    [Fact]
    public void ChestReroll_NoBudget_PicksByRules()
    {
        var d = CardDecider.Decide(Picker(true, Chest(0, "傷害秘藥"), Chest(1, "回復藥水")), Capped(), Treasure(200), Groups, _ => 0, rerollBudget: 0);

        Assert.Equal(PickDo.Pick, d.Do);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~CardDeciderTests"`
Expected: 編譯失敗（`Decide` 沒有這些參數）。

- [ ] **Step 3: 實作**

`RoHelper/Vision/Picker/CardDecider.cs`：

`Decide` 的簽章改成：

```csharp
    /// <param name="itemGroups">道具分類（schema 的 Items：藥水、秘藥…）；寶箱單張重抽用它判斷「同類有沒有要的」。null＝不知道分類</param>
    /// <param name="random">隨機函式（回 0～n−1）；null＝Random.Shared。測試傳固定值</param>
    /// <param name="rerollBudget">這個畫面還能重抽幾次（寶箱單張重抽與重抽全部合計；組隊時呼叫端限制）；≤ 0 不重抽</param>
    public static PickDecision Decide(PickerInfo p, PickSettings s, RunPickState st,
        IReadOnlyList<KeyValuePair<string, List<string>>>? itemGroups = null, Func<int, int>? random = null, int rerollBudget = int.MaxValue)
```

（原本的 `public static PickDecision Decide(PickerInfo p, PickSettings s, RunPickState st)` 那一行換成上面這段。）

在 `// 6. 道具／寶箱／其他。` 那行註解**之前**加上：

```csharp
        // 寶箱卡單張重抽（使用者 2026-11-25）：沒有一張是要的 → 單張重抽「同類抽得出要的」那張（單張只會換成同一類，挑錯類永遠抽不到）；
        // 單張用完改按重抽全部；都用完才往下照規則選。升級卡不單張重抽（只會換成同一技能的另一種強化）
        if (p.IsChest && s.ChestReroll && rerollBudget > 0)
        {
            var treasureOff = s.TreasureCap && st.Treasure >= s.TreasureCapN || st.IsEvent;
            if (!pool.Any(i => IsWantedChest(cards[i], s, st, treasureOff)))
            {
                var rerollable = all.Where(i => Usable(cards[i], ActKind.Reroll)).ToList();
                if (rerollable.Count > 0)
                {
                    var hopeful = rerollable.Where(i => CanYieldWanted(cards[i], s, st, treasureOff, itemGroups ?? [])).ToList();
                    var from = hopeful.Count > 0 ? hopeful : rerollable;
                    var next = random ?? (n => Random.Shared.Next(n));
                    return new(PickDo.RerollCard, from[next(from.Count)], hopeful.Count > 0 ? "寶箱都不是要的：單張重抽（同類有要的）" : "寶箱都不是要的：單張重抽");
                }
                if (p.RerollAll is { Usable: true })
                    return new(PickDo.Reroll, null, "寶箱都不是要的：重抽全部");
            }
        }

```

在 `IsFavItem` 方法之後加上：

```csharp

    /// <summary>
    /// 寶箱卡「要的」（使用者 2026-11-25）：勾選的道具、優先的神器（沒打 ✕；已鎖定時只有鎖定的那個）、尋寶率卡（還沒到上限、不是活動）、這局拿過的道具
    /// </summary>
    private static bool IsWantedChest(CardInfo c, PickSettings s, RunPickState st, bool treasureOff)
        => IsFavItem(c, s) || IsWantedArtifact(c, s, st) || !treasureOff && HasTreasure(c) || st.Owned.Contains(SkillKey(c.Name));

    private static bool IsWantedArtifact(CardInfo c, PickSettings s, RunPickState st)
    {
        if (!IsDeity(c) || IsBannedDeity(c, s))
            return false;
        if (s.DeityLock && st.DeityLock.Length > 0)
            return SameDeity(c.Name, st.DeityLock);
        return s.DeityPrio && s.DeityList.Any(w => w.Length > 0 && SameDeity(c.Name, w));
    }

    /// <summary>
    /// 這張卡單張重抽「有機會」抽到要的嗎：單張重抽只會換成同一類（Wiki：同一寶箱欄位的不同裝備），
    /// 所以看這張卡所屬的類別（schema 的道具分類；神器卡是神器類）裡有沒有要的東西。認不出類別回 false
    /// </summary>
    private static bool CanYieldWanted(CardInfo c, PickSettings s, RunPickState st, bool treasureOff, IReadOnlyList<KeyValuePair<string, List<string>>> groups)
    {
        if (IsDeity(c))
            return s.DeityLock && st.DeityLock.Length > 0
                || s.DeityPrio && s.DeityList.Any(w => OcrFix(w).Contains("神器") && !s.DeityBan.Any(b => SameDeity(w, b)))
                || st.Owned.Any(o => o.Contains("神器"));
        var name = Squash(c.Name);
        var names = groups.FirstOrDefault(g => g.Value.Any(n => n.Length > 0 && name.Contains(Squash(n)))).Value;
        if (names is null)
            return false;
        return names.Any(n => s.ItemPrio.Any(w => w.Length > 0 && Squash(n).Contains(Squash(w)))
            || st.Owned.Contains(SkillKey(n))
            || !treasureOff && TreasureItems.Any(t => Squash(n).Contains(t)));
    }
```

- [ ] **Step 4: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~CardDeciderTests"`
Expected: 全部通過（既有的也要過）。

- [ ] **Step 5: 全部測試後 Commit**

```bash
git add RoHelper/Vision/Picker/CardDecider.cs RoHelper.Tests/CardDeciderTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 寶箱卡都不是要的時單張重抽同類有要的那張，用完改重抽全部" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: PickerFlow 執行單張重抽（點擊、組隊次數、拿過的道具、分類）

**Files:**
- Modify: `RoHelper/Vision/Picker/PickerFlow.cs`
- Modify: `RoHelper/Vision/VisionEngine.cs`（`Start` 把分類交給 PickerFlow）
- Modify: `RoHelper/Forms/DevApiHost.cs`（預覽傳分類）
- Test: `RoHelper.Tests/PickerFlowTests.cs`

**Interfaces:**
- Consumes: Task 6 的 `CardDecider.Decide(…, itemGroups, random, rerollBudget)`；Task 5 的 `PickSettings.Party`、`RunPickState.Owned`、`PickDo.RerollCard`
- Produces: `PickerFlow(Func<int, int>? random = null)` 建構子；`public IReadOnlyList<KeyValuePair<string, List<string>>> ItemGroups { get; set; }`（預設空）

- [ ] **Step 1: 寫失敗的測試**

`RoHelper.Tests/PickerFlowTests.cs` 類別最後加上：

```csharp
    private static CardInfo ChestCard(int i, string name)
        => new(i, new Rectangle(i * 240, 40, 214, 352), name, name, 0, false, false,
            [new ActButton(ActKind.Reroll, new Rectangle(i * 240, 400, 40, 30), true, 5), Btn(ActKind.Focus, i * 240 + 50), Btn(ActKind.Banish, i * 240 + 100)]);

    private static PickerInfo ChestPicker(params CardInfo[] cards) => new(true, cards, new ActButton(ActKind.Reroll, new Rectangle(300, 470, 80, 30), true, 5));

    private static PickSettings ChestSettings(bool party)
        => new("巫師", true, Mage, false, [], false, "none", true, [], [], false, 1, [], [], [], true, true, true, 2, "treasure",
            true, true, 350, false, TreasureCap: true, TreasureCapN: 100, ChestReroll: true, Party: party);

    private static PickerFlow ChestFlow()
    {
        var flow = new PickerFlow(_ => 0) { ItemGroups = [new("藥水", ["回復藥水", "冷卻藥水"]), new("秘藥", ["傷害秘藥"])] };
        flow.State.Treasure = 200;
        return flow;
    }

    [Fact]
    public void ChestReroll_ClicksCardRerollButton_AndWaits()
    {
        var flow = ChestFlow();
        flow.Waiting(0, 350);

        var a = flow.Act(ChestPicker(ChestCard(0, "傷害秘藥"), ChestCard(1, "回復藥水")), ChestSettings(party: false), 400);

        Assert.Equal(new Point(20, 415), a.Click);
        Assert.StartsWith("單張重抽：傷害秘藥", a.Status);
        Assert.True(flow.Waiting(750, 350));
        Assert.False(flow.Waiting(850, 350));
    }

    [Fact]
    public void ChestReroll_Party_StopsAfterThreePerScreen_ResetsWhenGone()
    {
        var flow = ChestFlow();
        var picker = ChestPicker(ChestCard(0, "傷害秘藥"), ChestCard(1, "回復藥水"));
        var s = ChestSettings(party: true);
        flow.Waiting(0, 350);

        for (var i = 0; i < 3; i++)
            Assert.Equal(PickDo.RerollCard, flow.Act(picker, s, 400 + i * 1000).Decision!.Do);
        var fourth = flow.Act(picker, s, 4000);
        flow.Gone();
        flow.Waiting(5000, 350);
        // 下一個畫面換成別的卡：第 4 次選走的「傷害秘藥」已經記成拿過的道具（算要的），同一張卡再出現就不會重抽
        var nextScreen = flow.Act(ChestPicker(ChestCard(0, "回復藥水"), ChestCard(1, "冷卻藥水")), s, 5400);

        Assert.Equal(PickDo.Pick, fourth.Decision!.Do);
        Assert.Equal(PickDo.RerollCard, nextScreen.Decision!.Do);
    }

    [Fact]
    public void ChestPick_RemembersOwnedItem()
    {
        var flow = ChestFlow();
        flow.Waiting(0, 350);

        flow.Act(ChestPicker(ChestCard(0, "冷卻藥水 + 1")), ChestSettings(party: false) with { ItemPrio = ["冷卻藥水"] }, 400);

        Assert.Contains("冷卻藥水", flow.State.Owned);
    }
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~PickerFlowTests"`
Expected: 編譯失敗（`PickerFlow` 沒有這個建構子／`ItemGroups`）。

- [ ] **Step 3: 實作 PickerFlow**

`RoHelper/Vision/Picker/PickerFlow.cs`：

1. 類別宣告 `public sealed class PickerFlow` 改成 `public sealed class PickerFlow(Func<int, int>? random = null)`。

2. `private const long GameAutoTimeout = 5_000;` 之後加上：

```csharp

    /// <summary>組隊時每個寶箱畫面最多重抽幾次（單張＋重抽全部）：組隊選卡有秒數限制，連續重抽太多次會來不及（使用者 2026-11-25）</summary>
    private const int PartyChestRerolls = 3;

    /// <summary>這個選卡畫面已經重抽了幾次（<see cref="Gone"/> 歸零）</summary>
    private int _screenRerolls;

    /// <summary>道具分類（schema 的 Items）：寶箱單張重抽判斷「同類有沒有要的」。VisionEngine 啟動時設定</summary>
    public IReadOnlyList<KeyValuePair<string, List<string>>> ItemGroups { get; set; } = [];
```

3. `Gone()` 裡 `_pendingPick = null;` 之後加上 `_screenRerolls = 0;`。

4. `Act` 裡

```csharp
        var d = CardDecider.Decide(p, s, State);
```

改成：

```csharp
        var budget = p.IsChest && s.Party ? Math.Max(0, PartyChestRerolls - _screenRerolls) : int.MaxValue;
        var d = CardDecider.Decide(p, s, State, ItemGroups, random, budget);
```

5. `switch (d.Do)` 裡，把

```csharp
            case PickDo.Reroll:
                _readyAt = now + Settle;
                return new($"重抽全部（{d.Why}）", Click: Center(p.RerollAll!.Bounds), Decision: d);
```

改成：

```csharp
            case PickDo.Reroll:
                if (p.IsChest)
                    _screenRerolls++;
                _readyAt = now + Settle;
                return new($"重抽全部（{d.Why}）", Click: Center(p.RerollAll!.Bounds), Decision: d);
            case PickDo.RerollCard:
                _screenRerolls++;
                _readyAt = now + Settle;
                return new($"單張重抽：{name}（{d.Why}）", Click: Center(p.Cards[d.Index!.Value].Button(ActKind.Reroll)!.Bounds), Decision: d);
```

6. 選卡時記拿過的道具：在

```csharp
        if (CardDecider.SkillOf(picked, s.Skills.After) is { } w)
            State.Picked.Add(w);
```

之前加上：

```csharp
        // 寶箱卡：記下拿過的道具，之後再出現（強化它）算要的卡，不會為它單張重抽
        if (p.IsChest && picked.Name.Length > 0)
            State.Owned.Add(CardDecider.SkillKey(picked.Name));
```

7. 類別的 XML doc 最後一行（`/// 純邏輯：時間由呼叫端傳入，不截圖、不送輸入。`）之前加上：

```csharp
/// 寶箱單張重抽：點那張卡的「重抽卡片」、等 Settle；組隊時每個寶箱畫面最多重抽 <see cref="PartyChestRerolls"/> 次。
```

- [ ] **Step 4: VisionEngine 與預覽傳分類**

`RoHelper/Vision/VisionEngine.cs` 的 `Start` 裡，在 `_reader = new ScreenReader(kit.Templates);` 下一行加上：

```csharp
        _picker.ItemGroups = Schema.Items;
```

`RoHelper/Forms/DevApiHost.cs`，把 Task 3 改過的

```csharp
            : CardDecider.Decide(info, PickSettings.From(config), engine.PickState);
```

改成：

```csharp
            : CardDecider.Decide(info, PickSettings.From(config), engine.PickState, engine.Schema.Items, _ => 0);
```

- [ ] **Step 5: 跑測試確認通過**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~PickerFlowTests"`
Expected: 全部通過（既有的也要過）。

- [ ] **Step 6: 全部測試後 Commit**

```bash
git add RoHelper/Vision/Picker/PickerFlow.cs RoHelper/Vision/VisionEngine.cs RoHelper/Forms/DevApiHost.cs RoHelper.Tests/PickerFlowTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "feat 執行寶箱單張重抽：點按鈕、組隊每畫面最多 3 次、記拿過的道具" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: 文件

**Files:**
- Modify: `RoHelper/README.md`

- [ ] **Step 1: 更新說明**

`RoHelper/README.md`，在

```
- 「學到後放逐」和「一出現就放逐」的卡都放逐過後，會按遊戲的「自動」，這局之後交給遊戲選（面板「選卡 → 規則」可關）。
```

這一行（只比對開頭到「可關）。」，後面的文字保留）所在的條目之後，新增兩行：

```
- 技能神祇（遊戲 2026.11.23 版起）：角色約 20 等時道具欄會出現紫色「!」，小幫手在野外看到就點開，在「選擇你的神祇」照面板「神祇」頁的技能神祇順序選（打 ✕ 的不選），都沒有就照「技能」頁順序，再沒有選第一張。每局只選一次，之後神祇自己升級。跟「升級卡自動選」一起開關。
- 寶箱卡單張重抽：寶箱卡都不是要的（勾選的道具、優先的神器、尋寶率卡、這局拿過的道具）時，挑「同類有要的」那張按「重抽卡片」（單張重抽只會換成同一類），直到出現要的；單張用完改按重抽全部，都用完照規則選。組隊時每個畫面最多重抽 3 次。面板「選卡 → 規則」可關。
```

- [ ] **Step 2: Commit**

```bash
git add RoHelper/README.md
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "modify README 加上技能神祇與寶箱單張重抽說明" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: 自動專注只專注指定的（拿掉「前 N 個」備用做法）

使用者回報（2026-11-25）：沒有設任何「優先＋專注」，小幫手卻去點火箭術的專注。原因是「自動用專注」在技能頁沒有設「優先＋專注」時，會改專注優先順序的前 N 個。使用者決定：只專注指定的（技能頁「優先＋專注」、道具頁 ★），都沒設就不專注。

**Files:**
- Modify: `RoHelper/Vision/Picker/CardDecider.cs`（步驟 3 專注）
- Modify: `RoHelper/Engine/engine.js`（`autoFocus` 規則）、`RoHelper/Engine/schema.json`（重新產生）
- Test: `RoHelper.Tests/CardDeciderTests.cs`、`RoHelper.Tests/PickerFlowTests.cs`

**Interfaces:**
- `PickSettings.AutoFocusN` 保留（設定裡可能還有舊值，`PickSettingsTests` 也讀它），但決策不再使用。

- [ ] **Step 1: 改測試（先紅）**

`RoHelper.Tests/CardDeciderTests.cs`：把 `AutoFocus_FirstNonAfterPriority_Once` 整個換成下面兩個測試：

```csharp
    /// <summary>
    /// 只專注技能頁設成「優先＋專注」的（使用者 2026-11-25：沒設任何專注時小幫手卻去專注第 1 優先的火箭術）。
    /// 每個技能只專注一次
    /// </summary>
    [Fact]
    public void AutoFocus_OnlyMarkedSkills_Once()
    {
        var picker = Picker(false, Card(0, "冰箭術"), Card(1, "火箭術"));
        var settings = Settings(Skills(order: ["火箭術", "冰箭術"], focus: ["冰箭術"]), autoFocus: true);
        var st = new RunPickState();

        var first = CardDecider.Decide(picker, settings, st);
        st.Focused.Add(first.FocusKey!);
        var second = CardDecider.Decide(picker, settings, st);

        Assert.Equal(new PickDecision(PickDo.Focus, 0, "專注：冰箭術", "冰箭術"), first);
        Assert.Equal(PickDo.Pick, second.Do);
    }

    /// <summary>技能頁沒有任何「優先＋專注」：不專注（不再退回專注前 N 個優先技能）</summary>
    [Fact]
    public void AutoFocus_NothingMarked_DoesNotFocus()
    {
        var d = CardDecider.Decide(Picker(false, Card(0, "火箭術")), Settings(Skills(order: ["火箭術", "冰箭術"]), autoFocus: true, autoFocusN: 2), new RunPickState());

        Assert.Equal(PickDo.Pick, d.Do);
    }
```

同一檔案的 `AutoFocus_SkippedWhenFocusButtonUnusable` 裡，`Settings(Skills(order: ["冰箭術"]), autoFocus: true)` 改成 `Settings(Skills(order: ["冰箭術"], focus: ["冰箭術"]), autoFocus: true)`（讓它測的仍然是「按鈕不能按就不專注」）。

`RoHelper.Tests/PickerFlowTests.cs` 的 `Focus_ClicksFocusButton_AndRemembers` 裡，`Settings(Mage, autoFocus: true)` 改成 `Settings(Mage with { Focus = ["冰箭術"] }, autoFocus: true)`。

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test" --filter "FullyQualifiedName~CardDeciderTests|FullyQualifiedName~PickerFlowTests"`
Expected: `AutoFocus_NothingMarked_DoesNotFocus` 失敗（目前會專注火箭術），其他通過。

- [ ] **Step 2: 改決策**

`RoHelper/Vision/Picker/CardDecider.cs` 步驟 3，把

```csharp
            // 3. 專注：前 N 個「優先」技能（不含學到後放逐的），每個只專注一次
            if (s.AutoFocus)
            {
                var fl = s.Skills.Focus.Where(w => s.Skills.Order.Contains(w)).ToList();
                var targets = fl.Count > 0 ? fl : s.Skills.Order.Where(w => !s.Skills.After.Contains(w)).Take(s.AutoFocusN).ToList();
```

改成：

```csharp
            // 3. 專注：只專注技能頁設成「優先＋專注」的，每個只專注一次。
            // 刻意不再「都沒設就專注前 N 個優先技能」（JS 的做法）：使用者 2026-11-25 回報沒設專注卻去專注第 1 優先的火箭術，要的是只專注指定的
            if (s.AutoFocus)
            {
                var targets = s.Skills.Focus.Where(w => s.Skills.Order.Contains(w)).ToList();
```

（後面的 `var f = …`、`if (f >= 0) { … }` 不變。）

同時把類別 XML doc 第 7 行 `/// 順序：1 神祇 → 2 放逐（升級卡）→ 3 技能專注（升級卡）→ …` 保持不變。

- [ ] **Step 3: 改規則文字（拿掉 N）**

`RoHelper/Engine/engine.js` 把

```js
    { k: 'autoFocus',    label: '自動用「專注」（技能分頁設成「優先＋專注」的；都沒設就用前 N 個優先技能）', def: true, num: 1, min: 1, max: 2 },
```

改成：

```js
    { k: 'autoFocus',    label: '自動用「專注」（技能分頁設成「優先＋專注」的技能、道具分頁打 ★ 的道具；都沒設就不專注）', def: true },
```

重新產生：`node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json`

- [ ] **Step 4: 跑全部測試、Commit**

Run: `dotnet test RoHelper.Tests --artifacts-path "$TEMP/rohelper-test"` → 全部通過（`Scan_IsCheap` 例外照實回報）。

```bash
git add RoHelper/Vision/Picker/CardDecider.cs RoHelper/Engine/engine.js RoHelper/Engine/schema.json RoHelper.Tests/CardDeciderTests.cs RoHelper.Tests/PickerFlowTests.cs
git -c user.name=AndyYang1225 -c user.email=sabc6604@gmail.com commit -m "fix 自動專注只專注指定的技能，沒設就不專注" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
