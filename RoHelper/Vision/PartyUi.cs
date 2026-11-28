using System.Text.RegularExpressions;

using RoHelper.Vision.Field;

namespace RoHelper.Vision;

/// <summary>
/// 組隊的畫面判讀（遊戲 2026.11.23 版，實測 dpr 1、1904×682）：
/// - 邀請對話框「◆ 隊伍／X 邀請你加入隊伍。／拒絕 接受」：文字 y 326，接受 x 1083～1144、y 368～400。
/// - 右下角隊伍面板「隊伍 N/3」：3 人時 x 1685～1888、y 415～662（人少時面板比較矮、上緣比較低），列出每個人的角色名。
/// - 好友視窗（右上「好友」按鈕 x 1517～1600、y 13～45）：每一列「● 角色名 ▾ 頻道 N」，點角色名跳出選單（查看／密語／交易／檢舉／〔前往頻道 N〕／邀請組隊），
///   選單往上展開、最後一項是「邀請組隊」；「關閉」在視窗右下。
/// 角色名比對只容許讀錯一個字（自己開的三隻都叫「…鮭魚」，寬鬆比對會認錯人）。
/// </summary>
public static partial class PartyUi
{
    /// <summary>OCR 讀到的文字像不像這個角色名：最長共同子序列 ≥ 名字長度 − 1（至少 2 個字）</summary>
    public static bool NameLike(string text, string name)
    {
        var t = Normalize(OcrReader.Compact(text));
        name = Normalize(name);
        if (t.Length == 0 || name.Length == 0)
            return false;
        // 實測 OCR 會讀錯兩個字（「紐西蘭鮭魚」→「紐西蕄鮏魚」），所以容許量取 60%：4 個字對 3 個、5 個字對 3 個；
        // 三隻都叫「…鮭魚」，共同的「鮭魚」只有 2 個字，不會互相認錯
        return t.Contains(name) || JobNames.Lcs(t, name) >= Math.Max(2, (int)Math.Ceiling(name.Length * 0.6));
    }

    /// <summary>
    /// 隊伍面板上的隊長是不是自己的隊長。完整對上（<see cref="NameLike"/>）就是；像自己或其他分身（others）的名字就不是；
    /// 其他情況只要跟隊長名字有 2 個字以上相同就當成讀錯（實測 2026-11-27 C：「紐西蘭鮭魚」連兩次讀成「西魚」，被當成別人的隊伍離開）
    /// </summary>
    public static bool IsOurLeader(string text, IReadOnlyList<string> leaders, IEnumerable<string> others)
    {
        if (leaders.Any(n => NameLike(text, n)))
            return true;
        if (others.Any(n => n.Length > 0 && NameLike(text, n)))
            return false;
        var t = Normalize(OcrReader.Compact(text));
        return leaders.Any(n => JobNames.Lcs(t, Normalize(n)) >= 2);
    }

    /// <summary>
    /// 實測（2026-11-23 好友視窗，放大 2 倍）OCR 常把「鮭」讀成「銈」；名字的第一個字常被前面的綠點吃掉（「挪威鮭魚」→「威鮭魚」，
    /// 靠「讀錯一個字」的容許量吸收）。兩邊都換成同一個字再比
    /// </summary>
    private static string Normalize(string s) => s.Replace('銈', '鮭');

    /// <summary>OCR 兩種放大倍數各讀一次、合併（實測 2 倍、3 倍各有讀錯的名字，合起來才都讀得到）</summary>
    public static async Task<IReadOnlyList<OcrLine>> ReadBothAsync(OcrReader ocr, Frame f, Rectangle area)
    {
        if (area.Width == 0 || area.Height == 0)
            return [];
        return [.. await ocr.ReadAsync(f, area, 3), .. await ocr.ReadAsync(f, area, 2)];
    }

    /// <summary>
    /// 「X 邀請你加入隊伍。」→ X；不是邀請回 null。實測 OCR 讀成「紐西蕄鮏魚請加入隊伍 ,」（「邀請你」只剩「請」），
    /// 所以用「加入隊伍」當錨點，再去掉前面殘留的「邀請你」各種讀法
    /// </summary>
    public static string? Inviter(string text)
    {
        var t = OcrReader.Compact(text).Replace(" ", "");
        var i = t.IndexOf("加入隊伍", StringComparison.Ordinal);
        if (i < 0)
            i = t.IndexOf("邀請", StringComparison.Ordinal);
        if (i <= 0)
            return null;
        var name = t[..i];
        foreach (var tail in new[] { "邀請你", "邀請", "請你", "請", "你" })
            if (name.EndsWith(tail, StringComparison.Ordinal))
            {
                name = name[..^tail.Length];
                break;
            }
        return name.Length > 0 ? name : null;
    }

