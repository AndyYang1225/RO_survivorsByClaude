using System.Collections.Specialized;
using System.Text.Json.Nodes;
using System.Web;

using RoHelper.DevApi;

namespace RoHelper.Tests;

public class DevApiRouterTests
{
    private readonly FakeDevApiHost _host = new();
    private readonly DevApiRouter _router;

    public DevApiRouterTests() => _router = new DevApiRouter(_host);

    private Task<DevApiResult> Call(string method, string pathAndQuery)
    {
        var parts = pathAndQuery.Split('?', 2);
        NameValueCollection query = parts.Length > 1 ? HttpUtility.ParseQueryString(parts[1]) : new NameValueCollection();
        return _router.HandleAsync(method, parts[0], query);
    }

    [Fact]
    public async Task Click_PassesCoordinates()
    {
        var r = await Call("POST", "/click?x=10&y=20");

        Assert.Equal(200, r.Status);
        Assert.Equal("Click(10,20)", Assert.Single(_host.Calls));
        Assert.True((bool)r.Body["ok"]!);
    }

    [Theory]
    [InlineData("/click?x=10")]
    [InlineData("/click?x=-1&y=0")]
    [InlineData("/click?x=a&y=0")]
    public async Task Click_BadArgs_400(string url)
    {
        var r = await Call("POST", url);

        Assert.Equal(400, r.Status);
        Assert.NotNull(r.Body["error"]);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task Click_WrongMethod_405()
    {
        var r = await Call("GET", "/click?x=1&y=2");

        Assert.Equal(405, r.Status);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task Key_OneChar()
    {
        Assert.Equal(200, (await Call("POST", "/key?k=1")).Status);
        Assert.Equal("Key(1)", Assert.Single(_host.Calls));
        Assert.Equal(400, (await Call("POST", "/key?k=12")).Status);
        Assert.Equal(400, (await Call("POST", "/key?k=%23")).Status);
        Assert.Equal(400, (await Call("POST", "/key")).Status);
    }

    [Fact]
    public async Task Unknown_404()
    {
        var r = await Call("GET", "/nope");

        Assert.Equal(404, r.Status);
        Assert.Contains("GET /nope", (string)r.Body["error"]!);
    }

    [Fact]
    public async Task Root_ListsEndpoints()
    {
        var r = await Call("GET", "/");

        Assert.Equal(200, r.Status);
        Assert.Contains(r.Body["endpoints"]!.AsArray(), e => ((string)e!).StartsWith("POST /click", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Status_DefaultAndRange()
    {
        Assert.Equal(200, (await Call("GET", "/status")).Status);
        Assert.Equal("Status(30)", Assert.Single(_host.Calls));
        Assert.Equal(400, (await Call("GET", "/status?n=999")).Status);
        Assert.Equal(400, (await Call("GET", "/status?n=0")).Status);
        Assert.Single(_host.Calls);
    }

    [Fact]
    public async Task Evidence_Default20()
    {
        Assert.Equal(200, (await Call("GET", "/evidence")).Status);
        Assert.Equal("Evidence(20)", Assert.Single(_host.Calls));
    }

    [Fact]
    public async Task Shot_Name()
    {
        Assert.Equal(200, (await Call("GET", "/shot")).Status);
        Assert.Equal(200, (await Call("GET", "/shot?name=大廳")).Status);
        Assert.Equal(["Shot()", "Shot(大廳)"], _host.Calls);
        Assert.Equal(400, (await Call("GET", "/shot?name=a/b")).Status);
    }

    [Fact]
    public async Task PauseResume()
    {
        await Call("POST", "/pause");
        await Call("POST", "/resume");

        Assert.Equal(["Paused(True)", "Paused(False)"], _host.Calls);
    }

    [Fact]
    public async Task TemplatesSave_PassesArgs_DefaultThreshold()
    {
        var r = await Call("POST", "/templates/save?name=返回角色&shot=20261120-231129.png&x=912&y=627&w=80&h=28");

        Assert.Equal(200, r.Status);
        Assert.Equal("SaveTemplate(返回角色,20261120-231129.png,912,627,80,28,0.9)", Assert.Single(_host.Calls));
    }

    [Theory]
    [InlineData("/templates/save?name=a&shot=../config.json&x=0&y=0&w=1&h=1")]
    [InlineData("/templates/save?name=a&shot=C:/x.png&x=0&y=0&w=1&h=1")]
    [InlineData("/templates/save?name=a/b&shot=s.png&x=0&y=0&w=1&h=1")]
    [InlineData("/templates/save?name=a&shot=s.png&x=0&y=0&w=0&h=1")]
    [InlineData("/templates/save?name=a&shot=s.png&x=0&y=0&w=1&h=1&threshold=0.2")]
    [InlineData("/templates/save?shot=s.png&x=0&y=0&w=1&h=1")]
    public async Task TemplatesSave_BadArgs_400(string url)
    {
        var r = await Call("POST", url);

        Assert.Equal(400, r.Status);
        Assert.Empty(_host.Calls);
    }

    [Fact]
    public async Task HostThrows_500()
    {
        _host.Throw = true;
        var r = await Call("GET", "/screen");

        Assert.Equal(500, r.Status);
        Assert.Equal("boom", (string)r.Body["error"]!);
    }
}

/// <summary>假的開發介面 host：記下每次呼叫（方法名稱＋參數），回空物件</summary>
internal sealed class FakeDevApiHost : IDevApiHost
{
    public List<string> Calls { get; } = [];
    public bool Throw { get; set; }

    private Task<JsonObject> Record(string call)
    {
        if (Throw)
            throw new InvalidOperationException("boom");
        Calls.Add(call);
        return Task.FromResult(new JsonObject());
    }

    public Task<JsonObject> StatusAsync(int n) => Record($"Status({n})");
    public Task<JsonObject> ShotAsync(string? name) => Record($"Shot({name})");
    public Task<JsonObject> ScreenAsync() => Record("Screen()");
    public Task<JsonObject> PickerAsync() => Record("Picker()");
    public Task ClickAsync(int x, int y) => Record($"Click({x},{y})");
    public Task KeyAsync(char key) => Record($"Key({key})");
    public Task SetPausedAsync(bool paused) => Record($"Paused({paused})");
    public Task ReloadTemplatesAsync() => Record("Reload()");

    public Task<JsonObject> SaveTemplateAsync(string name, string shot, Rectangle crop, double threshold)
        => Record(FormattableString.Invariant($"SaveTemplate({name},{shot},{crop.X},{crop.Y},{crop.Width},{crop.Height},{threshold})"));

    public Task<JsonObject> EvidenceAsync(int n) => Record($"Evidence({n})");
}
