// 從 engine.js 抽出規則清單與遊戲資料，輸出成 schema.json（格式同 JS 引擎 hello 訊息的 schema，另加 stages）。
// 用法：node tools/gen-schema.js RoHelper/Engine/engine.js > RoHelper/Engine/schema.json
// 改了 engine.js 的 RULES／JOB_SKILLS／ITEMS 等資料後要重新產生；BuiltInSchemaTests 會檢查兩邊有沒有對齊。
const fs = require('fs');
const src = fs.readFileSync(process.argv[2], 'utf8');
const a = src.indexOf('// ================= 遊戲資料');
const b = src.indexOf('// 各種等待時間');
if (a < 0 || b < 0) throw new Error('engine.js 找不到資料區段的標記註解');
const body = src.slice(a, b);
const schema = new Function(body + `
  return { version: '8.0', tabs: TABS, rules: RULES, jobSkills: JOB_SKILLS, jobBase: JOB_BASE,
           deityAttr: DEITY_ATTR, items: ITEMS, skStates: SK_STATES, stages: DEFAULT_STAGES };`)();
process.stdout.write(JSON.stringify(schema, null, 2) + '\n');
