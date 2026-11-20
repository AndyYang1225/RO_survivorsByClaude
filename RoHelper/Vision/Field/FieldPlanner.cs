namespace RoHelper.Vision.Field;

/// <summary>這一輪的走位決策：面板文字，以及要點的「世界畫面比例座標」（0～1，同 JS clickWorld；null＝這輪不點）</summary>
public sealed record FieldDecision(string Status, PointF? Click);

/// <summary>
/// 野外走位決策（移植 engine.js fieldStep 的「小地圖模式」、wanderStep、checkStuck、bellStep）：
/// 躲到鐘下（最優先，見 <see cref="BellDodger"/>）→ 王死掉的地點（bossSpot）→ 撿王的掉落物 → 節慶扭蛋（畫面顏色，見 <see cref="BallFinder"/>）→ 磁鐵 → 寶箱（farLimit：太遠的先不追）→ 回地圖中間／繞圈；一直下指令但小地圖上的自己沒動就繞路；站在掉落物上 3 秒還在就忽略它。
/// MVP 死後的撿取倒數（lootPhase，見 <see cref="TimerReader"/>）只撿掉落物，沒有掉落物就在附近小範圍繞圈找。
/// 純邏輯：時間與亂數由呼叫端傳入（亂數注入才能寫測試），不截圖、不送輸入。
/// 刻意不做（第二批）：精確模式（JS 讀遊戲資料）、吃肉。
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
    private (MinimapBlob T, long At)? _lastLoot;
    private long _ballSince;
    private long _ballMute;
    private readonly BossTracker _bosses = new();
    private long _bossArrive;
    private readonly BellDodger _bell = new();

    /// <summary>躲鐘的狀態（呼叫端把公告文字交給它、問它要不要掃鐘；開發介面看剩幾秒）</summary>
    public BellDodger Bell => _bell;

    /// <summary>最近判定的王死掉地點（給開發介面看；null＝沒有）</summary>
    public BossSpot? BossSpot => _bosses.Spot;

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
        _lastLoot = null;
        _ballSince = 0;
        _ballMute = 0;
        _bosses.Reset();
        _bossArrive = 0;
        _bell.End();
    }

    /// <summary>
    /// 決定這一輪怎麼走。chestAffordable＝錢夠不夠開寶箱（呼叫端讀 HUD 算，見 <see cref="FieldSettings.CanAffordChest"/>）：
    /// 不夠就不追寶箱（同 JS：seekChest 且 canAffordChest() 才追），開不了的寶箱追過去只會一直站在上面或卡在旁邊。
    /// hp＝HP 比例（0～1，見 <see cref="HpReader"/>；null＝讀不到，不檢查）：lowHpStop 開著而且低於 lowHpStopN% 就不走位（同 JS）。
    /// lootPhase＝MVP 死後的撿取倒數中（計時器變紅字，見 <see cref="TimerReader"/>）：只撿掉落物，不追扭蛋、寶箱、磁鐵（同 JS 1126～1148 行）。
    /// balls＝畫面上像扭蛋的色塊（<see cref="BallFinder.Find"/>，離畫面中心近的在前面；null 或空＝沒看到）。
    /// bell＝躲鐘中這一輪掃到的鐘（<see cref="BellFinder.Find"/>；null＝沒掃）。公告由呼叫端交給 <see cref="Bell"/>。
    /// </summary>
    public FieldDecision Step(MinimapInfo? mm, FieldSettings s, long now, bool chestAffordable = true, double? hp = null, bool lootPhase = false,
        IReadOnlyList<BallBlob>? balls = null, BellView? bell = null)
    {
        // 刻意每輪都追蹤王（JS 只在 SeekEvery 之後才追蹤）：王的標記會閃，視覺模式一輪又比較久，
        // 被 SeekEvery 跳過的那幾輪也要記到「最後看到的時間」，才不會把閃爍誤判成死掉。
        // 躲鐘、HP 過低不走位時也照樣追蹤，免得那段時間看不到王、之後把王誤判成死掉
        if (mm is not null)
            _bosses.Update(mm, now);
        // 躲鐘最優先（同 JS fieldStep：在 HP 過低、SeekEvery 之前）；MVP 死了（撿取倒數）立刻結束（同 JS：鐘只在 MVP 戰鬥中出現）
        if (!s.BellDodge || lootPhase)
            _bell.End();
        if (_bell.Step(bell, now) is { } dodge)
        {
            if (dodge.Click is not null)
                _lastMove = now;
            return dodge;
        }
        // 同 JS：在「走位開關都關了」和 SeekEvery 之前檢查
        if (s.LowHpStop && hp < s.LowHpStopN / 100.0)
            return new("HP 過低，停止走位", null);
        if (!s.SeekLoot && !s.SeekBall && !s.SeekMagnet && !s.SeekChest && !s.AutoWander)
            return new("", null);
        if (now - _lastMove < s.SeekEvery)
            return new(_lastAct, null);

        _blocked.RemoveAll(b => b.Until <= now);

        // 正在繞開障礙物
        if (_escUntil > now)
            return Escape(now);

        // 讀不到小地圖：倒數中照樣小範圍繞圈；扭蛋是看畫面找的，不需要小地圖（只是沒辦法做卡住偵測）
        if (mm is null)
        {
            if (lootPhase)
                return LootWander(s, now);
            return BallStep(null, balls, s, now) ?? Wander(null, s, now);
        }
        var me = mm.Me;

        // 王剛死：跑到死掉的地點（最多 12 秒；到了之後停 1.5 秒讓東西被撿起來）
        if (_bosses.Spot is { } spot)
        {
            if (now - spot.At >= 12_000)
            {
                _bosses.Spot = null;
                _bossArrive = 0;
            }
            // 小地圖上有掉落物就交給下面的撿掉落物（同 JS 1124 行）。跟 JS 小差異：JS 這時已經往地點點了一次、下面又往掉落物點一次；
            // 這裡一輪只出一個決策，有掉落物就直接不走地點這段（地點留著，撿完掉落物還沒過 12 秒就接著去）
            else if (s.BossSpot && mm.Loot.Count == 0)
                return BossSpotStep(me, spot, now);
        }

        // 王的掉落物最優先（MVP 死後撿取時間有限）
        var loot = s.SeekLoot ? Nearest(me, mm.Loot, now) : null;
        // 掉落物在小地圖上會閃，倒數中看不到時就照 2.5 秒內最後看到的位置繼續走
        if (loot is not null)
            _lastLoot = (loot, now);
        else if (s.SeekLoot && lootPhase && _lastLoot is { } last && now - last.At < 2500)
            loot = last.T;
        if (loot is not null)
        {
            var (dx, dy, len) = Delta(me, loot);
            // 已經站在上面 3 秒還在 → 不是掉落物（可能是地形顏色），忽略 30 秒（撿取倒數中不放棄）
            if (len < 3 && !lootPhase)
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
            return Go(lootPhase ? "倒數中！撿王的掉落物" : "撿王的掉落物", dx, dy, len, now);
        }

        // 倒數中只撿掉落物（不去撿寶箱、磁鐵）；沒有掉落物就繞圈找
        if (lootPhase)
            return LootWander(s, now);

        // 節慶扭蛋：直接點球所在的位置
        if (BallStep(me, balls, s, now) is { } ball)
            return ball;

        // 磁鐵優先於寶箱；太遠的先不追（掉落物不受限，同 JS）
        var farMax = FarMax(mm, s);
        var magnet = s.SeekMagnet ? Nearest(me, Near(me, mm.Magnets, farMax), now) : null;
        var chest = s.SeekChest && chestAffordable ? Nearest(me, Near(me, mm.Chests, farMax), now) : null;
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
    /// 王死掉的地點（同 JS mapStep 的 bossSpot 段，距離改成小地圖像素）：離地點 &lt; 2 px（JS 3 格）算到了，停 1.5 秒後清掉，不點；
    /// 還沒到就往那邊走（步伐同撿掉落物）。卡住時回繞路的決策。
    /// </summary>
    private FieldDecision BossSpotStep(MinimapBlob me, BossSpot spot, long now)
    {
        double dx = spot.X - me.X, dy = spot.Y - me.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 2)
        {
            if (_bossArrive == 0)
                _bossArrive = now;
            if (now - _bossArrive > 1500)
            {
                _bosses.Spot = null;
                _bossArrive = 0;
            }
            return new(_lastAct = "在王死掉的地方撿東西", null);
        }
        _bossArrive = 0;
        if (CheckStuck(now, me, Math.Atan2(dy, dx), null))
            return Escape(now);
        return Go("跑到王死掉的地方", dx, dy, len, now);
    }

    /// <summary>
    /// 節慶扭蛋（同 JS ballTarget＋mapStep 的扭蛋段）：往最近的一顆走，點球的位置（往下 8 px，見 <see cref="BallBlob.Click"/>），狀態「前往扭蛋（顏色）」。
    /// 一直看到扭蛋超過 6 秒（多半不是球，例如技能特效）→ 先忽略 15 秒；看不到就重新計時。有小地圖時照樣做卡住偵測（往扭蛋的方向）。
    /// 沒有要追的扭蛋時回 null，交給下面的磁鐵、寶箱。
    /// </summary>
    private FieldDecision? BallStep(MinimapBlob? me, IReadOnlyList<BallBlob>? balls, FieldSettings s, long now)
    {
        if (!s.SeekBall || now < _ballMute)
            return null;
        if (balls is not { Count: > 0 })
        {
            _ballSince = 0;
            return null;
        }
        if (_ballSince == 0)
            _ballSince = now;
        if (now - _ballSince > 6000)
        {
            _ballMute = now + 15_000;
            _ballSince = 0;
            return null;
        }
        var b = balls[0];
        if (me is not null && CheckStuck(now, me, Math.Atan2(b.Click.Y - 0.5, b.Click.X - 0.5), null))
            return Escape(now);
        _lastMove = now;
        return Click(_lastAct = $"前往扭蛋（{b.Kind}）", b.Click.X, b.Click.Y);
    }

    /// <summary>
    /// 撿取倒數中沒看到掉落物（同 JS 1146 行）：每 WanderEvery 轉 60°，在附近小範圍繞圈（橫 0.25、直 0.2）找掉落物；不做卡住偵測（同 JS）。
    /// 跟 JS 小差異：JS 只設 st.action，沒設 lastAct，SeekEvery 內的那幾輪面板會顯示上一個動作；這裡也記成 lastAct，面板一直顯示「倒數中：找掉落物」。
    /// </summary>
    private FieldDecision LootWander(FieldSettings s, long now)
    {
        const string status = "倒數中：找掉落物";
        _lastAct = status;
        if (!s.AutoWander || now - _lastMove <= s.WanderEvery)
            return new(status, null);
        _lastMove = now;
        _angle += Math.PI / 3;
        return Click(status, 0.5 + Math.Cos(_angle) * 0.25, 0.5 + Math.Sin(_angle) * 0.2);
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

    /// <summary>
    /// farLimit（同 JS preciseStep 的 farMax）：目標離自己超過地圖長邊的一半就先不追，免得追到地圖角落。
    /// JS 用格子換算地圖大小；視覺模式直接用小地圖像素（地圖本體的外框）。外框太小（≤ 20 px，多半是沒讀到）時不限制，同 JS 的 M.h > 20。
    /// </summary>
    private static double FarMax(MinimapInfo mm, FieldSettings s)
        => s.FarLimit && mm.Map.Width > 20 && mm.Map.Height > 20 ? Math.Max(mm.Map.Width, mm.Map.Height) * 0.5 : double.PositiveInfinity;

    /// <summary>離自己 farMax 以內的目標</summary>
    private static IEnumerable<MinimapBlob> Near(MinimapBlob me, IReadOnlyList<MinimapBlob> list, double farMax)
        => list.Where(t => Math.Sqrt((t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y)) < farMax);

    /// <summary>最近而且不在忽略範圍（忽略點 3 px 內）的目標，同 JS nearest＋notBlocked</summary>
    private MinimapBlob? Nearest(MinimapBlob me, IEnumerable<MinimapBlob> list, long now)
        => list
            .Where(t => !_blocked.Any(b => b.Until > now && Math.Sqrt((b.X - t.X) * (b.X - t.X) + (b.Y - t.Y) * (b.Y - t.Y)) < 3))
            .MinBy(t => (t.X - me.X) * (t.X - me.X) + (t.Y - me.Y) * (t.Y - me.Y));
}
