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
                .Where(f => f.HoldAmount > 0)
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
            // 正式日档案只接受蚂蚁持仓页确认快照。官方净值和盘中估值只做行情元数据，不反推正式金额。
            var rows = new List<DailyArchive>();
            var confirmedMoney = new List<ConfirmedHoldingMoney>();
            int skippedCount = 0;
            int expectedActiveCount = 0;

            foreach (var fund in funds)
            {
                decimal pendingBuyAmount = PortfolioAccounting.Money(GetActivePendingBuyAmount(fund, dateDash));
                decimal confirmedHoldAmount = Math.Max(0m, PortfolioAccounting.Money(fund.HoldAmount) - pendingBuyAmount);
                if (confirmedHoldAmount <= 0.01m) continue;
                expectedActiveCount++;

                bool hasOcrSnapshot = fund.OcrYesterdayDate == dateDash;
                if (!hasOcrSnapshot)
                {
                    skippedCount++;
                    Console.WriteLine($"[settle-daily] 待确认 {fund.FundCode} {dateDash}：缺少蚂蚁昨日收益快照，不用净值或估值反推");
                    continue;
                }

                decimal dailyProfit = PortfolioAccounting.Money(fund.OcrYesterdayIncome);
                decimal currentAssets = confirmedHoldAmount;
                decimal baseAmount = Math.Max(0m, currentAssets - dailyProfit);
                decimal dailyRate = PortfolioAccounting.Percent(dailyProfit, baseAmount);
                decimal totalProfit = PortfolioAccounting.Money(fund.OcrHoldingIncome);
                decimal totalRate = decimal.Round(Convert.ToDecimal(fund.OcrHoldingRate), 2, MidpointRounding.AwayFromZero);

                rows.Add(new DailyArchive
                {
                    Username = username, FundCode = fund.FundCode, FundName = fund.FundName,
                    RecordDate = date, Assets = PortfolioAccounting.ToDouble(currentAssets),
                    DailyProfit = PortfolioAccounting.ToDouble(dailyProfit), DailyRate = Convert.ToDouble(dailyRate),
                    TotalProfit = PortfolioAccounting.ToDouble(totalProfit), TotalRate = Convert.ToDouble(totalRate),
                    Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow
                });
                confirmedMoney.Add(new ConfirmedHoldingMoney(currentAssets, dailyProfit, totalProfit));
            }

            if (rows.Count == 0) return rows;

            var summary = PortfolioAccounting.Calculate(confirmedMoney, 0m);
            decimal totalDailyBase = confirmedMoney.Sum(x => x.ConfirmedAmount - x.YesterdayProfit);
            decimal totalCost = summary.AntConfirmedAmount - summary.AntHoldingProfit;

            rows.Add(new DailyArchive
            {
                Username = username, FundCode = "TOTAL", FundName = "总持仓",
                RecordDate = date, Assets = PortfolioAccounting.ToDouble(summary.AntConfirmedAmount),
                DailyProfit = PortfolioAccounting.ToDouble(summary.ConfirmedYesterdayProfit),
                DailyRate = Convert.ToDouble(PortfolioAccounting.Percent(summary.ConfirmedYesterdayProfit, totalDailyBase)),
                TotalProfit = PortfolioAccounting.ToDouble(summary.AntHoldingProfit),
                TotalRate = Convert.ToDouble(PortfolioAccounting.Percent(summary.AntHoldingProfit, totalCost)),
                Source = rows.Count == expectedActiveCount ? "alipay-confirmed-total" : "alipay-confirmed-partial",
                IsFinal = rows.Count == expectedActiveCount,
                UpdatedAt = DateTime.UtcNow
            });

            if (skippedCount > 0)
                Console.WriteLine($"[settle-daily] {username} {dateDash}: 跳过 {skippedCount} 只基金（无有效数据），写入 {rows.Count} 条");

            return rows;
        }

    }
}