    /// <summary>邀請對話框的文字範圍：接受按鈕上方 80～15 px、往左 420 px</summary>
    internal static Rectangle InviteTextArea(Frame f, double dpr, Rectangle accept)
        => Clip(f, Rectangle.FromLTRB(accept.Right - (int)(420 * dpr), accept.Y - (int)(80 * dpr), accept.Right, accept.Y - (int)(15 * dpr)));

    /// <summary>
    /// 按鈕上方對話框的文字（兩種倍數讀到的接在一起）：按鈕上方 130～15 px、往左 420 px。
    /// 比邀請的範圍高：活動的出發提議多一行「點香」勾選框，「想與隊伍一同出發」在按鈕上方約 87 px（實測 2026-11-27 C）
    /// </summary>
    public static async Task<string> ReadDialogTextAsync(OcrReader ocr, Frame f, double dpr, Rectangle button)
        => string.Join(" ", (await ReadBothAsync(ocr, f, Clip(f, Rectangle.FromLTRB(button.Right - (int)(420 * dpr), button.Y - (int)(130 * dpr),
            button.Right, button.Y - (int)(15 * dpr))))).Select(l => l.Text));

    /// <summary>「X 想與隊伍一同出發：地圖 · 戰利品 ×2」（隊長的出發提議）</summary>
    public static bool IsDepartProposal(string text)
    {
        var t = OcrReader.Compact(text);
        return t.Contains("出發") || t.Contains("一同");
    }

    /// <summary>讀邀請對話框：是組隊邀請回邀請者（可能是空字串＝名字讀不到），不是回 null</summary>
    public static async Task<string?> ReadInviterAsync(OcrReader ocr, Frame f, double dpr, Rectangle accept)
    {
        var area = InviteTextArea(f, dpr, accept);
        if (area.Width == 0 || area.Height == 0)
            return null;
        foreach (var line in await ReadBothAsync(ocr, f, area))
            if (Inviter(line.Text) is { } who)
                return who;
        return null;
    }

    /// <summary>
    /// 好友選單某一格（中心 center）是不是「邀請組隊」：單獨裁那一格讀，左右各偏一點、兩種倍數都試（實測 OCR 對裁切範圍很敏感，
    /// 同一格寬 104 讀得到「謮隊」、寬 120 就讀不到）。只認讀到「隊」或「邀請」；「檢舉」那一格怎麼裁都讀不出這些字
    /// </summary>
    public static async Task<bool> IsInviteCellAsync(OcrReader ocr, Frame f, double dpr, Point center)
    {
        foreach (var dx in new[] { 12, 0, -12 })
            foreach (var w in new[] { 104, 120 })
            {
                var cell = Clip(f, new Rectangle(center.X + (int)((dx - w / 2) * dpr), center.Y - (int)(12 * dpr), (int)(w * dpr), (int)(24 * dpr)));
                if (cell.Width == 0 || cell.Height == 0)
                    continue;
                foreach (var k in new[] { 2.0, 3.0 })
                    if ((await ocr.ReadAsync(f, cell, k)).Any(l => l.Text.Contains('隊') || l.Text.Contains("邀請")))
                        return true;
            }
        return false;
    }

    /// <summary>右下角隊伍面板的範圍</summary>
    internal static Rectangle PanelArea(Frame f, double dpr)
        => Clip(f, new Rectangle(f.Width - (int)(225 * dpr), f.Height - (int)(290 * dpr), (int)(215 * dpr), (int)(275 * dpr)));

