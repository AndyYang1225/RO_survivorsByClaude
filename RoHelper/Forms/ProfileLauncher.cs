using System.Diagnostics;
using System.Runtime.InteropServices;

using RoHelper.DevApi;

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

    /// <summary>把某個分身的遊戲視窗切到前面（在螢幕外的會由它自己的 OnActivated 移回來）。找不到回 false</summary>
    public static bool Focus(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.ProcessName != Process.GetCurrentProcess().ProcessName || p.MainWindowHandle == IntPtr.Zero)
                return false;
            if (NativeMethods.IsIconic(p.MainWindowHandle))
                NativeMethods.ShowWindow(p.MainWindowHandle, NativeMethods.SwRestore);
            return NativeMethods.SetForegroundWindow(p.MainWindowHandle);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;

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
