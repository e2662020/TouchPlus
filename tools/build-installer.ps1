# 构建 Touch+ 安装程序：单文件 exe，按用户安装（无需管理员）
param(
    [string]$OutDir = "dist"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Get-Csc {
    foreach ($p in @(
        "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe")) {
        if (Test-Path $p) { return $p }
    }
    throw "找不到 csc.exe，请先安装 .NET Framework 4.x"
}

$csc = Get-Csc
$hostExe = Join-Path $root "$OutDir\Touch+\TouchPlus.exe"
$pluginDll = Join-Path $root "$OutDir\Touch+\plugins\TouchPlus.Plugins.DoubleClick.dll"
$icon = Join-Path $root 'assets\TouchPlus.ico'
$readme = Join-Path $root 'README.md'

if (-not (Test-Path $hostExe) -or -not (Test-Path $pluginDll)) {
    Write-Host "缺少宿主或插件产物，先执行 build.ps1…" -ForegroundColor Yellow
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -OutDir "$OutDir\Touch+"
}

$setupExe = Join-Path $root "$OutDir\TouchPlusSetup.exe"
$sources = @((Join-Path $root 'installer\Setup.cs'))

$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', "/out:$setupExe",
    "/win32icon:$icon",
    "/resource:$hostExe,TouchPlus.exe",
    "/resource:$pluginDll,TouchPlus.Plugins.DoubleClick.dll",
    "/resource:$icon,TouchPlus.ico",
    "/resource:$readme,README.md",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Security.dll'
)
$args += $sources

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败" }

$f = Get-Item $setupExe
Write-Host "安装程序已生成：$($f.FullName)（$([math]::Round($f.Length/1KB)) KB）" -ForegroundColor Green
Write-Host "用法：TouchPlusSetup.exe            图形安装"
Write-Host "      TouchPlusSetup.exe --silent   静默安装"
Write-Host "      TouchPlusSetup.exe --uninstall 卸载"