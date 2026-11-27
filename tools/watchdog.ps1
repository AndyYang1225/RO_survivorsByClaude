# 看門狗（使用者 2026-11-27：超過一段時間沒動靜就自己重開）。
# 程式內的卡住偵測（Vision/StallWatch.cs）只會重新整理網頁、最多 2 次就放棄，而且小幫手本身當掉、卡死時它也跑不動；
# 這個腳本在外面看：每分鐘問一次三隻的開發介面，
#   - 沒回應連續 NoReplyMinutes 分鐘，或
#   - 動作紀錄（/status 最後一筆的時間＋內容）StaleMinutes 分鐘都沒變（暫停中、局數上限不算）
# 就只把那一隻關掉重開（不編譯、不清戰績），B、C 重開後縮到背景。
# 重開後 GraceMinutes 分鐘內不再檢查那一隻；每隻每小時最多重開 MaxPerHour 次（重開也救不回來時不要一直重開）。
# tools\restart.ps1 執行中（%AppData%\RoHelper\restarting.lock）時不動作。紀錄寫到 %AppData%\RoHelper\watchdog.log。
# 用法：tools\restart.ps1 會自動在背景啟動（已經在跑就不重複開）；手動：
#   powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File tools\watchdog.ps1
param([int]$StaleMinutes = 10, [int]$NoReplyMinutes = 3, [int]$GraceMinutes = 5, [int]$MaxPerHour = 3)

$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$exeDir = Join-Path $root 'RoHelper\bin\Debug\net8.0-windows10.0.19041.0'
$data = Join-Path $env:APPDATA 'RoHelper'
$log = Join-Path $data 'watchdog.log'
$pidFile = Join-Path $data 'watchdog.pid'
$lock = Join-Path $data 'restarting.lock'
$bots = @(
    @{ Name = 'sabc1229'; Port = 8765; Args = '--dev-api=8765'; Dir = ''; Hide = $false },
    @{ Name = 'sabc1227(B)'; Port = 8766; Args = '--profile=B --dev-api=8766'; Dir = 'profiles\B'; Hide = $true },
    @{ Name = 'sabc1228(C)'; Port = 8767; Args = '--profile=C --dev-api=8767'; Dir = 'profiles\C'; Hide = $true }
)

# 只跑一隻看門狗
if (Test-Path $pidFile) {
    $old = Get-Content $pidFile -ErrorAction SilentlyContinue
    if ($old -and $old -ne "$PID" -and (Get-Process -Id $old -ErrorAction SilentlyContinue | ? { $_.ProcessName -match 'powershell' })) { exit 0 }
}
Set-Content $pidFile "$PID"

function Write-Log($text) {
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $text"
    Add-Content $log $line -Encoding UTF8
    # 只留最後 500 行
    $all = Get-Content $log -Encoding UTF8
    if ($all.Count -gt 600) { $all | Select-Object -Last 500 | Set-Content $log -Encoding UTF8 }
}
function Get-Status($port) {
    try { (Invoke-WebRequest "http://127.0.0.1:$port/status?n=1" -UseBasicParsing -TimeoutSec 10).Content | ConvertFrom-Json } catch { $null }
}
Add-Type -Namespace RoWatch -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);'

