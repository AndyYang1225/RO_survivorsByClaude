using RoHelper.Vision;

namespace RoHelper.Tests;

public class StatusHistoryTests
{
    [Fact]
    public void Add_SkipsSameAsLast()
    {
        var h = new StatusHistory(10);
        h.Add(new DateTime(2026, 1, 1, 0, 0, 0), "待機");
        h.Add(new DateTime(2026, 1, 1, 0, 0, 1), "待機");
        h.Add(new DateTime(2026, 1, 1, 0, 0, 2), "點：返回角色");

        Assert.Equal(["待機", "點：返回角色"], h.Recent(10).Select(e => e.Action));
    }

    [Fact]
    public void Add_DropsOldestBeyondCapacity()
    {
        var h = new StatusHistory(3);
        for (var i = 0; i < 5; i++)
            h.Add(DateTime.MinValue.AddSeconds(i), $"a{i}");

        Assert.Equal(["a2", "a3", "a4"], h.Recent(10).Select(e => e.Action));
        Assert.Equal(["a3", "a4"], h.Recent(2).Select(e => e.Action));
        Assert.Equal("a4", h.Last);
    }

    [Fact]
    public void Empty_LastIsEmptyString() => Assert.Equal("", new StatusHistory(3).Last);
}
