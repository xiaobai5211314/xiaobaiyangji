param(
    [string]$Url = "https://guzhi.21212121.xyz/index.html"
)

$ErrorActionPreference = "Stop"

$content = Invoke-WebRequest -Uri ($Url + "?_t=" + [DateTimeOffset]::Now.ToUnixTimeMilliseconds()) -UseBasicParsing | Select-Object -ExpandProperty Content

if ($content -notmatch 'jumpToTodayAnalysis') {
    throw "线上 index.html 还没有 jumpToTodayAnalysis。说明没发布成功，或 CDN/浏览器缓存还没刷新。"
}

if ($content -notmatch '@click\.prevent\.stop="jumpToTodayAnalysis"') {
    throw "线上按钮还不是 jumpToTodayAnalysis。说明没发布成功，或 index.html 不是这版。"
}

Write-Host "线上校验通过：今天按钮代码已上线。"
