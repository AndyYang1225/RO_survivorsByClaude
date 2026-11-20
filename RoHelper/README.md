# RO Survivors 小幫手（exe 版）

取代 Tampermonkey 腳本 v7.5。雙擊 `RoHelper.exe` 就會開遊戲並自動掛上小幫手，控制面板是獨立視窗。

## 目前狀態（2026-11-20）：JS 引擎已停用
- 一律用視覺模式，不在遊戲網頁注入任何 JS。相關程式碼是**註解掉**的（Program.cs、GameForm.cs、RoHelper.csproj 標了【JS 引擎已停用】），要恢復就拿掉註解，或看 git tag `js-engine-last`。
- 視覺模式會跑大廳流程和**自動選卡**（升級／寶箱，規則同面板設定）；**野外走位（計畫 4）完成前，進野外要自己走**。
- 選卡先不讀尋寶率：「尋寶率超過 N% 就不選尋寶卡」「超過後優先選生命」在視覺模式不生效，「尋寶率優先」等同「最稀有」。
- 「學到後放逐」和「一出現就放逐」的卡都放逐過後，會按遊戲的「自動」，這局之後交給遊戲選（面板「升級」分頁可關）。
- 選卡判讀有問題時：在選卡畫面按 F8 暫停 →「視覺除錯」→「截一張」→「讀選卡」，記錄欄會列出每張卡讀到的內容。
- 下面「自動更新引擎」「從 Tampermonkey 版搬設定」等 JS 相關段落暫時不適用；`picklog.json`、`runs.jsonl` 也不會再新增。

## 使用
- **F8**：暫停／繼續（遊戲或面板有焦點時都可以按）
- **F9**：顯示／隱藏面板（面板按 X 也只是隱藏）
- 遊戲視窗上方選單：面板、重新載入、開發者工具、開啟資料夾
- 從 Tampermonkey 版搬設定：舊面板「其他 → 匯出設定（複製）」→ 新面板「其他 → 匯入設定」貼上

## 檔案位置
| 內容 | 位置 |
|---|---|
| 規則設定 | `%AppData%\RoHelper\config.json` |
| 選卡紀錄 | `%AppData%\RoHelper\picklog.json` |
| 完整對局紀錄（每局一行，給分析用） | `%AppData%\RoHelper\runs.jsonl` |
| 視窗位置 | `%AppData%\RoHelper\app.json` |
| 瀏覽器資料（登入狀態） | `%LocalAppData%\RoHelper\WebView2` |

## 掛機
- 按遊戲視窗的「最小化」時，視窗其實是移到螢幕外，遊戲照常跑（真的最小化，WebView2 會讓遊戲暫停）。點工作列或 Alt+Tab 就會回來。
- 例外：Win+D（顯示桌面）是由系統直接最小化，這時遊戲還是會暫停。
- 不想要這個行為：把 `app.json` 的 `KeepRunningWhenMinimized` 改成 `false`。

## 自動更新引擎（取代 Tampermonkey 的 @updateURL）【已停用】
exe 啟動時會到 Gist 抓 `ro_engine.js`，比目前新就換上（也可以按選單「檢查更新」）。選單最右邊會顯示目前用的引擎版本。

**發佈新版引擎的步驟：**
1. 改 `Engine/engine.js`，把檔頭的 `// @version` 調高（例如 8.0.0 → 8.0.1）。
2. 如果改到 exe ↔ JS 的訊息格式，也要調高 `// @min-exe`，並發佈新的 exe。
3. 把 `engine.js` 的內容上傳到 Gist（`AndyYang1225/eae1593b…`），檔名用 **`ro_engine.js`**。
4. 大家下次開 exe 就會自動換上（GitHub raw 有大約 5 分鐘的快取）。

- 下載到的引擎存在 `%AppData%\RoHelper\engine-cache.js`，沒網路時也能用。
- 更新來源和開關在 `app.json`：`EngineUpdateUrl`、`AutoUpdateEngine`。

