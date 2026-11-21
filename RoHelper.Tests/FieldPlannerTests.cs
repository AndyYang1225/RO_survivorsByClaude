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

    // 這些測試一開局就放掉落物；地圖圖案的學習另外測（MapFeature_*）
    private static FieldPlanner Planner() => new(() => 0.5, learnMapFeatures: false);

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
            ["lowHpStop"] = true, ["lowHpStopN"] = 45, ["farLimit"] = false, ["bossSpot"] = false, ["seekBall"] = false,
            ["autoUpgrade"] = false, ["preMvp"] = false, ["preMvpN"] = 30,
        });
        Assert.Equal(new FieldSettings(true, true, false, true, true, true, 800, 1400, false, LowHpStop: true, LowHpStopN: 45, FarLimit: false, BossSpot: false, SeekBall: false,
            AutoUpgrade: false, PreMvp: false, PreMvpN: 30), FieldSettings.From(store));
    }

    [Fact]
    public void Step_LootPhase_LootStatusPrefixed()
    {
        var d = Planner().Step(Mm(B(1810, 105), chests: [B(1790, 90)], loot: [B(1820, 80)]), All, 10_000, lootPhase: true);

        Assert.Equal("倒數中！撿王的掉落物", d.Status);
        Assert.True(d.Click!.Value.X > 0.5 && d.Click.Value.Y < 0.5, $"{d.Click}");
    }

    [Fact]
    public void Step_LootPhase_IgnoresChestAndMagnet_WandersSmall()
    {
        var d = Planner().Step(Mm(B(1810, 105), magnets: [B(1830, 120)], chests: [B(1790, 90)]), All, 10_000, lootPhase: true);

        Assert.Equal("倒數中：找掉落物", d.Status);
        Assert.NotNull(d.Click);
        // 小範圍繞圈：橫 0.25、直 0.2 以內
        Assert.InRange(d.Click.Value.X, 0.25f - 0.001f, 0.75f + 0.001f);
        Assert.InRange(d.Click.Value.Y, 0.3f - 0.001f, 0.7f + 0.001f);
    }

    [Fact]
    public void Step_LootPhase_NoLoot_ClicksOnlyEveryWanderEvery()
    {
        var p = Planner();
        var mm = Mm(B(1810, 105));
        Assert.NotNull(p.Step(mm, All, 10_000, lootPhase: true).Click);

        Assert.Equal(new FieldDecision("倒數中：找掉落物", null), p.Step(mm, All, 10_200, lootPhase: true));
        Assert.Equal(new FieldDecision("倒數中：找掉落物", null), p.Step(mm, All, 10_500, lootPhase: true));
        Assert.NotNull(p.Step(mm, All, 10_600, lootPhase: true).Click);
    }

    [Fact]
    public void Step_LootPhase_AutoWanderOff_NoClick()
    {
        var d = Planner().Step(Mm(B(1810, 105)), All with { AutoWander = false }, 10_000, lootPhase: true);

        Assert.Equal(new FieldDecision("倒數中：找掉落物", null), d);
    }

    [Fact]
    public void Step_LootPhase_NullMinimap_WandersSmall()
    {
        Assert.Equal("倒數中：找掉落物", Planner().Step(null, All, 10_000, lootPhase: true).Status);
    }

    [Fact]
    public void Step_LootPhase_LootBlinksOut_KeepsGoingToLastSeenFor2500ms()
    {
        var p = Planner();
        var me = B(1810, 105);
        p.Step(Mm(me, loot: [B(1820, 80)]), All, 10_000, lootPhase: true);

        var d = p.Step(Mm(me), All, 11_000, lootPhase: true);
        Assert.Equal("倒數中！撿王的掉落物", d.Status);
        Assert.True(d.Click!.Value.X > 0.5 && d.Click.Value.Y < 0.5, $"{d.Click}");

        Assert.Equal("倒數中：找掉落物", p.Step(Mm(me), All, 12_600, lootPhase: true).Status);
    }

    [Fact]
    public void Step_LootBlinksOut_NotLootPhase_DoesNotRemember()
    {
        var p = Planner();
        var me = B(1810, 105);
        p.Step(Mm(me, loot: [B(1820, 80)]), All, 10_000);

        Assert.NotEqual("撿王的掉落物", p.Step(Mm(me), All, 11_000).Status);
    }

    [Fact]
    public void Step_LootPhase_StandingOnLoot_DoesNotGiveUp()
    {
        var p = Planner();
        var loot = B(1810, 105);
        for (var i = 0; i < 12; i++)
        {
            var now = 10_000 + i * 600L;
            // 自己左右晃 1 px（不觸發卡住偵測），一直站在掉落物上
            var d = p.Step(Mm(B(loot.X + (i % 2 == 0 ? 1 : -1), loot.Y), loot: [loot]), All, now, lootPhase: true);
            Assert.Equal("倒數中！撿王的掉落物", d.Status);
        }
    }

    private static BallBlob Ball(string kind, float fx, float fy) => new(kind, (int)(fx * 1904), (int)(fy * 682), 34, 18, 12, (221, 142, 119), 0, 0.63, new PointF(fx, fy));

    [Fact]
    public void Step_Ball_ClicksBall_AfterLoot_BeforeMagnetAndChest()
    {
        var me = B(1810, 105);
        var balls = new[] { Ball("粉紅", 0.53f, 0.55f) };

        var d = Planner().Step(Mm(me, magnets: [B(1830, 120)], chests: [B(1790, 90)]), All, 10_000, balls: balls);
        Assert.Equal("前往扭蛋（粉紅）", d.Status);
        Assert.Equal(new PointF(0.53f, 0.55f), d.Click);

        Assert.Equal("撿王的掉落物", Planner().Step(Mm(me, loot: [B(1820, 80)]), All, 10_000, balls: balls).Status);
    }

    [Fact]
    public void Step_Ball_FirstInListWins()
    {
        var d = Planner().Step(Mm(B(1810, 105)), All, 10_000, balls: [Ball("綠", 0.6f, 0.5f), Ball("粉紅", 0.9f, 0.9f)]);

        Assert.Equal("前往扭蛋（綠）", d.Status);
    }

    [Fact]
    public void Step_Ball_IgnoredInLootPhaseOrWhenSeekBallOff()
    {
        var mm = Mm(B(1810, 105), chests: [B(1790, 90)]);
        var balls = new[] { Ball("粉紅", 0.53f, 0.55f) };

        Assert.Equal("倒數中：找掉落物", Planner().Step(mm, All, 10_000, lootPhase: true, balls: balls).Status);
        Assert.Equal("前往寶箱", Planner().Step(mm, All with { SeekBall = false }, 10_000, balls: balls).Status);
    }

    [Fact]
    public void Step_Ball_NullMinimap_StillGoesToBall()
    {
        Assert.Equal("前往扭蛋（橘）", Planner().Step(null, All, 10_000, balls: [Ball("橘", 0.3f, 0.4f)]).Status);
    }

    [Fact]
    public void Step_Ball_OnlySeekBallOn_StillActs()
    {
        var s = new FieldSettings(false, false, false, false, true, true, 500, 500, true, SeekBall: true);

        Assert.Equal("前往扭蛋（粉紅）", Planner().Step(Mm(B(1810, 105)), s, 10_000, balls: [Ball("粉紅", 0.53f, 0.55f)]).Status);
    }

    [Fact]
    public void Step_Ball_SeenOver6Seconds_Muted15Seconds()
    {
        var p = Planner();
        var balls = new[] { Ball("白", 0.53f, 0.55f) };
        // 自己一直在動（不觸發卡住偵測）
        MinimapInfo At(int i) => Mm(B(1810 + (i % 2 == 0 ? 1 : -1), 105));
        var i = 0;
        for (var now = 10_000L; now <= 16_000; now += 600, i++)
            Assert.Equal("前往扭蛋（白）", p.Step(At(i), All, now, balls: balls).Status);

        Assert.NotEqual("前往扭蛋（白）", p.Step(At(i++), All, 16_600, balls: balls).Status);
        Assert.NotEqual("前往扭蛋（白）", p.Step(At(i++), All, 31_000, balls: balls).Status);
        Assert.Equal("前往扭蛋（白）", p.Step(At(i), All, 31_700, balls: balls).Status);
    }

    [Fact]
    public void Step_Ball_GoneResetsTimer()
    {
        var p = Planner();
        var balls = new[] { Ball("白", 0.53f, 0.55f) };
        MinimapInfo At(int i) => Mm(B(1810 + (i % 2 == 0 ? 1 : -1), 105));
        p.Step(At(0), All, 10_000, balls: balls);
        p.Step(At(1), All, 14_000, balls: balls);
        p.Step(At(2), All, 15_000, balls: []);

        Assert.Equal("前往扭蛋（白）", p.Step(At(3), All, 17_000, balls: balls).Status);
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
    /// <summary>實測（2026-11-21）斐揚洞穴：小地圖固定位置有一個像掉落物的深藍點，開局就在。王還沒死之前看到的「掉落物」是地圖圖案，整局忽略</summary>
    [Fact]
    public void MapFeature_LootSeenBeforeAnyBossDied_IgnoredAllRun()
    {
        var p = new FieldPlanner(() => 0.5);
        var feature = B(1758, 93);

        var first = p.Step(Mm(B(1810, 105), loot: [feature]), All, 1_000);
        var later = p.Step(Mm(B(1810, 105), loot: [feature]), All, 300_000);

        Assert.NotEqual("撿王的掉落物", first.Status);
        Assert.NotEqual("撿王的掉落物", later.Status);
    }

    /// <summary>王死了之後才出現的掉落物照樣去撿；地圖圖案以外的位置也照樣撿</summary>
    [Fact]
    public void MapFeature_NewLootAfterWindow_StillPicked()
    {
        var p = new FieldPlanner(() => 0.5);
        p.Step(Mm(B(1810, 105), loot: [B(1758, 93)]), All, 1_000);

        var d = p.Step(Mm(B(1800, 100), loot: [B(1758, 93), B(1830, 120)]), All, 200_000);

        Assert.Equal("撿王的掉落物", d.Status);
        Assert.True(d.Click!.Value.X > 0.5, $"{d.Click}");
    }

    /// <summary>新的一局重新學（地圖可能不一樣）</summary>
    [Fact]
    public void MapFeature_NewRun_Forgets()
    {
        var p = new FieldPlanner(() => 0.5);
        p.Step(Mm(B(1810, 105), loot: [B(1758, 93)]), All, 1_000);
        p.NewRun();

        // 新的一局開局 80 秒內又看到同一點：一樣當地圖圖案（這個測試確認不會因為上一局的記錄而影響「之後」才出現的點）
        p.Step(Mm(B(1810, 105)), All, 500_000);
        var d = p.Step(Mm(B(1795, 110), loot: [B(1758, 93)]), All, 700_000);
        Assert.Equal("撿王的掉落物", d.Status);
    }
}
