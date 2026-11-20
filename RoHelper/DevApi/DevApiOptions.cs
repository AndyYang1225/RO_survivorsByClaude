using System.Globalization;

namespace RoHelper.DevApi;

/// <summary>
/// 命令列的開發介面選項：<c>--dev-api</c>（port 8765）或 <c>--dev-api=9000</c>。沒帶就是 null＝不開介面、不存證據，程式行為完全不變。
/// </summary>
public sealed record DevApiOptions(int Port)
{
    public const int DefaultPort = 8765;

    private const string Flag = "--dev-api";

    /// <summary>
    /// 解析命令列。port 不是數字或不在 1024～65535 時用預設值：刻意不報錯，不要讓打錯參數害程式開不起來。
    /// </summary>
    public static DevApiOptions? Parse(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals(Flag, StringComparison.OrdinalIgnoreCase))
                return new DevApiOptions(DefaultPort);
            if (!arg.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase))
                continue;
            var ok = int.TryParse(arg.AsSpan(Flag.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port);
            return new DevApiOptions(ok && port is >= 1024 and <= 65535 ? port : DefaultPort);
        }
        return null;
    }
}
