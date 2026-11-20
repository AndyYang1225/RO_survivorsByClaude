using System.Text.Json.Nodes;

using RoHelper.Storage;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public sealed class FieldPlannerTests : IDisposable
{
    private static readonly RectangleF Map = new(1731, 40, 157, 129); // 中心 (1809.5, 104.5)
    private static readonly FieldSettings All = new(true, true, true, true, true, true, 500, 500, true);
    private readonly string _path = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"), "config.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static FieldPlanner Planner() => new(() => 0.5);

    private static MinimapBlob B(double x, double y) => new(x, y, 5);

    private static MinimapInfo Mm(MinimapBlob me, MinimapBlob[]? magnets = null, MinimapBlob[]? chests = null, MinimapBlob[]? loot = null, MinimapBlob[]? bosses = null)
        => new(me, magnets ?? [], chests ?? [], loot ?? [], bosses ?? [], Map);

    [Fact]
    public void Step_AllOff_DoesNothing()
    {
        var d = Planner().Step(Mm(B(1810, 105), chests: [B(1790, 90)]), new FieldSettings(false, false, false, false, true, true, 500, 500, true), 10_000);

        Assert.Equal(new FieldDecision("", null), d);
    }

    [Fact]
    public void Step_Chest_GoesTowardChest()
    {
        var d = Planner().Step(Mm(B(1810, 105), chests: [B(1790, 90)]), All, 10_000);

        Assert.Equal("前往寶箱", d.Status);
        Assert.NotNull(d.Click);
        Assert.True(d.Click.Value.X < 0.5 && d.Click.Value.Y < 0.5, $"{d.Click}");
    }

    [Fact]
    public void Step_MagnetBeatsChest_LootBeatsBoth()
    {
        var me = B(1810, 105);
        Assert.Equal("前往磁鐵", Planner().Step(Mm(me, magnets: [B(1830, 120)], chests: [B(1790, 90)]), All, 10_000).Status);

        var d = Planner().Step(Mm(me, magnets: [B(1830, 120)], chests: [B(1790, 90)], loot: [B(1820, 80)]), All, 10_000);
        Assert.Equal("撿王的掉落物", d.Status);
        Assert.True(d.Click!.Value.X > 0.5 && d.Click.Value.Y < 0.5, $"{d.Click}");
    }

    [Fact]
    public void Step_WithinSeekEvery_KeepsStatusWithoutClick()
    {
        var p = Planner();
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);
        p.Step(mm, All, 10_000);

        Assert.Equal(new FieldDecision("前往寶箱", null), p.Step(mm, All, 10_300));
    }

    [Fact]
    public void Step_NoTarget_FarFromCenter_WalksBackTowardCenter()
    {
        var d = Planner().Step(Mm(B(1750, 60)), All, 10_000);

        Assert.Equal("回地圖中間", d.Status);
        Assert.True(d.Click!.Value.X > 0.5 && d.Click.Value.Y > 0.5, $"{d.Click}");
    }

    [Fact]
    public void Step_NoTarget_NearCenter_Wanders()
    {
        var d = Planner().Step(Mm(B(1810, 105)), All, 10_000);

        Assert.Equal("繞圈", d.Status);
        Assert.NotNull(d.Click);
    }

    [Fact]
    public void Step_NullMinimap_Wanders()
    {
        var d = Planner().Step(null, All, 10_000);

        Assert.Equal("繞圈", d.Status);
        Assert.NotNull(d.Click);
    }

    [Fact]
    public void Step_MeNotMoving_EscapesAfter1500ms_For1300ms()
    {
        var p = Planner();
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);

        Assert.Equal("前往寶箱", p.Step(mm, All, 10_000).Status);
        Assert.Equal("前往寶箱", p.Step(mm, All, 10_600).Status);
        Assert.Equal("前往寶箱", p.Step(mm, All, 11_200).Status);
        var stuck = p.Step(mm, All, 11_700);
        Assert.Equal("卡住了，繞路", stuck.Status);
        Assert.NotNull(stuck.Click);
        Assert.Equal("卡住了，繞路", p.Step(mm, All, 12_200).Status);
        Assert.Equal("卡住了，繞路", p.Step(mm, All, 12_800).Status);
        // 繞路到 13_000 為止；之後位置有變（繞開了）就回去追寶箱
        Assert.Equal("前往寶箱", p.Step(Mm(B(1806, 101), chests: [B(1790, 90)]), All, 13_400).Status);
    }

    [Fact]
    public void Step_StandingOnLootFor3Seconds_IgnoresIt()
    {
        var p = Planner();
        var loot = B(1810, 105);
        var decisions = new List<(long T, FieldDecision D)>();
        for (var i = 0; i < 14; i++)
        {
            // 自己在掉落物上左右晃 1 px：位置有變，不會被當成卡住
            var now = 10_000 + i * 500L;
            decisions.Add((now, p.Step(Mm(B(loot.X + (i % 2 == 0 ? 1 : -1), loot.Y), loot: [loot]), All, now)));
        }

        Assert.Equal("撿王的掉落物", decisions[0].D.Status);
        Assert.DoesNotContain(decisions, x => x.T > 13_500 && x.D.Status == "撿王的掉落物" && x.D.Click is not null);
        Assert.Contains(decisions, x => x.T > 13_500 && x.D.Status == "繞圈" && x.D.Click is not null);
    }

    [Fact]
    public void NewRun_ResetsState()
    {
        var p = Planner();
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);
        p.Step(mm, All, 10_000);
        p.Step(mm, All, 10_600);
        p.Step(mm, All, 11_200);
        Assert.Equal("卡住了，繞路", p.Step(mm, All, 11_700).Status);

        p.NewRun();

        var d = p.Step(mm, All, 11_800);
        Assert.Equal("前往寶箱", d.Status);
        Assert.NotNull(d.Click);
    }

    [Fact]
    public void Step_FarTarget_StepCappedAt030()
    {
        // 40 px 外的磁鐵（在 farLimit 的 78.5 px 內）：k＝0.06＋len·0.03 夾到 0.30，點 (0.2, 0.5)
        var d = Planner().Step(Mm(B(1880, 105), magnets: [B(1840, 105)]), All, 10_000);

        Assert.InRange(d.Click!.Value.X, 0.05f, 0.95f);
        Assert.Equal(0.2f, d.Click.Value.X, 3);
        Assert.Equal(0.5f, d.Click.Value.Y, 3);
    }

    [Fact]
    public void Step_LowHpStop_BelowThreshold_Stops()
    {
        var s = All with { LowHpStop = true, LowHpStopN = 30 };
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);

        Assert.Equal(new FieldDecision("HP 過低，停止走位", null), Planner().Step(mm, s, 10_000, hp: 0.29));
        Assert.Equal("前往寶箱", Planner().Step(mm, s, 10_000, hp: 0.31).Status);
        // 讀不到 HP（null）不停；lowHpStop 關掉時 HP 再低也照走
        Assert.Equal("前往寶箱", Planner().Step(mm, s, 10_000, hp: null).Status);
        Assert.Equal("前往寶箱", Planner().Step(mm, All, 10_000, hp: 0.05).Status);
    }

    [Fact]
    public void Step_LowHpStop_CheckedBeforeSeekEvery()
    {
        var p = Planner();
        var s = All with { LowHpStop = true };
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);
        p.Step(mm, s, 10_000, hp: 1);

        Assert.Equal("HP 過低，停止走位", p.Step(mm, s, 10_100, hp: 0.1).Status);
    }

    [Fact]
    public void Step_FarLimit_SkipsFarChestAndMagnet_ButNotLoot()
    {
        // 地圖 157×129 → farMax＝78.5 px；(1730,45) 離自己 (1810,105) 100 px、(1790,90) 只有 25 px
        var me = B(1810, 105);
        var far = B(1730, 45);

        Assert.Equal("繞圈", Planner().Step(Mm(me, chests: [far]), All, 10_000).Status);
        Assert.Equal("繞圈", Planner().Step(Mm(me, magnets: [far]), All, 10_000).Status);
        Assert.Equal("前往寶箱", Planner().Step(Mm(me, chests: [far, B(1790, 90)]), All, 10_000).Status);
        Assert.Equal("撿王的掉落物", Planner().Step(Mm(me, loot: [far]), All, 10_000).Status);
        Assert.Equal("前往寶箱", Planner().Step(Mm(me, chests: [far]), All with { FarLimit = false }, 10_000).Status);
    }

    [Fact]
    public void Step_FarLimit_PicksNearChestEvenIfFarMagnet()
    {
        var d = Planner().Step(Mm(B(1810, 105), magnets: [B(1730, 45)], chests: [B(1790, 90)]), All, 10_000);

        Assert.Equal("前往寶箱", d.Status);
    }

    // 王死掉的地點：每次呼叫時自己的位置都換一點（> 0.8 px），才不會被當成卡住

    [Fact]
    public void Step_BossGoneFor2Seconds_RunsToWhereItDied()
    {
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), All, 10_000);
        p.Step(Mm(B(1812, 105), bosses: [B(1791, 90)]), All, 10_600);

        var d = p.Step(Mm(B(1814, 105)), All, 12_200);

        Assert.Equal("跑到王死掉的地方", d.Status);
        Assert.True(d.Click!.Value.X < 0.5 && d.Click.Value.Y < 0.5, $"{d.Click}");
        Assert.Equal(new BossSpot(1791, 90, 12_200), p.BossSpot);
    }

    [Fact]
    public void Step_BossBlinksUnder1500ms_NotDead()
    {
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), All, 10_000);
        p.Step(Mm(B(1812, 105)), All, 11_000);
        p.Step(Mm(B(1814, 105), bosses: [B(1790, 90)]), All, 11_400);

        Assert.NotEqual("跑到王死掉的地方", p.Step(Mm(B(1816, 105)), All, 12_000).Status);
        Assert.Null(p.BossSpot);
    }

    [Fact]
    public void Step_AtBossSpot_Waits1500ms_ThenClears()
    {
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), All, 10_000);

        Assert.Equal(new FieldDecision("在王死掉的地方撿東西", null), p.Step(Mm(B(1791, 90)), All, 12_000));
        Assert.Equal(new FieldDecision("在王死掉的地方撿東西", null), p.Step(Mm(B(1790.5, 90)), All, 12_800));
        Assert.Equal("在王死掉的地方撿東西", p.Step(Mm(B(1791, 90.5)), All, 13_600).Status);
        Assert.Null(p.BossSpot);
        Assert.NotEqual("在王死掉的地方撿東西", p.Step(Mm(B(1795, 95)), All, 14_200).Status);
    }

    [Fact]
    public void Step_BossLeftAtMinimapEdge_NotDead()
    {
        // 地圖本體左邊在 1731：1733 離邊緣只有 2 px（要 > 4 px 才算在小地圖裡面）→ 是跑出去了，不是死了
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1733, 90)]), All, 10_000);

        Assert.NotEqual("跑到王死掉的地方", p.Step(Mm(B(1812, 105)), All, 12_200).Status);
        Assert.Null(p.BossSpot);
    }

    [Fact]
    public void Step_BossSpot_ExpiresAfter12Seconds()
    {
        var p = Planner();
        p.Step(Mm(B(1850, 140), bosses: [B(1740, 60)]), All, 10_000);
        var decisions = new List<string>();
        for (var i = 0; i < 20; i++)
            decisions.Add(p.Step(Mm(B(1849 - i, 140 - i)), All, 11_600 + i * 600L).Status);

        // 11_600 判定死掉；23_000 時還在 12 秒內，23_700 就過期了
        Assert.All(decisions, d => Assert.Equal("跑到王死掉的地方", d));
        Assert.NotEqual("跑到王死掉的地方", p.Step(Mm(B(1825, 115)), All, 23_700).Status);
        Assert.Null(p.BossSpot);
    }

    [Fact]
    public void Step_BossSpot_LootOnMinimap_PicksLootInstead()
    {
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), All, 10_000);

        Assert.Equal("撿王的掉落物", p.Step(Mm(B(1812, 105), loot: [B(1830, 120)]), All, 12_200).Status);
        Assert.NotNull(p.BossSpot);
    }

    [Fact]
    public void Step_BossSpotOff_DoesNotRunThere()
    {
        var p = Planner();
        var off = All with { BossSpot = false };
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), off, 10_000);

        Assert.NotEqual("跑到王死掉的地方", p.Step(Mm(B(1812, 105)), off, 12_200).Status);
    }

    [Fact]
    public void NewRun_ClearsBossSpot()
    {
        var p = Planner();
        p.Step(Mm(B(1810, 105), bosses: [B(1790, 90)]), All, 10_000);
        p.Step(Mm(B(1812, 105)), All, 12_200);
        Assert.NotNull(p.BossSpot);

        p.NewRun();

        Assert.Null(p.BossSpot);
    }

    [Fact]
    public void FieldSettings_From_DefaultsAndValues()
    {
        var empty = new ConfigStore(_path);
        empty.Replace(new JsonObject());
        Assert.Equal(new FieldSettings(false, false, false, false, false, false, 1500, 1500, ChestMoney: true), FieldSettings.From(empty));

        var store = new ConfigStore(_path);
        store.Replace(new JsonObject
        {
            ["seekLoot"] = true, ["seekMagnet"] = true, ["seekChest"] = false, ["autoWander"] = true,
            ["stayCenter"] = true, ["zoomOut"] = true, ["tSeekN"] = 800, ["tWanderN"] = 1400, ["chestMoney"] = false,
            ["lowHpStop"] = true, ["lowHpStopN"] = 45, ["farLimit"] = false, ["bossSpot"] = false,
        });
        Assert.Equal(new FieldSettings(true, true, false, true, true, true, 800, 1400, false, LowHpStop: true, LowHpStopN: 45, FarLimit: false, BossSpot: false), FieldSettings.From(store));
    }

    [Fact]
    public void Step_ChestNotAffordable_SkipsChest_ButKeepsMagnet()
    {
        var me = B(1810, 105);
        Assert.NotEqual("前往寶箱", Planner().Step(Mm(me, chests: [B(1790, 90)]), All, 10_000, chestAffordable: false).Status);
        Assert.Equal("前往磁鐵", Planner().Step(Mm(me, magnets: [B(1830, 120)], chests: [B(1790, 90)]), All, 10_000, chestAffordable: false).Status);
    }

    [Theory]
    [InlineData(true, 1537, 3009, false)]
    [InlineData(true, 36, 30, true)]
    [InlineData(true, 30, 30, true)]
    [InlineData(false, 0, 30, true)]
    public void CanAffordChest_ComparesMoneyAndCost(bool chestMoney, int money, int cost, bool expected)
    {
        Assert.Equal(expected, (All with { ChestMoney = chestMoney }).CanAffordChest(new Hud(money, cost)));
    }

    [Fact]
    public void CanAffordChest_UnreadableHud_CountsAsAffordable()
    {
        Assert.True(All.CanAffordChest(null));
    }
}
