# 視覺模式：野外走位（第一批）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 進到野外後，視覺模式自己走位：讀右上角小地圖找自己、磁鐵、寶箱、王的掉落物，走過去撿；沒目標時在地圖中間附近繞圈；卡住時繞路；開局把鏡頭拉遠。

**Architecture:**
1. `MinimapReader`：截圖 → `MinimapInfo`（自己、磁鐵、寶箱、掉落物、王的小地圖座標，以及地圖本體範圍）。移植 engine.js 的 `readMinimap()`（約 285～311 行），顏色常數照抄，只是從遊戲畫布改成截圖右上角的小地圖區塊。
2. `FieldPlanner`：`MinimapInfo` ＋ `FieldSettings` ＋ 時間 → `FieldDecision`（要點的「世界畫面比例座標」或不動、面板文字）。移植 engine.js 小地圖模式的決策：撿掉落物 → 磁鐵 → 寶箱 → 繞圈／回中間，加上 `checkStuck()` 卡住繞路和「站在目標上 3 秒還在就忽略」。純邏輯，亂數由呼叫端注入。
3. `VisionEngine`：記住「在野外」（按了「前往野外」到看到結算／大廳按鈕之間），在野外、沒有選卡、沒有大廳按鈕時跑走位；開局拉遠鏡頭一次。
4. 開發介面 `GET /field`：讀小地圖＋預覽決策（不點），給 Claude 校正用。

**Tech Stack:** .NET 8 WinForms、C# 12、xUnit 2.9.2

## Global Constraints

- 全程不在遊戲網頁注入或執行 JS；CDP 只允許 `Input.*`。JS 引擎已停用，不要恢復。
- 移植以 `RoHelper/Engine/engine.js` 為準（`readMinimap`、`clusters`、`checkStuck`、`wanderStep`、`mapStep` 裡「小地圖模式」那段），面板文字（`st.action`）要一致：`撿王的掉落物`、`前往磁鐵`、`前往寶箱`、`回地圖中間`、`繞圈`、`卡住了，繞路`。
- **刻意不做（第二批）**：節慶扭蛋（JS 讀遊戲資料）、精確模式（JS 讀遊戲資料）、吃肉、躲鐘、王死掉的地點（要 OCR 小地圖座標）、MVP 倒數撿取階段、`farLimit`（JS 在小地圖模式本來就沒用）、`chestMoney`（要 OCR 金錢；第一批一律當買得起）、`lowHpStop`。在 `FieldSettings`／`FieldPlanner` 的 summary 寫明。
- 座標：小地圖座標是**截圖像素**；點擊用「世界畫面比例」`(fx, fy)`（0～1，同 JS 的 `clickWorld`，夾在 0.05～0.95），世界畫面＝整張截圖（野外時遊戲畫布佔滿視窗），換成截圖像素 `(fx·W, fy·H)` 再用 `ViewportMapper.ToCss`。
- 小地圖顏色常數（JS `COL`）：磁鐵 (255,59,74) ±12、自己 (192,26,43) ±10、寶箱 (255,200,60) ±12、掉落物外框 (31,50,92) ±14、MVP (255,42,42) ±14、小頭目 (255,154,46) ±14。分群：距離 ≤ 4 px 歸同一群，最少點數：自己 3、磁鐵 4、寶箱 3、王 4；掉落物照 JS 的外框規則（同群 ≥ 4 點、不在磁鐵 7 px 內）。
- 實測（2026-11-20，dpr 1，視窗 1904×682）：小地圖面板在右上角，x≈1731～1891、y≈12～197；標題列（地名＋座標）y≈12～37；地圖本體 y≈40～170；**下方按鈕列 y≈172～190（統計按鈕的深藍色會被誤認成掉落物，一定要排除）**。field4：自己的群中心≈(1812,131)、寶箱群中心≈(1806,129)。field2：自己≈(1811,130)、沒有寶箱。人物在畫面中央附近。
- 不新增 NuGet 套件。寫作風格依 `coding-style` skill（同前幾個計畫）。
- 測試：RoHelper.exe 執行中時 `dotnet test` 加 `--artifacts-path <暫存資料夾>`，不要關掉程式。分支 `vision-field`，每個 Task 一個 commit，結尾 `Co-Authored-By:` 行。

## 不在這份計畫的範圍

見 Global Constraints「刻意不做（第二批）」；另外遠征、職業偵測也不在這裡。

