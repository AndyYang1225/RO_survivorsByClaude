using System.Text.RegularExpressions;

namespace RoHelper.Vision.Picker;

/// <summary>
/// 選卡決策：engine.js decide()（約 448～559 行）的 C# 版，判斷順序和理由文字一行對一行。
/// 順序：1 神祇 → 2 放逐（升級卡）→ 3 技能專注（升級卡）→ 道具專注 → 可選的卡（排除放逐／強制不選／別的神祇）
/// → 4 技能優先（升級卡；稀有度 &gt; 順序）→ 5 不學清單外的新技能（升級卡）→ 6 尋寶率上限／活動 → 職業用不到的道具 → 勾選的道具 → 尋寶已滿→生命 → 選法（第一張／稀有度／尋寶率優先）。
/// 尋寶率（RunPickState.Treasure）由 VisionEngine 把滑鼠移到道具欄讀說明框得到（<see cref="Field.TreasureReader"/>）。
/// 跟 JS 不同：尋寶卡除了卡片文字有「尋寶」，卡名是已知的尋寶道具也算（<see cref="HasTreasure"/>），因為 OCR 偶爾把「尋寶」讀錯。
/// 純函式：不改 RunPickState（選了之後要記什麼由 PickerFlow 負責）。
/// </summary>
public static partial class CardDecider
{
    /// <param name="itemGroups">道具分類（schema 的 Items：藥水、秘藥…）；寶箱單張重抽用它判斷「同類有沒有要的」。null＝不知道分類</param>
    /// <param name="random">隨機函式（回 0～n−1）；null＝Random.Shared。測試傳固定值</param>
    /// <param name="rerollBudget">這個畫面還能重抽幾次（寶箱單張重抽與重抽全部合計；組隊時呼叫端限制）；≤ 0 不重抽</param>
    public static PickDecision Decide(PickerInfo p, PickSettings s, RunPickState st,
        IReadOnlyList<KeyValuePair<string, List<string>>>? itemGroups = null, Func<int, int>? random = null, int rerollBudget = int.MaxValue)
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

