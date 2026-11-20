using System.Text.Json.Nodes;

using RoHelper.Vision;

namespace RoHelper.Tests;

public class EvidenceTests
{
    [Fact]
    public void Policy_TriggersByPrefix_WithCooldown()
    {
        var p = new EvidencePolicy(cooldownMs: 60_000, periodicMs: 300_000);

        Assert.Equal("讀不到卡名", p.Check("看起來像選卡但讀不到卡名，不動作", 1_000));
        Assert.Null(p.Check("看起來像選卡但讀不到卡名，不動作", 30_000));
        Assert.Equal("讀不到卡名", p.Check("看起來像選卡但讀不到卡名，不動作", 61_001));
        Assert.Equal("按鍵沒反應", p.Check("選卡按鍵沒反應，改點卡片：雷爆術", 2_000));
        Assert.Equal("例外", p.Check("視覺判斷失敗 : boom", 3_000));
    }

    [Fact]
    public void Policy_Periodic_NotOnFirstCall()
    {
        var p = new EvidencePolicy(cooldownMs: 60_000, periodicMs: 300_000);

        Assert.Null(p.Check("待機", 0));
        Assert.Null(p.Check("待機", 299_999));
        Assert.Equal("定時", p.Check("待機", 300_000));
        Assert.Null(p.Check("待機", 300_001));
    }

    [Fact]
    public void Store_SavesPngAndJson_AndPrunesOldest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rohelper-ev-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EvidenceStore(dir, maxShots: 2);
            var frame = TestFrames.Solid(8, 6, 10, 20, 30);
            for (var i = 0; i < 3; i++)
                store.Save(frame, "定時", new DateTime(2026, 11, 20, 23, 0, i), new JsonObject { ["i"] = i });

            var pngs = Directory.GetFiles(dir, "*.png").Select(Path.GetFileName).Order().ToList();
            Assert.Equal(["20261120-230001-定時.png", "20261120-230002-定時.png"], pngs);
            Assert.Equal(2, Directory.GetFiles(dir, "*.json").Length);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "20261120-230002-定時.json")))!;
            Assert.Equal(2, (int)json["i"]!);
            Assert.Equal("定時", (string)json["reason"]!);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Store_Recent_NewestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rohelper-ev-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EvidenceStore(dir, maxShots: 10);
            var frame = TestFrames.Solid(8, 6, 10, 20, 30);
            store.Save(frame, "例外", new DateTime(2026, 11, 20, 23, 0, 0), new JsonObject());
            store.Save(frame, "定時", new DateTime(2026, 11, 20, 23, 0, 5), new JsonObject());

            Assert.Equal(["20261120-230005-定時", "20261120-230000-例外"], store.Recent(10).Select(Path.GetFileNameWithoutExtension));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
