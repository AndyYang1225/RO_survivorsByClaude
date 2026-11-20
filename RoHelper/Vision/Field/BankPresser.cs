namespace RoHelper.Vision.Field;

/// <summary>這一輪要不要按 B：Press＝要按、Status＝面板動作文字（空字串＝這一輪不處理升級，照常走位）、Note＝附在動作後面的提醒</summary>
public sealed record BankStep(bool Press, string Status, string Note);

/// <summary>
/// 累積升級時按 B 打開選卡（移植 engine.js fieldStep 1072～1085 的 canBank／pressBank）。
/// 順序同 JS：躲鐘之後（呼叫端躲鐘中不呼叫）；MVP 前 preMvpN 秒（preMvp）→「MVP 還有 N 秒，先選完升級」（就算沒勾 autoUpgrade）；
/// autoUpgrade 而且不在撿取倒數 →「打開升級」。
/// 按了 1.5 秒還沒出現選卡畫面（組隊時只有隊長能開、或遊戲當下不允許）→ 10 秒不按，面板附註「（升級打不開，10 秒後再試；組隊時可能只有隊長能開）」。
///
/// 跟 JS 不同：JS 在 1.5 秒內每輪都點一次按鈕；這裡每次嘗試只按一次 B，之後只等選卡畫面出現。
/// 原因：截圖辨識比較慢，按了之後下一輪可能還沒看到選卡畫面，B 如果是開關的話再按一次會把剛打開的選卡關掉。
/// 純邏輯：時間由呼叫端傳入，看到選卡畫面時呼叫端要呼叫 <see cref="PickerSeen"/>（同 JS 選卡時清 bankTry）。
/// </summary>
public sealed class BankPresser
{
    /// <summary>按了之後等選卡畫面多久（同 JS 1.5 秒）</summary>
    private const long TryWindow = 1500;

    /// <summary>打不開時多久不按（同 JS 10 秒）</summary>
    private const long MuteMs = 10_000;

    public const string MuteNote = "（升級打不開，10 秒後再試；組隊時可能只有隊長能開）";

    private long? _try;
    private long _mute = long.MinValue / 2;

    /// <summary>看到選卡畫面了（按 B 有效）：清掉這次嘗試</summary>
    public void PickerSeen() => _try = null;

    /// <summary>新的一局</summary>
    public void NewRun()
    {
        _try = null;
        _mute = long.MinValue / 2;
    }

    /// <summary>
    /// 一輪的決定。ready＝標籤是白的（N &gt; 0）；mvpLeft＝MVP 還有幾秒（null＝不知道）；lootPhase＝MVP 死後的撿取倒數。
    /// </summary>
    public BankStep Step(bool ready, int? mvpLeft, bool lootPhase, bool autoUpgrade, bool preMvp, int preMvpN, long now)
    {
        var canBank = ready && now > _mute;
        if (!canBank)
            _try = null;
        string? label = null;
        if (canBank && preMvp && mvpLeft is { } ml && ml > 0 && ml <= preMvpN)
            label = $"MVP 還有 {ml} 秒，先選完升級";
        else if (canBank && !lootPhase && autoUpgrade)
            label = "打開升級";
        if (label is not null)
        {
            if (_try is { } t && now - t > TryWindow)
            {
                _try = null;
                _mute = now + MuteMs;
            }
            else
            {
                var press = _try is null;
                _try ??= now;
                return new BankStep(press, label, "");
            }
        }
        return new BankStep(false, "", ready && now < _mute ? MuteNote : "");
    }
}
