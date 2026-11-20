namespace RoHelper.Vision;

/// <summary>
/// 把一張截圖變成 <see cref="ScreenState"/>：逐一用範本找大廳流程的按鈕。
/// 刻意先找按鈕、都沒有才找 NPC 名字和交談提示：後兩個要整張畫面找（比較花時間），而有按鈕的畫面一定不是在大廳走路。
/// lobby＝false（沒開活動）時也不找後兩個。
/// </summary>
public sealed class ScreenReader
{
    private readonly TemplateLibrary _templates;

    public ScreenReader(TemplateLibrary templates) => _templates = templates;

    public ScreenState Read(Frame frame, double dpr, bool lobby)
    {
        Rectangle? Find(string name) => _templates.Find(frame, name, dpr)?.Bounds;

        var s = new ScreenState(new Size(frame.Width, frame.Height), dpr)
        {
            Solo = Find(ScreenTemplates.Solo),
            Return = Find(ScreenTemplates.Return),
            Start = Find(ScreenTemplates.Start),
            GoField = Find(ScreenTemplates.GoField),
            Missing = ScreenTemplates.All.Where(n => _templates.Get(n) is null).ToList(),
        };
        if (!lobby || s.Solo is not null || s.Return is not null || s.Start is not null || s.GoField is not null)
            return s;
        return s with
        {
            TalkPrompt = Find(ScreenTemplates.TalkPrompt),
            FestivalHost = Find(ScreenTemplates.FestivalHost),
        };
    }
}
