param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

function Backup-And-Copy($relativePath) {
    $src = Join-Path $PSScriptRoot ("..\覆盖到项目\" + $relativePath)
    $dst = Join-Path $ProjectRoot $relativePath

    if (!(Test-Path $src)) {
        throw "补丁文件不存在: $src"
    }

    if (Test-Path $dst) {
        $backup = "$dst.bak_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
        Copy-Item $dst $backup
        Write-Host "备份: $backup"
    }

    $folder = Split-Path $dst -Parent
    if (!(Test-Path $folder)) {
        New-Item -ItemType Directory -Path $folder | Out-Null
    }

    Copy-Item $src $dst -Force
    Write-Host "已覆盖: $relativePath"
}

Backup-And-Copy "Services\StockQuoteService.cs"
Backup-And-Copy "Controllers\StockController.cs"
Backup-And-Copy "Models\AppDbContext.cs"

$parserPath = Join-Path $ProjectRoot "Services\StockOcrParserService.cs"
if (Test-Path $parserPath) {
    $backup = "$parserPath.bak_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Copy-Item $parserPath $backup
    $text = Get-Content $parserPath -Raw -Encoding UTF8
    $text = $text.Replace('private static readonly Regex CodeRegex = new(@"(?<!\d)([0-9]{6})(?!\d)", RegexOptions.Compiled);',
                          'private static readonly Regex CodeRegex = new(@"(?<!\d)([0-9]{5,6})(?!\d)", RegexOptions.Compiled);')
    Set-Content $parserPath $text -Encoding UTF8
    Write-Host "已修改 StockOcrParserService.cs：股票代码识别支持 5 位港股代码"
}

Write-Host "完成。请执行: dotnet build"