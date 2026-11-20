using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RoHelper.Bridge;

namespace RoHelper.Forms;

/// <summary>一般規則（RULES 陣列裡有 k 的那些）：勾選、數字、文字、下拉</summary>
public sealed partial class PanelForm
{
    private readonly Dictionary<string, CheckBox> _checks = [];
    private readonly Dictionary<string, NumericUpDown> _nums = [];
    private readonly Dictionary<string, TextBox> _texts = [];
    private readonly Dictionary<string, ComboBox> _combos = [];

    /// <summary>選項是遠征地圖清單的下拉（清單會隨遊戲裡看到的地圖變動）</summary>
    private readonly List<ComboBox> _stageCombos = [];

    // 規則說明裡的「 N 」或「 % 」是數字框的位置（同原面板的 replace(/ N | % /)）
    [GeneratedRegex(" (N|%) ")]
    private static partial Regex NumSlot();

    private void AddRule(FlowLayoutPanel page, RuleDef r)
    {
        if (r.Group is not null)
        {
            page.Controls.Add(Heading(r.Group));
            return;
        }
        if (r.Key is not { } key)
            return;

        var indent = S(r.Sub switch { 2 => 36, 1 => 18, _ => 0 });

        // 下拉選單
        if (r.Options is not null)
        {
            var row = Row(indent == 0 ? S(18) : indent);
            var combo = Combo(200);
            combo.Items.AddRange(r.Options.Cast<object>().ToArray());
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedItem is Choice c)
                    Send(key, c.Value);
            };
            row.Controls.Add(InlineText(r.Label));
            row.Controls.Add(combo);
            page.Controls.Add(row);
            _combos[key] = combo;
            if (r.StageOptions)
                _stageCombos.Add(combo);
            return;
        }

        // 勾選（nocheck 的規則沒有，例如時間設定）
        var rowMain = Row(indent);
        CheckBox? check = null;
        if (!r.NoCheck)
        {
            var c = check = BareCheck();
            c.CheckedChanged += (_, _) =>
            {
                var patch = new JsonObject { [key] = c.Checked };
                // 互斥：勾這條就把另一條取消（燈籠節 vs 遠征）
                if (c.Checked && r.Excl is { } other)
                    patch[other] = false;
                Send(patch);
            };
            rowMain.Controls.Add(c);
            _checks[key] = c;
        }

        var m = r.Num is not null ? NumSlot().Match(r.Label) : Match.Empty;
        if (m.Success)
        {
            // 「MVP 出現前 N 秒先把…」→ [勾] MVP 出現前 [15] 秒先把…
            var num = new NumericUpDown
            {
                Minimum = r.Min,
                Maximum = r.Max,
                Width = S(64),
                Margin = new Padding(S(2), 0, S(2), S(2)),
            };
            num.ValueChanged += (_, _) => Send(key + "N", (int)num.Value);
            var right = (m.Groups[1].Value == "%" ? "% " : "") + r.Label[(m.Index + m.Length)..];
            rowMain.Controls.Add(InlineText(r.Label[..m.Index], check));
            rowMain.Controls.Add(num);
            rowMain.Controls.Add(InlineText(right, check));
            _nums[key + "N"] = num;
        }
        else
            rowMain.Controls.Add(InlineText(r.Label, check));
        page.Controls.Add(rowMain);

        // 勾選＋文字（例如「其他一出現就放逐」的關鍵字）
        if (r.Text is not null)
        {
            var box = new TextBox { Width = S(300), Margin = new Padding(indent + S(22), 0, 0, S(4)) };
            box.TextChanged += (_, _) => Send(key + "T", box.Text.Trim());
            page.Controls.Add(box);
            _texts[key + "T"] = box;
        }
    }

    private void ApplyRules()
    {
        foreach (var (key, check) in _checks)
            check.Checked = _config.GetBool(key);

        foreach (var (key, num) in _nums)
        {
            var v = Math.Clamp(_config.GetNumber(key, num.Minimum), num.Minimum, num.Maximum);
            if (num.Value != v)
                num.Value = v;
        }

        foreach (var (key, box) in _texts)
        {
            var v = _config.GetString(key);
            if (!box.Focused && box.Text != v)
                box.Text = v;
        }

        // 遠征地圖下拉：選項＝遊戲裡看到的地圖清單（目前選的不在清單裡也要留著，同原面板）
        foreach (var combo in _stageCombos)
        {
            var key = _combos.First(x => x.Value == combo).Key;
            var current = _config.GetString(key);
            var list = _config.GetStringList("expeList");
            if (current.Length > 0 && !list.Contains(current))
                list.Add(current);
            var existing = combo.Items.Cast<Choice>().Select(c => c.Value);
            if (!existing.SequenceEqual(list))
            {
                combo.Items.Clear();
                combo.Items.AddRange(list.Select(n => (object)new Choice(n, n)).ToArray());
            }
        }

        foreach (var (key, combo) in _combos)
            SelectValue(combo, _config.GetString(key));
    }
}
