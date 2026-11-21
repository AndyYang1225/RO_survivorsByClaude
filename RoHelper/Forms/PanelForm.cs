using System.Text.Json.Nodes;

using RoHelper.Bridge;
using RoHelper.Storage;

namespace RoHelper.Forms;

/// <summary>
/// 控制面板（獨立視窗，可以跟遊戲分開擺、單獨最小化）。
/// 介面不是寫死的：引擎就緒後送來規則清單（<see cref="EngineSchema"/>；JS 引擎的 hello 訊息，或視覺模式的內建 schema.json），面板照著產生分頁和控制項。
/// 資料流向是單向循環：使用者操作 → <see cref="IAutomation.SendPatch"/> → 引擎改 cfg → 存回 ConfigStore →
/// <see cref="ConfigStore.Changed"/> → <see cref="ApplyConfig"/> 把畫面對齊設定。
/// 刻意不在操作當下直接改畫面以外的狀態：所有顯示都以 JS 回傳的設定為準，兩邊才不會不同步。
/// 檔案拆分：本檔＝骨架與共用；Rules＝一般規則；Lists＝技能／神祇／道具；Other＝其他分頁。
/// </summary>
public sealed partial class PanelForm : Form
{
    private readonly ConfigStore _config;
    private readonly LogStore _log;
    private readonly RunStore _runs;
    private readonly IAutomation _automation;
    private readonly AppSettings _settings;

