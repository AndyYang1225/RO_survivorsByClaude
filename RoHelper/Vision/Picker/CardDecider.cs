using System.Text.RegularExpressions;

namespace RoHelper.Vision.Picker;

/// <summary>
/// 選卡決策：engine.js decide()（約 448～559 行）的 C# 版，判斷順序和理由文字一行對一行。
/// 順序：1 神祇 → 2 放逐（升級卡）→ 3 技能專注（升級卡）→ 道具專注 → 可選的卡（排除放逐／強制不選／別的神祇）
/// → 4 技能優先（升級卡）→ 5 不學清單外的新技能（升級卡）→ 6 尋寶率上限 → 勾選的道具 → 尋寶已滿→生命 → 選法（第一張／稀有度／尋寶率優先）。
/// 尋寶率（RunPickState.Treasure）由 VisionEngine 把滑鼠移到道具欄讀說明框得到（<see cref="Field.TreasureReader"/>）。
/// 跟 JS 不同：尋寶卡除了卡片文字有「尋寶」，卡名是已知的尋寶道具也算（<see cref="HasTreasure"/>），因為 OCR 偶爾把「尋寶」讀錯。
/// 純函式：不改 RunPickState（選了之後要記什麼由 PickerFlow 負責）。
/// </summary>
public static partial class CardDecider
{
    public static PickDecision Decide(PickerInfo p, PickSettings s, RunPickState st)
    {
        var cards = p.Cards;
        var all = Enumerable.Range(0, cards.Count).ToList();

        // 1. 神祇（寶箱第五欄）
        PickDecision? deityPick = null;
        if (s.DeityLock && st.DeityLock.Length > 0)
        {
            var same = all.FirstOrDefault(i => IsDeity(cards[i]) && SameDeity(cards[i].Name, st.DeityLock), -1);
            if (same >= 0)
                deityPick = new(PickDo.Pick, same, "神祇精煉：" + st.DeityLock);
        }
        else if (s.DeityPrio)
        {
            foreach (var w in s.DeityList)
            {
                var i = all.FirstOrDefault(i => IsDeity(cards[i]) && w.Length > 0 && OcrFix(cards[i].Name).Contains(OcrFix(w)), -1);
                if (i < 0)
                    continue;
                deityPick = new(PickDo.Pick, i, "神祇優先：" + w);
                break;
            }
        }
        var skipDeity = false;
        // 不特別優先：交給後面的一般規則（道具勾選、稀有度）
        if (deityPick is not null && s.DeityOrder == "none")
            deityPick = null;
        if (deityPick is { Index: { } di })
        {
            // 「稀有度比較」模式：同一次選卡裡有別張卡比神祇卡更稀有 → 這次不選神祇
            var better = s.DeityOrder == "rarity" && all.Any(i => !IsDeity(cards[i]) && !IsSkipped(cards[i], s)
                && !ShouldBanish(cards[i], s, st) && cards[i].Rarity > cards[di].Rarity);
            if (!better)
                return deityPick;
            skipDeity = true;
        }

        if (!p.IsChest)
        {
            // 2. 放逐（一次一張）
            var b = all.FirstOrDefault(i => ShouldBanish(cards[i], s, st) && Usable(cards[i], ActKind.Banish)
                && (!IsLearnedUnwanted(cards[i], st) || KeepAllows(cards[i], s, st)), -1);
            if (b >= 0)
                return new(PickDo.Banish, b, IsLearnedUnwanted(cards[b], st) ? "學到的清單外技能" : "放逐清單");

            // 3. 專注：前 N 個「優先」技能（不含學到後放逐的），每個只專注一次
            if (s.AutoFocus)
            {
                var fl = s.Skills.Focus.Where(w => s.Skills.Order.Contains(w)).ToList();
                var targets = fl.Count > 0 ? fl : s.Skills.Order.Where(w => !s.Skills.After.Contains(w)).Take(s.AutoFocusN).ToList();
                var f = all.FirstOrDefault(i => SkillOf(cards[i], targets) is { } w && !st.Focused.Contains(w) && Usable(cards[i], ActKind.Focus), -1);
                if (f >= 0)
                {
                    var w = SkillOf(cards[f], targets)!;
                    return new(PickDo.Focus, f, "專注：" + w, w);
                }
            }
        }

        // 道具專注：勾了 ★ 的道具出現時先按「專注」（升級卡、寶箱卡都適用；每個道具每局只專注一次）
        if (s.AutoFocus && s.ItemFocus.Count > 0)
        {
            string? ItemOf(CardInfo c) => s.ItemFocus.FirstOrDefault(x => x.Length > 0 && Squash(c.Name).Contains(Squash(x)));
            var f = all.FirstOrDefault(i => ItemOf(cards[i]) is { } w && !st.Focused.Contains("item:" + w) && Usable(cards[i], ActKind.Focus), -1);
            if (f >= 0)
            {
                var w = ItemOf(cards[f])!;
                return new(PickDo.Focus, f, "道具專注：" + w, "item:" + w);
            }
        }

        // 可以考慮的卡：不含要放逐的；強制不選（神祇 ✕、技能「不選」、道具 ✕）不考慮；已鎖定神祇時，不同的神祇也不考慮
        var pool = all.Where(i => !ShouldBanish(cards[i], s, st) && !IsBannedDeity(cards[i], s) && !IsSkipped(cards[i], s)
            && !(skipDeity && IsDeity(cards[i]))
            && !(s.DeityLock && st.DeityLock.Length > 0 && IsDeity(cards[i]) && !SameDeity(cards[i].Name, st.DeityLock))).ToList();
        if (pool.Count == 0)
        {
            // 全是不要的 → 能重抽就重抽；不能才只好挑一張（遊戲規定一定要選）
            if (p.RerollAll is { Usable: true })
                return new(PickDo.Reroll, null, "全部都是強制不選的卡");
            pool = all.Where(i => !IsBannedDeity(cards[i], s) && !IsSkipped(cards[i], s)).ToList();
            if (pool.Count == 0)
                pool = all;
        }

        if (!p.IsChest)
        {
            var order = s.Skills.Order;
            int Rank(int i) => order.ToList().FindIndex(w => w.Length > 0 && OcrFix(cards[i].Name).Contains(OcrFix(w)));

            // 4. 技能優先：第一個有出現就選；其餘的看稀有度，同稀有度照順序
            var first = pool.FirstOrDefault(i => Rank(i) == 0, -1);
            if (first >= 0)
                return new(PickDo.Pick, first, "技能第 1 優先");
            var hits = pool.Where(i => Rank(i) > 0).OrderByDescending(i => cards[i].Rarity).ThenBy(Rank).ToList();
            if (hits.Count > 0)
                return new(PickDo.Pick, hits[0], "技能優先（稀有度 > 順序）");

            // 5. 不學清單外的新技能
            var bad = pool.Where(i => IsUnwantedNew(cards[i], s)).ToList();
            if (s.AvoidNew && bad.Count > 0)
            {
                var good = pool.Where(i => !IsUnwantedNew(cards[i], s)).ToList();
                if (good.Count > 0)
                    pool = good;
                else
                {
                    if (s.RerollAll && p.RerollAll is { Usable: true })
                        return new(PickDo.Reroll, null, "只有不要的新技能");
                    var keep = s.BanishKeep ? s.BanishKeepN : 0;
                    var bi = bad.FirstOrDefault(i => cards[i].Button(ActKind.Banish) is { Usable: true } bb && bb.Left > keep, -1);
                    if (bi >= 0)
                        return new(PickDo.Banish, bi, "不要的新技能（重抽用完）");
                }
            }
            else if (bad.Count == pool.Count && s.RerollAll && p.RerollAll is { Usable: true })
                return new(PickDo.Reroll, null, "只有不要的新技能");
        }

        // 6. 道具／寶箱／其他。尋寶率超過上限：還有別的卡時不選尋寶卡
        var capped = s.TreasureCap && st.Treasure >= s.TreasureCapN;
        if (capped)
        {
            var nt = pool.Where(i => !HasTreasure(cards[i])).ToList();
            if (nt.Count > 0)
                pool = nt;
        }
        if (s.ItemPrio.Count > 0)
        {
            var fav = pool.Where(i => IsFavItem(cards[i], s)).ToList();
            if (fav.Count > 0)
                return new(PickDo.Pick, RarestIndex(cards, fav), "勾選的道具");
        }
        if (capped && s.HpAfterCap)
        {
            var hp = pool.Where(i => HasHp(cards[i])).ToList();
            if (hp.Count > 0)
                return new(PickDo.Pick, RarestIndex(cards, hp), "尋寶已滿→生命");
        }
        if (s.PickOrder == "first")
            return new(PickDo.Pick, pool[0], "第一張");
        if (s.PickOrder == "rarity")
        {
            // 同稀有度時優先尋寶卡（超過上限時不優先）
            var top = pool.Max(i => cards[i].Rarity);
            var best = pool.Where(i => cards[i].Rarity == top).ToList();
            var tb = capped ? -1 : best.FirstOrDefault(i => HasTreasure(cards[i]), -1);
            return new(PickDo.Pick, tb >= 0 ? tb : best[0], "稀有度優先");
        }
        List<int> tr = capped ? [] : [.. pool.Where(i => HasTreasure(cards[i]))];
        return tr.Count > 0
            ? new(PickDo.Pick, RarestIndex(cards, tr), "尋寶率優先")
            : new(PickDo.Pick, RarestIndex(cards, pool), "最稀有");
    }

