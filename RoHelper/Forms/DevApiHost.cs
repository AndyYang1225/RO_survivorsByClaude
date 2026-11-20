using System.Drawing.Imaging;
using System.Text.Json.Nodes;

using RoHelper.DevApi;
using RoHelper.Storage;
using RoHelper.Vision;
using RoHelper.Vision.Field;
using RoHelper.Vision.Picker;

namespace RoHelper.Forms;

/// <summary>
/// 開發介面的實作。HttpListener 的請求在執行緒集區進來，這裡每個動作都用 <see cref="OnUi{T}"/> 切回 UI 執行緒做：
/// WebView2（CDP 輸入）只能在 UI 執行緒呼叫，截圖（PrintWindow）也要 UI 執行緒處理訊息，範本庫、設定、狀態紀錄也都只在 UI 執行緒讀寫（不加鎖）。
/// 刻意連比對範本、OCR 這種純計算也留在 UI 執行緒：視覺引擎本身就這樣做，偶爾卡一下畫面可接受，換來不用處理範本庫被 Reload 同時讀的問題。
/// 全程只截圖和送輸入，不在遊戲網頁執行任何 JS；不提供輸入文字的功能（不碰帳密）。
/// </summary>
public sealed class DevApiHost(Control ui, VisionKit kit, VisionEngine engine, PanelForm panel, ConfigStore config, EvidenceStore? evidence) : IDevApiHost
{
    public Task<JsonObject> StatusAsync(int n) => OnUi(() =>
    {
        var st = engine.PickState;
        var history = engine.History.Recent(n).Select(e => (JsonNode?)new JsonObject
        {
            ["t"] = e.Time.ToString("HH:mm:ss"),
            ["a"] = e.Action,
        });
        return Task.FromResult(new JsonObject
        {
            ["paused"] = config.GetBool("paused"),
            ["runs"] = config.GetInt("runs"),
            ["action"] = engine.History.Last,
            ["history"] = new JsonArray(history.ToArray()),
            ["pick"] = new JsonObject
            {
                ["picked"] = Strings(st.Picked),
                ["focused"] = Strings(st.Focused),
                ["banished"] = Strings(st.Banished),
                ["deityLock"] = st.DeityLock,
                ["gameAuto"] = st.GameAuto,
            },
        });
    });

    public Task<JsonObject> ShotAsync(string? name) => OnUi(async () =>
    {
        var frame = await kit.Grabber.CaptureAsync();
        Directory.CreateDirectory(AppPaths.ApiShotsDir);
        var path = Path.Combine(AppPaths.ApiShotsDir, (name ?? DateTime.Now.ToString("yyyyMMdd-HHmmss")) + ".png");
        using (var bmp = frame.ToBitmap())
            bmp.Save(path, ImageFormat.Png);
        return new JsonObject
        {
            ["path"] = path,
            ["width"] = frame.Width,
            ["height"] = frame.Height,
            ["dpr"] = kit.Viewport.Dpr,
        };
    });

    public Task<JsonObject> ScreenAsync() => OnUi(async () =>
    {
        var frame = await kit.Grabber.CaptureAsync();
        var dpr = kit.Viewport.Dpr;
        var templates = new JsonObject();
        foreach (var name in ScreenTemplates.All)
        {
            if (kit.Templates.Get(name) is not { } t)
            {
                templates[name] = null;
                continue;
            }
            // 不論過不過門檻都回最佳位置與分數，方便判斷門檻要不要調
            var m = kit.Templates.Match(frame, name, dpr);
            templates[name] = new JsonObject
            {
                ["score"] = m is { } r ? Math.Round(r.Score, 4) : null,
                ["bounds"] = m is { } b ? Rect(b.Bounds) : null,
                ["threshold"] = t.Threshold,
                ["found"] = m is { } f && f.Score >= t.Threshold,
            };
        }
        // lobby: true＝連 NPC 名字和交談提示也找（跟開了活動時的引擎一樣）
        var s = new ScreenReader(kit.Templates).Read(frame, dpr, lobby: true);
        return new JsonObject
        {
            ["width"] = frame.Width,
            ["height"] = frame.Height,
            ["dpr"] = dpr,
            ["templates"] = templates,
            ["reader"] = new JsonObject
            {
                [ScreenTemplates.Solo] = Rect(s.Solo),
                [ScreenTemplates.Start] = Rect(s.Start),
                [ScreenTemplates.GoField] = Rect(s.GoField),
                [ScreenTemplates.Return] = Rect(s.Return),
                [ScreenTemplates.FestivalHost] = Rect(s.FestivalHost),
                [ScreenTemplates.TalkPrompt] = Rect(s.TalkPrompt),
                [ScreenTemplates.Discard] = Rect(s.Discard),
                [ScreenTemplates.DiscardOk] = Rect(s.DiscardOk),
                [ScreenTemplates.Title] = Rect(s.Title),
                [ScreenTemplates.CharSelect] = Rect(s.CharSelect),
                ["missing"] = Strings(s.Missing),
            },
        };
    });

