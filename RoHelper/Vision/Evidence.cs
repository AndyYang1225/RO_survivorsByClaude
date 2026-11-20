using System.Drawing.Imaging;
using System.Text.Json.Nodes;

using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>
/// 決定這一輪要不要自動存證據（純邏輯，時間由呼叫端傳入方便測試）。
/// 異常（讀不到卡名、按鍵沒反應、例外）看狀態文字開頭判斷，同一個理由在 cooldownMs 內只存一次，免得卡住時每 250 ms 存一張；
/// 沒有異常時每 periodicMs 存一張「定時」，事後能看到一路上的畫面。第一次呼叫不存定時（剛啟動沒意義），從第一次呼叫開始計時。
/// </summary>
public sealed class EvidencePolicy(long cooldownMs = 60_000, long periodicMs = 300_000)
{
    private const string Periodic = "定時";

    private static readonly (string Prefix, string Reason)[] Triggers =
    [
        ("看起來像選卡但讀不到卡名", "讀不到卡名"),
        ("選卡按鍵沒反應", "按鍵沒反應"),
        ("視覺判斷失敗", "例外"),
    ];

    private readonly Dictionary<string, long> _lastSaved = [];
    private long? _periodicFrom;

    /// <summary>回這一輪要存的理由；不用存時回 null。now 用 <see cref="Environment.TickCount64"/></summary>
    public string? Check(string action, long now)
    {
        _periodicFrom ??= now;
        foreach (var (prefix, reason) in Triggers)
        {
            if (!action.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            // 觸發前綴時不檢查定時：這一輪的畫面要嘛剛存過異常、要嘛在冷卻中，再存一張定時沒有新資訊
            if (_lastSaved.TryGetValue(reason, out var last) && now - last < cooldownMs)
                return null;
            _lastSaved[reason] = now;
            return reason;
        }
        if (now - _periodicFrom < periodicMs)
            return null;
        _periodicFrom = now;
        return Periodic;
    }
}

/// <summary>
/// 自動存證據：一張截圖 PNG ＋ 同名 JSON（理由、時間、當下動作與最近的狀態紀錄），檔名「yyyyMMdd-HHmmss-理由」。
/// 上限預設 150 張：1904×682 的 PNG 約 0.5～1 MB，150 張約 100 MB 以內；超過就刪最舊的（檔名就是時間，依檔名排序即可）。
/// 同一秒同一個理由會覆蓋前一張：<see cref="EvidencePolicy"/> 的冷卻讓這種情況不會發生，所以不另外加序號。
/// </summary>
public sealed class EvidenceStore(string dir, int maxShots = 150)
{
    /// <summary>存一組證據，回 PNG 路徑。info 會複製一份再加上 reason、time，不改呼叫端的物件</summary>
    public string Save(Frame frame, string reason, DateTime time, JsonObject info)
    {
        Directory.CreateDirectory(dir);
        var name = $"{time:yyyyMMdd-HHmmss}-{reason}";
        var png = Path.Combine(dir, name + ".png");
        using (var bmp = frame.ToBitmap())
            bmp.Save(png, ImageFormat.Png);

        var json = (JsonObject)info.DeepClone();
        json["reason"] = reason;
        json["time"] = time.ToString("yyyy-MM-dd HH:mm:ss");
        JsonFile.Write(Path.Combine(dir, name + ".json"), json);

        Prune();
        return png;
    }

    /// <summary>PNG 超過上限時刪最舊的（連同同名 JSON）</summary>
    public void Prune()
    {
        var pngs = Pngs();
        foreach (var png in pngs.Take(Math.Max(0, pngs.Count - maxShots)))
        {
            File.Delete(png);
            File.Delete(Path.ChangeExtension(png, ".json"));
        }
    }

    /// <summary>最近 n 張的 PNG 路徑，新的在前；資料夾不存在回空</summary>
    public IReadOnlyList<string> Recent(int n) => [.. Pngs().AsEnumerable().Reverse().Take(n)];

    /// <summary>所有 PNG，舊的在前（檔名開頭是時間，用 Ordinal 排序就是時間順序）</summary>
    private List<string> Pngs()
    {
        if (!Directory.Exists(dir))
            return [];
        return [.. Directory.GetFiles(dir, "*.png").Order(StringComparer.Ordinal)];
    }
}
