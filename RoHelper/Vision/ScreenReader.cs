using System.Runtime.ExceptionServices;

namespace RoHelper.Vision;

/// <summary>
/// 把一張截圖變成 <see cref="ScreenState"/>：逐一用範本找大廳流程的按鈕。
/// 刻意先找按鈕、都沒有才找 NPC 名字和交談提示：後兩個要整張畫面找（比較花時間），而有按鈕的畫面一定不是在大廳走路。
/// lobby＝false（沒開活動）時也不找後兩個。
/// 「未完成的一局」的捨棄、「已捨棄本局」的 OK、首頁和角色選擇的開始遊戲算按鈕那一組，一律找（沒存範本時 Find 回 null，不花時間）。
/// inField＝true（在野外）時只找「返回角色」：野外畫面不可能有其他按鈕，全部比對一輪約 400 ms，會拖慢走位。
/// 斷線回首頁之類的情況由呼叫端定時做一次完整辨識來涵蓋（見 VisionEngine）。
/// expe＝true（自動遠征）時另外找遠征的範本：遠征視窗標題、難度標題算按鈕那一組；沒有按鈕時才找遠征隊長、隊長交談提示。
/// 遠征的範本除了遠征隊長（會跟著畫面移動，整張畫面找）都用這裡寫死的範圍找（<see cref="TemplateLibrary.FindIn"/>，不用範本存的範圍），原因：
/// 1. 開發介面存範本時沒辦法指定範圍（一律整張畫面）；整張畫面找會走金字塔，實測（ExpeditionScreenTests）小範本位置的奇偶跟範本不同時常找不到
///    （隊長交談提示、難度標題整張畫面找 0.86、0.85，限縮範圍後 1.0）。
/// 2. 「遠征出發」只在遠征視窗標題的右下方找、難度對話框的「前往野外」用既有的「前往野外」範本在難度標題下方找（對話框比活動的高，不在範本自己的範圍裡）。
/// 同一組裡互不相依的範本同時比對（<see cref="FindAll"/>），結果跟逐一比對相同。本身沒有狀態，可以在背景執行緒呼叫（VisionEngine 就是這樣用）。
/// </summary>
public sealed class ScreenReader
{
    private readonly TemplateLibrary _templates;

    public ScreenReader(TemplateLibrary templates) => _templates = templates;

