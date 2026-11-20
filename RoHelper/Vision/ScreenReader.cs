namespace RoHelper.Vision;

/// <summary>
/// 把一張截圖變成 <see cref="ScreenState"/>：逐一用範本找大廳流程的按鈕。
/// 刻意先找按鈕、都沒有才找 NPC 名字和交談提示：後兩個要整張畫面找（比較花時間），而有按鈕的畫面一定不是在大廳走路。
/// lobby＝false（沒開活動）時也不找後兩個。
/// 「未完成的一局」的捨棄、「已捨棄本局」的 OK、首頁和角色選擇的開始遊戲算按鈕那一組，一律找（沒存範本時 Find 回 null，不花時間）。
/// inField＝true（在野外）時只找「返回角色」：野外畫面不可能有其他按鈕，全部比對一輪約 400 ms，會拖慢走位。
/// 斷線回首頁之類的情況由呼叫端定時做一次完整辨識來涵蓋（見 VisionEngine）。
/// </summary>
public sealed class ScreenReader
{
    private readonly TemplateLibrary _templates;

    public ScreenReader(TemplateLibrary templates) => _templates = templates;

    public ScreenState Read(Frame frame, double dpr, bool lobby, bool inField = false)
    {
        Rectangle? Find(string name) => _templates.Find(frame, name, dpr)?.Bounds;

        // Missing 照樣算（只查字典，不花時間）：面板的「缺少範本」提示不會因為進野外就忽隱忽現
        var missing = ScreenTemplates.All.Where(n => _templates.Get(n) is null).ToList();
        if (inField)
            return new ScreenState(new Size(frame.Width, frame.Height), dpr) { Return = Find(ScreenTemplates.Return), Missing = missing };

        var s = new ScreenState(new Size(frame.Width, frame.Height), dpr)
        {
            Solo = Find(ScreenTemplates.Solo),
            Return = Find(ScreenTemplates.Return),
            Start = Find(ScreenTemplates.Start),
            GoField = Find(ScreenTemplates.GoField),
            Discard = Find(ScreenTemplates.Discard),
            DiscardOk = Find(ScreenTemplates.DiscardOk),
            Title = Find(ScreenTemplates.Title),
            CharSelect = Find(ScreenTemplates.CharSelect),
            Missing = missing,
        };
        if (!lobby || s.Solo is not null || s.Return is not null || s.Start is not null || s.GoField is not null
            || s.Discard is not null || s.DiscardOk is not null || s.Title is not null || s.CharSelect is not null)
            return s;
        return s with
        {
            TalkPrompt = Find(ScreenTemplates.TalkPrompt),
            FestivalHost = Find(ScreenTemplates.FestivalHost),
        };
    }
}
