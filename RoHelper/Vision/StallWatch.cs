namespace RoHelper.Vision;

public enum StallAct { None, Reload, Alert }

/// <summary>卡住偵測的結果：要做什麼、為什麼（寫進 events.log、顯示在標題列）</summary>
public sealed record StallVerdict(StallAct Act, string Why)
{
    public static readonly StallVerdict None = new(StallAct.None, "");
}

/// <summary>
/// 卡住偵測。實測（2026-11-22 三開）：網頁跳出原生對話框，A 視窗停了 8.5 小時都沒人發現。
/// 兩種卡法：
/// 1. 畫面凍結：截圖連續 <c>frozenMs</c> 完全沒變（網頁被對話框擋住、渲染當掉），或主迴圈根本沒在跑（輸入卡住，這一輪一直沒結束）。
///    遊戲大廳、野外都有動畫，正常情況截圖一定會變。
/// 2. 流程卡住：<c>noRunMs</c> 沒有開新局也沒有結算（正常一局 15～22 分鐘）。
/// 處理：先重新整理頁面（最多 <c>maxReloads</c> 次，交給大廳流程從首頁接手），還是不行就提醒使用者一次，之後不再動作，免得一直重新整理。
/// 有進度（開新局、結算）時全部歸零。暫停、達到局數上限、沒開自動活動／遠征時不算卡住（<see cref="Idle"/>）。
/// 純邏輯、時間由外面傳入，單元測試不用真的等。執行緒：全部在 UI 執行緒呼叫。
/// </summary>
public sealed class StallWatch(long frozenMs = 3 * 60_000, long noRunMs = 45 * 60_000, int maxReloads = 2)
{
    private long _alive;
    private long _progress;
    private long _baseline;
    private ulong _sig;
    private bool _hasSig;

    /// <summary>上次有進度之後，已經自動重新整理幾次</summary>
    public int Reloads { get; private set; }

    /// <summary>重新整理也救不回來、已經提醒過使用者（有進度後清掉）</summary>
    public bool Alerted { get; private set; }

    /// <summary>主迴圈每輪截圖後呼叫：畫面有變才算「還活著」</summary>
    public void Frame(long now, ulong sig)
    {
        if (_hasSig && sig == _sig)
            return;
        _sig = sig;
        _hasSig = true;
        _alive = now;
    }

    /// <summary>開新局或結算</summary>
    public void Progress(long now)
    {
        _progress = now;
        Reloads = 0;
        Alerted = false;
    }

    /// <summary>暫停、達到局數上限等「本來就不會動」的狀態：計時從現在重新開始</summary>
    public void Idle(long now) => _baseline = now;

    private string? _forced;

    /// <summary>
    /// 主迴圈自己看出卡住了（例如野外的遊戲時間超過一局該有的長度）：下一次 Check 就當成卡住處理（同樣受重新整理次數限制）
    /// </summary>
    public void Force(string why) => _forced ??= why;

    /// <summary>定時呼叫（例如每 15 秒）</summary>
    public StallVerdict Check(long now)
    {
        var alive = Math.Max(_alive, _baseline);
        var progress = Math.Max(_progress, _baseline);
        var why = _forced
            ?? (now - alive >= frozenMs ? $"畫面 {frozenMs / 60_000} 分鐘沒有變化"
            : now - progress >= noRunMs ? $"{noRunMs / 60_000} 分鐘沒有開新局"
            : null);
        _forced = null;
        if (why is null)
            return StallVerdict.None;
        // 處理過就重新計時：重新整理後給大廳流程一段時間接手
        _baseline = now;
        if (Reloads < maxReloads)
        {
            Reloads++;
            return new(StallAct.Reload, why);
        }
        if (Alerted)
            return StallVerdict.None;
        Alerted = true;
        return new(StallAct.Alert, why);
    }

    /// <summary>截圖的指紋：等距取 32×32 個點做雜湊。只用來判斷「跟上一張完全一樣」，不用整張比</summary>
    public static ulong Signature(Frame f)
    {
        var h = 14695981039346656037UL;
        for (var j = 0; j < 32; j++)
        {
            var y = (int)((j + 0.5) * f.Height / 32);
            for (var i = 0; i < 32; i++)
            {
                var x = (int)((i + 0.5) * f.Width / 32);
                var k = (y * f.Width + x) * 4;
                h = (h ^ f.Pixels[k]) * 1099511628211UL;
                h = (h ^ f.Pixels[k + 1]) * 1099511628211UL;
                h = (h ^ f.Pixels[k + 2]) * 1099511628211UL;
            }
        }
        return h;
    }
}
