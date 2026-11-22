using System.Text.Json.Nodes;

using RoHelper.Forms;
using RoHelper.Storage;

namespace RoHelper.Tests;

public class ConfigSyncTests
{
    [Fact]
    public void Shareable_DropsPerAccountAndModeKeys()
    {
        var cfg = new JsonObject
        {
            ["paused"] = true,
            ["runs"] = 12,
            ["stats"] = new JsonArray(),
            ["curJob"] = "巫師",
            ["autoEvent"] = true,
            ["autoExpe"] = false,
            ["expeStage"] = "斐揚森林",
            ["runLimit"] = true,
            ["skills"] = new JsonObject { ["巫師"] = new JsonObject { ["skip"] = new JsonArray("火焰之壁") } },
            ["bellDodge"] = true,
        };
        var s = ConfigSync.Shareable(cfg);
        Assert.Equal(["bellDodge", "skills"], s.Select(kv => kv.Key).Order());
        // 深拷貝：改送出去的不影響自己的
        ((JsonArray)s["skills"]!["巫師"]!["skip"]!).Add("x");
        Assert.Single((JsonArray)cfg["skills"]!["巫師"]!["skip"]!);
    }

    [Fact]
    public void PushThenTake_RoundTripsOnce()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rohelper-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(ConfigSync.TakePending(dir));
            ConfigSync.Push(new JsonObject { ["runs"] = 5, ["seekBall"] = false }, dir);
            var got = ConfigSync.TakePending(dir);
            Assert.NotNull(got);
            Assert.False(got!["seekBall"]!.GetValue<bool>());
            Assert.Null(got["runs"]);
            Assert.Null(ConfigSync.TakePending(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("", false, new string[0])]
    [InlineData("", true, new[] { "--dev-api" })]
    [InlineData("B", true, new[] { "--profile=B", "--dev-api=8766" })]
    [InlineData("c", true, new[] { "--profile=c", "--dev-api=8767" })]
    [InlineData("D", false, new[] { "--profile=D" })]
    public void LauncherArgs(string profile, bool devApi, string[] expected)
        => Assert.Equal(expected, ProfileLauncher.ArgsFor(profile, devApi));

    [Fact]
    public void LauncherPort_LongNamesAbove8800()
    {
        var p = ProfileLauncher.PortFor("acct_2");
        Assert.InRange(p, 8800, 8899);
        Assert.Equal(p, ProfileLauncher.PortFor("ACCT_2"));
    }
}