---

## File Structure

| 檔案 | 責任 |
|---|---|
| `RoHelper.Tests/Fixtures/野外1.png`、`野外2寶箱.png`（複製） | 真實野外截圖（field2、field4） |
| `RoHelper/Vision/Field/MinimapReader.cs`（新） | `MinimapBlob`、`MinimapInfo`、`MinimapReader` |
| `RoHelper/Vision/Field/FieldSettings.cs`（新） | 從 cfg 讀走位設定 |
| `RoHelper/Vision/Field/FieldPlanner.cs`（新） | 走位決策與狀態（卡住、忽略清單、繞圈角度） |
| `RoHelper/Vision/VisionEngine.cs`（改） | 在野外狀態、走位、拉遠鏡頭 |
| `RoHelper/DevApi/DevApiRouter.cs`、`RoHelper/Forms/DevApiHost.cs`（改） | `GET /field` |

---

### Task 1: 小地圖判讀（MinimapReader）

**Files:** Create `RoHelper/Vision/Field/MinimapReader.cs`；複製 fixtures（`%AppData%\RoHelper\shots\api\field2.png` → `RoHelper.Tests/Fixtures/野外1.png`、`field4.png` → `野外2寶箱.png`）；Test `RoHelper.Tests/MinimapReaderTests.cs`

- [ ] **Step 1：測試**（先失敗）
  - `野外2寶箱`：`Me` 不是 null，中心在 (1812±3, 131±3)；`Chests` 恰好 1 群，中心在 (1806±3, 129±3)；`Loot` 空（按鈕列已排除）；`Magnets` 空；`Map` 的寬高都 > 100。
  - `野外1`：`Me` 中心在 (1811±3, 130±3)；`Chests` 空；`Loot` 空。
  - 純色畫面（`TestFrames.Solid(1904, 682, 20, 25, 40)`）→ 回 null（找不到自己）。
  - 用合成畫面測顏色與分群：在 1904×682 的深色畫面上，在小地圖區內畫一個 3×3 的自己色塊和一個 2×2 的寶箱色塊 → `Me` 有、`Chests` 也有（2×2＝4 點 ≥ 3）；只畫 1 點的磁鐵色 → `Magnets` 空。
- [ ] **Step 2：實作**
  - `public sealed record MinimapBlob(double X, double Y, int N);`（截圖像素中心、點數）
  - `public sealed record MinimapInfo(MinimapBlob Me, IReadOnlyList<MinimapBlob> Magnets, IReadOnlyList<MinimapBlob> Chests, IReadOnlyList<MinimapBlob> Loot, IReadOnlyList<MinimapBlob> Bosses, RectangleF Map);`
  - `public static class MinimapReader { public static MinimapInfo? Read(Frame f, double dpr); internal static Rectangle Area(Frame f, double dpr); }`
  - `Area`：右上角固定區塊，`x = W − 173·dpr`、`y = 40·dpr`、寬 `158·dpr`、高 `130·dpr`（只含地圖本體，不含標題列和按鈕列），夾在畫面內。summary 寫為什麼不用找的（面板固定在右上角，按鈕列一定要排除）。
  - 掃描：依序判斷 自己 → 磁鐵 → 寶箱 → 掉落物外框 → 王（MVP 或小頭目），同 JS 的 else-if 順序；`Clusters(points, minSize)` 照 JS（逐點找 4 px 內的群，更新平均）。
  - `Map`：區塊內 `r+g+b > 45` 的像素外框（同 JS）；沒有就用整個區塊。
  - 掉落物：照 JS（外框點逐點歸群 ≤ 6 px、每群 ≥ 4 點、離任何磁鐵 ≥ 7 px）。
  - 找不到自己回 null。
- [ ] **Step 3：測試全過 → commit `feat: 野外小地圖判讀`**

---

### Task 2: 走位決策（FieldSettings、FieldPlanner）

**Files:** Create `RoHelper/Vision/Field/FieldSettings.cs`、`FieldPlanner.cs`；Test `RoHelper.Tests/FieldPlannerTests.cs`

