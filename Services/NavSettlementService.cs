using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using 估值助手.Models;

namespace 估值助手.Services
{
    public class NavSettlementService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NavSettlementService> _logger;
        private readonly HttpClient _httpClient;

        public NavSettlementService(IServiceProvider serviceProvider, ILogger<NavSettlementService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("夜间净值清算服务已启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var localTime = ChinaNow();
                    if (localTime.Hour >= 20 || localTime.Hour < 2)
                    {
                        await SettleTodayNavAsync(localTime, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "夜间清算主循环异常");
                }

                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
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

        private static bool ApplyOneDaySettlement(MyFundConfig fund, double actualRate, string settleDate)
        {
            if (fund.LastSettledDate == settleDate) return false;

            double baseAmount = GetEffectiveBaseAmount(fund, settleDate);
            double pending = GetPendingTradeAmount(fund, settleDate);
            double settledProfit = Math.Round(baseAmount * (actualRate / 100.0), 2);

            fund.HoldAmount = Math.Round(baseAmount + settledProfit + pending, 2);
            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);
            return true;
        }

        private async Task EnsureRuntimeColumnsAsync(AppDbContext dbContext)
        {
            var sqlList = new[]
            {
                "ALTER TABLE FundRecords ADD COLUMN ActualRate DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE FundRecords ADD COLUMN DiffRate DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE MyFunds ADD COLUMN LastSettledDate VARCHAR(20);",
                "ALTER TABLE MyFunds ADD COLUMN LastSettledProfit DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE MyFunds ADD COLUMN LastSettledRate DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE MyFunds ADD COLUMN LastTradeDate VARCHAR(20);",
                "ALTER TABLE MyFunds ADD COLUMN LastAddAmount DOUBLE NOT NULL DEFAULT 0;",
                "ALTER TABLE MyFunds ADD COLUMN RealizedProfit DOUBLE NOT NULL DEFAULT 0;",
                "CREATE INDEX IX_FundRecord_Code_Time ON FundRecords (FundCode, FetchTime);",
                "CREATE INDEX IX_DailyArchive_User_Date_Code ON DailyArchives (Username, RecordDate, FundCode);"
            };

            foreach (var sql in sqlList)
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync(sql);
                }
                catch
                {
                    // 列/索引已存在时忽略。正式生产建议改为 EF Core migration。
                }
            }
        }

        private async Task SettleTodayNavAsync(DateTime localTime, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            await EnsureRuntimeColumnsAsync(dbContext);

            string settleDate = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            var targetFunds = await dbContext.MyFunds
                .Select(f => f.FundCode)
                .Distinct()
                .ToListAsync(stoppingToken);

            foreach (var code in targetFunds)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1&_={timestamp}";
                    string response = await _httpClient.GetStringAsync(url, stoppingToken);

                    using var doc = JsonDocument.Parse(response);
                    if (!doc.RootElement.TryGetProperty("Data", out var data) ||
                        !data.TryGetProperty("LSJZList", out var dataArray) ||
                        dataArray.GetArrayLength() == 0)
                    {
                        continue;
                    }

                    var latestData = dataArray[0];
                    string fsrq = latestData.GetProperty("FSRQ").GetString() ?? "";
                    string jzzzlStr = latestData.GetProperty("JZZZL").GetString() ?? "";

                    if (fsrq != settleDate || !double.TryParse(jzzzlStr, out double actualRate))
                    {
                        continue;
                    }

                    var targetRecord = await dbContext.FundRecords
                        .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefaultAsync(stoppingToken);

                    if (targetRecord != null)
                    {
                        targetRecord.ActualRate = actualRate;
                        targetRecord.DiffRate = Math.Round(actualRate - targetRecord.EstimatedRate, 2);
                    }

                    var holdingUsers = await dbContext.MyFunds
                        .Where(f => f.FundCode == code)
                        .ToListAsync(stoppingToken);

                    foreach (var holding in holdingUsers)
                    {
                        if (ApplyOneDaySettlement(holding, actualRate, settleDate))
                        {
                            _logger.LogInformation("清算完成 {FundName}({Code}) {Rate}% -> {Amount}", holding.FundName, code, actualRate, holding.HoldAmount);
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
