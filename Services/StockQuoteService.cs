using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using 估值助手.Models;

namespace 估值助手.Services
{
    public record StockQuoteDto(
        string Code,
        string Market,
        string Name,
        decimal Price,
        decimal ChangeAmount,
        decimal ChangeRate,
        decimal Open,
        decimal High,
        decimal Low,
        decimal PreviousClose,
        decimal Volume,
        decimal Amount,
        DateTime QuoteTime);

    public record StockKLineDto(
        string Time,
        decimal Open,
        decimal Close,
        decimal High,
        decimal Low,
        decimal Volume,
        decimal Amount,
        decimal ChangeRate);

    public record StockSearchDto(string Code, string Market, string Name, string Type);

    public interface IStockQuoteService
    {
        Task<StockQuoteDto?> GetQuoteAsync(string code, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<StockQuoteDto>> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<StockKLineDto>> GetKLinesAsync(string code, string period, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<StockSearchDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
        string InferMarket(string code);
        string ToSecId(string code);
    }

    public sealed class EastmoneyStockQuoteService : IStockQuoteService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan _quoteCacheTtl = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _snapshotSaveDelay = TimeSpan.FromMilliseconds(100);
        
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _context;
        private readonly ILogger<EastmoneyStockQuoteService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

        public EastmoneyStockQuoteService(
            IHttpClientFactory httpClientFactory, 
            AppDbContext context, 
            ILogger<EastmoneyStockQuoteService> logger,
            IMemoryCache cache,
            IServiceProvider serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _logger = logger;
            _cache = cache;
            _serviceProvider = serviceProvider;
        }

        public string InferMarket(string code)
        {
            code = NormalizeCode(code);
            if (code.Length == 5) return "HK";
            if (code.StartsWith("6") || code.StartsWith("9")) return "SH";
            if (code.StartsWith("0") || code.StartsWith("2") || code.StartsWith("3")) return "SZ";
            if (code.StartsWith("4") || code.StartsWith("8")) return "BJ";
            return "A";
        }

        public string ToSecId(string code)
        {
            code = NormalizeCode(code);
            return InferMarket(code) switch
            {
                "HK" => $"116.{code}",
                "SH" => $"1.{code}",
                "BJ" => $"0.{code}",
                _ => $"0.{code}"
            };
        }

        public async Task<StockQuoteDto?> GetQuoteAsync(string code, CancellationToken cancellationToken = default)
        {
            code = NormalizeCode(code);
            if (!IsStockCode(code)) return null;

            // 检查缓存
            var cacheKey = $"quote_{code}";
            if (_cache.TryGetValue<StockQuoteDto>(cacheKey, out var cachedQuote))
            {
                return cachedQuote;
            }

            var market = InferMarket(code);
            var client = _httpClientFactory.CreateClient("EastMoneyQuote");
            var fieldsCache = "f43,f44,f45,f46,f47,f48,f57,f58,f60,f107,f116,f169,f170,f86";
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={Uri.EscapeDataString(ToSecId(code))}&fields={Uri.EscapeDataString(fieldsCache)}";

            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return null;

            var quote = new StockQuoteDto(
                Code: NormalizeCode(GetString(data, "f57", code)),
                Market: market,
                Name: GetString(data, "f58", code),
                Price: ScalePrice(GetDecimal(data, "f43"), market),
                ChangeAmount: ScalePrice(GetDecimal(data, "f169"), market),
                ChangeRate: ScalePercent(GetDecimal(data, "f170")),
                Open: ScalePrice(GetDecimal(data, "f46"), market),
                High: ScalePrice(GetDecimal(data, "f44"), market),
                Low: ScalePrice(GetDecimal(data, "f45"), market),
                PreviousClose: ScalePrice(GetDecimal(data, "f60"), market),
                Volume: GetDecimal(data, "f47"),
                Amount: GetDecimal(data, "f48"),
                QuoteTime: DateTime.Now);

            // 缓存结果
            _cache.Set(cacheKey, quote, _quoteCacheTtl);

            // 异步保存快照（不阻塞主流程）
            _ = Task.Run(async () =>
            {
                await _saveSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    context.StockQuoteSnapshots.Add(new StockQuoteSnapshot
                    {
                        StockCode = quote.Code,
                        Market = quote.Market,
                        StockName = quote.Name,
                        LatestPrice = quote.Price,
                        ChangeAmount = quote.ChangeAmount,
                        ChangeRate = quote.ChangeRate,
                        OpenPrice = quote.Open,
                        HighPrice = quote.High,
                        LowPrice = quote.Low,
                        PreviousClose = quote.PreviousClose,
                        Volume = quote.Volume,
                        Amount = quote.Amount,
                        QuoteTime = quote.QuoteTime,
                        CreatedAt = DateTime.Now
                    });
                    await context.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "保存股票行情快照失败：{Code}", code);
                }
                finally
                {
                    _saveSemaphore.Release();
                }
            }, cancellationToken);

