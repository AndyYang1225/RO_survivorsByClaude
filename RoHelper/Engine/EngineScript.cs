using System.Reflection;
using System.Text.RegularExpressions;

namespace RoHelper.Engine;

/// <summary>
/// 一份 JS 引擎原始碼和它檔頭宣告的版本。
/// 檔頭格式（engine.js 開頭的註解）：<c>// @version 8.0.0</c>、<c>// @min-exe 8.0.0</c>。
/// </summary>
public sealed partial class EngineScript
{
    private EngineScript(string source, Version version, Version minExe, string origin)
    {
        Source = source;
        Version = version;
        MinExe = minExe;
        Origin = origin;
    }

    public string Source { get; }
    public Version Version { get; }

    /// <summary>需要的最低 exe 版本</summary>
    public Version MinExe { get; }

    /// <summary>來源說明（內建、已更新、外部檔案），顯示在選單上</summary>
    public string Origin { get; }

    /// <summary>這支 exe 跑不跑得動這份引擎</summary>
    public bool CompatibleWithExe => MinExe <= ExeVersion;

    /// <summary>exe 自己的版本（csproj 的 Version）</summary>
    public static Version ExeVersion { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    [GeneratedRegex(@"^\s*//\s*@version\s+(\d+(?:\.\d+){0,3})\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"^\s*//\s*@min-exe\s+(\d+(?:\.\d+){0,3})\s*$", RegexOptions.Multiline)]
    private static partial Regex MinExeLine();

    /// <summary>
    /// 解析引擎原始碼。沒有 @version 的一律視為無效（回 null）：
    /// 例如 Gist 網址打錯回傳了 404 頁面、或下載到舊的使用者腳本，都不能拿去注入。
    /// </summary>
    public static EngineScript? Parse(string source, string origin)
    {
        // 只看檔頭（前 2000 字），避免程式碼裡剛好有長得像的字串
        var head = source.Length > 2000 ? source[..2000] : source;
        var v = VersionLine().Match(head);
        if (!v.Success || !source.Contains("chrome.webview"))
            return null;
        var m = MinExeLine().Match(head);
        return new EngineScript(
            source,
            Normalize(Version.Parse(v.Groups[1].Value)),
            m.Success ? Normalize(Version.Parse(m.Groups[1].Value)) : new Version(0, 0, 0, 0),
            origin);
    }

    public string Describe() => $"引擎 v{Version.ToString(3)}（{Origin}）";

    /// <summary>
    /// 補齊成四段再比較：System.Version 把沒寫的段當 -1，8.0.0 會被判定小於 8.0.0.0。
    /// </summary>
    private static Version Normalize(Version v)
        => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
