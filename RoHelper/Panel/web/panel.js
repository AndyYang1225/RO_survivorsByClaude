'use strict';
// 新版控制面板。C# 送來 schema／config／status／window／saved（見 PanelForm.Web.cs），這裡只負責畫面。
// 改設定一律送回 C#（patch 或指令），畫面永遠照 C# 回傳的 config 重畫，不自己保存設定（同舊面板的單向資料流）。
// 目前頁面存在 localStorage，不存進設定：每次改設定引擎都會重設大廳流程，切個分頁不該影響自動化。

const $ = id => document.getElementById(id);
const $main = $('main');
const $nav = $('nav');

const state = {
  schema: null,
  cfg: {},
  status: {},
  topMost: false,
  page: load('page', 'overview'),
  sub: load('sub', 'rules'),
};

// 左側導覽。tab＝這一頁顯示 schema 裡哪個分頁的規則
const PAGES = [
  { id: 'overview', icon: '📊', name: '總覽' },
  { id: 'pick', icon: '🃏', name: '選卡' },
  { id: 'walk', icon: '🚶', name: '走位', tab: '走位' },
  { id: 'start', icon: '🗺', name: '出發', tab: '出發' },
  { id: 'records', icon: '📜', name: '紀錄' },
  { id: 'other', icon: '⚙', name: '其他' },
];
// 選卡頁的小分頁（原本的升級、技能、神祇、道具四個分頁）
const PICK_SUBS = [
  { id: 'rules', name: '規則', tab: '升級' },
  { id: 'skills', name: '技能', tab: '技能' },
  { id: 'deity', name: '神祇', tab: '神祇' },
  { id: 'items', name: '道具', tab: '道具' },
];
// 上面已經安排位置的 schema 分頁；以後新增的分頁自動補在導覽最後
const KNOWN_TABS = new Set(['升級', '技能', '神祇', '道具', '走位', '出發', '其他']);
// 「其他」分頁裡改放到「紀錄」頁的規則
const RECORD_KEYS = new Set(['debugLog']);
// 規則說明裡的「 N 」或「 % 」是數字欄位的位置（同舊面板的 NumSlot）
const SLOT = / (N|%) /;

// ── 共用小工具 ──

function load(key, fallback) {
  try { return localStorage.getItem('panel.' + key) || fallback; } catch { return fallback; }
}

function save(key, value) {
  try { localStorage.setItem('panel.' + key, value); } catch { /* 存不了就算了，下次回到總覽 */ }
}

function send(msg) { window.chrome.webview.postMessage(msg); }

function patch(p) { send({ type: 'patch', patch: p }); }

/** 建立元素：props 的 on* 是事件，false／null／undefined 的屬性略過 */
function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props || {})) {
    if (v === undefined || v === null || v === false) continue;
    if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
    else if (k === 'class') el.className = v;
    else if (k === 'style') el.style.cssText = v;
    else if (k in el) el[k] = v;
    else el.setAttribute(k, v === true ? '' : v);
  }
  for (const c of kids.flat()) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : String(c));
  }
  return el;
}

const bool = k => state.cfg[k] === true;
const str = (k, d = '') => (typeof state.cfg[k] === 'string' ? state.cfg[k] : d);
const num = (k, d = 0) => (typeof state.cfg[k] === 'number' ? state.cfg[k] : d);
const list = k => (Array.isArray(state.cfg[k]) ? state.cfg[k].filter(x => typeof x === 'string') : []);
const jobList = (k, job) => (Array.isArray(state.cfg[k]?.[job]) ? state.cfg[k][job] : []);

/** 目前生效的職業（同 ConfigStore.CurrentJob）：選「自動」時用偵測到的 */
function currentJob() {
  const sel = str('jobSel', 'auto');
  return sel === 'auto' ? str('curJob') : sel;
}

function skillsOf(job) {
  return state.schema.jobs.find(j => j.job === job)?.skills || [];
}

function card(title, ...kids) {
  return h('section', { class: 'card' }, title ? h('h3', {}, title) : null, ...kids);
}

function btn(text, onclick) {
  return h('button', { class: 'btn', onclick }, text);
}

function note(text) {
  return h('div', { class: 'note' }, text);
}

// ── 頂端列 ──

