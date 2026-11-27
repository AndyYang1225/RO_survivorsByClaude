# RO Survivors 小幫手

.NET 8 WinForms + WebView2 的遊戲自動化（純視覺：截圖、範本比對、Windows OCR、CDP 送輸入，不在遊戲網頁注入 JS）。
使用者講繁體中文，回覆一律用繁體中文。

## 常用指令（在 C:\Ro）

- **重開三隻（換新版）**：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\restart.ps1`
  預設等每隻這局打完才重開；`-Now` 馬上重開、`-NoBuild` 不編譯。重開前先問使用者（除非使用者已經說要重開）。
  編譯成功（改版）時會清空三隻的戰績、局數、`runs.jsonl`（`tools/reset-stats.js`，舊的搬到各帳號 `history\`；使用者要求，避免讀到舊版資料）；`-KeepStats` 不清。
  所以分析戰績只看 `runs.jsonl`（＝這一版），要比較舊版才去 `history\`。
  `-Set runLimit=true,runLimitN=5`：關掉後、重開前改三隻的 `config.json`（`tools/set-config.js`，備份到 `history\`）。
  切換活動／遠征、局數上限只能這樣改（`config.pending.json` 會濾掉玩法選擇的鍵）：活動＝`autoEvent=true,autoExpe=false`。
  局數上限到了會停在大廳不開新局。
  重開完會在背景啟動看門狗（`tools\watchdog.ps1`，`-NoWatchdog` 不啟動）：某一隻開發介面 3 分鐘沒回應、或動作紀錄 10 分鐘沒變
  （暫停、局數上限、待機不算）就只重開那一隻，每隻每小時最多 3 次，紀錄在 `%AppData%\RoHelper\watchdog.log`。
  restart.ps1 執行中有 `restarting.lock`，看門狗不動作。程式內的 `Vision/StallWatch.cs` 只會重新整理網頁（最多 2 次）。
- **編譯**：`dotnet build RoHelper`（小幫手在跑時 bin 會被鎖住，先用上面的腳本或關掉）
- **測試**（小幫手在跑時也能測）：`dotnet test RoHelper.Tests --artifacts-path <scratchpad>/art < /dev/null`
  `BellFinderTests.Scan_IsCheap` 是速度測試，三隻同時在跑時偶爾會失敗，單獨重跑會過。
- **上傳**：用 git-deploy skill（commit 訊息「前綴 繁中摘要」，前綴 feat／fix／modify…；先 `git pull --rebase` 再 `git push`）。
  **每次上傳完（使用者 2026-11-26）**：把這次的改動更新進這份 CLAUDE.md（新功能、規則位置、陷阱；過時的刪掉，保持精簡），
  一起 commit 上傳，然後問使用者要不要 `/clear` 開新對話（省 token）。
- 改了 `RoHelper/Engine/engine.js` 的規則：`node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json`

## 三隻帳號（profile）

| 帳號 | 啟動參數 | 開發介面 | 資料夾 |
|---|---|---|---|
| sabc1229 | `--dev-api=8765` | 127.0.0.1:8765 | `%AppData%\RoHelper\` |
| sabc1227 | `--profile=B --dev-api=8766` | 127.0.0.1:8766 | `%AppData%\RoHelper\profiles\B\` |
| sabc1228 | `--profile=C --dev-api=8767` | 127.0.0.1:8767 | `%AppData%\RoHelper\profiles\C\` |

每個資料夾：`config.json`（規則）、`runs.jsonl`（每局完整紀錄，含 outcome clear＝勝／death＝敗）、`status.json`、`shots\`。
範本內建在 exe（`RoHelper/Templates`），`%AppData%\RoHelper\templates` 同名的優先。

## 開發介面（只綁 127.0.0.1）

`GET /status?n=`（狀態、最近 n 筆動作，最多 200）、`/shot?name=`（存到 `shots\api`，最多 100 張）、`/picker`（現在會怎麼選卡，只預覽）、
`/field`（小地圖、尋寶率、`danger` 攻擊警示範圍與候選色塊數字）、`/screen`；
`POST /pause`、`/resume`、`/click?x&y`、`/move?x&y`（只移滑鼠，看說明框用）、`/key?k=`（POST 用 `curl -X POST -d ''`）。完整清單：`GET /`。

## 改設定（程式在跑時）

不要直接寫 `config.json`（程式下次存檔會蓋掉）。寫 `config.pending.json` 到該帳號資料夾，內容是要合併的鍵（例如 `{"itemBan": [...]}`），
程式下一輪自己併進去並刪掉檔案（見 `Storage/ConfigSync.cs`）。程式關著時才可以直接改 `config.json`（先備份）。
沒有使用者同意不要改他的選卡規則。

## 選卡規則的位置

- `Vision/Picker/CardDecider.cs`：判斷順序（檔頭註解）、技能優先（清單裡的技能先比稀有度、同稀有度照順序）、
  寶箱道具排名（`itemRankExpe`／`itemRankEvent`，各類第 1→2→3）、
  尋寶率上限（`treasureCap`，野外到上限放逐尋寶道具）、技能神祇（`DecideDeity`，選完才按遊戲「自動技能」）
- `Vision/Picker/PickerFlow.cs`：一個選卡畫面的流程與等待時間；選了尋寶卡先把卡上的 % 加進尋寶率（`TreasureGain`，連開寶箱時沒機會重讀道具欄），
  下次讀道具欄（`Vision/Field/TreasureReader.cs`）再用實際值蓋掉；`PickSettings.cs`：從 config 讀設定

## 走位規則的位置（`Vision/Field/`）

- `FieldPlanner.cs`：優先順序（躲鐘 → 攻擊警示 → 王死掉地點 → 掉落物 → 扭蛋 → 磁鐵 → 寶箱 → 繞圈）
- `BellDodger.cs`：躲鐘；畫面上找不到鐘時先看上方計時器那一塊有沒有底座圓盤（`BellFinder.FindBehindHud`，往那邊走到 MVP 血條下），
  再不行 FieldPlanner 往小地圖上的 MVP 走去找（`MinimapInfo.Mvps`）。躲鐘最長 90 秒（`MaxDodge`）；
  站在 < 200 點的小色塊上不延長（`ParkMinN`，火盆）；看過大鐘之後中等的鐘（`IsHoldBell`）也延長（鐘開啟後變暗）
- `DangerFinder.cs`：王的攻擊警示。`Find`＝紅圈、粉紅圓、扇形（只用斐揚洞穴月夜貓校正）；`FindStripes`＝洛陽的條紋（直、斜、橫、格子，
  格子遮罩＋安全距離 20 px，角色本身那塊不取樣）。站在裡面就逃（條紋：找最近的空地）、路線穿過就繞；躲鐘時不看。
  鐘的紅圈長得一樣：畫面上有鐘時包住鐘的圈不算、被切成半邊的大弧用寬高比／填滿比例排除
- 尋寶率：`TreasureReader.Sanity`（一局只增不減、暴增 > 200 要連續兩次差不多才採用）；道具欄讀到 0 格但已經有道具時不歸零
- 面板：新版是網頁（`Panel/web/panel.js`，C# 端 `Forms/PanelForm.Web.cs`），舊版 WinForms（`Forms/PanelForm*.cs`）

## 查證用的證據截圖（各帳號 `shots\`）

`bell`（躲鐘、MVP 戰鬥）、`party`（不明對話框等）、`auto`（定時）、`deity`（2026-11-27 起：點神祇「!」後 15 秒每秒一張、
4 張卡的選卡畫面，查技能神祇被選錯——B、C 拿到屬性附加 Deity，紀錄裡卻沒有小幫手選神祇的動作，原因未明）、`api`（`/shot`）。
`debugLog=true` 時選卡紀錄在 `picklog.json`（300 筆）。

## 陷阱

- 不要用 `/move` 移滑鼠叫出說明框來查東西：小幫手會把說明框當成對話框，按「確認結算畫面」。
- 新寫的 `.ps1` 有中文要存成 UTF-8 BOM（Windows PowerShell 5.1 不認無 BOM 的 UTF-8）。

- Bash 指令一律加 `< /dev/null`；不要執行 `python`／`python3`（會等輸入卡住），用 node。
- 原始碼是 CRLF：多行的 sed 取代常對不上，改用 Edit 工具。
- 不要 `git stash`（使用者有未提交的改動時會誤收）。
- Claude 開的小幫手視窗要縮小：送 `SC_MINIMIZE`（會移到螢幕外照跑），不要真的最小化。
- 遊戲畫面 1920×745（截圖 1904×682），升級畫面底部按鈕列會被切掉一半。
