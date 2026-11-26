namespace RoHelper.Vision.Field;

/// <summary>躲鐘這一輪看到的畫面：找到的鐘（null＝沒找到）、截圖寬高（像素）、dpr</summary>
public sealed record BellView(BellBlob? Bell, int Width, int Height, double Dpr);

/// <summary>
/// 躲到鐘下（移植 engine.js bellStep 的「② 備用：看畫面顏色找鐘」，精確模式要讀遊戲資料，不做）。
/// 王會喊「躲到鐘下！」並在附近放一口金色的鐘，倒數結束時不在鐘下或鐘還被封印 → 直接被秒殺（JS 註解）。
/// 公告（<see cref="BannerReader.DodgeMs"/>）開始 15 秒（「太遲」4 秒）的躲鐘；畫面上還看得到大鐘、或站在鐘下還看得到鐘就一直延長，
/// 延長過之後 4 秒找不到 → 鐘消失、提早結束。
/// 躲鐘期間最優先，不做任何其他走位（<see cref="FieldPlanner.Step"/> 第一個檢查）。純邏輯：時間由呼叫端傳入。
/// 刻意不做：斐揚洞穴 MVP 前 10 秒提醒（要地名＋王的時間表，視覺模式沒有 nextBoss）。
/// </summary>
public sealed class BellDodger
{
    /// <summary>找不到鐘時，多久內照上次的位置走（同 JS）</summary>
    private const long KeepLast = 1500;

    /// <summary>有鐘口時算「站好」用的鐘寬（dpr 1 的 px）：鐘口 0.25 個（25 px）內算站好，站好後 0.4 個（40 px）內不動</summary>
    private const double MouthW = 100;

    /// <summary>
    /// 看到大鐘（<see cref="BellFinder.IsBigBell"/>）就至少再躲這麼久：鐘還在就一直躲，不管公告讀不讀得到。
    /// 實測（2026-11-26 斐揚洞穴 14:06～14:44）：鐘持續 36 秒以上，公告消失後 15 秒時間到就「躲鐘結束」去撿寶箱，鐘還在、走出鐘下 2 秒就死
    /// </summary>
    private const long BigHold = 4000;

    /// <summary>
    /// 這次躲鐘看過大鐘、之後這麼久都找不到鐘 → 鐘消失了，提早結束躲鐘恢復走位（約 3 輪；鐘偶爾被特效蓋住 1 輪）。
    /// 實測（2026-11-26 第 3 次迭代）：鐘消失後還照 15 秒原地等，王貼身打，HP 4 秒內 100→17
    /// </summary>
    private const long GoneEnd = 4000;

    /// <summary>鐘消失結束躲鐘後這麼久內，公告的殘影不重新開始躲（公告會在畫面上多留 2～3 秒）</summary>
    private const long GoneMute = 5000;

    /// <summary>躲鐘中但畫面上找不到鐘（沒站好、1.5 秒內也沒看過）：<see cref="FieldPlanner"/> 看到這個狀態就往小地圖上的王走過去找鐘</summary>
    public const string Searching = "躲鐘：找鐘中，原地等";

    private long _until;
    private bool _parked;
    private (double X, double Y, long At)? _last;
    private double _w;
    private long? _bigAt;
    private long? _goneAt;

    /// <summary>躲鐘到什麼時候（TickCount64 毫秒；0＝沒在躲）</summary>
    public long Until => _until;

    /// <summary>最近找到的鐘（給 <see cref="BellFinder.Find"/> 認封印後變紅的鐘，也給開發介面看）</summary>
    public BellBlob? Seen { get; private set; }

    /// <summary>現在是不是在躲鐘</summary>
    public bool Active(long now) => now < _until;

    /// <summary>讀到公告文字：要躲鐘的話設定躲到什麼時候（同 JS：每次讀到都重新算，「太遲」會縮短成 4 秒）</summary>
    public void Announce(string text, long now)
    {
        if (_goneAt is { } g && now - g < GoneMute)
            return;
        if (BannerReader.DodgeMs(text) is { } ms)
            _until = now + ms;
    }