function renderHeader() {
  const paused = bool('paused');
  const limit = bool('runLimit') ? num('runLimitN') : 0;
  $('dot').className = 'dot ' + (!state.schema ? '' : paused ? 'paused' : 'running');
  $('title').textContent = !state.schema
    ? '等待遊戲載入…'
    : `${paused ? '已暫停' : '運行中'} · 第 ${num('runs')}${limit ? ' / ' + limit : ''} 局`;
  $('pause').textContent = paused ? '▶ 繼續 F8' : '⏸ 暫停 F8';
  $('pause').classList.toggle('on', paused);
  $('pin').classList.toggle('on', state.topMost);
}

$('pause').addEventListener('click', () => send({ type: 'pause' }));
$('pin').addEventListener('click', () => send({ type: 'topMost', on: !state.topMost }));

// WebView2 有焦點時 WinForms 收不到按鍵，F8／F9 由這裡轉給 C#
document.addEventListener('keydown', e => {
  if (e.key === 'F8') {
    e.preventDefault();
    send({ type: 'pause' });
  } else if (e.key === 'F9') {
    e.preventDefault();
    send({ type: 'hide' });
  }
});

// ── 導覽 ──

function pages() {
  const extra = (state.schema?.tabs || [])
    .filter(t => !KNOWN_TABS.has(t))
    .map(t => ({ id: 'tab:' + t, icon: '📄', name: t, tab: t }));
  return [...PAGES, ...extra];
}

function currentPage() {
  return pages().find(p => p.id === state.page) || PAGES[0];
}

function renderNav() {
  const cur = currentPage().id;
  $nav.replaceChildren(...pages().map(p =>
    h('button', { class: 'nav-item' + (p.id === cur ? ' on' : ''), onclick: () => go(p.id) },
      h('span', { class: 'nav-icon' }, p.icon), h('span', {}, p.name))));
}

function go(id) {
  state.page = id;
  save('page', id);
  renderNav();
  render();
  $main.scrollTop = 0;
}

// ── 重畫 ──

// 正在輸入文字或數字時不重畫（會把打到一半的內容蓋掉），離開欄位後再補畫
let renderPending = false;

function typing() {
  const a = document.activeElement;
  return a && $main.contains(a) && a.tagName === 'INPUT' && (a.type === 'text' || a.type === 'number');
}

// 滑鼠按住時不重畫：重畫會換掉按鈕，mouseup 落空，這一下點擊就被吃掉
let pressed = false;
// 下拉選單（select）的彈出清單常吃掉 pointerup，pressed 會卡住、之後的重畫全被擋；select 不算按住
// （選完是 change，不會有按鈕被重畫換掉而吃掉點擊的問題）
window.addEventListener('pointerdown', e => {
  if (e.target instanceof Element && e.target.closest('select')) return;
  pressed = true;
}, true);
const release = () => {
  pressed = false;
  // 等 click 事件跑完再補畫
  setTimeout(() => {
    if (renderPending && !pressed && !typing()) {
      renderPending = false;
      render();
    }
  }, 0);
};
window.addEventListener('pointerup', release, true);
window.addEventListener('pointercancel', release, true);
// 選完下拉選單、或視窗失去焦點時 pointerup 可能不會來：一樣走放開流程，補畫就不會卡住
document.addEventListener('change', release, true);
window.addEventListener('blur', release);

// 目前頁面實際顯示的狀態欄位；沒變就不必重畫
function statusSig() {
  const s = state.status || {};
  if (state.page === 'overview') return JSON.stringify([s.action, s.treasure, s.next]);
  if (state.page === 'records') return JSON.stringify([s.logCount, s.runsRecorded]);
  return '';
}
let renderedSig = '';

function renderSoon() {
  if (pressed || typing()) {
    renderPending = true;
    return;
  }
  render();
}

$main.addEventListener('focusout', () => setTimeout(() => {
  // 按著滑鼠時留給 pointerup 補畫
  if (renderPending && !pressed && !typing()) {
    renderPending = false;
    render();
  }
}, 0));

function render() {
  renderedSig = statusSig();
  const top = $main.scrollTop;
  $main.replaceChildren(...pageContent());
  $main.scrollTop = top;
}

function pageContent() {
  if (!state.schema) return [h('div', { class: 'empty' }, '等待遊戲頁面載入…')];
  const p = currentPage();
  switch (p.id) {
    case 'overview': return overviewPage();
    case 'pick': return pickPage();
    case 'records': return recordsPage();
    case 'other': return otherPage();
    default: return rulesOf(p.tab);
  }
}

// ── 規則（依 schema 產生，同舊面板 AddRule） ──