    /// <summary>
    /// 尋寶卡：卡片文字有「尋寶」（同 JS hasTreasure），或卡名是已知的尋寶道具（尋寶藥水、驚奇秘藥、豐饒祝福；JS 註解列的）。
    /// 後者是 exe 新增：OCR 偶爾把「尋寶」二字讀錯，卡名比較大、比較好讀。
    /// </summary>
    public static bool HasTreasure(CardInfo c)
        => (c.Name + " " + c.Text).Contains("尋寶") || TreasureItems.Any(n => Squash(c.Name).Contains(n));

    /// <summary>提升生命的卡（同 JS hasHp）</summary>
    public static bool HasHp(CardInfo c) => HpWord().IsMatch(c.Name + " " + c.Text);

    /// <summary>已知會加尋寶率的道具卡名</summary>
    private static readonly string[] TreasureItems = ["尋寶藥水", "驚奇秘藥", "豐饒祝福"];

    /// <summary>
    /// 放逐清單的字詞：學到後放逐、直接放逐、一出現就放逐（勾選時）。放逐時記下是哪一個，全部放逐過後就能改按遊戲「自動」。
    /// </summary>
    public static IReadOnlyList<string> BanTargets(PickSettings s)
        => [.. s.Skills.After.Concat(s.Skills.Ban).Concat(s.BanExtra ? s.BanExtraWords : []).Where(w => w.Length > 0).Distinct()];

