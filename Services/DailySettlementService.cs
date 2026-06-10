using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using 小白养基.Models;

namespace 小白养基.Services
{
    /// <summary>
    /// 定时每日结算服务：在指定时间自动将当天收益写入 DailyArchives。
    /// 不依赖用户打开页面，确保盈亏日历跨天不会清零。
    /// </summary>
    public class DailySettlementService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DailySettlementService> _logger;
        private readonly IMemoryCache _cache;

        // 北京时间执行时刻（时, 分）
        private static readonly (int Hour, int Minute)[] Schedule = new[]
        {
            (21, 30),
            (22, 30),
            (23, 30),
            (0, 10),  // 补结算上一个交易日
            (8, 30),  // 再补结算上一个交易日
        };

        public DailySettlementService(IServiceProvider serviceProvider, ILogger<DailySettlementService> logger, IMemoryCache cache)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _cache = cache;
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("每日结算定时服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = ChinaNow();
                    var delay = GetDelayToNextSchedule(now);
                    _logger.LogInformation("下次结算时间: {Time}（{Delay}分钟后）", now.Add(delay), delay.TotalMinutes.ToString("F0"));
                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested) break;

                    now = ChinaNow();
                    // 00:10 和 08:30 补结算上一个交易日，21:30/22:30/23:30 结算当天
                    bool settlePrevious = now.Hour < 17;

