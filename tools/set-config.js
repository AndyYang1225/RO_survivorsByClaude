// 小幫手關著時，直接改三隻的 config.json（程式在跑時改會被蓋掉；config.pending.json 又會濾掉玩法選擇的鍵，見 Storage/ConfigSync.cs）。
// tools/restart.ps1 -Set 在關掉之後、重開之前呼叫。改之前備份到各帳號的 history\config-set-<時間>.json。
// 用法：node tools/set-config.js runLimit=true runLimitN=5 autoExpe=false
//      值先當 JSON 解析（true、5、"文字"、[...]），失敗就當字串
const fs = require('fs');
const path = require('path');

const pairs = process.argv.slice(2).map((a) => {
  const i = a.indexOf('=');
  if (i <= 0) throw new Error(`格式要是 鍵=值：${a}`);
  const raw = a.slice(i + 1);
  let v;
  try { v = JSON.parse(raw); } catch { v = raw; }
  return [a.slice(0, i), v];
});
if (!pairs.length) { console.log('沒有要改的設定'); process.exit(0); }

const root = path.join(process.env.APPDATA, 'RoHelper');
const p2 = (n) => String(n).padStart(2, '0');
const d = new Date();
const stamp = `${d.getFullYear()}${p2(d.getMonth() + 1)}${p2(d.getDate())}-${p2(d.getHours())}${p2(d.getMinutes())}${p2(d.getSeconds())}`;

for (const dir of [root, path.join(root, 'profiles', 'B'), path.join(root, 'profiles', 'C')]) {
  const cfgPath = path.join(dir, 'config.json');
  if (!fs.existsSync(cfgPath)) continue;
  const text = fs.readFileSync(cfgPath, 'utf8').replace(/^﻿/, '');
  const cfg = JSON.parse(text);
  fs.mkdirSync(path.join(dir, 'history'), { recursive: true });
  fs.writeFileSync(path.join(dir, 'history', `config-set-${stamp}.json`), text);
  for (const [k, v] of pairs) cfg[k] = v;
  fs.writeFileSync(cfgPath, JSON.stringify(cfg, null, 2));
  console.log(`${path.relative(root, dir) || '預設'}：${pairs.map(([k, v]) => `${k}=${JSON.stringify(v)}`).join(' ')}`);
}