            // 3. 專注：只專注技能頁設成「優先＋專注」的，每個只專注一次。
            // 刻意不再「都沒設就專注前 N 個優先技能」（JS 的做法）：使用者 2026-11-25 回報沒設專注卻去專注第 1 優先的火箭術，要的是只專注指定的
            if (s.AutoFocus)
            {
                var targets = s.Skills.Focus.Where(w => s.Skills.Order.Contains(w)).ToList();
                var f = all.FirstOrDefault(i => SkillOf(cards[i], targets) is { } w && !st.Focused.Contains(w) && Usable(cards[i], ActKind.Focus), -1);
                if (f >= 0)
                {
                    var w = SkillOf(cards[f], targets)!;
                    return new(PickDo.Focus, f, "專注：" + w, w);
                }
            }
        }

        // 野外尋寶率已達上限：寶箱出現尋寶類的卡就放逐（使用者 2026-11-25）。道具欄大多是尋寶道具時，
        // 寶箱只會出它們的精煉，三張全是尋寶卡只能選一張；放逐後這局不再出現。保留放逐次數給放逐清單（同 KeepAllows）
        if (p.IsChest && !st.IsEvent && s.TreasureCap && st.Treasure >= s.TreasureCapN)
        {
            var t = all.FirstOrDefault(i => HasTreasure(cards[i]) && Usable(cards[i], ActKind.Banish) && KeepAllows(cards[i], s, st), -1);
            if (t >= 0)
                return new(PickDo.Banish, t, $"尋寶率已滿（{st.Treasure:0}%）：放逐尋寶道具");
        }

        // 道具專注：勾了 ★ 的道具出現時先按「專注」（升級卡、寶箱卡都適用；每個道具每局只專注一次）
        if (s.AutoFocus && s.ItemFocus.Count > 0)
        {
            string? ItemOf(CardInfo c) => s.ItemFocus.FirstOrDefault(x => x.Length > 0 && ItemIs(Squash(c.Name), x));
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

            // 4. 技能優先：清單裡的技能一起比，先比稀有度、同稀有度照順序（使用者 2026-11-27：冰箭、火箭、雷擊同時出現選稀有度高的；
            // 以前是第 1 優先只要出現就選）。只有一張清單裡的技能、而且是第 1 優先時，理由照舊寫「技能第 1 優先」
            var hits = pool.Where(i => Rank(i) >= 0).OrderByDescending(i => cards[i].Rarity).ThenBy(Rank).ToList();
            if (hits.Count == 1 && Rank(hits[0]) == 0)
                return new(PickDo.Pick, hits[0], "技能第 1 優先");
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

        // 寶箱卡單張重抽（使用者 2026-11-25）：沒有一張是要的 → 單張重抽「同類抽得出要的」那張（單張只會換成同一類，挑錯類永遠抽不到）；
        // 單張用完改按重抽全部；都用完才往下照規則選。升級卡不單張重抽（只會換成同一技能的另一種強化）
        if (p.IsChest && s.ChestReroll && rerollBudget > 0)
        {
            var treasureOff = s.TreasureCap && st.Treasure >= s.TreasureCapN || st.IsEvent;
            if (!pool.Any(i => IsWantedChest(cards[i], s, st, treasureOff)))
            {
                var rerollable = all.Where(i => Usable(cards[i], ActKind.Reroll)).ToList();
                if (rerollable.Count > 0)
                {
                    var hopeful = rerollable.Where(i => CanYieldWanted(cards[i], s, st, treasureOff, itemGroups ?? [])).ToList();
                    var from = hopeful.Count > 0 ? hopeful : rerollable;
                    var next = random ?? (n => Random.Shared.Next(n));
                    return new(PickDo.RerollCard, from[next(from.Count)], hopeful.Count > 0 ? "寶箱都不是要的：單張重抽（同類有要的）" : "寶箱都不是要的：單張重抽");
                }
                if (p.RerollAll is { Usable: true })
                    return new(PickDo.Reroll, null, "寶箱都不是要的：重抽全部");
            }
        }

        // 6. 道具／寶箱／其他。尋寶率超過上限、或這局是活動（尋寶率沒用）：還有別的卡時不選尋寶卡，也不優先
        var capped = s.TreasureCap && st.Treasure >= s.TreasureCapN;
        var noTreasure = capped || st.IsEvent;
        if (noTreasure)
        {
            // 活動：排名裡的道具（例如活動排第 1 的智慧神器，附帶尋寶率）照排名選，不因為帶尋寶率被濾掉。
            // 尋寶率已達上限時一律不選尋寶卡（上限優先於排名）
            var nt = pool.Where(i => !HasTreasure(cards[i]) || !capped && IsFavItem(cards[i], s)).ToList();
            if (nt.Count > 0)
                pool = nt;
        }
        // 職業用不到的道具（例如法師系的閃避類）：還有別的卡時不選。跟道具 ✕ 不同，全是這種卡時不會為了它重抽
        if (UselessFor(s.Job) is { Length: > 0 } useless)
        {
            var ok = pool.Where(i => !useless.Any(u => Squash(cards[i].Name).Contains(u))).ToList();
            if (ok.Count > 0)
                pool = ok;
        }
        if (s.ItemPrio.Count > 0)
        {
            var fav = pool.Where(i => IsFavItem(cards[i], s)).ToList();
            if (fav.Count > 0)
            {
                // 有設道具排名：照 ItemPrio 的順序（見 PickSettings.ForRun），各類的第 1 名先於第 2 名，同名次選比較稀有的。
                // 只有勾選（沒有先後）：照舊選勾選裡最稀有的
                if (!s.ItemPrioRanked)
                    return new(PickDo.Pick, RarestIndex(cards, fav), "勾選的道具");
                var best = fav.Min(i => PrioRank(cards[i], s));
                var top = fav.Where(i => PrioRank(cards[i], s) == best).ToList();
                return new(PickDo.Pick, RarestIndex(cards, top), "道具排名：" + s.ItemPrio[best]);
            }
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
            var tb = noTreasure ? -1 : best.FirstOrDefault(i => HasTreasure(cards[i]), -1);
            return new(PickDo.Pick, tb >= 0 ? tb : best[0], "稀有度優先");
        }
        List<int> tr = noTreasure ? [] : [.. pool.Where(i => HasTreasure(cards[i]))];
        return tr.Count > 0
            ? new(PickDo.Pick, RarestIndex(cards, tr), "尋寶率優先")
            : new(PickDo.Pick, RarestIndex(cards, pool), "最稀有");
    }

    /// <summary>
    /// 技能神祇選擇畫面（<see cref="PickerInfo.IsDeityPick"/>）的選法，使用者 2026-11-25 確認：
    /// 1. 「神祇」頁該職業的技能神祇優先順序（DeityList），跳過打 ✕ 的；2. 都沒有 → 「技能」頁優先順序；3. 還是沒有 → 第一張（沒打 ✕ 的）。
    /// 每局只選一次（之後神祇自己升級），所以不鎖定、不跟寶箱神器的鎖定（RunPickState.DeityLock）混在一起。
    /// 卡名是「火箭術 Deity」，OCR 常讀成「火箾術 De」：比對前去掉 Deity／De、套用 OcrFix。
    /// </summary>
    public static PickDecision DecideDeity(PickerInfo p, PickSettings s)
    {
        var cards = p.Cards;
        var all = Enumerable.Range(0, cards.Count).ToList();
        bool Is(int i, string w) => w.Length > 0 && DeitySkill(cards[i].Name).Contains(Squash(w));
        var ok = all.Where(i => !s.DeityBan.Any(b => Is(i, b))).ToList();
        if (ok.Count == 0)
            ok = all;
        foreach (var w in s.DeityList)
        {
            var i = ok.FirstOrDefault(i => Is(i, w), -1);
            if (i >= 0)
                return new(PickDo.Pick, i, "技能神祇優先：" + w);
        }
        foreach (var w in s.Skills.Order)
        {
            var i = ok.FirstOrDefault(i => Is(i, w), -1);
            if (i >= 0)
                return new(PickDo.Pick, i, "技能優先：" + w);
        }
        return new(PickDo.Pick, ok[0], "第一張");
    }

    /// <summary>技能神祇卡名去掉結尾的 Deity／De 和空白（「火箾術 De」→「火箭術」）</summary>
    private static string DeitySkill(string name) => Squash(DeitySuffix().Replace(name, ""));

    /// <summary>
    /// 尋寶卡：卡片文字有「尋寶」（同 JS hasTreasure），或卡名是已知的尋寶道具（尋寶藥水、驚奇秘藥、豐饒祝福；JS 註解列的）。
    /// 後者是 exe 新增：OCR 偶爾把「尋寶」二字讀錯，卡名比較大、比較好讀。
    /// </summary>
    public static bool HasTreasure(CardInfo c)
        => (c.Name + " " + c.Text).Contains("尋寶") || TreasureItems.Any(n => Squash(c.Name).Contains(n));

    /// <summary>提升生命的卡（同 JS hasHp）</summary>
    public static bool HasHp(CardInfo c) => HpWord().IsMatch(c.Name + " " + c.Text);

    /// <summary>
    /// 職業用不到的道具（卡名，去掉空白比對）。使用者（2026-11-25）：法師相關（魔法師、巫師、賢者）不點閃避類＝迴避秘藥。
    /// AGI 料理不算：圖書館員 Wiki 寫 AGI 每點 +0.67 冷卻縮減（另 +0.2% 迴避），對法師有用。
    /// </summary>
    private static readonly string[] MageJobs = ["魔法師", "巫師", "賢者"];
    private static readonly string[] DodgeItems = ["迴避秘藥"];

    internal static string[] UselessFor(string job) => MageJobs.Contains(job) ? DodgeItems : [];

    /// <summary>
    /// 選這張卡會增加多少尋寶率（%）：卡片文字的「N% 尋寶」加總。不是尋寶卡回 0；
    /// 是尋寶卡但數字讀不到（OCR 讀錯）時回 10（百科：尋寶道具每次最少 +10%），寧可估高一點也不要漏算
    /// </summary>
    public static double TreasureGain(CardInfo c)
    {
        if (!HasTreasure(c))
            return 0;
        var v = Field.TreasureReader.SumTreasure(c.Name + " " + c.Text);
        return v > 0 ? v : 10;
    }

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
    /// <summary>
    /// 這個選卡畫面可以按遊戲的「自動」了嗎（回理由；不行回 null）。使用者（2026-11-25）：
    /// 升級卡＝技能欄滿了（技能都選完），而且放逐清單都放逐過了 → 按「自動技能」；
    /// 寶箱卡＝道具欄六格都有了 → 按「自動寶箱」（不看放逐）。
    /// 欄位數讀不到時不按，下一個畫面再看。總開關是「放逐完後按遊戲自動」，寶箱另外要開「寶箱卡也按遊戲的自動」。
    /// </summary>
    public static string? GameAutoWhy(PickerInfo p, PickSettings s, RunPickState st)
    {
        if (!s.AutoAfterBan || p.SlotsFull != true)
            return null;
        var (used, max) = p.Slots!.Value;
        if (p.IsChest)
            return s.AutoChest ? $"道具欄滿了（{used}/{max}）" : null;
        // 有指定技能神祇（神祇清單裡有技能名，例如雷擊術）：Lv 20 選完神祇才按「自動技能」。
        // 實測（2026-11-25）：Lv 20 前就按了自動技能，遊戲到 Lv 20 自己挑了火箭術，小幫手沒機會選
        if (WantsSkillDeity(s) && !st.DeityPicked)
            return null;
        return ReadyForGameAuto(s, st) ? $"技能欄滿了（{used}/{max}），放逐清單都放逐過了：" + string.Join("、", BanTargets(s)) : null;
    }

    /// <summary>神祇清單裡有技能（不是「X神器」「X之神」）＝有指定技能神祇</summary>
    public static bool WantsSkillDeity(PickSettings s)
        => s.DeityList.Any(w => w.Length > 0 && !w.EndsWith("神器") && !w.EndsWith("之神"));

    public static bool ReadyForGameAuto(PickSettings s, RunPickState st)
    {
        if (!s.AutoAfterBan)
            return false;
        var targets = BanTargets(s);
        return targets.Count > 0 && targets.All(st.Banished.Contains);
    }

    /// <summary>神祇卡（同 JS isDeity：卡片文字有 Deity／之神／神祇；2026.11.23 版起改名「神器」）</summary>
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
    /// 遊戲 2026.11.23 版起神祇「X之神」改成「X神器」（分類從「神祇」變「神器」）：設定裡舊的「戰爭之神」等也換成「戰爭神器」，兩邊才對得上。
    private static string OcrFix(string s) => s.Replace('箾', '箭').Replace('褔', '福').Replace('襵', '福').Replace("之神", "神器");

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
        => !IsDeity(c) && (SkillOf(c, s.Skills.Skip) is not null || s.ItemBan.Any(x => x.Length > 0 && ItemIs(Squash(c.Name), x)));

    private static bool IsBannedDeity(CardInfo c, PickSettings s) => IsDeity(c) && s.DeityBan.Any(n => SameDeity(c.Name, n));

    private static bool IsFavItem(CardInfo c, PickSettings s)
    {
        var n = Squash(c.Name);
        return n.Length > 0 && s.ItemPrio.Any(w => w.Length > 0 && ItemIs(n, w));
    }

    /// <summary>
    /// 寶箱卡「要的」（使用者 2026-11-25）：勾選的道具、優先的神器（沒打 ✕；已鎖定時只有鎖定的那個）、尋寶率卡（還沒到上限、不是活動）、這局拿過的道具
    /// </summary>
    private static bool IsWantedChest(CardInfo c, PickSettings s, RunPickState st, bool treasureOff)
        => IsFavItem(c, s) || IsWantedArtifact(c, s, st) || !treasureOff && HasTreasure(c) || st.Owned.Contains(SkillKey(c.Name));

    private static bool IsWantedArtifact(CardInfo c, PickSettings s, RunPickState st)
    {
        if (!IsDeity(c) || IsBannedDeity(c, s))
            return false;
        if (s.DeityLock && st.DeityLock.Length > 0)
            return SameDeity(c.Name, st.DeityLock);
        return s.DeityPrio && s.DeityList.Any(w => w.Length > 0 && SameDeity(c.Name, w));
    }

    /// <summary>
    /// 這張卡單張重抽「有機會」抽到要的嗎：單張重抽只會換成同一類（Wiki：同一寶箱欄位的不同裝備），
    /// 所以看這張卡所屬的類別（schema 的道具分類；神器卡是神器類）裡有沒有要的東西。認不出類別回 false
    /// </summary>
    private static bool CanYieldWanted(CardInfo c, PickSettings s, RunPickState st, bool treasureOff, IReadOnlyList<KeyValuePair<string, List<string>>> groups)
    {
        if (IsDeity(c))
            return s.DeityLock && st.DeityLock.Length > 0
                || s.DeityPrio && s.DeityList.Any(w => OcrFix(w).Contains("神器") && !s.DeityBan.Any(b => SameDeity(w, b)))
                || st.Owned.Any(o => o.Contains("神器"));
        var name = Squash(c.Name);
        var names = groups.FirstOrDefault(g => g.Value.Any(n => n.Length > 0 && name.Contains(Squash(n)))).Value;
        if (names is null)
            return false;
        return names.Any(n => s.ItemPrio.Any(w => w.Length > 0 && ItemIs(Squash(n), w))
            || st.Owned.Contains(SkillKey(n))
            || !treasureOff && TreasureItems.Any(t => Squash(n).Contains(t)));
    }

    /// <summary>神祇名稱比對（卡片可能寫「雷爆術 Deity」、道具欄寫「雷爆術 Deity +3」）</summary>
    private static bool SameDeity(string a, string b)
    {
        var (x, y) = (DeityKey(a), DeityKey(b));
        return x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
    }

    private static string DeityKey(string n) => Spaces().Replace(DeityNoise().Replace(OcrFix(n), ""), "");

    /// <summary>同 JS rarestIndex：最稀有的；一樣稀有取前面的</summary>
    /// <summary>名字包含別的道具名的道具（「爆擊傷害秘藥」含「傷害秘藥」）：比對短的名字時要排除這些</summary>
    private static readonly string[] LongItemNames = ["爆擊傷害秘藥", "持續傷害秘藥", "雙重詠唱祝福"];

    /// <summary>
    /// 卡名（已 Squash）是不是道具 w。不能只看「包含」：實測（2026-11-25）排名「傷害秘藥」時，「爆擊傷害秘藥 + 1」被當成傷害秘藥選走
    /// </summary>
    private static bool ItemIs(string name, string w)
    {
        var x = Squash(w);
        return x.Length > 0 && name.Contains(x) && !LongItemNames.Any(l => l != x && l.Contains(x) && name.Contains(l));
    }

    /// <summary>卡片在 ItemPrio 裡的位置（第一個對得上的；對不上回 int.MaxValue）</summary>
    private static int PrioRank(CardInfo c, PickSettings s)
    {
        var n = Squash(c.Name);
        for (var k = 0; k < s.ItemPrio.Count; k++)
            if (s.ItemPrio[k].Length > 0 && ItemIs(n, s.ItemPrio[k]))
                return k;
        return int.MaxValue;
    }

    private static int RarestIndex(IReadOnlyList<CardInfo> cards, List<int> pool)
    {
        var top = pool.Max(i => cards[i].Rarity);
        return pool.First(i => cards[i].Rarity == top);
    }

    private static string Squash(string s) => Spaces().Replace(OcrFix(s), "");

    [GeneratedRegex("生命|HP|血量|治癒|回復|恢復", RegexOptions.IgnoreCase)]
    private static partial Regex HpWord();

    [GeneratedRegex("Deity|之神|神祇|神器", RegexOptions.IgnoreCase)]
    private static partial Regex DeityWord();

    [GeneratedRegex(@"Deity|\+\s*\d+", RegexOptions.IgnoreCase)]
    private static partial Regex DeityNoise();

    [GeneratedRegex(@"\s*\+\s*\d+.*$")]
    private static partial Regex PlusLevel();

    [GeneratedRegex(@"\s*De(ity)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DeitySuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
