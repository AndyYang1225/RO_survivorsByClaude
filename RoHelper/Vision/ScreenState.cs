namespace RoHelper.Vision;

/// <summary>
/// 大廳流程用到的範本名稱。除錯視窗存範本時要用一模一樣的名字，引擎才找得到。
/// </summary>
public static class ScreenTemplates
{
    /// <summary>「選擇大廳」視窗的「單人大廳」按鈕</summary>
    public const string Solo = "單人大廳";

    /// <summary>節慶主持人對話裡的「開始遊戲」</summary>
    public const string Start = "開始遊戲";

    /// <summary>「前往野外」按鈕</summary>
    public const string GoField = "前往野外";

    /// <summary>結算畫面的「返回角色」</summary>
    public const string Return = "返回角色";

    /// <summary>大廳裡節慶主持人頭上的名字（會跟著畫面移動，存範本時要勾「整張畫面找」）</summary>
    public const string FestivalHost = "節慶主持人";

    /// <summary>走到 NPC 旁邊時出現、點了會開始交談的提示（存範本時要勾「整張畫面找」）</summary>
    public const string TalkPrompt = "交談提示";

    public static readonly string[] All = [Solo, Start, GoField, Return, FestivalHost, TalkPrompt];
}

/// <summary>
/// 一張截圖裡認出來的東西。座標是截圖像素，沒找到是 null。
/// Missing＝範本庫裡還沒存的範本（顯示在面板上，提醒使用者去除錯視窗補）。
/// </summary>
public sealed record ScreenState(Size FrameSize, double Dpr)
{
    public Rectangle? Solo { get; init; }
    public Rectangle? Start { get; init; }
    public Rectangle? GoField { get; init; }
    public Rectangle? Return { get; init; }
    public Rectangle? FestivalHost { get; init; }
    public Rectangle? TalkPrompt { get; init; }
    public IReadOnlyList<string> Missing { get; init; } = [];
}
