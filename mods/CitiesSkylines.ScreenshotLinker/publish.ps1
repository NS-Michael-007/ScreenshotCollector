# 发布模组到游戏本地模组目录（复制 DLL）
# 用法：右键用 PowerShell 运行，或 pwsh -File publish.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDll = Join-Path $root "bin\Debug\CitiesSkylines.ScreenshotLinker.dll"
$dstDir = Join-Path $env:LOCALAPPDATA "Colossal Order\Cities_Skylines\Addons\Mods\CitiesSkylines.ScreenshotLinker"

if (-not (Test-Path -LiteralPath $srcDll)) {
    Write-Host "未找到编译产物，请先编译：dotnet build $root\CitiesSkylines.ScreenshotLinker.csproj" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
Copy-Item -LiteralPath $srcDll -Destination $dstDir -Force

$pdb = Join-Path $root "bin\Debug\CitiesSkylines.ScreenshotLinker.pdb"
if (Test-Path -LiteralPath $pdb) {
    Copy-Item -LiteralPath $pdb -Destination $dstDir -Force
}

Write-Host "已发布到：$dstDir" -ForegroundColor Green
Write-Host "在游戏 内容管理器 中启用「截图自动收集器联动」即可。" -ForegroundColor Green