    public Task<JsonObject> PickerAsync() => OnUi(async () =>
    {
        if (kit.Ocr is null)
            return new JsonObject { ["error"] = kit.OcrError };
        var frame = await kit.Grabber.CaptureAsync();
        if (await new PickerReader(kit.Ocr).ReadAsync(frame, kit.Viewport.Dpr) is not { } info)
            return new JsonObject { ["picker"] = null };
        // 只預覽：CardDecider 只讀不改選卡狀態，這裡也不按鍵、不點
        var d = CardDecider.Decide(info, PickSettings.From(config), engine.PickState);
        return new JsonObject
        {
            ["picker"] = new JsonObject
            {
                ["isChest"] = info.IsChest,
                ["cards"] = new JsonArray(info.Cards.Select(c => (JsonNode?)new JsonObject
                {
                    ["index"] = c.Index,
                    ["name"] = c.Name,
                    ["text"] = c.Text,
                    ["rarity"] = c.Rarity,
                    ["skillBadge"] = c.SkillBadge,
                    ["isNew"] = c.IsNew,
                    ["bounds"] = Rect(c.Bounds),
                    ["buttons"] = new JsonArray(c.Buttons.Select(Button).ToArray()),
                }).ToArray()),
                ["rerollAll"] = info.RerollAll is { } ra ? Button(ra) : null,
                ["auto"] = info.Auto is { } a ? Button(a) : null,
            },
            ["decision"] = new JsonObject
            {
                ["do"] = d.Do.ToString(),
                ["index"] = d.Index,
                ["why"] = d.Why,
                ["focusKey"] = d.FocusKey,
            },
        };
    });

