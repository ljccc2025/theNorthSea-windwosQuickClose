<#
.SYNOPSIS
    提权路径验证：用管理员身份重跑同一套 UIA 验收台（tools/UiSmoke），
    覆盖未提权时只能诚实跳过的那几条写入路径。

.DESCRIPTION
    验收台自己检测是否提权（MainViewModel.IsElevated）并据此切换断言：

      · 未提权：点防火墙必须失败、副标题给出系统原文、系统真值不动、开关弹回；
      · 提权：  防火墙必须真的相对系统真值翻转 → 再点必须还原；
                三张 Windows 功能组件卡必须读到真状态而不是 Unknown；
                提权徽标必须是「管理员模式」；剪贴板历史必须真写并还原。

    本脚本不自己实现验证逻辑，只负责"用管理员身份重新起一次同一个 exe，并把日志落盘"。
    退出码 = FAIL 数（0 = 全绿）。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify-elevated.ps1

.EXAMPLE
    # 已经在管理员窗口里运行时，跳过自举
    powershell -ExecutionPolicy Bypass -File tools\verify-elevated.ps1 -NoElevate
#>
[CmdletBinding()]
param(
    [switch]$NoElevate,
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$project = Join-Path $PSScriptRoot 'UiSmoke\UiSmoke.csproj'
$exe = Join-Path $PSScriptRoot 'UiSmoke\bin\Debug\net10.0-windows\UiSmoke.exe'
$log = Join-Path $env:TEMP 'ui-smoke-elevated.txt'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "先构建验收台：$project"
    & dotnet build $project -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

function Show-Summary {
    if (-not (Test-Path -LiteralPath $log)) { Write-Host "日志不存在：$log"; return 1 }
    $lines = Get-Content -LiteralPath $log -Encoding UTF8
    # 验收台会把同一份结果打印两次（实时输出 + 结尾报告），只统计结尾报告那一段。
    $idx = [Array]::LastIndexOf($lines, '================ UI SMOKE REPORT ================')
    $report = if ($idx -ge 0) { $lines[$idx..($lines.Count - 1)] } else { $lines }
    $pass = @($report | Where-Object { $_ -like '[[]PASS[]]*' }).Count
    $fail = @($report | Where-Object { $_ -like '[[]FAIL[]]*' }).Count
    $skip = @($report | Where-Object { $_ -like '[[]SKIP[]]*' }).Count
    Write-Host ""
    Write-Host "PASS=$pass FAIL=$fail SKIP=$skip   （日志：$log）"
    $report | Where-Object { $_ -like '[[]FAIL[]]*' -or $_ -like '[[]SKIP[]]*' } | ForEach-Object { Write-Host $_ }
    return $fail
}

if ($isAdmin) {
    Write-Host "当前已是管理员身份，直接跑。"
    & $exe *>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    Write-Host "exit=$code（退出码 = FAIL 数）"
    Get-Content -LiteralPath $log -Encoding UTF8 |
        Where-Object { $_ -match '^\[(FAIL|SKIP)\]' } | ForEach-Object { Write-Host $_ }
    exit $code
}

if ($NoElevate) {
    Write-Host "当前不是管理员，且指定了 -NoElevate：请先打开一个「以管理员身份运行」的终端再执行本脚本。"
    exit 2
}

Write-Host "当前未提权 → 用管理员身份重新起一次同一个 exe。日志：$log"
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }

# 提权子进程自己把 stdout/stderr 追加写进日志，并补一行 EXITCODE=；
# 父进程只轮询日志，不做别的 —— 父进程被外部杀掉时子进程仍会把日志写完。
$inner = @"
[Console]::OutputEncoding=[Text.Encoding]::UTF8
& '$exe' *>> '$log'
"EXITCODE=`$LASTEXITCODE" | Out-File -LiteralPath '$log' -Append -Encoding utf8
"@

Start-Process -FilePath 'powershell.exe' -Verb RunAs `
    -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $inner)

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$done = $false
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $log) {
        $raw = Get-Content -LiteralPath $log -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        # 收尾标记以验收台自己打印的报告头为准（不依赖包装进程还能不能接着往下跑）。
        if ($raw -match 'UI SMOKE REPORT') { $done = $true; break }
    }
    Start-Sleep -Seconds 5
}
# 报告打印完 ≠ 进程退出：验收台还要关窗口、退进程，包装进程之后才写 EXITCODE=。最多再等 40 秒。
if ($done) {
    $tailDeadline = (Get-Date).AddSeconds(40)
    while ((Get-Date) -lt $tailDeadline) {
        if ((Get-Content -LiteralPath $log -Raw -Encoding UTF8 -ErrorAction SilentlyContinue) -match 'EXITCODE=') { break }
        Start-Sleep -Milliseconds 500
    }
}

if (-not (Test-Path -LiteralPath $log)) { Write-Host "日志未生成，提权窗口可能被拒绝。"; exit 3 }
if (-not $done) { Write-Host "等待超时（$TimeoutMinutes 分钟），验收台还没打印报告；下面是已落盘的部分。" }
elseif ((Get-Content -LiteralPath $log -Raw -Encoding UTF8) -notmatch 'EXITCODE=') {
    Write-Host "（注意：40 秒内没等到包装进程的 EXITCODE=，按报告段统计结果。）"
}

$fails = Show-Summary
exit $fails
