using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Engine;
using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelSchemaTests
{
    private static readonly EngineSchema Schema = BuiltInSchema.Load();
    private static readonly JsonObject Json = PanelSchema.ToJson(Schema);

    private static IEnumerable<JsonObject> Rules => Json["rules"]!.AsArray().OfType<JsonObject>();

    private static JsonObject Rule(string key) => Rules.First(r => (string?)r["key"] == key);

    [Fact]
    public void NumberRule_KeepsTabRangeAndDefault()
    {
        var r = Rule("preMvp");

        Assert.Equal("升級", (string?)r["tab"]);
        Assert.Equal(15m, r["num"]!.GetValue<decimal>());
        Assert.Equal(5m, r["min"]!.GetValue<decimal>());
        Assert.Equal(60m, r["max"]!.GetValue<decimal>());
        Assert.False(r["noCheck"]!.GetValue<bool>());
    }

    [Fact]
    public void TimeRule_IsNoCheck()
        => Assert.True(Rule("tTick")["noCheck"]!.GetValue<bool>());

    [Fact]
    public void GroupRule_HasGroupAndTab_NoKey()
    {
        var g = Rules.First(r => r["group"] is not null);

        Assert.Null(g["key"]);
        Assert.False(string.IsNullOrEmpty((string?)g["tab"]));
    }

    [Fact]
    public void StageRule_FlagsStageOptions()
    {
        var r = Rule("expeStage");

        Assert.True(r["stageOptions"]!.GetValue<bool>());
        Assert.Equal(2, r["sub"]!.GetValue<int>());
    }

    [Fact]
    public void OptionsRule_KeepsValueTextPairs()
    {
        var opts = Rule("pickOrder")["options"]!.AsArray();

        Assert.Equal("treasure", (string?)opts[0]![0]);
        Assert.Equal("尋寶率優先", (string?)opts[0]![1]);
    }

    [Fact]
    public void TextRule_FlagsText()
        => Assert.True(Rule("banExtra")["text"]!.GetValue<bool>());

    [Fact]
    public void ExclusiveRule_KeepsExcl()
        => Assert.Equal("autoExpe", (string?)Rule("autoEvent")["excl"]);

    [Fact]
    public void Jobs_IncludeBaseJobSkills()
    {
        var job = Schema.JobBase.Keys.First();
        var skills = Json["jobs"]!.AsArray().OfType<JsonObject>()
            .First(j => (string?)j["job"] == job)["skills"]!.AsArray().Select(x => (string?)x).ToList();

        Assert.Equal(Schema.SkillsOf(job), skills);
    }

    [Fact]
    public void ItemsAndStates_AreCopied()
    {
        Assert.Equal(Schema.Items.Count, Json["items"]!.AsArray().Count);
        Assert.Equal("prio", (string?)Json["skillStates"]!.AsArray()[1]![0]);
        Assert.Equal(Schema.DeityAttr.Count, Json["deityAttr"]!.AsArray().Count);
    }
}
