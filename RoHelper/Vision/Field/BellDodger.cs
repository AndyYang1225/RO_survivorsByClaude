namespace RoHelper.Vision.Field;

/// <summary>躲鐘這一輪看到的畫面：找到的鐘（null＝沒找到）、截圖寬高（像素）、dpr</summary>
public sealed record BellView(BellBlob? Bell, int Width, int Height, double Dpr);

/// <summary>
/// 躲到鐘下（移植 engine.js bellStep 的「② 備用：看畫面顏色找鐘」，精確模式要讀遊戲資料，不做）。
/// 王會喊「躲到鐘下！」並在附近放一口金色的鐘，倒數結束時不在鐘下或鐘還被封印 → 直接被秒殺（JS 註解）。
/// 公告（<see cref="BannerReader.DodgeMs"/>）開始 15 秒（「太遲」4 秒）的躲鐘；MVP 死了（撿取倒數）立刻結束。
/// 躲鐘期間最優先，不做任何其他走位（<see cref="FieldPlanner.Step"/> 第一個檢查）。純邏輯：時間由呼叫端傳入。
/// 刻意不做：斐揚洞穴 MVP 前 10 秒提醒（要地名＋王的時間表，視覺模式沒有 nextBoss）。
/// </summary>
public sealed class BellDodger
{
    /// <summary>找不到鐘時，多久內照上次的位置走（同 JS）</summary>
    private const long KeepLast = 1500;

    private long _until;
    private bool _parked;
    private (double X, double Y, long At)? _last;
    private double _w;

    /// <summary>躲鐘到什麼時候（TickCount64 毫秒；0＝沒在躲）</summary>
    public long Until => _until;

    /// <summary>最近找到的鐘（給 <see cref="BellFinder.Find"/> 認封印後變紅的鐘，也給開發介面看）</summary>
    public BellBlob? Seen { get; private set; }

    /// <summary>現在是不是在躲鐘</summary>
    public bool Active(long now) => now < _until;

    /// <summary>讀到公告文字：要躲鐘的話設定躲到什麼時候（同 JS：每次讀到都重新算，「太遲」會縮短成 4 秒）</summary>
    public void Announce(string text, long now)
    {
        if (BannerReader.DodgeMs(text) is { } ms)
            _until = now + ms;
    }

    /// <summary>結束躲鐘（MVP 死了、新的一局、躲鐘關掉）</summary>
    public void End()
    {
        _until = 0;
        Reset();
    }

    /// <summary>
    /// 躲鐘的一輪（同 JS bellStep）。沒在躲鐘時回 null（並清掉站好、上次位置、鐘寬，同 JS）。
    /// 找到鐘：鐘寬 bw＝max(鐘寬, 上次 × 0.8, 30·dpr)、目標點＝鐘底中間往上 0.3 bw；
    /// 自己的腳下（畫面中心往下 15 px）離目標 &lt; 0.25 bw（已經站好時 &lt; 0.4 bw）→「在鐘下等待」不動，否則「躲到鐘下！」直接點目標點（JS 的「在鐘框內」不用，見下面註解）。
    /// 找不到鐘：已經站好就原地等；1.5 秒內看過就照上次的目標點走；否則「躲鐘：找鐘中，原地等」。
    /// view 是 null（沒掃畫面）時當成找不到鐘。
    /// </summary>
    public FieldDecision? Step(BellView? view, long now)
    {
        if (!Active(now))
        {
            Reset();
            return null;
        }
        var b = view?.Bell;
        if (view is null || b is null)
        {
            if (_parked)
                return new("在鐘下等待", null);
            if (_last is { } l && now - l.At < KeepLast)
                return Click("躲到鐘下！（照上次位置）", l.X, l.Y);
            return new("躲鐘：找鐘中，原地等", null);
        }
        Seen = b;
        // JS 註解：鐘半徑 78、安全半徑 150 ≈ 鐘的寬度，所以離鐘底中心 0.35 個鐘寬內才算站好（JS 實測：寬鬆的固定像素門檻會被秒殺）
        _w = Math.Max(b.Width, _w * 0.8);
        var bw = Math.Max(_w, 30 * view.Dpr);
        double tx = b.X, ty = b.Y1 - bw * 0.3;
        _last = (tx / view.Width, ty / view.Height, now);
        // 改善 JS（實測 2026-11-21 斐揚洞穴月夜貓）：JS 「自己在鐘框內就算站好」——這口鐘的框包含鐘身（甚至旁邊火盆的火焰），
        // 角色站在鐘頂上方也在框內，被當成站好、結果被秒殺。改成只看「腳下」離目標（底座圓盤中心）多遠；
        // 框可能被火焰撐大，門檻也從 0.35／0.6 個鐘寬收緊成 0.25／0.4。腳下＝畫面中心往下 15 px（角色血條的位置）
        double cx = view.Width / 2.0, cy = view.Height / 2.0 + 15 * view.Dpr;
        var dist = Math.Sqrt((tx - cx) * (tx - cx) + (ty - cy) * (ty - cy));
        if (dist < bw * 0.25 || (_parked && dist < bw * 0.4))
        {
            _parked = true;
            return new("在鐘下等待", null);
        }
        _parked = false;
        return Click("躲到鐘下！", tx / view.Width, ty / view.Height);
    }

    private void Reset()
    {
        _parked = false;
        _last = null;
        _w = 0;
        Seen = null;
    }

    /// <summary>點擊座標夾在 0.05～0.95（同走位的 clickWorld；JS 躲鐘直接點，不夾，這裡避免點到畫面邊緣的 UI）</summary>
    private static FieldDecision Click(string status, double fx, double fy)
        => new(status, new PointF((float)Math.Clamp(fx, 0.05, 0.95), (float)Math.Clamp(fy, 0.05, 0.95)));
}
