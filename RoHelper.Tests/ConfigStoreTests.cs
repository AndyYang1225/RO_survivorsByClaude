using System.Text.Json.Nodes;

using RoHelper.Storage;

namespace RoHelper.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void GetNumber_ReadsIntSetInCode()
    {
        var store = new ConfigStore(_path);

        store.Replace(new JsonObject { ["runs"] = 3, ["big"] = 5_000_000_000L });

        Assert.Equal(3m, store.GetNumber("runs"));
        Assert.Equal(5_000_000_000m, store.GetNumber("big"));
    }

    [Fact]
    public void GetNumber_HugeDouble_DoesNotThrow()
    {
        var store = new ConfigStore(_path);

        store.Replace(new JsonObject { ["x"] = 1e30 });

        Assert.Equal(decimal.MaxValue, store.GetNumber("x"));
    }

    [Fact]
    public void GetInt_SaturatesHugeValues()
    {
        var store = new ConfigStore(_path);

        store.Replace(new JsonObject { ["big"] = JsonNode.Parse("1e10"), ["small"] = JsonNode.Parse("-1e10"), ["n"] = 3 });

        Assert.Equal(int.MaxValue, store.GetInt("big"));
        Assert.Equal(int.MinValue, store.GetInt("small"));
        Assert.Equal(3, store.GetInt("n"));
        Assert.Equal(7, store.GetInt("missing", 7));
    }
}