    public Task<JsonObject> FieldAsync() => OnUi(async () =>
    {
        var frame = await kit.Grabber.CaptureAsync();
        var dpr = kit.Viewport.Dpr;
        var mm = MinimapReader.Read(frame, dpr);
        // 金錢與寶箱價格：每次都讀（開發介面不在意 OCR 的時間），沒有 OCR 時是 null
        var hud = kit.Ocr is null ? null : await new HudReader(kit.Ocr).ReadAsync(frame, dpr);
        var hp = HpReader.Read(frame, dpr);
        var timer = TimerReader.Read(frame, dpr);
        var balls = BallFinder.Find(frame, dpr);
        // 公告與鐘：每次都讀、都掃（不管有沒有在躲鐘），校正用
        var banner = kit.Ocr is null ? null : await new BannerReader(kit.Ocr).ReadAsync(frame, dpr);
        var bell = BellFinder.Find(frame, dpr, engine.Bell.Seen);
        var now = Environment.TickCount64;
        var s = FieldSettings.From(config);
        // 只預覽：用一個新的走位決策（沒有上次移動時間、卡住紀錄），不動引擎的走位狀態（也不會在躲鐘），也不點
        var d = new FieldPlanner(Random.Shared.NextDouble).Step(mm, s, now, s.CanAffordChest(hud), hp, timer == TimerPhase.Loot, balls);
        return new JsonObject
        {
            ["width"] = frame.Width,
            ["height"] = frame.Height,
            ["dpr"] = dpr,
            ["inField"] = engine.InField,
            ["hp"] = hp is null ? null : Math.Round(hp.Value, 3),
            // 計時器：normal（深藍字）、loot（MVP 死後的撿取倒數，紅字）、null（找不到計時器）
            ["timer"] = timer is { } t ? (t == TimerPhase.Loot ? "loot" : "normal") : null,
            // 引擎的走位狀態（預覽用的新決策沒有追蹤紀錄）；ageMs＝判定死掉到現在多久
            ["bossSpot"] = engine.BossSpot is { } spot
                ? new JsonObject { ["x"] = Math.Round(spot.X, 1), ["y"] = Math.Round(spot.Y, 1), ["ageMs"] = now - spot.At }
                : null,
            // 躲鐘：remainMs＝引擎還要躲多久（0＝沒在躲）；banner＝這次 OCR 到的公告、dodgeMs＝這段公告會躲多久（null＝不會）；
            // found＝這張截圖上找到的鐘、seen＝引擎躲鐘中最近找到的鐘（截圖像素）
            ["bell"] = new JsonObject
            {
                ["remainMs"] = Math.Max(0, engine.Bell.Until - now),
                ["banner"] = banner,
                ["dodgeMs"] = banner is null ? null : BannerReader.DodgeMs(banner),
                ["found"] = Bell(bell),
                ["seen"] = Bell(engine.Bell.Seen),
            },
            // 畫面上像扭蛋的色塊（離畫面中心近的在前面；截圖像素）
            ["balls"] = new JsonArray(balls.Select(b => (JsonNode?)new JsonObject
            {
                ["kind"] = b.Kind,
                ["x"] = b.X,
                ["y"] = b.Y,
                ["w"] = b.Width,
                ["h"] = b.Height,
                ["n"] = b.N,
                ["fill"] = Math.Round(b.Fill, 2),
                ["rgb"] = $"{b.Rgb.R}/{b.Rgb.G}/{b.Rgb.B}",
            }).ToArray()),
            ["hud"] = hud is null ? null : new JsonObject { ["money"] = hud.Money, ["chestCost"] = hud.ChestCost },
            ["chestAffordable"] = s.CanAffordChest(hud),
            ["minimap"] = mm is null ? null : new JsonObject
            {
                ["area"] = Rect(MinimapReader.Area(frame, dpr)),
                ["map"] = new JsonObject
                {
                    ["x"] = Math.Round(mm.Map.X, 1),
                    ["y"] = Math.Round(mm.Map.Y, 1),
                    ["w"] = Math.Round(mm.Map.Width, 1),
                    ["h"] = Math.Round(mm.Map.Height, 1),
                },
                ["me"] = Blob(mm.Me),
                ["magnets"] = Blobs(mm.Magnets),
                ["chests"] = Blobs(mm.Chests),
                ["loot"] = Blobs(mm.Loot),
                ["bosses"] = Blobs(mm.Bosses),
            },
            ["preview"] = new JsonObject
            {
                ["status"] = d.Status,
                // 世界畫面比例換成截圖像素（同引擎）
                ["click"] = d.Click is { } c
                    ? new JsonObject { ["x"] = (int)Math.Round(c.X * frame.Width), ["y"] = (int)Math.Round(c.Y * frame.Height) }
                    : null,
            },
        };
    });

    public Task ClickAsync(int x, int y) => OnUi(async () =>
    {
        var (cx, cy) = kit.Viewport.ToCss(x, y);
        await kit.Input.ClickAsync(cx, cy);
        return true;
    });

    public Task MoveAsync(int x, int y) => OnUi(async () =>
    {
        var (cx, cy) = kit.Viewport.ToCss(x, y);
        await kit.Input.MoveAsync(cx, cy);
        return true;
    });

    public Task KeyAsync(char key) => OnUi(async () =>
    {
        await kit.Input.KeyAsync(key);
        return true;
    });

