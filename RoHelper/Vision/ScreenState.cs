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

    /// <summary>「未完成的一局」對話框的「捨棄」按鈕（遊戲 0.7.8 起，被中斷的局再上線時出現）</summary>
    public const string Discard = "捨棄本局";

    /// <summary>按了捨棄之後「已捨棄本局」對話框的「OK」按鈕</summary>
    public const string DiscardOk = "確定捨棄";

    /// <summary>「暫離」對話框的「接受」（5 分鐘沒動作被踢回首頁時出現，例如暫停太久）</summary>
    public const string AfkOk = "暫離接受";

    /// <summary>新帳號第一次進大廳的「中文頻道」對話框的「現在不要」</summary>
    public const string ChannelNo = "中文頻道現在不要";

    /// <summary>遊戲首頁的「開始遊戲！」大藍按鈕（重開程式或斷線後會回到首頁）</summary>
    public const string Title = "首頁開始遊戲";

    /// <summary>角色選擇畫面右下的「開始遊戲」按鈕</summary>
    public const string CharSelect = "選角開始遊戲";

    /// <summary>大廳裡遠征隊長頭上的名字（會跟著畫面移動，整張畫面找）</summary>
    public const string ExpeLeader = "遠征隊長";

    /// <summary>走到遠征隊長旁邊時出現的「與隊長交談 空格」提示（跟節慶主持人的「與主持人交談」不同，要另外存）</summary>
    public const string LeaderPrompt = "隊長交談提示";

    /// <summary>遠征視窗（地圖清單）左上角的標題「◆ 遠征」：清單、詳細資料的位置都以它為準</summary>
    public const string StageList = "遠征標題";

    /// <summary>遠征視窗右下角藍色的「出發」按鈕（灰色的「劇情」不算）</summary>
    public const string ExpeGo = "遠征出發";

    /// <summary>按了出發之後「難度」對話框的標題「◆ 難度」：滑桿、「前往野外」的位置都以它為準</summary>
    public const string DiffTitle = "難度標題";

    /// <summary>組隊：隊長的難度對話框裡取代「前往野外」的「向隊伍提議」按鈕</summary>
    public const string PartyPropose = "向隊伍提議";

    /// <summary>組隊：隊員收到「X 想與隊伍一同出發：地圖」時的「準備好了！」按鈕</summary>
    public const string PartyReady = "準備好了";

    /// <summary>伺服器維護畫面（首頁右邊「維護中／我們正在更新伺服器」）的「再檢查一次」按鈕</summary>
    public const string MaintenanceRetry = "維護再檢查";

    /// <summary>組隊結算畫面「隊伍戰利品」的「其餘全 Greed」按鈕（旁邊是藍色的「準備好了」，倒數約 15 秒，期間「返回角色」是灰的）</summary>
    public const string LootGreed = "其餘全Greed";

    /// <summary>組隊：「隊長在頻道 N。要加入嗎？」的「前往頻道」按鈕（範本只截「頻道」兩個字：「前往」跟難度對話框的「前往野外」一樣，會認錯）</summary>
    public const string PartyChannel = "前往頻道";

    /// <summary>自動遠征開著時一定要有的範本（沒存的列在 <see cref="ScreenState.Missing"/>）</summary>
    public static readonly string[] Expedition = [ExpeLeader, LeaderPrompt, StageList, ExpeGo, DiffTitle];

    /// <summary>
    /// 一定要有的範本（沒存的會列在 <see cref="ScreenState.Missing"/> 提醒使用者）。
    /// 刻意不含 <see cref="Discard"/>、<see cref="DiscardOk"/>：那兩個對話框只在局被中斷時才出現，是選用範本，沒存也不提醒。
    /// 同理不含 <see cref="Title"/>、<see cref="CharSelect"/>：只在重開或斷線後才會看到。
    /// </summary>
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
    public Rectangle? Discard { get; init; }
    public Rectangle? DiscardOk { get; init; }
    public Rectangle? AfkOk { get; init; }
    public Rectangle? ChannelNo { get; init; }
    public Rectangle? Title { get; init; }
    public Rectangle? CharSelect { get; init; }
    public Rectangle? ExpeLeader { get; init; }
    public Rectangle? LeaderPrompt { get; init; }
    public Rectangle? StageList { get; init; }
    public Rectangle? ExpeGo { get; init; }
    public Rectangle? DiffTitle { get; init; }

    /// <summary>難度對話框裡的「前往野外」（用「前往野外」範本在難度標題下方找，見 <see cref="ScreenReader"/>）</summary>
    public Rectangle? DiffGo { get; init; }

    /// <summary>燈籠節視窗的分頁、九連抽／製作香按鈕、點香勾選框（有「開始遊戲」時才讀，見 <see cref="FestivalPanel"/>）</summary>
    public FestivalPanel? Festival { get; init; }

    /// <summary>伺服器維護中的「再檢查一次」</summary>
    public Rectangle? MaintenanceRetry { get; init; }

    /// <summary>組隊結算：「其餘全 Greed」</summary>
    public Rectangle? LootGreed { get; init; }

    /// <summary>組隊結算：「其餘全 Greed」右邊的「準備好了」（用「準備好了」範本在 Greed 按鈕右邊找）</summary>
    public Rectangle? LootReady { get; init; }

    /// <summary>組隊：DiffGo 是「向隊伍提議」（隊長）而不是「前往野外」</summary>
    public bool DiffGoIsProposal { get; init; }

    /// <summary>組隊：隊員的「準備好了！」</summary>
    public Rectangle? PartyReady { get; init; }

    /// <summary>組隊：「前往頻道 N」（加入隊長所在的頻道）</summary>
    public Rectangle? PartyChannel { get; init; }

    /// <summary>
    /// 組隊邀請「X 邀請你加入隊伍。」的「接受」按鈕。跟「暫離」的「接受」長得一樣（同一個範本），
    /// VisionEngine 讀對話框文字有「邀請」才放這裡（並清掉 AfkOk）
    /// </summary>
    public Rectangle? PartyInvite { get; init; }

    /// <summary>邀請者是本機設成隊長的分身（PartyRoster.LeaderNames）</summary>
    public bool PartyInviteTrusted { get; init; }

    /// <summary>組隊：「選擇大廳」視窗裡要選的那個多人大廳那一列（VisionEngine 用 OCR 找，見 LobbyListReader）</summary>
    public Rectangle? PartyLobby { get; init; }
    public IReadOnlyList<string> Missing { get; init; } = [];
}