## 開發
- 自動化邏輯在 `Engine/engine.js`（從使用者腳本拆出來的，邏輯本身沒改）；面板依 JS 送來的 `RULES` 自動產生，新增規則只要改 JS。
- 改 JS 不想重新編譯：把 `engine.js` 放在 exe 旁邊，重新開 exe 就會用它（腳本在啟動時註冊，按 F5 不會重讀檔案）。這時不會檢查線上更新。
- 建置：`dotnet build`；發佈單一 exe（自帶 .NET）：`dotnet publish -c Release -o publish`
- 測試：`dotnet test ..\RoHelper.Tests`（測試專案跟 RoHelper 資料夾平行，避免被主專案一起編譯）
- 建置輸出路徑：TFM 改成 `net8.0-windows10.0.19041.0`（OCR 需要 WinRT），所以輸出在 `bin\Debug\net8.0-windows10.0.19041.0\`

## 視覺判斷（開發中，不碰網頁）
目標：完全不在遊戲頁面注入 JS，只看截圖判斷、用瀏覽器層級的真實輸入操作。目前完成第 2 階段：視覺模式能自動跑大廳流程（單人大廳、走到節慶主持人、開始遊戲、前往野外、返回角色）；選卡、野外走位、遠征還是要用 JS 模式。
- 切換模式：遊戲視窗選單「模式：JS／視覺」，重新開啟後生效（存在 `app.json` 的 `VisionMode`）
- 視覺模式的 F8／F9 由 exe 處理；JS 模式維持 engine.js 自己的處理
- 視覺模式需要的範本（名稱要一字不差）：單人大廳、開始遊戲、前往野外、返回角色、節慶主持人、交談提示。後兩個存範本時要勾「整張畫面找」；缺哪個面板會顯示「缺少範本」
- 注意：暫停狀態會存進設定，從 JS 模式暫停後切到視覺模式，重開後仍是暫停中（按 F8 繼續）
- 規則清單：視覺模式用內建的 `Engine/schema.json`。改了 `engine.js` 的規則要重新產生：`node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json`（在 `C:Ro` 執行；`BuiltInSchemaTests` 會檢查有沒有忘記）
- 遊戲視窗選單「視覺除錯」：即時截圖、點擊測試、框選 OCR、存範本、找範本
- 截圖用 Windows 的 PrintWindow（約 50 ms，視窗移到螢幕外掛機時也截得到）
- 範本存在 `%AppData%\RoHelper\templates`：每個範本是 `名稱.png`＋`名稱.json`（`X/Y/W/H`＝搜尋範圍佔截圖的比例、`Threshold`＝門檻、`Dpr`＝截圖當下的顯示縮放）
- OCR 需要 Windows 的繁體中文 OCR 語言套件（設定 → 時間與語言 → 語言與地區 → 中文（台灣）→ 光學字元辨識）
- 計畫書：`C:\Ro\docs\superpowers\plans\`

## 開發介面（給 Claude 用，預設關閉）
用 `dotnet run --project RoHelper -- --dev-api`（或 `RoHelper.exe --dev-api[=port]`）啟動時，在 `http://127.0.0.1:8765/` 開一個本機 HTTP 介面，讓 Claude 自己截圖、讀畫面、操作；同時自動存證據。沒帶這個參數時程式行為完全不變。

| 端點 | 用途 |
|---|---|
| `GET /` | 端點清單 |
| `GET /status?n=30` | 暫停、局數、目前動作、最近的狀態紀錄、本局選卡狀態 |
| `GET /shot?name=xxx` | 截一張存到 `shots\api\`，回路徑 |
| `GET /screen` | 每個大廳範本的最佳位置、分數、門檻 |
| `GET /picker` | 讀選卡畫面＋現在設定下會怎麼選（只預覽） |
| `GET /field` | 讀小地圖（自己、磁鐵、寶箱、掉落物、王）＋現在設定下會怎麼走（只預覽） |
| `POST /click?x=&y=`、`POST /key?k=1` | 點擊（截圖像素座標）、按鍵 |
| `POST /pause`、`POST /resume` | 暫停、繼續 |
| `POST /templates/reload` | 重新讀範本 |
| `POST /templates/save?name=&shot=&x=&y=&w=&h=[&threshold=0.9]` | 從 `shots\` 底下的截圖裁一塊存成範本（同名範本沿用原搜尋範圍） |
| `GET /evidence?n=20` | 最近自動存的證據 |

- 範例：`curl -s 127.0.0.1:8765/status`、`curl -s -d "" "127.0.0.1:8765/click?x=950&y=640"`
- POST 要帶 `-d ""`：只寫 `-X POST` 時 curl 不送 Content-Length，Windows 的 HTTP.sys 會直接回 411，請求到不了程式。
- 中文參數要自己先編成 UTF-8 的 %XX（例如 `node -p "encodeURIComponent('返回角色')"`）：Git Bash 的 curl 用 `--data-urlencode` 時會把中文轉成 Big5 送出，存出來的檔名是亂碼。
- **自動存證據**：讀不到卡名、選卡按鍵沒反應、例外時（同一種 60 秒內只存一次），以及每 5 分鐘一張，存到 `%AppData%\RoHelper\shots\auto\`（PNG＋同名 JSON，最多 150 張，超過刪最舊的）。
- 安全：只聽 127.0.0.1（其他電腦連不到）、不加驗證、不提供輸入文字（不碰帳密，登入由使用者自己做一次）。