                    if (settlePrevious)
                    {
                        var prevDate = GetPreviousTradeDate(now.Date);
                        _logger.LogInformation("补结算上一个交易日: {Date}", prevDate.ToString("yyyy-MM-dd"));
                        await SettleForDate(prevDate, stoppingToken);
                    }
                    else
                    {
                        _logger.LogInformation("结算当天: {Date}", now.Date.ToString("yyyy-MM-dd"));
                        await SettleForDate(now.Date, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "每日结算定时任务异常");
                    try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private static TimeSpan GetDelayToNextSchedule(DateTime now)
        {
            var candidates = new List<DateTime>();
            foreach (var (h, m) in Schedule)
            {
                var today = now.Date.AddHours(h).AddMinutes(m);
                if (today > now) candidates.Add(today);
                // 也检查明天的早场（0:10, 8:30）
                var tomorrow = today.AddDays(1);
                candidates.Add(tomorrow);
            }
            var next = candidates.Where(t => t > now).OrderBy(t => t).First();
            var delay = next - now;
            if (delay.TotalSeconds < 10) delay = TimeSpan.FromSeconds(10);
            if (delay.TotalHours > 12) delay = TimeSpan.FromHours(12);
            return delay;
        }

        private static DateTime GetPreviousTradeDate(DateTime date)
        {
            var d = date.AddDays(-1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                d = d.AddDays(-1);
            return d;
        }

        private async Task SettleForDate(DateTime targetDate, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            string dateDash = targetDate.ToString("yyyy-MM-dd");
            string cacheKey = $"daily_settle_{dateDash}";
            if (_cache.TryGetValue(cacheKey, out _))
            {
                _logger.LogInformation("今日已结算过 {Date}，跳过", dateDash);
                return;
            }

            _logger.LogInformation("开始结算 {Date}", dateDash);

            var allHoldings = await dbContext.MyFunds
                .AsNoTracking()
                .Where(f => f.HoldShares > 0 || f.PendingBuyAmount > 0)
                .ToListAsync(stoppingToken);

            if (allHoldings.Count == 0)
            {
                _logger.LogInformation("无持仓，跳过结算");
                return;
            }

            // 查询当日 FundRecords：优先按 NavDate（官方净值），再按 FetchTime（盘中估值）
            var todayStart = targetDate;
            var todayEnd = targetDate.AddDays(1);
            var todayRecords = await dbContext.FundRecords
                .AsNoTracking()
                .Where(r => (r.IsOfficial && r.NavDate == dateDash)
                         || (r.FetchTime >= todayStart && r.FetchTime < todayEnd))
                .ToListAsync(stoppingToken);

            var usernames = allHoldings.Select(f => f.Username).Distinct().ToList();
            int totalSaved = 0;
            int totalSkipped = 0;

            foreach (var username in usernames)
            {
                var userFunds = allHoldings.Where(f => f.Username == username).ToList();

                var rows = BuildArchiveRows(username, targetDate, userFunds, todayRecords, dateDash);
                if (rows.Count == 0)
                {
                    _logger.LogInformation("{User} {Date} 无有效数据，跳过", username, dateDash);
                    continue;
                }

                var skipped = await UpsertArchives(dbContext, username, targetDate, rows, stoppingToken);
                totalSaved += rows.Count;
                totalSkipped += skipped;

                _cache.Remove($"Tactical_TodayData_{username}");
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            _cache.Set(cacheKey, true, TimeSpan.FromHours(1));

            _logger.LogInformation("结算完成 {Date}，写入 {Count} 条，跳过 {Skipped} 条（已有非零记录）",
                dateDash, totalSaved, totalSkipped);
        }

        private static double GetActivePendingBuyAmount(MyFundConfig fund, string settleDate)
        {
            double explicitPending = fund.PendingBuyAmount > 0
                && !string.IsNullOrEmpty(fund.PendingTradeStatus)
                && !fund.PendingTradeStatus.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
                && !fund.PendingTradeStatus.Equals("settled", StringComparison.OrdinalIgnoreCase)
                ? fund.PendingBuyAmount : 0;
            double legacyTodayAdd = fund.LastTradeDate == settleDate && fund.LastAddAmount > 0 ? fund.LastAddAmount : 0;
            return Math.Round(Math.Max(explicitPending, legacyTodayAdd), 2);
        }

        private static List<DailyArchive> BuildArchiveRows(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords, string dateDash)
        {
            // 按 FundCode 索引：优先取官方净值记录，再取最新估值
            var officialDict = todayRecords
                .Where(r => r.IsOfficial && r.NavDate == dateDash)
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var estimateDict = todayRecords
                .Where(r => !r.IsOfficial && r.FetchTime >= date && r.FetchTime < date.AddDays(1))
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0, totalRealized = 0, totalDailyProfit = 0, totalDailyBase = 0, totalCurrentAssets = 0;
            int skippedCount = 0;

            foreach (var fund in funds)
            {
                double pendingBuyAmount = GetActivePendingBuyAmount(fund, dateDash);
                bool pendingBuy = pendingBuyAmount > 0;
                if (fund.HoldShares <= 0 && !pendingBuy)
                {
                    double soldProfit = fund.PlatformCumulativeProfit > 0 ? fund.PlatformCumulativeProfit : fund.RealizedProfit;
                    double soldCost = PortfolioSettlementService.GetSoldCost(fund);
                    double soldRate = soldCost > 0 ? soldProfit / soldCost * 100.0 : 0;
                    rows.Add(new DailyArchive
                    {
                        Username = username, FundCode = fund.FundCode, FundName = fund.FundName,
                        RecordDate = date, Assets = 0, DailyProfit = 0, DailyRate = 0,
                        TotalProfit = Math.Round(soldProfit, 2), TotalRate = Math.Round(soldRate, 2)
                    });
                    totalRealized += soldProfit;
                    continue;
                }

                // 判断数据来源
                bool hasOfficial = officialDict.ContainsKey(fund.FundCode);
                bool hasEstimate = estimateDict.ContainsKey(fund.FundCode);
                bool hasLastSettled = fund.LastSettledDate == dateDash;

                if (!hasOfficial && !hasEstimate && !hasLastSettled)
                {
                    // 无任何有效数据源，不写 0，跳过
                    skippedCount++;
                    Console.WriteLine($"[settle-daily] 跳过 {fund.FundCode} {dateDash}：无官方净值、无估值记录、无 LastSettledDate");
                    continue;
                }

                double confirmedHoldAmount = Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 2));
                double cost = Math.Max(0, Math.Round((fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount) - pendingBuyAmount, 2));
                double baseAmount = hasLastSettled
                    ? Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount - fund.LastSettledProfit, 4))
                    : Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 4));

                double dailyRate, dailyProfit;
                double currentAssets;

                if (hasLastSettled)
                {
                    // 优先用 LastSettled（NavSettlementService 已写入的真实结算值）
                    dailyRate = fund.LastSettledRate;
                    dailyProfit = fund.LastSettledProfit;
                    currentAssets = confirmedHoldAmount;
                }
                else if (hasOfficial)
                {
                    // 有官方净值记录但 LastSettled 未更新（罕见），用官方记录
                    var rec = officialDict[fund.FundCode];
                    dailyRate = rec.ActualRate;
                    dailyProfit = Math.Round(baseAmount * (dailyRate / 100.0), 2);
                    currentAssets = Math.Round(confirmedHoldAmount + dailyProfit, 2);
                }
                else
                {
                    // 只有盘中估值
                    var rec = estimateDict[fund.FundCode];
                    dailyRate = Math.Abs(rec.ActualRate) > 0.000001 ? rec.ActualRate : rec.EstimatedRate;
                    dailyProfit = Math.Round(baseAmount * (dailyRate / 100.0), 2);
                    currentAssets = Math.Round(confirmedHoldAmount + dailyProfit, 2);
                }

                double totalProfit = currentAssets - cost + fund.RealizedProfit;
                double totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;

                rows.Add(new DailyArchive
                {
                    Username = username, FundCode = fund.FundCode, FundName = fund.FundName,
                    RecordDate = date, Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2), DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2), TotalRate = Math.Round(totalRate, 2)
                });

                totalCost += cost;
                totalRealized += fund.RealizedProfit;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
            }

            if (rows.Count == 0) return rows;

            rows.Add(new DailyArchive
            {
                Username = username, FundCode = "TOTAL", FundName = "总持仓",
                RecordDate = date, Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0, 2),
                TotalProfit = Math.Round(totalCurrentAssets - totalCost + totalRealized, 2),
                TotalRate = Math.Round(totalCost > 0 ? (totalCurrentAssets - totalCost + totalRealized) / totalCost * 100.0 : 0, 2)
            });

            if (skippedCount > 0)
                Console.WriteLine($"[settle-daily] {username} {dateDash}: 跳过 {skippedCount} 只基金（无有效数据），写入 {rows.Count} 条");

            return rows;
        }

        private static async Task<int> UpsertArchives(AppDbContext dbContext, string username, DateTime date, IEnumerable<DailyArchive> incoming, CancellationToken stoppingToken)
        {
            var normalized = incoming
                .Where(x => !string.IsNullOrWhiteSpace(x.FundCode))
                .GroupBy(x => x.FundCode)
                .Select(g => g.Last())
                .ToList();
            if (normalized.Count == 0) return 0;

            var codes = normalized.Select(x => x.FundCode).ToList();
            var existing = await dbContext.DailyArchives
                .Where(a => a.Username == username && a.RecordDate == date && codes.Contains(a.FundCode))
                .ToListAsync(stoppingToken);

            var existingDict = existing
                .GroupBy(x => x.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            int skipped = 0;
            foreach (var item in normalized)
            {
                item.Username = username;
                item.RecordDate = date;

                // 如果新数据 profit=0 且 rate=0，但旧记录有非零值，不覆盖
                bool incomingIsZero = Math.Abs(item.DailyProfit) < 0.001 && Math.Abs(item.DailyRate) < 0.001;
                if (existingDict.TryGetValue(item.FundCode, out var old))
                {
                    bool oldIsNonZero = Math.Abs(old.DailyProfit) > 0.001 || Math.Abs(old.DailyRate) > 0.001;
                    if (incomingIsZero && oldIsNonZero && item.FundCode != "TOTAL")
                    {
                        // 不用空数据覆盖已有非零记录
                        skipped++;
                        continue;
                    }
                    old.FundName = string.IsNullOrWhiteSpace(item.FundName) ? old.FundName : item.FundName;
                    old.Assets = Math.Round(item.Assets, 2);
                    old.DailyProfit = Math.Round(item.DailyProfit, 2);
                    old.DailyRate = Math.Round(item.DailyRate, 2);
                    old.TotalProfit = Math.Round(item.TotalProfit, 2);
                    old.TotalRate = Math.Round(item.TotalRate, 2);
                    dbContext.DailyArchives.Update(old);
                }
                else
                {
                    if (incomingIsZero && item.FundCode != "TOTAL")
                    {
                        skipped++;
                        continue;
                    }
                    dbContext.DailyArchives.Add(item);
                }
            }
            return skipped;
        }
    }
}
