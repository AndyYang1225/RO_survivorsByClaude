// RO Survivors 小幫手 — 自動化引擎（由 RoHelper.exe 在每次載入頁面時注入，源自使用者腳本 v7.5）
// @version 8.2.0
// @min-exe 8.2.0
//
// 上面兩行 exe 會讀（自動更新用），請保持格式：
// - @version：引擎版本。發佈新版到 Gist 前一定要調高，exe 只會換成「比較新」的版本。
// - @min-exe：需要的最低 exe 版本。改到 exe ↔ JS 的訊息格式時調高，舊 exe 就不會載入它（避免舊 exe 配新引擎壞掉）。
//
// 與使用者腳本版的差異：
// - 沒有網頁內的面板：設定、狀態、紀錄都透過 chrome.webview 訊息和 exe 的原生面板交換。
// - 設定不存 localStorage：exe 在頁面載入後送 init 訊息帶入，之後每次 save() 都把整份 cfg 回傳給 exe 存檔。
// - 注入時機是「文件建立時」（比 document-idle 早），攔截掉落物陣列不會漏掉一開始的東西；
//   但 DOM 還沒好，所以主迴圈要等收到 init 才開始，且 step() 會先確認 document.body 存在。
//
// 訊息格式（JS → exe）：hello{schema}、cfg{cfg}、status{status}、log{entry}、run{run}、togglePanel
// 訊息格式（exe → JS）：init{cfg}、set{patch}、replace{cfg}

