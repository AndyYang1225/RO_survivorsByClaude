using System.Text.Json.Nodes;

using RoHelper.Storage;
using RoHelper.Vision;

namespace RoHelper.Tests;

public sealed class FlowSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void From_HugeRunLimit_DoesNotThrow()
    {
        var store = new ConfigStore(_path);
        store.Replace(new JsonObject { ["runLimitN"] = JsonNode.Parse("1e10") });

        var s = FlowSettings.From(store);

        Assert.Equal(int.MaxValue, s.RunLimitN);
    }

    [Fact]
    public void From_ExpeditionDefaultsAndValues()
    {
        var empty = new ConfigStore(_path);
        empty.Replace(new JsonObject());
        var d = FlowSettings.From(empty);
        Assert.True(d.ExpeTalk && d.ExpePick && d.ExpeGo && d.ExpeEnter);
        Assert.Equal("斐揚洞穴", d.ExpeStage);
        Assert.Equal("keep", d.ExpeDiff);

        var store = new ConfigStore(_path);
        store.Replace(new JsonObject
        {
            ["autoExpe"] = true, ["expeTalk"] = false, ["expePick"] = false, ["expeStage"] = "失事輪",
            ["expeGo"] = false, ["expeDiff"] = "max", ["expeEnter"] = false,
        });
        var s = FlowSettings.From(store);
        Assert.True(s.AutoExpe);
        Assert.False(s.ExpeTalk || s.ExpePick || s.ExpeGo || s.ExpeEnter);
        Assert.Equal("失事輪", s.ExpeStage);
        Assert.Equal("max", s.ExpeDiff);
    }
}
