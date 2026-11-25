# 截 RoHelper 所有可見視窗（含面板）存成 PNG，給驗收、除錯用。
# 用 PrintWindow（PW_RENDERFULLCONTENT）：視窗被擋住也截得到，WebView2 的內容也會畫出來。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\winshot.ps1 -OutDir <資料夾>
param([Parameter(Mandatory = $true)][string]$OutDir)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class WinShot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
"@

New-Item -ItemType Directory -Force $OutDir | Out-Null
$ids = @(Get-Process RoHelper -ErrorAction Stop | ForEach-Object { $_.Id })
$handles = New-Object System.Collections.Generic.List[IntPtr]
[WinShot]::EnumWindows({ param($h, $l)
    $p = 0
    [void][WinShot]::GetWindowThreadProcessId($h, [ref]$p)
    if ($ids -contains $p -and [WinShot]::IsWindowVisible($h)) { $handles.Add($h) }
    $true }, [IntPtr]::Zero) | Out-Null

$i = 0
foreach ($h in $handles) {
    $r = New-Object WinShot+Rect
    [void][WinShot]::GetWindowRect($h, [ref]$r)
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -lt 50 -or $hh -lt 50) { continue }
    $title = New-Object System.Text.StringBuilder 256
    [void][WinShot]::GetWindowText($h, $title, 256)
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    [void][WinShot]::PrintWindow($h, $dc, 2)
    $g.ReleaseHdc($dc); $g.Dispose()
    $file = Join-Path $OutDir "win$i.png"
    $bmp.Save($file); $bmp.Dispose()
    "$file  ${w}x$hh  $($title.ToString())"
    $i++
}
