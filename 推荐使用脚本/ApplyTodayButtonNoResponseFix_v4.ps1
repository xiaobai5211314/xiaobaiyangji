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

# 1. 今天按钮不再只改 selectedAnalysisDate，而是调用专用函数。
$text = [regex]::Replace($text, '@click\s*=\s*"selectedAnalysisDate\s*=\s*todayDashStr"', '@click="jumpToTodayAnalysis"')

# 2. 清理旧版 jumpToTodayAnalysis，避免重复定义。
$text = [regex]::Replace($text, "                const getLocalTodayDash = \(\) => \{.*?                \};\r?\n\r?\n                const jumpToTodayAnalysis = async \(\) => \{.*?                \};\r?\n\r?\n", "", "Singleline")
$text = [regex]::Replace($text, "                const jumpToTodayAnalysis = async \(\) => \{.*?                \};\r?\n", "", "Singleline")

$helper = @'
                const getLocalTodayDash = () => {
                    const d = new Date();
                    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
                };

                const jumpToTodayAnalysis = async () => {
                    const today = getLocalTodayDash();
                    analysisMonth.value = today.slice(0, 7);
                    selectedAnalysisDate.value = today;
                    await nextTick();
                    await fetchAnalysis(true, { keepSelectedDate: true, targetDate: today });
                    selectedAnalysisDate.value = today;
                };

'@

$text = $text.Replace("                const selectedAnalysisDate = ref(todayDashStr);`n", "                const selectedAnalysisDate = ref(todayDashStr);`n" + $helper)

# 3. fetchAnalysis 支持 keepSelectedDate，避免刷新后又自动跳到当前月份第一条有记录的日期。
$text = $text.Replace(
    'const fetchAnalysis = async (force = false) => {',
    'const fetchAnalysis = async (force = false, options = {}) => {'
)

$text = $text.Replace(
    "if (!currentUser.value) return;`n                    if (analysisRecords.value.length > 0 && !force) return;",
    "if (!currentUser.value) return;`n                    const keepSelectedDate = options === true || !!options.keepSelectedDate;`n                    const targetDate = typeof options === 'object' && options.targetDate`n                        ? options.targetDate`n                        : selectedAnalysisDate.value;`n                    if (analysisRecords.value.length > 0 && !force) return;"
)

$text = $text.Replace(
    "if (monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {",
    "if (!keepSelectedDate && monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {"
)

$text = $text.Replace(
    "                        if (!keepSelectedDate && monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {`n                            selectedAnalysisDate.value = String(monthRecords[0].recordDate || '').slice(0, 10);`n                        }",
    "                        if (!keepSelectedDate && monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {`n                            selectedAnalysisDate.value = String(monthRecords[0].recordDate || '').slice(0, 10);`n                        }`n                        if (keepSelectedDate && targetDate) {`n                            selectedAnalysisDate.value = targetDate;`n                        }"
)

# 4. 暴露给模板。
$text = $text.Replace(
    'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, todayDashStr,',
    'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, jumpToTodayAnalysis, todayDashStr,'
)

# 5. 手机端日历数字贴边修复。
$text = $text.Replace('grid-template-columns: repeat(7, 1fr);', 'grid-template-columns: repeat(7, minmax(0, 1fr));')

if ($text -notmatch '本轮修复：手机端盈亏日历周六/周日数字贴边、溢出') {
    $css = @'

        /* 本轮修复：手机端盈亏日历周六/周日数字贴边、溢出 */
        .calendar-grid {
            width: 100%;
            max-width: 100%;
            box-sizing: border-box;
        }

        .calendar-day {
            min-width: 0;
            box-sizing: border-box;
            overflow: hidden;
        }

        .calendar-profit {
            max-width: 100%;
            min-width: 0;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: clip;
            line-height: 1.15;
            letter-spacing: -0.2px;
        }

        @media screen and (max-width: 768px) {
            .analysis-grid > .glass-panel {
                min-width: 0;
                overflow: hidden;
            }

            .calendar-grid {
                gap: 3px;
                padding: 0 2px;
            }

            .calendar-day {
                min-height: 54px;
                padding: 6px 3px;
                border-radius: 10px;
            }

            .calendar-date {
                font-size: 11px;
                line-height: 1.1;
            }

            .calendar-profit {
                font-size: 10px;
                letter-spacing: -0.6px;
            }
        }

        @media screen and (max-width: 390px) {
            .calendar-grid {
                gap: 2px;
                padding: 0 1px;
            }

            .calendar-day {
                padding: 5px 2px;
                border-radius: 9px;
            }

            .calendar-profit {
                font-size: 9px;
            }
        }
'@
    if ($text.Contains('        /* 本轮：资金流只在板块页展示 + 数字统一微软雅黑等宽数字 */')) {
        $text = $text.Replace('        /* 本轮：资金流只在板块页展示 + 数字统一微软雅黑等宽数字 */', $css + "`n`n        /* 本轮：资金流只在板块页展示 + 数字统一微软雅黑等宽数字 */")
    } else {
        $text = $text.Replace('</style>', $css + "`n    </style>")
    }
}

# 6. 顺手保留正确 CDN 域名，避免 vendor 资源又走后端域名。
$cdn = 'https://guzhicdn.21212121.xyz'
$text = $text.Replace('src="/vendor/vue.global.prod.min.js"', 'src="' + $cdn + '/vendor/vue.global.prod.min.js"')
$text = $text.Replace('src="/vendor/echarts.min.js"', 'src="' + $cdn + '/vendor/echarts.min.js"')
$text = $text.Replace("const ECHARTS_LOCAL_URL = '/vendor/echarts.min.js';", "const ECHARTS_LOCAL_URL = '" + $cdn + "/vendor/echarts.min.js';")

Set-Content $IndexPath $text -Encoding UTF8

Write-Host "完成：今天按钮已修复，点击后会切换到今天所在月份并保留今天选中态。"