    /// <summary>
    /// 可以改按遊戲「自動」了：有勾這條規則、放逐清單不是空的（空的話一開局就交給遊戲，不是使用者要的）、清單裡每一項這局都放逐過。
    /// </summary>
    public static bool ReadyForGameAuto(PickSettings s, RunPickState st)
    {
        if (!s.AutoAfterBan)
            return false;
        var targets = BanTargets(s);
        return targets.Count > 0 && targets.All(st.Banished.Contains);
    }

    /// <summary>神祇卡（同 JS isDeity：卡片文字有 Deity／之神／神祇）</summary>
    public static bool IsDeity(CardInfo c) => DeityWord().IsMatch(c.Name + " " + c.Text);

    /// <summary>神祇名稱去掉精煉等級（「思想之神 +3」→「思想之神」），鎖定神祇時記這個</summary>
    public static string DeityName(string name) => PlusLevel().Replace(name, "").Trim();

    /// <summary>卡名裡有沒有清單裡的某個技能（同 JS skillOfCard：卡名可能是「雷爆術」或「攻擊：屬性附加」）</summary>
    public static string? SkillOf(CardInfo c, IReadOnlyList<string> list)
    {
        var name = OcrFix(c.Name);
        return list.FirstOrDefault(w => w.Length > 0 && name.Contains(OcrFix(w)));
    }

    /// <summary>
    /// OCR 常見的形近錯字換回正字再比對。實測（2026-11-21 三開 50 局戰績）：「冰箭術」「火箭術」讀成「冰箾術」「火箾術」，
    /// 對不上技能清單、被當成一般卡用「最稀有」選；「祝福」讀成「祝褔」「祝襵」。
    /// </summary>
    private static string OcrFix(string s) => s.Replace('箾', '箭').Replace('褔', '福').Replace('襵', '福');

    private static bool Usable(CardInfo c, ActKind kind) => c.Button(kind) is { Usable: true };

