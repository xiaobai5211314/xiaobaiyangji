using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using 小白养基.Models;

namespace 小白养基.Services
{
    // 新浪基金估值接口返回结构（hq.sinajs.cn/list=fu_{code}）
    // 响应格式: var hq_str_fu_017968="基金名,HH:mm:ss,估算净值,昨日净值,昨日累计,?,估算涨跌幅%,yyyy-MM-dd,?,昨日涨跌幅%";
    // 字段索引: [0]基金名 [1]时间 [2]估算净值 [3]昨日净值 [6]估算涨跌幅% [7]日期
    public record SinaFundQuote(string FundName, double EstimatedRate, DateTime FetchTime);

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

            var targetFundRows = await dbContext.MyFunds
                .AsNoTracking()
                .Where(f => f.HoldAmount > 0 || f.HoldShares > 0 || f.PendingBuyAmount > 0 || f.PendingSellAmount > 0)
                .Select(f => new { f.FundCode, f.FundName })
                .ToListAsync(stoppingToken);

            var targetFunds = targetFundRows
                .Where(f => !string.IsNullOrWhiteSpace(f.FundCode))
                .GroupBy(f => f.FundCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

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

        /// <summary>
        /// 通过新浪 hq.sinajs.cn/list=fu_{code} 抓取基金实时估值。
        /// 替代已下线的天天基金 fundgz.1234567.com.cn 接口。
        /// 返回 null 表示抓取失败或基金不在新浪数据库。
        /// </summary>
        public static async Task<SinaFundQuote?> TryFetchSinaQuoteAsync(HttpClient client, string fundCode, CancellationToken ct)
        {
            try
            {
                string url = $"https://hq.sinajs.cn/list=fu_{fundCode}";
                using var resp = await client.GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                // 新浪返回 GB18030 编码，参考 StockQuoteService.GetStringGbAsync
                string response;
                try { response = System.Text.Encoding.GetEncoding("GB18030").GetString(bytes); }
                catch { response = System.Text.Encoding.UTF8.GetString(bytes); }

                var match = Regex.Match(response, $@"var hq_str_fu_{Regex.Escape(fundCode)}=""([^""]+)""");
                if (!match.Success) return null;

                var fields = match.Groups[1].Value.Split(',');
                if (fields.Length < 8) return null;

                string fundName = fields[0];
                string timeStr = fields[1];   // HH:mm:ss
                string dateStr = fields[7];   // yyyy-MM-dd

                if (!double.TryParse(fields[6], out double rate))
                {
                    rate = 0;
                }

                if (!DateTime.TryParse($"{dateStr} {timeStr}", out DateTime parsedTime))
                {
                    parsedTime = DateTime.Now;
                }

                return new SinaFundQuote(fundName, rate, parsedTime);
            }
            catch
            {
                return null;
            }
        }

        private async Task<FundData?> FetchOneAsync(string fundCode, string fundName, CancellationToken stoppingToken)
        {
            var quote = await TryFetchSinaQuoteAsync(_httpClient, fundCode, stoppingToken);
            if (quote == null)
            {
                _logger.LogWarning("抓取 {Code} 失败（新浪 fu_ 接口无数据）", fundCode);
                return null;
            }

            return new FundData
            {
                FundCode = fundCode,
                FundName = fundName,
                EstimatedRate = quote.EstimatedRate,
                FetchTime = quote.FetchTime
            };
        }
    }
}
