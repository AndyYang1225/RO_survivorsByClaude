using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Storage;

namespace RoHelper.Forms;

/// <summary>技能、神祇、道具三個分頁（依職業的清單設定）</summary>
public sealed partial class PanelForm
{
    private readonly List<ComboBox> _jobCombos = [];
    private readonly List<Label> _jobNotes = [];
    private readonly FlowLayoutPanel _skillsBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly FlowLayoutPanel _deityBox = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly Dictionary<string, CheckBox> _itemPrio = [];
    private readonly Dictionary<string, CheckBox> _itemFocus = [];
    private readonly Dictionary<string, CheckBox> _itemBan = [];

    // 上次重建時的設定內容；沒變就不重建（重建會讓下拉選單失去焦點、畫面閃一下）
    private string _skillsSig = "";
    private string _deitySig = "";

    #region 職業選單（技能、神祇兩頁共用）

    private Control JobRow()
    {
        var row = Row();
        var combo = Combo(150);
        combo.Items.Add(new Choice("auto", "自動（目前角色）"));
        combo.Items.AddRange(_schema!.JobSkills.Select(j => (object)new Choice(j.Key, j.Key)).ToArray());
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (combo.SelectedItem is Choice c)
                Send("jobSel", c.Value);
        };
        var note = InlineText("");
        note.ForeColor = Color.DimGray;
        row.Controls.Add(InlineText("職業："));
        row.Controls.Add(combo);
        row.Controls.Add(note);
        _jobCombos.Add(combo);
        _jobNotes.Add(note);
        return row;
    }

    private void ApplyJob()
    {
        var jobSel = _config.GetString("jobSel", "auto");
        var curJob = _config.GetString("curJob");
        foreach (var combo in _jobCombos)
            SelectValue(combo, jobSel);
        foreach (var note in _jobNotes)
            note.Text = jobSel == "auto" ? (curJob.Length > 0 ? "→ " + curJob : "（尚未偵測）") : "";

        var job = _config.CurrentJob();
        var skillsSig = job + "|" + _config.Current["skills"]?[job]?.ToJsonString();
        if (skillsSig != _skillsSig)
        {
            _skillsSig = skillsSig;
            RebuildSkills(job);
        }
        var deitySig = job + "|" + _config.Current["deity"]?[job]?.ToJsonString() + "|" + _config.Current["deityBan"]?[job]?.ToJsonString();
        if (deitySig != _deitySig)
        {
            _deitySig = deitySig;
            RebuildDeity(job);
        }
    }

    #endregion

    #region 技能

    private void BuildSkillsTab(FlowLayoutPanel page)
    {
        page.Controls.Add(JobRow());
        page.Controls.Add(Note("優先的順序＝設定的先後（數字＝順位）。第 1 優先出現就選，其餘看稀有度。"));
        page.Controls.Add(_skillsBox);
    }

    private void RebuildSkills(string job)
    {
        _skillsBox.SuspendLayout();
        DisposeChildren(_skillsBox);
        if (job.Length == 0)
        {
            _skillsBox.Controls.Add(Note("回選角畫面點一下角色，或手動選職業"));
            _skillsBox.ResumeLayout();
            return;
        }

        var s = SkillSetting.From(_config.Current["skills"]?[job]);
        foreach (var name in _schema!.SkillsOf(job))
        {
            var row = Row();
            var rank = s.Order.IndexOf(name);
            var label = InlineText((rank >= 0 ? $"{rank + 1}. " : "") + name + (rank >= 0 && s.Focus.Contains(name) ? " ★" : ""));
            label.AutoSize = false;
            label.Width = S(200);
            label.Height = S(22);
            if (rank >= 0)
                label.Font = new Font(Font, FontStyle.Bold);
            var combo = Combo(170);
            combo.Items.AddRange(_schema.SkillStates.Cast<object>().ToArray());
            SelectValue(combo, s.StateOf(name));
            var skill = name;
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedItem is not Choice c || _applying)
                    return;
                // 以「目前設定」為準重新取一次，不用重建當下的舊快照
                var latest = SkillSetting.From(_config.Current["skills"]?[job]);
                latest.SetState(skill, c.Value);
                var all = _config.Current["skills"]?.DeepClone() as JsonObject ?? [];
                all[job] = latest.ToJson();
                Send("skills", all);
            };
            row.Controls.Add(label);
            row.Controls.Add(combo);
            _skillsBox.Controls.Add(row);
        }
        _skillsBox.ResumeLayout();
    }

    #endregion

    #region 神祇

    private void BuildDeityTab(FlowLayoutPanel page)
    {
        page.Controls.Add(JobRow());
        page.Controls.Add(Note("依勾選順序優先（數字＝順位）；紅色 ✕＝強制不選"));
        page.Controls.Add(_deityBox);
        var clear = new LinkLabel { Text = "清除這個職業", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        clear.LinkClicked += (_, _) =>
        {
            var job = _config.CurrentJob();
            if (job.Length > 0)
                Send("deity", WithJob("deity", job, []));
        };
        page.Controls.Add(clear);
    }

    private void RebuildDeity(string job)
    {
        _deityBox.SuspendLayout();
        DisposeChildren(_deityBox);
        if (job.Length == 0)
        {
            _deityBox.Controls.Add(Note("回選角畫面點一下角色，或手動選職業"));
            _deityBox.ResumeLayout();
            return;
        }

        var list = ConfigStore.ToStringList(_config.Current["deity"]?[job]);
        var ban = ConfigStore.ToStringList(_config.Current["deityBan"]?[job]);
        _deityBox.Controls.Add(Heading("技能神祇"));
        _deityBox.Controls.Add(DeityGrid(job, _schema!.SkillsOf(job), list, ban));
        _deityBox.Controls.Add(Heading("屬性神祇"));
        _deityBox.Controls.Add(DeityGrid(job, _schema.DeityAttr, list, ban));
        _deityBox.ResumeLayout();
    }

    private FlowLayoutPanel DeityGrid(string job, List<string> names, List<string> list, List<string> ban)
    {
        var grid = Row();
        foreach (var n in names)
        {
            var rank = list.IndexOf(n);
            var banned = ban.Contains(n);
            var cell = new FlowLayoutPanel { AutoSize = false, Width = S(215), Height = S(26), WrapContents = false, Margin = Padding.Empty };
            var prio = new CheckBox
            {
                Text = (rank >= 0 ? $"{rank + 1}. " : "") + n,
                Checked = rank >= 0,
                Enabled = !banned,
                AutoSize = true,
                Font = banned ? new Font(Font, FontStyle.Strikeout) : rank >= 0 ? new Font(Font, FontStyle.Bold) : Font,
            };
            var x = new CheckBox { Text = "✕", Checked = banned, AutoSize = true, ForeColor = Color.Red };
            var name = n;
            prio.CheckedChanged += (_, _) =>
            {
                var cur = ConfigStore.ToStringList(_config.Current["deity"]?[job]).Where(v => v != name).ToList();
                if (prio.Checked)
                    cur.Add(name);
                Send("deity", WithJob("deity", job, cur));
            };
            // ✕＝強制不選：勾了就從優先清單拿掉，寶箱出現這個神祇也絕對不選
            x.CheckedChanged += (_, _) =>
            {
                var b = ConfigStore.ToStringList(_config.Current["deityBan"]?[job]).Where(v => v != name).ToList();
                var patch = new JsonObject();
                if (x.Checked)
                {
                    b.Add(name);
                    var cur = ConfigStore.ToStringList(_config.Current["deity"]?[job]).Where(v => v != name);
                    patch["deity"] = WithJob("deity", job, cur);
                }
                patch["deityBan"] = WithJob("deityBan", job, b);
                Send(patch);
            };
            cell.Controls.Add(prio);
            cell.Controls.Add(x);
            grid.Controls.Add(cell);
        }
        return grid;
    }

    /// <summary>複製整個「職業 → 清單」物件，只換掉某個職業的清單（patch 要送整個 key）</summary>
    private JsonObject WithJob(string key, string job, IEnumerable<string> list)
    {
        var all = _config.Current[key]?.DeepClone() as JsonObject ?? [];
        all[job] = ToJsonArray(list);
        return all;
    }

    #endregion

    #region 道具

    private void BuildItemsTab(FlowLayoutPanel page, EngineSchema schema)
    {
        page.Controls.Add(Note("左邊勾選＝優先選（同時出現選最稀有的）；★＝出現時先按「專注」；紅色 ✕＝強制不選。尋寶率滿了以後，尋寶道具一律跳過。"));
        foreach (var (category, items) in schema.Items)
        {
            page.Controls.Add(Heading(category));
            var grid = Row();
            foreach (var n in items)
            {
                var cell = new FlowLayoutPanel { AutoSize = false, Width = S(215), Height = S(26), WrapContents = false, Margin = Padding.Empty };
                var prio = new CheckBox { Text = n, AutoSize = true };
                var focus = new CheckBox { Text = "★", AutoSize = true, ForeColor = Color.DarkGoldenrod };
                var ban = new CheckBox { Text = "✕", AutoSize = true, ForeColor = Color.Red };
                var tip = new ToolTip();
                tip.SetToolTip(focus, "出現時先按「專注」");
                tip.SetToolTip(ban, "強制不選");
                var name = n;
                prio.CheckedChanged += (_, _) => Send("itemPrio", Toggle("itemPrio", name, prio.Checked));
                focus.CheckedChanged += (_, _) => Send("itemFocus", Toggle("itemFocus", name, focus.Checked));
                ban.CheckedChanged += (_, _) => Send("itemBan", Toggle("itemBan", name, ban.Checked));
                cell.Controls.Add(prio);
                cell.Controls.Add(focus);
                cell.Controls.Add(ban);
                grid.Controls.Add(cell);
                _itemPrio[n] = prio;
                _itemFocus[n] = focus;
                _itemBan[n] = ban;
            }
            page.Controls.Add(grid);
        }
    }

    private JsonArray Toggle(string key, string name, bool on)
    {
        var list = _config.GetStringList(key).Where(x => x != name).ToList();
        if (on)
            list.Add(name);
        return ToJsonArray(list);
    }

    private void ApplyItems()
    {
        var prio = _config.GetStringList("itemPrio");
        var focus = _config.GetStringList("itemFocus");
        var ban = _config.GetStringList("itemBan");
        foreach (var (n, c) in _itemPrio)
            c.Checked = prio.Contains(n);
        foreach (var (n, c) in _itemFocus)
            c.Checked = focus.Contains(n);
        foreach (var (n, c) in _itemBan)
            c.Checked = ban.Contains(n);
    }

    #endregion

    private static void DisposeChildren(Control parent)
    {
        var old = parent.Controls.Cast<Control>().ToList();
        parent.Controls.Clear();
        foreach (var c in old)
            c.Dispose();
    }
}