    /// <summary>
    /// 讀隊伍面板：沒在隊伍裡（看不到「隊伍 N/3」）回 null；有的話回面板上所有文字行（拿來找誰已經在隊上）
    /// </summary>
    public static async Task<IReadOnlyList<string>?> ReadPanelAsync(OcrReader ocr, Frame f, double dpr)
    {
        var area = PanelArea(f, dpr);
        if (area.Width == 0 || area.Height == 0)
            return null;
        var lines = (await ReadBothAsync(ocr, f, area)).Select(l => l.Text).ToList();
        // 實測（2026-11-23）OCR 把標題讀成兩行「隊伍」「3」，所以只要有一行以「隊伍」開頭就算有面板
        return lines.Any(l => OcrReader.Compact(l).Replace(" ", "").StartsWith("隊伍", StringComparison.Ordinal)) ? lines : null;
    }

    /// <summary>讀隊伍面板（附位置，找「離開隊伍」要點）：沒在隊伍裡回 null</summary>
    public static async Task<IReadOnlyList<OcrLine>?> ReadPanelLinesAsync(OcrReader ocr, Frame f, double dpr)
    {
        var area = PanelArea(f, dpr);
        if (area.Width == 0 || area.Height == 0)
            return null;
        var lines = await ReadBothAsync(ocr, f, area);
        return lines.Any(l => OcrReader.Compact(l.Text).Replace(" ", "").StartsWith("隊伍", StringComparison.Ordinal)) ? lines : null;
    }

    /// <summary>
    /// 面板上的隊長名：隊長一定列在最上面（實測面板「隊伍 N/3」下面第一個名字，旁邊有「隊長」標籤）。
    /// 從上往下找第一行不是標題、職業等級、GS、頻道、按鈕的字；去掉「隊長」。找不到回 null
    /// </summary>
    public static string? PanelLeader(IEnumerable<OcrLine> lines)
    {
        foreach (var l in lines.OrderBy(l => l.Bounds.Y))
        {
            var t = OcrReader.Compact(l.Text).Replace(" ", "");
            if (t.Length == 0 || t.StartsWith("隊伍", StringComparison.Ordinal) || t.Contains("Lv") || t.Contains("GS", StringComparison.OrdinalIgnoreCase)
                || t.Contains("頻道") || t.Contains("離開") || t.Contains("巫師") || t.Contains("取消") || t.All(c => char.IsDigit(c) || c == '/' || c == '•' || c == '·'))
                continue;
            var name = t.Replace("隊長", "");
            if (name.Length >= 2)
                return name;
        }
        return null;
    }

    /// <summary>
    /// 面板最下面的「離開隊伍」按鈕：實測 OCR 只讀得到「隊伍」兩個字（「離開」讀不出來），
    /// 所以認「有『離開』」或「標題『隊伍』下面 100 px 以外的『隊伍』」。找不到回 null
    /// </summary>
    public static OcrLine? LeaveButton(IReadOnlyList<OcrLine> lines, double dpr)
    {
        if (lines.FirstOrDefault(l => l.Text.Contains("離開")) is { } leave)
            return leave;
        var header = lines.Where(l => OcrReader.Compact(l.Text).Replace(" ", "").StartsWith("隊伍", StringComparison.Ordinal)).MinBy(l => l.Bounds.Y);
        return header is null ? null
            : lines.Where(l => OcrReader.Compact(l.Text).Replace(" ", "") == "隊伍" && l.Bounds.Y > header.Bounds.Y + 100 * dpr).MaxBy(l => l.Bounds.Y);
    }

    /// <summary>還不在隊伍面板上的隊員（面板是 null＝自己沒有隊伍，全部都要邀）</summary>
    public static IReadOnlyList<string> Missing(IReadOnlyList<string> members, IReadOnlyList<string>? panel)
        => members.Where(m => panel is null || !panel.Any(l => NameLike(l, m))).ToList();

    /// <summary>右上角「好友」按鈕的中心（從畫面右緣算）</summary>
    public static Point FriendsButton(Frame f, double dpr) => new(f.Width - (int)Math.Round(346 * dpr), (int)Math.Round(29 * dpr));

    /// <summary>好友視窗的範圍：畫面中央 ±240 px、上下 18%～82%</summary>
    internal static Rectangle FriendsArea(Frame f, double dpr)
        => Clip(f, Rectangle.FromLTRB(f.Width / 2 - (int)(240 * dpr), (int)(f.Height * 0.18), f.Width / 2 + (int)(240 * dpr), (int)(f.Height * 0.82)));

    private static Rectangle Clip(Frame f, Rectangle r) => Rectangle.Intersect(r, new Rectangle(0, 0, f.Width, f.Height));
}
