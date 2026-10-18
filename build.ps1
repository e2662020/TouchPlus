# Touch+ 一键构建脚本
# 用法：  pwsh -ExecutionPolicy Bypass -File .\build.ps1
# 说明：  只依赖 Windows 自带的 .NET Framework C# 编译器（Win7 SP1 以上都有），
#         不需要安装 Visual Studio / .NET SDK。

param(
    [switch]$Test,          # 只跑纯逻辑自检，不构建
    [string]$OutDir = "dist\Touch+"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Get-Csc {
    $candidates = @(
        "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    throw "找不到 csc.exe，请先安装 .NET Framework 4.x"
}

$csc = Get-Csc
Write-Host "编译器 : $csc" -ForegroundColor Cyan

$core     = Get-ChildItem "$root\src\TouchPlus\Core\*.cs" -Recurse  | ForEach-Object { $_.FullName }
$plugins  = Get-ChildItem "$root\src\TouchPlus\Plugins\*.cs"                    | ForEach-Object { $_.FullName }
$ui       = Get-ChildItem "$root\src\TouchPlus\UI\*.cs"            | ForEach-Object { $_.FullName }
$controls = Get-ChildItem "$root\src\TouchPlus\UI\Controls\*.cs"   | ForEach-Object { $_.FullName }
$props    = Get-ChildItem "$root\src\TouchPlus\Properties\*.cs"    | ForEach-Object { $_.FullName }

$refs = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')

New-Item -ItemType Directory -Force -Path "$root\build" | Out-Null

# ---------- 自检程序（纯逻辑，不碰系统设置） ----------
$testSrc = $core + $plugins + (Get-ChildItem "$root\src\TouchPlus.Test\*.cs" | ForEach-Object { $_.FullName })
& $csc /nologo /target:exe /platform:anycpu /out:"$root\build\TouchPlus.Test.exe" @refs @testSrc
Write-Host "已生成自检程序 build\TouchPlus.Test.exe" -ForegroundColor Green

if ($Test) {
    & "$root\build\TouchPlus.Test.exe" logic
    exit $LASTEXITCODE
}

# ---------- 正式程序 ----------
New-Item -ItemType Directory -Force -Path "$root\$OutDir" | Out-Null
$manifest = "$root\src\TouchPlus\app.manifest"
$icon = "$root\assets\TouchPlus.ico"

$args = @('/nologo', '/target:winexe', '/platform:anycpu', "/out:$root\$OutDir\TouchPlus.exe")
if (Test-Path $manifest) { $args += "/win32manifest:$manifest" }
if (Test-Path $icon)     { $args += "/win32icon:$icon"; $args += "/resource:$icon,TouchPlus.ico" }
$args += $refs
$args += ($core + $plugins + $ui + $controls + @("$root\src\TouchPlus\Program.cs") + $props)

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

# ---------- 验证工具（观察模式，可选） ----------
$verifySrc = $core + $plugins + (Get-ChildItem "$root\src\TouchPlus.Verify\*.cs" -Recurse | ForEach-Object { $_.FullName })
& $csc /nologo /target:winexe /platform:anycpu /out:"$root\$OutDir\TouchPlusVerify.exe" `
    "/win32manifest:$root\src\TouchPlus.Verify\app.manifest" @refs @verifySrc

$exe = Get-Item "$root\$OutDir\TouchPlus.exe"
Write-Host ""
Write-Host "构建完成：" -ForegroundColor Green
Write-Host ("  {0}  ({1:N0} 字节)" -f $exe.FullName, $exe.Length)
Write-Host ""
Write-Host "运行自检：  .\build\TouchPlus.Test.exe logic   （纯逻辑，绝对安全）"
Write-Host "启动程序：  .\$OutDir\TouchPlus.exe --safe --show   （安全模式，只开界面不改系统）"
