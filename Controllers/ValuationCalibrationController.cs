using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using 小白养基.Models;

namespace 小白养基.Controllers
{
    [ApiController]
    [Route("api/fund/valuation-calibration")]
    public sealed class ValuationCalibrationController : ControllerBase
    {
        private const int MaxSampleWindow = 8;
        private readonly AppDbContext _context;

        public ValuationCalibrationController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("current")]
        public async Task<IActionResult> GetCurrent([FromQuery] string username, [FromQuery] string? fundCode = null)
        {
            var user = NormalizeUsername(username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var query = _context.FundValuationCalibrations
                .AsNoTracking()
                .Where(x => x.Username == user);

            if (!string.IsNullOrWhiteSpace(fundCode))
            {
                var code = fundCode.Trim();
                query = query.Where(x => x.FundCode == code);
            }

            var rows = await query
                .OrderByDescending(x => x.TradeDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            var profiles = rows
                .GroupBy(x => x.FundCode)
                .Select(group => BuildProfile(group.Take(MaxSampleWindow).OrderBy(x => x.TradeDate).ToList()))
                .OrderByDescending(x => Math.Abs(x.Offset))
                .ToList();

            return Ok(new
            {
                success = true,
                username = user,
                generatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                items = profiles
            });
        }

        [HttpGet("samples")]
        public async Task<IActionResult> GetSamples([FromQuery] string username, [FromQuery] string fundCode, [FromQuery] int limit = 30)
        {
            var user = NormalizeUsername(username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var code = (fundCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code)) return BadRequest(new { success = false, message = "请提供基金代码" });

            var rows = await _context.FundValuationCalibrations
                .AsNoTracking()
                .Where(x => x.Username == user && x.FundCode == code)
                .OrderByDescending(x => x.TradeDate)
                .ThenByDescending(x => x.Id)
                .Take(Math.Clamp(limit, 1, 120))
                .Select(x => new
                {
                    x.FundCode,
                    x.FundName,
                    tradeDate = x.TradeDate.ToString("yyyy-MM-dd"),
                    x.EstimatedRate,
                    x.ActualRate,
                    x.ErrorRate,
                    x.CorrectionOffset,
                    x.SampleCount,
                    x.Confidence,
                    updatedAt = x.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            return Ok(new { success = true, items = rows });
        }

        [HttpPost("intraday-snapshot")]
        public async Task<IActionResult> SaveIntradaySnapshot([FromBody] IntradaySnapshotRequest request)
        {
            var user = NormalizeUsername(request.Username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var items = request.Items ?? new List<ValuationEstimateDto>();
            if (items.Count == 0) return Ok(new { success = true, saved = 0 });

            var now = ChinaNow();
            var saved = 0;

            foreach (var item in items.Take(200))
            {
                var code = NormalizeCode(item.FundCode);
                if (code == null || !IsFinite(item.EstimatedRate)) continue;

                var tradeDate = ParseDate(item.TradeDate) ?? now.Date;
                var estimate = await _context.FundValuationEstimates
                    .FirstOrDefaultAsync(x => x.Username == user && x.FundCode == code && x.TradeDate == tradeDate);

                if (estimate == null)
                {
                    estimate = new FundValuationEstimate
                    {
                        Username = user,
                        FundCode = code,
                        TradeDate = tradeDate
                    };
                    _context.FundValuationEstimates.Add(estimate);
                }

                estimate.FundName = SafeText(item.FundName, 160) ?? estimate.FundName ?? code;
                estimate.EstimatedRate = Round4(item.EstimatedRate);
                estimate.EstimatedAssets = Round2(item.EstimatedAssets);
                estimate.Source = SafeText(item.Source, 40) ?? "intraday";
                estimate.EstimatedAt = now;
                estimate.RawPayloadJson = item.RawPayload is null ? null : item.RawPayload.Value.GetRawText();
                saved++;
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, saved });
        }

        [HttpPost("settle")]
        public async Task<IActionResult> Settle([FromBody] ValuationSettleRequest request)
        {
            var user = NormalizeUsername(request.Username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var items = request.Items ?? new List<ValuationSettleDto>();
            if (items.Count == 0) return Ok(new { success = true, settled = 0, items = Array.Empty<object>() });

            var now = ChinaNow();
            var results = new List<object>();
            var settled = 0;

            foreach (var item in items.Take(200))
            {
                var code = NormalizeCode(item.FundCode);
                if (code == null || !IsFinite(item.ActualRate)) continue;

                var tradeDate = ParseDate(item.TradeDate) ?? now.Date;
                var estimate = await _context.FundValuationEstimates
                    .AsNoTracking()
                    .Where(x => x.Username == user && x.FundCode == code && x.TradeDate == tradeDate)
                    .OrderByDescending(x => x.EstimatedAt)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync();

                var estimatedRate = estimate?.EstimatedRate ?? item.EstimatedRate;
                if (!IsFinite(estimatedRate)) continue;

                var error = Round4(item.ActualRate - estimatedRate);
                var row = await _context.FundValuationCalibrations
                    .FirstOrDefaultAsync(x => x.Username == user && x.FundCode == code && x.TradeDate == tradeDate);

                if (row == null)
                {
                    row = new FundValuationCalibration
                    {
                        Username = user,
                        FundCode = code,
                        TradeDate = tradeDate,
                        CreatedAt = now
                    };
                    _context.FundValuationCalibrations.Add(row);
                }

                row.FundName = SafeText(item.FundName, 160) ?? estimate?.FundName ?? row.FundName ?? code;
                row.EstimatedRate = Round4(estimatedRate);
                row.ActualRate = Round4(item.ActualRate);
                row.ErrorRate = error;
                row.EstimatedAssets = Round2(estimate?.EstimatedAssets ?? item.EstimatedAssets);
                row.ActualAssets = Round2(item.ActualAssets);
                row.UpdatedAt = now;

                await _context.SaveChangesAsync();

                var profile = await BuildProfileAsync(user, code);
                row.CorrectionOffset = profile.Offset;
                row.SampleCount = profile.Samples;
                row.Confidence = profile.Confidence;
                row.UpdatedAt = now;
                await _context.SaveChangesAsync();

                settled++;
                results.Add(new
                {
                    code,
                    row.FundName,
                    tradeDate = tradeDate.ToString("yyyy-MM-dd"),
                    row.EstimatedRate,
                    row.ActualRate,
                    row.ErrorRate,
                    row.CorrectionOffset,
                    row.SampleCount,
                    row.Confidence
                });
            }

            return Ok(new { success = true, settled, items = results });
        }

        [HttpDelete("clear")]
        public async Task<IActionResult> Clear([FromQuery] string username, [FromQuery] string? fundCode = null)
        {
            var user = NormalizeUsername(username);
            if (user == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var estimates = _context.FundValuationEstimates.Where(x => x.Username == user);
            var calibrations = _context.FundValuationCalibrations.Where(x => x.Username == user);
            if (!string.IsNullOrWhiteSpace(fundCode))
            {
                var code = fundCode.Trim();
                estimates = estimates.Where(x => x.FundCode == code);
                calibrations = calibrations.Where(x => x.FundCode == code);
            }

            _context.FundValuationEstimates.RemoveRange(estimates);
            _context.FundValuationCalibrations.RemoveRange(calibrations);
            var deleted = await _context.SaveChangesAsync();
            return Ok(new { success = true, deleted });
        }

        private async Task<CalibrationProfileDto> BuildProfileAsync(string username, string fundCode)
        {
            var rows = await _context.FundValuationCalibrations
                .AsNoTracking()
                .Where(x => x.Username == username && x.FundCode == fundCode)
                .OrderByDescending(x => x.TradeDate)
                .ThenByDescending(x => x.Id)
                .Take(MaxSampleWindow)
                .ToListAsync();

            return BuildProfile(rows.OrderBy(x => x.TradeDate).ToList());
        }

        private static CalibrationProfileDto BuildProfile(List<FundValuationCalibration> samples)
        {
            if (samples.Count == 0)
            {
                return new CalibrationProfileDto(string.Empty, string.Empty, 0, 0, 0, "低", string.Empty);
            }

            double weightedSum = 0;
            double weight = 0;
            for (var i = 0; i < samples.Count; i++)
            {
                var w = i + 1;
                weightedSum += samples[i].ErrorRate * w;
                weight += w;
            }

            var offset = weight > 0 ? Math.Clamp(weightedSum / weight, -3, 3) : 0;
            var last = samples[^1];
            var confidence = samples.Count >= 5 ? "高" : samples.Count >= 3 ? "中" : "低";
            var note = $"最近{samples.Count}天样本 · 最近误差 {(last.ErrorRate >= 0 ? "+" : string.Empty)}{Round2(last.ErrorRate):0.##}%";

            return new CalibrationProfileDto(
                last.FundCode,
                last.FundName,
                Round4(offset),
                samples.Count,
                Round4(last.ErrorRate),
                confidence,
                note);
        }

        private static string? NormalizeUsername(string? username)
        {
            var value = (username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? NormalizeCode(string? code)
        {
            var value = (code ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string? SafeText(string? value, int maxLength)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return text.Length <= maxLength ? text : text[..maxLength];
        }

        private static DateTime? ParseDate(string? value)
        {
            if (DateTime.TryParse(value, out var date)) return date.Date;
            return null;
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
        private static double Round4(double value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

        public sealed class IntradaySnapshotRequest
        {
            public string Username { get; set; } = string.Empty;
            public List<ValuationEstimateDto> Items { get; set; } = new();
        }

        public sealed class ValuationEstimateDto
        {
            public string FundCode { get; set; } = string.Empty;
            public string FundName { get; set; } = string.Empty;
            public string? TradeDate { get; set; }
            public double EstimatedRate { get; set; }
            public double EstimatedAssets { get; set; }
            public string? Source { get; set; }
            public JsonElement? RawPayload { get; set; }
        }

        public sealed class ValuationSettleRequest
        {
            public string Username { get; set; } = string.Empty;
            public List<ValuationSettleDto> Items { get; set; } = new();
        }

        public sealed class ValuationSettleDto
        {
            public string FundCode { get; set; } = string.Empty;
            public string FundName { get; set; } = string.Empty;
            public string? TradeDate { get; set; }
            public double EstimatedRate { get; set; } = double.NaN;
            public double ActualRate { get; set; }
            public double EstimatedAssets { get; set; }
            public double ActualAssets { get; set; }
        }

        private sealed record CalibrationProfileDto(
            string Code,
            string Name,
            double Offset,
            int Samples,
            double LastError,
            string Confidence,
            string Note);
    }
}
