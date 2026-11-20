namespace RoHelper.Vision.Field;

/// <summary>地圖上的一隻頭目：名字、種類（Mini／MVP）、出現時間（遊戲計時器的秒數）</summary>
public sealed record StageBoss(string Name, string Kind, int Secs);

/// <summary>下一隻頭目與還有幾秒出現</summary>
public sealed record NextBoss(string Name, string Kind, int Left);

/// <summary>
/// 各地圖頭目出現時間（遠征視窗），照抄 engine.js 的 STAGE_BOSS（171～179）。
/// 地名用 <see cref="FieldInfoReader"/> 從小地圖標題讀到的；不在表上的地圖（例如活動的洛陽）查不到，回 null（同 JS）。
/// </summary>
public static class StageBosses
{
    public static readonly IReadOnlyDictionary<string, StageBoss[]> All = new Dictionary<string, StageBoss[]>
    {
        ["普隆德拉原野"] = [new("Eclipse", "Mini", 150), new("Toad", "Mini", 300), new("Dragon Fly", "Mini", 450), new("Vocal", "Mini", 600), new("Mastering", "MVP", 720)],
        ["普隆德拉地下水道"] = [new("Tarou ×3", "Mini", 180), new("Golden Thief Bug", "MVP", 480)],
        ["失事輪"] = [new("Ghostring", "Mini", 180), new("Penomena", "Mini", 360), new("Drake", "MVP", 600)],
        ["柏伊亞嵐島"] = [new("Deviace", "Mini", 180), new("Strouf", "Mini", 360), new("King Dramoh", "Mini", 540), new("Kraken", "MVP", 720)],
        ["斐揚森林"] = [new("Angeling", "Mini", 180), new("Deviling", "Mini", 360), new("Vagabond Wolf", "Mini", 600), new("Eddga", "MVP", 840)],
        ["斐揚洞穴"] = [new("Skeleton General", "Mini", 180), new("Cat O' Nine Tails", "Mini", 360), new("Hyegun", "Mini", 600), new("Moonlight Flower", "MVP", 840)],
        ["普隆德拉城"] = [new("High Orc", "MVP", 90)],
    };

    /// <summary>MVP 還有幾秒出現（同 JS mvpLeft）：地圖不在表上或讀不到遊戲時間回 null；MVP 已經出現過是負數或 0</summary>
    public static int? MvpLeft(string stage, int? clock)
    {
        if (clock is not { } t || !All.TryGetValue(stage, out var list))
            return null;
        return list.FirstOrDefault(x => x.Kind == "MVP") is { } mvp ? mvp.Secs - t : null;
    }

    /// <summary>下一隻還沒出現的頭目（同 JS nextBoss）：地圖不在表上、讀不到遊戲時間、全部都出現過了回 null</summary>
    public static NextBoss? Next(string stage, int? clock)
    {
        if (clock is not { } t || !All.TryGetValue(stage, out var list))
            return null;
        return list.FirstOrDefault(x => x.Secs > t) is { } b ? new NextBoss(b.Name, b.Kind, b.Secs - t) : null;
    }

    /// <summary>
    /// 面板「下一隻」那一行（同 JS render 的 nx）：撿取倒數 →「撿戰利品倒數中！」；計時器已經變成倒數（MVP 出現了）→「MVP 戰鬥中」（斐揚洞穴加「（注意躲鐘）」）；
    /// 查得到下一隻 →「地名｜下一隻：名字（種類）m:ss 後」（斐揚洞穴的 MVP 剩 10 秒內加「　準備躲鐘！」）；其他空字串。
    /// 跟 JS 不同：地名讀不到時不顯示 JS 的「（地圖不明：從遠征出發才查得到頭目時間）」：視覺模式的地名是 OCR 小地圖標題，不是從遠征出發記下來的。
    /// </summary>
    public static string NextText(string stage, int? clock, bool loot, bool countdown)
    {
        if (loot)
            return "撿戰利品倒數中！";
        if (countdown)
            return "MVP 戰鬥中" + (stage == "斐揚洞穴" ? "（注意躲鐘）" : "");
        if (Next(stage, clock) is not { } nb)
            return "";
        var warn = stage == "斐揚洞穴" && nb.Kind == "MVP" && nb.Left <= 10 ? "　準備躲鐘！" : "";
        return $"{stage}｜下一隻：{nb.Name}（{nb.Kind}）{nb.Left / 60}:{nb.Left % 60:00} 後{warn}";
    }
}
