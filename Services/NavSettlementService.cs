using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using 估值助手.Models;

namespace 估值助手.Services
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
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "夜间清算主循环异常");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        private static double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            double baseAmount = fund.HoldAmount;
            if (fund.LastTradeDate == settleDate)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        private static double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
        {
            return fund.LastTradeDate == settleDate ? fund.LastAddAmount : 0;
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

            var latest = dataArray[0];
            string fsrq = latest.TryGetProperty("FSRQ", out var fsrqElement) ? (fsrqElement.GetString() ?? string.Empty) : string.Empty;
            if (fsrq != settleDate) return null;

            double? apiRate = TryGetDouble(latest, "JZZZL", out var parsedRate) ? parsedRate : null;
            double? navRate = null;
            double? todayNav = null;
            double? yesterdayNav = null;
            double? navDiff = null;

            if (dataArray.GetArrayLength() > 1 &&
                TryGetDouble(latest, "DWJZ", out var navToday) &&
                TryGetDouble(dataArray[1], "DWJZ", out var navYesterday) &&
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
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            double pending = GetPendingTradeAmount(fund, settleDate);

            double settledProfit;
            double newHoldAmount;

            if (exactAssets.HasValue && exactAssets.Value > 0)
            {
                double exactMarketAmount = Math.Round(exactAssets.Value, 2);

                newHoldAmount = Math.Round(exactMarketAmount + pending, 2);
                settledProfit = Math.Round(exactProfit ?? (exactMarketAmount - baseAmount), 2);
            }
            else
            {
                settledProfit = Math.Round(exactProfit ?? (baseAmount * (actualRate / 100.0)), 2);
                newHoldAmount = Math.Round(baseAmount + settledProfit + pending, 2);
            }

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.HoldAmount - newHoldAmount) > 0.01;

            if (!changed) return false;

            fund.HoldAmount = newHoldAmount;
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

        private static double GetRecordRateForToday(FundData? record)
        {
            if (record == null) return 0;
            return Math.Abs(record.ActualRate) > 0.000001 ? record.ActualRate : record.EstimatedRate;
        }

        private static void CopyArchiveValues(DailyArchive target, DailyArchive source)
        {
            target.FundName = string.IsNullOrWhiteSpace(source.FundName) ? target.FundName : source.FundName;
            target.Assets = Math.Round(source.Assets, 2);
            target.DailyProfit = Math.Round(source.DailyProfit, 2);
            target.DailyRate = Math.Round(source.DailyRate, 2);
            target.TotalProfit = Math.Round(source.TotalProfit, 2);
            target.TotalRate = Math.Round(source.TotalRate, 2);
        }

        private static List<DailyArchive> BuildArchiveRowsFromCurrentHoldings(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            string dateDash = date.ToString("yyyy-MM-dd");
            var latestRecordDict = todayRecords
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0;
            double totalRealized = 0;
            double totalDailyProfit = 0;
            double totalDailyBase = 0;
            double totalCurrentAssets = 0;

            foreach (var fund in funds)
            {
                latestRecordDict.TryGetValue(fund.FundCode, out var record);

                double cost = fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount;
                double baseAmount = GetDailyBaseAmount(fund, dateDash);
                double dailyRate = fund.LastSettledDate == dateDash ? fund.LastSettledRate : GetRecordRateForToday(record);
                double dailyProfit = fund.LastSettledDate == dateDash
                    ? fund.LastSettledProfit
                    : Math.Round(baseAmount * (dailyRate / 100.0), 2);
                double currentAssets = fund.LastSettledDate == dateDash
                    ? fund.HoldAmount
                    : Math.Round(fund.HoldAmount + dailyProfit, 2);

                double totalProfit = currentAssets - cost + fund.RealizedProfit;
                double totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;

                rows.Add(new DailyArchive
                {
                    Username = username,
                    FundCode = fund.FundCode,
                    FundName = fund.FundName,
                    RecordDate = date,
                    Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2),
                    DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalRate = Math.Round(totalRate, 2)
                });

                totalCost += cost;
                totalRealized += fund.RealizedProfit;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
            }

            rows.Add(new DailyArchive
            {
                Username = username,
                FundCode = "TOTAL",
                FundName = "总持仓",
                RecordDate = date,
                Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0, 2),
                TotalProfit = Math.Round(totalCurrentAssets - totalCost + totalRealized, 2),
                TotalRate = Math.Round(totalCost > 0 ? (totalCurrentAssets - totalCost + totalRealized) / totalCost * 100.0 : 0, 2)
            });

            return rows;
        }

        private static async Task UpsertDailyArchivesAsync(AppDbContext dbContext, string username, DateTime date, IEnumerable<DailyArchive> incoming, CancellationToken stoppingToken)
        {
            var normalizedIncoming = incoming
                .Where(x => !string.IsNullOrWhiteSpace(x.FundCode))
                .GroupBy(x => x.FundCode)
                .Select(g => g.Last())
                .ToList();
            if (normalizedIncoming.Count == 0) return;

            var codes = normalizedIncoming.Select(x => x.FundCode).ToList();
            var existing = await dbContext.DailyArchives
                .Where(a => a.Username == username && a.RecordDate == date && codes.Contains(a.FundCode))
                .ToListAsync(stoppingToken);

            var duplicateGroups = existing.GroupBy(x => x.FundCode).Where(g => g.Count() > 1).ToList();
            foreach (var group in duplicateGroups)
            {
                var keep = group.OrderByDescending(x => x.Id).First();
                var remove = group.Where(x => x.Id != keep.Id).ToList();
                if (remove.Count > 0) dbContext.DailyArchives.RemoveRange(remove);
            }

            var existingDict = existing
                .GroupBy(x => x.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            foreach (var item in normalizedIncoming)
            {
                item.Username = username;
                item.RecordDate = date;
                if (existingDict.TryGetValue(item.FundCode, out var old))
                {
                    CopyArchiveValues(old, item);
                    dbContext.DailyArchives.Update(old);
                }
                else
                {
                    dbContext.DailyArchives.Add(item);
                }
            }
        }

        private void ClearTodayCacheForUser(string username, string settleDate)
        {
            _cache.Remove($"Tactical_TodayData_{username}");
            _cache.Remove($"Tactical_TodayData_{username}_{settleDate}");
        }

        private async Task SettleTodayNavAsync(DateTime localTime, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string settleDate = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);
            var affectedUsers = new HashSet<string>();

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
                .Where(r => r.FetchTime >= todayStart && r.FetchTime < tomorrowStart && targetFunds.Contains(r.FundCode))
                .ToListAsync(stoppingToken);

            var latestTodayRecordByCode = todayRecordList
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

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

                    latestTodayRecordByCode.TryGetValue(code, out var targetRecord);

                    if (targetRecord != null)
                    {
                        targetRecord.ActualRate = actualRate;
                        targetRecord.DiffRate = Math.Round(actualRate - targetRecord.EstimatedRate, 2);
                    }

                    // 使用预加载的持仓数据
                    if (holdingsByCode.TryGetValue(code, out var holdingUsers))
                    {
                        foreach (var holding in holdingUsers)
                        {
                            double? exactProfit = null;
                            double? exactAssets = null;

                            double effectiveShares = GetEffectiveShares(holding, settleDate);

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

                            if (ApplyOneDaySettlement(holding, actualRate, settleDate, exactProfit, exactAssets))
                            {
                                affectedUsers.Add(holding.Username);
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

            if (affectedUsers.Count > 0)
            {
                var todayRecords = todayRecordList;

                // 优化4：批量查询所有受影响用户的基金
                var allUserFunds = await dbContext.MyFunds
                    .Where(f => affectedUsers.Contains(f.Username))
                    .ToListAsync(stoppingToken);

                var userFundsDict = allUserFunds.GroupBy(f => f.Username)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var username in affectedUsers)
                {
                    if (userFundsDict.TryGetValue(username, out var userFunds))
                    {
                        var rows = BuildArchiveRowsFromCurrentHoldings(username, todayStart, userFunds, todayRecords);
                        await UpsertDailyArchivesAsync(dbContext, username, todayStart, rows, stoppingToken);
                        ClearTodayCacheForUser(username, settleDate);
                    }
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
