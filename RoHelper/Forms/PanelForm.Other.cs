using System.Text.Json;
using System.Text.Json.Nodes;

using RoHelper.Storage;

namespace RoHelper.Forms;

/// <summary>其他分頁：設定備份、選卡紀錄、戰績、局數</summary>
public sealed partial class PanelForm
{
    private readonly Label _logCount = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(6, 6, 0, 0) };
    private readonly Label _statsLabel = new() { AutoSize = true };
    private readonly Label _runsDone = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    private readonly Label _runsRecorded = new() { AutoSize = true };
    private string _statsSig = "";

    private void BuildOtherTab(FlowLayoutPanel page)
    {
        page.Controls.Add(Heading("設定備份"));
        var backup = Row();
        backup.Controls.Add(Button("匯出設定（複製）", ExportConfig));
        backup.Controls.Add(Button("匯入設定", ImportConfig));
        page.Controls.Add(backup);
        page.Controls.Add(Note("Tampermonkey 版面板「匯出設定」複製出來的內容，也可以直接貼進「匯入設定」。"));
        var sync = Row();
        sync.Controls.Add(Button("套用到其他分身…", PushToProfiles));
        page.Controls.Add(sync);
        page.Controls.Add(Note("把這個視窗的選卡、走位等規則複製給其他分身（正在執行的幾秒內生效）。不會動到對方的局數、戰績、活動／遠征選擇和局數上限。"));

        page.Controls.Add(Heading("紀錄"));
        var log = Row();
        log.Controls.Add(Button("複製紀錄", () => CopyText(_log.ToText(), "紀錄")));
        log.Controls.Add(Button("清除紀錄", _log.Clear));
        log.Controls.Add(_logCount);
        page.Controls.Add(log);
        UpdateLogCount();

        page.Controls.Add(Heading("戰績（最近 10 局）"));
        _statsLabel.MaximumSize = new Size(ContentWidth, 0);
        page.Controls.Add(_statsLabel);
        var stats = Row();
        stats.Controls.Add(Button("複製全部戰績", () => CopyText(string.Join("\n", Stats().Select(FormatStat)), "戰績")));
        stats.Controls.Add(Button("清除戰績", () => Send("stats", new JsonArray())));
        page.Controls.Add(stats);

        page.Controls.Add(Heading("完整對局紀錄（分析用）"));
        page.Controls.Add(_runsRecorded);
        page.Controls.Add(Note("每局自動記錄（暫停小幫手自己玩也會記）：地圖、難度、職業、結果、等級、擊殺、每次選卡、各技能傷害、獲得物品。存在資料夾裡的 runs.jsonl。"));
        UpdateRunsCount();

        var runs = Row();
        runs.Controls.Add(_runsDone);
        var reset = new LinkLabel { Text = "歸零", AutoSize = true, Margin = new Padding(S(4), S(4), 0, 0) };
        reset.LinkClicked += (_, _) => Send("runs", 0);
        runs.Controls.Add(reset);
        page.Controls.Add(runs);
    }

    private void ApplyOther()
    {
        var runs = (int)_config.GetNumber("runs");
        var limit = _config.GetBool("runLimit") ? " / " + (int)_config.GetNumber("runLimitN") : "";
        _runsDone.Text = $"已完成 {runs}{limit} 局";

        var sig = _config.Current["stats"]?.ToJsonString() ?? "";
        if (sig == _statsSig)
            return;
        _statsSig = sig;
        var last10 = Stats().TakeLast(10).Reverse().Select(FormatStat).ToList();
        _statsLabel.Text = last10.Count > 0 ? string.Join("\n", last10) : "（還沒有）";
    }

    private void UpdateLogCount() => _logCount.Text = $"{_log.Count} 筆";

    private void UpdateRunsCount() => _runsRecorded.Text = $"已記錄 {_runs.Count} 局";

    private List<JsonObject> Stats()
        => (_config.Current["stats"] as JsonArray ?? []).OfType<JsonObject>().ToList();

    private static string FormatStat(JsonObject x)
    {
        var secs = x["secs"] is JsonValue v && v.TryGetValue(out int s) ? s : 0;
        return $"{x["result"] ?? "？"}｜{x["t"]}｜{x["stage"]}｜{secs / 60}分{secs % 60}秒";
    }

    private Button Button(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, S(2), S(4), S(2)) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void CopyText(string text, string label)
    {
        if (text.Length == 0)
        {
            MessageBox.Show(this, $"{label}是空的", Text);
            return;
        }
        Clipboard.SetText(text);
        MessageBox.Show(this, $"{label}已複製到剪貼簿", Text);
    }

    private void ExportConfig() => CopyText(_config.Current.ToJsonString(), "設定");

    /// <summary>
    /// 匯入設定。遊戲頁面開著時交給 JS（JS 會清回預設再套用、處理舊版欄位，然後回傳存檔）；
    /// 頁面還沒載入時直接寫檔，等頁面載入時 init 帶過去。
    /// </summary>
    private void ImportConfig()
    {
        var text = PromptText("貼上之前匯出的設定：");
        if (string.IsNullOrWhiteSpace(text))
            return;

        JsonObject? cfg;
        try
        {
            cfg = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            cfg = null;
        }
        if (cfg is null)
        {
            MessageBox.Show(this, "格式不對，沒有匯入", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_automation.Schema is not null)
            _automation.SendReplace(cfg);
        else
            _config.Replace(cfg);
        MessageBox.Show(this, "已匯入", Text);
    }

    /// <summary>選要套用的分身（預設全勾），送出 config.pending.json（見 <see cref="ConfigSync"/>）</summary>
    private void PushToProfiles()
    {
        var others = AppPaths.AllProfiles().Where(p => p != AppPaths.Profile).ToList();
        if (others.Count == 0)
        {
            MessageBox.Show(this, "還沒有其他分身。可以從遊戲視窗選單「多開總覽 → 新增分身」建立。", Text);
            return;
        }
        using var dlg = new Form
        {
            Text = "套用到其他分身",
            Font = Font,
            Size = new Size(S(340), S(300)),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };
        var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        foreach (var p in others)
            list.Items.Add(AppPaths.DisplayName(p), true);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(S(4)) };
        var ok = new Button { Text = "套用", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        dlg.Controls.Add(list);
        dlg.Controls.Add(bar);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        var targets = list.CheckedIndices.Cast<int>().Select(i => others[i]).ToList();
        foreach (var p in targets)
            ConfigSync.Push(_config.Current, AppPaths.ProfileDirOf(p));
        if (targets.Count > 0)
            MessageBox.Show(this, $"已送出給：{string.Join("、", targets.Select(AppPaths.DisplayName))}。\n正在執行的分身幾秒內生效，沒在執行的下次開啟時生效。", Text);
    }

    private string? PromptText(string prompt)
    {
        using var dlg = new Form
        {
            Text = prompt,
            Size = new Size(S(520), S(360)),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = Font,
        };
        var box = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(S(4)) };
        var ok = new Button { Text = "匯入", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        bar.Controls.Add(cancel);
        bar.Controls.Add(ok);
        dlg.Controls.Add(box);
        dlg.Controls.Add(bar);
        dlg.AcceptButton = null; // 多行文字框要能按 Enter 換行
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }
}
