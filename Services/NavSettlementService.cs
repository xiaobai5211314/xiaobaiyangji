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
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🌙 夜间清算引擎已挂载...");
            while (!stoppingToken.IsCancellationRequested)
            {
                var localTime = DateTime.UtcNow.AddHours(8);
                if (localTime.Hour >= 20 || localTime.Hour < 2)
                {
                    await SettleTodayNavAsync(localTime);
                }
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

                private async Task SettleTodayNavAsync(DateTime localTime)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 💥 黑客级自动补丁 2.0：除了雷达字段，再强行开辟一个财务防刷锁字段！
            try {
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN ActualRate DOUBLE NOT NULL DEFAULT 0;");
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN DiffRate DOUBLE NOT NULL DEFAULT 0;");
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE MyFunds ADD COLUMN LastSettledDate VARCHAR(20);");
            } catch { /* 忽略列已存在的报错 */ }

            var targetFunds = await dbContext.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();

            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            foreach (var code in targetFunds)
            {
                try
                {
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1&_={timestamp}";

                    _httpClient.DefaultRequestHeaders.Remove("Accept");
                    _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");

                    string response = await _httpClient.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(response);
                    var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                    if (dataArray.GetArrayLength() > 0)
                    {
                        var latestData = dataArray[0];
                        string fsrq = latestData.GetProperty("FSRQ").GetString() ?? "";
                        string jzzzlStr = latestData.GetProperty("JZZZL").GetString() ?? "";

                        // 如果拿到了今天的真实涨跌幅（比如 1.31）
                        if (fsrq == todayStr && double.TryParse(jzzzlStr, out double actualRate))
                        {
                            // 【1. 渣男雷达探针逻辑】
                            var targetRecord = await dbContext.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();

                            if (targetRecord != null && Math.Abs(targetRecord.EstimatedRate - actualRate) > 0.001)
                            {
                                targetRecord.ActualRate = actualRate;
                                targetRecord.DiffRate = Math.Round(actualRate - targetRecord.EstimatedRate, 2);
                                _logger.LogInformation("✅ [雷达探针生效] {Code} 真实净值: {Rate}%, 捕捉到调仓误差: {Diff}%", code, actualRate, targetRecord.DiffRate);
                            }

                            // 【2. 💰 战术二：自动复利滚存引擎 (带防重刷锁)】
                            var holdingUsers = await dbContext.MyFunds.Where(f => f.FundCode == code).ToListAsync();
                            foreach (var holding in holdingUsers)
                            {
                                // 检查今天是不是已经结算过了，死死防住“无限刷钱BUG”！
                                if (holding.LastSettledDate != todayStr)
                                {
                                    double oldAmount = holding.HoldAmount;
                                    // 核心金融算法：老本金 * (1 + 涨幅/100)
                                    holding.HoldAmount = Math.Round(oldAmount * (1.0 + actualRate / 100.0), 2);
                                    holding.LastSettledDate = todayStr; // 立刻上锁！
                                    
                                    _logger.LogInformation("💰 [自动复利生效] {Name} 本金翻滚: {Old} -> {New}", holding.FundName, oldAmount, holding.HoldAmount);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("❌ 清算 {Code} 失败: {Message}", code, ex.Message);
                }
            }
            await dbContext.SaveChangesAsync();

            // ================= 午夜数据清道夫 =================
            try
            {
                var deadline = DateTime.UtcNow.AddHours(8).Date.AddDays(-7);
                var oldRecords = await dbContext.FundRecords
                    .Where(r => r.FetchTime < deadline)
                    .ToListAsync();

                if (oldRecords.Any())
                {
                    dbContext.FundRecords.RemoveRange(oldRecords);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("🧹 [清道夫执行完毕] 成功清理了 {Count} 条过期废弃数据！", oldRecords.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("❌ 清道夫运行失败: {Message}", ex.Message);
            }
        }

    }
}
