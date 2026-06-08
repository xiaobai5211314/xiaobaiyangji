using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using 小白养基.Models;

namespace 小白养基.Services
{
    public class FundScraperService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FundScraperService> _logger;
        private readonly HttpClient _httpClient;

        public FundScraperService(IServiceProvider serviceProvider, ILogger<FundScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("FundGz");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("基金估值抓取服务已启动");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FetchAndSaveDataAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "基金估值抓取批处理异常");
                    try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        private async Task FetchAndSaveDataAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var targetFunds = await dbContext.MyFunds
                .AsNoTracking()
                .Select(f => new { f.FundCode, f.FundName })
                .Distinct()
                .ToListAsync(stoppingToken);

            if (targetFunds.Count == 0) return;

            var semaphore = new SemaphoreSlim(8);
            var tasks = targetFunds.Select(async fund =>
            {
                await semaphore.WaitAsync(stoppingToken);
                try
                {
                    return await FetchOneAsync(fund.FundCode, fund.FundName, stoppingToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var fetched = (await Task.WhenAll(tasks))
                .Where(x => x != null)
                .Cast<FundData>()
                .ToList();

            if (fetched.Count == 0) return;

            var codes = fetched.Select(x => x.FundCode).Distinct().ToList();
            var times = fetched.Select(x => x.FetchTime).Distinct().ToList();

            var existingKeys = await dbContext.FundRecords
                .AsNoTracking()
                .Where(r => codes.Contains(r.FundCode) && times.Contains(r.FetchTime))
                .Select(r => new { r.FundCode, r.FetchTime })
                .ToListAsync(stoppingToken);

            var existingSet = existingKeys
                .Select(x => $"{x.FundCode}|{x.FetchTime:yyyy-MM-dd HH:mm:ss}")
                .ToHashSet();

            var newRows = fetched
                .Where(x => !existingSet.Contains($"{x.FundCode}|{x.FetchTime:yyyy-MM-dd HH:mm:ss}"))
                .ToList();

            if (newRows.Count == 0) return;

            dbContext.FundRecords.AddRange(newRows);
            await dbContext.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("本轮写入 {Count} 条估值记录", newRows.Count);
        }

        private async Task<FundData?> FetchOneAsync(string fundCode, string fundName, CancellationToken stoppingToken)
        {
            try
            {
                string url = $"http://fundgz.1234567.com.cn/js/{fundCode}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                string response = await _httpClient.GetStringAsync(url, stoppingToken);
                var match = Regex.Match(response, @"jsonpgz\((.*?)\);");
                if (!match.Success) return null;

                using var json = JsonDocument.Parse(match.Groups[1].Value);
                var root = json.RootElement;

                if (!double.TryParse(root.GetProperty("gszzl").GetString() ?? "0", out double rate))
                {
                    rate = 0;
                }

                string timeStr = root.GetProperty("gztime").GetString() ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                if (!DateTime.TryParse(timeStr, out DateTime parsedTime))
                {
                    parsedTime = DateTime.Now;
                }

                return new FundData
                {
                    FundCode = fundCode,
                    FundName = fundName,
                    EstimatedRate = rate,
                    FetchTime = parsedTime
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "抓取 {Code} 失败", fundCode);
                return null;
            }
        }
    }
}
