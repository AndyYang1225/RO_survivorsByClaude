using RoHelper.Vision.Field;

namespace RoHelper.Tests;

/// <summary>躲鐘的狀態機（合成的鐘色塊；畫面 1904×682，中心 (952,341)＝自己）</summary>
public class BellDodgerTests
{
    private static readonly MinimapBlob Me = new(1810, 105, 5);
    private static readonly FieldSettings All = new(true, true, true, true, true, true, 500, 500, true);

    /// <summary>寬 w 的鐘，底部中間在 (x, bottom)</summary>
    private static BellBlob Bell(int x, int bottom, int w = 100, int h = 90)
        => new(x, bottom, x - w / 2, x + w / 2, bottom - h, bottom, 500, w, h, 500, 0, 1.0);

    private static BellView View(BellBlob? b) => new(b, 1904, 682, 1.0);

    private static MinimapInfo Mm(MinimapBlob[]? loot = null)
        => new(Me, [], [], loot ?? [], [], new RectangleF(1731, 40, 157, 129));

    private static BellDodger Announced(string text = "躲到鐘下！", long now = 10_000)
    {
        var d = new BellDodger();
        d.Announce(text, now);
        return d;
    }

    [Fact]
    public void Step_NoAnnouncement_NotDodging()
    {
        var d = new BellDodger();
        d.Announce("伺服器將進行更新", 10_000);

        Assert.Null(d.Step(View(Bell(1400, 400)), 10_000));
    }

    [Fact]
    public void Step_NoBellYet_WaitsInPlace()
    {
        var d = Announced();

        Assert.Equal(new FieldDecision("躲鐘：找鐘中，原地等", null), d.Step(View(null), 10_100));
        Assert.Equal(new FieldDecision("躲鐘：找鐘中，原地等", null), d.Step(null, 10_200));
    }

    [Fact]
    public void Step_BellFar_ClicksAboveBellBottom()
    {
        var d = Announced().Step(View(Bell(1400, 400)), 10_100);

        Assert.NotNull(d);
        Assert.Equal("躲到鐘下！", d.Status);
        // 目標點＝鐘底中間往上 0.3 個鐘寬
        Assert.Equal(1400 / 1904.0, d.Click!.Value.X, 3);
        Assert.Equal((400 - 30) / 682.0, d.Click.Value.Y, 3);
    }

    [Fact]
    public void Step_BellLost_FollowsLastPositionFor1500Ms()
    {
        var d = Announced();
        var first = d.Step(View(Bell(1400, 400)), 10_100)!;

        var again = d.Step(View(null), 11_000)!;
        Assert.Equal("躲到鐘下！（照上次位置）", again.Status);
        Assert.Equal(first.Click, again.Click);

        Assert.Equal(new FieldDecision("躲鐘：找鐘中，原地等", null), d.Step(View(null), 11_700));
    }

    [Fact]
    public void Step_UnderBell_ParksAndStaysParkedWithLooserRadius()
    {
        var d = Announced();
        // 目標點 (952, 371−30=341)＝畫面中心
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(Bell(952, 371, 100, 20)), 10_100));
        // 鐘框很扁、自己不在框內；離目標 50 px（0.5 個鐘寬）：還沒站好時要走，已經站好就繼續等
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(Bell(1002, 371, 100, 20)), 10_200));
        // 看不到鐘但已經站好：原地等
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(null), 10_300));

        var fresh = Announced();
        Assert.Equal("躲到鐘下！", fresh.Step(View(Bell(1002, 371, 100, 20)), 10_100)!.Status);
    }

    [Fact]
    public void Step_InsideBellBox_Parks()
    {
        // 自己在鐘框裡（離目標點遠，但在框內）
        var d = Announced().Step(View(Bell(952, 600, 300, 400)), 10_100);

        Assert.Equal(new FieldDecision("在鐘下等待", null), d);
    }

    [Fact]
    public void Step_BellWidth_ShrinksSlowly()
    {
        // 鐘寬取 max(這次, 上次 × 0.8)：鐘閃一下變小，站好的範圍不會跟著突然變小
        var d = Announced();
        d.Step(View(Bell(1400, 400, 200, 100)), 10_100);
        // 這次寬 40，但 bw＝160：目標點往上 48
        var s = d.Step(View(Bell(1400, 400, 40, 40)), 10_200)!;
        Assert.Equal((400 - 48) / 682.0, s.Click!.Value.Y, 3);
    }

    [Fact]
    public void Announce_Lasts15SecondsOr4AfterTooLate()
    {
        var d = Announced(now: 10_000);
        Assert.True(d.Active(24_999));
        Assert.False(d.Active(25_000));
        Assert.Null(d.Step(View(Bell(1400, 400)), 25_000));

        d.Announce("鐘：太遲了", 12_000);
        Assert.True(d.Active(15_999));
        Assert.False(d.Active(16_000));
    }

    [Fact]
    public void Planner_DodgeBeatsLootAndLowHp()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);
        var s = All with { LowHpStop = true };

        var d = p.Step(Mm(loot: [new(1830, 90, 5)]), s, 10_100, hp: 0.1, bell: View(Bell(1400, 400)));

        Assert.Equal("躲到鐘下！", d.Status);
        Assert.NotNull(d.Click);
    }

    [Fact]
    public void Planner_LootPhase_EndsDodge()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);

        var d = p.Step(Mm(loot: [new(1830, 90, 5)]), All, 10_100, lootPhase: true, bell: View(Bell(1400, 400)));

        Assert.Equal("倒數中！撿王的掉落物", d.Status);
        Assert.False(p.Bell.Active(10_200));
    }

    [Fact]
    public void Planner_BellDodgeOff_Ignored()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);

        var d = p.Step(Mm(loot: [new(1830, 90, 5)]), All with { BellDodge = false }, 10_100, bell: View(Bell(1400, 400)));

        Assert.Equal("撿王的掉落物", d.Status);
    }

    [Fact]
    public void Planner_AfterDodgeClick_WaitsSeekEveryBeforeOtherMoves()
    {
        // 同 JS：躲鐘點了也算移動（st.lastMove），躲鐘結束後不會馬上又點別的
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);
        p.Step(Mm(), All, 10_100, bell: View(Bell(1400, 400)));
        p.Bell.End();

        Assert.Null(p.Step(Mm(loot: [new(1830, 90, 5)]), All, 10_300).Click);
        Assert.Equal("撿王的掉落物", p.Step(Mm(loot: [new(1830, 90, 5)]), All, 10_700).Status);
    }

    [Fact]
    public void Planner_NewRun_EndsDodge()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);
        p.NewRun();

        Assert.False(p.Bell.Active(10_100));
    }
}
