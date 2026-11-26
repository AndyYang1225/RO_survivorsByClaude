// 改版重開時清空三隻的戰績、局數、完整對戰紀錄（使用者 2026-11-27：避免讀到舊版的資料）。
// 小幫手要先關掉（程式在跑時會把 config.json 蓋回去）；tools/restart.ps1 編譯成功後、重開前呼叫。
// 不直接刪：舊的 runs.jsonl 和 config.json 搬／複製到各帳號資料夾的 history\（檔名加時間），程式讀不到，等於清空。
// 用法：node tools/reset-stats.js            三隻都清
const fs = require('fs');
const path = require('path');

const root = path.join(process.env.APPDATA, 'RoHelper');
const dirs = [root, path.join(root, 'profiles', 'B'), path.join(root, 'profiles', 'C')];
const p2 = (n) => String(n).padStart(2, '0');
const d = new Date();
const stamp = `${d.getFullYear()}${p2(d.getMonth() + 1)}${p2(d.getDate())}-${p2(d.getHours())}${p2(d.getMinutes())}${p2(d.getSeconds())}`;

for (const dir of dirs) {
  if (!fs.existsSync(dir)) continue;
  const hist = path.join(dir, 'history');
  fs.mkdirSync(hist, { recursive: true });
  const notes = [];

  // 完整對戰紀錄：整份搬走（程式下一局會開新檔）
  const runs = path.join(dir, 'runs.jsonl');
  if (fs.existsSync(runs)) {
    const n = fs.readFileSync(runs, 'utf8').split('\n').filter((l) => l.trim()).length;
    fs.renameSync(runs, path.join(hist, `runs-${stamp}.jsonl`));
    notes.push(`對戰紀錄 ${n} 局`);
  }

  // 戰績（stats）、局數（runs）：存在 config.json，先備份再清
  const cfgPath = path.join(dir, 'config.json');
  if (fs.existsSync(cfgPath)) {
    const text = fs.readFileSync(cfgPath, 'utf8').replace(/^﻿/, '');
    const cfg = JSON.parse(text);
    fs.writeFileSync(path.join(hist, `config-${stamp}.json`), text);
    notes.push(`戰績 ${Array.isArray(cfg.stats) ? cfg.stats.length : 0} 筆`, `局數 ${cfg.runs ?? 0}`);
    cfg.stats = [];
    cfg.runs = 0;
    fs.writeFileSync(cfgPath, JSON.stringify(cfg, null, 2));
  }
  console.log(`${path.relative(root, dir) || '預設'}：清空 ${notes.join('、')}（舊的在 history\\*-${stamp}）`);
}
