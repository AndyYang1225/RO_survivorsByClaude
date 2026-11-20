namespace RoHelper.Vision.Field;

/// <summary>
/// 記住「現在在野外」：按了活動的「前往野外」（<see cref="FlowDecision.StartsRun"/>）就進野外；
/// 記一局（結算畫面第一次出現）、或畫面上認出單人大廳／開始遊戲／節慶主持人／返回角色任一範本就離開。
/// 刻意不用小地圖判斷在不在野外：大廳也可能有類似的顏色，誤判會在大廳亂點；只信任自己按的「前往野外」。
/// 純邏輯：時間由呼叫端傳入，可以單元測試。
/// </summary>
public sealed class FieldGate
{
    /// <summary>現在是不是在野外</summary>
    public bool InField { get; private set; }

    /// <summary>進野外的時間（毫秒，同呼叫端的時鐘）；不在野外時沒有意義</summary>
    public long Since { get; private set; }

    /// <summary>按了「前往野外」</summary>
    public void Enter(long now)
    {
        InField = true;
        Since = now;
    }

    public void Leave() => InField = false;

    /// <summary>
    /// 每輪辨識完、大廳流程決定後呼叫一次。先處理離開再處理進入：
    /// 同一輪按了「前往野外」時以進入為準（按鈕在野外畫面出現前的最後一格）。
    /// </summary>
    public void Update(ScreenState s, FlowDecision d, long now)
    {
        if (d.CountRun || s.Solo is not null || s.Start is not null || s.FestivalHost is not null || s.Return is not null)
            Leave();
        if (d.StartsRun)
            Enter(now);
    }

    /// <summary>畫面上有沒有任何大廳流程的按鈕或標籤（有的話這一輪不走位，交給大廳流程）</summary>
    public static bool LobbyVisible(ScreenState s)
        => s.Solo is not null || s.Start is not null || s.GoField is not null || s.Return is not null
            || s.FestivalHost is not null || s.TalkPrompt is not null;
}
