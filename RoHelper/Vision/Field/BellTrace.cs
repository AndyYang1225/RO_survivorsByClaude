using System.Text.Json.Nodes;

namespace RoHelper.Vision.Field;

/// <summary>
/// 躲鐘紀錄（診斷用，純邏輯：時間由呼叫端傳入）：每次躲鐘從開始到結束記一份摘要，放進對局紀錄的 bells；
/// 開始、之後每 shotEvery 毫秒、結束各存一張截圖（回傳截圖理由，呼叫端存）。
/// 摘要：公告原文、遊戲時間、持續秒數、幾輪、幾輪找到鐘、幾輪站在鐘下、結束時的狀態與血量，以及每秒一筆取樣（找到的鐘外框、狀態、血量）。
/// 為什麼要有：躲鐘失敗事後要分得出是「找不到鐘」（辨識問題）還是「找到了沒站好」（走位問題）。
/// </summary>
public sealed class BellTrace(long shotEvery = 3000, long sampleEvery = 1000)
{
    private long? _from;
    private long _nextShot, _nextSample;
    private int _frames, _found, _parked;
    private string _banner = "", _clock = "", _status = "";
    private double? _hpStart, _hp;
    private readonly List<JsonObject> _samples = [];

    /// <summary>正在記錄一次躲鐘</summary>
    public bool Active => _from is not null;

    /// <summary>
    /// 每輪野外走位呼叫一次。active＝這一輪在躲鐘；banner＝觸發躲鐘的公告原文；view＝這一輪找鐘的結果；status＝走位決策的狀態；hp 0～1。
    /// 回傳：這一輪要存的截圖理由（null＝不存）、躲鐘剛結束時的摘要（null＝還沒結束）。
    /// </summary>
    public (string? Shot, JsonObject? Done) Step(bool active, string banner, BellView? view, string status, double? hp, string clock, long now)
    {
        if (!active)
            return _from is null ? (null, null) : ("躲鐘結束", Finish(now));
        string? shot = null;
        if (_from is null)
        {
            _from = now;
            _nextShot = now + shotEvery;
            _nextSample = now;
            _frames = _found = _parked = 0;
            _banner = banner;
            _clock = clock;
            _hpStart = hp;
            _samples.Clear();
            shot = "躲鐘開始";
        }
        else if (now >= _nextShot)
        {
            shot = $"躲鐘{(now - _from.Value) / 1000}s";
            _nextShot += shotEvery;
        }
        _frames++;
        var b = view?.Bell;
        if (b is not null)
            _found++;
        if (status == "在鐘下等待")
            _parked++;
        _status = status;
        _hp = hp ?? _hp;
        if (now >= _nextSample)
        {
            _nextSample = now + sampleEvery;
            _samples.Add(new JsonObject
            {
                ["t"] = Math.Round((now - _from.Value) / 1000.0, 1),
                ["bell"] = b is null ? null : $"{b.X0},{b.Y0},{b.X1},{b.Y1} n{b.N} g{b.Gold} r{b.Red} d{b.Dim}",
                ["status"] = status,
                ["hp"] = Pct(hp),
            });
        }
        return (shot, null);
    }

    /// <summary>新的一局、躲鐘關掉時丟掉還沒結束的紀錄</summary>
    public void Reset() => _from = null;

    private JsonObject Finish(long now)
    {
        var o = new JsonObject
        {
            ["banner"] = _banner,
            ["clock"] = _clock,
            ["secs"] = Math.Round((now - _from!.Value) / 1000.0, 1),
            ["frames"] = _frames,
            ["found"] = _found,
            ["parked"] = _parked,
            ["lastStatus"] = _status,
            ["hpStart"] = Pct(_hpStart),
            ["hpEnd"] = Pct(_hp),
            ["samples"] = new JsonArray(_samples.Select(s => (JsonNode?)s.DeepClone()).ToArray()),
        };
        _from = null;
        return o;
    }

    private static int? Pct(double? hp) => hp is { } h ? (int)Math.Round(h * 100) : null;
}
