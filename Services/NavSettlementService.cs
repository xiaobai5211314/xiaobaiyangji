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
                // 无视服务器时区，强行获取北京/新加坡时间 (UTC+8)
                var localTime = DateTime.UtcNow.AddHours(8);

                // 只要过了晚上 20 点，立刻查账！
                if (localTime.Hour >= 20 || localTime.Hour < 2)
                {
                    await SettleTodayNavAsync(localTime);
                }

                // 查完睡 10 分钟
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task SettleTodayNavAsync(DateTime localTime)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var targetFunds = await dbContext.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();

            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            foreach (var code in targetFunds)
            {
                try
                {
                    // 💥 核心修复：加装时间戳破甲弹，强制打穿天天基金的 CDN 缓存！
                    long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1&_={timestamp}";

                    // 伪装防爬虫
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

                        if (fsrq == todayStr && double.TryParse(jzzzlStr, out double actualRate))
                        {
                            var targetRecord = await dbContext.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();

                            // 只要相差 0.001 就说明真实净值和估值不同，果断覆盖！
                            if (targetRecord != null && Math.Abs(targetRecord.EstimatedRate - actualRate) > 0.001)
                            {
                                targetRecord.EstimatedRate = actualRate;
                                _logger.LogInformation("✅ [夜间清算成功] 破甲击穿！{Code} 修正为真实净值: {Rate}%", code, actualRate);
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

            // ================= 加装：午夜数据清道夫 =================
            try
            {
                // 只保留最近 7 天的记录
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