namespace RoHelper.Vision.Field;

/// <summary>這一輪的走位決策：面板文字，以及要點的「世界畫面比例座標」（0～1，同 JS clickWorld；null＝這輪不點）</summary>
public sealed record FieldDecision(string Status, PointF? Click);

/// <summary>
/// 野外走位決策（移植 engine.js fieldStep 的「小地圖模式」、wanderStep、checkStuck）：
/// 撿王的掉落物 → 磁鐵 → 寶箱 → 回地圖中間／繞圈；一直下指令但小地圖上的自己沒動就繞路；站在掉落物上 3 秒還在就忽略它。
/// 純邏輯：時間與亂數由呼叫端傳入（亂數注入才能寫測試），不截圖、不送輸入。
/// 刻意不做（第二批）：節慶扭蛋、精確模式（JS 讀遊戲資料）、吃肉、躲鐘、王死掉的地點（要 OCR 小地圖座標）、
/// MVP 倒數撿取階段、farLimit（JS 在小地圖模式本來就沒用）、lowHpStop。
/// 小地圖座標用截圖像素：假設 dpr 1 時跟 JS 的小地圖畫布像素同尺度，所以 JS 以像素為單位的門檻（0.8、3、len·0.03）直接沿用。
/// </summary>
public sealed class FieldPlanner(Func<double> random)
{
    /// <summary>繞圈半徑（同 JS GEO.wanderRadius）</summary>
    private const double WanderRadius = 0.32;

    /// <summary>朝目標走一步的最大距離（同 JS GEO.stepRadius）</summary>
    private const double StepRadius = 0.30;

    private const string Stuck = "卡住了，繞路";

    private long _lastMove;
    private string _lastAct = "";
    private double _angle;
    private (double X, double Y, long T)? _anchor;
    private int _stuckN;
    private double _escAng;
    private long _escUntil;
    private readonly List<(double X, double Y, long Until)> _blocked = [];
    private long _lootNear;

    /// <summary>新的一局：全部重設</summary>
    public void NewRun()
    {
        _lastMove = 0;
        _lastAct = "";
        _angle = 0;
        _anchor = null;
        _stuckN = 0;
        _escAng = 0;
        _escUntil = 0;
        _blocked.Clear();
        _lootNear = 0;
    }

    /// <summary>
    /// 決定這一輪怎麼走。chestAffordable＝錢夠不夠開寶箱（呼叫端讀 HUD 算，見 <see cref="FieldSettings.CanAffordChest"/>）：
    /// 不夠就不追寶箱（同 JS：seekChest 且 canAffordChest() 才追），開不了的寶箱追過去只會一直站在上面或卡在旁邊。
    /// </summary>
    public FieldDecision Step(MinimapInfo? mm, FieldSettings s, long now, bool chestAffordable = true)
    {
        if (!s.SeekLoot && !s.SeekMagnet && !s.SeekChest && !s.AutoWander)
            return new("", null);
        if (now - _lastMove < s.SeekEvery)
            return new(_lastAct, null);

        _blocked.RemoveAll(b => b.Until <= now);

        // 正在繞開障礙物
        if (_escUntil > now)
            return Escape(now);

        if (mm is null)
            return Wander(null, s, now);
        var me = mm.Me;

        // 王的掉落物最優先
        if (s.SeekLoot && Nearest(me, mm.Loot, now) is { } loot)
        {
            var (dx, dy, len) = Delta(me, loot);
            // 已經站在上面 3 秒還在 → 不是掉落物（可能是地形顏色），忽略 30 秒
            if (len < 3)
            {
                if (_lootNear == 0)
                    _lootNear = now;
                if (now - _lootNear > 3000)
                {
                    _blocked.Add((loot.X, loot.Y, now + 30_000));
                    _lootNear = 0;
                }
            }
            else
                _lootNear = 0;
            if (CheckStuck(now, me, Math.Atan2(dy, dx), loot))
                return Escape(now);
            return Go("撿王的掉落物", dx, dy, len, now);
        }

        // 磁鐵優先於寶箱
        var magnet = s.SeekMagnet ? Nearest(me, mm.Magnets, now) : null;
        var chest = s.SeekChest && chestAffordable ? Nearest(me, mm.Chests, now) : null;
        if ((magnet ?? chest) is { } pick)
        {
            var (dx, dy, len) = Delta(me, pick);
            if (CheckStuck(now, me, Math.Atan2(dy, dx), pick))
                return Escape(now);
            return Go(magnet is not null ? "前往磁鐵" : "前往寶箱", dx, dy, len, now);
        }

        return Wander(mm, s, now);
    }