/** 某個 schema 分頁的規則，依分組標題切成卡片；keep 可以濾掉某些規則 */
function rulesOf(tab, keep = () => true) {
  const cards = [];
  let cur = null;
  for (const r of state.schema.rules.filter(x => x.tab === tab)) {
    if (r.group) {
      cur = card(r.group);
      cards.push(cur);
      continue;
    }
    if (!r.key || !keep(r)) continue;
    if (!cur) {
      cur = card(null);
      cards.push(cur);
    }
    cur.append(...ruleRow(r));
  }
  // 規則都被濾掉的卡片不顯示
  return cards.filter(c => [...c.children].some(e => e.tagName !== 'H3'));
}

/** 說明裡的括號內容移到下面當灰色小字（括號裡有數字欄位的不拆） */
function splitNote(label) {
  const m = label.match(/^(.*?)（([^（）]*)）(.*)$/);
  if (!m || SLOT.test(m[2])) return { title: label, note: '' };
  return { title: (m[1] + m[3]).trim(), note: m[2] };
}

function ruleRow(r) {
  const indent = (r.sub || 0) * 18;

  // 下拉選單（遠征地圖的選項是遊戲裡看到的地圖清單，目前選的不在清單裡也要留著，同舊面板）
  if (r.options) {
    let opts = r.options;
    if (r.stageOptions) {
      const stages = list('expeList');
      const cur = str(r.key);
      if (cur && !stages.includes(cur)) stages.push(cur);
      opts = stages.map(n => [n, n]);
    }
    const sel = h('select', { onchange: e => patch({ [r.key]: e.target.value }) },
      opts.map(([v, t]) => h('option', { value: v }, t)));
    sel.value = str(r.key);
    return [h('div', { class: 'row', style: `margin-left:${indent}px` }, h('span', { class: 'label' }, r.label), sel)];
  }

  const { title, note: extra } = splitNote(r.label);
  const row = h('label', { class: 'row', style: `margin-left:${indent}px` });
  // 開關（時間設定這類 noCheck 的沒有）；互斥：開這條就把另一條關掉
  if (!r.noCheck) {
    row.append(h('input', {
      type: 'checkbox',
      class: 'switch',
      checked: bool(r.key),
      onchange: e => {
        const p = { [r.key]: e.target.checked };
        if (e.target.checked && r.excl) p[r.excl] = false;
        patch(p);
      },
    }));
  }
  // 數字欄位放在說明文字裡「 N 」的位置
  const m = r.num != null ? title.match(SLOT) : null;
  if (m) {
    const before = title.slice(0, m.index);
    const after = (m[1] === '%' ? '% ' : '') + title.slice(m.index + m[0].length);
    const input = h('input', {
      type: 'number',
      class: 'num',
      min: r.min,
      max: r.max,
      value: Math.min(r.max, Math.max(r.min, num(r.key + 'N', r.min))),
      onchange: e => {
        const v = Math.round(Number(e.target.value));
        if (!Number.isFinite(v)) return;
        const c = Math.min(r.max, Math.max(r.min, v));
        // 設定值沒變就不會重畫，欄位要自己改回夾過的值
        e.target.value = c;
        patch({ [r.key + 'N']: c });
      },
    });
    row.append(h('span', { class: 'label' }, before), input, h('span', { class: 'label' }, after));
  } else {
    row.append(h('span', { class: 'label' }, title));
  }

  const out = [row];
  const sub = indent + (r.noCheck ? 0 : 42);
  if (extra) out.push(h('div', { class: 'note', style: `margin-left:${sub}px` }, extra));
  if (r.text) {
    out.push(h('input', {
      type: 'text',
      class: 'text',
      value: str(r.key + 'T'),
      style: `margin-left:${sub}px`,
      onchange: e => patch({ [r.key + 'T']: e.target.value.trim() }),
    }));
  }
  return out;
}

// ── 總覽（第 1 階段：先放舊面板上方的三行資訊，儀表板是第 2 階段） ──

function overviewPage() {
  const s = state.status;
  const action = String(s.action || '').replace('［視覺］', '');
  return [
    card('現在在做什麼', h('div', { class: 'now' }, action || '—')),
    card('本局',
      h('div', { class: 'kv' }, h('span', {}, '尋寶率'), h('b', {}, `+${s.treasure ?? 0}%`)),
      s.next ? h('div', { class: 'next' }, s.next) : null),
  ];
}

// ── 選卡 ──

