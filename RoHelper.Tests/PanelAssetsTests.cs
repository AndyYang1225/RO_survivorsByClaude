using System.Text;

using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelAssetsTests
{
    [Theory]
    [InlineData("https://panel.rohelper/index.html", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/index.html?v=1", "text/html; charset=utf-8")]
    [InlineData("https://panel.rohelper/panel.css", "text/css; charset=utf-8")]
    [InlineData("https://panel.rohelper/panel.js", "text/javascript; charset=utf-8")]
    public void KnownFiles_AreServed(string uri, string type)
    {
        Assert.True(PanelAssets.TryGet(uri, out var data, out var contentType));
        Assert.Equal(type, contentType);
        Assert.NotEmpty(data);
    }

    [Fact]
    public void Index_LinksStyleAndScript()
    {
        PanelAssets.TryGet(PanelAssets.Origin + "index.html", out var data, out _);
        var html = Encoding.UTF8.GetString(data);

        Assert.Contains("panel.css", html);
        Assert.Contains("panel.js", html);
    }

    [Theory]
    [InlineData("https://panel.rohelper/missing.html")]
    [InlineData("https://panel.rohelper/../RoHelper.schema.json")]
    [InlineData("https://panel.rohelper/sub/index.html")]
    [InlineData("https://panel.rohelper/schema.json")]
    [InlineData("https://ro-survivors.com/index.html")]
    public void OtherRequests_AreRejected(string uri)
        => Assert.False(PanelAssets.TryGet(uri, out _, out _));
}
