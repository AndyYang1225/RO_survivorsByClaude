using System.Net;
using System.Text.Json.Nodes;

using RoHelper.DevApi;

namespace RoHelper.Tests;

public sealed class DevApiServerTests : IDisposable
{
    private readonly FakeDevApiHost _host = new();
    private readonly DevApiServer _server;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>隨機挑 20000～40000 的 port；剛好被占用就換一個再試</summary>
    public DevApiServerTests()
    {
        for (var attempt = 0; ; attempt++)
        {
            var server = new DevApiServer(new DevApiRouter(_host), Random.Shared.Next(20000, 40000));
            try
            {
                server.Start();
                _server = server;
                break;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                server.Dispose();
            }
        }
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}/");
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task Root_200_Json()
    {
        using var res = await _http.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/json", res.Content.Headers.ContentType?.MediaType);
        var body = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        Assert.NotEmpty(body["endpoints"]!.AsArray());
    }

    [Fact]
    public async Task Unknown_404()
    {
        using var res = await _http.GetAsync("/nope");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        // 中文直接輸出，不轉成 \uXXXX
        Assert.Contains("沒有這個端點", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Key_200()
    {
        using var res = await _http.PostAsync("/key?k=1", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(["Key(1)"], _host.Calls);
    }
}
