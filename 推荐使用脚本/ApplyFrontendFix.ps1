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

$text = $text.Replace('@click="selectedAnalysisDate = todayDashStr">今天</button>', '@click="jumpToTodayAnalysis">今天</button>')

if ($text -notmatch 'const jumpToTodayAnalysis') {
    $needle = "                const selectedAnalysisDate = ref(todayDashStr);`n"
    $insert = @'
                const jumpToTodayAnalysis = async () => {
                    analysisMonth.value = todayDashStr.slice(0, 7);
                    selectedAnalysisDate.value = todayDashStr;
                    await nextTick();
                    await fetchAnalysis(true);
                };
'@
    $text = $text.Replace($needle, $needle + $insert)
}

$text = $text.Replace(
    'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, todayDashStr,',
    'analysisMonth, selectedAnalysisDate, analysisViewMode, analysisLoading, analysisRecords, analysisCalendarDays, selectedDayFundRecords, analysisProfitTop, analysisLossTop, analysisMonthTotal, fetchAnalysis, jumpToTodayAnalysis, todayDashStr,'
)

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
    $text = $text.Replace('        /* 本轮：资金流只在板块页展示 + 数字统一微软雅黑等宽数字 */', $css + "`n`n        /* 本轮：资金流只在板块页展示 + 数字统一微软雅黑等宽数字 */")
}

Set-Content $IndexPath $text -Encoding UTF8
Write-Host "完成：已修复今天按钮和手机端盈亏日历周六/周日贴边问题。"