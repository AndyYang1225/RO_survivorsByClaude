namespace RoHelper.Vision.Field;

/// <summary>王死掉的地點（小地圖截圖像素）與判定死掉的時間</summary>
public sealed record BossSpot(double X, double Y, long At);

/// <summary>
/// 追蹤小地圖上的王，判斷牠死在哪裡（移植 engine.js trackBosses）。
/// 跟 JS 的差別：JS 把王的位置換成格子座標（要 OCR 小地圖上的座標），視覺模式還不 OCR 座標，所以全程用小地圖像素；
/// 換算後的門檻：同一隻王 &lt; 6 px（JS 25 格）、在小地圖裡面＝離地圖本體外框 &gt; 4 px（JS 離畫布邊緣 8 px）。
/// 用「地圖本體的外框」當小地圖邊緣：MinimapInfo 沒有帶小地圖區塊，而小地圖沒拉到整張地圖時外框就等於區塊；
/// 拉到整張地圖時王本來就跑不出外框，用外框也不會誤判。
/// 已知限制：用像素記地點的前提是小地圖不會跟著自己捲動（同 stayCenter 拿地圖本體中心當地圖中心的前提）；會捲動的地圖上，地點會跟著偏掉，要等 OCR 座標才能修。
/// </summary>
public sealed class BossTracker
{
    /// <summary>超過這麼久沒看到就不再追蹤（同 JS；標記閃爍週期約 0.3 秒，1.5 秒看不到才算真的不見）</summary>
    private const long Gone = 1500;

    private const double SameBoss = 6;
    private const double Edge = 4;

    private readonly List<(double X, double Y, long At, bool InView)> _seen = [];

    /// <summary>最近判定的王死掉地點；null＝沒有（由 <see cref="FieldPlanner"/> 用完或過期後清掉）</summary>
    public BossSpot? Spot { get; set; }

    /// <summary>新的一局：全部清掉</summary>
    public void Reset()
    {
        _seen.Clear();
        Spot = null;
    }

    /// <summary>
    /// 每輪呼叫一次：把這輪看到的王配對到已知的王（更新位置、時間、在不在小地圖裡面），
    /// 超過 1.5 秒沒看到、而且最後是在小地圖裡面（不是跑出邊界）的判定死了，設成 <see cref="Spot"/>。
    /// </summary>
    public void Update(MinimapInfo mm, long now)
    {
        var m = mm.Map;
        foreach (var b in mm.Bosses)
        {
            var inView = b.X > m.Left + Edge && b.X < m.Right - Edge && b.Y > m.Top + Edge && b.Y < m.Bottom - Edge;
            var k = _seen.FindIndex(q => Math.Sqrt((q.X - b.X) * (q.X - b.X) + (q.Y - b.Y) * (q.Y - b.Y)) < SameBoss);
            if (k < 0)
                _seen.Add((b.X, b.Y, now, inView));
            else
                _seen[k] = (b.X, b.Y, now, inView);
        }
        // 同 JS：同一輪有好幾隻死掉時，留最後一隻
        foreach (var s in _seen.Where(s => now - s.At > Gone && s.InView))
            Spot = new BossSpot(s.X, s.Y, now);
        _seen.RemoveAll(s => now - s.At >= Gone);
    }
}