    public ScreenState Read(Frame frame, double dpr, bool lobby, bool inField = false, bool expe = false)
    {
        // 先算好截圖的灰階：下面多個執行緒同時比對範本時不用各自第一次計算
        _ = frame.Gray;

        // Missing 照樣算（只查字典，不花時間）：面板的「缺少範本」提示不會因為進野外就忽隱忽現
        var missing = ScreenTemplates.All.Concat(expe ? ScreenTemplates.Expedition : []).Where(n => _templates.Get(n) is null).ToList();
        if (inField)
        {
            // 組隊結算的戰利品分配（在結算畫面上，野外模式也要看；範本沒存時 Find 直接回 null）
            var field = FindAll(frame, dpr, [(ScreenTemplates.Return, null), (ScreenTemplates.LootGreed, null)]);
            var fieldGreed = field[ScreenTemplates.LootGreed];
            return new ScreenState(new Size(frame.Width, frame.Height), dpr)
            {
                Return = field[ScreenTemplates.Return],
                LootGreed = fieldGreed,
                LootReady = LootReady(fieldGreed),
                Missing = missing,
            };
        }

        // 步驟 1：按鈕那一組。彼此互不相依，同時找；要靠別的範本位置決定範圍的（戰利品的準備好、遠征出發、難度的前往野外）等這一組找完再找
        List<(string Name, Rectangle? Area)> buttons =
        [
            (ScreenTemplates.Solo, null),
            (ScreenTemplates.Return, null),
            (ScreenTemplates.Start, null),
            (ScreenTemplates.GoField, null),
            (ScreenTemplates.Discard, null),
            (ScreenTemplates.DiscardOk, null),
            (ScreenTemplates.AfkOk, null),
            (ScreenTemplates.ChannelNo, null),
            (ScreenTemplates.Title, null),
            (ScreenTemplates.CharSelect, null),
            // 組隊的兩個對話框（範本沒存時 Find 直接回 null，不花時間）
            (ScreenTemplates.PartyReady, null),
            (ScreenTemplates.PartyChannel, null),
            (ScreenTemplates.LootGreed, null),
            (ScreenTemplates.MaintenanceRetry, null),
        ];
        if (expe)
        {
            buttons.Add((ScreenTemplates.StageList, StageListArea(frame, dpr)));
            buttons.Add((ScreenTemplates.DiffTitle, DiffTitleArea(frame, dpr)));
        }
        var hit = FindAll(frame, dpr, buttons);
        var greed = hit[ScreenTemplates.LootGreed];
        var s = new ScreenState(new Size(frame.Width, frame.Height), dpr)
        {
            Solo = hit[ScreenTemplates.Solo],
            Return = hit[ScreenTemplates.Return],
            Start = hit[ScreenTemplates.Start],
            GoField = hit[ScreenTemplates.GoField],
            Discard = hit[ScreenTemplates.Discard],
            DiscardOk = hit[ScreenTemplates.DiscardOk],
            AfkOk = hit[ScreenTemplates.AfkOk],
            ChannelNo = hit[ScreenTemplates.ChannelNo],
            Title = hit[ScreenTemplates.Title],
            CharSelect = hit[ScreenTemplates.CharSelect],
            PartyReady = hit[ScreenTemplates.PartyReady],
            PartyChannel = hit[ScreenTemplates.PartyChannel],
            LootGreed = greed,
            LootReady = LootReady(greed),
            MaintenanceRetry = hit[ScreenTemplates.MaintenanceRetry],
            Missing = missing,
        };
        if (expe)
        {
            var list = hit[ScreenTemplates.StageList];
            var diff = hit[ScreenTemplates.DiffTitle];
            // 組隊時難度對話框的按鈕是「向隊伍提議」（同一個位置）
            var go = diff is { } d ? FindIn(ScreenTemplates.GoField, DiffGoArea(d, dpr)) : null;
            var propose = diff is { } d2 && go is null ? FindIn(ScreenTemplates.PartyPropose, DiffGoArea(d2, dpr)) : null;
            s = s with
            {
                StageList = list,
                ExpeGo = list is { } l ? FindIn(ScreenTemplates.ExpeGo, ExpeGoArea(l, dpr, frame.Height)) : null,
                DiffTitle = diff,
                DiffGo = go ?? propose,
                DiffGoIsProposal = go is null && propose is not null,
            };
        }
        if (s.Solo is not null || s.Return is not null || s.Start is not null || s.GoField is not null
            || s.Discard is not null || s.DiscardOk is not null || s.AfkOk is not null || s.ChannelNo is not null || s.Title is not null || s.CharSelect is not null
            || s.StageList is not null || s.DiffTitle is not null || s.PartyReady is not null || s.PartyChannel is not null || s.LootGreed is not null || s.MaintenanceRetry is not null)
            return s;

        // 步驟 2：沒有按鈕才找會跟著畫面移動的 NPC 名字和交談提示（一樣同時找）
        List<(string Name, Rectangle? Area)> walkers = [];
        if (expe)
        {
            walkers.Add((ScreenTemplates.LeaderPrompt, LeaderPromptArea(frame, dpr)));
            walkers.Add((ScreenTemplates.ExpeLeader, null));
        }
        if (lobby)
        {
            walkers.Add((ScreenTemplates.TalkPrompt, null));
            walkers.Add((ScreenTemplates.FestivalHost, null));
        }
        // 這幾個範本都是整張畫面走一半大小的金字塔：先算好一次，不用每個平行的執行緒各算一遍
        if (walkers.Count > 0)
            _ = frame.Half.Gray;
        var seen = FindAll(frame, dpr, walkers);
        if (expe)
            s = s with { LeaderPrompt = seen[ScreenTemplates.LeaderPrompt], ExpeLeader = seen[ScreenTemplates.ExpeLeader] };
        if (lobby)
            s = s with { TalkPrompt = seen[ScreenTemplates.TalkPrompt], FestivalHost = seen[ScreenTemplates.FestivalHost] };
        return s;

        Rectangle? FindIn(string name, Rectangle area) => _templates.FindIn(frame, name, dpr, area)?.Bounds;

        // 戰利品分配的「準備好了」在「其餘全 Greed」右邊找
        Rectangle? LootReady(Rectangle? greedAt) => greedAt is { } g
            ? FindIn(ScreenTemplates.PartyReady, Rectangle.Intersect(new Rectangle(g.Right, g.Y - (int)(15 * dpr), (int)(200 * dpr), g.Height + (int)(30 * dpr)), new Rectangle(0, 0, frame.Width, frame.Height)))
            : null;
    }

