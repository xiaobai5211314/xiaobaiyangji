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
            var archiveService = scope.ServiceProvider.GetRequiredService<DailyArchiveService>();

            string dateDash = targetDate.ToString("yyyy-MM-dd");
            _logger.LogInformation("开始结算 {Date}", dateDash);

            var allHoldings = await dbContext.MyFunds
                .AsNoTracking()
                .Where(f => f.HoldShares > 0)
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

                var changed = await archiveService.UpsertAsync(username, targetDate, rows, stoppingToken);
                totalSaved += changed;

                _cache.Remove($"Tactical_TodayData_{username}");
            }

            await dbContext.SaveChangesAsync(stoppingToken);

            _logger.LogInformation("结算完成 {Date}，写入/更新 {Count} 条，跳过 {Skipped} 条",
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
            // 日档案只接受可追溯的结算来源；盘中估值不能写成最终盈亏档案。
            var officialDict = todayRecords
                .Where(r => r.IsOfficial && r.NavDate == dateDash)
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0, totalDailyProfit = 0, totalDailyBase = 0, totalCurrentAssets = 0, totalHoldingProfit = 0;
            int skippedCount = 0;
            int expectedActiveCount = 0;

            foreach (var fund in funds)
            {
                double pendingBuyAmount = GetActivePendingBuyAmount(fund, dateDash);
                double confirmedHoldAmount = Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 2));
                if (fund.HoldShares <= 0 || confirmedHoldAmount <= 0.01) continue;
                expectedActiveCount++;

                // 判断数据来源
                bool hasOfficial = officialDict.ContainsKey(fund.FundCode);
                bool hasLastSettled = fund.LastSettledDate == dateDash;
                bool hasOcrSnapshot = fund.OcrYesterdayDate == dateDash;

                if (!hasOfficial && !hasLastSettled && !hasOcrSnapshot)
                {
                    skippedCount++;
                    Console.WriteLine($"[settle-daily] 跳过 {fund.FundCode} {dateDash}：无官方净值、OCR快照或 LastSettledDate");
                    continue;
                }

                double cost = Math.Max(0, Math.Round((fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount) - pendingBuyAmount, 2));
                double baseAmount = hasLastSettled
                    ? Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount - fund.LastSettledProfit, 4))
                    : Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 4));

                double dailyRate, dailyProfit;
                double currentAssets;
                double totalProfit;
                double totalRate;
                string source;

                if (hasOcrSnapshot)
                {
                    dailyProfit = Math.Round(fund.OcrYesterdayIncome, 2);
                    currentAssets = confirmedHoldAmount;
                    baseAmount = Math.Max(0, Math.Round(currentAssets - dailyProfit, 4));
                    dailyRate = baseAmount > 0 ? dailyProfit / baseAmount * 100.0 : 0;
                    totalProfit = Math.Round(fund.OcrHoldingIncome, 2);
                    totalRate = Math.Round(fund.OcrHoldingRate, 2);
                    source = "alipay-snapshot";
                }
                else if (hasLastSettled)
                {
                    // 优先用 LastSettled（NavSettlementService 已写入的真实结算值）
                    dailyRate = fund.LastSettledRate;
                    dailyProfit = fund.LastSettledProfit;
                    currentAssets = confirmedHoldAmount;
                    totalProfit = Math.Round(currentAssets - cost + fund.RealizedProfit, 2);
                    totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;
                    source = "official-nav";
                }
                else
                {
                    var rec = officialDict[fund.FundCode];
                    dailyRate = rec.ActualRate;
                    dailyProfit = Math.Round(baseAmount * (dailyRate / 100.0), 2);
                    currentAssets = rec.Nav is > 0 && fund.HoldShares > 0
                        ? Math.Round(fund.HoldShares * rec.Nav.Value, 2)
                        : Math.Round(baseAmount + dailyProfit, 2);
                    totalProfit = Math.Round(currentAssets - cost + fund.RealizedProfit, 2);
                    totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;
                    source = "official-nav";
                }

                rows.Add(new DailyArchive
                {
                    Username = username, FundCode = fund.FundCode, FundName = fund.FundName,
                    RecordDate = date, Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2), DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2), TotalRate = Math.Round(totalRate, 2),
                    Source = source, IsFinal = true, UpdatedAt = DateTime.UtcNow
                });

                totalCost += cost;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
                totalHoldingProfit += totalProfit;
            }

            if (rows.Count == 0) return rows;

            rows.Add(new DailyArchive
            {
                Username = username, FundCode = "TOTAL", FundName = "总持仓",
                RecordDate = date, Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0, 2),
                TotalProfit = Math.Round(totalHoldingProfit, 2),
                TotalRate = Math.Round(totalCost > 0 ? totalHoldingProfit / totalCost * 100.0 : 0, 2),
                Source = rows.Count == expectedActiveCount ? "mixed-final" : "partial-final",
                IsFinal = rows.Count == expectedActiveCount,
                UpdatedAt = DateTime.UtcNow
            });

            if (skippedCount > 0)
                Console.WriteLine($"[settle-daily] {username} {dateDash}: 跳过 {skippedCount} 只基金（无有效数据），写入 {rows.Count} 条");

            return rows;
        }

    }
}
