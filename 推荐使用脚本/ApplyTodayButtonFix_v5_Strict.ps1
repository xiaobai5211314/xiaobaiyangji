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

$oldButton = '<button class="btn btn-primary" @click="selectedAnalysisDate = todayDashStr">今天</button>'
$newButton = '<button type="button" class="btn btn-primary" @click.prevent.stop="jumpToTodayAnalysis">今天</button>'

if (!$text.Contains($oldButton) -and !$text.Contains('@click.prevent.stop="jumpToTodayAnalysis"')) {
    throw "没有找到旧的今天按钮代码。请不要继续发布，先把当前 wwwroot/index.html 发我。"
}

$text = $text.Replace($oldButton, $newButton)

# 清理之前可能插入失败的旧函数。
$text = [regex]::Replace($text, "\s*const getLocalTodayDash = \(\) => \{.*?\n\s*const jumpToTodayAnalysis = async \(\) => \{.*?\n\s*\};\r?\n", "`n", "Singleline")
$text = [regex]::Replace($text, "\s*const jumpToTodayAnalysis = async \(\) => \{.*?\n\s*\};\r?\n", "`n", "Singleline")

$anchor = "                const selectedAnalysisDate = ref(todayDashStr);`n"
if (!$text.Contains($anchor)) {
    throw "没有找到 selectedAnalysisDate 定义。脚本停止，避免误改。"
}

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

                    if (activePage.value !== 'analysis') {
                        activePage.value = 'analysis';
                    }

                    await fetchAnalysis(true, {
                        keepSelectedDate: true,
                        targetDate: today
                    });

                    analysisMonth.value = today.slice(0, 7);
                    selectedAnalysisDate.value = today;
                    showToast(`📅 已跳转到今天 ${today}`, 'success');
                };

'@

$text = $text.Replace($anchor, $anchor + $helper)

$oldFetch = "                const fetchAnalysis = async (force = false) => {`n                    if (!currentUser.value) return;`n                    if (analysisRecords.value.length > 0 && !force) return;"
$newFetch = @'
                const fetchAnalysis = async (force = false, options = {}) => {
                    if (!currentUser.value) return;

                    const keepSelectedDate = options === true || !!options.keepSelectedDate;
                    const targetDate = typeof options === 'object' && options.targetDate
                        ? options.targetDate
                        : selectedAnalysisDate.value;

                    if (analysisRecords.value.length > 0 && !force) return;
'@

if (!$text.Contains($oldFetch) -and !$text.Contains("const fetchAnalysis = async (force = false, options = {})")) {
    throw "没有找到 fetchAnalysis 旧函数头。脚本停止，避免误改。"
}

$text = $text.Replace($oldFetch, $newFetch)

$oldAutoSelect = @'
                        if (monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {
                            selectedAnalysisDate.value = String(monthRecords[0].recordDate || '').slice(0, 10);
                        }
'@

$newAutoSelect = @'
                        if (!keepSelectedDate && monthRecords.length > 0 && !monthRecords.some(r => String(r.recordDate || '').slice(0, 10) === selectedAnalysisDate.value)) {
                            selectedAnalysisDate.value = String(monthRecords[0].recordDate || '').slice(0, 10);
                        }

                        if (keepSelectedDate && targetDate) {
                            selectedAnalysisDate.value = targetDate;
                        }
'@

if (!$text.Contains($newAutoSelect)) {
    if (!$text.Contains($oldAutoSelect)) {
        throw "没有找到 fetchAnalysis 自动选日期代码。脚本停止，避免误改。"
    }
    $text = $text.Replace($oldAutoSelect, $newAutoSelect)
}

$oldReturn = 'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, todayDashStr,'
$newReturn = 'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, jumpToTodayAnalysis, todayDashStr,'

if (!$text.Contains($newReturn)) {
    if (!$text.Contains($oldReturn)) {
        throw "没有找到 return 中的 analysis 项。脚本停止，避免误改。"
    }
    $text = $text.Replace($oldReturn, $newReturn)
}

# 修正 CDN 域名，避免静态资源走主站。
$cdn = 'https://guzhicdn.21212121.xyz'
$text = $text.Replace('src="/vendor/vue.global.prod.min.js"', 'src="' + $cdn + '/vendor/vue.global.prod.min.js"')
$text = $text.Replace('src="/vendor/echarts.min.js"', 'src="' + $cdn + '/vendor/echarts.min.js"')
$text = $text.Replace("const ECHARTS_LOCAL_URL = '/vendor/echarts.min.js';", "const ECHARTS_LOCAL_URL = '" + $cdn + "/vendor/echarts.min.js';")

if ($text -notmatch '修复：手机端盈亏日历周六/周日收益贴边') {
    $text = $text.Replace('grid-template-columns: repeat(7, 1fr);', 'grid-template-columns: repeat(7, minmax(0, 1fr));')
    $css = @'

        /* 修复：手机端盈亏日历周六/周日收益贴边 */
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
    $text = $text.Replace('</style>', $css + "`n    </style>")
}

# 应用后校验，失败直接中止。
if ($text -notmatch '@click\.prevent\.stop="jumpToTodayAnalysis"') { throw "校验失败：按钮没有改成 jumpToTodayAnalysis" }
if ($text -notmatch 'const jumpToTodayAnalysis = async \(\)') { throw "校验失败：没有 jumpToTodayAnalysis 函数" }
if ($text -notmatch 'const fetchAnalysis = async \(force = false, options = \{\}\)') { throw "校验失败：fetchAnalysis 没有 options 参数" }
if ($text -notmatch 'fetchAnalysis, jumpToTodayAnalysis, todayDashStr') { throw "校验失败：return 没有暴露 jumpToTodayAnalysis" }

Set-Content $IndexPath $text -Encoding UTF8

Write-Host "完成：今天按钮已修复。"
Write-Host "下一步：重新发布，确认 GitHub Actions 成功；如果部署失败，线上仍是旧代码。"
