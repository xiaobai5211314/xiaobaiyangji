param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

$src = Join-Path $PSScriptRoot "..\覆盖到项目\Services\StockQuoteService.cs"
$dst = Join-Path $ProjectRoot "Services\StockQuoteService.cs"

if (!(Test-Path $src)) {
    throw "找不到补丁文件: $src"
}

if (Test-Path $dst) {
    $backup = "$dst.bak_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Copy-Item $dst $backup
    Write-Host "已备份: $backup"
}

Copy-Item $src $dst -Force
Write-Host "已覆盖: Services\StockQuoteService.cs"
Write-Host "完成。请执行: dotnet build"
