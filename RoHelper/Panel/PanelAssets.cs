namespace RoHelper.Panel;

/// <summary>
/// 新版面板的介面檔案（index.html、panel.css、panel.js），打包在 exe 裡（csproj 的 EmbeddedResource，LogicalName 是 RoHelper.Panel.檔名）。
/// 面板的 WebView2 開 <see cref="Origin"/>，所有請求由 WebResourceRequested 交給 <see cref="TryGet"/>，不碰檔案系統、不連網路。
/// 只接受單層檔名和三種副檔名：網頁只能拿到面板自己的檔案，拿不到 exe 裡其他資源（schema.json、範本圖片）。
/// </summary>
public static class PanelAssets
{
    public const string Origin = "https://panel.rohelper/";

    private const string ResourcePrefix = "RoHelper.Panel.";

    public static bool TryGet(string uri, out byte[] data, out string contentType)
    {
        data = [];
        contentType = "";
        if (!uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
            return false;
        var name = uri[Origin.Length..];
        var cut = name.IndexOfAny(['?', '#']);
        if (cut >= 0)
            name = name[..cut];
        if (name.Length == 0)
            name = "index.html";
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            return false;

        var type = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            _ => "",
        };
        if (type.Length == 0)
            return false;

        using var stream = typeof(PanelAssets).Assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
            return false;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        data = buffer.ToArray();
        contentType = type;
        return true;
    }
}
