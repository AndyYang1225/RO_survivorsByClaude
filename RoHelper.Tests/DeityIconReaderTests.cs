using RoHelper.Vision;
using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class DeityIconReaderTests
{
    /// <summary>實測（2026-11-25，Lv 21 斐揚森林）：道具欄 5 格道具，第 6 格是白底紫色「!」，中心 (1023, 580)</summary>
    [Fact]
    public void Find_IconInSixthSlot()
    {
        var p = DeityIconReader.Find(CardLayoutTests.Fixture("技能神祇圖示"), 1.0);

        Assert.NotNull(p);
        Assert.InRange(p.Value.X, 1020, 1026);
        Assert.InRange(p.Value.Y, 577, 583);
    }

    /// <summary>其他所有測試圖（野外、大廳、選卡、結算、說明框…）都不能誤認成「!」</summary>
    [Fact]
    public void Find_NoIconInAnyOtherFixture()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var hits = Directory.EnumerateFiles(dir, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != "技能神祇圖示")
            .Where(n => DeityIconReader.Find(CardLayoutTests.Fixture(n!), 1.0) is not null)
            .ToList();

        Assert.Empty(hits);
    }
}