/// <summary>
/// 某職業的技能設定（對應 JS 的 cfg.skills[職業]）。每個技能只會在一種狀態：
/// 優先（order）、優先＋專注（order＋focus）、優先・學到後放逐（order＋after）、直接放逐（ban）、不選（skip）、或都不是。
/// </summary>
public sealed class SkillSetting
{
    public List<string> Order { get; } = [];
    public List<string> After { get; } = [];
    public List<string> Ban { get; } = [];
    public List<string> Focus { get; } = [];
    public List<string> Skip { get; } = [];

    public static SkillSetting From(JsonNode? node)
    {
        var s = new SkillSetting();
        s.Order.AddRange(ConfigStore.ToStringList(node?["order"]));
        s.After.AddRange(ConfigStore.ToStringList(node?["after"]));
        s.Ban.AddRange(ConfigStore.ToStringList(node?["ban"]));
        s.Focus.AddRange(ConfigStore.ToStringList(node?["focus"]));
        s.Skip.AddRange(ConfigStore.ToStringList(node?["skip"]));
        return s;
    }

    /// <summary>同原面板的 state()：判斷順序有意義（after 優先於 focus 優先於 prio）</summary>
    public string StateOf(string n)
        => After.Contains(n) ? "after"
         : Focus.Contains(n) && Order.Contains(n) ? "focus"
         : Order.Contains(n) ? "prio"
         : Ban.Contains(n) ? "ban"
         : Skip.Contains(n) ? "skip"
         : "";

    /// <summary>
    /// 同原面板 select onchange 的邏輯。已經是優先的技能改成「優先＋專注」等時保留原本順位，不會掉到最後。
    /// </summary>
    public void SetState(string n, string v)
    {
        var wasPrio = Order.Contains(n);
        After.Remove(n);
        Ban.Remove(n);
        Focus.Remove(n);
        Skip.Remove(n);
        if (v == "skip")
            Skip.Add(n);
        if (v is "prio" or "after" or "focus")
        {
            if (!wasPrio)
                Order.Add(n);
            if (v == "after")
                After.Add(n);
            if (v == "focus")
                Focus.Add(n);
        }
        else
            Order.Remove(n);
        if (v == "ban")
            Ban.Add(n);
    }

    public JsonObject ToJson() => new()
    {
        ["order"] = Arr(Order),
        ["after"] = Arr(After),
        ["ban"] = Arr(Ban),
        ["focus"] = Arr(Focus),
        ["skip"] = Arr(Skip),
    };

    private static JsonArray Arr(List<string> list) => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
}