function Restart-Bot($b, $why) {
    Write-Log "$($b.Name)：$why → 重開"
    $procs = Get-CimInstance Win32_Process -Filter "Name='RoHelper.exe'" | ? { $_.CommandLine -match "--dev-api=$($b.Port)\b" }
    foreach ($p in $procs) {
        $proc = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
        if ($proc) { [void]$proc.CloseMainWindow() }
    }
    $end = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $end -and ($procs | ? { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })) { Start-Sleep 1 }
    foreach ($p in $procs) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
    $started = Get-Date
    cmd /c start '""' /D "$exeDir" RoHelper.exe $b.Args.Split(' ')
    $end = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $end -and -not (Get-Status $b.Port)) { Start-Sleep 3 }
    try { [void](Invoke-WebRequest "http://127.0.0.1:$($b.Port)/resume" -Method Post -Body '' -UseBasicParsing -TimeoutSec 5) } catch { }
    if ($b.Hide) {
        # 等 status.json 換成新視窗的 hwnd 再縮到背景（SC_MINIMIZE，會移到螢幕外照跑）
        $file = Join-Path $data "$($b.Dir)\status.json"
        $end = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $end -and -not ((Test-Path $file) -and (Get-Item $file).LastWriteTime -gt $started)) { Start-Sleep 3 }
        $st = Get-Content $file -Raw -Encoding UTF8 -ErrorAction SilentlyContinue | ConvertFrom-Json
        if ($st -and $st.hwnd) { [void][RoWatch.Win]::PostMessage([IntPtr][long]$st.hwnd, 0x0112, [IntPtr]0xF020, [IntPtr]0) }
    }
    Write-Log "$($b.Name)：重開完成（$(if (Get-Status $b.Port) { '有回應' } else { '還沒回應' })）"
}

$state = @{}
foreach ($b in $bots) { $state[$b.Port] = @{ Sig = ''; Since = Get-Date; NoReply = $null; Grace = Get-Date; Restarts = @() } }
Write-Log "看門狗啟動（PID $PID；動作 $StaleMinutes 分鐘沒變、沒回應 $NoReplyMinutes 分鐘就重開）"

while ($true) {
    Start-Sleep 60
    if ((Test-Path $lock) -and ((Get-Date) - (Get-Item $lock).LastWriteTime).TotalMinutes -lt 20) {
        foreach ($b in $bots) { $st = $state[$b.Port]; $st.Since = Get-Date; $st.NoReply = $null; $st.Grace = Get-Date }
        continue
    }
    foreach ($b in $bots) {
        $st = $state[$b.Port]
        $now = Get-Date
        if (($now - $st.Grace).TotalMinutes -lt $GraceMinutes) { continue }
        $s = Get-Status $b.Port
        $why = $null
        if (-not $s) {
            # 程序根本不在（沒開這一隻）就不管：只救「開著卻沒回應」的
            $alive = Get-CimInstance Win32_Process -Filter "Name='RoHelper.exe'" | ? { $_.CommandLine -match "--dev-api=$($b.Port)\b" }
            if (-not $alive) { $st.NoReply = $null; continue }
            if (-not $st.NoReply) { $st.NoReply = $now }
            if (($now - $st.NoReply).TotalMinutes -ge $NoReplyMinutes) { $why = "開發介面 $NoReplyMinutes 分鐘沒回應" }
        }
        else {
            $st.NoReply = $null
            $last = @($s.history)[-1]
            $sig = "$($last.t)|$($last.a)|$($s.action)"
            # 暫停中、局數上限到了（停在大廳）、沒開自動（待機）是本來就不會動
            if ($s.paused -or "$($s.action)" -match '局數上限|已暫停|^待機') { $st.Sig = $sig; $st.Since = $now; continue }
            if ($sig -ne $st.Sig) { $st.Sig = $sig; $st.Since = $now; continue }
            if (($now - $st.Since).TotalMinutes -ge $StaleMinutes) { $why = "$StaleMinutes 分鐘沒有新動作（停在「$($s.action)」）" }
        }
        if (-not $why) { continue }
        $st.Restarts = @($st.Restarts | ? { ($now - $_).TotalMinutes -lt 60 })
        if ($st.Restarts.Count -ge $MaxPerHour) {
            if (($now - $st.Since).TotalMinutes -lt $StaleMinutes + 1 -or $st.NoReply) { Write-Log "$($b.Name)：$why，但一小時內已經重開 $MaxPerHour 次，先不動" }
            $st.Since = $now
            continue
        }
        Restart-Bot $b $why
        $st.Restarts += Get-Date
        $st.Sig = ''
        $st.Since = Get-Date
        $st.NoReply = $null
        $st.Grace = Get-Date
    }
}
