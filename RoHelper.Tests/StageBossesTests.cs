using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class StageBossesTests
{
    [Fact]
    public void MvpLeft_KnownStage()
    {
        Assert.Equal(840 - 137, StageBosses.MvpLeft("斐揚森林", 137));
        Assert.Equal(-10, StageBosses.MvpLeft("普隆德拉城", 100));
    }

    [Theory]
    [InlineData("獸人村", 100)]
    [InlineData("", 100)]
    [InlineData("斐揚森林", null)]
    public void MvpLeft_Unknown_Null(string stage, int? clock)
    {
        Assert.Null(StageBosses.MvpLeft(stage, clock));
    }

    [Fact]
    public void Next_FirstBossNotYetSpawned()
    {
        Assert.Equal(new NextBoss("Deviling", "Mini", 360 - 200), StageBosses.Next("斐揚森林", 200));
        Assert.Null(StageBosses.Next("普隆德拉城", 90));
    }

    [Fact]
    public void NextText_SameAsJs()
    {
        Assert.Equal("斐揚森林｜下一隻：Deviling（Mini）2:40 後", StageBosses.NextText("斐揚森林", 200, false, false));
        Assert.Equal("斐揚洞穴｜下一隻：Moonlight Flower（MVP）0:05 後　準備躲鐘！", StageBosses.NextText("斐揚洞穴", 835, false, false));
        Assert.Equal("撿戰利品倒數中！", StageBosses.NextText("斐揚洞穴", null, true, true));
        Assert.Equal("MVP 戰鬥中（注意躲鐘）", StageBosses.NextText("斐揚洞穴", null, false, true));
        Assert.Equal("洛陽｜下一隻：Dancing Dragon（Mini）0:50 後", StageBosses.NextText("洛陽", 100, false, false));
        Assert.Equal("", StageBosses.NextText("獸人村", 100, false, false));
    }

    /// <summary>洛陽（燈籠節）：MVP 白素貞 14:00 出現，「MVP 前先選完升級」才有依據</summary>
    [Fact]
    public void Louyang_MvpAt840()
    {
        Assert.Equal(840 - 800, StageBosses.MvpLeft("洛陽", 800));
        Assert.Equal(new NextBoss("Rice Cake Boy", "Mini", 20), StageBosses.Next("洛陽", 700));
        Assert.Equal(new NextBoss("White Lady", "MVP", 100), StageBosses.Next("洛陽", 740));
    }

    [Fact]
    public void Table_StagesAreKnownStages()
    {
        var stages = RoHelper.Engine.BuiltInSchema.Load().Stages;

        // 洛陽是活動地圖，不在遠征的地名表上（地名由 FieldInfoReader 的誤讀表對回來）
        Assert.All(StageBosses.All.Keys.Where(k => k != "洛陽"), k => Assert.Contains(k, stages));
    }
}
