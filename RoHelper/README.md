# RO Survivors 小幫手（exe 版）

取代 Tampermonkey 腳本 v7.5。雙擊 `RoHelper.exe` 就會開遊戲並自動掛上小幫手，控制面板是獨立視窗。

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

## 自動更新引擎（取代 Tampermonkey 的 @updateURL）
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
目標：完全不在遊戲頁面注入 JS，只看截圖判斷、用瀏覽器層級的真實輸入操作。目前完成第 1 階段（框架＋除錯工具），自動化仍由 JS 引擎負責。
- 遊戲視窗選單「視覺除錯」：即時截圖、點擊測試、框選 OCR、存範本、找範本
- 截圖用 Windows 的 PrintWindow（約 50 ms，視窗移到螢幕外掛機時也截得到）
- 範本存在 `%AppData%\RoHelper\templates`：每個範本是 `名稱.png`＋`名稱.json`（`X/Y/W/H`＝搜尋範圍佔截圖的比例、`Threshold`＝門檻、`Dpr`＝截圖當下的顯示縮放）
- OCR 需要 Windows 的繁體中文 OCR 語言套件（設定 → 時間與語言 → 語言與地區 → 中文（台灣）→ 光學字元辨識）
- 計畫書：`C:\Ro\docs\superpowers\plans\`