    private static bool ShouldBanish(CardInfo c, PickSettings s, RunPickState st)
    {
        if (IsDeity(c))
            return false;
        if (s.BanExtra && SkillOf(c, s.BanExtraWords) is not null)
            return true;
        if (SkillOf(c, s.Skills.Ban) is not null)
            return true;
        if (IsLearnedUnwanted(c, st))
            return true;
        // 有「新！」＝還沒學 → 要選；沒有＝已經學了 → 放逐
        var w = SkillOf(c, s.Skills.After);
        return w is not null && (!c.IsNew || st.Picked.Contains(w));
    }

    /// <summary>
    /// 這局已經學到的清單外技能又出現（升級卡）：要放逐，不升級。不算進放逐清單（<see cref="BanTargets"/>），所以不影響「放逐完後按自動」。
    /// 沒有「新！」才算（有「新！」的同名卡是另一張還沒學的，交給「不學清單外的新技能」）。
    /// </summary>
    private static bool IsLearnedUnwanted(CardInfo c, RunPickState st)
        => st.Unwanted.Count > 0 && !c.IsNew && !IsDeity(c) && st.Unwanted.Contains(SkillKey(c.Name));

    /// <summary>
    /// 放逐清單外技能前，看「保留放逐次數」：放逐清單（雷爆術、屬性附加…）還沒放逐完時，至少留 BanishKeepN 次給它們，
    /// 否則用光了就永遠按不到遊戲「自動」。清單都放逐過了就不必保留。
    /// </summary>
    private static bool KeepAllows(CardInfo c, PickSettings s, RunPickState st)
        => !s.BanishKeep || BanTargets(s).All(st.Banished.Contains) || c.Button(ActKind.Banish) is { } bb && bb.Left > s.BanishKeepN;

    /// <summary>記學到的清單外技能用的名字：去掉「+N」和空白，OCR 形近字換回正字</summary>
    public static string SkillKey(string name) => Squash(PlusLevel().Replace(name, ""));

    /// <summary>清單外的新技能（技能欄還沒滿時不想學的）</summary>
    public static bool IsUnwantedNew(CardInfo c, PickSettings s)
        => s.SkillOn && c.IsNew && !IsDeity(c) && c.SkillBadge && SkillOf(c, s.Skills.Order) is null;

    /// <summary>強制不選：技能「不選」、道具 ✕（神祇不算，神祇另有 IsBannedDeity）</summary>
    private static bool IsSkipped(CardInfo c, PickSettings s)
        => !IsDeity(c) && (SkillOf(c, s.Skills.Skip) is not null || s.ItemBan.Any(x => x.Length > 0 && Squash(c.Name).Contains(Squash(x))));

    private static bool IsBannedDeity(CardInfo c, PickSettings s) => IsDeity(c) && s.DeityBan.Any(n => SameDeity(c.Name, n));

    private static bool IsFavItem(CardInfo c, PickSettings s)
    {
        var n = Squash(c.Name);
        return n.Length > 0 && s.ItemPrio.Any(w => w.Length > 0 && n.Contains(Squash(w)));
    }

    /// <summary>神祇名稱比對（卡片可能寫「雷爆術 Deity」、道具欄寫「雷爆術 Deity +3」）</summary>
    private static bool SameDeity(string a, string b)
    {
        var (x, y) = (DeityKey(a), DeityKey(b));
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    private static string DeityKey(string n) => Spaces().Replace(DeityNoise().Replace(n, ""), "");

    /// <summary>同 JS rarestIndex：最稀有的；一樣稀有取前面的</summary>
    private static int RarestIndex(IReadOnlyList<CardInfo> cards, List<int> pool)
    {
        var top = pool.Max(i => cards[i].Rarity);
        return pool.First(i => cards[i].Rarity == top);
    }

    private static string Squash(string s) => Spaces().Replace(OcrFix(s), "");

    [GeneratedRegex("生命|HP|血量|治癒|回復|恢復", RegexOptions.IgnoreCase)]
    private static partial Regex HpWord();

    [GeneratedRegex("Deity|之神|神祇", RegexOptions.IgnoreCase)]
    private static partial Regex DeityWord();

    [GeneratedRegex(@"Deity|\+\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex DeityNoise();

    [GeneratedRegex(@"\s*\+\s*\d+.*$")]
    private static partial Regex PlusLevel();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
