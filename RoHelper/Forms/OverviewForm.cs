using RoHelper.Storage;

namespace RoHelper.Forms;

/// <summary>
/// 多開總覽：一次看所有分身的狀態（讀各 profile 資料夾的 status.json，每 3 秒更新）。
/// 點兩下：在執行的切到它的視窗、沒在執行的用上次的參數開起來。也可以從這裡新增分身、建立桌面捷徑。
/// </summary>
public sealed class OverviewForm : Form
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly bool _devApi;

    /// <param name="devApi">這個視窗有開發介面：新開的分身也開（見 <see cref="ProfileLauncher.ArgsFor"/>）</param>
    public OverviewForm(bool devApi)
    {
        _devApi = devApi;
        Text = "RO 小幫手 多開總覽";
        Font = new Font("Microsoft JhengHei UI", 11f);
        Size = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(320));
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        _list.Columns.Add("分身", LogicalToDeviceUnits(90));
        _list.Columns.Add("狀態", LogicalToDeviceUnits(360));
        _list.Columns.Add("局數", LogicalToDeviceUnits(70), HorizontalAlignment.Right);
        _list.Columns.Add("最後更新", LogicalToDeviceUnits(110));
        _list.DoubleClick += (_, _) => OpenSelected();

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(LogicalToDeviceUnits(4)) };
        bar.Controls.Add(Button("切換／開啟", OpenSelected));
        bar.Controls.Add(Button("新增分身…", AddProfile));
        bar.Controls.Add(Button("建立桌面捷徑", CreateShortcut));
        bar.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "點兩下：切到視窗／開啟",
            Margin = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(8), 0, 0),
        });

        Controls.Add(_list);
        Controls.Add(bar);
        _timer.Tick += (_, _) => Reload();
        Shown += (_, _) =>
        {
            Reload();
            _timer.Start();
        };
        FormClosed += (_, _) => _timer.Dispose();
    }

    private Button Button(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Reload()
    {
        var selected = (_list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null) ?? AppPaths.Profile;
        var now = DateTime.Now;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var profile in AppPaths.AllProfiles())
        {
            var st = InstanceStatus.Load(AppPaths.ProfileDirOf(profile));
            var alive = st?.IsAlive(now) == true;
            var state = st is null ? "（還沒有紀錄）"
                : !alive ? "沒在執行"
                : st.Warning.Length > 0 ? "⚠ " + st.Warning
                : st.Paused ? "已暫停"
                : st.Action;
            var item = new ListViewItem([
                AppPaths.DisplayName(profile),
                state,
                st is null ? "" : st.Runs.ToString(),
                st is null ? "" : Ago(now - st.Updated),
            ])
            { Tag = profile };
            if (!alive)
                item.ForeColor = Color.Gray;
            else if (st!.Warning.Length > 0)
                item.ForeColor = Color.OrangeRed;
            _list.Items.Add(item);
            if (profile == selected)
                item.Selected = true;
        }
        _list.EndUpdate();
    }

    private static string Ago(TimeSpan t)
        => t.TotalSeconds < 60 ? "剛剛" : t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} 分鐘前" : t.TotalHours < 24 ? $"{(int)t.TotalHours} 小時前" : $"{(int)t.TotalDays} 天前";

    private string? SelectedProfile => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as string : null;

    private void OpenSelected()
    {
        if (SelectedProfile is not { } profile)
            return;
        var st = InstanceStatus.Load(AppPaths.ProfileDirOf(profile));
        var now = DateTime.Now;
        if (st is not null && st.IsAlive(now))
        {
            // 切到這個分身時，其他分身的遊戲視窗移到螢幕外（照常掛機），畫面上只留一隻，不會看錯是哪個帳號
            var others = AppPaths.AllProfiles().Where(p => p != profile)
                .Select(p => InstanceStatus.Load(AppPaths.ProfileDirOf(p))).OfType<InstanceStatus>().Where(o => o.IsAlive(now));
            if (!ProfileLauncher.SwitchTo(st, others))
                MessageBox.Show(this, $"找不到 {AppPaths.DisplayName(profile)} 的視窗。", Text);
            return;
        }
        // 同一個 profile 已經開著（只是還沒寫狀態）會被它自己的「只能開一個」擋下，不會開兩份
        ProfileLauncher.Launch(st?.Args is { Length: > 0 } a ? a : ProfileLauncher.ArgsFor(profile, _devApi));
    }

    private void AddProfile()
    {
        var name = Prompt("新增分身", "分身名稱（英文字母、數字、- 或 _，例如 D）：", Suggest());
        if (name is null)
            return;
        if (ProfileOptions.Parse(["--profile=" + name], out var error) is null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var args = ProfileLauncher.ArgsFor(name, _devApi);
        var shortcut = MessageBox.Show(this, $"要順便在桌面建立「RO 小幫手 {name}」的捷徑嗎？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (shortcut == DialogResult.Yes)
            TryCreateShortcut(name, args);
        ProfileLauncher.Launch(args);
        MessageBox.Show(this, $"已開啟分身 {name}。\n第一次開會照主視窗的設定和視窗大小，請在新視窗登入這個分身要用的帳號。", Text);
    }

    /// <summary>建議的新名稱：B、C、D… 第一個還沒用過的</summary>
    private static string Suggest()
    {
        var used = AppPaths.AllProfiles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var c = 'B'; c <= 'Z'; c++)
            if (!used.Contains(c.ToString()))
                return c.ToString();
        return "";
    }

    private void CreateShortcut()
    {
        var profile = SelectedProfile ?? AppPaths.Profile;
        var st = InstanceStatus.Load(AppPaths.ProfileDirOf(profile));
        TryCreateShortcut(profile, st?.Args is { Length: > 0 } a ? a : ProfileLauncher.ArgsFor(profile, _devApi), announce: true);
    }

    private void TryCreateShortcut(string profile, string[] args, bool announce = false)
    {
        try
        {
            var path = ProfileLauncher.CreateShortcut(profile, args);
            if (announce)
                MessageBox.Show(this, $"已建立桌面捷徑：{Path.GetFileNameWithoutExtension(path)}", Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"建立捷徑失敗 : {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? Prompt(string title, string label, string value)
    {
        using var dlg = new Form
        {
            Text = title,
            Font = Font,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(LogicalToDeviceUnits(10)),
        };
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var box = new TextBox { Text = value, Width = LogicalToDeviceUnits(280) };
        var ok = new Button { Text = "確定", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        panel.Controls.Add(box);
        panel.Controls.Add(buttons);
        dlg.Controls.Add(panel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}
