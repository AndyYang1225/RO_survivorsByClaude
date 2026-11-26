# 重開三隻小幫手（預設 sabc1229、B＝sabc1227、C＝sabc1228）換成最新編譯的版本。
# 流程：等每隻這局打完就暫停（不中斷對局）→ 全部關掉 → 編譯 → 重開 → 繼續 → 等進野外 → 視窗縮到背景。
# 用法（在 C:\Ro）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\restart.ps1          等這局打完再重開
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\restart.ps1 -Now     不等，馬上重開
#   加 -NoBuild：不重新編譯（只重開）
#   編譯成功（＝改版）時會清空戰績、局數、完整對戰紀錄（tools\reset-stats.js，舊的搬到 history\）；加 -KeepStats 不清
#   加 -Set runLimit=true,runLimitN=5：關掉之後、重開之前改三隻的 config.json（tools\set-config.js；玩法選擇只能這樣改）
param([switch]$Now, [switch]$NoBuild, [switch]$KeepStats, [string[]]$Set = @(), [int]$WaitMinutes = 60)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exeDir = Join-Path $root 'RoHelper\bin\Debug\net8.0-windows10.0.19041.0'
$bots = @(
    @{ Name = 'sabc1229'; Port = 8765; Args = '--dev-api=8765' },
    @{ Name = 'sabc1227(B)'; Port = 8766; Args = '--profile=B --dev-api=8766' },
    @{ Name = 'sabc1228(C)'; Port = 8767; Args = '--profile=C --dev-api=8767' }
)

function Get-Status($port) {
    try { (Invoke-WebRequest "http://127.0.0.1:$port/status?n=1" -UseBasicParsing -TimeoutSec 5).Content | ConvertFrom-Json } catch { $null }
}
function Send-Post($port, $path) {
    try { [void](Invoke-WebRequest "http://127.0.0.1:$port/$path" -Method Post -Body '' -UseBasicParsing -TimeoutSec 5) } catch { }
}
function Write-Step($text) { Write-Host "$(Get-Date -Format HH:mm:ss) $text" }

# 1. 等這局打完（局數 +1）就暫停；沒在執行的跳過
if (-not $Now) {
    $start = @{}
    foreach ($b in $bots) { if ($s = Get-Status $b.Port) { $start[$b.Port] = [int]$s.runs } }
    Write-Step "等這局打完：$(($bots | ? { $start.ContainsKey($_.Port) } | % { $_.Name }) -join '、')"
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    while ($start.Count -gt 0 -and (Get-Date) -lt $deadline) {
        foreach ($port in @($start.Keys)) {
            $s = Get-Status $port
            if ($s -and [int]$s.runs -gt $start[$port]) {
                Send-Post $port 'pause'
                $start.Remove($port)
                Write-Step "$(($bots | ? { $_.Port -eq $port }).Name) 打完了，已暫停"
            }
        }
        if ($start.Count -gt 0) { Start-Sleep 10 }
    }
    if ($start.Count -gt 0) { Write-Step "等了 $WaitMinutes 分鐘還沒打完，直接重開" }
}

# 2. 全部關掉（正常關閉，30 秒沒關才強制結束）
Get-Process RoHelper -ErrorAction SilentlyContinue | % { [void]$_.CloseMainWindow() }
$end = (Get-Date).AddSeconds(30)
while ((Get-Process RoHelper -ErrorAction SilentlyContinue) -and (Get-Date) -lt $end) { Start-Sleep 1 }
Get-Process RoHelper -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Step '已全部關掉'

# 3. 編譯
if (-not $NoBuild) {
    $out = & dotnet build (Join-Path $root 'RoHelper') 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | Select-String ' error ' | Select-Object -First 10 | % { Write-Host $_ }
        Write-Step '編譯失敗，沒有重開'
        exit 1
    }
    Write-Step '編譯完成'
    # 改版了：清空戰績、局數、完整對戰紀錄（使用者 2026-11-27，避免讀到舊版的資料；舊的搬到各帳號的 history\）。-KeepStats 不清
    if (-not $KeepStats) {
        Write-Step '清空戰績、局數、完整對戰紀錄'
        & node (Join-Path $PSScriptRoot 'reset-stats.js')
        if ($LASTEXITCODE -ne 0) { Write-Step '清空紀錄失敗（照樣重開）' }
    }
}

# 3.5 改設定（-Set）
$Set = @($Set | % { $_ -split ',' } | ? { $_ })  # 用 -File 執行時 a,b 不會變成陣列
if ($Set.Count -gt 0) {
    Write-Step "改設定：$($Set -join ' ')"
    & node (Join-Path $PSScriptRoot 'set-config.js') @Set
    if ($LASTEXITCODE -ne 0) { Write-Step '改設定失敗，沒有重開'; exit 1 }
}

# 4. 重開（用 cmd start 開成獨立的程序，不會跟著這個腳本結束）
foreach ($b in $bots) { cmd /c start '""' /D "$exeDir" RoHelper.exe $b.Args.Split(' ') }
$end = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $end -and ($bots | ? { -not (Get-Status $_.Port) })) { Start-Sleep 3 }
foreach ($b in $bots) { Send-Post $b.Port 'resume' }
Write-Step '已重開並繼續'

# 5. 等進野外（最多 3 分鐘），B、C 視窗縮到背景（SC_MINIMIZE，會移到螢幕外照跑）
$end = (Get-Date).AddMinutes(3)
do {
    Start-Sleep 5
    $actions = $bots | % { $s = Get-Status $_.Port; "$($_.Name)：$(if ($s) { $s.action } else { '沒回應' })" }
    $inField = @($actions | ? { $_ -match '走位|選.*卡|準備進入' }).Count
} while ($inField -lt $bots.Count -and (Get-Date) -lt $end)

# A（主帳號）留在螢幕上，B、C 移到螢幕外。用 status.json 的 Hwnd（遊戲視窗）：程序的主視窗有時是面板，送過去縮掉的是面板
# 用 PostMessage：SendMessage 跨程序實測不一定馬上生效
Add-Type -Namespace RoRestart -Name Win -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);'
foreach ($dir in 'profiles\B', 'profiles\C') {
    $st = Get-Content (Join-Path $env:APPDATA "RoHelper\$dir\status.json") -Raw -Encoding UTF8 -ErrorAction SilentlyContinue | ConvertFrom-Json
    if ($st -and $st.hwnd) { [void][RoRestart.Win]::PostMessage([IntPtr][long]$st.hwnd, 0x0112, [IntPtr]0xF020, [IntPtr]0) }
}

$actions | % { Write-Host "  $_" }
Write-Step $(if ($inField -eq $bots.Count) { '完成：全部進野外了' } else { '完成，但有的還沒進野外（看上面的狀態）' })
