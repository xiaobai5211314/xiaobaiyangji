using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using 估值助手.Models;
using 估值助手.Services;

namespace 估值助手.Controllers
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

        public StockController(AppDbContext context, IStockQuoteService quotes, IBaiduOcrService ocr, StockOcrParserService parser, ILogger<StockController> logger)
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
                .OrderBy(x => x.StockCode)
                .ToListAsync(cancellationToken);
            var watch = await _context.StockWatchItems.AsNoTracking()
                .Where(x => x.Username == username)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.StockCode)
                .ToListAsync(cancellationToken);

            var codes = holdings.Select(x => x.StockCode).Concat(watch.Select(x => x.StockCode)).Distinct().ToList();
            var quoteMap = (await _quotes.GetQuotesAsync(codes, cancellationToken)).ToDictionary(x => x.Code, x => x);

            return Ok(new
            {
                success = true,
                username,
                holdings = holdings.Select(x => EnrichHolding(x, quoteMap.GetValueOrDefault(x.StockCode))).ToList(),
                watchList = watch.Select(x => EnrichWatch(x, quoteMap.GetValueOrDefault(x.StockCode))).ToList(),
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
        public async Task<IActionResult> KLines([FromQuery] string code, [FromQuery] string period = "day", CancellationToken cancellationToken = default)
        {
            var rows = await _quotes.GetKLinesAsync(code, period, cancellationToken);
            return Ok(new { success = true, code = NormalizeCode(code), period, items = rows });
        }

        [HttpPost("holding")]
        public async Task<IActionResult> SaveHolding([FromBody] SaveStockHoldingRequest request, CancellationToken cancellationToken)
        {
            var username = NormalizeUser(request.Username);
            var code = NormalizeCode(request.StockCode);
            if (username == null || string.IsNullOrWhiteSpace(code)) return BadRequest(new { success = false, message = "用户名或股票代码为空" });

            var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
            var name = !string.IsNullOrWhiteSpace(request.StockName) ? request.StockName.Trim() : (quote?.Name ?? code);
            var row = await _context.StockHoldings.FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);
            if (row == null)
            {
                row = new StockHolding { Username = username, StockCode = code, CreatedAt = DateTime.Now };
                _context.StockHoldings.Add(row);
            }

            row.Market = quote?.Market ?? _quotes.InferMarket(code);
            row.StockName = name;
            row.Shares = Math.Max(0, request.Shares);
            row.CostPrice = Math.Max(0, request.CostPrice);
            row.CostAmount = request.CostAmount > 0 ? request.CostAmount : Math.Round(row.Shares * row.CostPrice, 2);
            row.UpdatedAt = DateTime.Now;
            ApplyQuote(row, quote);

            await _context.SaveChangesAsync(cancellationToken);
            await UpsertWatchAsync(username, code, row.Market, row.StockName, cancellationToken);
            return Ok(new { success = true, item = EnrichHolding(row, quote) });
        }

        [HttpDelete("holding")]
        public async Task<IActionResult> DeleteHolding([FromQuery] string username, [FromQuery] string code, CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);
            code = NormalizeCode(code);
            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });
            var row = await _context.StockHoldings.FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);
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
            var username = NormalizeUser(request.Username);
            var code = NormalizeCode(request.StockCode);
            if (username == null || string.IsNullOrWhiteSpace(code)) return BadRequest(new { success = false, message = "用户名或股票代码为空" });
            var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
            var name = !string.IsNullOrWhiteSpace(request.StockName) ? request.StockName.Trim() : (quote?.Name ?? code);
            await UpsertWatchAsync(username, code, quote?.Market ?? _quotes.InferMarket(code), name, cancellationToken);
            return Ok(new { success = true });
        }

        [HttpDelete("watch")]
        public async Task<IActionResult> DeleteWatch([FromQuery] string username, [FromQuery] string code, CancellationToken cancellationToken)
        {
            username = NormalizeUser(username);
            code = NormalizeCode(code);
            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });
            var row = await _context.StockWatchItems.FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);
            if (row != null)
            {
                _context.StockWatchItems.Remove(row);
                await _context.SaveChangesAsync(cancellationToken);
            }
            return Ok(new { success = true });
        }

        [HttpPost("import-ocr-preview")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> ImportOcrPreview([FromForm] string username, [FromForm] IFormFile image, CancellationToken cancellationToken)
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
            var words = blocks.Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            var candidates = blocks.Any(x => x.Width > 0 && x.Height > 0)
                ? _parser.Parse(blocks)
                : _parser.Parse(words);
            var diagnostics = new List<string>
            {
                $"OCR 文本行数：{words.Count}",
                $"OCR 坐标行数：{blocks.Count(x => x.Width > 0 && x.Height > 0)}",
                $"识别候选：{candidates.Count}",
                "股票持仓 OCR：优先使用 location 坐标按表格列解析市值、盈亏、持仓、成本价。"
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
                var code = NormalizeCode(c.StockCode);
                // 预览阶段不逐条拉行情，避免 OCR 成功后被外部行情接口拖慢；确认写库时再补行情和证券简称。
                batch.Items.Add(new StockOcrImportItem
                {
                    BatchId = batch.Id,
                    StockCode = code,
                    Market = string.IsNullOrWhiteSpace(code) ? string.Empty : _quotes.InferMarket(code),
                    StockName = !string.IsNullOrWhiteSpace(c.StockName) ? c.StockName : c.RecognizedName,
                    RecognizedName = c.RecognizedName,
                    Shares = c.Shares,
                    CostPrice = c.CostPrice,
                    CostAmount = c.CostAmount,
                    MarketValue = c.MarketValue,
                    FloatingProfit = c.FloatingProfit,
                    FloatingProfitRate = c.FloatingProfitRate,
                    Action = c.Action,
                    Note = c.Note
                });
            }
            await _context.SaveChangesAsync(cancellationToken);

            var items = await _context.StockOcrImportItems.AsNoTracking().Where(x => x.BatchId == batch.Id).ToListAsync(cancellationToken);
            return Ok(new { success = true, batchId = batch.Id, count = items.Count, items, diagnostics });
        }

        [HttpPost("import-ocr-confirm")]
        public async Task<IActionResult> ConfirmOcr([FromBody] ConfirmStockOcrRequest request, CancellationToken cancellationToken)
        {
            var username = NormalizeUser(request.Username);
            if (username == null) return BadRequest(new { success = false, message = "请提供用户名" });
            var batch = await _context.StockOcrImportBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == request.BatchId && x.Username == username, cancellationToken);
            if (batch == null) return NotFound(new { success = false, message = "未找到 OCR 预览批次" });

            var selected = request.Items?.Count > 0 ? request.Items : batch.Items.Select(x => new ConfirmStockOcrItem(x.Id, x.StockCode, x.StockName, x.Action, x.Shares, x.CostPrice, x.CostAmount)).ToList();
            var saved = 0;
            foreach (var item in selected)
            {
                var code = NormalizeCode(item.StockCode);
                if (string.IsNullOrWhiteSpace(code)) continue;
                var quote = await _quotes.GetQuoteAsync(code, cancellationToken);
                var action = (item.Action ?? "holding").Trim().ToLowerInvariant();
                var name = !string.IsNullOrWhiteSpace(item.StockName) ? item.StockName.Trim() : quote?.Name ?? code;
                if (action == "watch")
                {
                    await UpsertWatchAsync(username, code, quote?.Market ?? _quotes.InferMarket(code), name, cancellationToken);
                    saved++;
                    continue;
                }

                var row = await _context.StockHoldings.FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);
                if (row == null)
                {
                    row = new StockHolding { Username = username, StockCode = code, CreatedAt = DateTime.Now };
                    _context.StockHoldings.Add(row);
                }
                row.StockName = name;
                row.Market = quote?.Market ?? _quotes.InferMarket(code);
                row.Shares = Math.Max(0, item.Shares ?? 0);
                row.CostPrice = Math.Max(0, item.CostPrice ?? 0);
                row.CostAmount = item.CostAmount.GetValueOrDefault() > 0 ? item.CostAmount!.Value : Math.Round(row.Shares * row.CostPrice, 2);
                row.UpdatedAt = DateTime.Now;
                ApplyQuote(row, quote);
                await UpsertWatchAsync(username, code, row.Market, row.StockName, cancellationToken);
                saved++;
            }

            batch.Status = "confirmed";
            batch.ConfirmedAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
            return Ok(new { success = true, saved });
        }

        private async Task UpsertWatchAsync(string username, string code, string market, string name, CancellationToken cancellationToken)
        {
            var watch = await _context.StockWatchItems.FirstOrDefaultAsync(x => x.Username == username && x.StockCode == code, cancellationToken);
            if (watch == null)
            {
                var maxSort = await _context.StockWatchItems.Where(x => x.Username == username).Select(x => (int?)x.SortOrder).MaxAsync(cancellationToken) ?? 0;
                watch = new StockWatchItem { Username = username, StockCode = code, CreatedAt = DateTime.Now, SortOrder = maxSort + 1 };
                _context.StockWatchItems.Add(watch);
            }
            watch.Market = market;
            watch.StockName = name;
            watch.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
        }

        private static void ApplyQuote(StockHolding row, StockQuoteDto? quote)
        {
            if (quote == null) return;
            row.LastPrice = quote.Price;
            row.LastRate = quote.ChangeRate;
            row.LastMarketValue = Math.Round(row.Shares * quote.Price, 2);
            row.LastProfit = row.LastMarketValue - row.CostAmount;
            row.LastProfitRate = row.CostAmount > 0 ? Math.Round(row.LastProfit.GetValueOrDefault() / row.CostAmount * 100, 2) : 0;
        }

        private static object EnrichHolding(StockHolding x, StockQuoteDto? quote)
        {
            var price = quote?.Price ?? x.LastPrice ?? 0;
            var value = Math.Round(x.Shares * price, 2);
            var profit = value - x.CostAmount;
            var profitRate = x.CostAmount > 0 ? Math.Round(profit / x.CostAmount * 100, 2) : 0;
            return new
            {
                x.Id,
                code = x.StockCode,
                market = quote?.Market ?? x.Market,
                name = quote?.Name ?? x.StockName,
                x.Shares,
                x.CostPrice,
                x.CostAmount,
                price,
                changeAmount = quote?.ChangeAmount ?? 0,
                changeRate = quote?.ChangeRate ?? x.LastRate ?? 0,
                marketValue = value,
                totalProfit = profit,
                totalProfitRate = profitRate,
                quoteTime = quote?.QuoteTime.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static object EnrichWatch(StockWatchItem x, StockQuoteDto? quote)
        {
            return new
            {
                x.Id,
                code = x.StockCode,
                market = quote?.Market ?? x.Market,
                name = quote?.Name ?? x.StockName,
                price = quote?.Price ?? 0,
                changeAmount = quote?.ChangeAmount ?? 0,
                changeRate = quote?.ChangeRate ?? 0,
                quoteTime = quote?.QuoteTime.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }


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

        private static string NormalizeCode(string code)
        {
            var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length > 6) digits = digits[^6..];
            return digits.Length == 0 ? string.Empty : digits.PadLeft(6, '0');
        }
    }

    public record SaveStockHoldingRequest(string Username, string StockCode, string? StockName, decimal Shares, decimal CostPrice, decimal CostAmount);
    public record SaveStockWatchRequest(string Username, string StockCode, string? StockName);
    public record ConfirmStockOcrRequest(string Username, int BatchId, List<ConfirmStockOcrItem>? Items);
    public record ConfirmStockOcrItem(int Id, string StockCode, string? StockName, string? Action, decimal? Shares, decimal? CostPrice, decimal? CostAmount);
}
