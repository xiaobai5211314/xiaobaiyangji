using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using 小白养基.Models;

namespace 小白养基.Services
{
    public record StockQuoteDto(
        string Code,
        string Market,
        string Name,
        decimal? Price,
        decimal? ChangeAmount,
        decimal? ChangeRate,
        decimal? Open,
        decimal? High,
        decimal? Low,
        decimal? PreviousClose,
        decimal? Volume,
        decimal? Amount,
        DateTime? QuoteTime,
        bool QuoteOk,
        string QuoteSource,
        bool IsFallback,
        bool IsStale,
        string? ErrorMessage);

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
        private static readonly HashSet<string> LiveSources = new(StringComparer.OrdinalIgnoreCase)
        {
            "eastmoney",
            "tencent",
            "sina"
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _context;
        private readonly ILogger<EastmoneyStockQuoteService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

        public EastmoneyStockQuoteService(
            IHttpClientFactory httpClientFactory,
            AppDbContext context,
            ILogger<EastmoneyStockQuoteService> logger,
            IMemoryCache cache,
            IServiceScopeFactory scopeFactory)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _logger = logger;
            _cache = cache;
            _scopeFactory = scopeFactory;
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

            var cacheKey = $"quote_{code}";
            if (_cache.TryGetValue<StockQuoteDto>(cacheKey, out var cachedQuote))
            {
                return cachedQuote;
            }

            var errors = new List<string>();
            var candidates = new List<StockQuoteDto>();

            var eastmoney = await TryFetchSourceAsync("eastmoney", code, () => FetchEastmoneyQuoteAsync(code, cancellationToken), errors);
            if (eastmoney != null) candidates.Add(eastmoney);

            var needsBackup =
                eastmoney == null ||
                !eastmoney.QuoteOk ||
                eastmoney.Price is null or <= 0 ||
                eastmoney.PreviousClose is null or <= 0 ||
                (eastmoney.ChangeRate.HasValue && Math.Abs(eastmoney.ChangeRate.Value) < 0.0001m);

            StockQuoteDto? tencent = null;
            StockQuoteDto? sina = null;
            if (needsBackup)
            {
                tencent = await TryFetchSourceAsync("tencent", code, () => FetchTencentQuoteAsync(code, cancellationToken), errors);
                if (tencent != null) candidates.Add(tencent);

                sina = await TryFetchSourceAsync("sina", code, () => FetchSinaQuoteAsync(code, cancellationToken), errors);
                if (sina != null) candidates.Add(sina);
            }

            var liveQuote = SelectBestLiveQuote(code, candidates, errors);
            if (liveQuote != null)
            {
                _cache.Set(cacheKey, liveQuote, _quoteCacheTtl);
                _logger.LogInformation(
                    "stock quote code={Code} source={Source} price={Price} previousClose={PreviousClose} changeRate={ChangeRate} quoteOk={QuoteOk} isFallback={IsFallback} isStale={IsStale} quoteTime={QuoteTime} error={Error}",
                    liveQuote.Code,
                    liveQuote.QuoteSource,
                    liveQuote.Price,
                    liveQuote.PreviousClose,
                    liveQuote.ChangeRate,
                    liveQuote.QuoteOk,
                    liveQuote.IsFallback,
                    liveQuote.IsStale,
                    liveQuote.QuoteTime,
                    liveQuote.ErrorMessage);

                SaveSnapshotInBackground(liveQuote);
                return liveQuote;
            }

            var snapshot = await BuildSnapshotQuoteAsync(code, errors, cancellationToken)
                ?? BuildMissingQuote(code, string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e))));

            _cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(15));
            _logger.LogWarning(
                "stock quote code={Code} source={Source} price={Price} previousClose={PreviousClose} changeRate={ChangeRate} quoteOk={QuoteOk} isFallback={IsFallback} isStale={IsStale} quoteTime={QuoteTime} error={Error}",
                snapshot.Code,
                snapshot.QuoteSource,
                snapshot.Price,
                snapshot.PreviousClose,
                snapshot.ChangeRate,
                snapshot.QuoteOk,
                snapshot.IsFallback,
                snapshot.IsStale,
                snapshot.QuoteTime,
                snapshot.ErrorMessage);

            return snapshot;
        }

        public async Task<IReadOnlyList<StockQuoteDto>> GetQuotesAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
        {
            var normalizedCodes = codes.Select(NormalizeCode).Where(IsStockCode).Distinct().ToList();
            if (normalizedCodes.Count == 0) return Array.Empty<StockQuoteDto>();

            var semaphore = new SemaphoreSlim(10);
            var tasks = normalizedCodes.Select(async code =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await GetQuoteAsync(code, cancellationToken)
                        ?? BuildMissingQuote(code, "股票代码无效");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "查询股票行情失败：{Code}", code);
                    return BuildMissingQuote(code, ex.Message);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            return await Task.WhenAll(tasks);
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
                if (q != null) return new[] { new StockSearchDto(q.Code, q.Market, q.Name, q.QuoteSource) };
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

        private async Task<StockQuoteDto?> TryFetchSourceAsync(
            string source,
            string code,
            Func<Task<StockQuoteDto?>> fetch,
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
                }

                return quote;
            }
            catch (Exception ex)
            {
                errors.Add($"{source} failed: {ex.Message}");
                _logger.LogWarning(ex, "{Source} 股票行情失败：{Code}", source, code);
                return null;
            }
        }

        private async Task<StockQuoteDto?> FetchEastmoneyQuoteAsync(string code, CancellationToken cancellationToken)
        {
            var market = InferMarket(code);
            var client = _httpClientFactory.CreateClient("EastMoneyQuote");
            var fieldsCache = "f43,f44,f45,f46,f47,f48,f57,f58,f60,f107,f116,f169,f170,f86";
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={Uri.EscapeDataString(ToSecId(code))}&fields={Uri.EscapeDataString(fieldsCache)}";

            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return null;

            var quoteTime = GetDecimal(data, "f86") is { } rawTime && rawTime > 0
                ? ParseEastmoneyTime(rawTime.ToString(CultureInfo.InvariantCulture))
                : DateTime.Now;

            return NormalizeLiveQuote(
                code: NormalizeCode(GetString(data, "f57", code)),
                market: market,
                name: GetString(data, "f58", code),
                price: ScalePrice(GetDecimal(data, "f43"), market),
                changeAmount: ScalePrice(GetDecimal(data, "f169"), market),
                changeRate: ScalePercent(GetDecimal(data, "f170")),
                open: ScalePrice(GetDecimal(data, "f46"), market),
                high: ScalePrice(GetDecimal(data, "f44"), market),
                low: ScalePrice(GetDecimal(data, "f45"), market),
                previousClose: ScalePrice(GetDecimal(data, "f60"), market),
                volume: GetDecimal(data, "f47"),
                amount: GetDecimal(data, "f48"),
                quoteTime: quoteTime,
                source: "eastmoney",
                isFallback: false,
                warning: null);
        }

        private async Task<StockQuoteDto?> FetchTencentQuoteAsync(string code, CancellationToken cancellationToken)
        {
            var market = InferMarket(code);
            var symbol = ToTencentSymbol(code, market);
            var client = _httpClientFactory.CreateClient("TencentQuote");
            var text = await client.GetStringAsync($"https://qt.gtimg.cn/q={symbol}", cancellationToken);
            var payload = ExtractQuotedPayload(text);
            if (string.IsNullOrWhiteSpace(payload)) return null;

            var parts = payload.Split('~');
            if (parts.Length < 35) return null;

            var quoteTime = ParseTencentTime(GetPart(parts, 30)) ?? DateTime.Now;
            return NormalizeLiveQuote(
                code: NormalizeCode(GetPart(parts, 2, code)),
                market: market,
                name: GetPart(parts, 1, code),
                price: ParseDecimalNullable(GetPart(parts, 3)),
                changeAmount: ParseDecimalNullable(GetPart(parts, 31)),
                changeRate: ParseDecimalNullable(GetPart(parts, 32)),
                open: ParseDecimalNullable(GetPart(parts, 5)),
                high: ParseDecimalNullable(GetPart(parts, 33)),
                low: ParseDecimalNullable(GetPart(parts, 34)),
                previousClose: ParseDecimalNullable(GetPart(parts, 4)),
                volume: ParseDecimalNullable(GetPart(parts, 36)),
                amount: ParseDecimalNullable(GetPart(parts, 37)),
                quoteTime: quoteTime,
                source: "tencent",
                isFallback: true,
                warning: null);
        }

        private async Task<StockQuoteDto?> FetchSinaQuoteAsync(string code, CancellationToken cancellationToken)
        {
            var market = InferMarket(code);
            var symbol = ToSinaSymbol(code, market);
            var client = _httpClientFactory.CreateClient("SinaQuote");
            var text = await client.GetStringAsync($"https://hq.sinajs.cn/list={symbol}", cancellationToken);
            var payload = ExtractQuotedPayload(text);
            if (string.IsNullOrWhiteSpace(payload)) return null;

            var parts = payload.Split(',');
            if (parts.Length < 32) return null;

            var dateText = GetPart(parts, 30);
            var timeText = GetPart(parts, 31);
            var quoteTime = DateTime.TryParse($"{dateText} {timeText}", out var parsedTime) ? parsedTime : DateTime.Now;
            var price = ParseDecimalNullable(GetPart(parts, 3));
            var previousClose = ParseDecimalNullable(GetPart(parts, 2));
            decimal? changeAmount = price.HasValue && previousClose.HasValue ? price.Value - previousClose.Value : null;
            decimal? changeRate = price.HasValue && previousClose is > 0 ? Math.Round((price.Value - previousClose.Value) / previousClose.Value * 100m, 4) : null;

            return NormalizeLiveQuote(
                code: code,
                market: market,
                name: GetPart(parts, 0, code),
                price: price,
                changeAmount: changeAmount,
                changeRate: changeRate,
                open: ParseDecimalNullable(GetPart(parts, 1)),
                high: ParseDecimalNullable(GetPart(parts, 4)),
                low: ParseDecimalNullable(GetPart(parts, 5)),
                previousClose: previousClose,
                volume: ParseDecimalNullable(GetPart(parts, 8)),
                amount: ParseDecimalNullable(GetPart(parts, 9)),
                quoteTime: quoteTime,
                source: "sina",
                isFallback: true,
                warning: null);
        }

        private StockQuoteDto? SelectBestLiveQuote(string code, List<StockQuoteDto> candidates, List<string> errors)
        {
            var valid = candidates
                .Where(q => q.QuoteOk && q.Price is > 0 && q.PreviousClose is > 0 && q.ChangeRate.HasValue)
                .ToList();
            if (valid.Count == 0) return null;

            var eastmoney = valid.FirstOrDefault(q => q.QuoteSource == "eastmoney");
            var backup = valid.FirstOrDefault(q => q.QuoteSource != "eastmoney");
            StockQuoteDto selected = eastmoney ?? valid[0];

            if (eastmoney != null && backup != null)
            {
                var eastRateZero = Math.Abs(eastmoney.ChangeRate.GetValueOrDefault()) < 0.0001m;
                var backupRateNotZero = Math.Abs(backup.ChangeRate.GetValueOrDefault()) >= 0.01m;
                if (eastRateZero && backupRateNotZero)
                {
                    selected = backup with
                    {
                        IsFallback = true,
                        ErrorMessage = AppendMessage(backup.ErrorMessage, "东方财富涨跌幅为 0，已切换备用源")
                    };
                }
                else
                {
                    var priceDiff = RelativeDiff(eastmoney.Price.GetValueOrDefault(), backup.Price.GetValueOrDefault());
                    if (priceDiff > 0.03m)
                    {
                        selected = eastmoney with
                        {
                            ErrorMessage = AppendMessage(eastmoney.ErrorMessage, $"备用源价格差异 {priceDiff:P2}")
                        };
                    }
                }
            }
            else if (selected.QuoteSource != "eastmoney")
            {
                selected = selected with { IsFallback = true };
            }

            if (errors.Count > 0)
            {
                selected = selected with { ErrorMessage = AppendMessage(selected.ErrorMessage, string.Join("; ", errors)) };
            }

            return selected;
        }

        private async Task<StockQuoteDto?> BuildSnapshotQuoteAsync(string code, List<string> errors, CancellationToken cancellationToken)
        {
            var snapshot = await _context.StockQuoteSnapshots
                .AsNoTracking()
                .Where(x => x.StockCode == code)
                .OrderByDescending(x => x.QuoteTime)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (snapshot == null) return null;

            var today = DateTime.Now.Date;
            var isToday = snapshot.QuoteTime.Date == today || snapshot.CreatedAt.Date == today;
            return new StockQuoteDto(
                Code: snapshot.StockCode,
                Market: string.IsNullOrWhiteSpace(snapshot.Market) ? InferMarket(snapshot.StockCode) : snapshot.Market,
                Name: string.IsNullOrWhiteSpace(snapshot.StockName) ? snapshot.StockCode : snapshot.StockName,
                Price: snapshot.LatestPrice,
                ChangeAmount: snapshot.ChangeAmount,
                ChangeRate: snapshot.ChangeRate,
                Open: snapshot.OpenPrice,
                High: snapshot.HighPrice,
                Low: snapshot.LowPrice,
                PreviousClose: snapshot.PreviousClose,
                Volume: snapshot.Volume,
                Amount: snapshot.Amount,
                QuoteTime: snapshot.QuoteTime,
                QuoteOk: false,
                QuoteSource: isToday ? "snapshot_today" : "snapshot_old",
                IsFallback: true,
                IsStale: !isToday,
                ErrorMessage: AppendMessage(isToday ? "实时源失败，使用今日快照" : "实时源失败，使用历史快照", string.Join("; ", errors)));
        }

        private static StockQuoteDto NormalizeLiveQuote(
            string code,
            string market,
            string name,
            decimal? price,
            decimal? changeAmount,
            decimal? changeRate,
            decimal? open,
            decimal? high,
            decimal? low,
            decimal? previousClose,
            decimal? volume,
            decimal? amount,
            DateTime quoteTime,
            string source,
            bool isFallback,
            string? warning)
        {
            if ((!changeRate.HasValue || !changeAmount.HasValue) && price.HasValue && previousClose is > 0)
            {
                changeAmount ??= Math.Round(price.Value - previousClose.Value, 4);
                changeRate ??= Math.Round((price.Value - previousClose.Value) / previousClose.Value * 100m, 4);
            }

            var quoteOk = price is > 0 && previousClose is > 0 && changeRate.HasValue;
            var error = quoteOk ? warning : AppendMessage(warning, "price/previousClose/changeRate 缺失或无效");

            return new StockQuoteDto(
                Code: NormalizeCode(code),
                Market: market,
                Name: string.IsNullOrWhiteSpace(name) ? NormalizeCode(code) : name.Trim(),
                Price: price.HasValue ? Math.Round(price.Value, 4) : null,
                ChangeAmount: changeAmount.HasValue ? Math.Round(changeAmount.Value, 4) : null,
                ChangeRate: changeRate.HasValue ? Math.Round(changeRate.Value, 4) : null,
                Open: open.HasValue ? Math.Round(open.Value, 4) : null,
                High: high.HasValue ? Math.Round(high.Value, 4) : null,
                Low: low.HasValue ? Math.Round(low.Value, 4) : null,
                PreviousClose: previousClose.HasValue ? Math.Round(previousClose.Value, 4) : null,
                Volume: volume,
                Amount: amount,
                QuoteTime: quoteTime,
                QuoteOk: quoteOk,
                QuoteSource: source,
                IsFallback: isFallback,
                IsStale: false,
                ErrorMessage: error);
        }

        private StockQuoteDto BuildMissingQuote(string code, string? error)
        {
            code = NormalizeCode(code);
            return new StockQuoteDto(
                Code: code,
                Market: InferMarket(code),
                Name: code,
                Price: null,
                ChangeAmount: null,
                ChangeRate: null,
                Open: null,
                High: null,
                Low: null,
                PreviousClose: null,
                Volume: null,
                Amount: null,
                QuoteTime: null,
                QuoteOk: false,
                QuoteSource: "missing",
                IsFallback: false,
                IsStale: false,
                ErrorMessage: string.IsNullOrWhiteSpace(error) ? "所有行情源均失败" : error);
        }

        private void SaveSnapshotInBackground(StockQuoteDto quote)
        {
            if (!quote.QuoteOk ||
                !LiveSources.Contains(quote.QuoteSource) ||
                quote.Price is not > 0 ||
                quote.PreviousClose is not > 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                await _saveSemaphore.WaitAsync();
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    using var saveCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    context.StockQuoteSnapshots.Add(new StockQuoteSnapshot
                    {
                        StockCode = quote.Code,
                        Market = quote.Market,
                        StockName = quote.Name,
                        LatestPrice = quote.Price.Value,
                        ChangeAmount = quote.ChangeAmount ?? 0,
                        ChangeRate = quote.ChangeRate ?? 0,
                        OpenPrice = quote.Open ?? 0,
                        HighPrice = quote.High ?? 0,
                        LowPrice = quote.Low ?? 0,
                        PreviousClose = quote.PreviousClose ?? 0,
                        Volume = quote.Volume ?? 0,
                        Amount = quote.Amount ?? 0,
                        QuoteTime = quote.QuoteTime ?? DateTime.Now,
                        CreatedAt = DateTime.Now
                    });
                    await context.SaveChangesAsync(saveCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "保存股票行情快照失败：{Code}", quote.Code);
                }
                finally
                {
                    _saveSemaphore.Release();
                }
            });
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

        private static decimal? ScalePrice(decimal? value, string market)
        {
            if (!value.HasValue) return null;
            if (value.Value == 0) return 0;
            return market == "HK"
                ? Math.Round(value.Value / 1000m, 4)
                : Math.Round(value.Value / 100m, 4);
        }

        private static decimal? ScalePercent(decimal? value)
            => value.HasValue ? Math.Round(value.Value / 100m, 4) : null;

        private static decimal ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;

        private static decimal? ParseDecimalNullable(string? value)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value) || value == "-" || value == "--") return null;
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        private static decimal? GetDecimal(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String)
            {
                var text = p.GetString();
                if (string.IsNullOrWhiteSpace(text) || text == "-" || text == "--") return null;
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
            }
            return null;
        }

        private static string GetString(JsonElement element, string name, string fallback)
        {
            if (!element.TryGetProperty(name, out var p)) return fallback;
            return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? fallback) : p.ToString();
        }

        private static string GetPart(string[] parts, int index, string fallback = "")
            => index >= 0 && index < parts.Length ? (parts[index] ?? fallback) : fallback;

        private static string ExtractQuotedPayload(string text)
        {
            var match = Regex.Match(text ?? string.Empty, "\"(.*)\"");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static DateTime ParseEastmoneyTime(string raw)
        {
            raw = Regex.Replace(raw ?? string.Empty, "\\D", "");
            if (DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }
            return DateTime.Now;
        }

        private static DateTime? ParseTencentTime(string raw)
        {
            raw = Regex.Replace(raw ?? string.Empty, "\\D", "");
            return DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : null;
        }

        private static string ToTencentSymbol(string code, string market)
        {
            return market switch
            {
                "HK" => $"hk{code}",
                "SH" => $"sh{code}",
                "BJ" => $"bj{code}",
                _ => $"sz{code}"
            };
        }

        private static string ToSinaSymbol(string code, string market)
        {
            return market switch
            {
                "HK" => $"hk{code}",
                "SH" => $"sh{code}",
                "BJ" => $"bj{code}",
                _ => $"sz{code}"
            };
        }

        private static decimal RelativeDiff(decimal a, decimal b)
        {
            var baseValue = Math.Max(Math.Abs(a), Math.Abs(b));
            return baseValue <= 0 ? 0 : Math.Abs(a - b) / baseValue;
        }

        private static string? AppendMessage(string? first, string? second)
        {
            if (string.IsNullOrWhiteSpace(first)) return string.IsNullOrWhiteSpace(second) ? null : second;
            if (string.IsNullOrWhiteSpace(second)) return first;
            return $"{first}; {second}";
        }
    }
}
