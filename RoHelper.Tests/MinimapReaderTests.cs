using RoHelper.Vision.Field;

namespace RoHelper.Tests;

public class MinimapReaderTests
{
    [Fact]
    public void Read_ChestFixture_FindsMeAndOneChest_AndExcludesButtonRow()
    {
        var mm = MinimapReader.Read(CardLayoutTests.Fixture("野外2寶箱"), 1.0);

        Assert.NotNull(mm);
        Assert.InRange(mm.Me.X, 1809, 1815);
        Assert.InRange(mm.Me.Y, 128, 134);
        var chest = Assert.Single(mm.Chests);
        Assert.InRange(chest.X, 1803, 1809);
        Assert.InRange(chest.Y, 126, 132);
        Assert.Empty(mm.Loot);
        Assert.Empty(mm.Magnets);
        Assert.True(mm.Map.Width > 100 && mm.Map.Height > 100, $"地圖範圍 {mm.Map}");
    }

    [Fact]
    public void Read_PlainFixture_FindsMe_NoChestNoLoot()
    {
        var mm = MinimapReader.Read(CardLayoutTests.Fixture("野外1"), 1.0);

        Assert.NotNull(mm);
        Assert.InRange(mm.Me.X, 1808, 1814);
        Assert.InRange(mm.Me.Y, 127, 133);
        Assert.Empty(mm.Chests);
        Assert.Empty(mm.Loot);
    }

    [Fact]
    public void Read_SolidFrame_ReturnsNull()
    {
        Assert.Null(MinimapReader.Read(TestFrames.Solid(1904, 682, 20, 25, 40), 1.0));
    }

    [Fact]
    public void Read_Synthetic_ColorsAndMinimumClusterSizes()
    {
        var f = TestFrames.Solid(1904, 682, 20, 25, 40);
        TestFrames.Fill(f, new Rectangle(1800, 100, 3, 3), 192, 26, 43);  // 自己 9 點
        TestFrames.Fill(f, new Rectangle(1780, 80, 2, 2), 255, 200, 60);  // 寶箱 4 點 ≥ 3
        TestFrames.Fill(f, new Rectangle(1830, 120, 1, 1), 255, 59, 74);  // 磁鐵 1 點 < 4

        var mm = MinimapReader.Read(f, 1.0);

        Assert.NotNull(mm);
        Assert.Equal(1801, mm.Me.X, 3);
        Assert.Equal(101, mm.Me.Y, 3);
        Assert.Equal(9, mm.Me.N);
        var chest = Assert.Single(mm.Chests);
        Assert.Equal(4, chest.N);
        Assert.Empty(mm.Magnets);
    }

    [Fact]
    public void Read_Synthetic_LootOutlineNearMagnetIsIgnored()
    {
        var f = TestFrames.Solid(1904, 682, 20, 25, 40);
        TestFrames.Fill(f, new Rectangle(1800, 100, 3, 3), 192, 26, 43);
        TestFrames.Fill(f, new Rectangle(1760, 60, 3, 3), 31, 50, 92);    // 遠處的掉落物外框 → 算
        TestFrames.Fill(f, new Rectangle(1840, 140, 2, 2), 255, 59, 74);  // 磁鐵 4 點
        TestFrames.Fill(f, new Rectangle(1843, 140, 2, 2), 31, 50, 92);   // 磁鐵旁的外框 → 不算

        var mm = MinimapReader.Read(f, 1.0);

        Assert.NotNull(mm);
        Assert.Single(mm.Magnets);
        var loot = Assert.Single(mm.Loot);
        Assert.Equal(1761, loot.X, 3);
    }

    [Fact]
    public void Area_IsTopRightMapBody_ClampedToFrame()
    {
        var a = MinimapReader.Area(TestFrames.Solid(1904, 682, 0, 0, 0), 1.0);

        Assert.Equal(new Rectangle(1731, 40, 158, 130), a);
        Assert.Equal(new Rectangle(0, 40, 85, 60), MinimapReader.Area(TestFrames.Solid(100, 100, 0, 0, 0), 1.0));
    }
}