    /// <summary>
    /// 同時比對多個範本：每個範本的比對互不相依，結果跟一個一個找完全一樣。Area＝null 時用範本自己存的搜尋範圍，否則只在 Area 裡找。
    /// 同時跑的數量限制在核心數的一半：遊戲畫面（WebView2）也要 CPU，全部核心都拿來比對會讓遊戲本身掉幀。
    /// 比對中丟的例外拆開 AggregateException 再拋，面板顯示的才是真正的原因（不是「發生一或多個錯誤」）。
    /// </summary>
    private Dictionary<string, Rectangle?> FindAll(Frame frame, double dpr, IReadOnlyList<(string Name, Rectangle? Area)> finds)
    {
        var found = new Rectangle?[finds.Count];
        try
        {
            Parallel.For(0, finds.Count, Parallelism, i =>
            {
                var (name, area) = finds[i];
                found[i] = (area is { } a ? _templates.FindIn(frame, name, dpr, a) : _templates.Find(frame, name, dpr))?.Bounds;
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
        var result = new Dictionary<string, Rectangle?>(finds.Count);
        for (var i = 0; i < finds.Count; i++)
            // 用 Add：範本名稱重複要直接丟例外，不能默默蓋掉前一個的結果
            result.Add(finds[i].Name, found[i]);
        return result;
    }

    private static readonly ParallelOptions Parallelism = new() { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) };

    /// <summary>遠征視窗標題的搜尋範圍：畫面左半、上面 1/3（實測標題範本在 (522, 32)，對話框水平置中）</summary>
    internal static Rectangle StageListArea(Frame f, double dpr) => new(0, 0, f.Width / 2, f.Height / 3);

    /// <summary>難度標題的搜尋範圍：畫面中心往左 300·dpr 到中心、上半部（實測標題範本在 (772, 168)，對話框置中）</summary>
    internal static Rectangle DiffTitleArea(Frame f, double dpr) => new(f.Width / 2 - (int)(300 * dpr), 0, (int)(300 * dpr), f.Height / 2);

    /// <summary>隊長交談提示的搜尋範圍：畫面下方中央 ±250·dpr、最下面 130·dpr（實測提示框 x 873～1030、y 602～642）</summary>
    internal static Rectangle LeaderPromptArea(Frame f, double dpr)
    {
        int half = (int)(250 * dpr), h = (int)(130 * dpr);
        return new Rectangle(f.Width / 2 - half, f.Height - h, half * 2, h);
    }

    /// <summary>
    /// 「出發」按鈕的搜尋範圍：實測（遠征選好地圖，dpr 1）標題範本在 (522, 32)、出發按鈕 x 1236～1394、y 609～644。
    /// 按鈕跟著對話框底部（視窗高度不同時位置不同），所以 x 取標題右邊 +700～+920、y 從標題下方 +400 到畫面底部。
    /// </summary>
    internal static Rectangle ExpeGoArea(Rectangle title, double dpr, int frameHeight)
    {
        int x = title.X + (int)(700 * dpr), y = title.Y + (int)(400 * dpr);
        return new Rectangle(x, y, (int)(220 * dpr), Math.Max(0, frameHeight - y));
    }

    /// <summary>
    /// 難度對話框「前往野外」的搜尋範圍：實測（遠征難度，dpr 1）標題範本在 (772, 168)、前往野外按鈕 x 1060～1144、y 476～508，
    /// 取標題右下 +250～+420、+280～+370（按鈕四周各留 40 px 以上）。
    /// </summary>
    internal static Rectangle DiffGoArea(Rectangle title, double dpr)
        => new(title.X + (int)(250 * dpr), title.Y + (int)(280 * dpr), (int)(170 * dpr), (int)(90 * dpr));
}
