using System.Text.RegularExpressions;

namespace RoHelper;

/// <summary>
/// 解析命令列的 <c>--profile=名稱</c>（多開用：每個 profile 有自己的設定、戰績、截圖、登入狀態，見 <see cref="AppPaths.ProfileDir"/>）。
/// 名稱只允許英文字母、數字、- 和 _，最長 32 字：會拿來當資料夾名稱和 Mutex 名稱，不能有路徑字元。
/// </summary>
public static partial class ProfileOptions
{
    /// <summary>
    /// 回傳 profile 名稱：沒帶 <c>--profile</c> 回空字串（跟以前一樣）；名稱不合法回 null，error 說明原因（呼叫端顯示後結束，
    /// 刻意不自動改名或退回預設：打錯名字卻默默用預設的話，會跟另一個視窗搶同一份設定和登入）
    /// </summary>
    public static string? Parse(string[] args, out string error)
    {
        error = "";
        var arg = args.LastOrDefault(a => a.StartsWith("--profile", StringComparison.OrdinalIgnoreCase));
        if (arg is null)
            return "";
        var eq = arg.IndexOf('=');
        var name = eq < 0 ? "" : arg[(eq + 1)..].Trim();
        if (!ValidName().IsMatch(name))
        {
            error = $"--profile 的名稱「{name}」不合法：只能用英文字母、數字、- 和 _（1～32 字），例如 --profile=A";
            return null;
        }
        return name;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex ValidName();
}
