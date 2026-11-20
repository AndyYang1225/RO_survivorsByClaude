using System.Text.Json.Nodes;

using RoHelper.Storage;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public sealed class PickSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private ConfigStore Store(JsonObject cfg)
    {
        var store = new ConfigStore(_path);
        store.Replace(cfg);
        return store;
    }

    [Fact]
    public void From_ReadsJobSkillsAndDeity_FromCurrentJob()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["jobSel"] = "auto",
            ["curJob"] = "巫師",
            ["skillOn"] = true,
            ["skills"] = new JsonObject
            {
                ["巫師"] = new JsonObject
                {
                    ["order"] = new JsonArray("雷爆術", "冰箭術"),
                    ["after"] = new JsonArray("雷爆術"),
                    ["ban"] = new JsonArray("火箭術"),
                    ["focus"] = new JsonArray("冰箭術"),
                    ["skip"] = new JsonArray("聖靈召喚"),
                },
            },
            ["deity"] = new JsonObject { ["巫師"] = new JsonArray("思想之神") },
            ["deityBan"] = new JsonObject { ["巫師"] = new JsonArray("貪婪之神") },
        }));

        Assert.Equal("巫師", s.Job);
        Assert.Equal(["雷爆術", "冰箭術"], s.Skills.Order);
        Assert.Equal(["雷爆術"], s.Skills.After);
        Assert.Equal(["火箭術"], s.Skills.Ban);
        Assert.Equal(["冰箭術"], s.Skills.Focus);
        Assert.Equal(["聖靈召喚"], s.Skills.Skip);
        Assert.Equal(["思想之神"], s.DeityList);
        Assert.Equal(["貪婪之神"], s.DeityBan);
    }

    [Fact]
    public void From_SkillOff_UsesEmptySkills()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["jobSel"] = "魔法師",
            ["skillOn"] = false,
            ["skills"] = new JsonObject { ["魔法師"] = new JsonObject { ["order"] = new JsonArray("火箭術") } },
        }));

        Assert.Equal("魔法師", s.Job);
        Assert.Empty(s.Skills.Order);
    }

    [Fact]
    public void From_SplitsBanExtraWords_LikeJs()
    {
        var s = PickSettings.From(Store(new JsonObject { ["banExtra"] = true, ["banExtraT"] = "屬性附加，普攻強化、 毒" }));

        Assert.Equal(["屬性附加", "普攻強化", "毒"], s.BanExtraWords);
    }

    [Fact]
    public void From_ReadsFlagsAndNumbers()
    {
        var s = PickSettings.From(Store(new JsonObject
        {
            ["pickSkill"] = true, ["pickChest"] = false, ["autoFocus"] = true, ["autoFocusN"] = 2,
            ["banishKeep"] = true, ["banishKeepN"] = 1, ["pickOrder"] = "rarity", ["deityOrder"] = "first",
            ["tPickN"] = 350, ["debugLog"] = true,
        }));

        Assert.True(s.PickSkill);
        Assert.False(s.PickChest);
        Assert.Equal(2, s.AutoFocusN);
        Assert.Equal(1, s.BanishKeepN);
        Assert.Equal("rarity", s.PickOrder);
        Assert.Equal("first", s.DeityOrder);
        Assert.Equal(350, s.PickDelay);
        Assert.True(s.DebugLog);
    }
}
