using System.Globalization;
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
        private readonly IHttpClientFactory _httpClientFactory;

        public FundScraperService(IServiceProvider serviceProvider, ILogger<FundScraperService> logger, IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("基金估值抓取服务已启动");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await FetchAndSaveDataAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "基金估值抓取批处理异常");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
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
            var errors = new List<string>();
            var quote =
                await TryFetchQuoteAsync("fundgz_1234567", fundCode, fundName, () => FetchFundGzAsync(fundCode, fundName, stoppingToken), errors)
                ?? await TryFetchQuoteAsync("eastmoney_estimate", fundCode, fundName, () => FetchEastmoneyEstimateAsync(fundCode, fundName, stoppingToken), errors)
                ?? await TryFetchQuoteAsync("sina_fund_estimate", fundCode, fundName, () => FetchSinaEstimateAsync(fundCode, fundName, stoppingToken), errors);

            if (quote == null)
            {
                _logger.LogWarning("基金估值缺失 fundCode={FundCode} errors={Errors}", fundCode, string.Join("; ", errors));
                return null;
            }

            var today = ChinaNow().Date;
            if (quote.EstimateTime.Date != today)
            {
                _logger.LogWarning(
                    "基金估值不是今日数据，跳过写入 fundCode={FundCode} source={Source} estimateTime={EstimateTime}",
                    fundCode,
                    quote.EstimateSource,
                    quote.EstimateTime);
                return null;
            }

            _logger.LogInformation(
                "fund estimate fundCode={FundCode} source={Source} estimateRate={Rate} estimateTime={EstimateTime} quoteOk={QuoteOk} isFallback={IsFallback} isStale={IsStale} error={Error}",
                fundCode,
                quote.EstimateSource,
                quote.EstimatedRate,
                quote.EstimateTime,
                quote.QuoteOk,
                quote.IsFallback,
                quote.IsStale,
                quote.ErrorMessage);

            return new FundData
            {
                FundCode = fundCode,
                FundName = string.IsNullOrWhiteSpace(quote.FundName) ? fundName : quote.FundName,
                EstimatedRate = quote.EstimatedRate,
                FetchTime = quote.EstimateTime,
                EstimateSource = quote.EstimateSource,
                QuoteOk = quote.QuoteOk,
                IsFallback = quote.IsFallback,
                IsStale = quote.IsStale,
                EstimateMessage = quote.ErrorMessage ?? string.Empty,
                RawTime = quote.RawTime ?? string.Empty
            };
        }

        private async Task<FundEstimateQuote?> TryFetchQuoteAsync(
            string source,
            string fundCode,
            string fundName,
            Func<Task<FundEstimateQuote?>> fetch,
            List<string> errors)
        {
            try
            {
                var quote = await fetch();
                if (quote == null)
                {
                    errors.Add($"{source} failed: empty");
                    return null;
                }

                if (!quote.QuoteOk)
                {
                    errors.Add($"{source} failed: {quote.ErrorMessage ?? "invalid quote"}");
                    return null;
                }

                return quote;
            }
            catch (Exception ex)
            {
                errors.Add($"{source} failed: {ex.Message}");
                _logger.LogWarning(ex, "{Source} 基金估值失败：{Code} {Name}", source, fundCode, fundName);
                return null;
            }
        }

        private async Task<FundEstimateQuote?> FetchFundGzAsync(string fundCode, string fundName, CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient("FundGz");
            string url = $"http://fundgz.1234567.com.cn/js/{fundCode}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            string response = await client.GetStringAsync(url, stoppingToken);
            var match = Regex.Match(response, @"jsonpgz\((.*?)\);");
            if (!match.Success) return null;

            using var json = JsonDocument.Parse(match.Groups[1].Value);
            var root = json.RootElement;
            var rateText = GetJsonString(root, "gszzl");
            var timeText = GetJsonString(root, "gztime");
            if (!TryParseDouble(rateText, out var rate))
            {
                return BuildInvalidQuote(fundCode, fundName, "fundgz_1234567", timeText, "gszzl 缺失");
            }

            if (!DateTime.TryParse(timeText, out var parsedTime))
            {
                parsedTime = ChinaNow();
            }

            return BuildQuote(
                fundCode,
                GetJsonString(root, "name", fundName),
                rate,
                parsedTime,
                "fundgz_1234567",
                false,
                timeText);
        }

        private async Task<FundEstimateQuote?> FetchEastmoneyEstimateAsync(string fundCode, string fundName, CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient("EastMoney");
            var url = $"https://fundmobapi.eastmoney.com/FundMApi/FundValuationDetail.ashx?FCODE={Uri.EscapeDataString(fundCode)}&deviceid=Wap&plat=Wap&product=EFund&version=2.0.0";
            var response = await client.GetStringAsync(url, stoppingToken);
            using var doc = JsonDocument.Parse(response);

            if (!doc.RootElement.TryGetProperty("Datas", out var datas) || datas.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            JsonElement item = datas.ValueKind == JsonValueKind.Array && datas.GetArrayLength() > 0
                ? datas[0]
                : datas;

            var rateText = FirstJsonString(item, "GSZZL", "gszzl", "EstimatedRate", "GZ");
            var timeText = FirstJsonString(item, "GZTIME", "gztime", "EstimateTime", "time");
            if (!TryParseDouble(rateText, out var rate))
            {
                return null;
            }

            if (!DateTime.TryParse(timeText, out var parsedTime))
            {
                parsedTime = ChinaNow();
            }

            return BuildQuote(fundCode, fundName, rate, parsedTime, "eastmoney_estimate", true, timeText);
        }

        private async Task<FundEstimateQuote?> FetchSinaEstimateAsync(string fundCode, string fundName, CancellationToken stoppingToken)
        {
            var client = _httpClientFactory.CreateClient("SinaQuote");
            var response = await client.GetStringAsync($"https://hq.sinajs.cn/list=f_{fundCode}", stoppingToken);
            if (string.IsNullOrWhiteSpace(response) || !response.Contains("hq_str_f_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 新浪 f_ 接口当前更像净值快照，不能确认为盘中估值；保留探测日志，不写入 FundRecords。
            _logger.LogDebug("sina_fund_estimate probe fundCode={FundCode} payload={Payload}", fundCode, response);
            return null;
        }

        private static FundEstimateQuote BuildQuote(
            string fundCode,
            string fundName,
            double estimatedRate,
            DateTime estimateTime,
            string source,
            bool isFallback,
            string? rawTime)
        {
            var now = ChinaNow();
            var isToday = estimateTime.Date == now.Date;
            var isStale = !isToday || (now - estimateTime).TotalMinutes > 15;
            return new FundEstimateQuote(
                FundCode: fundCode,
                FundName: fundName,
                EstimatedRate: Math.Round(estimatedRate, 4),
                EstimateTime: estimateTime,
                EstimateSource: source,
                QuoteOk: isToday,
                IsFallback: isFallback || isStale,
                IsStale: isStale,
                ErrorMessage: isToday ? string.Empty : "估值时间不是今日",
                RawTime: rawTime);
        }

        private static FundEstimateQuote BuildInvalidQuote(string fundCode, string fundName, string source, string? rawTime, string error)
            => new(
                FundCode: fundCode,
                FundName: fundName,
                EstimatedRate: 0,
                EstimateTime: ChinaNow(),
                EstimateSource: source,
                QuoteOk: false,
                IsFallback: false,
                IsStale: false,
                ErrorMessage: error,
                RawTime: rawTime);

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        private static bool TryParseDouble(string? text, out double value)
            => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, out value);

        private static string GetJsonString(JsonElement element, string name, string fallback = "")
            => element.TryGetProperty(name, out var prop) ? prop.ToString() : fallback;

        private static string FirstJsonString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var prop))
                {
                    return prop.ToString();
                }
            }

            return string.Empty;
        }

        private sealed record FundEstimateQuote(
            string FundCode,
            string FundName,
            double EstimatedRate,
            DateTime EstimateTime,
            string EstimateSource,
            bool QuoteOk,
            bool IsFallback,
            bool IsStale,
            string? ErrorMessage,
            string? RawTime);
    }
}
