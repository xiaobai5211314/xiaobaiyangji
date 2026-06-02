using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using 小白养基.Models;
using 小白养基.Services;

namespace 小白养基.Controllers
{
    [ApiController]
    [Route("api/stock")]
    public class StockController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IStockQuoteService _quotes;
        private readonly IBaiduOcrService _ocr;
        private readonly StockOcrParserService _parser;
        private readonly ILogger<StockController> _logger;

        public StockController(
            AppDbContext context,
            IStockQuoteService quotes,
            IBaiduOcrService ocr,
            StockOcrParserService parser,
            ILogger<StockController> logger)
        {
            _context = context;
            _quotes = quotes;
            _ocr = ocr;
            _parser = parser;
            _logger = logger;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard([FromQuery] string username, CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);
            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });

            var holdings = await _context.StockHoldings.AsNoTracking()
                .Where(x => x.Username == username)
                .OrderBy(x => x.Market)
                .ThenBy(x => x.StockCode)
                .ToListAsync(cancellationToken);

            var watch = await _context.StockWatchItems.AsNoTracking()
                .Where(x => x.Username == username)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Market)
                .ThenBy(x => x.StockCode)
                .ToListAsync(cancellationToken);

            var codes = holdings
                .Select(x => x.StockCode)
                .Concat(watch.Select(x => x.StockCode))
                .Distinct()
                .ToList();

            var quoteList = await _quotes.GetQuotesAsync(codes, cancellationToken);
            var quoteMap = quoteList
                .ToDictionary(x => x.Code, x => x);

            var quoteRequestedCount = codes.Count;
            var quoteSuccessCount = quoteList.Count(x => x.QuoteOk);
            var quoteRealtimeCount = quoteList.Count(x => x.QuoteOk && IsRealtimeQuoteSource(x.QuoteSource));
            var quoteFallbackCodes = quoteList
                .Where(x => x.IsFallback)
                .Select(x => x.Code)
                .Distinct()
                .ToList();
            var quoteStaleCodes = quoteList
                .Where(x => x.IsStale || x.QuoteSource is "snapshot_old" or "holding_last")
                .Select(x => x.Code)
                .Distinct()
                .ToList();
            var quoteFailedCodes = quoteList
                .Where(x => !x.QuoteOk && x.QuoteSource == "missing")
                .Select(x => x.Code)
                .Distinct()
                .ToList();

            return Ok(new
            {
                success = true,
                username,
                holdings = holdings.Select(x => EnrichHolding(x, quoteMap.GetValueOrDefault(x.StockCode))).ToList(),
                watchList = watch.Select(x => EnrichWatch(x, quoteMap.GetValueOrDefault(x.StockCode))).ToList(),
                quoteRequestedCount,
                quoteSuccessCount,
                quoteRealtimeCount,
                quoteFallbackCount = quoteFallbackCodes.Count,
                quoteFailedCodes,
                quoteFallbackCodes,
                quoteStaleCodes,
                hasFreshQuotes = quoteRequestedCount > 0 && quoteRealtimeCount == quoteRequestedCount,
                updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        [HttpGet("quote")]
        public async Task<IActionResult> Quote([FromQuery] string code, CancellationToken cancellationToken)
        {
            var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
            if (quote == null) return NotFound(new { success = false, message = "未找到股票行情" });

            return Ok(new { success = true, quote });
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string keyword, CancellationToken cancellationToken)
        {
            var rows = await _quotes.SearchAsync(keyword, cancellationToken);
            return Ok(new { success = true, items = rows });
        }

        [HttpGet("klines")]
        public async Task<IActionResult> KLines(
            [FromQuery] string code,
            [FromQuery] string period = "day",
            CancellationToken cancellationToken = default)
        {
            var rows = await _quotes.GetKLinesAsync(code, period, cancellationToken);
            return Ok(new { success = true, code = NormalizeCode(code), period, items = rows });
        }

        [HttpPost("holding")]
        public async Task<IActionResult> SaveHolding([FromBody] SaveStockHoldingRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(new { success = false, message = "请求体为空" });

            var username = NormalizeUser(request.Username);
            var identity = await ResolveStockIdentityAsync(request.StockCode, request.Market, request.StockName, cancellationToken);
            var code = identity.Code;

            if (username == null || string.IsNullOrWhiteSpace(code))
            {
                return BadRequest(new { success = false, message = "用户名或股票代码为空" });
            }

            var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
            var name = !string.IsNullOrWhiteSpace(identity.Name) ? identity.Name : quote?.Name ?? code;
            var market = quote?.Market ?? identity.Market ?? _quotes.InferMarket(code);

            var row = await _context.StockHoldings
                .FirstOrDefaultAsync(x => x.Username == username && x.Market == market && x.StockCode == code, cancellationToken)
                ?? await _context.StockHoldings
                    .FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);

            if (row == null)
            {
                row = new StockHolding
                {
                    Username = username,
                    StockCode = code,
                    CreatedAt = DateTime.Now
                };
                _context.StockHoldings.Add(row);
            }

            row.Market = market;
            row.StockCode = code;
            row.StockName = name;
            row.Shares = Math.Max(0, request.Shares);
            row.CostPrice = Math.Max(0, request.CostPrice);
            row.CostAmount = request.CostAmount > 0
                ? request.CostAmount
                : Math.Round(row.Shares * row.CostPrice, 2);
            row.UpdatedAt = DateTime.Now;

            ApplyQuote(row, quote);

            await _context.SaveChangesAsync(cancellationToken);
            await UpsertWatchAsync(username, code, row.Market, row.StockName, cancellationToken);

            return Ok(new { success = true, item = EnrichHolding(row, quote) });
        }

        [HttpDelete("holding")]
        public async Task<IActionResult> DeleteHolding(
            [FromQuery] string username,
            [FromQuery] string code,
            [FromQuery] string? market,
            CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);
            code = NormalizeCode(code, market, null);
            market = NormalizeMarket(market);

            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });

            var query = _context.StockHoldings.Where(x => x.Username == username && x.StockCode == code);
            if (!string.IsNullOrWhiteSpace(market))
            {
                query = query.Where(x => x.Market == market);
            }

            var row = await query.FirstOrDefaultAsync(cancellationToken);
            if (row != null)
            {
                _context.StockHoldings.Remove(row);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Ok(new { success = true });
        }

        [HttpPost("watch")]
        public async Task<IActionResult> SaveWatch([FromBody] SaveStockWatchRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(new { success = false, message = "请求体为空" });

            var username = NormalizeUser(request.Username);
            var identity = await ResolveStockIdentityAsync(request.StockCode, request.Market, request.StockName, cancellationToken);
            var code = identity.Code;

            if (username == null || string.IsNullOrWhiteSpace(code))
            {
                return BadRequest(new { success = false, message = "用户名或股票代码为空" });
            }

            var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
            var market = quote?.Market ?? identity.Market ?? _quotes.InferMarket(code);
            var name = !string.IsNullOrWhiteSpace(identity.Name) ? identity.Name : quote?.Name ?? code;

            await UpsertWatchAsync(username, code, market, name, cancellationToken);

            var row = await _context.StockWatchItems.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Market == market && x.StockCode == code, cancellationToken)
                ?? await _context.StockWatchItems.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);

            return Ok(new
            {
                success = true,
                item = row == null ? null : EnrichWatch(row, quote)
            });
        }

        [HttpDelete("watch")]
        public async Task<IActionResult> DeleteWatch(
            [FromQuery] string username,
            [FromQuery] string code,
            [FromQuery] string? market,
            CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);
            code = NormalizeCode(code, market, null);
            market = NormalizeMarket(market);

            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });

            var query = _context.StockWatchItems.Where(x => x.Username == username && x.StockCode == code);
            if (!string.IsNullOrWhiteSpace(market))
            {
                query = query.Where(x => x.Market == market);
            }

            var row = await query.FirstOrDefaultAsync(cancellationToken);
            if (row != null)
            {
                _context.StockWatchItems.Remove(row);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Ok(new { success = true });
        }

        [HttpPost("import-ocr-preview")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> ImportOcrPreview(
            [FromForm] string username,
            [FromForm] IFormFile image,
            CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);

            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });
            if (image == null || image.Length == 0) return BadRequest(new { success = false, message = "请上传股票持仓截图" });

            await using var ms = new MemoryStream();
            await image.CopyToAsync(ms, cancellationToken);

            JObject result;
            try
            {
                result = await _ocr.AccurateWithLocationAsync(ms.ToArray(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "股票 OCR 调用失败：{User}", username);
                return StatusCode(500, new { success = false, message = ex.Message });
            }

            var blocks = result["words_result"]?
                .Select(ToStockOcrTextBlock)
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Text))
                .Cast<StockOcrTextBlock>()
                .ToList() ?? new List<StockOcrTextBlock>();

            var words = blocks
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var candidates = blocks.Any(x => x.Width > 0 && x.Height > 0)
                ? _parser.Parse(blocks)
                : _parser.Parse(words);

            var diagnostics = new List<string>
            {
                $"OCR 文本行数：{words.Count}",
                $"OCR 坐标行数：{blocks.Count(x => x.Width > 0 && x.Height > 0)}",
                $"识别候选：{candidates.Count}",
                "股票持仓 OCR：优先使用 location 坐标按表格列解析市值、盈亏、持仓、成本价。",
                "股票代码补全：如果 OCR 未识别代码，会尝试按股票名称搜索补全。"
            };

            var batch = new StockOcrImportBatch
            {
                Username = username,
                Source = "stock-ocr",
                Status = "preview",
                RawText = string.Join("\n", words),
                DiagnosticsJson = JsonSerializer.Serialize(diagnostics),
                CreatedAt = DateTime.Now
            };

            _context.StockOcrImportBatches.Add(batch);
            await _context.SaveChangesAsync(cancellationToken);

            foreach (var c in candidates)
            {
                var rawName = !string.IsNullOrWhiteSpace(c.StockName) ? c.StockName : c.RecognizedName;
                var identity = await ResolveStockIdentityAsync(c.StockCode, null, rawName, cancellationToken);
                var code = identity.Code;

                batch.Items.Add(new StockOcrImportItem
                {
                    BatchId = batch.Id,
                    StockCode = code,
                    Market = !string.IsNullOrWhiteSpace(identity.Market)
                        ? identity.Market
                        : string.IsNullOrWhiteSpace(code) ? string.Empty : _quotes.InferMarket(code),
                    StockName = !string.IsNullOrWhiteSpace(identity.Name)
                        ? identity.Name
                        : rawName,
                    RecognizedName = c.RecognizedName,
                    Shares = c.Shares,
                    CostPrice = c.CostPrice,
                    CostAmount = c.CostAmount,
                    MarketValue = c.MarketValue,
                    FloatingProfit = c.FloatingProfit,
                    FloatingProfitRate = c.FloatingProfitRate,
                    Action = c.Action,
                    Note = string.IsNullOrWhiteSpace(code)
                        ? c.Note
                        : $"{c.Note}；已按名称/代码补全证券代码"
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            var items = await _context.StockOcrImportItems.AsNoTracking()
                .Where(x => x.BatchId == batch.Id)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                batchId = batch.Id,
                count = items.Count,
                items,
                diagnostics
            });
        }

        [HttpPost("import-ocr-confirm")]
        public async Task<IActionResult> ConfirmOcr([FromBody] ConfirmStockOcrRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(new { success = false, message = "请求体为空" });

            var username = NormalizeUser(request.Username);
            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });

            var batch = await _context.StockOcrImportBatches
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == request.BatchId && x.Username == username, cancellationToken);

            if (batch == null) return NotFound(new { success = false, message = "未找到 OCR 预览批次" });

            var selected = request.Items?.Count > 0
                ? request.Items
                : batch.Items
                    .Select(x => new ConfirmStockOcrItem(
                        x.Id,
                        x.StockCode,
                        x.StockName,
                        x.Action,
                        x.Shares,
                        x.CostPrice,
                        x.CostAmount,
                        x.Market))
                    .ToList();

            var saved = 0;
            var skipped = new List<object>();

            foreach (var item in selected)
            {
                var identity = await ResolveStockIdentityAsync(item.StockCode, item.Market, item.StockName, cancellationToken);
                var code = identity.Code;

                if (string.IsNullOrWhiteSpace(code))
                {
                    skipped.Add(new
                    {
                        item.Id,
                        item.StockName,
                        reason = "未能识别或补全股票代码"
                    });
                    continue;
                }

                var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
                var action = (item.Action ?? "holding").Trim().ToLowerInvariant();
                var name = !string.IsNullOrWhiteSpace(identity.Name)
                    ? identity.Name
                    : quote?.Name ?? code;
                var market = quote?.Market ?? identity.Market ?? _quotes.InferMarket(code);

                if (action == "watch")
                {
                    await UpsertWatchAsync(
                        username,
                        code,
                        market,
                        name,
                        cancellationToken);

                    saved++;
                    continue;
                }

                var row = await _context.StockHoldings
                    .FirstOrDefaultAsync(x => x.Username == username && x.Market == market && x.StockCode == code, cancellationToken)
                    ?? await _context.StockHoldings
                        .FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);

                if (row == null)
                {
                    row = new StockHolding
                    {
                        Username = username,
                        StockCode = code,
                        CreatedAt = DateTime.Now
                    };
                    _context.StockHoldings.Add(row);
                }

                row.StockCode = code;
                row.StockName = name;
                row.Market = market;
                row.Shares = Math.Max(0, item.Shares ?? 0);
                row.CostPrice = Math.Max(0, item.CostPrice ?? 0);
                row.CostAmount = item.CostAmount.GetValueOrDefault() > 0
                    ? item.CostAmount!.Value
                    : Math.Round(row.Shares * row.CostPrice, 2);
                row.UpdatedAt = DateTime.Now;

                ApplyQuote(row, quote);

                await UpsertWatchAsync(username, code, row.Market, row.StockName, cancellationToken);
                saved++;
            }

            batch.Status = "confirmed";
            batch.ConfirmedAt = DateTime.Now;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                saved,
                skipped
            });
        }

        private async Task UpsertWatchAsync(
            string username,
            string code,
            string market,
            string name,
            CancellationToken cancellationToken)
        {
            var watch = await _context.StockWatchItems
                .FirstOrDefaultAsync(x => x.Username == username && x.Market == market && x.StockCode == code, cancellationToken)
                ?? await _context.StockWatchItems
                    .FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);

            if (watch == null)
            {
                var maxSort = await _context.StockWatchItems
                    .Where(x => x.Username == username)
                    .Select(x => (int?)x.SortOrder)
                    .MaxAsync(cancellationToken) ?? 0;

                watch = new StockWatchItem
                {
                    Username = username,
                    StockCode = code,
                    CreatedAt = DateTime.Now,
                    SortOrder = maxSort + 1
                };

                _context.StockWatchItems.Add(watch);
            }

            watch.StockCode = code;
            watch.Market = market;
            watch.StockName = name;
            watch.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync(cancellationToken);
        }

        private async Task<(string Code, string Market, string Name)> ResolveStockIdentityAsync(
            string? code,
            string? market,
            string? name,
            CancellationToken cancellationToken)
        {
            var normalizedName = NormalizeStockName(name);
            var normalizedMarket = NormalizeMarket(market);
            var normalizedCode = NormalizeCode(code ?? string.Empty, normalizedMarket, normalizedName);

            if (!string.IsNullOrWhiteSpace(normalizedCode))
            {
                try
                {
                    var quote = await _quotes.GetQuoteAsync(normalizedCode, cancellationToken);
                    return (
                        normalizedCode,
                        quote?.Market ?? normalizedMarket ?? _quotes.InferMarket(normalizedCode),
                        !string.IsNullOrWhiteSpace(quote?.Name) ? quote.Name : normalizedName
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "按股票代码补全失败：{Code}", normalizedCode);
                    return (normalizedCode, normalizedMarket ?? _quotes.InferMarket(normalizedCode), normalizedName);
                }
            }

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return (string.Empty, string.Empty, string.Empty);
            }

            try
            {
                var matches = await _quotes.SearchAsync(normalizedName, cancellationToken);
                var normalizedTarget = NormalizeStockName(normalizedName);

                var match = matches.FirstOrDefault(x => NormalizeStockName(x.Name) == normalizedTarget)
                    ?? matches.FirstOrDefault(x =>
                        NormalizeStockName(x.Name).Contains(normalizedTarget) ||
                        normalizedTarget.Contains(NormalizeStockName(x.Name)))
                    ?? matches.FirstOrDefault();

                if (match == null)
                {
                    return (string.Empty, string.Empty, normalizedName);
                }

                return (NormalizeCode(match.Code, match.Market, match.Name), match.Market, match.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "按股票名称补全失败：{Name}", normalizedName);
                return (string.Empty, string.Empty, normalizedName);
            }
        }

        private static void ApplyQuote(StockHolding row, StockQuoteDto? quote)
        {
            if (quote?.QuoteOk != true || quote.Price is not { } price) return;

            row.LastPrice = price;
            row.LastRate = quote.ChangeRate;
            row.LastMarketValue = Math.Round(row.Shares * price, 2);
            row.LastProfit = row.LastMarketValue - row.CostAmount;
            row.LastProfitRate = row.CostAmount > 0
                ? Math.Round(row.LastProfit.GetValueOrDefault() / row.CostAmount * 100, 2)
                : 0;
        }

        private static object EnrichHolding(StockHolding x, StockQuoteDto? quote)
        {
            var useHoldingLast = (quote == null || quote.QuoteSource == "missing") && x.LastPrice.HasValue;
            var price = useHoldingLast ? x.LastPrice : quote?.Price;
            decimal? value = price.HasValue ? Math.Round(x.Shares * price.Value, 2) : null;
            decimal? profit = value.HasValue ? value.Value - x.CostAmount : null;
            decimal? profitRate = profit.HasValue && x.CostAmount > 0 ? Math.Round(profit.Value / x.CostAmount * 100, 2) : null;
            var quoteSource = useHoldingLast ? "holding_last" : quote?.QuoteSource ?? "missing";
            var quoteOk = quote?.QuoteOk == true && !useHoldingLast;
            var isFallback = useHoldingLast || quote?.IsFallback == true;
            var isStale = useHoldingLast || quote?.IsStale == true;

            return new
            {
                x.Id,
                code = x.StockCode,
                market = quote?.Market ?? x.Market,
                name = !string.IsNullOrWhiteSpace(quote?.Name) && quote!.Name != quote.Code ? quote.Name : x.StockName,
                x.Shares,
                x.CostPrice,
                x.CostAmount,
                price,
                changeAmount = useHoldingLast ? null : quote?.ChangeAmount,
                changeRate = useHoldingLast ? x.LastRate : quote?.ChangeRate,
                marketValue = value,
                totalProfit = profit,
                totalProfitRate = profitRate,
                quoteOk,
                quoteSource,
                isFallback,
                isStale,
                quoteTime = useHoldingLast ? null : quote?.QuoteTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                errorMessage = useHoldingLast ? "实时行情缺失，使用持仓上次价格" : quote?.ErrorMessage
            };
        }

        private static object EnrichWatch(StockWatchItem x, StockQuoteDto? quote)
        {
            var hasDisplayQuote = quote != null && quote.QuoteSource != "missing" && quote.Price.HasValue;
            return new
            {
                x.Id,
                code = x.StockCode,
                market = quote?.Market ?? x.Market,
                name = quote?.Name ?? x.StockName,
                price = hasDisplayQuote ? quote!.Price : null,
                changeAmount = hasDisplayQuote ? quote!.ChangeAmount : null,
                changeRate = hasDisplayQuote ? quote!.ChangeRate : null,
                quoteOk = quote?.QuoteOk == true,
                quoteSource = quote?.QuoteSource ?? "missing",
                isFallback = quote?.IsFallback == true,
                isStale = quote?.IsStale == true,
                quoteTime = quote?.QuoteTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                errorMessage = quote?.ErrorMessage
            };
        }

        private static bool IsRealtimeQuoteSource(string? source)
            => source is "eastmoney" or "tencent" or "sina";

        private static StockOcrTextBlock? ToStockOcrTextBlock(JToken? token)
        {
            var text = token?["words"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return null;

            var loc = token?["location"];

            return new StockOcrTextBlock(
                text,
                ToInt(loc?["left"]),
                ToInt(loc?["top"]),
                ToInt(loc?["width"]),
                ToInt(loc?["height"]));
        }

        private static int ToInt(JToken? token)
        {
            return int.TryParse(token?.ToString(), out var value) ? value : 0;
        }

        private static string? NormalizeUser(string username)
        {
            username = (username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }

        private static string? NormalizeMarket(string? market)
        {
            market = (market ?? string.Empty).Trim().ToUpperInvariant();
            return market switch
            {
                "HK" or "HKG" or "116" or "港股" => "HK",
                "SH" or "SHA" or "1" or "沪市" => "SH",
                "SZ" or "SZA" or "0" or "深市" => "SZ",
                "BJ" or "BSE" or "北交所" => "BJ",
                _ => string.IsNullOrWhiteSpace(market) ? null : market
            };
        }

        private static bool LooksLikeHongKongName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            name = name.Trim();
            return name.EndsWith("-W", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("-SW", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("-B", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("集团-W", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("集团-SW", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("控股-W", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("港股", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCode(string code, string? market = null, string? name = null)
        {
            var text = (code ?? string.Empty).Trim().ToUpperInvariant();

            var secIdMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?<!\d)116\.(\d{5})(?!\d)");
            if (secIdMatch.Success) return secIdMatch.Groups[1].Value;

            text = text
                .Replace(".HK", "")
                .Replace("HK", "")
                .Replace("SH", "")
                .Replace("SZ", "")
                .Replace("BJ", "");

            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return string.Empty;

            market = NormalizeMarket(market);

            if (market == "HK" || LooksLikeHongKongName(name))
            {
                if (digits.Length == 6 && digits.StartsWith("0"))
                {
                    digits = digits[1..];
                }

                if (digits.Length > 5)
                {
                    digits = digits[^5..];
                }

                return digits.PadLeft(5, '0');
            }

            if (digits.Length == 5)
            {
                return digits;
            }

            if (digits.Length > 6)
            {
                digits = digits[^6..];
            }

            return digits.PadLeft(6, '0');
        }

        private static string NormalizeStockName(string? name)
        {
            return (name ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .Replace("Ａ", "A")
                .Replace("Ｂ", "B")
                .Replace("Ｃ", "C")
                .Replace("Ｄ", "D")
                .Replace("（", "(")
                .Replace("）", ")")
                .Trim();
        }
    }

    public record SaveStockHoldingRequest(
        string Username,
        string StockCode,
        string? StockName,
        decimal Shares,
        decimal CostPrice,
        decimal CostAmount,
        string? Market = null);

    public record SaveStockWatchRequest(
        string Username,
        string StockCode,
        string? StockName,
        string? Market = null);

    public record ConfirmStockOcrRequest(
        string Username,
        int BatchId,
        List<ConfirmStockOcrItem>? Items);

    public record ConfirmStockOcrItem(
        int Id,
        string StockCode,
        string? StockName,
        string? Action,
        decimal? Shares,
        decimal? CostPrice,
        decimal? CostAmount,
        string? Market = null);
}
