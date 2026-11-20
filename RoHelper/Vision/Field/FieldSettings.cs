using RoHelper.Storage;

namespace RoHelper.Vision.Field;

/// <summary>
/// 野外走位的設定快照（每輪從 cfg 讀一次）。欄位對應 engine.js 的 cfg 鍵：seekLoot、seekMagnet、seekChest、autoWander、stayCenter、zoomOut、
/// tSeekN（撿東西時每 N 毫秒下一次移動指令）、tWanderN（繞圈時每 N 毫秒換一次方向）、chestMoney（金錢不夠開寶箱時不追寶箱）、
/// lowHpStop／lowHpStopN（HP 低於 N% 時停止走位）、farLimit（太遠的磁鐵、寶箱先不追）、bossSpot（王死掉後跑到死掉的地點）。
/// chestMoney、farLimit、bossSpot 沒設定時當 true（同 schema 的預設）；lowHpStopN 沒設定時是 30（同 schema）；其他開關沒設定時是 false（由 ConfigDefaults 依 schema 補齊）。
/// 新加的欄位放在最後並給預設值（同 schema 的預設），既有的呼叫端不用改。
/// 刻意不做（第二批）：seekBall（節慶扭蛋，JS 讀遊戲資料）、precise（精確模式，JS 讀遊戲資料）、吃肉、躲鐘、MVP 倒數撿取階段。
/// </summary>
public sealed record FieldSettings(
    bool SeekLoot, bool SeekMagnet, bool SeekChest, bool AutoWander, bool StayCenter, bool ZoomOut,
    int SeekEvery, int WanderEvery, bool ChestMoney, bool LowHpStop = false, int LowHpStopN = 30,
    bool FarLimit = true, bool BossSpot = true)
{
    public static FieldSettings From(ConfigStore c) => new(
        c.GetBool("seekLoot"), c.GetBool("seekMagnet"), c.GetBool("seekChest"), c.GetBool("autoWander"),
        c.GetBool("stayCenter"), c.GetBool("zoomOut"),
        c.GetInt("tSeekN", 1500), c.GetInt("tWanderN", 1500),
        c.Current["chestMoney"] is null || c.GetBool("chestMoney"),
        c.GetBool("lowHpStop"), c.GetInt("lowHpStopN", 30),
        c.Current["farLimit"] is null || c.GetBool("farLimit"),
        c.Current["bossSpot"] is null || c.GetBool("bossSpot"));

    /// <summary>
    /// 買不買得起寶箱（同 JS canAffordChest）：chestMoney 關掉、或讀不到金錢／價格（hud 是 null）時當買得起，
    /// 讀不到就不追的話，OCR 一失敗整局都不會去開寶箱。
    /// </summary>
    public bool CanAffordChest(Hud? hud) => !ChestMoney || hud is null || hud.Money >= hud.ChestCost;
}