    /// <summary>
    /// 沒東西撿時（同 JS wanderStep）：離地圖中心超過地圖長邊 12% 就往中心走，否則在附近繞圈。
    /// 小地圖拉到看得到整張地圖時，地圖本體的中心＝地圖中心。
    /// </summary>
    private FieldDecision Wander(MinimapInfo? mm, FieldSettings s, long now)
    {
        if (!s.AutoWander)
            return new("", null);
        if (mm is not null && CheckStuck(now, mm.Me, null, null))
            return Escape(now);
        if (now - _lastMove <= s.WanderEvery)
            return new(_lastAct.Length > 0 ? _lastAct : "繞圈", null);
        _lastMove = now;

        if (s.StayCenter && mm is not null && mm.Map.Width > 20)
        {
            var m = mm.Map;
            double dx = m.X + m.Width / 2.0 - mm.Me.X, dy = m.Y + m.Height / 2.0 - mm.Me.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            var side = Math.Max(m.Width, m.Height);
            if (d > side * 0.12)
            {
                var k = Math.Min(StepRadius, 0.12 + d / side * 0.6);
                return Click(_lastAct = "回地圖中間", 0.5 + dx / d * k, 0.5 + dy / d * k);
            }
        }

        _angle += Math.PI / 3 + (random() - 0.5) * 0.6;
        var rad = WanderRadius * (0.6 + random() * 0.4) * (s.StayCenter ? 0.7 : 1);
        return Click(_lastAct = "繞圈", 0.5 + Math.Cos(_angle) * rad, 0.5 + Math.Sin(_angle) * rad * 0.8);
    }

    /// <summary>
    /// 卡地形偵測（同 JS checkStuck）：一直下指令但小地圖上的位置 1.5 秒都沒動（移動 ≤ 0.8 px）→ 卡住了：
    /// 往目標的側邊（第 3 次起往反方向）繞開 1.3 秒；同一個目標卡 3 次就先放棄它 20 秒。
    /// </summary>
    private bool CheckStuck(long now, MinimapBlob me, double? targetAng, MinimapBlob? target)
    {
        if (_anchor is not { } a || Math.Sqrt((me.X - a.X) * (me.X - a.X) + (me.Y - a.Y) * (me.Y - a.Y)) > 0.8)
        {
            _anchor = (me.X, me.Y, now);
            return false;
        }
        if (now - a.T < 1500 || _lastMove < a.T)
            return false;

        _anchor = (me.X, me.Y, now);
        _stuckN++;
        var side = _stuckN % 2 == 1 ? 1 : -1;
        var baseAng = targetAng ?? _angle;
        _escAng = _stuckN >= 3
            ? baseAng + Math.PI + (random() - 0.5) * 0.8
            : baseAng + side * (Math.PI / 2 + random() * 0.5);
        _escUntil = now + 1300;
        if (target is not null && _stuckN >= 3)
        {
            _blocked.Add((target.X, target.Y, now + 20_000));
            _stuckN = 0;
        }
        // 繞圈時直接換方向
        if (targetAng is null)
            _angle = _escAng;
        return true;
    }

    /// <summary>
    /// 繞開障礙物：往 _escAng 方向點 0.3 遠。
    /// 跟 JS 小差異：JS 判定卡住的那一輪不點，下一輪（約 0.25 秒後）才開始繞；視覺模式一輪比較久，判定卡住當下就繞。
    /// </summary>
    private FieldDecision Escape(long now)
    {
        _lastMove = now;
        return Click(_lastAct = Stuck, 0.5 + Math.Cos(_escAng) * 0.3, 0.5 + Math.Sin(_escAng) * 0.3);
    }

    /// <summary>朝目標走一步：越遠步伐越大，最多 0.30</summary>
    private FieldDecision Go(string status, double dx, double dy, double len, long now)
    {
        _lastMove = now;
        var k = Math.Min(StepRadius, 0.06 + len * 0.03);
        return Click(_lastAct = status, 0.5 + dx / len * k, 0.5 + dy / len * k);
    }

    /// <summary>點擊座標夾在 0.05～0.95（同 JS clickWorld），避免點到畫面邊緣的 UI</summary>
    private static FieldDecision Click(string status, double fx, double fy)
        => new(status, new PointF((float)Math.Clamp(fx, 0.05, 0.95), (float)Math.Clamp(fy, 0.05, 0.95)));

    /// <summary>到目標的位移與距離；距離 0 時當 1（同 JS 的 || 1），避免除以 0</summary>
    private static (double Dx, double Dy, double Len) Delta(MinimapBlob me, MinimapBlob t)
    {
        double dx = t.X - me.X, dy = t.Y - me.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        return (dx, dy, len > 0 ? len : 1);
    }

    /// <summary>最近而且不在忽略範圍（忽略點 3 px 內）的目標，同 JS nearest＋notBlocked</summary>
    private MinimapBlob? Nearest(MinimapBlob me, IReadOnlyList<MinimapBlob> list, long now)
        => list
            .Where(t => !_blocked.Any(b => b.Until > now && Math.Sqrt((b.X - t.X) * (b.X - t.X) + (b.Y - t.Y) * (b.Y - t.Y)) < 3))
            .MinBy(t => (t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y));
}