    /// <summary>
    /// MVP 戰鬥中直接在畫面上看到鐘（公告沒讀到也躲）：沒在躲鐘時開始 15 秒的躲鐘；已經在躲就不動（躲鐘中看到大鐘的延長在 <see cref="Step"/>）。
    /// 實測（2026-11-26 斐揚洞穴）公告只出現 2～3 秒，OCR 漏讀「躲」字沒認出來，鐘就在畫面上卻沒躲，被秒殺
    /// </summary>
    public void Spotted(long now)
    {
        _goneAt = null;
        if (!Active(now))
            _until = now + 15_000;
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
        if (b is not null && BellFinder.IsBigBell(b, view!.Dpr))
        {
            _bigAt = now;
            _until = Math.Max(_until, now + BigHold);
        }
        if (view is null || b is null)
        {
            if (_bigAt is { } ba && now - ba >= GoneEnd)
            {
                End();
                _goneAt = now;
                return null;
            }
            if (_parked)
                return new("在鐘下等待", null);
            if (_last is { } l && now - l.At < KeepLast)
                return Click("躲到鐘下！（照上次位置）", l.X, l.Y);
            return new(Searching, null);
        }
        Seen = b;
        // JS 註解：鐘半徑 78、安全半徑 150 ≈ 鐘的寬度，所以離鐘底中心 0.35 個鐘寬內才算站好（JS 實測：寬鬆的固定像素門檻會被秒殺）
        _w = Math.Max(b.Width, _w * 0.8);
        var bw = Math.Max(_w, 30 * view.Dpr);
        double tx = b.X, ty = b.Y1 - bw * 0.3;
        // 真實截圖有鐘口（BellFinder 找鐘身）就站鐘口：外框常連著火盆、光暈，用外框算會偏到圓盤邊緣。
        // 實測（2026-11-26 斐揚洞穴 105343）：外框目標 (958,373)，腳站在那裡、在圓盤上還是被鐘打死；鐘口 (924,284)。
        // 門檻改用鐘身寬（約 100 px），不用會被火盆撐大的外框寬
        if (b.BX != 0 || b.BY != 0)
            (tx, ty, bw) = (b.BX, b.BY, MouthW * view.Dpr);
        _last = (tx / view.Width, ty / view.Height, now);
        // 改善 JS（實測 2026-11-21 斐揚洞穴月夜貓）：JS 「自己在鐘框內就算站好」——這口鐘的框包含鐘身（甚至旁邊火盆的火焰），
        // 角色站在鐘頂上方也在框內，被當成站好、結果被秒殺。改成只看「腳下」離目標（底座圓盤中心）多遠；
        // 框可能被火焰撐大，門檻也從 0.35／0.6 個鐘寬收緊成 0.25／0.4。腳下＝畫面中心往下 15 px（角色血條的位置）
        double cx = view.Width / 2.0, cy = view.Height / 2.0 + 15 * view.Dpr;
        var dist = Math.Sqrt((tx - cx) * (tx - cx) + (ty - cy) * (ty - cy));
        if (dist < bw * 0.25 || (_parked && dist < bw * 0.4))
        {
            // 站在鐘下、還看得到這口鐘 → 不管大小都延長（同大鐘）。站好表示這塊就在腳下，不會是遠處的燈籠、火盆。
            // 實測（2026-11-27 C 斐揚洞穴第二次躲鐘）：洞穴光線暗只找到鐘身（n 439～641 < 大鐘門檻 650），
            // 一次都沒延長，公告 15 秒到就去繞圈，鐘和紅圈都還在，2 秒後被秒殺
            _bigAt = now;
            _until = Math.Max(_until, now + BigHold);
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
        _bigAt = null;
        Seen = null;
    }

    /// <summary>點擊座標夾在 0.05～0.95（同走位的 clickWorld；JS 躲鐘直接點，不夾，這裡避免點到畫面邊緣的 UI）</summary>
    private static FieldDecision Click(string status, double fx, double fy)
        => new(status, new PointF((float)Math.Clamp(fx, 0.05, 0.95), (float)Math.Clamp(fy, 0.05, 0.95)));
}