    private readonly CheckBox _pauseBox = new() { Text = "暫停 (F8)", AutoSize = true };
    private readonly CheckBox _topMostBox = new() { Text = "面板置頂", AutoSize = true };
    private readonly Label _runsLabel = new() { AutoSize = true, Text = "局數 0｜本局尋寶率 +0%" };
    private readonly Label _nextLabel = new() { AutoSize = true, ForeColor = Color.DarkOrange };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "→ 等待遊戲載入…" };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _waiting = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "等待遊戲頁面載入…" };

    private EngineSchema? _schema;

    /// <summary>
    /// 正在把設定值套到畫面上。這段期間控制項的 Changed 事件不能再送 patch，否則會形成「套用→送出→回傳→套用」的無限循環。
    /// </summary>
    private bool _applying;

    public PanelForm(ConfigStore config, LogStore log, RunStore runs, IAutomation automation, AppSettings settings)
    {
        _config = config;
        _log = log;
        _runs = runs;
        _automation = automation;
        _settings = settings;

        Text = AppPaths.Profile.Length == 0 ? "RO 小幫手 面板" : $"RO 小幫手 面板 [{AppPaths.Profile}]";
        Font = new Font("Microsoft JhengHei UI", FontSize);
        KeyPreview = true;
        ShowInTaskbar = true;
        MinimumSize = new Size(S(500), S(400));
        StartPosition = FormStartPosition.Manual;
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Bounds = AppSettings.OnScreen(settings.PanelBounds)
                 ?? new Rectangle(screen.Right - S(520), screen.Top + S(40), S(500), Math.Min(screen.Height - S(80), S(760)));
        TopMost = settings.PanelTopMost;

        BuildHeader();
        Controls.Add(_waiting);
        _waiting.BringToFront();

        _config.Changed += ApplyConfig;
        _log.Changed += UpdateLogCount;
        _runs.Changed += UpdateRunsCount;
        _automation.SchemaReceived += OnSchema;
        _automation.StatusReceived += ShowStatus;
    }

    /// <summary>內容區寬度（扣掉捲軸），換行用</summary>
    private int ContentWidth => S(440);

    /// <summary>面板字級（pt）。版面數值是以 9pt 設計的，<see cref="S"/> 會照比例一起放大，字變大時欄寬不會切到字</summary>
    private const float FontSize = 11f;

    private int S(int logical) => LogicalToDeviceUnits((int)Math.Round(logical * FontSize / 9f));

    /// <summary>暫停／繼續（F8）。遊戲視窗攔到 F8 時也呼叫這裡，兩個視窗的行為一致</summary>
    public void TogglePause() => Send("paused", !_config.GetBool("paused"));

    public void SaveWindowState()
    {
        _settings.PanelBounds = WindowBounds.From(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds);
        _settings.PanelVisible = Visible;
        _settings.PanelTopMost = TopMost;
    }

    /// <summary>按右上角 X 只是隱藏（遊戲裡按 F9 或選單「面板」叫回來）；結束程式由遊戲視窗負責</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.F8:
                TogglePause();
                e.Handled = true;
                break;
            case Keys.F9:
                Hide();
                e.Handled = true;
                break;
        }
    }

    #region 上方：暫停、狀態

    private void BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(S(6), S(4), S(6), S(4)),
        };
        var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        top.Controls.Add(_pauseBox);
        top.Controls.Add(_topMostBox);
        header.Controls.Add(top);
        header.Controls.Add(_runsLabel);
        header.Controls.Add(_nextLabel);
        header.Controls.Add(_statusLabel);
        _statusLabel.MaximumSize = new Size(ContentWidth, 0);
        _nextLabel.MaximumSize = new Size(ContentWidth, 0);

        _pauseBox.Font = new Font(Font, FontStyle.Bold);
        _pauseBox.CheckedChanged += (_, _) => Send("paused", _pauseBox.Checked);
        _topMostBox.Checked = TopMost;
        _topMostBox.CheckedChanged += (_, _) => TopMost = _topMostBox.Checked;

        Controls.Add(header);
    }

    private void ShowStatus(JsonObject s)
    {
        var runs = s["runs"]?.ToString() ?? "0";
        var limit = s["runLimit"]?.ToString() ?? "0";
        _runsLabel.Text = $"局數 {runs}{(limit != "0" ? " / " + limit : "")}｜本局尋寶率 +{s["treasure"]}%";
        _nextLabel.Text = s["next"]?.ToString() ?? "";
        _nextLabel.Visible = _nextLabel.Text.Length > 0;
        _statusLabel.Text = "→ " + (s["action"]?.ToString() ?? "");
    }

    #endregion

    #region 依規則清單產生分頁

    private void OnSchema(EngineSchema schema)
    {
        // 只在第一次產生：之後重新整理頁面也會再送 hello，但規則清單一樣，重建只會讓畫面閃一下、捲動位置跑掉
        if (_schema is not null)
            return;
        _schema = schema;

        SuspendLayout();
        _tabs.TabPages.Clear();
        foreach (var tab in schema.Tabs)
        {
            var page = NewPage();
            foreach (var rule in schema.Rules.Where(r => r.Tab == tab))
                AddRule(page, rule);
            switch (tab)
            {
                case "技能":
                    BuildSkillsTab(page);
                    break;
                case "神祇":
                    BuildDeityTab(page);
                    break;
                case "道具":
                    BuildItemsTab(page, schema);
                    break;
                case "其他":
                    BuildOtherTab(page);
                    break;
            }
            var tp = new TabPage(tab) { UseVisualStyleBackColor = true };
            tp.Controls.Add(page);
            _tabs.TabPages.Add(tp);
        }
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (!_applying && _tabs.SelectedTab is { } t)
                Send("tab", t.Text);
        };

        Controls.Remove(_waiting);
        Controls.Add(_tabs);
        _tabs.BringToFront();
        ResumeLayout();
        ApplyConfig();
    }

    /// <summary>
    /// 把目前設定套到所有控制項。JS 每次回傳 cfg 都會呼叫；正在輸入的文字框跳過，免得游標跳走或打到一半被舊值蓋掉。
    /// </summary>
    private void ApplyConfig()
    {
        if (_schema is null)
            return;
        _applying = true;
        try
        {
            _pauseBox.Checked = _config.GetBool("paused");
            ApplyRules();
            ApplyJob();
            ApplyItems();
            ApplyOther();
            var tab = _config.GetString("tab");
            if (_tabs.SelectedTab?.Text != tab && _tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == tab) is { } page)
                _tabs.SelectedTab = page;
        }
        finally
        {
            _applying = false;
        }
    }

    #endregion

    #region 共用小工具

    /// <summary>送出單一設定值（套用設定中不送）</summary>
    private void Send(string key, JsonNode? value)
    {
        if (_applying)
            return;
        _automation.SendPatch(new JsonObject { [key] = value });
    }

    private void Send(JsonObject patch)
    {
        if (_applying)
            return;
        _automation.SendPatch(patch);
    }

    private FlowLayoutPanel NewPage() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(S(6)),
    };

    private Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        ForeColor = Color.SteelBlue,
        Margin = new Padding(0, S(8), 0, S(2)),
    };

    private Label Note(string text, int indent = 0) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.DimGray,
        MaximumSize = new Size(ContentWidth - indent, 0),
        Margin = new Padding(indent, S(2), 0, S(2)),
    };

    /// <summary>一列：由左往右排、太長自動換行</summary>
    private FlowLayoutPanel Row(int indent = 0) => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = true,
        MaximumSize = new Size(ContentWidth - indent, 0),
        Margin = new Padding(indent, 0, 0, 0),
    };

    /// <summary>
    /// 列中的文字。刻意不用 CheckBox 自己的 Text：CheckBox 的文字不會自動換行，規則說明很長時會被切掉；
    /// 改成「沒有文字的 CheckBox ＋ 會換行的 Label」，點 Label 一樣能切換勾選。
    /// </summary>
    private Label InlineText(string text, CheckBox? toggles = null, int maxWidth = 0)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, S(4), S(2), 0),
            MaximumSize = new Size(maxWidth > 0 ? maxWidth : ContentWidth - S(30), 0),
        };
        if (toggles is not null)
            label.Click += (_, _) => toggles.Checked = !toggles.Checked;
        return label;
    }

    private CheckBox BareCheck() => new() { AutoSize = true, Margin = new Padding(0, S(3), 0, 0) };

    private ComboBox Combo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = S(width),
        Margin = new Padding(S(2), 0, 0, S(2)),
    };

    /// <summary>依值選取下拉選項（找不到就不動）</summary>
    private static void SelectValue(ComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is Choice c && c.Value == value)
            {
                if (combo.SelectedIndex != i)
                    combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static JsonArray ToJsonArray(IEnumerable<string> list)
        => new(list.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    #endregion
}
