using System.Text.Json.Nodes;

using RoHelper.Engine;

namespace RoHelper.Tests;

public class ConfigDefaultsTests
{
    private static readonly RoHelper.Bridge.EngineSchema Schema = BuiltInSchema.Load();

    [Fact]
    public void Build_HasRuleDefaults()
    {
        var cfg = ConfigDefaults.Build(Schema);

        Assert.True(cfg["autoEvent"]!.GetValue<bool>());
        Assert.False(cfg["autoExpe"]!.GetValue<bool>());
        Assert.Equal(250m, cfg["tTickN"]!.GetValue<decimal>());
        Assert.Equal("屬性附加", cfg["banExtraT"]!.GetValue<string>());
        Assert.Equal(7, cfg["expeList"]!.AsArray().Count);
        Assert.Equal("雷爆術", cfg["skills"]!["巫師"]!["order"]![0]!.GetValue<string>());
        Assert.Equal(0, cfg["runs"]!.GetValue<int>());
    }

    [Fact]
    public void Apply_StoredOverridesDefaults_AndKeepsMissingDefaults()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["autoReturn"] = false }, Schema, reset: false);

        Assert.False(cfg["autoReturn"]!.GetValue<bool>());
        Assert.True(cfg["soloLobby"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_EventAndExpeditionBothOn_TurnsEventOff()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["autoEvent"] = true, ["autoExpe"] = true }, Schema, reset: false);

        Assert.False(cfg["autoEvent"]!.GetValue<bool>());
        Assert.True(cfg["autoExpe"]!.GetValue<bool>());
    }

    [Fact]
    public void Apply_ClampsTimes_AndReplacesNonNumbers()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["tTickN"] = 5, ["tPickN"] = "abc" }, Schema, reset: false);

        Assert.Equal(100m, cfg["tTickN"]!.GetValue<decimal>());
        Assert.Equal(350m, cfg["tPickN"]!.GetValue<decimal>());
    }

    [Fact]
    public void Apply_InvalidTab_ResetsToFirstTab()
    {
        var cfg = ConfigDefaults.Apply(ConfigDefaults.Build(Schema), new JsonObject { ["tab"] = "不存在" }, Schema, reset: false);

        Assert.Equal("升級", cfg["tab"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_Reset_DropsOldKeys_AndDoesNotTouchInput()
    {
        var current = ConfigDefaults.Build(Schema);
        current["junk"] = 1;

        var cfg = ConfigDefaults.Apply(current, new JsonObject(), Schema, reset: true);

        Assert.False(cfg.ContainsKey("junk"));
        Assert.True(current.ContainsKey("junk"));
    }
}
