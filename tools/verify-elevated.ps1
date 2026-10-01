<#
.SYNOPSIS
    提权路径验证：在管理员权限下跑同一套 UIA 验收台（UiSmoke），覆盖未提权时只能跳过的写入路径。

.DESCRIPTION
    UiSmoke 会自己检测自己是否提权（读 MainViewModel.IsElevated），并据此切换断言：
      · 未提权（默认）：防火墙点击必须失败、副标题给出系统原文、真值不动、开关弹回。
      · 提权（本脚本）：防火墙必须真的三档全开 → 再点一次必须三档还原；三张 Windows 功能组件卡
        必须读到真状态而不是 Unknown；提权徽标必须是「管理员模式」；总电源开关的确认框路径可进入。
    本脚本不自己实现验证逻辑，只负责"用管理员身份重新起一次同一个 exe，并把日志落盘"。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify-elevated.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'UiSmoke\UiSmoke.csproj'
$exe = Join-Path $PSScriptRoot 'UiSmoke\bin\Debug\net10.0-windows\UiSmoke.exe'
$log = Join-Path $env:TEMP 'ui-smoke-elevated.txt'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "构建验收台：$project"
    & dotnet build $project -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin -and -not $NoElevate) {
    Write-Host "当前会话未提权 → 用管理员身份重新启动（会弹一次 UAC）。日志：$log"
    $inner = "& '$exe' *>&1 | Tee-Object -FilePath '$log'; Write-Host ('exit=' + $LASTEXITCODE); Read-Host '按回车关闭'"
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $inner)
    Write-Host "已拉起提权窗口；等它跑完（约 3-5 分钟）后执行： Get-Content '$log' | Select-String 'PASS=|\[FAIL\]|\[SKIP\]'"
    return
}

Write-Host ("提权状态：IsAdmin={0}" -f $isAdmin)
& $exe *>&1 | Tee-Object -FilePath $log
$code = $LASTEXITCODE
Write-Host ("exit=$code（退出码 = FAIL 数）")
Get-Content -LiteralPath $log | Select-String -Pattern 'PASS=|^\[FAIL\]|^\[SKIP\]' | ForEach-Object { $_.Line }
exit $code
