using System.Text.Json.Nodes;

using RoHelper.Vision;
using RoHelper.Vision.Field;
using RoHelper.Vision.Picker;

namespace RoHelper.Tests;

public class RunRecorderTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);

    private static CardInfo Card(int i, string name, int rarity = 2, bool isNew = false, string text = "")
        => new(i, new Rectangle(i * 220, 40, 214, 352), name, name + " " + text, rarity, false, isNew, []);

    private static readonly RunResult Clear = new("你存活下來了！", "clear", "魔法師", "14:20", 860, 57, 9254, 7,
        "基本驗 : + 339 Base Lv 32", ["火術 163.2M 191k DPS 34 %"], "486M , 565k DPS");

    [Fact]
    public void Finish_WithoutStart_ReturnsNull()
    {
        Assert.Null(new RunRecorder().Finish(Clear, T0));
    }

    [Fact]
    public void FullRun_HasJsFieldsAndCounts()
    {
        var rec = new RunRecorder();
        rec.Start(T0, "魔法師", paused: false);
        rec.SetInfo("洛陽", "");
        var info = new PickerInfo(true, [Card(0, "火箭術", isNew: true), Card(1, "思想之神 +1", text: "神祇")], null);
        rec.Pick(new PickDecision(PickDo.Pick, 0, "技能順序"), info);
        rec.Pick(new PickDecision(PickDo.Banish, 1, "放逐"), info, "02:17");
        rec.Pick(new PickDecision(PickDo.Reroll, null, "沒有想要的"), info);
        rec.Sample(1.0, "走位：繞圈", paused: false);
        rec.Sample(0.42, "走位：前往寶箱", paused: false);
        rec.Sample(0.9, "走位：繞圈", paused: false);

        var run = rec.Finish(Clear, T0.AddSeconds(900))!;

        Assert.False(rec.Active);
        Assert.Equal(1, (int)run["v"]!);
        Assert.Equal("2026-11-21T15:00:00.000Z", (string?)run["start"]);
        Assert.Equal("2026-11-21T15:15:00.000Z", (string?)run["end"]);
        Assert.Equal(900, (int)run["wallSecs"]!);
        Assert.Equal("洛陽", (string?)run["stage"]);
        Assert.Equal("魔法師", (string?)run["job"]);
        Assert.Equal("event", (string?)run["mode"]);
        Assert.True((bool)run["autoAtStart"]!);
        Assert.False((bool)run["pausedAny"]!);
        Assert.Equal(42, (int)run["hpMin"]!);
        Assert.Equal(90, (int)run["last"]!["hp"]!);
        Assert.Equal("走位：繞圈", (string?)run["last"]!["action"]);
        Assert.Equal("clear", (string?)run["outcome"]);
        Assert.Equal("你存活下來了！", (string?)run["title"]);
        Assert.Equal(860, (int)run["result"]!["secs"]!);
        Assert.Equal(7, (int)run["result"]!["bosses"]!);
        Assert.Equal("486M , 565k DPS", (string?)run["dpsTotal"]);
        Assert.Equal("火術 163.2M 191k DPS 34 %", (string?)run["dps"]![0]!["text"]);

        var picks = run["picks"]!.AsArray();
        Assert.Equal(3, picks.Count);
        Assert.Equal("", (string?)picks[0]!["gt"]);
        Assert.Equal("pick", (string?)picks[0]!["kind"]);
        Assert.True((bool)picks[0]!["chest"]!);
        Assert.Equal("火箭術", (string?)picks[0]!["name"]);
        Assert.Equal("稀有", (string?)picks[0]!["rarity"]);
        Assert.True((bool)picks[0]!["isNew"]!);
        Assert.False((bool)picks[0]!["deity"]!);
        Assert.True((bool)picks[1]!["deity"]!);
        Assert.Equal("02:17", (string?)picks[1]!["gt"]);
        Assert.Null(picks[2]!["name"]);
        Assert.Equal("{\"pick\":1,\"banish\":1,\"reroll\":1,\"focus\":0}", run["counts"]!.ToJsonString());
    }

    [Fact]
    public void Sample_Paused_MarksPausedAny()
    {
        var rec = new RunRecorder();
        rec.Start(T0, "", paused: false);
        rec.Sample(null, "", paused: true);

        var run = rec.Finish(null, T0)!;

        Assert.True((bool)run["pausedAny"]!);
        Assert.Equal(100, (int)run["hpMin"]!);
        Assert.Equal("unknown", (string?)run["outcome"]);
        Assert.Null(run["result"]!["secs"]);
    }

    [Fact]
    public void Start_Expedition_ModeExpe()
    {
        var rec = new RunRecorder();
        rec.Start(T0, "", paused: false, "expe");

        Assert.Equal("expe", (string?)rec.Finish(null, T0)!["mode"]);
    }

    [Fact]
    public void Start_Again_DropsUnfinishedRun()
    {
        var rec = new RunRecorder();
        rec.Start(T0, "", paused: false);
        rec.Pick(new PickDecision(PickDo.Reroll, null, ""), new PickerInfo(false, [], null));
        rec.Start(T0.AddMinutes(1), "", paused: false);

        Assert.Empty(rec.Finish(null, T0.AddMinutes(2))!["picks"]!.AsArray());
    }

    [Fact]
    public void Stat_PrefersGameTime_AndKeepsLast50()
    {
        var s = RunRecorder.Stat(T0, "", 860, 900);
        Assert.Equal("?", (string?)s["stage"]);
        Assert.Equal(860, (int)s["secs"]!);
        Assert.Equal(900, (int)RunRecorder.Stat(T0, "洛陽", null, 900)["secs"]!);

        JsonNode? stats = null;
        for (var i = 0; i < 55; i++)
            stats = RunRecorder.AppendStat(stats, RunRecorder.Stat(T0, "洛陽", i, 0));
        var arr = stats!.AsArray();
        Assert.Equal(50, arr.Count);
        Assert.Equal(5, (int)arr[0]!["secs"]!);
        Assert.Equal(54, (int)arr[49]!["secs"]!);
    }
}
