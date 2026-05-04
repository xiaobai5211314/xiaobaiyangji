param(
    [string]$IndexPath = ".\wwwroot\index.html"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $IndexPath)) {
    throw "找不到 index.html: $IndexPath"
}

$backup = "$IndexPath.bak_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Copy-Item $IndexPath $backup
Write-Host "已备份: $backup"

$text = Get-Content $IndexPath -Raw -Encoding UTF8

$cdn = "https://guzhicdn.21212121.xyz"

$text = $text.Replace('src="/vendor/vue.global.prod.min.js"', 'src="' + $cdn + '/vendor/vue.global.prod.min.js"')
$text = $text.Replace("src='/vendor/vue.global.prod.min.js'", "src='" + $cdn + "/vendor/vue.global.prod.min.js'")
$text = $text.Replace('src="/vendor/echarts.min.js"', 'src="' + $cdn + '/vendor/echarts.min.js"')
$text = $text.Replace("src='/vendor/echarts.min.js'", "src='" + $cdn + "/vendor/echarts.min.js'")

$text = $text.Replace("const ECHARTS_LOCAL_URL = '/vendor/echarts.min.js';", "const ECHARTS_LOCAL_URL = '" + $cdn + "/vendor/echarts.min.js';")
$text = $text.Replace('const ECHARTS_LOCAL_URL = "/vendor/echarts.min.js";', 'const ECHARTS_LOCAL_URL = "' + $cdn + '/vendor/echarts.min.js";')

$text = $text.Replace('/vendor/vue.global.prod.min.js', $cdn + '/vendor/vue.global.prod.min.js')
$text = $text.Replace('/vendor/echarts.min.js', $cdn + '/vendor/echarts.min.js')

if ($text -notmatch 'https://guzhicdn\.21212121\.xyz') {
    $text = $text.Replace(
        '<link rel="preconnect" href="https://guzhi.21212121.xyz" crossorigin>',
        '<link rel="preconnect" href="https://guzhi.21212121.xyz" crossorigin>' + "`n    " + '<link rel="preconnect" href="https://guzhicdn.21212121.xyz" crossorigin>'
    )
}

Set-Content $IndexPath $text -Encoding UTF8
Write-Host "完成：vendor 静态资源已改为 guzhicdn.21212121.xyz"
Write-Host "请刷新 CDN 缓存并浏览器 Ctrl+F5。"
