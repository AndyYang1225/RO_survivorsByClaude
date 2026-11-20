using RoHelper.Storage;

namespace RoHelper.Vision.Field;

/// <summary>
/// 野外走位的設定快照（每輪從 cfg 讀一次）。欄位對應 engine.js 的 cfg 鍵：seekLoot、seekMagnet、seekChest、autoWander、stayCenter、zoomOut、
/// tSeekN（撿東西時每 N 毫秒下一次移動指令）、tWanderN（繞圈時每 N 毫秒換一次方向）。
/// 刻意不做（第二批）：seekBall（節慶扭蛋，JS 讀遊戲資料）、precise（精確模式，JS 讀遊戲資料）、吃肉、躲鐘、bossSpot（王死掉的地點，要 OCR 小地圖座標）、
/// MVP 倒數撿取階段、farLimit（JS 在小地圖模式本來就沒用）、chestMoney（要 OCR 金錢；第一批一律當買得起）、lowHpStop。
/// </summary>
public sealed record FieldSettings(
    bool SeekLoot, bool SeekMagnet, bool SeekChest, bool AutoWander, bool StayCenter, bool ZoomOut,
    int SeekEvery, int WanderEvery)
{
    public static FieldSettings From(ConfigStore c) => new(
        c.GetBool("seekLoot"), c.GetBool("seekMagnet"), c.GetBool("seekChest"), c.GetBool("autoWander"),
        c.GetBool("stayCenter"), c.GetBool("zoomOut"),
        c.GetInt("tSeekN", 1500), c.GetInt("tWanderN", 1500));
}