(function () {
  'use strict';
  if (window.top !== window.self) return; // 只在最上層頁面跑（iframe 不注入）
  if (!/(^|\.)ro-survivors\.com$/.test(location.hostname)) return;
  if (window.__roHelper) return;
  window.__roHelper = true;
  const host = window.chrome && window.chrome.webview;
  if (!host) return;
  const post = (msg) => { try { host.postMessage(msg); } catch (e) {} };

  // ================= 讀遊戲裡的掉落物清單 =================
  // 遊戲每掉一個東西（寶石、寶箱、磁鐵、扭蛋、王的掉落物…）都會 push 進一個陣列，
  // 每個物件有 kind（種類）和 x, y（地圖上的精確座標）。這裡攔截 push 把那個陣列記下來。
  (function hookPickups() {
    const orig = Array.prototype.push;
    Array.prototype.push = function (...a) {
      const h = a[0];
      // 王的「鐘」：遊戲把它 push 進 3D 特效清單，mesh.position 的 x、z 就是鐘在地圖上的精確座標
      if (h && typeof h === 'object' && h.bellMat && h.mesh && h.mesh.position) window.__rhBell = { o: h, at: Date.now() };
      if (h && typeof h === 'object' && typeof h.kind === 'string' && typeof h.x === 'number' && 'view' in h) {
        const L = window.__rhPkLists || (window.__rhPkLists = []);
        if (!L.includes(this)) { L.push(this); if (L.length > 6) L.shift(); }
      }
      return orig.apply(this, a);
    };
  })();
  // 可能抓到好幾個陣列（每局一個新的）：選「還活著、而且還掛在畫面上」的東西最多的那個
  function pickupList() {
    let best = null, bn = 0;
    for (const L of window.__rhPkLists || []) {
      let n = 0;
      for (let i = 0; i < L.length && n < 50; i++) { const p = L[i]; if (p && !p.dead && p.view && p.view.parent) n++; }
      if (n > bn) { bn = n; best = L; }
    }
    return best;
  }

  // ================= 遊戲資料（圖書館員） =================
  const DEFAULT_STAGES = ['普隆德拉原野', '普隆德拉地下水道', '失事輪', '柏伊亞嵐島', '斐揚森林', '斐揚洞穴', '普隆德拉城'];

  // 各職業技能（一個技能對應一個神祇）。二轉職業也會帶一轉技能
  const JOB_SKILLS = {
    '劍士': ['狂擊', '怒爆', '劍舞', '盾牌衝擊', '挑釁', '降低士氣的吶喊', '霸體', '快速恢復'],
    '魔法師': ['火箭術', '冰箭術', '雷擊術', '聖靈召喚', '心靈爆破', '火焰之壁', '冰凍術', '火狩', '雷爆術'],
    '弓箭手': ['二連矢', '箭雨', '箭矢暴雨', '衝鋒箭', '撤退', '奧術射擊', '火焰箭', '心神凝聚'],
    '服事': ['治癒術', '神聖之光', 'Ruwach', '緩速術', 'Signum Crucis', 'Aqua Benedicta', '天使之賜福', '神聖重擊'],
    '盜賊': ['施毒', '噴砂', '隱匿', '投擲石頭', '毒藥瓶', '影步', '飛刀扇', '撒菱'],
    '商人': ['金錢攻擊', '手推車攻擊', '金幣雨', '手推車衝撞', '露天商店', '大聲吶喊', '負重量上升'],
    '騎士': ['騎乘攻擊', '衝鋒攻擊', '連刺攻擊', '投擲長矛攻擊', '反擊', '怪物互擊'],
    '十字軍': ['長矛加速術', '盾擊', '聖十字攻擊', '聖錘 十字星', '迴旋盾擊', '自動防禦'],
    '巫師': ['隕石術', '火柱攻擊', '霜凍之術', '暴風雪', '雷鳴術', '怒雷強擊'],
    '賢者': ['火元素領域', '水元素領域', '地震術', '崩裂術', '自動念咒', '隨機技能'],
    '獵人': ['閃電衝擊', '獵鷹尋敵', '爆散陷阱', '睡魔陷阱', '動物猛擊', '幻影箭'],
    '吟遊詩人/舞孃': ['奧義箭亂舞', '不諧和音', '搖籃曲', '洛奇的悲鳴', '戰鼓震天', '不死神齊格弗里德'],
    '祭司': ['光耀之堂', '暗之障壁', '聖母之祈福', '十字驅魔攻擊', '神威祈福', '聖母之頌歌'],
    '武僧': ['蓄氣', '彈指神通', '連環全身掌', '猛龍誇強', '爆氣', '阿修羅霸凰拳'],
    '刺客': ['音速投擲', '無影之牙', '塗毒', '病毒散撥', '偽裝', '毒刃'],
    '流氓': ['背刺', '潛擊', '卸除武器', '脅持', '偷錢', '塗鴉'],
    '鐵匠': ['旋風斧', '回力斧', '無視體型攻擊', '衝擊震地', '貪婪', '尋找礦石'],
    '鍊金術師': ['強酸攻擊', '火煙瓶投擲', '生命體召喚', '生物調撥', '藥水投擲', '化學保護'],
  };
  const JOB_BASE = { '騎士': '劍士', '十字軍': '劍士', '巫師': '魔法師', '賢者': '魔法師', '獵人': '弓箭手', '吟遊詩人/舞孃': '弓箭手',
    '祭司': '服事', '武僧': '服事', '刺客': '盜賊', '流氓': '盜賊', '鐵匠': '商人', '鍊金術師': '商人' };
  const skillsOf = (job) => (JOB_BASE[job] ? JOB_SKILLS[JOB_BASE[job]] : []).concat(JOB_SKILLS[job] || []);
  const DEITY_ATTR = ['貪婪之神', '戰爭之神', '處刑之神', '力量之神', '思想之神', '記憶之神', '堡壘之神', '疾風之神',
    '生命之神', '智慧之神', '束縛之神', '詭計之神', '折磨之神'];
  const ITEMS = {
    '藥水': ['回復藥水', '冷卻藥水', '生命藥水', '護甲藥水', '範圍藥水', '採集藥水', '貪婪藥水', '尋寶藥水', '速度藥水', '詠唱藥水', '傷害藥水'],
    '秘藥': ['爆擊秘藥', '傷害秘藥', '生命秘藥', '爆擊傷害秘藥', '驚奇秘藥', '持續傷害秘藥', '迴避秘藥', '詠唱秘藥', '範圍秘藥', '護甲秘藥'],
    '祝福': ['傷害祝福', '回復祝福', '冷卻祝福', '吸血祝福', '雙重詠唱祝福', '豐饒祝福', '詠唱祝福', '範圍祝福', '智慧祝福'],
    '料理': ['STR 料理', 'AGI 料理', 'VIT 料理', 'INT 料理', 'DEX 料理', 'LUK 料理'],
  };
  // 技能設定的四種狀態
  const SK_STATES = [['', '—'], ['prio', '優先'], ['focus', '優先＋專注'], ['after', '優先・學到後放逐'], ['ban', '直接放逐'], ['skip', '不選（不浪費放逐）']];

  // ================= 規則清單（面板會照這個產生） =================
  // tab：放在哪個分頁；excl：互斥；sub：縮排
  const RULES = [
    { tab: '升級', group: '選卡' },
    { k: 'autoUpgrade',  label: '有累積升級時自動按 B', def: true },
    { k: 'preMvp',       label: 'MVP 出現前 N 秒先把累積的升級選完', def: true, num: 15, min: 5, max: 60 },
    { k: 'pickSkill',    label: '升級卡自動選', def: true },
    { k: 'pickChest',    label: '寶箱卡自動選（會扣錢）', def: true },
    { k: 'pickOrder',    label: '其他卡的選法：', def: 'treasure',
      options: [['treasure', '尋寶率優先'], ['rarity', '稀有度優先'], ['first', '直接選第一張']] },
    { tab: '升級', group: '尋寶率' },
    { k: 'treasureCap',  label: '尋寶率累積超過 N % 就不再選尋寶卡', def: true, num: 300, min: 10, max: 5000 },
    { k: 'hpAfterCap',   label: '超過之後，優先選提升生命的卡', def: true, sub: true },
    { tab: '升級', group: '抽卡特權' },
    { k: 'rerollAll',    label: '只有不要的新技能時，按「重抽全部」', def: true },
    { k: 'avoidNew',     label: '技能欄還沒滿時，不學清單外的新技能', def: true },
    { k: 'banishKeep',   label: '避開新技能時可放逐，但至少留 N 次放逐', def: true, num: 2, min: 0, max: 4 },
    { k: 'autoFocus',    label: '自動用「專注」（技能分頁設成「優先＋專注」的技能、道具分頁打 ★ 的道具；都沒設就不專注）', def: true },
    { k: 'autoAfterBan', label: '技能欄滿了、而且「學到後放逐」和「一出現就放逐」的卡都放逐過後，改按遊戲的「自動技能」（這局之後交給遊戲選）', def: true },
    // 遊戲 2026.11.23 版起自動分成「自動技能」（升級卡）和寶箱卡的自動兩顆（只有視覺模式用）
    { k: 'autoChest',    label: '道具欄六格都有了以後，寶箱卡按遊戲的「自動寶箱」（不看放逐；關掉的話寶箱卡照下面的規則選：尋寶率、神祇、道具）', def: true, sub: true },

    { tab: '技能', group: '技能優先（依職業）' },
    { k: 'skillOn',      label: '使用下面的技能設定', def: true },
    { k: 'banExtra',     label: '其他一出現就放逐（普攻強化等）：', def: true, text: '屬性附加' },

    { tab: '神祇', group: '神祇優先（依職業）' },
    { k: 'deityPrio',    label: '使用下面的神祇順序', def: true },
    { k: 'deityOrder',   label: '神祇和其他卡比較：', def: 'none', sub: 2,
      options: [['none', '神祇不特別優先（跟其他卡一起比）'], ['rarity', '其他卡更稀有就先選其他卡'], ['first', '神祇永遠最優先']] },
    { k: 'deityLock',    label: '神祇選定後就不再更換（之後只選同一個神祇來精煉）', def: true },

    { tab: '走位', group: '野外走位' },
    { k: 'bellDodge',    label: '王喊「躲到鐘下」時自動跑到鐘下面', def: true },
    { k: 'bossSpot',     label: '任何王死掉時，跑到牠死掉的地點撿掉落物', def: true },
    { k: 'seekLoot',     label: '最優先去撿王的掉落物（裝備／卡片／蛋）', def: true },
    { k: 'meatLowHp',    label: 'HP 低於 % 時最優先去吃肉（回血）', def: true, num: 50, min: 10, max: 95 },
    { k: 'seekBall',     label: '去撿節慶扭蛋（白／橘／紅／綠球，所有顏色都會撿）', def: true },
    { k: 'colorBall',    label: '（備用）讀不到遊戲資料時，用畫面顏色找扭蛋（容易被技能特效誤判）', def: false, sub: true },
    { k: 'zoomOut',      label: '進到野外時，自動把鏡頭拉到最遠（看得更廣，等同滾輪往下）', def: true },
    { k: 'seekMagnet',   label: '優先走去撿磁鐵', def: true },
    { k: 'seekChest',    label: '走去撿寶箱', def: true },
    { k: 'chestMoney',   label: '金錢不夠開寶箱時就不去撿寶箱', def: true, sub: true },
    { k: 'autoWander',   label: '沒目標時繞圈走', def: true },
    { k: 'stayCenter',   label: '盡量待在地圖正中間（離太遠就走回來，避免卡在角落）', def: true, sub: true },
    { k: 'farLimit',     label: '離地圖中心超過一半距離的東西先不去撿', def: true, sub: true },
    { k: 'lowHpStop',    label: 'HP 低於 % 時停止自動走位', def: false, num: 30, min: 5, max: 95 },
    { tab: '走位', group: '結算' },
    { k: 'autoReturn',   label: '自動按「返回角色」', def: true },

    { tab: '出發', group: '大廳' },
    { k: 'soloLobby',    label: '出現「選擇大廳」視窗時自動按「單人大廳」', def: true },
    // 組隊（只有視覺模式用；JS 引擎不讀這兩個設定）
    { k: 'partyRole',    label: '組隊：', def: 'off',
      options: [['off', '不組隊（單人大廳）'], ['leader', '隊長（自動遠征、向隊伍提議出發）'], ['member', '隊員（等隊長提議、按準備好了）']] },
    { k: 'partyStay',    label: '組隊時原地站樁（不繞圈、不追寶箱；躲鐘和 MVP 死後撿東西照常）', def: true, sub: true },
    { k: 'partyLobby',   label: '組隊的大廳：', def: '約頓海姆', sub: 2,
      options: [['阿斯嘉特', '阿斯嘉特'], ['米德加爾特', '米德加爾特'], ['亞爾夫海姆', '亞爾夫海姆'], ['華納海姆', '華納海姆'], ['約頓海姆', '約頓海姆'], ['尼達維勒', '尼達維勒']] },
    { tab: '出發', group: '燈籠節活動' },
    { k: 'autoEvent',    label: '自動參加活動（回城→節慶主持人→開始遊戲→前往野外）', def: true, excl: 'autoExpe' },
    { tab: '出發', group: '遠征' },
    { k: 'autoExpe',     label: '自動遠征（總開關）', def: false, excl: 'autoEvent' },
    { k: 'expeTalk',     label: '① 走到遠征隊長並交談', def: true, sub: true },
    { k: 'expePick',     label: '② 選地圖', def: true, sub: true },
    { k: 'expeStage',    label: '地圖：', def: '斐揚洞穴', options: 'stages', sub: 2 },
    { k: 'expeGo',       label: '③ 按「出發」', def: true, sub: true },
    { k: 'expeDiff',     label: '難度：', def: 'keep', sub: 2,
      options: [['keep', '維持遊戲設定'], ['max', '全部拉滿'], ['min', '全部最低']] },
    { k: 'expeEnter',    label: '④ 按「前往野外」進入關卡', def: true, sub: true },
    { tab: '出發', group: '局數' },
    { k: 'runLimit',     label: '跑滿 N 局後停止自動開始', def: false, num: 10, min: 1, max: 999 },

    // 「遊戲視窗打開時自動收起面板」已拿掉：面板現在是獨立視窗，不會擋到遊戲
    // 時間設定：nocheck＝只有數字、沒有勾選框（一定生效）
    { tab: '其他', group: '時間（毫秒，1000＝1 秒）' },
    { k: 'tTick',   label: '主迴圈每 N 毫秒判斷一次', nocheck: true, num: 250, min: 100, max: 2000 },
    { k: 'tPick',   label: '選卡畫面出現後，等 N 毫秒再選', nocheck: true, num: 350, min: 0, max: 10000 },
    { k: 'tStart',  label: '按「開始遊戲／出發／前往野外」前等 N 毫秒', nocheck: true, num: 800, min: 0, max: 30000 },
    { k: 'tReturn', label: '結算畫面出現後，等 N 毫秒再按「返回角色」', nocheck: true, num: 1500, min: 0, max: 60000 },
    { k: 'tSeek',   label: '撿東西時，每 N 毫秒下一次移動指令', nocheck: true, num: 500, min: 100, max: 5000 },
    { k: 'tWander', label: '沒目標繞圈時，每 N 毫秒換一次方向', nocheck: true, num: 1400, min: 300, max: 10000 },
    { k: 'tLobby',  label: '城裡走向 NPC 時，每 N 毫秒點一次', nocheck: true, num: 700, min: 200, max: 5000 },
    { tab: '其他', group: '除錯' },
    { k: 'debugLog',     label: '記錄每次選卡（看到什麼、選了什麼、為什麼）', def: false },
  ];
  // 各地圖頭目出現時間（遠征視窗），秒
  const STAGE_BOSS = {
    '普隆德拉原野': [['Eclipse', 'Mini', 150], ['Toad', 'Mini', 300], ['Dragon Fly', 'Mini', 450], ['Vocal', 'Mini', 600], ['Mastering', 'MVP', 720]],
    '普隆德拉地下水道': [['Tarou ×3', 'Mini', 180], ['Golden Thief Bug', 'MVP', 480]],
    '失事輪': [['Ghostring', 'Mini', 180], ['Penomena', 'Mini', 360], ['Drake', 'MVP', 600]],
    '柏伊亞嵐島': [['Deviace', 'Mini', 180], ['Strouf', 'Mini', 360], ['King Dramoh', 'Mini', 540], ['Kraken', 'MVP', 720]],
    '斐揚森林': [['Angeling', 'Mini', 180], ['Deviling', 'Mini', 360], ['Vagabond Wolf', 'Mini', 600], ['Eddga', 'MVP', 840]],
    '斐揚洞穴': [['Skeleton General', 'Mini', 180], ["Cat O' Nine Tails", 'Mini', 360], ['Hyegun', 'Mini', 600], ['Moonlight Flower', 'MVP', 840]],
    '普隆德拉城': [['High Orc', 'MVP', 90]],
  };
  const TABS = ['升級', '技能', '道具', '神祇', '走位', '出發', '其他'];

  // 各種等待時間（毫秒）：v8.1 起改成面板「其他 → 時間」可自訂的固定值，預設值同原腳本。
  // 用 getter 讀 cfg，下面用到 T.xxx 的地方都不用改；改了馬上生效（主迴圈間隔另外在 set 訊息裡重設計時器）
  const T = {
    get tick() { return cfg.tTickN; },
    get pickDelay() { return cfg.tPickN; },
    get returnDelay() { return cfg.tReturnN; },
    get startDelay() { return cfg.tStartN; },
    get seekEvery() { return cfg.tSeekN; },
    get wanderEvery() { return cfg.tWanderN; },
    get lobbyMoveEvery() { return cfg.tLobbyN; },
  };
  const GEO = { wanderRadius: 0.32, stepRadius: 0.30 };
  // 小地圖顏色（照遊戲程式碼）：磁鐵 #ff3b4a、自己 #c0182a、寶箱 #ffc83c、
  // 王的掉落物（裝備/卡片/蛋）＝外框 #1d2d57 的菱形（磁鐵外框也是這個色，要排除）
  const COL = { magnet: [255, 59, 74], player: [192, 26, 43], chest: [255, 200, 60], loot: [31, 50, 92],
    mvp: [255, 42, 42], mini: [255, 154, 46] }; // 王的標記：MVP 紅圈、小頭目橘圈（會閃）

  // ================= 設定 =================
  const MAGE = { order: ['雷爆術', '冰箭術', '雷擊術', '火箭術'], after: ['雷爆術'], ban: [] };
  const MAGE_DEITY = ['雷爆術', '冰箭術', '雷擊術', '火箭術', '思想之神', '束縛之神', '戰爭之神'];
  const cfg = {
    paused: false, collapsed: false, tab: '升級', runs: 0, expeList: DEFAULT_STAGES.slice(),
    jobSel: 'auto', curJob: '', itemPrio: [],
    skills: { '魔法師': JSON.parse(JSON.stringify(MAGE)), '巫師': JSON.parse(JSON.stringify(MAGE)) },
    deity: { '魔法師': MAGE_DEITY.slice(), '巫師': MAGE_DEITY.slice() },
  };
  RULES.forEach((r) => { if (r.k) { if (!r.nocheck) cfg[r.k] = r.def; if (r.num != null) cfg[r.k + 'N'] = r.num; if (r.text != null) cfg[r.k + 'T'] = r.text; } });
  const DEFAULT_CFG = JSON.stringify(cfg);
  // 套用 exe 存的設定（或使用者匯入的舊版設定）。reset＝先清回預設值再套（匯入時用，避免殘留舊鍵）
  function applyStored(o, reset) {
    if (reset) { for (const k of Object.keys(cfg)) delete cfg[k]; Object.assign(cfg, JSON.parse(DEFAULT_CFG)); }
    if (o && typeof o === 'object') Object.assign(cfg, o);
    if (cfg.deityJob && cfg.jobSel === 'auto' && cfg.deityJob !== 'auto') cfg.jobSel = cfg.deityJob; // 舊版設定
    if (cfg.autoEvent && cfg.autoExpe) cfg.autoEvent = false;
    if (cfg.deityOrderV !== 2) { cfg.deityOrder = 'none'; cfg.deityOrderV = 2; } // v7.4：預設改成神祇不特別優先
    if (!TABS.includes(cfg.tab)) cfg.tab = '升級';
    clampTimes();
  }
  // 時間設定一定要是範圍內的數字：不是數字（匯入壞掉的設定）時 T.xxx 會變 undefined，比較全部變 false，自動化會整個卡住
  function clampTimes() {
    for (const r of RULES) {
      if (!r.nocheck) continue;
      const v = +cfg[r.k + 'N'];
      cfg[r.k + 'N'] = Number.isFinite(v) ? Math.min(r.max, Math.max(r.min, Math.round(v))) : r.num;
    }
  }
  // 設定的唯一擁有者是這裡（JS）；exe 只是鏡像＋存檔，所以每次改動都把整份送回去
  const save = () => post({ type: 'cfg', cfg });

  const st = {
    angle: 0, pickerSeenAt: 0, returnSeenAt: 0, startSeenAt: 0, lastMove: 0, lastLobbyMove: 0,
    npcDone: false, npcTalked: false, festAt: 0, stageAt: 0, stagesSeenAt: 0, diffDone: false,
    picked: new Set(), focused: new Set(), treasure: 0, got: { loot: 0, ball: 0, chest: 0 }, seenP: new WeakSet(), doneP: new WeakSet(), blocked: [], escUntil: 0, stuckN: 0, anchor: null, wasInRun: false, action: '',
  };

  // ================= 工具 =================
  const $ = (s) => document.querySelector(s);
  const visible = (el) => !!el && !el.classList.contains('hidden') && el.getClientRects().length > 0;
  const on = (k) => !cfg.paused && cfg[k];
  const ex = (k) => on('autoExpe') && cfg[k];
  const txt = (e) => ((e && e.textContent) || '').trim().replace(/\s+/g, ' ');

  function pressKey(key) {
    const code = /^\d$/.test(key) ? 'Digit' + key : 'Key' + key.toUpperCase();
    const kc = key.toUpperCase().charCodeAt(0);
    for (const type of ['keydown', 'keyup'])
      document.dispatchEvent(new KeyboardEvent(type, { key, code, keyCode: kc, which: kc, bubbles: true, cancelable: true }));
  }
  function clickAt(x, y) {
    const t = document.elementFromPoint(x, y);
    if (!t || t.tagName !== 'CANVAS') return false;
    const b = { clientX: x, clientY: y, bubbles: true, cancelable: true, button: 0, pointerId: 1, pointerType: 'mouse', isPrimary: true, view: window };
    t.dispatchEvent(new PointerEvent('pointerdown', { ...b, buttons: 1 }));
    t.dispatchEvent(new MouseEvent('mousedown', { ...b, buttons: 1 }));
    t.dispatchEvent(new PointerEvent('pointerup', { ...b, buttons: 0 }));
    t.dispatchEvent(new MouseEvent('mouseup', { ...b, buttons: 0 }));
    t.dispatchEvent(new MouseEvent('click', { ...b, buttons: 0 }));
    return true;
  }
  function clickWorld(fx, fy) {
    const cv = $('canvas.world3d');
    if (!cv) return;
    const r = cv.getBoundingClientRect();
    fx = Math.min(0.95, Math.max(0.05, fx));
    fy = Math.min(0.95, Math.max(0.05, fy));
    clickAt(r.left + r.width * fx, r.top + r.height * fy);
  }
  function hpPct() {
    const m = txt($('#hp-text')).match(/(\d+)\s*\/\s*(\d+)/);
    return m ? (100 * +m[1]) / Math.max(1, +m[2]) : 100;
  }

  // ---- 小地圖 ----
  const near = (d, i, c, tol) => Math.abs(d[i] - c[0]) <= tol && Math.abs(d[i + 1] - c[1]) <= tol && Math.abs(d[i + 2] - c[2]) <= tol;
  function clusters(pts, minSize) {
    const gs = [];
    for (const p of pts) {
      let g = gs.find((g) => Math.abs(g.cx - p[0]) <= 4 && Math.abs(g.cy - p[1]) <= 4);
      if (!g) { g = { sx: 0, sy: 0, n: 0, cx: p[0], cy: p[1] }; gs.push(g); }
      g.sx += p[0]; g.sy += p[1]; g.n++; g.cx = g.sx / g.n; g.cy = g.sy / g.n;
    }
    return gs.filter((g) => g.n >= minSize);
  }
  function readMinimap() {
    const cv = $('#minimap canvas');
    if (!cv || !cv.width) return null;
    let d;
    try { d = cv.getContext('2d').getImageData(0, 0, cv.width, cv.height).data; } catch (e) { return null; }
    const W = cv.width, H = cv.height, P = [], M = [], C = [], L = [], B = [];
    let bx0 = W, bx1 = 0, by0 = H, by1 = 0; // 地圖本體的範圍（小地圖外圍是黑色）
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const i = (y * W + x) * 4;
      if (d[i] + d[i + 1] + d[i + 2] > 45) { if (x < bx0) bx0 = x; if (x > bx1) bx1 = x; if (y < by0) by0 = y; if (y > by1) by1 = y; }
      if (near(d, i, COL.player, 10)) P.push([x, y]);
      else if (near(d, i, COL.magnet, 12)) M.push([x, y]);
      else if (near(d, i, COL.chest, 12)) C.push([x, y]);
      else if (near(d, i, COL.loot, 14)) L.push([x, y, 1]);
      else if (near(d, i, COL.mvp, 14) || near(d, i, COL.mini, 14)) B.push([x, y]);
    }
    const me = clusters(P, 3).sort((a, b) => b.n - a.n)[0];
    const magnets = clusters(M, 4);
    // 掉落物：品質色（白／綠／藍／紫／橘金）填滿、深藍外框的菱形。填色會跟地形混在一起，
    // 所以只認深藍外框：一圈外框至少 4 點，而且不在磁鐵旁（磁鐵也有深藍外框）
    const lg = [];
    for (const p of L) {
      let g = lg.find((g) => Math.abs(g.cx - p[0]) <= 6 && Math.abs(g.cy - p[1]) <= 6);
      if (!g) { g = { sx: 0, sy: 0, n: 0, nv: 0, cx: p[0], cy: p[1] }; lg.push(g); }
      g.sx += p[0]; g.sy += p[1]; g.n++; g.nv += p[2]; g.cx = g.sx / g.n; g.cy = g.sy / g.n;
    }
    const loot = lg.filter((g) => g.nv >= 4 && !magnets.some((m) => Math.hypot(m.cx - g.cx, m.cy - g.cy) < 7));
    const m = txt($('#minimap-coords')).match(/(\d+)\s*,\s*(\d+)/);
    const map = bx1 > bx0 ? { cx: (bx0 + bx1) / 2, cy: (by0 + by1) / 2, w: bx1 - bx0, h: by1 - by0 } : null;
    return me ? { me, magnets, chests: clusters(C, 3), loot, bosses: clusters(B, 4), W, H, map,
      tile: m ? { x: +m[1], y: +m[2] } : null } : null;
  }
  const nearest = (me, list) => list.reduce((best, t) => {
    const d = Math.hypot(t.cx - me.cx, t.cy - me.cy);
    return !best || d < best.d ? { t, d } : best;
  }, null);

  // ================= 職業 =================
  function detectJob() {
    const b = $('.slot.selected .slot-job b');
    let j = b && b.textContent.trim();
    if (j && /吟遊詩人|舞孃/.test(j)) j = '吟遊詩人/舞孃';
    if (j && JOB_SKILLS[j] && j !== cfg.curJob) { cfg.curJob = j; save(); }
  }
  const curJob = () => (cfg.jobSel === 'auto' ? cfg.curJob : cfg.jobSel);
  const skillCfg = () => {
    const j = curJob();
    if (!j) return { order: [], after: [], ban: [] };
    if (!cfg.skills[j]) cfg.skills[j] = { order: [], after: [], ban: [] };
    return cfg.skills[j];
  };

  // ================= 卡片判讀 =================
  const RARITY = ['普通', '優良', '稀有', '史詩', '傳說', '神話'];
  const cardName = (c) => txt(c.querySelector('.pk-name'));
  const badgeOf = (c) => txt(c.querySelector('.pk-badge'));
  const isNewCard = (c) => /新/.test(txt(c.querySelector('.pk-ribbon')));
  const isDeity = (c) => /Deity|之神|神祇/i.test(c.textContent);
  const hasTreasure = (c) => /尋寶/.test(c.textContent);
  const hasHp = (c) => /生命|HP|血量|治癒|回復|恢復/i.test(c.textContent);
  function rarityOf(card) {
    const i = RARITY.findIndex((r) => badgeOf(card).includes(r));
    if (i >= 0) return i;
    const m = (card.className + ' ' + (card.parentElement || {}).className).match(/\br(\d)\b/);
    return m ? +m[1] - 1 : 0;
  }
  function rarestIndex(cards, pool) {
    let best = pool[0];
    pool.forEach((i) => { if (rarityOf(cards[i]) > rarityOf(cards[best])) best = i; });
    return best;
  }
  const treasureOf = (c) => {
    let sum = 0;
    for (const m of txt(c.querySelector('.pk-lines') || c).matchAll(/([\d.]+)\s*%\s*尋寶/g)) sum += +m[1];
    return sum;
  };
  const treasureCapped = () => on('treasureCap') && st.treasure >= cfg.treasureCapN;
  // 尋寶率：直接讀遊戲的數字。把滑鼠「假裝」移到下方每個道具圖示上，讀說明框的「+N% 尋寶率」加總
  //（尋寶藥水、驚奇秘藥、豐饒祝福…），讀完馬上移開。不再自己用卡片數字加總（精煉卡會重複算）。
  const countPick = () => {};
  function readTreasure() {
    const tip = $('#skill-tip'), slots = document.querySelectorAll('#runitems .run-slot[data-item]');
    if (!tip || !slots.length) return null;
    let sum = 0;
    const items = [];
    for (const sl of slots) {
      const r = sl.getBoundingClientRect(), o = { bubbles: true, clientX: r.x + r.width / 2, clientY: r.y + r.height / 2, view: window };
      sl.dispatchEvent(new MouseEvent('mouseover', o));
      const t = txt(tip.querySelector('.st-hist') || tip);
      const head = txt(tip.querySelector('.st-head b') || tip.querySelector('.st-head'));
      if (head) items.push(head); // 對局紀錄用：目前持有的道具（含等級，例如「尋寶藥水 +3」）
      for (const m of t.matchAll(/([\d.]+)\s*%\s*尋寶/g)) sum += +m[1];
      // 順便讀目前的神祇（道具欄的 deity 格，例如「堡壘之神 +15」）
      if (sl.dataset.item === 'deity') {
        const h = txt(tip.querySelector('.st-head b') || tip.querySelector('.st-head') || tip).replace(/\s*\+\s*\d+.*$/, '');
        if (h) st.deityLock = h;
      }
      for (const ty of ['mouseout', 'mouseleave']) sl.dispatchEvent(new MouseEvent(ty, o));
      for (const ty of ['pointerout', 'pointerleave']) sl.dispatchEvent(new PointerEvent(ty, o));
    }
    tip.classList.add('hidden');
    st.treasure = sum; st.treasureAt = Date.now(); st.items = items;
    return sum;
  }
  // 強制不選的技能（技能分頁「不選」）和道具（道具分頁 ✕）
  const isSkipped = (c) => {
    if (isDeity(c)) return false;
    if (skillOfCard(c, sk().skip || [])) return true;
    const nm = cardName(c).replace(/\s+/g, '');
    return (cfg.itemBan || []).some((x) => nm.includes(x.replace(/\s+/g, '')));
  };
  // 強制不選的神祇（依職業）
  const isBannedDeity = (c) => isDeity(c) && ((cfg.deityBan || {})[curJob()] || []).some((n) => sameDeity(cardName(c), n));
  // 神祇名稱比對（卡片可能寫「雷爆術 Deity」、道具欄寫「雷爆術 Deity +3」）
  const deityKey = (n) => (n || '').replace(/Deity|\+\s*\d+/gi, '').replace(/\s+/g, '');
  const sameDeity = (a, b) => { const x = deityKey(a), y = deityKey(b); return !!x && !!y && (x.includes(y) || y.includes(x)); };
  const isFavItem = (c) => {
    const n = cardName(c).replace(/\s+/g, '');
    return !!n && cfg.itemPrio.some((w) => n.includes(w.replace(/\s+/g, '')));
  };
  const words = (k) => (cfg[k + 'T'] || '').split(/[,，、;；\s]+/).filter(Boolean);
  // 卡片名稱裡有沒有某個技能（名稱可能是「雷爆術」或「攻擊：屬性附加」）
  const skillOfCard = (c, list) => { const n = cardName(c); return list.find((w) => n.includes(w)); };

  // 卡片上的動作按鈕（重抽 / 專注 / 放逐），還有剩幾次
  function actBtn(card, re) {
    const slot = card.closest('.pk-slot') || card.parentElement;
    return [...slot.querySelectorAll('.pk-act')].find((a) => re.test(a.textContent + ' ' + a.title));
  }
  const actLeft = (b) => { const m = txt(b && b.querySelector('small')).match(/\d+/); return m ? +m[0] : b && !b.disabled ? 99 : 0; };
  const usable = (b) => !!b && !b.disabled && actLeft(b) > 0;
  const barBtn = (re) => [...document.querySelectorAll('#pk-bar .pk-act')].find((a) => re.test(a.textContent));

  // ---- 技能規則 ----
  const sk = () => (on('skillOn') ? skillCfg() : { order: [], after: [], ban: [] });
  function shouldBanish(c) {
    if (isDeity(c)) return false;
    if (on('banExtra') && skillOfCard(c, words('banExtra'))) return true;
    const s = sk();
    if (skillOfCard(c, s.ban)) return true;
    const w = skillOfCard(c, s.after);
    // 有「新！」= 還沒學 → 要選；沒有 = 已經學了 → 放逐
    return !!w && (!isNewCard(c) || st.picked.has(w));
  }
  // 清單外的新技能（技能欄還沒滿時不想學的）
  const isUnwantedNew = (c) => on('skillOn') && isNewCard(c) && !isDeity(c) && /技能/.test(badgeOf(c)) && !skillOfCard(c, sk().order);

  // ================= 除錯紀錄 =================
  // 紀錄由 exe 保存（最多 300 筆、寫進 picklog.json），這裡只負責送出
  function addLog(entry) {
    if (!cfg.debugLog) return;
    post({ type: 'log', entry });
  }
  function logPick(cards, act, why) {
    addLog({
      t: new Date().toLocaleTimeString(), job: curJob(), title: txt($('#pk-title')), treasure: Math.round(st.treasure),
      cards: cards.map((c) => [cardName(c), badgeOf(c), txt(c.querySelector('.pk-ribbon')), txt(c.querySelector('.pk-sub')),
        txt(c.querySelector('.pk-lines')).slice(0, 60)].filter(Boolean).join(' | ')),
      act, why,
    });
  }

  // ================= 選卡決策 =================
  // 回傳 { do: 'pick'|'banish'|'reroll'|'focus', i, why }
  function decide(cards, isChest) {
    const all = cards.map((_, i) => i);

    // 1. 神祇（寶箱第五欄）
    // 「稀有度比較」模式：同一次選卡裡有別張卡比神祇卡更稀有（例如金色藥水 vs 紫色神祇）→ 這次不選神祇
    let deityPick = null;
    if (on('deityLock') && st.deityLock) {
      const same = all.find((i) => isDeity(cards[i]) && sameDeity(cardName(cards[i]), st.deityLock));
      if (same != null) deityPick = { do: 'pick', i: same, why: '神祇精煉：' + st.deityLock };
    } else if (on('deityPrio')) {
      const list = cfg.deity[curJob()] || [];
      for (const w of list) {
        const i = all.find((i) => isDeity(cards[i]) && cardName(cards[i]).includes(w));
        if (i != null) { deityPick = { do: 'pick', i, why: '神祇優先：' + w }; break; }
      }
    }
    let skipDeity = false;
    if (deityPick && cfg.deityOrder === 'none') deityPick = null; // 不特別優先：交給後面的一般規則（道具勾選、尋寶、稀有度）
    if (deityPick) {
      const better = cfg.deityOrder === 'rarity' && all.some((i) => !isDeity(cards[i]) && !isSkipped(cards[i]) &&
        !shouldBanish(cards[i]) && rarityOf(cards[i]) > rarityOf(cards[deityPick.i]));
      if (!better) return deityPick;
      skipDeity = true; // 讓後面的規則從其他卡裡挑
    }

    if (!isChest) {
      // 2. 放逐（一次一張）
      const b = all.find((i) => shouldBanish(cards[i]) && usable(actBtn(cards[i], /放逐|Banish/i)));
      if (b != null) return { do: 'banish', i: b, why: '放逐清單' };

      // 3. 專注：前 N 個「優先」技能（不含學到後放逐的），每個只專注一次
      if (on('autoFocus')) {
        const s = sk();
        const fl = (s.focus || []).filter((w) => s.order.includes(w));
        const targets = fl.length ? fl : s.order.filter((w) => !s.after.includes(w)).slice(0, cfg.autoFocusN);
        const f = all.find((i) => { const w = skillOfCard(cards[i], targets); return w && !st.focused.has(w) && usable(actBtn(cards[i], /專注|Focus/i)); });
        if (f != null) return { do: 'focus', i: f, why: '專注：' + skillOfCard(cards[f], targets) };
      }
    }

    // 道具專注：勾了 ★ 的道具出現時先按「專注」（升級卡、寶箱卡都適用；每個道具每局只專注一次）
    if (on('autoFocus') && (cfg.itemFocus || []).length) {
      const nm = (c) => cardName(c).replace(/\s+/g, '');
      const f = all.find((i) => {
        const w = cfg.itemFocus.find((x) => nm(cards[i]).includes(x.replace(/\s+/g, '')));
        return w && !st.focused.has('item:' + w) && usable(actBtn(cards[i], /專注|Focus/i));
      });
      if (f != null) {
        const w = cfg.itemFocus.find((x) => nm(cards[f]).includes(x.replace(/\s+/g, '')));
        return { do: 'focus', i: f, item: w, why: '道具專注：' + w };
      }
    }

    // 可以考慮的卡：不含要放逐的；已鎖定神祇時，不同的神祇也不考慮
    // 強制不選：神祇 ✕、技能「不選」、道具 ✕ → 都不考慮
    let pool = all.filter((i) => !shouldBanish(cards[i]) && !isBannedDeity(cards[i]) && !isSkipped(cards[i]) && !(skipDeity && isDeity(cards[i])) &&
      !(on('deityLock') && st.deityLock && isDeity(cards[i]) && !sameDeity(cardName(cards[i]), st.deityLock)));
    if (!pool.length) {
      // 三張全是不要的 → 能重抽就重抽；不能才只好挑一張（遊戲規定一定要選）
      const rr = barBtn(/重抽全部/);
      if (usable(rr)) return { do: 'reroll', why: '全部都是強制不選的卡' };
      pool = all.filter((i) => !isBannedDeity(cards[i]) && !isSkipped(cards[i]));
      if (!pool.length) pool = all;
    }

    if (!isChest) {
      const order = sk().order;
      const rank = (i) => order.findIndex((w) => cardName(cards[i]).includes(w));
      // 4. 技能優先：第一個有出現就選；其餘的看稀有度，同稀有度照順序
      const first = pool.find((i) => rank(i) === 0);
      if (first != null) return { do: 'pick', i: first, why: '技能第 1 優先' };
      const hits = pool.filter((i) => rank(i) > 0);
      if (hits.length) {
        hits.sort((x, y) => rarityOf(cards[y]) - rarityOf(cards[x]) || rank(x) - rank(y));
        return { do: 'pick', i: hits[0], why: '技能優先（稀有度 > 順序）' };
      }
      // 5. 不學清單外的新技能
      const bad = pool.filter((i) => isUnwantedNew(cards[i]));
      if (on('avoidNew') && bad.length) {
        const good = pool.filter((i) => !isUnwantedNew(cards[i]));
        if (good.length) pool = good;
        else {
          const rr = barBtn(/重抽全部/);
          if (on('rerollAll') && usable(rr)) return { do: 'reroll', why: '只有不要的新技能' };
          const bi = bad.find((i) => { const bb = actBtn(cards[i], /放逐|Banish/i); return usable(bb) && actLeft(bb) > (on('banishKeep') ? cfg.banishKeepN : 0); });
          if (bi != null) return { do: 'banish', i: bi, why: '不要的新技能（重抽用完）' };
        }
      } else if (bad.length === pool.length && on('rerollAll') && usable(barBtn(/重抽全部/))) {
        return { do: 'reroll', why: '只有不要的新技能' };
      }
    }

    // 6. 道具 / 寶箱 / 其他
    const capped = treasureCapped();
    if (capped) { const nt = pool.filter((i) => !hasTreasure(cards[i])); if (nt.length) pool = nt; }
    if (cfg.itemPrio.length) {
      const fav = pool.filter((i) => isFavItem(cards[i]));
      if (fav.length) return { do: 'pick', i: rarestIndex(cards, fav), why: '勾選的道具' };
    }
    if (capped && on('hpAfterCap')) {
      const hp = pool.filter((i) => hasHp(cards[i]));
      if (hp.length) return { do: 'pick', i: rarestIndex(cards, hp), why: '尋寶已滿→生命' };
    }
    if (cfg.pickOrder === 'first') return { do: 'pick', i: pool[0], why: '第一張' };
    if (cfg.pickOrder === 'rarity') {
      const top = Math.max(...pool.map((i) => rarityOf(cards[i])));
      const best = pool.filter((i) => rarityOf(cards[i]) === top);
      return { do: 'pick', i: (capped ? null : best.find((i) => hasTreasure(cards[i]))) ?? best[0], why: '稀有度優先' };
    }
    const tr = capped ? [] : pool.filter((i) => hasTreasure(cards[i]));
    return { do: 'pick', i: rarestIndex(cards, tr.length ? tr : pool), why: tr.length ? '尋寶率優先' : '最稀有' };
  }

  function pickerStep(now) {
    const isChest = /寶箱/.test(txt($('#pk-title')));
    if (!on(isChest ? 'pickChest' : 'pickSkill')) { st.action = '等你選卡'; return; }
    st.action = isChest ? '選寶箱卡' : '選升級卡';
    if (!st.pickerSeenAt) { st.pickerSeenAt = now; st.bankTry = 0; readTreasure(); }
    if (now - st.pickerSeenAt < T.pickDelay) return;

    const cards = [...document.querySelectorAll('#pk-cards .pk-card')];
    const d = decide(cards, isChest);
    const name = d.i != null ? cardName(cards[d.i]) : '';
    st.pickerSeenAt = now + 400; // 等畫面更新再看下一步

    recPick(d.do, d.i != null ? cards[d.i] : null, { why: d.why });
    if (d.do === 'banish') {
      st.action = '放逐：' + name; logPick(cards, '放逐 ' + name, d.why);
      actBtn(cards[d.i], /放逐|Banish/i).click(); return;
    }
    if (d.do === 'focus') {
      st.action = '專注：' + name; logPick(cards, '專注 ' + name, d.why);
      st.focused.add(d.item ? 'item:' + d.item : skillOfCard(cards[d.i], sk().order)); actBtn(cards[d.i], /專注|Focus/i).click(); return;
    }
    if (d.do === 'reroll') {
      st.action = '重抽全部'; logPick(cards, '重抽全部', d.why);
      barBtn(/重抽全部/).click(); return;
    }
    const idx = d.i;
    countPick(cards[idx]);
    if (isDeity(cards[idx]) && !st.deityLock) st.deityLock = name.replace(/\s*\+\s*\d+.*$/, '');
    const w = skillOfCard(cards[idx], sk().after); if (w) st.picked.add(w);
    st.action += '：' + (name || '第 ' + (idx + 1) + ' 張');
    logPick(cards, '選 ' + name, d.why);
    pressKey(String(idx + 1));
    setTimeout(() => {
      const p = $('#picker'), cs = document.querySelectorAll('#pk-cards .pk-card');
      if (visible(p) && !p.classList.contains('closing') && cs[idx]) cs[idx].click();
    }, 250);
  }

  // ================= 城裡 / 遠征 =================
  // 在野外：右上角「本局傷害」視窗（#dps）出現就是在野外。
  // 備援（萬一你把傷害視窗關掉）：不在城裡、而且看得到技能列或「升級」按鈕
  const inRun = () => visible($('#dps')) ||
    (!document.body.classList.contains('in-lobby') && (visible($('#skillbar')) || visible($('#bank-btn'))));
  const anyOverlayOpen = () => [...document.querySelectorAll('.overlay')].some((o) => !o.classList.contains('hidden') && o.getClientRects().length);
  const runLimitReached = () => on('runLimit') && cfg.runs >= cfg.runLimitN;
  const stageCards = () => [...document.querySelectorAll('#stages .stg-card')];
  const stageName = (c) => txt(c.querySelector('.stg-card-info b'));

  function syncStageList() {
    const names = stageCards().filter((c) => !c.classList.contains('soon')).map(stageName).filter(Boolean);
    if (!names.length || names.join('|') === cfg.expeList.join('|')) return;
    cfg.expeList = names; save();
  }
  function stageStep(now) {
    if (!visible($('#stages'))) { st.stageAt = 0; return false; }
    st.stagesSeenAt = now;
    syncStageList();
    if (!on('autoExpe')) return false;
    if (runLimitReached()) { st.action = '已達局數上限'; return true; }
    let want;
    if (ex('expePick')) {
      want = cfg.expeStage;
      const card = stageCards().find((c) => stageName(c) === want);
      if (!card) { st.action = '找不到地圖：' + want; return true; }
      if (card.classList.contains('soon')) { st.action = want + ' 尚未開放'; return true; }
      if (!card.classList.contains('sel')) { st.action = '② 選擇地圖：' + want; card.click(); st.stageAt = now; return true; }
    } else {
      const sel = stageCards().find((c) => c.classList.contains('sel'));
      want = sel ? stageName(sel) : '';
    }
    if (!ex('expeGo')) { st.action = '等你按「出發」'; return true; }
    if (!st.stageAt) st.stageAt = now;
    if (now - st.stageAt < T.startDelay) { st.action = '準備出發：' + want; return true; }
    const skip = $('#stg-skip'), go = $('#stg-go');
    if (visible(skip) && !skip.disabled) { st.action = '跳過劇情'; skip.click(); st.stageAt = now; return true; }
    if (visible(go) && !go.disabled) { st.action = '③ 出發：' + want; go.click(); st.stageAt = 0; return true; }
    st.action = want + ' 目前不能出發，請換一張地圖';
    return true;
  }
  // 遠征難度（對局紀錄用）：每一列的文字和滑桿值
  function readDiff(dlg) {
    return [...dlg.querySelectorAll('.diff-row')].map((row) => {
      const i = row.querySelector('input[type=range]');
      return { text: txt(row), v: i ? +i.value : null, max: i ? +i.max : null };
    });
  }
  function applyDifficulty(dlg) {
    if (cfg.expeDiff === 'keep') return;
    dlg.querySelectorAll('.diff-row input[type=range]').forEach((i) => {
      const v = cfg.expeDiff === 'max' ? i.max : i.min;
      if (i.value === v) return;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(i, v);
      i.dispatchEvent(new Event('input', { bubbles: true }));
      i.dispatchEvent(new Event('change', { bubbles: true }));
    });
  }

  function lobbyStep(now) {
    st.action = '';
    const target = on('autoExpe') ? (cfg.expeTalk ? { name: '遠征隊長', re: /隊長/ } : null)
                 : on('autoEvent') ? { name: '節慶主持人', re: /主持人/ } : null;
    if (target) { const pm = $('#player-menu'); if (visible(pm)) pm.classList.add('hidden'); }
    if (stageStep(now)) return;
    if (anyOverlayOpen() || !target) return;
    if (runLimitReached()) { st.action = '已達局數上限'; return; }

    const L = $('#lobby-labels');
    const me = L && L.querySelector('.lobby-name.self');
    const npc = L && [...L.querySelectorAll('.lobby-name.npc')].find((e) => e.textContent.trim() === target.name);
    if (!me || !npc) return;
    const prompt = $('#lobby-prompt');
    if (visible(prompt) && target.re.test(prompt.textContent)) {
      st.npcDone = true;
      if (!st.npcTalked) { st.npcTalked = true; st.action = '和' + target.name + '交談'; prompt.click(); }
      return;
    }
    if (st.npcDone) return;
    st.action = '走向' + target.name;
    if (now - st.lastLobbyMove < T.lobbyMoveEvery) return;
    st.lastLobbyMove = now;
    const mid = (e) => { const r = e.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height }; };
    const a = mid(me), b = mid(npc);
    const dx = b.x - a.x, dy = b.y - a.y, dist = Math.hypot(dx, dy) || 1;
    const cv = $('canvas.world3d').getBoundingClientRect();
    let k = Math.min(1, (Math.min(cv.width, cv.height) * 0.35) / dist);
    for (let i = 0; i < 4; i++) {
      let x = a.x + dx * k, y = a.y + dy * k + 45;
      x = Math.min(cv.right - 20, Math.max(cv.left + 20, x));
      y = Math.min(cv.bottom - 20, Math.max(cv.top + 20, y));
      if (clickAt(x, y)) break;
      k *= 0.6;
    }
  }

  // ---- 卡地形偵測 ----
  // 一直下指令但小地圖上的位置 1.5 秒都沒動 → 卡住了：往目標的側邊（或反方向）繞開一下；
  // 同一個目標卡 3 次就先放棄它 20 秒
  function checkStuck(now, me, targetAng, target) {
    const a = st.anchor;
    if (!a || Math.hypot(me.cx - a.x, me.cy - a.y) > 0.8) { st.anchor = { x: me.cx, y: me.cy, t: now }; return false; }
    if (now - a.t < 1500 || st.lastMove < a.t) return false;
    st.anchor = { x: me.cx, y: me.cy, t: now };
    st.stuckN = (st.stuckN || 0) + 1;
    const side = st.stuckN % 2 ? 1 : -1;
    const base = targetAng != null ? targetAng : st.angle;
    st.escAng = st.stuckN >= 3 ? base + Math.PI + (Math.random() - 0.5) * 0.8
                               : base + side * (Math.PI / 2 + Math.random() * 0.5);
    st.escUntil = now + 1300;
    if (target && st.stuckN >= 3) {
      st.blocked.push({ x: target.cx, y: target.cy, until: now + 20000 });
      st.stuckN = 0;
    }
    if (targetAng == null) st.angle = st.escAng; // 繞圈時直接換方向
    return true;
  }
  // ---- 節慶扭蛋：直接看遊戲畫面（小地圖上沒有） ----
  // 每 0.5 秒在畫面更新的那一格把 3D 畫面縮成 1/3 讀顏色，找出球的色塊
  const BALL = {
    '白': (r, g, b) => b >= r + 15 && b >= g + 12 && b >= 115,
    '橘': (r, g, b) => r >= 180 && g >= 90 && g <= 190 && b <= 110 && r - b >= 90,
    '紅': (r, g, b) => r >= 150 && g <= 80 && b <= 90 && r - g >= 90,
    '粉紅': (r, g, b) => r >= 200 && g >= 90 && g <= 160 && b >= 80 && b <= 160 && r - g >= 60, // 節慶扭蛋（珊瑚粉紅）
    '綠': (r, g, b) => g >= 140 && g - r >= 40 && g - b >= 30,                                   // 蛇王扭蛋
  };
  const balls = { list: [], at: 0, busy: false };
  const scanCv = document.createElement('canvas');
  function scanBalls() {
    const w = $('canvas.world3d');
    if (!w || balls.busy) return;
    balls.busy = true;
    requestAnimationFrame(() => {
      balls.busy = false;
      try {
        const S = 3, W = Math.floor(w.width / S), H = Math.floor(w.height / S);
        scanCv.width = W; scanCv.height = H;
        const x = scanCv.getContext('2d', { willReadFrequently: true });
        x.imageSmoothingEnabled = false;
        x.drawImage(w, 0, 0, W, H);
        const d = x.getImageData(0, 0, W, H).data;
        const cx = W / 2, cy = H / 2, found = [];
        for (const [kind, f] of Object.entries(BALL)) {
          const gs = [];
          for (let y = 0; y < H; y++) for (let X = 0; X < W; X++) {
            const i = (y * W + X) * 4;
            if (!f(d[i], d[i + 1], d[i + 2])) continue;
            if (Math.hypot(X - cx, y - cy) < 24) continue; // 自己的角色
            let g = gs.find((g) => X >= g.x0 - 3 && X <= g.x1 + 3 && y >= g.y0 - 3 && y <= g.y1 + 3);
            if (!g) { g = { x0: X, x1: X, y0: y, y1: y, n: 0, r: 0, gg: 0, b: 0 }; gs.push(g); }
            g.r += d[i]; g.gg += d[i + 1]; g.b += d[i + 2];
            g.n++; g.x0 = Math.min(g.x0, X); g.x1 = Math.max(g.x1, X); g.y0 = Math.min(g.y0, y); g.y1 = Math.max(g.y1, y);
          }
          // 球大約 11×14 格（縮小後）；太小是雜點，太大是水面或燈籠
          gs.filter((g) => g.n >= 20 && g.n <= 300 && g.x1 - g.x0 <= 22 && g.y1 - g.y0 <= 26)
            .forEach((g) => found.push({ kind, n: g.n, w: g.x1 - g.x0, h: g.y1 - g.y0,
              rgb: [g.r, g.gg, g.b].map((v) => Math.round(v / g.n)).join('/'), x: ((g.x0 + g.x1) / 2) * S, y: ((g.y0 + g.y1) / 2) * S, d: Math.hypot((g.x0 + g.x1) / 2 - cx, (g.y0 + g.y1) / 2 - cy) * S }));
        }
        balls.list = found.sort((a, b) => a.d - b.d);
        balls.at = Date.now();
      } catch (e) { balls.list = []; }
    });
  }
  // ---- 躲到鐘下（Moonlight Flower 等王的機制） ----
  // 王會喊「躲到鐘下！」並在離你 380~620 距離的地方放一口金色的鐘，倒數結束時不在鐘下（安全半徑 150）
  // 或鐘還被封印 → 直接被秒殺。鐘是自己發光的材質，白天晚上顏色都一樣：金色 (1.0, 0.8, 0.42)，封印時偏紅。
  function bellAnnounce() {
    const hud = $('#hud');
    if (!hud) return '';
    for (const e of hud.querySelectorAll('*')) {
      if (e.children.length || !visible(e)) continue;
      const t = e.textContent;
      if (/鐘/.test(t)) return t.trim();
    }
    return '';
  }
  const bell = { at: 0, busy: false, pos: null };
  const bellCv = document.createElement('canvas');
  function scanBell() {
    const w = $('canvas.world3d');
    if (!w || bell.busy) return;
    bell.busy = true;
    requestAnimationFrame(() => {
      bell.busy = false;
      try {
        const S = w.width > 1100 ? 4 : 3, W = Math.floor(w.width / S), H = Math.floor(w.height / S); // 視窗小時取樣細一點
        bellCv.width = W; bellCv.height = H;
        const x = bellCv.getContext('2d', { willReadFrequently: true });
        x.imageSmoothingEnabled = false;
        x.drawImage(w, 0, 0, W, H);
        const d = x.getImageData(0, 0, W, H).data, gs = [];
        for (let y = 0; y < H; y++) for (let X = 0; X < W; X++) {
          const i = (y * W + X) * 4, r = d[i], g = d[i + 1], b = d[i + 2];
          const gold = r >= 225 && g >= 165 && g <= 240 && b >= 70 && b <= 200 && r - b >= 55;
          const red = r >= 220 && g <= 120 && b <= 110;   // 封印中的紅色
          if (!gold && !red) continue;
          let gr = gs.find((q) => X >= q.x0 - 4 && X <= q.x1 + 4 && y >= q.y0 - 4 && y <= q.y1 + 4);
          if (!gr) { gr = { x0: X, x1: X, y0: y, y1: y, n: 0 }; gs.push(gr); }
          gr.n++; gr.x0 = Math.min(gr.x0, X); gr.x1 = Math.max(gr.x1, X); gr.y0 = Math.min(gr.y0, y); gr.y1 = Math.max(gr.y1, y);
        }
        // 鐘很大：取最大的一塊（至少 25 格），站點＝鐘底部中間
        const big = gs.filter((q) => q.n >= 25).sort((a, b) => b.n - a.n)[0];
        bell.pos = big ? { x: ((big.x0 + big.x1) / 2) * S, y: big.y1 * S, x0: big.x0 * S, x1: big.x1 * S, y0: big.y0 * S, y1: big.y1 * S,
          n: big.n, w: (big.x1 - big.x0) * S, h: (big.y1 - big.y0) * S } : null;
        bell.at = Date.now();
      } catch (e) { bell.pos = null; }
    });
  }
  // 回傳 true 表示這一輪在處理躲鐘
  function bellStep(now) {
    if (!on('bellDodge')) return false;
    const a = bellAnnounce();
    if (/躲到鐘下|鐘開啟|躲到下面|被封印/.test(a)) st.bellUntil = now + 15000;
    if (/太遲/.test(a)) st.bellUntil = now + 4000;
    // 斐揚洞穴 MVP 前 10 秒就先提醒（公告一出來立刻找鐘）
    const ml = mvpLeft();
    if (st.runStage === '斐揚洞穴' && ml != null && ml > 0 && ml <= 10 && !(now < (st.bellUntil || 0))) { st.bellAlert = true; }
    // 鐘只會在 MVP 戰鬥中出現：王一死（進入撿戰利品倒數）就馬上結束躲鐘，去撿掉落物
    const tmEl = $('#timer');
    if (tmEl && !tmEl.classList.contains('mvp')) st.bellUntil = 0;
    if (!(now < (st.bellUntil || 0))) { st.bellParked = false; st.bellLast = null; st.bellW = 0; return false; }
    // ① 精確模式：讀到鐘的真實座標＋認得出自己 → 直接走到鐘的正下方（安全半徑 150，站到 60 以內）
    const B = window.__rhBell, me = B && now - B.at < 30000 && myPos();
    if (B && me) {
      const bx = B.o.mesh.position.x, by = B.o.mesh.position.z;
      const dx = bx - me.x, dy = by - me.y, d = Math.hypot(dx, dy);
      if (d < 60 || (st.bellParked && d < 100)) { st.bellParked = true; st.action = '在鐘下等待（' + Math.round(d) + '）' + (B.o.sealed ? '鐘封印中' : ''); return true; }
      st.bellParked = false;
      st.action = '躲到鐘下！（距離 ' + Math.round(d) + '）'; st.lastMove = now;
      const k = Math.min(GEO.stepRadius, 0.03 + d / 1800); // 越近步伐越小，避免走過頭
      clickWorld(0.5 + (dx / d) * k, 0.5 + (dy / d) * k);
      return true;
    }
    // ② 備用：看畫面顏色找鐘
    if (now - bell.at > 300) scanBell();
    const w = $('canvas.world3d'), r = w.getBoundingClientRect();
    const cx = w.width / 2, cy = w.height / 2;
    let b = bell.pos && now - bell.at <= 1500 ? bell.pos : null;
    // 實測：鐘會閃爍，偵測時有時無。躲鐘期間絕不去做別的事（撿寶箱、開升級都不行）：
    // 找不到鐘時，若剛剛還看得到（1.5 秒內）就照上次的位置走；已經站好就原地不動
    if (!b) {
      if (st.bellParked) { st.action = '在鐘下等待'; return true; }
      const L = st.bellLast;
      if (L && now - L.at < 1500) {
        st.action = '躲到鐘下！（照上次位置）'; st.lastMove = now;
        clickAt(r.left + r.width * (L.x / w.width), r.top + r.height * (L.y / w.height));
      } else st.action = '躲鐘：找鐘中，原地等';
      return true;
    }
    if (cfg.debugLog && now - (st.bellLogAt || 0) > 2000) {
      st.bellLogAt = now;
      addLog({ t: new Date().toLocaleTimeString(), job: curJob(), title: '躲鐘', treasure: 0,
        cards: [`公告：${a || '(已消失)'}`, `鐘位置(${Math.round(b.x)},${Math.round(b.y)}) 大小${b.n} ${b.w}x${b.h}`], act: '跑向鐘', why: '' });
    }
    // 實測（v6.7 陣亡那次）：視窗小、鏡頭又拉遠時，鐘在畫面上只有 50～130 像素寬，
    // 舊的「70／160 像素內就算站好」太寬鬆，人其實還在鐘外面就停下來等 → 被秒殺。
    // 改成照鐘在畫面上的大小換算：鐘半徑 78、安全半徑 150 ≈ 鐘的寬度，所以離鐘底中心 0.35 個鐘寬內才算站好。
    const bw = Math.max(st.bellW = Math.max(b.w, (st.bellW || 0) * 0.8), 30);
    const tx = b.x, ty = b.y1 - bw * 0.3;
    st.bellLast = { x: tx, y: ty, at: now };
    const dist = Math.hypot(tx - cx, ty - cy), m = bw * 0.15;
    const inside = cx > b.x0 + m && cx < b.x1 - m && cy > b.y0 + m && cy < b.y1 - m;
    if (inside || dist < bw * 0.35 || (st.bellParked && dist < bw * 0.6)) { st.bellParked = true; st.action = '在鐘下等待'; return true; }
    st.bellParked = false;
    st.action = '躲到鐘下！'; st.lastMove = now;
    clickAt(r.left + r.width * (tx / w.width), r.top + r.height * (ty / w.height));
    return true;
  }

  // 追同一顆球太久（超過 6 秒還在附近）→ 可能不是球，先忽略 15 秒
  function ballTarget(now) {
    if (!on('seekBall') || !cfg.colorBall || now < (st.ballMute || 0)) return null;
    if (now - balls.at > 400) scanBalls();
    const b = balls.list[0];
    if (!b || now - balls.at > 1500) { st.ballSince = 0; return null; }
    if (!st.ballSince) st.ballSince = now;
    if (now - st.ballSince > 6000) { st.ballMute = now + 15000; st.ballSince = 0; return null; }
    // 除錯紀錄：每 3 秒記一次看到的球（方便校正顏色）
    if (cfg.debugLog && now - (st.ballLogAt || 0) > 3000) {
      st.ballLogAt = now;
      addLog({ t: new Date().toLocaleTimeString(), job: curJob(), title: '扭蛋偵測', treasure: Math.round(st.treasure),
        cards: balls.list.slice(0, 5).map((x) => `${x.kind} 位置(${Math.round(x.x)},${Math.round(x.y)}) 大小${x.n} ${x.w}x${x.h} 平均色 ${x.rgb}`),
        act: '前往' + b.kind, why: '畫面偵測' });
    }
    return b;
  }

  const notBlocked = (now) => (t) => !st.blocked.some((b) => b.until > now && Math.hypot(b.x - t.cx, b.y - t.cy) < 3);

  // ---- 追蹤王的位置，王死掉就跑去牠死掉的地方 ----
  // 小地圖上的點會跟著畫面捲動，所以換算成「地圖格子座標」記住（右上角的 (x, y) 就是自己的格子座標）。
  // 小地圖 1 格＝k 像素：用寶箱（不會動）跟自己的相對位移即時估算；格子 y 往上變大、畫面 y 往下變大。
  function updateK(mm) {
    if (!mm.tile || !mm.chests.length) return;
    const c = mm.chests.reduce((a, b) => (Math.hypot(b.cx - mm.me.cx, b.cy - mm.me.cy) < Math.hypot(a.cx - mm.me.cx, a.cy - mm.me.cy) ? b : a));
    const rel = { x: mm.me.cx - c.cx, y: mm.me.cy - c.cy, cx: c.cx, cy: c.cy, tile: mm.tile };
    const p = st.kPrev;
    st.kPrev = rel;
    if (!p) return;
    const dt = Math.hypot(rel.tile.x - p.tile.x, rel.tile.y - p.tile.y);
    if (dt < 3 || dt > 30) return;
    const dp = Math.hypot(rel.x - p.x, rel.y - p.y);
    const k = dp / dt;
    if (k > 0.1 && k < 5) st.k = st.k ? st.k * 0.8 + k * 0.2 : k;
  }
  const pxToTile = (mm, px, py) => ({ x: mm.tile.x + (px - mm.me.cx) / (st.k || 0.5), y: mm.tile.y - (py - mm.me.cy) / (st.k || 0.5) });
  function trackBosses(mm, now) {
    if (!mm.tile) return;
    updateK(mm);
    const edge = 8;
    st.bossSeen = st.bossSeen || [];
    for (const b of mm.bosses) {
      const t = pxToTile(mm, b.cx, b.cy), inView = b.cx > edge && b.cx < mm.W - edge && b.cy > edge && b.cy < mm.H - edge;
      let s = st.bossSeen.find((q) => Math.hypot(q.x - t.x, q.y - t.y) < 25);
      if (!s) { s = { x: t.x, y: t.y }; st.bossSeen.push(s); }
      Object.assign(s, { x: t.x, y: t.y, at: now, inView });
    }
    // 1.5 秒沒看到（標記閃爍週期約 0.3 秒）、而且最後是在小地圖裡面（不是跑出邊界）→ 判定死了
    for (const s of st.bossSeen) {
      if (now - s.at > 1500 && s.inView) { st.bossSpot = { x: s.x, y: s.y, at: now }; s.dead = true; }
    }
    st.bossSeen = st.bossSeen.filter((s) => !s.dead && now - s.at < 1500);
  }

  // ---- 精確定位：用遊戲的掉落物清單＋自己的角色位置 ----
  // 自己的位置：掉落物的畫面物件放在同一層，自己的角色也在那層；
  // 實測 角色 x ÷ 32 ≈ 右上角格子 x + 0.5，角色 y ÷ 32 ＋ 格子 y ≈ 固定值（每張地圖不同），用來認出哪個是自己
  const LOOT_KINDS = ['loot', 'card', 'egg', 'drop', 'mystery'];
  // 認出「自己」：同一層裡還有怪物、隊友，光看 x 會認錯。
  // 做法：每次自己的格子座標改變時，記下每個候選的 (x/32 − 格子x) 和 (y/32 + 格子y)；
  // 只有自己的這兩個值會一直固定不變（怪物、隊友會亂跳）。累積 5 次變動後選最穩定的那個鎖定。
  function myPos() {
    const A = pickupList(), m = txt($('#minimap-coords')).match(/(\d+)\s*,\s*(\d+)/);
    if (!A || !m) return null;
    const tx = +m[1], ty = +m[2];
    const v0 = st.meView;
    if (v0 && v0.parent && Math.abs(v0.x / 32 - tx - st.meDx) < 1.5 && Math.abs(v0.y / 32 + ty - st.mapH) < 1.5) return { x: v0.x, y: v0.y };
    if (v0) { st.meView = null; st.cand = new Map(); } // 鎖定的失效了 → 重新認
    const key = tx + ',' + ty;
    if (key === st.lastTileKey) return null;      // 格子沒變，無法分辨
    st.lastTileKey = key;
    const any = A.find((p) => !p.dead && p.view && p.view.parent);
    if (!any) return null;
    const pv = new Set(A.map((p) => p.view));
    st.cand = st.cand || new Map();
    let best = null, bs = 1e9;
    for (const v of any.view.parent.children) {
      if (pv.has(v)) continue;
      const dx = v.x / 32 - tx, sy = v.y / 32 + ty;
      if (Math.abs(dx) > 3) { st.cand.delete(v); continue; }
      let c = st.cand.get(v);
      if (!c) { c = { n: 0, dx0: dx, sy0: sy, err: 0 }; st.cand.set(v, c); }
      c.n++; c.err = Math.max(c.err, Math.abs(dx - c.dx0), Math.abs(sy - c.sy0));
      // 自己的 x 永遠約在「格子 x + 0.5」：多一道條件排除跟著你跑的怪物或隊友
      if (c.n >= 5 && c.err < 1.0 && Math.abs(c.dx0 - 0.5) < 0.7 && c.err < bs) { bs = c.err; best = v; }
    }
    if (!best) return null;
    const c = st.cand.get(best);
    st.meView = best; st.meDx = c.dx0; st.mapH = c.sy0;
    return { x: best.x, y: best.y };
  }

  const ignoreP = new WeakMap();
  // 走向某個掉落物（地圖座標）。同一個東西追超過 15 秒還沒撿到 → 先忽略 20 秒
  function goWorld(now, me, t, label, mm) {
    const dx = t.x - me.x, dy = t.y - me.y, d = Math.hypot(dx, dy) || 1;
    if (st.pTarget !== t) { st.pTarget = t; st.pSince = now; }
    else if (now - st.pSince > 15000) { ignoreP.set(t, now + 20000); st.pTarget = null; return false; }
    // 卡地形：改用角色的精確座標（格）判斷有沒有在動
    if (checkStuck(now, { cx: me.x / 32, cy: me.y / 32 }, Math.atan2(dy, dx), null)) return true;
    st.action = st.lastAct = label + '（' + Math.round(d / 32) + ' 格）'; st.lastMove = now;
    const k = Math.min(GEO.stepRadius, 0.05 + d / 1500);
    clickWorld(0.5 + (dx / d) * k, 0.5 + (dy / d) * k);
    return true;
  }
  // 金錢夠不夠開寶箱：右上角「1485 z」和「寶箱 1694 z」
  function canAffordChest() {
    if (!cfg.chestMoney) return true;
    const z = +(txt($('#zeny-text')).match(/\d+/) || [NaN])[0], c = +(txt($('#chest-cost')).match(/\d+/) || [NaN])[0];
    return isNaN(z) || isNaN(c) || z >= c;
  }
  // 統計這局撿到的東西：看到過、後來消失（被撿走）的就算一個
  function countGot() {
    const A = pickupList();
    if (!A || !st.got) return;
    for (const p of A) {
      const k = LOOT_KINDS.includes(p.kind) ? 'loot' : p.kind === 'ball' ? 'ball' : p.kind === 'chest' ? 'chest' : null;
      if (!k) continue;
      if (!p.dead) st.seenP.add(p);
      else if (st.seenP.has(p) && !st.doneP.has(p)) { st.doneP.add(p); st.got[k]++; }
    }
  }
  function preciseStep(now, mm, lootPhase) {
    const A = pickupList(), me = A && myPos();
    if (!me) return false;
    // 別追到地圖角落：目標離自己太遠（超過地圖一半大小，用小地圖比例換算）就先不追；王的掉落物例外
    // 地圖高度（格）≈ st.mapH（自己的 y/32 + 格子 y 是常數，正好等於地圖格數），寬度用小地圖比例換算
    const M = mm && mm.map;
    const mapTiles = M && M.h > 20 && st.mapH ? Math.max(st.mapH, (M.w / M.h) * st.mapH) : 0;
    const farMax = on('farLimit') && mapTiles ? mapTiles * 32 * 0.5 : Infinity;
    const alive = (p) => !p.dead && !(ignoreP.get(p) > now) && (LOOT_KINDS.includes(p.kind) || Math.hypot(p.x - me.x, p.y - me.y) < farMax);
    const nearestP = (f) => {
      let b = null, bd = 1e12;
      for (const p of A) if (alive(p) && f(p)) { const d = (p.x - me.x) ** 2 + (p.y - me.y) ** 2; if (d < bd) { bd = d; b = p; } }
      return b;
    };
    // 血少 → 先吃最近的肉
    if (on('meatLowHp') && hpPct() < cfg.meatLowHpN) {
      const meat = nearestP((p) => p.kind === 'meat');
      if (meat && goWorld(now, me, meat, '血少，吃肉', mm)) return true;
    }
    const loot = on('seekLoot') && nearestP((p) => LOOT_KINDS.includes(p.kind));
    if (loot && goWorld(now, me, loot, (lootPhase ? '倒數中！' : '') + '撿王的掉落物', mm)) return true;
    if (lootPhase) { st.action = '倒數中：沒有掉落物了'; return true; }
    const ball = on('seekBall') && nearestP((p) => p.kind === 'ball');
    if (ball && goWorld(now, me, ball, '撿節慶扭蛋', mm)) return true;
    const mag = on('seekMagnet') && nearestP((p) => p.kind === 'magnet');
    if (mag && goWorld(now, me, mag, '撿磁鐵', mm)) return true;
    const ch = on('seekChest') && canAffordChest() && nearestP((p) => p.kind === 'chest');
    if (ch && goWorld(now, me, ch, '撿寶箱', mm)) return true;
    return false; // 沒東西可撿 → 交給繞圈
  }

  // ---- 沒東西撿時：往地圖正中間靠，在中間附近小範圍繞 ----
  // 小地圖拉到看得到整張地圖後，地圖本體的中心＝地圖中心；自己離中心超過地圖大小的 12% 就往中心走
  function wanderStep(now, mm) {
    if (!on('autoWander')) return;
    if (mm && checkStuck(now, mm.me, null, null)) return;
    if (now - st.lastMove <= T.wanderEvery) { st.action = st.lastAct || '繞圈'; return; }
    st.lastMove = now;
    const M = mm && mm.map;
    if (on('stayCenter') && M && M.w > 20) {
      const dx = M.cx - mm.me.cx, dy = M.cy - mm.me.cy, d = Math.hypot(dx, dy), R = Math.max(M.w, M.h) * 0.12;
      if (d > R) {
        st.action = st.lastAct = '回地圖中間';
        const k = Math.min(GEO.stepRadius, 0.12 + (d / Math.max(M.w, M.h)) * 0.6);
        clickWorld(0.5 + (dx / d) * k, 0.5 + (dy / d) * k);
        return;
      }
    }
    st.action = st.lastAct = '繞圈';
    st.angle += Math.PI / 3 + (Math.random() - 0.5) * 0.6;
    const rad = GEO.wanderRadius * (0.6 + Math.random() * 0.4) * (on('stayCenter') ? 0.7 : 1);
    clickWorld(0.5 + Math.cos(st.angle) * rad, 0.5 + Math.sin(st.angle) * rad * 0.8);
  }

  // 遊戲計時（一般時候往上數；MVP 出現後變成倒數，這時回傳 null）
  function runClock() {
    const tm = $('#timer');
    if (!tm || tm.classList.contains('mvp') || tm.classList.contains('loot')) return null;
    const m = txt(tm).match(/(\d+):(\d+)/);
    return m ? +m[1] * 60 + +m[2] : null;
  }
  const fmt = (s) => Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
  function nextBoss() {
    const list = STAGE_BOSS[st.runStage] || [], t = runClock();
    if (t == null || !list.length) return null;
    const b = list.find((x) => x[2] > t);
    return b ? { name: b[0], kind: b[1], left: b[2] - t } : null;
  }
  function mvpLeft() {
    const t = runClock(), b = (STAGE_BOSS[st.runStage] || []).find((x) => x[1] === 'MVP');
    return t == null || !b ? null : b[2] - t;
  }

  function fieldStep(now) {
    st.action = '';
    // MVP 死後：計時器變成 15 秒倒數（#timer 有 loot 類別）＝撿掉落物時間，這時只撿掉落物、不開升級
    const lootPhase = !!$('#timer') && $('#timer').classList.contains('loot');
    if (now - (st.gotAt || 0) > 1000) { st.gotAt = now; countGot(); }
    if (bellStep(now)) return; // 躲鐘最優先，連升級都先不開
    if (now - (st.treasureAt || 0) > 5000) readTreasure(); // 面板上的尋寶率每 5 秒更新
    const n = parseInt(txt($('#bank-n')) || '0', 10);
    // MVP 快出現了 → 先把累積的升級選完（就算沒勾自動按 B）
    const ml = mvpLeft();
    // 按「升級」：按了 1.5 秒還沒跳出選卡畫面（例如組隊時只有隊長能開、或遊戲當下不允許）→ 先停 10 秒不按，繼續撿東西
    const canBank = n > 0 && visible($('#bank-btn')) && !$('#bank-btn').disabled && now > (st.bankMute || 0);
    const pressBank = (label) => {
      if (st.bankTry && now - st.bankTry > 1500) { st.bankTry = 0; st.bankMute = now + 10000; return false; }
      if (!st.bankTry) st.bankTry = now;
      st.action = label; $('#bank-btn').click(); return true;
    };
    if (!canBank) st.bankTry = 0;
    if (canBank && on('preMvp') && ml != null && ml > 0 && ml <= cfg.preMvpN && pressBank('MVP 還有 ' + ml + ' 秒，先選完升級')) return;
    if (canBank && !lootPhase && on('autoUpgrade') && pressBank('打開升級')) return;
    if (n > 0 && now < (st.bankMute || 0)) st.bankNote = '（升級打不開，10 秒後再試；組隊時可能只有隊長能開）'; else st.bankNote = '';
    if (on('lowHpStop') && hpPct() < cfg.lowHpStopN) { st.action = 'HP 過低，停止走位'; return; }
    if (!on('seekLoot') && !on('seekBall') && !on('seekMagnet') && !on('seekChest') && !on('autoWander')) return;
    if (now - st.lastMove < T.seekEvery) { st.action = st.lastAct || ''; return; }

    const mm = readMinimap();
    st.blocked = (st.blocked || []).filter((b) => b.until > now);

    // 正在繞開障礙物
    if (st.escUntil > now) {
      st.action = st.lastAct = '卡住了，繞路';
      st.lastMove = now;
      clickWorld(0.5 + Math.cos(st.escAng) * 0.3, 0.5 + Math.sin(st.escAng) * 0.3);
      return;
    }

    // 精確模式（讀到遊戲的掉落物清單時）：直接用真實座標去撿，比看顏色準確很多
    if (cfg.precise !== false && preciseStep(now, mm, lootPhase)) { st.precise = true; return; }
    st.precise = !!st.meView; // 已認出自己但沒東西撿 → 繞圈；還沒認出 → 用小地圖的舊方法
    if (st.precise) { wanderStep(now, mm); return; } // 精確模式但沒東西撿 → 回中間繞

    if (mm) trackBosses(mm, now);
    // 王剛死：跑到死掉的地點（最多 12 秒；到了之後停 1.5 秒讓東西被撿起來）
    const spot = st.bossSpot;
    if (spot && on('bossSpot') && mm && mm.tile && now - spot.at < 12000) {
      const dx = spot.x - mm.tile.x, dy = -(spot.y - mm.tile.y), len = Math.hypot(dx, dy);
      if (len < 3) {
        st.bossArrive = st.bossArrive || now;
        if (now - st.bossArrive > 1500) { st.bossSpot = null; st.bossArrive = 0; }
        st.action = st.lastAct = '在王死掉的地方撿東西';
      } else {
        st.bossArrive = 0;
        if (checkStuck(now, mm.me, Math.atan2(dy, dx), null)) return;
        st.action = st.lastAct = '跑到王死掉的地方（' + Math.round(len) + ' 格）'; st.lastMove = now;
        const k = Math.min(GEO.stepRadius, 0.06 + len * 0.015);
        clickWorld(0.5 + (dx / len) * k, 0.5 + (dy / len) * k);
      }
      if (!mm.loot.length) return; // 地圖上有看到掉落物就交給下面「撿掉落物」去追
    } else if (spot && now - spot.at >= 12000) st.bossSpot = null;

    // 王的掉落物最優先（MVP 死後撿取時間有限）
    if (mm && on('seekLoot')) {
      let l = nearest(mm.me, mm.loot.filter(notBlocked(now)));
      // 掉落物在小地圖上會閃，倒數中看不到時就照 2.5 秒內最後看到的位置繼續走
      if (l) st.lastLoot = { t: l.t, at: now };
      else if (lootPhase && st.lastLoot && now - st.lastLoot.at < 2500) l = { t: st.lastLoot.t };
      if (l) {
        const dx = l.t.cx - mm.me.cx, dy = l.t.cy - mm.me.cy, len = Math.hypot(dx, dy) || 1;
        // 已經站在上面 3 秒還在 → 不是掉落物（可能是地形顏色），忽略 30 秒（撿取倒數中不放棄）
        if (len < 3 && !lootPhase) { st.lootNear = st.lootNear || now; if (now - st.lootNear > 3000) { st.blocked.push({ x: l.t.cx, y: l.t.cy, until: now + 30000 }); st.lootNear = 0; } }
        else st.lootNear = 0;
        if (checkStuck(now, mm.me, Math.atan2(dy, dx), l.t)) return;
        st.action = st.lastAct = (lootPhase ? '倒數中！' : '') + '撿王的掉落物'; st.lastMove = now;
        const k = Math.min(GEO.stepRadius, 0.06 + len * 0.03);
        clickWorld(0.5 + (dx / len) * k, 0.5 + (dy / len) * k);
        return;
      }
    }

    // 倒數中只撿掉落物（不去撿寶箱、扭蛋）；沒有掉落物就繞圈找
    if (lootPhase) { if (on('autoWander') && now - st.lastMove > T.wanderEvery) { st.lastMove = now; st.angle += Math.PI / 3;
      clickWorld(0.5 + Math.cos(st.angle) * 0.25, 0.5 + Math.sin(st.angle) * 0.2); } st.action = '倒數中：找掉落物'; return; }

    // 節慶扭蛋：直接點球所在的位置
    const ball = ballTarget(now);
    if (ball) {
      const w = $('canvas.world3d'), r = w.getBoundingClientRect();
      const fx = ball.x / w.width, fy = ball.y / w.height;
      if (mm && checkStuck(now, mm.me, Math.atan2(fy - 0.5, fx - 0.5), null)) return;
      st.action = st.lastAct = '前往扭蛋（' + ball.kind + '）'; st.lastMove = now;
      clickAt(r.left + r.width * fx, r.top + r.height * fy + 8);
      return;
    }

    if (mm && (on('seekMagnet') || on('seekChest'))) {
      const ok = notBlocked(now);
      const m = on('seekMagnet') && nearest(mm.me, mm.magnets.filter(ok));
      const c = on('seekChest') && canAffordChest() && nearest(mm.me, mm.chests.filter(ok));
      const pick = (m && { ...m, kind: '磁鐵' }) || (c && { ...c, kind: '寶箱' });
      if (pick) {
        const dx = pick.t.cx - mm.me.cx, dy = pick.t.cy - mm.me.cy, len = Math.hypot(dx, dy) || 1;
        if (checkStuck(now, mm.me, Math.atan2(dy, dx), pick.t)) return;
        st.action = st.lastAct = '前往' + pick.kind; st.lastMove = now;
        const k = Math.min(GEO.stepRadius, 0.06 + len * 0.03);
        clickWorld(0.5 + (dx / len) * k, 0.5 + (dy / len) * k);
        return;
      }
    }
    wanderStep(now, mm);
  }

  // ================= 對局紀錄（給分析用） =================
  // 每局一筆：開局建立 st.rec，過程記選卡／放逐／重抽／專注（自動和手動都記）、最低血量，
  // 結算時讀結算畫面（結果、等級、擊殺、每個技能的傷害、獲得的裝備卡片），整筆送給 exe 寫進 runs.jsonl。
  // 刻意只記「遊戲狀態和選擇」，不記操作的時間間隔或滑鼠動作。
  // 暫停時也要記（使用者可能暫停小幫手自己玩），所以這些函式在 step() 的暫停判斷之前呼叫。
  const gameClock = () => txt($('#timer'));
  const timerPhase = () => { const t = $('#timer'); return !t ? '' : t.classList.contains('loot') ? 'loot' : t.classList.contains('mvp') ? 'mvp' : 'normal'; };
  function cardInfo(c) {
    return { name: cardName(c), rarity: badgeOf(c), isNew: isNewCard(c), deity: isDeity(c) };
  }
  function recStart(now) {
    st.rec = {
      v: 1, start: new Date(now).toISOString(), stage: st.runStage || '', job: curJob(),
      mode: now - (st.diffSeenAt || 0) < 60000 ? 'expe' : now - (st.festSeenAt || 0) < 60000 ? 'event' : '',
      diff: now - (st.diffSeenAt || 0) < 60000 ? st.diffRows : null,
      autoAtStart: !cfg.paused, pausedAny: !!cfg.paused, picks: [], hpMin: 100,
    };
  }
  // kind：pick／banish／reroll／focus；manual：使用者自己點的
  function recPick(kind, card, extra) {
    if (!st.rec) return;
    const isChest = /寶箱/.test(txt($('#pk-title')));
    st.rec.picks.push(Object.assign({ gt: gameClock(), kind, chest: isChest }, card ? cardInfo(card) : {}, extra || {}));
  }
  function recSample(now) {
    const r = st.rec;
    if (!r) return;
    if (cfg.paused) r.pausedAny = true;
    r.hpMin = Math.min(r.hpMin, Math.round(hpPct()));
    // 最後狀態：死掉或結束前一刻的時間、階段、血量、下一隻王（分析失敗原因用）
    const nb = nextBoss();
    r.last = { gt: gameClock(), phase: timerPhase(), hp: Math.round(hpPct()), nextBoss: nb ? nb.name : '', action: st.action || '' };
    // 手動遊玩時主迴圈不會去讀道具欄，這裡每 10 秒補讀一次（道具、神祇、尋寶率）
    if (now - (st.treasureAt || 0) > 10000) readTreasure();
  }
  function outcomeOf(title) {
    if (/放棄|abandon/i.test(title)) return 'abandoned';
    if (/活下|存活|生還|倖存|勝利|通關|突破|超越|sobreviv|superad|surviv|clear/i.test(title)) return 'clear';
    if (/死|陣亡|倒下|落敗|muert|caíd|died|dead|fallen/i.test(title)) return 'death';
    if (/結束|terminad|ended|over/i.test(title)) return 'ended';
    return 'unknown';
  }
  function recFinish(now) {
    const r = st.rec;
    if (!r) return;
    st.rec = null;
    const go = $('#gameover');
    const chips = [...(go ? go.querySelectorAll('.go-chips .go-chip') : [])].map((c) => txt(c.querySelector('b')));
    const tm = (chips[1] || '').match(/(\d+):(\d+)/);
    const title = txt($('#go-title'));
    const run = Object.assign(r, {
      end: new Date(now).toISOString(), wallSecs: Math.round((now - Date.parse(r.start)) / 1000),
      outcome: outcomeOf(title), title,
      // 結算第一區固定順序：職業、時間、局內等級、擊殺數、擊殺王數
      result: { job: chips[0] || '', time: chips[1] || '', secs: tm ? +tm[1] * 60 + +tm[2] : null, level: +chips[2] || null, kills: +chips[3] || null, bosses: +chips[4] || null },
      exp: txt(go && go.querySelector('.go-exp')),
      loot: [...(go ? go.querySelectorAll('.go-items img') : [])].map((i) => ({ name: i.title, q: +((i.className.match(/q(\d)/) || [])[1] || 0) })),
      cards: [...(go ? go.querySelectorAll('.go-cards img') : [])].map((i) => i.title),
      serverLoot: txt(go && go.querySelector('#go-server-loot')),
      dps: [...(go ? go.querySelectorAll('.go-dps .dps-row') : [])].map((d) => ({ key: d.dataset.key || '', name: d.title || '', text: txt(d) })),
      dpsTotal: txt(go && go.querySelector('.go-box-title b')),
      got: Object.assign({}, st.got), treasure: Math.round(st.treasure), deity: st.deityLock || '', items: st.items || [],
      counts: ['pick', 'banish', 'reroll', 'focus'].reduce((o, k) => (o[k] = r.picks.filter((p) => p.kind === k).length, o), {}),
    });
    post({ type: 'run', run });
  }
  // 使用者自己選卡（滑鼠點卡片、按 1~5、點放逐／專注／重抽）。只認 isTrusted：小幫手自己送的點擊不算，免得重複記
  document.addEventListener('click', (e) => {
    if (!e.isTrusted || !e.target.closest) return;
    const act = e.target.closest('#picker .pk-act');
    if (act) {
      const t = act.textContent + ' ' + act.title;
      const kind = /放逐|Banish/i.test(t) ? 'banish' : /專注|Focus/i.test(t) ? 'focus' : /重抽/.test(t) ? 'reroll' : '';
      const slot = act.closest('.pk-slot');
      if (kind) recPick(kind, slot && slot.querySelector('.pk-card'), { manual: true });
      return;
    }
    const c = e.target.closest('#pk-cards .pk-card');
    if (c) recPick('pick', c, { manual: true });
  }, true);
  document.addEventListener('keydown', (e) => {
    if (!e.isTrusted || !/^[1-5]$/.test(e.key) || !visible($('#picker'))) return;
    const c = document.querySelectorAll('#pk-cards .pk-card')[+e.key - 1];
    if (c) recPick('pick', c, { manual: true });
  }, true);

  // ================= 主迴圈 =================
  function step() {
    if (!document.body) return; // 注入得早，頁面還在載入
    const now = Date.now();
    detectJob();
    // 記住遠征視窗裡選的地圖（出發後用來查頭目時間）
    if (visible($('#stages'))) {
      st.stagesSeenAt = now;
      const sel = stageCards().find((c) => c.classList.contains('sel'));
      if (sel && stageName(sel) !== cfg.lastStage) { cfg.lastStage = stageName(sel); save(); }
    }
    const running = inRun();
    if (running && !st.wasInRun) st.runStage = now - (st.stagesSeenAt || 0) < 60000 ? cfg.lastStage : '';

    // 對局紀錄：放在暫停判斷之前，暫停小幫手自己玩也照樣記
    const diffDlg = $('#npc-dialog');
    if (diffDlg && visible(diffDlg) && diffDlg.querySelector('.diff-row')) { st.diffSeenAt = now; st.diffRows = readDiff(diffDlg); }
    if (visible($('#festival-play'))) st.festSeenAt = now;
    if (running && !st.recIn) recStart(now);
    st.recIn = running;
    if (running && now - (st.recSampleAt || 0) >= 1000) { st.recSampleAt = now; recSample(now); }
    const endBtn = $('#btn-restart');
    if (visible(endBtn) && !endBtn.disabled) {
      if (!st.recEndAt) st.recEndAt = now;
      if (st.rec && now - st.recEndAt >= 1000) recFinish(now); // 等 1 秒讓結算畫面填完
    } else st.recEndAt = 0;

    if (cfg.paused) { st.wasInRun = running; st.action = '已暫停'; return render(); }
    if (running && !st.wasInRun) {
      st.zoomAt = now + 1200; st.zoomed = false; st.mmZoomed = false; st.meView = null; st.mapH = null; st.cand = new Map(); st.lastTileKey = '';
      st.runStart = now; st.got = { loot: 0, ball: 0, chest: 0 }; st.seenP = new WeakSet(); st.doneP = new WeakSet(); // 進野外 1.2 秒後把鏡頭拉遠（等畫面載好）
      st.npcDone = false; st.npcTalked = false; st.picked = new Set(); st.focused = new Set(); st.treasure = 0; st.deityLock = '';
    }
    st.wasInRun = running;

    // 「選擇大廳」視窗 → 按「單人大廳」
    if (on('soloLobby')) {
      const solo = [...document.querySelectorAll('button')].find((b) => visible(b) && /^\s*單人大廳\s*$/.test(b.textContent));
      if (solo) {
        st.action = '選單人大廳';
        if (!st.soloAt) st.soloAt = now;
        if (now - st.soloAt > 500) { solo.click(); st.soloAt = 0; }
        return render();
      }
      st.soloAt = 0;
    }

    // 結算
    const back = $('#btn-restart');
    if (visible(back) && !back.disabled) {
      if (!st.returnSeenAt) {
        st.returnSeenAt = now; cfg.runs++;
        // 戰績：地圖、時間、撿到的王掉落物／扭蛋數
        cfg.stats = (cfg.stats || []).concat([{ t: new Date().toLocaleString(), stage: st.runStage || '?', secs: Math.round((now - (st.runStart || now)) / 1000),
          loot: st.got.loot, balls: st.got.ball, chests: st.got.chest }]).slice(-50);
        save();
      }
      // 對局紀錄還沒讀完結算畫面前不按返回（返回後結算畫面就沒了）
      if (on('autoReturn')) { st.action = '準備返回角色'; if (now - st.returnSeenAt > T.returnDelay && !st.rec) back.click(); }
      return render();
    }
    st.returnSeenAt = 0;

    // 開始遊戲 / 前往野外
    const fp = $('#festival-play');
    const goBtn = [...document.querySelectorAll('#npc-options button')].find((b) => /前往野外/.test(b.textContent) && visible(b));
    const dlg = $('#npc-dialog');
    const expeDlg = goBtn && dlg && dlg.querySelector('.diff-row') && now - st.stagesSeenAt < 30000;
    let btn = null;
    if (!runLimitReached()) {
      if (goBtn && expeDlg && ex('expeEnter')) btn = goBtn;
      else if (goBtn && !expeDlg && on('autoEvent') && now - st.festAt < 10000) btn = goBtn;
      else if (visible(fp) && !fp.disabled && on('autoEvent')) btn = fp;
    } else if (goBtn || visible(fp)) st.action = '已達局數上限，停止自動開始';
    if (btn) {
      st.action = btn === fp ? '準備開始遊戲' : expeDlg ? '④ 準備進入關卡' : '準備前往野外';
      if (!st.startSeenAt) st.startSeenAt = now;
      if (expeDlg && !st.diffDone) { applyDifficulty(dlg); st.diffDone = true; st.diffRows = readDiff(dlg); }
      if (now - st.startSeenAt > T.startDelay) { if (btn === fp) st.festAt = now; btn.click(); st.startSeenAt = 0; }
      return render();
    }
    st.startSeenAt = 0;
    if (!goBtn) st.diffDone = false;

    // 進野外：滾輪往下把鏡頭拉到最遠（看得更廣）（遊戲每格滾輪 deltaY×0.001，送 10 次 +300 一定縮到底）
    // 小地圖拉到最遠（看得到整張地圖，才知道地圖中心在哪）
    if (running && on('stayCenter') && !st.mmZoomed && now > (st.zoomAt || 0)) {
      const minus = [...document.querySelectorAll('#minimap .mm-zoom button')].find((b) => b.textContent.trim() === '−' || b.textContent.trim() === '-');
      if (minus) for (let i = 0; i < 5; i++) minus.click();
      st.mmZoomed = true;
    }
    if (running && on('zoomOut') && !st.zoomed && now > (st.zoomAt || 0)) {
      const c = $('canvas.ro-cursor') || $('canvas.world3d');
      if (c) {
        const r = c.getBoundingClientRect();
        for (let i = 0; i < 10; i++)
          c.dispatchEvent(new WheelEvent('wheel', { deltaY: 300, bubbles: true, cancelable: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2 }));
        st.zoomed = true;
      }
    }

    // 選卡
    const picker = $('#picker');
    if (visible(picker) && !picker.classList.contains('closing') && $('#pk-cards .pk-card')) { pickerStep(now); return render(); }
    st.pickerSeenAt = 0;

    if (!running) { lobbyStep(now); return render(); }
    fieldStep(now);
    render();
  }

  // ================= 狀態回報（給 exe 面板） =================
  // 原本網頁面板的 render()：改成把要顯示的文字整理好送給 exe；內容沒變就不送，避免每 250ms 灌訊息
  let lastStatus = '';
  function render() {
    let nx = '';
    if (inRun()) {
      const nb = nextBoss(), tm = $('#timer');
      if (tm && tm.classList.contains('mvp')) nx = 'MVP 戰鬥中' + (st.runStage === '斐揚洞穴' ? '（注意躲鐘）' : '');
      else if (tm && tm.classList.contains('loot')) nx = '撿戰利品倒數中！';
      else if (nb) nx = (st.runStage || '') + '｜下一隻：' + nb.name + '（' + nb.kind + '）' + fmt(nb.left) + ' 後' +
        (st.runStage === '斐揚洞穴' && nb.kind === 'MVP' && nb.left <= 10 ? '　準備躲鐘！' : '');
      else if (!st.runStage) nx = '（地圖不明：從遠征出發才查得到頭目時間）';
    }
    const status = {
      runs: cfg.runs, runLimit: cfg.runLimit ? cfg.runLimitN : 0, treasure: Math.round(st.treasure), next: nx,
      action: (st.action || '待機') + (st.bankNote && inRun() ? ' ' + st.bankNote : ''), paused: !!cfg.paused,
    };
    const s = JSON.stringify(status);
    if (s === lastStatus) return;
    lastStatus = s;
    post({ type: 'status', status });
  }

  // ================= 和 exe 溝通 =================
  let timer = 0, timerMs = 0;
  // 主迴圈間隔改了才重設（setInterval 的間隔建立後就固定，改 cfg 不會自己生效）
  function restartTimer() {
    if (!timer || timerMs === T.tick) return;
    clearInterval(timer);
    timerMs = T.tick;
    timer = setInterval(step, timerMs);
  }
  host.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || typeof m !== 'object') return;
    switch (m.type) {
      // 頁面載入後 exe 帶入存檔的設定 → 才開始跑主迴圈（沒收到 init 前絕不 save，免得預設值蓋掉存檔）
      case 'init':
        applyStored(m.cfg, false);
        save();
        lastStatus = '';
        if (!timer) { timerMs = T.tick; timer = setInterval(step, timerMs); }
        break;
      // 面板改了某些設定
      case 'set':
        Object.assign(cfg, m.patch || {});
        clampTimes();
        st.npcDone = false; st.npcTalked = false; // 跟原面板一樣：改了規則就重新找 NPC
        restartTimer();
        save();
        break;
      // 匯入設定：整份換掉
      case 'replace':
        applyStored(m.cfg, true);
        restartTimer();
        save();
        break;
    }
  });

  // F8 暫停、F9 顯示／隱藏 exe 的面板（焦點在遊戲畫面時，按鍵只有網頁收得到）
  window.addEventListener('keydown', (e) => {
    if (e.key === 'F8' && timer) { cfg.paused = !cfg.paused; save(); e.preventDefault(); }
    if (e.key === 'F9') { post({ type: 'togglePanel' }); e.preventDefault(); }
  });

  // 告訴 exe 規則清單和遊戲資料，面板照這個產生
  post({ type: 'hello', schema: {
    version: '8.0', tabs: TABS, rules: RULES, jobSkills: JOB_SKILLS, jobBase: JOB_BASE,
    deityAttr: DEITY_ATTR, items: ITEMS, skStates: SK_STATES,
  } });
})();
