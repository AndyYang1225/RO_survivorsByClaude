using System.Diagnostics;
using System.Runtime.InteropServices;

using RoHelper.DevApi;
using RoHelper.Storage;

namespace RoHelper.Forms;

/// <summary>
/// 開分身、建立桌面捷徑、切到某個分身的視窗。
/// 以前開分身要自己打命令列參數（--profile=B --dev-api=8766），改成選單點一下。
/// </summary>
public static class ProfileLauncher
{
    /// <summary>
    /// 某個 profile 的啟動參數。devApi＝這個視窗有開發介面時，分身也開（port 照名稱排開，不會撞到）。
    /// 單一英文字母照字母排：A＝8765（同預設）、B＝8766、C＝8767…，跟目前手動開的一致；其他名稱放到 8800 之後
    /// </summary>
    public static string[] ArgsFor(string profile, bool devApi)
    {
        var args = new List<string>();
        if (profile.Length > 0)
            args.Add("--profile=" + profile);
        if (devApi)
            args.Add(profile.Length == 0 ? "--dev-api" : $"--dev-api={PortFor(profile)}");
        return [.. args];
    }

    public static int PortFor(string profile)
    {
        if (profile.Length == 1 && char.IsAsciiLetter(profile[0]))
            return DevApiOptions.DefaultPort + (char.ToUpperInvariant(profile[0]) - 'A');
        var h = 0;
        foreach (var c in profile.ToUpperInvariant())
            h = (h * 31 + c) % 100;
        return 8800 + h;
    }

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static void Launch(string[] args)
    {
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>在桌面建立捷徑（同名的會被換掉，例如 exe 換了位置），回傳捷徑路徑</summary>
    public static string CreateShortcut(string profile, string[] args)
    {
        var name = profile.Length == 0 ? "RO 小幫手" : $"RO 小幫手 {profile}";
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("這台電腦沒有 WScript.Shell，無法建立捷徑");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            try
            {
                link.TargetPath = ExePath;
                link.Arguments = string.Join(" ", args);
                link.WorkingDirectory = Path.GetDirectoryName(ExePath);
                link.IconLocation = ExePath + ",0";
                link.Description = $"RO Survivors 小幫手（{AppPaths.DisplayName(profile)}）";
                link.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
        return path;
    }

    /// <summary>
    /// 切到某個分身：先把其他在螢幕上的分身遊戲視窗移到螢幕外（送 SC_MINIMIZE，GameForm 會攔下來移走、照常掛機），
    /// 再把這個分身的遊戲視窗切到前面（在螢幕外的會移回來）。找不到這個分身的視窗回 false。
    /// </summary>
    /// <param name="others">其他還在執行的分身</param>
    public static bool SwitchTo(InstanceStatus target, IEnumerable<InstanceStatus> others)
    {
        if (GameWindow(target) is not { } h)
            return false;
        var parking = others.Where(o => o.Pid != target.Pid).Select(GameWindow).OfType<IntPtr>().Where(OnScreen).ToList();
        foreach (var o in parking)
            NativeMethods.PostMessage(o, WmSysCommand, ScMinimize, IntPtr.Zero);
        // 等它們移走（各自的 UI 執行緒處理，通常幾十毫秒）；移走時會把前景交給後面的視窗，所以要等完再切，免得被搶走
        for (var i = 0; i < 20 && parking.Any(OnScreen); i++)
            Thread.Sleep(50);
        if (NativeMethods.IsIconic(h))
            NativeMethods.ShowWindow(h, NativeMethods.SwRestore);
        // 在螢幕外：再送一次 SC_MINIMIZE 就是移回來（GameForm.WndProc 的切換）；不靠 OnActivated，跨程序切前景可能被系統擋掉
        else if (!OnScreen(h))
            NativeMethods.PostMessage(h, WmSysCommand, ScMinimize, IntPtr.Zero);
        NativeMethods.SetForegroundWindow(h);
        return true;
    }

    private const int WmSysCommand = 0x0112;
    private static readonly IntPtr ScMinimize = 0xF020;

    /// <summary>分身的遊戲視窗：status.json 有寫 Hwnd 而且還是那個程序的就用它，舊版沒寫才用程序的主視窗（可能是面板）</summary>
    private static IntPtr? GameWindow(InstanceStatus st)
    {
        try
        {
            using var p = Process.GetProcessById(st.Pid);
            if (p.ProcessName != Process.GetCurrentProcess().ProcessName)
                return null;
            var h = (IntPtr)st.Hwnd;
            if (h != IntPtr.Zero && NativeMethods.GetWindowThreadProcessId(h, out var pid) != 0 && pid == st.Pid)
                return h;
            return p.MainWindowHandle == IntPtr.Zero ? null : p.MainWindowHandle;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>視窗在螢幕上（沒有最小化、也沒被移到螢幕外）</summary>
    private static bool OnScreen(IntPtr h)
        => !NativeMethods.IsIconic(h) && NativeMethods.GetWindowRect(h, out var r)
            && Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).IntersectsWith(SystemInformation.VirtualScreen);

    private static class NativeMethods
    {
        public const int SwRestore = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }
}
