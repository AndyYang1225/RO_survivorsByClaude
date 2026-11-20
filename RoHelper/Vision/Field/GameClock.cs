namespace RoHelper.Vision.Field;

/// <summary>
/// 這一局的遊戲時間（計時器平常往上數，同 JS runClock）。OCR 每 1 秒讀一次（<see cref="TimerReader.ReadClockAsync"/>），
/// 讀之間用經過的時間往後推，所以每輪都有值。
///
/// 跟 JS 不同：JS 看計時器的 class（mvp／loot）知道什麼時候變成倒數；視覺模式只認得撿取倒數的紅字（<see cref="TimerPhase.Loot"/>），
/// MVP 出現後的倒數長什麼樣子還沒在實機看過。所以另外用「時間不會倒退」判斷：讀到比上次讀到的值少超過 3 秒算倒退，
/// 連續 2 次倒退而且第 2 次比第 1 次更少（在往下數）→ 當成計時器開始倒數，這一局之後都不再提供遊戲時間（同 JS：倒數時 runClock 回 null）。
/// 讀到比推算值多太多（超過 5 秒）也是可疑的值。可疑的值先不用；下一次又可疑、而且跟上一個可疑的值對得上（沒有變少、增加的不超過經過的時間）
/// ＝之前讀到的才是錯的（OCR 偶爾讀錯一位數），改用新的值。
/// 刻意跟「上次讀到的值」比倒退，不跟推算值比：選卡時遊戲可能暫停計時，推算值會跑在前面，跟它比會誤判成倒退。
/// 純邏輯：時間由呼叫端傳入。
/// </summary>
public sealed class GameClock
{
    /// <summary>讀到的值比上次讀到的值少這麼多秒以上算「倒退」</summary>
    private const int BackSlack = 3;

    /// <summary>讀到的值比推算值多這麼多秒以上當讀錯（還沒讀到過時不檢查）</summary>
    private const int AheadSlack = 5;

    private (int Secs, long At)? _last;
    private (int Secs, long At, bool Back)? _odd;
    private bool _countdown;

    /// <summary>計時器已經變成倒數（MVP 出現了），這一局不再有遊戲時間</summary>
    public bool Countdown => _countdown;

    /// <summary>新的一局：全部清掉</summary>
    public void Reset()
    {
        _last = null;
        _odd = null;
        _countdown = false;
    }

    /// <summary>MVP 死後的撿取倒數（紅字）：計時器已經不是遊戲時間</summary>
    public void Loot() => _countdown = true;

    /// <summary>OCR 讀到一個值（秒）；讀不到（null）不改任何東西</summary>
    public void Read(int? secs, long now)
    {
        if (_countdown || secs is not { } s)
            return;
        if (_last is { } last)
        {
            var back = s < last.Secs - BackSlack;
            if (back || s > Now(now) + AheadSlack)
            {
                if (_odd is { } o && back && o.Back && s < o.Secs)
                    _countdown = true;
                else if (_odd is { } p && s >= p.Secs && s <= p.Secs + (now - p.At) / 1000 + AheadSlack)
                    (_last, _odd) = ((s, now), null);
                else
                    _odd = (s, now, back);
                return;
            }
        }
        _odd = null;
        _last = (s, now);
    }

    /// <summary>現在的遊戲時間（秒，用上次讀到的值加上經過的時間推算）；還沒讀到或已經倒數回 null</summary>
    public int? Now(long now)
    {
        if (_countdown || _last is not { } l)
            return null;
        return l.Secs + (int)((now - l.At) / 1000);
    }

    /// <summary>「mm:ss」（給對局紀錄的 gt，同 JS 計時器的文字）；沒有遊戲時間回空字串</summary>
    public string Text(long now) => Now(now) is { } s ? $"{s / 60:00}:{s % 60:00}" : "";
}
