using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using 估值助手.Models;

namespace 估值助手.Services
{
    // 夜间 T+1 精准清算引擎
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
            // ⚠️ 核心伪装：必须带上这个 Referer，否则东方财富的真实净值接口会拒绝访问
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🌙 夜间清算引擎已挂载，等待入夜...");
            while (!stoppingToken.IsCancellationRequested)
            {
                // 每半小时醒来检查一次
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);

                // 只有在晚上 21:00 到 23:59 之间才执行清算
                if (DateTime.Now.Hour >= 21)
                {
                    await SettleTodayNavAsync();
                }
            }
        }

        private async Task SettleTodayNavAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var targetFunds = await dbContext.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");

            foreach (var code in targetFunds)
            {
                try
                {
                    // 调用东方财富真实历史净值 API
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await _httpClient.GetStringAsync(url);

                    using var doc = JsonDocument.Parse(response);
                    var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                    if (dataArray.GetArrayLength() > 0)
                    {
                        var latestData = dataArray[0];
                        string fsrq = latestData.GetProperty("FSRQ").GetString() ?? ""; // 净值日期
                        string jzzzlStr = latestData.GetProperty("JZZZL").GetString() ?? ""; // 真实涨跌幅

                        // 如果基金公司已经公布了今天的真实净值
                        if (fsrq == todayStr && double.TryParse(jzzzlStr, out double actualRate))
                        {
                            // 找到今天下午 15:00:00 的那条记录
                            var targetRecord = await dbContext.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime.Date == DateTime.Today)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();

                            // 如果找到了，并且它的值还不是真实的，就强行覆写！
                            if (targetRecord != null && Math.Abs(targetRecord.EstimatedRate - actualRate) > 0.001)
                            {
                                targetRecord.EstimatedRate = actualRate;
                                _logger.LogInformation("✅ [夜间清算成功] {Code} 估值修正为真实净值: {Rate}%", code, actualRate);
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