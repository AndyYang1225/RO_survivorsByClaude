using System.Text.Json.Nodes;

using RoHelper.Panel;

namespace RoHelper.Tests;

public class PanelActionsTests
{
    private static JsonObject Msg(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static List<string> List(JsonNode? node) => node!.AsArray().Select(x => (string)x!).ToList();

    [Fact]
    public void Patch_IsCopied()
    {
        var msg = Msg("""{"type":"patch","patch":{"tTickN":100,"pickSkill":true}}""");

        var p = PanelActions.ToPatch(msg, [])!;

        Assert.Equal(100, p["tTickN"]!.GetValue<int>());
        Assert.True(p["pickSkill"]!.GetValue<bool>());
        p["tTickN"] = 1;
        Assert.Equal(100, msg["patch"]!["tTickN"]!.GetValue<int>());
    }

    [Fact]
    public void SetSkill_ToFocus_KeepsRank_AndOtherJobs()
    {
        var cfg = Msg("""{"skills":{"賢者":{"order":["火箭術","冰箭術"]},"騎士":{"order":["怒爆"]}}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"冰箭術","state":"focus"}"""), cfg)!;

        Assert.Equal(["火箭術", "冰箭術"], List(p["skills"]!["賢者"]!["order"]));
        Assert.Equal(["冰箭術"], List(p["skills"]!["賢者"]!["focus"]));
        Assert.Equal(["怒爆"], List(p["skills"]!["騎士"]!["order"]));
    }

    [Fact]
    public void SetSkill_ToBan_RemovesFromOrder()
    {
        var cfg = Msg("""{"skills":{"賢者":{"order":["火箭術"]}}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"火箭術","state":"ban"}"""), cfg)!;

        Assert.Empty(List(p["skills"]!["賢者"]!["order"]));
        Assert.Equal(["火箭術"], List(p["skills"]!["賢者"]!["ban"]));
    }

    [Fact]
    public void SetSkill_UnknownState_IsNull()
        => Assert.Null(PanelActions.ToPatch(Msg("""{"type":"setSkill","job":"賢者","skill":"火箭術","state":"hack"}"""), []));

    [Fact]
    public void Deity_OnAppends_OffRemoves()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神"],"騎士":["貪婪之神"]}}""");

        var on = PanelActions.ToPatch(Msg("""{"type":"deity","job":"賢者","name":"處刑之神","on":true}"""), cfg)!;
        var off = PanelActions.ToPatch(Msg("""{"type":"deity","job":"賢者","name":"戰爭之神","on":false}"""), cfg)!;

        Assert.Equal(["戰爭之神", "處刑之神"], List(on["deity"]!["賢者"]));
        Assert.Equal(["貪婪之神"], List(on["deity"]!["騎士"]));
        Assert.Empty(List(off["deity"]!["賢者"]));
    }

    [Fact]
    public void DeityBan_On_AlsoRemovesFromPriority()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神","處刑之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityBan","job":"賢者","name":"戰爭之神","on":true}"""), cfg)!;

        Assert.Equal(["戰爭之神"], List(p["deityBan"]!["賢者"]));
        Assert.Equal(["處刑之神"], List(p["deity"]!["賢者"]));
    }

    [Fact]
    public void DeityBan_Off_OnlyTouchesBanList()
    {
        var cfg = Msg("""{"deityBan":{"賢者":["戰爭之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityBan","job":"賢者","name":"戰爭之神","on":false}"""), cfg)!;

        Assert.Empty(List(p["deityBan"]!["賢者"]));
        Assert.Null(p["deity"]);
    }

    [Fact]
    public void DeityClear_EmptiesThatJob()
    {
        var cfg = Msg("""{"deity":{"賢者":["戰爭之神"],"騎士":["貪婪之神"]}}""");

        var p = PanelActions.ToPatch(Msg("""{"type":"deityClear","job":"賢者"}"""), cfg)!;

        Assert.Empty(List(p["deity"]!["賢者"]));
        Assert.Equal(["貪婪之神"], List(p["deity"]!["騎士"]));
    }

    [Fact]
    public void Item_ToggleOnOff()
    {
        var cfg = Msg("""{"itemPrio":["紅藥水"]}""");

        var on = PanelActions.ToPatch(Msg("""{"type":"item","list":"itemPrio","name":"白藥水","on":true}"""), cfg)!;
        var off = PanelActions.ToPatch(Msg("""{"type":"item","list":"itemPrio","name":"紅藥水","on":false}"""), cfg)!;

        Assert.Equal(["紅藥水", "白藥水"], List(on["itemPrio"]));
        Assert.Empty(List(off["itemPrio"]));
    }

    [Theory]
    [InlineData("""{"type":"item","list":"skills","name":"x","on":true}""")]
    [InlineData("""{"type":"deity","job":"","name":"x","on":true}""")]
    [InlineData("""{"type":"setSkill","skill":"x","state":"prio"}""")]
    [InlineData("""{"type":"export"}""")]
    [InlineData("""{"type":"patch","patch":5}""")]
    public void Invalid_IsNull(string json)
        => Assert.Null(PanelActions.ToPatch(Msg(json), []));
}
