using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore; // 👉 新增：用于支持 AnyAsync 异步查询
using 估值助手.Models;

namespace 估值助手.Services
{
    public class FundScraperService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FundScraperService> _logger;
        private readonly HttpClient _httpClient;

        public FundScraperService(IServiceProvider serviceProvider, ILogger<FundScraperService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("基金雷达后台服务已启动...");
            while (!stoppingToken.IsCancellationRequested)
            {
                await FetchAndSaveDataAsync();
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        private async Task FetchAndSaveDataAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 👉 极其聪明的抓取：提取所有用户配置的基金，去重后抓取，节约服务器性能
            var targetFunds = dbContext.MyFunds
                .Select(f => new { f.FundCode, f.FundName })
                .Distinct()
                .ToList();

            if (targetFunds.Count == 0) return; // 没人加基金就先待机

            foreach (var fund in targetFunds)
            {
                try
                {
                    string url = $"http://fundgz.1234567.com.cn/js/{fund.FundCode}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    string response = await _httpClient.GetStringAsync(url);
                    var match = Regex.Match(response, @"jsonpgz\((.*?)\);");
                    if (match.Success)
                    {
                        var root = System.Text.Json.JsonDocument.Parse(match.Groups[1].Value).RootElement;
                        double rate = double.Parse(root.GetProperty("gszzl").GetString() ?? "0");
                        string timeStr = root.GetProperty("gztime").GetString() ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                        DateTime parsedTime = DateTime.Parse(timeStr);

                        // 【核心修复】：防重校验， 防止收盘后无限插入相同时间的数据
                        bool exists = await dbContext.FundRecords
                            .AnyAsync(r => r.FundCode == fund.FundCode && r.FetchTime == parsedTime);

                        if (!exists)
                        {
                            dbContext.FundRecords.Add(new FundData
                            {
                                FundCode = fund.FundCode,
                                FundName = fund.FundName,
                                EstimatedRate = rate,
                                FetchTime = parsedTime
                            });
                            _logger.LogInformation("[入库成功] {Name} : {Rate}%", fund.FundName, rate);
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError("抓取 {Code} 失败: {Message}", fund.FundCode, ex.Message); }
            }
            await dbContext.SaveChangesAsync();
        }
    }
}