- `public sealed record FieldSettings(bool SeekLoot, bool SeekMagnet, bool SeekChest, bool AutoWander, bool StayCenter, bool ZoomOut, int SeekEvery, int WanderEvery)`；`From(ConfigStore c)` 讀 `seekLoot`、`seekMagnet`、`seekChest`、`autoWander`、`stayCenter`、`zoomOut`、`tSeekN`（預設 1500）、`tWanderN`（預設 1500）。
- `public sealed record FieldDecision(string Status, PointF? Click);`（Click＝世界畫面比例 0～1；null＝這輪不點）
- `public sealed class FieldPlanner(Func<double> random)`：
  - 狀態（同 JS 的 `st`）：`_lastMove`、`_lastAct`、`_angle`、`_anchor (x,y,t)`、`_stuckN`、`_escAng`、`_escUntil`、`_blocked (x,y,until)`、`_lootNear`。
  - `public void NewRun()`：全部重設。
  - `public FieldDecision Step(MinimapInfo? mm, FieldSettings s, long now)`，順序：
    1. 五個開關都關 → `("", null)`。
    2. `now − _lastMove < SeekEvery` → `(_lastAct, null)`。
    3. 清掉過期的 `_blocked`。
    4. `_escUntil > now` → `"卡住了，繞路"`，點 `(0.5 + cos(escAng)·0.3, 0.5 + sin(escAng)·0.3)`，`_lastMove = now`。
    5. `mm` 是 null → 交給繞圈（`Wander(null, …)`）。
    6. 掉落物（SeekLoot）：最近且不在忽略範圍的；`len < 3` 持續 3 秒 → 加入忽略 30 秒；`CheckStuck` 為真 → 回卡住決策；否則 `"撿王的掉落物"`，`k = min(0.30, 0.06 + len·0.03)`，點 `(0.5 + dx/len·k, 0.5 + dy/len·k)`。
    7. 磁鐵優先於寶箱（SeekMagnet、SeekChest），文字 `"前往磁鐵"`／`"前往寶箱"`，k 同上。
    8. 繞圈 `Wander`：`AutoWander` 關 → `("", null)`；`CheckStuck(me, null, null)`；`now − _lastMove ≤ WanderEvery` → `(_lastAct or "繞圈", null)`；`StayCenter` 且地圖寬 > 20 且離中心 > 地圖長邊 12% → `"回地圖中間"`，`k = min(0.30, 0.12 + d/長邊·0.6)`；否則 `"繞圈"`：`_angle += π/3 + (rand−0.5)·0.6`，`rad = 0.32·(0.6 + rand·0.4)·(StayCenter ? 0.7 : 1)`，點 `(0.5 + cos·rad, 0.5 + sin·rad·0.8)`。
  - `CheckStuck` 照 JS 699～716 行（anchor 移動 > 0.8 px 重設；1.5 秒沒動且這期間有點過 → 側閃或回頭、`_escUntil = now + 1300`、第 3 次把目標加入忽略 20 秒）。注意小地圖座標是截圖像素（dpr 1 時跟 JS 的小地圖畫布像素同尺度；註解寫這個假設）。
  - 所有點擊座標夾在 0.05～0.95。
- [ ] **Step 1：測試**（random 固定回 0.5）：
  - 有寶箱 → `"前往寶箱"`，點的方向跟寶箱方向一致（dx、dy 同號）。
  - 同時有磁鐵和寶箱 → 去磁鐵；有掉落物 → 去掉落物。
  - SeekEvery 內第二次呼叫 → 不點、文字沿用。
  - 沒目標、離中心遠 → `"回地圖中間"` 且方向朝中心；在中心附近 → `"繞圈"`。
  - 卡住：連續呼叫但 `Me` 不動，1.5 秒後 → `"卡住了，繞路"`，之後 1.3 秒內都繞路。
  - 站在掉落物上 3 秒 → 之後不再追那個點（改繞圈）。
  - `NewRun` 後狀態重設。
  - `FieldSettings.From`：預設值、`tSeekN` 讀得到。
- [ ] **Step 2：實作 → 測試全過 → commit `feat: 野外走位決策（移植 JS 小地圖模式）`**

---

### Task 3: 接進視覺引擎

**Files:** Modify `RoHelper/Vision/VisionEngine.cs`（必要時 `LobbyFlow.cs`）；Test：能抽成純邏輯的部分（例如 `FieldGate`：在野外狀態的進出）加測試

