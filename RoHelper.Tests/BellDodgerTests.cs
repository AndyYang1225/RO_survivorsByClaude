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
        // 目標點 (952, 386−30=356)＝腳下（畫面中心 341 往下 15）
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(Bell(952, 386, 100, 20)), 10_100));
        // 離目標 30 px（0.3 個鐘寬）：還沒站好時要走（門檻 0.25），已經站好就繼續等（門檻 0.4）
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(Bell(982, 386, 100, 20)), 10_200));
        // 看不到鐘但已經站好：原地等
        Assert.Equal(new FieldDecision("在鐘下等待", null), d.Step(View(null), 10_300));

        var fresh = Announced();
        Assert.Equal("躲到鐘下！", fresh.Step(View(Bell(982, 386, 100, 20)), 10_100)!.Status);
    }

    /// <summary>
    /// 實測（2026-11-21 斐揚洞穴月夜貓，站在鐘頂）：角色站在鐘頂上方，在鐘框裡但離底座圓盤很遠，JS 的「在框內就算站好」讓角色停在那裡被秒殺。
    /// 在框內但離目標遠 → 還要走過去
    /// </summary>
    [Fact]
    public void Step_OnBellTopInsideBox_StillWalksToDisc()
    {
        var d = Announced().Step(View(Bell(952, 600, 300, 400)), 10_100)!;

        Assert.Equal("躲到鐘下！", d.Status);
        Assert.True(d.Click!.Value.Y > 0.5, $"{d.Click}");
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

    /// <summary>使用者 2026-11-27：離王太遠、畫面上沒有鐘 → 往小地圖上的 MVP 走過去找鐘（MVP 優先於比較近的小頭目）</summary>
    [Fact]
    public void Planner_NoBellOnScreen_WalksToMvp()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);
        var mm = new MinimapInfo(Me, [], [], [], [new(1812, 110, 5), new(1840, 105, 5)], new RectangleF(1731, 40, 157, 129))
            { Mvps = [new(1840, 105, 5)] };

        var d = p.Step(mm, All, 10_100, bell: View(null));

        Assert.Equal("躲鐘：往王那邊找鐘", d.Status);
        Assert.True(d.Click!.Value.X > 0.5);
        Assert.Equal(0.5, d.Click.Value.Y, 3);

        // MVP 標記閃掉一輪：照上次看到的位置走
        Assert.Equal("躲鐘：往王那邊找鐘", p.Step(Mm(), All, 10_600, bell: View(null)).Status);
        // 鐘出現在畫面上：照常躲到鐘下
        Assert.Equal("躲到鐘下！", p.Step(mm, All, 10_900, bell: View(Bell(1400, 400))).Status);
    }

    [Fact]
    public void Planner_NoBellOnScreen_NextToBoss_Waits()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);
        var mm = new MinimapInfo(Me, [], [], [], [new(1811, 105, 5)], new RectangleF(1731, 40, 157, 129));

        Assert.Equal(new FieldDecision("躲鐘：在王旁邊找鐘", null), p.Step(mm, All, 10_100, bell: View(null)));
    }

    [Fact]
    public void Planner_NoBellNoBoss_WaitsInPlace()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);

        Assert.Equal(new FieldDecision(BellDodger.Searching, null), p.Step(Mm(), All, 10_100, bell: View(null)));
    }

    /// <summary>警示範圍：畫面 1904×682，腳下 (952,356)</summary>
    private static DangerView Danger(params DangerZone[] zones) => new(zones, 1904, 682, 1.0);

    /// <summary>站在攻擊範圍裡：往背離範圍中心的方向逃（範圍中心在左邊 → 往右）</summary>
    [Fact]
    public void Planner_InsideDanger_EscapesAway()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);

        var d = p.Step(Mm(), All, 10_000, danger: Danger(new DangerZone(900, 356, 150, 120, "粉紅圓")));

        Assert.Equal("避開王的攻擊範圍", d.Status);
        Assert.True(d.Click!.Value.X * 1904 > 900 + 150);
    }

    /// <summary>要去的寶箱在攻擊範圍後面：繞開，不直線穿過去</summary>
    [Fact]
    public void Planner_PathThroughDanger_Reroutes()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        // 寶箱在小地圖正右邊 → 往畫面右邊點；警示範圍擋在右邊
        var mm = new MinimapInfo(Me, [], [new(1840, 105, 5)], [], [], new RectangleF(1731, 40, 157, 129));
        var zone = new DangerZone(1150, 356, 100, 80, "紅圈");

        var plain = new FieldPlanner(() => 0.5, learnMapFeatures: false).Step(mm, All, 10_000);
        Assert.Equal("前往寶箱", plain.Status);

        var d = p.Step(mm, All, 10_000, danger: Danger(zone));
        Assert.Equal("前往寶箱（繞開攻擊範圍）", d.Status);
        var c = d.Click!.Value;
        Assert.False(zone.Contains(c.X * 1904, c.Y * 682));
        Assert.NotEqual(plain.Click!.Value.Y, c.Y, 2);
    }

    /// <summary>躲鐘中不看警示範圍（鐘的紅圈是要站進去的）</summary>
    [Fact]
    public void Planner_Dodging_IgnoresDanger()
    {
        var p = new FieldPlanner(() => 0.5, learnMapFeatures: false);
        p.Bell.Announce("躲到鐘下！", 10_000);

        var d = p.Step(Mm(), All, 10_100, bell: View(Bell(1400, 400)), danger: Danger(new DangerZone(952, 356, 300, 200, "紅圈")));

        Assert.Equal("躲到鐘下！", d.Status);
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

    /// <summary>真實大小的大鐘（n≥650、高≥220）</summary>
    private static BellBlob Big(int x = 952, int bottom = 420)
        => new(x, bottom, x - 100, x + 100, bottom - 260, bottom, 1300, 200, 260, 1300, 0, 0.3);

    [Fact]
    public void Step_BigBellStillVisible_KeepsDodgingPast15s()
    {
        // 實測 2026-11-26：鐘持續 36 秒以上，公告沒了 15 秒到就走出去撿寶箱被秒殺
        var d = Announced();
        for (long t = 10_000; t <= 60_000; t += 1500)
            Assert.NotNull(d.Step(View(Big()), t));
        Assert.True(d.Active(63_000));
    }

    [Fact]
    public void Step_BigBellGone_EndsEarly_AndBannerAfterglowIgnored()
    {
        var d = Announced();
        Assert.NotNull(d.Step(View(Big()), 10_500));
        Assert.NotNull(d.Step(View(null), 12_000));
        Assert.NotNull(d.Step(View(null), 13_500));
        Assert.Null(d.Step(View(null), 14_600));
        Assert.False(d.Active(14_700));

        d.Announce("躲到鐘下！", 15_000);
        Assert.False(d.Active(15_100));
        d.Announce("躲到鐘下！", 20_000);
        Assert.True(d.Active(20_100));
    }

    [Fact]
    public void Step_SmallBellOnly_NoEarlyEnd()
    {
        // 沒看過大鐘（只看到被切掉的小塊）就不提早結束，照公告的 15 秒
        var d = Announced();
        Assert.NotNull(d.Step(View(Bell(1400, 400)), 10_100));
        Assert.NotNull(d.Step(View(null), 20_000));
        Assert.True(d.Active(24_000));
    }

    /// <summary>
    /// 實測（2026-11-27 C 斐揚洞穴）：站在鐘下但只找到鐘身（n 439～641，不到大鐘門檻），公告 15 秒到就走掉被秒殺。
    /// 站在鐘下還看得到鐘 → 一直延長；鐘消失 4 秒 → 結束
    /// </summary>
    [Fact]
    public void Step_ParkedUnderSmallBell_KeepsDodgingUntilBellGone()
    {
        var d = Announced();
        var small = Bell(952, 386, 100, 20);
        // 最後一輪在 40_100
        for (long t = 10_100; t <= 40_100; t += 1500)
            Assert.Equal("在鐘下等待", d.Step(View(small), t)!.Status);
        Assert.True(d.Active(42_000));

        Assert.NotNull(d.Step(View(null), 41_500));
        Assert.NotNull(d.Step(View(null), 43_000));
        Assert.Null(d.Step(View(null), 44_600));
    }

    [Fact]
    public void Step_RealMouth_TargetsMouthNotBoxBottom()
    {
        // 外框 820～1096 × 156～456（連著火盆），鐘口 (924,284)；腳在 (952,356) 不算站好，要走去鐘口
        var b = new BellBlob(958, 456, 820, 1096, 156, 456, 1185, 276, 300, 1174, 11, 0.22, 1080, 924, 284);
        var d = Announced().Step(View(b), 10_100);

        Assert.NotNull(d);
        Assert.Equal("躲到鐘下！", d.Status);
        Assert.Equal(924 / 1904.0, d.Click!.Value.X, 3);
        Assert.Equal(284 / 682.0, d.Click.Value.Y, 3);

        // 腳在鐘口 25 px 內才算站好
        var ok = new BellBlob(958, 456, 820, 1096, 156, 456, 1185, 276, 300, 1174, 11, 0.22, 1080, 952, 340);
        Assert.Equal("在鐘下等待", Announced().Step(View(ok), 10_100)!.Status);
    }
}