function pickPage() {
  const tabs = h('div', { class: 'subtabs' }, PICK_SUBS.map(s =>
    h('button', {
      class: 'subtab' + (s.id === state.sub ? ' on' : ''),
      onclick: () => {
        state.sub = s.id;
        save('sub', s.id);
        render();
        $main.scrollTop = 0;
      },
    }, s.name)));
  const sub = PICK_SUBS.find(s => s.id === state.sub) || PICK_SUBS[0];
  const body = rulesOf(sub.tab);
  if (sub.id === 'skills') body.push(skillsCard());
  if (sub.id === 'deity') body.push(deityCard());
  if (sub.id === 'items') body.push(...itemCards());
  return [tabs, ...body];
}

function jobRow() {
  const sel = h('select', { onchange: e => patch({ jobSel: e.target.value }) },
    h('option', { value: 'auto' }, '自動（目前角色）'),
    state.schema.jobs.map(j => h('option', { value: j.job }, j.job)));
  sel.value = str('jobSel', 'auto');
  const cur = str('curJob');
  const hint = str('jobSel', 'auto') === 'auto' ? (cur ? '→ ' + cur : '（尚未偵測）') : '';
  return h('div', { class: 'row' }, h('span', { class: 'label' }, '職業：'), sel, h('span', { class: 'note inline' }, hint));
}

function skillsCard() {
  const job = currentJob();
  const c = card('技能', jobRow(), note('優先的順序＝設定的先後（數字＝順位）。第 1 優先出現就選，其餘看稀有度。'));
  if (!job) {
    c.append(note('回選角畫面點一下角色，或手動選職業'));
    return c;
  }
  const s = state.cfg.skills?.[job] || {};
  const arr = k => (Array.isArray(s[k]) ? s[k] : []);
  // 只用來顯示目前狀態（同 SkillSetting.StateOf）；改狀態的計算在 C#（PanelActions）
  const stateOf = n => arr('after').includes(n) ? 'after'
    : arr('focus').includes(n) && arr('order').includes(n) ? 'focus'
    : arr('order').includes(n) ? 'prio'
    : arr('ban').includes(n) ? 'ban'
    : arr('skip').includes(n) ? 'skip'
    : '';
  for (const n of skillsOf(job)) {
    const rank = arr('order').indexOf(n);
    const sel = h('select', { onchange: e => send({ type: 'setSkill', job, skill: n, state: e.target.value }) },
      state.schema.skillStates.map(([v, t]) => h('option', { value: v }, t)));
    sel.value = stateOf(n);
    const name = (rank >= 0 ? `${rank + 1}. ` : '') + n + (rank >= 0 && arr('focus').includes(n) ? ' ★' : '');
    c.append(h('div', { class: 'row skill' + (rank >= 0 ? ' ranked' : '') }, h('span', { class: 'label' }, name), sel));
  }
  return c;
}

function deityCard() {
  const job = currentJob();
  const c = card('神祇', jobRow(), note('依勾選順序優先（數字＝順位）；紅色 ✕＝強制不選'));
  if (!job) {
    c.append(note('回選角畫面點一下角色，或手動選職業'));
    return c;
  }
  const prio = jobList('deity', job);
  const ban = jobList('deityBan', job);
  const grid = names => h('div', { class: 'grid' }, names.map(n => {
    const rank = prio.indexOf(n);
    const banned = ban.includes(n);
    return h('div', { class: 'cell' + (banned ? ' banned' : rank >= 0 ? ' ranked' : '') },
      h('label', {},
        h('input', { type: 'checkbox', checked: rank >= 0, disabled: banned, onchange: e => send({ type: 'deity', job, name: n, on: e.target.checked }) }),
        (rank >= 0 ? `${rank + 1}. ` : '') + n),
      h('label', { class: 'x', title: '強制不選' },
        h('input', { type: 'checkbox', checked: banned, onchange: e => send({ type: 'deityBan', job, name: n, on: e.target.checked }) }),
        '✕'));
  }));
  c.append(
    h('h4', {}, '技能神祇'), grid(skillsOf(job)),
    h('h4', {}, '屬性神祇'), grid(state.schema.deityAttr),
    h('div', { class: 'btns' }, h('button', { class: 'link', onclick: () => send({ type: 'deityClear', job }) }, '清除這個職業')));
  return c;
}