- [ ] **Step 1：在野外狀態**（小類別 `FieldGate`，純邏輯可測）：`Enter(now)`＝按了「前往野外」（`decision.StartsRun`）；`Leave()`＝`CountRun`、或畫面上認出 Solo／Start／FestivalHost／Return 任一範本；`InField`；`Since`（進野外的時間）。
- [ ] **Step 2：VisionEngine**
  - 每輪：選卡優先（不變）→ 辨識大廳畫面 → `FieldGate` 更新 → 若 `InField` 且畫面上沒有任何大廳按鈕：`zoomOut` 開著且這局還沒拉過、進野外滿 3 秒 → 在畫面中心 `WheelAsync(x, y, 300)` 三次（間隔不用等），記住已拉過；然後 `MinimapReader.Read` → `FieldPlanner.Step` → 有 Click 就換成截圖像素 → `ToCss` → `ClickAsync`；面板顯示 `"走位：" + Status`。
  - 不在野外 → 照舊跑 `LobbyFlow`（大廳流程）。在野外時 LobbyFlow 仍要跑（才看得到結算的「返回角色」），但它的 `Action` 是「待機」時改顯示走位狀態。
  - 新的一局（`StartsRun`、`CountRun`）→ `FieldPlanner.NewRun()`、重設「已拉過鏡頭」。
  - 暫停時不走位（暫停本來就整輪跳過）。
- [ ] **Step 3：確認 `CdpInput.WheelAsync` 的 deltaY 正負號＝拉遠**（看 JS 1358 行：deltaY 300＝拉遠）。
- [ ] **Step 4：測試全過 → commit `feat: 視覺模式野外走位`**

---

### Task 4: 開發介面 `GET /field`

**Files:** Modify `RoHelper/DevApi/DevApiRouter.cs`、`RoHelper/Forms/DevApiHost.cs`、`RoHelper.Tests/DevApiRouterTests.cs`

- [ ] `IDevApiHost.FieldAsync()`：截一張 → `MinimapReader.Read` → `{ inField, minimap: { area, map, me, magnets, chests, loot, bosses } | null, preview }`，`preview` 用一個**新的** `FieldPlanner`（不改引擎的狀態）跑一次 `Step` 的結果（status、click 換成截圖像素）。
- [ ] 路由 `GET /field`、端點清單加一行；router 測試加一個。
- [ ] commit `feat: 開發介面 /field`

---

### Task 5: 實機驗證與文件（Claude 自己做）

- [ ] 用 `--dev-api` 啟動，讓大廳流程進野外，期間：`/field` 看小地圖判讀是否正確（寶箱、磁鐵出現時）、`/status` 看走位狀態、每隔幾秒 `/shot` 看角色是否真的在移動、會不會卡在牆邊。
- [ ] 跑完至少 3 局，看 `/evidence` 的定時截圖；記錄每局存活時間、撿到的寶箱數（結算畫面）。
- [ ] 校正：方向反了、步伐太大／太小、卡牆、誤認，改常數並補測試素材（`/shot` 存的截圖複製到 Fixtures）。
- [ ] README「目前狀態」更新；本計畫書最後記「實機驗證結果」；commit。

## 實機驗證結果（2026-11-21 00:56～01:10，Claude 用開發介面自己跑）

- 大廳流程進野外 → `FieldGate` 進入野外 → 走位接手 ✅（`/field` 的 inField=true，小地圖每次都讀到自己）。
- 走位狀態在「回地圖中間／繞圈／前往寶箱／前往磁鐵／卡住了，繞路」之間切換，小地圖座標 (136,107) → (125,142) → (163,141)，角色確實在移動。
- **結果：「你存活下來了！」14:20、本局等級 57、擊殺 9254、頭目 7。** 對照：站著不動約 2.5 分鐘死亡；使用者自己玩的一局 7:10 死亡、等級 29。
- 新範本「返回角色」在「存活」版結算畫面也認得出來；「確認結算畫面」（連續 2 輪）→ 準備返回角色 ✅。
- 問題：
  1. 「前往寶箱」與「卡住了，繞路」反覆交替（00:05～00:10 約 10 次）：疑似寶箱在雕像後面走不到或錢不夠開。待查（可能要 chestMoney，或卡住 3 次後的忽略時間加長）。
  2. 遊戲 0.7.8 更新後，被中斷的局再上線會跳「未完成的一局（捨棄／繼續）」與「已捨棄本局（OK）」，大廳流程不認得 → 這次手動按掉。
  3. 「前往野外」範本（62×24）在首頁（0.906）和大廳（0.908）都誤判；目前只在交談後的時間窗內會點，所以沒誤點，但要重存大一點的範本。
