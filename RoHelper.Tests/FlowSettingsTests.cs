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
}
