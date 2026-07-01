using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using 小白养基.Models;

namespace 小白养基.Services
{
    public class NavSettlementService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NavSettlementService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;

        public NavSettlementService(IServiceProvider serviceProvider, ILogger<NavSettlementService> logger, IMemoryCache cache, IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _cache = cache;
            _httpClient = httpClientFactory.CreateClient("EastMoney");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("夜间净值清算服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var localTime = ChinaNow();
                    if ((localTime.Hour >= 17 && localTime.Hour <= 23) || localTime.Hour < 2)
                    {
                        await SettleTodayNavAsync(localTime, stoppingToken);
                    }

                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // 正常停止，不视为错误
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "夜间清算主循环异常");
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        private static DateTime GetPreviousTradeDate(DateTime date)
            => MarketCalendar.GetPreviousTradingDate(date.AddDays(-1));

        private static double GetActivePendingBuyAmount(MyFundConfig fund, string settleDate)
            => PortfolioSettlementService.GetActivePendingBuyAmount(fund, settleDate);

        private static double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            double baseAmount = fund.HoldAmount;
            baseAmount -= GetActivePendingBuyAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        private static double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetActivePendingBuyAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                pending += fund.LastAddAmount;
            }
            return Math.Round(pending, 2);
        }

        private static double GetEffectiveShares(MyFundConfig fund, string settleDate)
        {
            if (fund.HoldShares <= 0) return 0;

            double baseAmount = GetEffectiveBaseAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && Math.Abs(fund.LastAddAmount) > 0.000001 && fund.HoldAmount > 0)
            {
                return Math.Max(0, fund.HoldShares * (baseAmount / fund.HoldAmount));
            }

            return fund.HoldShares;
        }

        private sealed class OfficialNavSnapshot
        {
            public string Date { get; set; } = string.Empty;
            public double Rate { get; set; }
            public double? TodayNav { get; set; }
            public double? YesterdayNav { get; set; }
            public double? NavDiff { get; set; }
            public string Source { get; set; } = string.Empty;
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.Number) return prop.TryGetDouble(out value);
            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text) || text == "--") return false;
                return double.TryParse(text, out value);
            }
            return false;
        }

        private static OfficialNavSnapshot? TryBuildOfficialNavSnapshot(JsonElement dataArray, string settleDate)
        {
            if (dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() == 0) return null;

            if (!DateTime.TryParse(settleDate, out var expectedDate)) return null;
            var selectedIndex = -1;
            for (var i = 0; i < dataArray.GetArrayLength(); i++)
            {
                var dateText = dataArray[i].TryGetProperty("FSRQ", out var dateElement)
                    ? dateElement.GetString()
                    : null;
                if (DateTime.TryParse(dateText, out var navDate) && navDate.Date <= expectedDate.Date)
                {
                    selectedIndex = i;
                    break;
                }
            }
            if (selectedIndex < 0) return null;

            var latest = dataArray[selectedIndex];
            string fsrq = latest.TryGetProperty("FSRQ", out var fsrqElement) ? (fsrqElement.GetString() ?? string.Empty) : string.Empty;

            double? apiRate = TryGetDouble(latest, "JZZZL", out var parsedRate) ? parsedRate : null;
            double? navRate = null;
            double? todayNav = null;
            double? yesterdayNav = null;
            double? navDiff = null;

            if (selectedIndex + 1 < dataArray.GetArrayLength() &&
                TryGetDouble(latest, "DWJZ", out var navToday) &&
                TryGetDouble(dataArray[selectedIndex + 1], "DWJZ", out var navYesterday) &&
                navYesterday > 0)
            {
                todayNav = navToday;
                yesterdayNav = navYesterday;
                navDiff = navToday - navYesterday;
                navRate = Math.Round(navDiff.Value / navYesterday * 100.0, 4);
            }

            double? finalRate = null;
            string source = string.Empty;

            if (navRate.HasValue && (!apiRate.HasValue ||
                                     (Math.Abs(apiRate.Value) < 0.000001 && Math.Abs(navRate.Value) > 0.000001) ||
                                     Math.Abs(apiRate.Value - navRate.Value) > 0.05))
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }
            else if (apiRate.HasValue)
            {
                finalRate = apiRate.Value;
                source = "JZZZL";
            }
            else if (navRate.HasValue)
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }

            // 只有接口给了 0、但没有前后两个单位净值可校验时，不打“净值确认”标签。
            // 否则旧版本会把 0 当成真实涨跌幅写入，造成“净值确认 0%”。
            if (!finalRate.HasValue) return null;
            if (Math.Abs(finalRate.Value) < 0.000001 && !navRate.HasValue) return null;

            return new OfficialNavSnapshot
            {
                Date = fsrq,
                Rate = Math.Round(finalRate.Value, 4),
                TodayNav = todayNav,
                YesterdayNav = yesterdayNav,
                NavDiff = navDiff,
                Source = source
            };
        }

        private static bool ApplyOneDaySettlement(
      MyFundConfig fund,
      double actualRate,
      string settleDate,
      double? exactProfit = null,
      double? exactAssets = null)
        {
            double beforeHoldAmount = fund.HoldAmount;
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            double settledProfit = fund.OcrYesterdayDate == settleDate
                ? Math.Round(fund.OcrYesterdayIncome, 2)
                : Math.Round(exactProfit ?? (baseAmount * actualRate / 100.0), 2);
            double activePendingBuyAmount = GetActivePendingBuyAmount(fund, settleDate);
            double settledDisplayAmount = PortfolioAccounting.ToDouble(
                PortfolioAccounting.ResolveSettledDisplayAmount(
                    Convert.ToDecimal(baseAmount),
                    Convert.ToDecimal(settledProfit),
                    Convert.ToDecimal(activePendingBuyAmount),
                    exactAssets.HasValue ? Convert.ToDecimal(exactAssets.Value) : null));

            Console.WriteLine(
                $"[官方净值落库] code={fund.FundCode}, beforeHoldAmount={beforeHoldAmount:F2}, baseAmount={baseAmount:F2}, settledProfit={settledProfit:F2}, exactAssets={exactAssets:F2}, nextHoldAmount={settledDisplayAmount:F2}; HoldAmount随净值滚动");

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.HoldAmount - settledDisplayAmount) > 0.004;

            if (!changed) return false;

            fund.HoldAmount = settledDisplayAmount;
            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);

            return true;
        }
        private static double GetDailyBaseAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetPendingTradeAmount(fund, settleDate);
            if (fund.LastSettledDate == settleDate)
            {
                return Math.Max(0, Math.Round(fund.HoldAmount - pending - fund.LastSettledProfit, 4));
            }
            return GetEffectiveBaseAmount(fund, settleDate);
        }

        private async Task SettleTodayNavAsync(DateTime localTime, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 0:00~16:59 期间，东方财富最新净值仍是上一个交易日的，必须用上一个交易日
            // 17:00~23:59 期间，当天净值已出，用当天
            string settleDate = localTime.Hour < 17
                ? GetPreviousTradeDate(localTime.Date).ToString("yyyy-MM-dd")
                : localTime.ToString("yyyy-MM-dd");
            var settleDateDt = DateTime.Parse(settleDate);
            var todayStart = settleDateDt;
            var tomorrowStart = settleDateDt.AddDays(1);

            var targetFunds = await dbContext.MyFunds
                .Select(f => f.FundCode)
                .Distinct()
                .ToListAsync(stoppingToken);

            // 优化3：一次性获取所有持仓，避免N+1查询
            var allHoldings = await dbContext.MyFunds
                .Where(f => targetFunds.Contains(f.FundCode))
                .ToListAsync(stoppingToken);

            var holdingsByCode = allHoldings.GroupBy(f => f.FundCode)
                .ToDictionary(g => g.Key, g => g.ToList());

            var todayRecordList = await dbContext.FundRecords
                .Where(r => r.FetchTime >= todayStart.AddDays(-10) && r.FetchTime < tomorrowStart && targetFunds.Contains(r.FundCode))
                .ToListAsync(stoppingToken);

            foreach (var code in targetFunds)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=2&_={timestamp}";
                    string response = await _httpClient.GetStringAsync(url, stoppingToken);

                    using var doc = JsonDocument.Parse(response);
                    if (!doc.RootElement.TryGetProperty("Data", out var data) ||
                        !data.TryGetProperty("LSJZList", out var dataArray) ||
                        dataArray.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var navSnapshot = TryBuildOfficialNavSnapshot(dataArray, settleDate);
                    if (navSnapshot == null)
                    {
                        continue;
                    }

                    double actualRate = navSnapshot.Rate;
                    string actualSettleDate = navSnapshot.Date;
                    DateTime actualDate = DateTime.Parse(actualSettleDate).Date;
                    DateTime actualDateEnd = actualDate.AddDays(1);

                    var targetRecord = todayRecordList
                        .Where(r => r.FundCode == code && !r.IsOfficial && r.FetchTime >= actualDate && r.FetchTime < actualDateEnd)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefault();

                    if (targetRecord != null)
                    {
                        targetRecord.ActualRate = actualRate;
                        targetRecord.DiffRate = Math.Round(actualRate - targetRecord.EstimatedRate, 2);
                    }

                    // 写入官方净值 FundRecord（IsOfficial=true），用于 /today 接口判断 dataStatus
                    bool hasOfficialToday = todayRecordList.Any(r =>
                        r.FundCode == code && r.IsOfficial && r.NavDate == actualSettleDate);
                    if (!hasOfficialToday)
                    {
                        string fundName = holdingsByCode.TryGetValue(code, out var holdings) && holdings.Count > 0
                            ? holdings[0].FundName : code;
                        var officialRecord = new FundData
                        {
                            FundCode = code,
                            FundName = fundName,
                            EstimatedRate = actualRate,
                            ActualRate = actualRate,
                            FetchTime = ChinaNow(),
                            NavDate = actualSettleDate,
                            Nav = navSnapshot.TodayNav,
                            Source = "official-nav",
                            IsOfficial = true
                        };
                        dbContext.FundRecords.Add(officialRecord);
                        todayRecordList.Add(officialRecord);
                    }

                    // 使用预加载的持仓数据
                    if (holdingsByCode.TryGetValue(code, out var holdingUsers))
                    {
                        foreach (var holding in holdingUsers)
                        {
                            double? exactProfit = null;
                            double? exactAssets = null;

                            double effectiveShares = GetEffectiveShares(holding, actualSettleDate);

                            if (effectiveShares > 0)
                            {
                                if (navSnapshot.NavDiff.HasValue)
                                {
                                    exactProfit = Math.Round(effectiveShares * navSnapshot.NavDiff.Value, 2);
                                }

                                if (navSnapshot.TodayNav.HasValue && navSnapshot.TodayNav.Value > 0)
                                {
                                    exactAssets = Math.Round(effectiveShares * navSnapshot.TodayNav.Value, 2);
                                }
                            }

                            if (ApplyOneDaySettlement(holding, actualRate, actualSettleDate, exactProfit, exactAssets))
                            {
                                _logger.LogInformation(
                                    "清算完成 {FundName}({Code}) {Rate}% [{Source}] -> {Amount} ExactAssets={ExactAssets}",
                                    holding.FundName,
                                    code,
                                    actualRate,
                                    navSnapshot.Source,
                                    holding.HoldAmount,
                                    exactAssets);
                            }

                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清算 {Code} 失败", code);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            await CleanupOldRecordsAsync(dbContext, stoppingToken);
        }

        private async Task CleanupOldRecordsAsync(AppDbContext dbContext, CancellationToken stoppingToken)
        {
            try
            {
                var deadline = ChinaNow().Date.AddDays(-10);
                var oldRecords = await dbContext.FundRecords
                    .Where(r => r.FetchTime < deadline)
                    .ToListAsync(stoppingToken);

                if (oldRecords.Count == 0) return;

                dbContext.FundRecords.RemoveRange(oldRecords);
                await dbContext.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("已清理 {Count} 条过期估值记录", oldRecords.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "估值记录清理失败");
            }
        }
    }
}
