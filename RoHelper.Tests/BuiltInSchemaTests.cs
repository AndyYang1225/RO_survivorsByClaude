using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RoHelper.Engine;

namespace RoHelper.Tests;

public class BuiltInSchemaTests
{
    [Fact]
    public void Load_HasTabsRulesDefaultsAndStages()
    {
        var s = BuiltInSchema.Load();

        Assert.Equal(["升級", "技能", "道具", "神祇", "走位", "出發", "其他"], s.Tabs);
        Assert.Contains(s.Rules, r => r.Key == "autoEvent" && r.Default is JsonValue v && v.GetValue<bool>());
        Assert.Contains(s.Rules, r => r.Key == "tTick" && r.NoCheck && r.Num == 250);
        Assert.Equal(7, s.Stages.Count);
        Assert.Equal("魔法師", s.JobBase["巫師"]);
    }

    /// <summary>engine.js 改了規則卻忘了重新產生 schema.json 時，這個測試會失敗（產生方式見 tools/gen-schema.js）</summary>
    [Fact]
    public void Rules_MatchEngineJs()
    {
        var js = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "engine.js"));
        var section = js[js.IndexOf("const RULES = [", StringComparison.Ordinal)..js.IndexOf("const STAGE_BOSS", StringComparison.Ordinal)];
        var keys = Regex.Matches(section, @"\bk:\s*'(\w+)'").Select(m => m.Groups[1].Value).ToList();
        var labels = Regex.Matches(section, @"label:\s*'([^']*)'").Select(m => m.Groups[1].Value).ToList();

        var rules = BuiltInSchema.Load().Rules.Where(r => r.Key is not null).ToList();

        Assert.Equal(keys, rules.Select(r => r.Key!));
        Assert.Equal(labels, rules.Select(r => r.Label));
    }
}