            return quote;
        }

        public async Task<IReadOnlyList<StockQuoteDto>> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
        {
            var normalizedCodes = codes.Select(NormalizeCode).Where(IsStockCode).Distinct().ToList();
            if (normalizedCodes.Count == 0) return Array.Empty<StockQuoteDto>();
            
            // 并行请求，限制并发数避免过载
            var semaphore = new SemaphoreSlim(10);
            var tasks = normalizedCodes.Select(async code =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await GetQuoteAsync(code, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            var quotes = await Task.WhenAll(tasks);
            return quotes.Where(q => q != null).ToList()!;
        }

        public async Task<IReadOnlyList<StockKLineDto>> GetKLinesAsync(string code, string period, CancellationToken cancellationToken = default)
        {
            code = NormalizeCode(code);
            period = NormalizePeriod(period);
            if (!IsStockCode(code)) return Array.Empty<StockKLineDto>();

            var cache = await _context.StockKLineCaches
                .FirstOrDefaultAsync(x => x.StockCode == code && x.Period == period, cancellationToken);

            var cacheSeconds = period is "minute" or "hour" ? 20 : 3600;
            if (cache != null && (DateTime.Now - cache.RefreshedAt).TotalSeconds < cacheSeconds)
            {
                return JsonSerializer.Deserialize<List<StockKLineDto>>(cache.PayloadJson, JsonOptions) ?? new List<StockKLineDto>();
            }

            var rows = period == "minute"
                ? await GetTrendLinesAsync(code, cancellationToken)
                : await GetHistoryKLinesAsync(code, period, cancellationToken);

            var payload = JsonSerializer.Serialize(rows, JsonOptions);
            if (cache == null)
            {
                _context.StockKLineCaches.Add(new StockKLineCache
                {
                    StockCode = code,
                    Market = InferMarket(code),
                    Period = period,
                    PayloadJson = payload,
                    RefreshedAt = DateTime.Now
                });
            }
            else
            {
                cache.PayloadJson = payload;
                cache.RefreshedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return rows;
        }

        public async Task<IReadOnlyList<StockSearchDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
        {
            keyword = (keyword ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<StockSearchDto>();

            var exactCode = NormalizeCode(keyword);
            if (IsStockCode(exactCode))
            {
                var q = await GetQuoteAsync(exactCode, cancellationToken);
                if (q != null) return new[] { new StockSearchDto(q.Code, q.Market, q.Name, "quote") };
            }

            var client = _httpClientFactory.CreateClient("EastMoneyQuote");
            var url = $"https://searchapi.eastmoney.com/api/suggest/get?input={Uri.EscapeDataString(keyword)}&type=14&token=683a570d857a342d4fb06698ab1d6ea7";
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var result = new List<StockSearchDto>();
            if (doc.RootElement.TryGetProperty("QuotationCodeTable", out var table)
                && table.TryGetProperty("Data", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var code = NormalizeCode(GetString(item, "Code", string.Empty));
                    if (!IsStockCode(code)) continue;

                    var type = GetString(item, "SecurityTypeName", "stock");
                    var market = InferMarket(code);
                    if (type.Contains("港", StringComparison.OrdinalIgnoreCase)) market = "HK";

                    result.Add(new StockSearchDto(code, market, GetString(item, "Name", code), type));
                }
            }

            return result
                .GroupBy(x => new { x.Market, x.Code })
                .Select(g => g.First())
                .Take(12)
                .ToList();
        }

        private async Task<IReadOnlyList<StockKLineDto>> GetTrendLinesAsync(string code, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("EastMoneyQuote");
            var fields1 = "f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11";
            var fields2 = "f51,f52,f53,f54,f55,f56,f57,f58";
            var url = $"https://push2his.eastmoney.com/api/qt/stock/trends2/get?secid={Uri.EscapeDataString(ToSecId(code))}&fields1={fields1}&fields2={fields2}&iscr=0&iscca=0";
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var rows = new List<StockKLineDto>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("trends", out var arr)) return rows;

            foreach (var el in arr.EnumerateArray())
            {
                var parts = (el.GetString() ?? string.Empty).Split(',');
                if (parts.Length < 5) continue;

                var price = ParseDecimal(parts[2]);
                var volume = ParseDecimal(parts.Length > 5 ? parts[5] : "0");
                var amount = ParseDecimal(parts.Length > 6 ? parts[6] : "0");
                rows.Add(new StockKLineDto(parts[0], price, price, price, price, volume, amount, 0));
            }

            return rows;
        }

        private async Task<IReadOnlyList<StockKLineDto>> GetHistoryKLinesAsync(string code, string period, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient("EastMoneyQuote");
            var klt = period switch
            {
                "hour" => "60",
                "day" => "101",
                "month" => "103",
                "year" => "106",
                _ => "101"
            };

            var limit = period == "year" ? 20 : 240;
            var fields1 = "f1,f2,f3,f4,f5,f6";
            var fields2 = "f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61";
            var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={Uri.EscapeDataString(ToSecId(code))}&klt={klt}&fqt=1&lmt={limit}&end=20500101&fields1={fields1}&fields2={fields2}";
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var rows = new List<StockKLineDto>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("klines", out var arr)) return rows;

            foreach (var el in arr.EnumerateArray())
            {
                var parts = (el.GetString() ?? string.Empty).Split(',');
                if (parts.Length < 11) continue;

                rows.Add(new StockKLineDto(
                    Time: parts[0],
                    Open: ParseDecimal(parts[1]),
                    Close: ParseDecimal(parts[2]),
                    High: ParseDecimal(parts[3]),
                    Low: ParseDecimal(parts[4]),
                    Volume: ParseDecimal(parts[5]),
                    Amount: ParseDecimal(parts[6]),
                    ChangeRate: ParseDecimal(parts[8])));
            }

            return rows;
        }

        private static string NormalizePeriod(string period) => (period ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "min" or "minute" or "fen" or "分k" => "minute",
            "hour" or "60" or "时k" => "hour",
            "day" or "daily" or "日k" => "day",
            "month" or "monthly" or "月k" => "month",
            "year" or "yearly" or "年k" => "year",
            _ => "day"
        };

        private static bool IsStockCode(string code)
        {
            code = NormalizeCode(code);
            return Regex.IsMatch(code, "^[0-9]{5}$") || Regex.IsMatch(code, "^[0-9]{6}$");
        }

        private static string NormalizeCode(string code)
        {
            var text = (code ?? string.Empty).Trim().ToUpperInvariant();

            var secIdMatch = Regex.Match(text, @"(?<!\d)116\.(\d{5})(?!\d)");
            if (secIdMatch.Success) return secIdMatch.Groups[1].Value;

            text = text
                .Replace(".HK", "")
                .Replace("HK", "")
                .Replace("SH", "")
                .Replace("SZ", "")
                .Replace("BJ", "");

            var digits = Regex.Replace(text, "\\D", "");
            if (string.IsNullOrWhiteSpace(digits)) return string.Empty;

            if (digits.Length == 5) return digits;
            if (digits.Length == 6) return digits;
            if (digits.Length > 6) return digits[^6..];
            return digits.PadLeft(6, '0');
        }

        private static decimal ScalePrice(decimal value, string market)
        {
            if (value == 0) return 0;
            return market == "HK"
                ? Math.Round(value / 1000m, 4)
                : Math.Round(value / 100m, 4);
        }

        private static decimal ScalePercent(decimal value) => value == 0 ? 0 : Math.Round(value / 100m, 4);
        private static decimal ParseDecimal(string? value) => decimal.TryParse(value, out var d) ? d : 0;

        private static decimal GetDecimal(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var p)) return 0;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var s)) return s;
            return 0;
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (!element.TryGetProperty(name, out var p)) return fallback;
            return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? fallback) : p.ToString();
        }
    }
}