    public Task SetPausedAsync(bool paused) => OnUi(() =>
    {
        // 走面板的 TogglePause（跟按 F8 一樣），面板上的按鈕和狀態才會跟著更新
        if (config.GetBool("paused") != paused)
            panel.TogglePause();
        return Task.FromResult(true);
    });

    public Task ReloadTemplatesAsync() => OnUi(() =>
    {
        kit.Templates.Reload();
        return Task.FromResult(true);
    });

    public Task<JsonObject> SaveTemplateAsync(string name, string shot, Rectangle crop, double threshold) => OnUi(() =>
    {
        // 路由已經擋掉 .. 和絕對路徑，這裡再確認一次真的在 shots 底下
        var root = Path.GetFullPath(AppPaths.ShotsDir) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(AppPaths.ShotsDir, shot));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("shot 必須在 shots 資料夾底下");
        if (!File.Exists(path))
            throw new FileNotFoundException($"找不到截圖：{path}");

        // 先讀成 byte[] 再建 Bitmap：new Bitmap(path) 會鎖住檔案
        Frame frame;
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var bmp = new Bitmap(ms))
            frame = Frame.FromBitmap(bmp);
        if (crop.Right > frame.Width || crop.Bottom > frame.Height)
            throw new ArgumentException($"裁切範圍 {crop} 超出截圖（{frame.Width}×{frame.Height}）");

        var region = kit.Templates.Get(name)?.Region ?? new RectangleF(0, 0, 1, 1);
        // 假設存下來的截圖都是在 dpr 1 下截的（使用者的電腦是 100% 縮放）；在其他縮放比例截的圖存成範本，比對時大小會不對
        kit.Templates.Save(name, frame.Crop(crop), region, threshold, dpr: 1.0);
        return Task.FromResult(new JsonObject
        {
            ["saved"] = name,
            ["region"] = new JsonObject { ["x"] = region.X, ["y"] = region.Y, ["w"] = region.Width, ["h"] = region.Height },
        });
    });

    public Task<JsonObject> EvidenceAsync(int n) => Task.FromResult(new JsonObject
    {
        // 不用切 UI 執行緒：只列資料夾裡的檔案，跟引擎存檔同時發生最多是少列或多列一張
        ["enabled"] = evidence is not null,
        ["shots"] = Strings(evidence?.Recent(n) ?? []),
    });

    /// <summary>
    /// 在 UI 執行緒執行 work，把結果或例外傳回呼叫端（執行緒集區）。
    /// RunContinuationsAsynchronously：讓呼叫端的後續（寫 HTTP 回應）不要在 UI 執行緒上跑。
    /// 視窗已經關掉時 BeginInvoke 直接丟例外，路由會回 500。
    /// </summary>
    private Task<T> OnUi<T>(Func<Task<T>> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.BeginInvoke(async () =>
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static JsonArray Strings(IEnumerable<string> items) => new(items.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray());

    private static JsonObject? Rect(Rectangle? r)
        => r is { } v ? new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["w"] = v.Width, ["h"] = v.Height } : null;

    private static JsonNode? Blob(MinimapBlob b) => new JsonObject
    {
        ["x"] = Math.Round(b.X, 1),
        ["y"] = Math.Round(b.Y, 1),
        ["n"] = b.N,
    };

    private static JsonObject? Bell(BellBlob? b) => b is null ? null : new JsonObject
    {
        ["x"] = b.X,
        ["y"] = b.Y,
        ["x0"] = b.X0,
        ["y0"] = b.Y0,
        ["w"] = b.Width,
        ["h"] = b.Height,
        ["n"] = b.N,
        ["gold"] = b.Gold,
        ["red"] = b.Red,
        ["fill"] = Math.Round(b.Fill, 2),
    };

    private static JsonArray Blobs(IEnumerable<MinimapBlob> items) => new(items.Select(Blob).ToArray());

    private static JsonNode? Button(ActButton b) => new JsonObject
    {
        ["kind"] = b.Kind.ToString(),
        ["bounds"] = Rect(b.Bounds),
        ["enabled"] = b.Enabled,
        ["left"] = b.Left,
    };
}