function itemCards() {
  const lists = { itemPrio: list('itemPrio'), itemFocus: list('itemFocus'), itemBan: list('itemBan') };
  const toggle = (name, key, text, title, cls) => h('label', { class: cls, title },
    h('input', { type: 'checkbox', checked: lists[key].includes(name), onchange: e => send({ type: 'item', list: key, name, on: e.target.checked }) }),
    text);
  const cards = [card(null, note('左邊勾選＝優先選（同時出現選最稀有的）；★＝出現時先按「專注」；紅色 ✕＝強制不選。尋寶率滿了以後，尋寶道具一律跳過。'))];
  for (const g of state.schema.items) {
    cards.push(card(g.category, h('div', { class: 'grid' }, g.names.map(n => h('div', { class: 'cell' },
      toggle(n, 'itemPrio', n),
      toggle(n, 'itemFocus', '★', '出現時先按「專注」', 'star'),
      toggle(n, 'itemBan', '✕', '強制不選', 'x'))))));
  }
  return cards;
}

// ── 紀錄 ──

function recordsPage() {
  const s = state.status;
  const stats = (Array.isArray(state.cfg.stats) ? state.cfg.stats : []).filter(x => x && typeof x === 'object');
  // 同舊面板 FormatStat
  const fmt = x => {
    const secs = Number.isInteger(x.secs) ? x.secs : 0;
    return `${x.t ?? ''}｜${x.stage ?? ''}｜${Math.floor(secs / 60)}分${secs % 60}秒｜王掉落 ${x.loot ?? ''}｜扭蛋 ${x.balls ?? ''}｜寶箱 ${x.chests ?? ''}`;
  };
  const last10 = stats.slice(-10).reverse();
  const limit = bool('runLimit') ? ` / ${num('runLimitN')}` : '';
  const debugRule = state.schema.rules.find(r => r.key === 'debugLog');
  return [
    card('戰績（最近 10 局）',
      last10.length ? h('ul', { class: 'stats' }, last10.map(x => h('li', {}, fmt(x)))) : note('（還沒有）'),
      h('div', { class: 'btns' },
        btn('複製全部戰績', () => send({ type: 'copyStats' })),
        btn('清除戰績', () => patch({ stats: [] })))),
    card('局數',
      h('div', { class: 'row' },
        h('span', { class: 'label' }, `已完成 ${num('runs')}${limit} 局`),
        h('button', { class: 'link', onclick: () => patch({ runs: 0 }) }, '歸零'))),
    card('選卡紀錄',
      ...(debugRule ? ruleRow(debugRule) : []),
      h('div', { class: 'btns' },
        btn('複製紀錄', () => send({ type: 'copyLog' })),
        btn('清除紀錄', () => send({ type: 'clearLog' })),
        h('span', { class: 'note inline' }, `${s.logCount ?? 0} 筆`))),
    card('完整對局紀錄（分析用）',
      h('div', {}, `已記錄 ${s.runsRecorded ?? 0} 局`),
      note('每局自動記錄（暫停小幫手自己玩也會記）：地圖、難度、職業、結果、等級、擊殺、每次選卡、各技能傷害、獲得物品。存在資料夾裡的 runs.jsonl。')),
  ];
}

// ── 其他 ──

function otherPage() {
  return [
    ...rulesOf('其他', r => !RECORD_KEYS.has(r.key)),
    card('設定備份',
      h('div', { class: 'btns' },
        btn('匯出設定（複製）', () => send({ type: 'export' })),
        btn('匯入設定', () => send({ type: 'import' }))),
      note('Tampermonkey 版面板「匯出設定」複製出來的內容，也可以直接貼進「匯入設定」。'),
      h('div', { class: 'btns' }, btn('套用到其他分身…', () => send({ type: 'applyOthers' }))),
      note('把這個視窗的選卡、走位等規則複製給其他分身（正在執行的幾秒內生效）。不會動到對方的局數、戰績、活動／遠征選擇和局數上限。')),
  ];
}

// ── 已儲存提示 ──

let toastTimer = 0;

function toast() {
  $('toast').classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $('toast').classList.remove('show'), 1200);
}

// ── 收 C# 的訊息 ──

window.chrome.webview.addEventListener('message', e => {
  const m = e.data || {};
  switch (m.type) {
    case 'schema':
      state.schema = m.schema;
      renderHeader();
      renderNav();
      render();
      break;
    case 'config':
      state.cfg = m.config || {};
      renderHeader();
      renderSoon();
      break;
    case 'status':
      state.status = m.status || {};
      // 狀態一秒變好幾次，只有會顯示狀態的頁面才重畫
      if ((state.page === 'overview' || state.page === 'records') && statusSig() !== renderedSig) renderSoon();
      break;
    case 'window':
      state.topMost = !!m.topMost;
      renderHeader();
      break;
    case 'saved':
      toast();
      break;
  }
});

renderHeader();
renderNav();
render();
send({ type: 'ready' });
