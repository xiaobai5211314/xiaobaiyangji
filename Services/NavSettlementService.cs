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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🌙 夜间清算引擎已挂载...");
            while (!stoppingToken.IsCancellationRequested)
            {
                // 【核心修复 1】：无视服务器时区，强行获取北京/新加坡时间 (UTC+8)
                var localTime = DateTime.UtcNow.AddHours(8);

                // 只要过了晚上 20 点，立刻查账！
                if (localTime.Hour >= 20 || localTime.Hour < 2)
                {
                    await SettleTodayNavAsync(localTime);
                }

                // 查完睡 10 分钟就行，不用等半小时
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
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
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
                            // 【核心修复 2】：不用 .Date，直接用时间区间查询，避开 MySQL 时区转换坑
                            var targetRecord = await dbContext.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();

                            if (targetRecord != null && Math.Abs(targetRecord.EstimatedRate - actualRate) > 0.001)
                            {
                                targetRecord.EstimatedRate = actualRate;
                                _logger.LogInformation("✅ [夜间清算成功] {Code} 修正为真实净值: {Rate}%", code, actualRate);
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
        }
    }
}