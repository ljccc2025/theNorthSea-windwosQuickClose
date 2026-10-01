<#
.SYNOPSIS
    鎻愭潈璺緞楠岃瘉锛氬湪绠＄悊鍛樻潈闄愪笅璺戝悓涓€濂?UIA 楠屾敹鍙帮紙UiSmoke锛夛紝瑕嗙洊鏈彁鏉冩椂鍙兘璺宠繃鐨勫啓鍏ヨ矾寰勩€?

.DESCRIPTION
    UiSmoke 浼氳嚜宸辨娴嬭嚜宸辨槸鍚︽彁鏉冿紙璇?MainViewModel.IsElevated锛夛紝骞舵嵁姝ゅ垏鎹㈡柇瑷€锛?
      路 鏈彁鏉冿紙榛樿锛夛細闃茬伀澧欑偣鍑诲繀椤诲け璐ャ€佸壇鏍囬缁欏嚭绯荤粺鍘熸枃銆佺湡鍊间笉鍔ㄣ€佸紑鍏冲脊鍥炪€?
      路 鎻愭潈锛堟湰鑴氭湰锛夛細闃茬伀澧欏繀椤荤湡鐨勪笁妗ｅ叏寮€ 鈫?鍐嶇偣涓€娆″繀椤讳笁妗ｈ繕鍘燂紱涓夊紶 Windows 鍔熻兘缁勪欢鍗?
        蹇呴』璇诲埌鐪熺姸鎬佽€屼笉鏄?Unknown锛涙彁鏉冨窘鏍囧繀椤绘槸銆岀鐞嗗憳妯″紡銆嶏紱鎬荤數婧愬紑鍏崇殑纭妗嗚矾寰勫彲杩涘叆銆?
    鏈剼鏈笉鑷繁瀹炵幇楠岃瘉閫昏緫锛屽彧璐熻矗"鐢ㄧ鐞嗗憳韬唤閲嶆柊璧蜂竴娆″悓涓€涓?exe锛屽苟鎶婃棩蹇楄惤鐩?銆?

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\verify-elevated.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'UiSmoke\UiSmoke.csproj'
$exe = Join-Path $PSScriptRoot 'UiSmoke\bin\Debug\net10.0-windows\UiSmoke.exe'
$log = Join-Path $env:TEMP 'ui-smoke-elevated.txt'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "鏋勫缓楠屾敹鍙帮細$project"
    & dotnet build $project -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "鏋勫缓澶辫触锛岄€€鍑虹爜 $LASTEXITCODE" }
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin -and -not $NoElevate) {
    Write-Host "褰撳墠浼氳瘽鏈彁鏉?鈫?鐢ㄧ鐞嗗憳韬唤閲嶆柊鍚姩锛堜細寮逛竴娆?UAC锛夈€傛棩蹇楋細$log"
    $inner = "& '$exe' *>&1 | Tee-Object -FilePath '$log'; Write-Host ('exit=' + $LASTEXITCODE); Read-Host '鎸夊洖杞﹀叧闂?"
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $inner)
    Write-Host "宸叉媺璧锋彁鏉冪獥鍙ｏ紱绛夊畠璺戝畬锛堢害 3-5 鍒嗛挓锛夊悗鎵ц锛?Get-Content '$log' | Select-String 'PASS=|\[FAIL\]|\[SKIP\]'"
    return
}

Write-Host ("鎻愭潈鐘舵€侊細IsAdmin={0}" -f $isAdmin)
& $exe *>&1 | Tee-Object -FilePath $log
$code = $LASTEXITCODE
Write-Host ("exit=$code锛堥€€鍑虹爜 = FAIL 鏁帮級")
Get-Content -LiteralPath $log | Select-String -Pattern 'PASS=|^\[FAIL\]|^\[SKIP\]' | ForEach-Object { $_.Line }
exit $code
