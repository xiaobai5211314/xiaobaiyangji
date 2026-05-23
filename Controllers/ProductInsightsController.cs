using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using 小白养基.Models;

namespace 小白养基.Controllers
{
    [ApiController]
    [Route("api/fund/insights")]
    public sealed class ProductInsightsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductInsightsController(AppDbContext context)
        {
            _context = context;
        }

        private static DateTime ChinaToday() => DateTime.UtcNow.AddHours(8).Date;

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard([FromQuery] string username, [FromQuery] string? date = null)
        {
            var normalizedUser = NormalizeUsername(username);
            if (normalizedUser == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var funds = await LoadFundsAsync(normalizedUser);
            var archiveDate = await ResolveArchiveDateAsync(normalizedUser, date);
            var rows = await LoadArchiveRowsAsync(normalizedUser, archiveDate, funds);

            return Ok(new
            {
                success = true,
                username = normalizedUser,
                date = archiveDate.ToString("yyyy-MM-dd"),
                generatedAt = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd HH:mm:ss"),
                dailyReport = BuildDailyReport(funds, rows, archiveDate),
                exposure = BuildExposure(funds, rows),
                recovery = BuildRecoverySummary(funds, rows),
                confidence = BuildConfidenceRadar(funds, rows),
                ocrLearning = await BuildOcrLearningSummaryAsync(normalizedUser)
            });
        }

        [HttpGet("daily-report")]
        public async Task<IActionResult> GetDailyReport([FromQuery] string username, [FromQuery] string? date = null)
        {
            var normalizedUser = NormalizeUsername(username);
            if (normalizedUser == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var funds = await LoadFundsAsync(normalizedUser);
            var archiveDate = await ResolveArchiveDateAsync(normalizedUser, date);
            var rows = await LoadArchiveRowsAsync(normalizedUser, archiveDate, funds);

            return Ok(new { success = true, date = archiveDate.ToString("yyyy-MM-dd"), report = BuildDailyReport(funds, rows, archiveDate) });
        }

        [HttpGet("exposure")]
        public async Task<IActionResult> GetExposure([FromQuery] string username, [FromQuery] string? date = null)
        {
            var normalizedUser = NormalizeUsername(username);
            if (normalizedUser == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var funds = await LoadFundsAsync(normalizedUser);
            var archiveDate = await ResolveArchiveDateAsync(normalizedUser, date);
            var rows = await LoadArchiveRowsAsync(normalizedUser, archiveDate, funds);

            return Ok(new { success = true, date = archiveDate.ToString("yyyy-MM-dd"), exposure = BuildExposure(funds, rows) });
        }

        [HttpGet("recovery-plan")]
        public async Task<IActionResult> GetRecoveryPlan([FromQuery] string username, [FromQuery] string? fundCode = null, [FromQuery] string? date = null)
        {
            var normalizedUser = NormalizeUsername(username);
            if (normalizedUser == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            var funds = await LoadFundsAsync(normalizedUser);
            if (!string.IsNullOrWhiteSpace(fundCode))
            {
                funds = funds.Where(x => x.FundCode == fundCode.Trim()).ToList();
            }

            var archiveDate = await ResolveArchiveDateAsync(normalizedUser, date);
            var rows = await LoadArchiveRowsAsync(normalizedUser, archiveDate, funds);

            return Ok(new { success = true, date = archiveDate.ToString("yyyy-MM-dd"), recovery = BuildRecoverySummary(funds, rows) });
        }

        [HttpGet("ocr-learning")]
        public async Task<IActionResult> GetOcrLearning([FromQuery] string username)
        {
            var normalizedUser = NormalizeUsername(username);
            if (normalizedUser == null) return Unauthorized(new { success = false, message = "请提供用户名" });

            return Ok(new { success = true, ocrLearning = await BuildOcrLearningSummaryAsync(normalizedUser) });
        }

        private static string? NormalizeUsername(string username)
        {
            var value = (username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private async Task<List<MyFundConfig>> LoadFundsAsync(string username)
        {
            return await _context.MyFunds
                .AsNoTracking()
                .Where(x => x.Username == username)
                .OrderByDescending(x => x.HoldAmount)
                .ToListAsync();
        }

        private async Task<DateTime> ResolveArchiveDateAsync(string username, string? date)
        {
            if (DateTime.TryParse(date, out var parsed)) return parsed.Date;

            var latest = await _context.DailyArchives
                .AsNoTracking()
                .Where(x => x.Username == username)
                .MaxAsync(x => (DateTime?)x.RecordDate);

            return latest?.Date ?? ChinaToday();
        }

        private async Task<List<InsightFundRow>> LoadArchiveRowsAsync(string username, DateTime date, List<MyFundConfig> funds)
        {
            var start = date.Date;
            var end = start.AddDays(1);
            var archives = await _context.DailyArchives
                .AsNoTracking()
                .Where(x => x.Username == username && x.RecordDate >= start && x.RecordDate < end)
                .ToListAsync();

            var archiveByCode = archives
                .Where(x => x.FundCode != "TOTAL")
                .GroupBy(x => x.FundCode)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.Id).First());

            var rows = new List<InsightFundRow>();
            foreach (var fund in funds)
            {
                archiveByCode.TryGetValue(fund.FundCode, out var archive);
                var assets = archive?.Assets ?? fund.HoldAmount;
                var cost = fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount;
                var dailyProfit = archive?.DailyProfit ?? fund.LastSettledProfit;
                var dailyRate = archive?.DailyRate ?? fund.LastSettledRate;
                var totalProfit = archive?.TotalProfit ?? Math.Round(assets - cost + fund.RealizedProfit, 2);
                var totalRate = archive?.TotalRate ?? (cost > 0 ? Math.Round(totalProfit / cost * 100, 2) : 0);

                rows.Add(new InsightFundRow
                {
                    Code = fund.FundCode,
                    Name = fund.FundName,
                    Assets = Math.Round(assets, 2),
                    Cost = Math.Round(cost, 2),
                    HoldAmount = Math.Round(fund.HoldAmount, 2),
                    HoldShares = Math.Round(fund.HoldShares, 2),
                    DailyProfit = Math.Round(dailyProfit, 2),
                    DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalRate = Math.Round(totalRate, 2),
                    RealizedProfit = Math.Round(fund.RealizedProfit, 2),
                    LastTradeDate = fund.LastTradeDate,
                    LastAddAmount = Math.Round(fund.LastAddAmount, 2),
                    LastSettledDate = fund.LastSettledDate,
                    LastSettledRate = Math.Round(fund.LastSettledRate, 2),
                    Theme = ClassifyTheme(fund.FundName),
                    Confidence = ScoreConfidence(fund, archive, date)
                });
            }

            return rows.OrderByDescending(x => x.Assets).ToList();
        }

        private static object BuildDailyReport(List<MyFundConfig> funds, List<InsightFundRow> rows, DateTime date)
        {
            var totalAssets = Math.Round(rows.Sum(x => x.Assets), 2);
            var dailyProfit = Math.Round(rows.Sum(x => x.DailyProfit), 2);
            var dailyBase = rows.Sum(x => Math.Max(0, x.Assets - x.DailyProfit));
            var dailyRate = dailyBase > 0 ? Math.Round(dailyProfit / dailyBase * 100, 2) : 0;
            var topContribution = rows.OrderByDescending(x => x.DailyProfit).FirstOrDefault();
            var biggestDrag = rows.OrderBy(x => x.DailyProfit).FirstOrDefault();
            var mainTheme = BuildExposure(funds, rows).Themes.FirstOrDefault();

            var suggestion = dailyProfit > 0
                ? "当日组合为正收益，优先复盘贡献基金是否由单一赛道驱动。"
                : dailyProfit < 0
                    ? "当日组合为负收益，优先检查高集中赛道和亏损扩大基金。"
                    : "当日收益接近持平，重点观察净值确认和持仓集中度。";

            return new
            {
                date = date.ToString("yyyy-MM-dd"),
                fundCount = rows.Count,
                totalAssets,
                dailyProfit,
                dailyRate,
                totalProfit = Math.Round(rows.Sum(x => x.TotalProfit), 2),
                topContribution,
                biggestDrag,
                mainTheme,
                suggestion
            };
        }

        private static ExposureResult BuildExposure(List<MyFundConfig> funds, List<InsightFundRow> rows)
        {
            var totalAssets = rows.Sum(x => Math.Max(0, x.Assets));
            var themes = rows
                .GroupBy(x => x.Theme)
                .Select(g => new ThemeExposure
                {
                    Theme = g.Key,
                    FundCount = g.Count(),
                    Assets = Math.Round(g.Sum(x => x.Assets), 2),
                    Weight = totalAssets > 0 ? Math.Round(g.Sum(x => x.Assets) / totalAssets * 100, 2) : 0,
                    DailyProfit = Math.Round(g.Sum(x => x.DailyProfit), 2),
                    Funds = g.OrderByDescending(x => x.Assets).Select(x => new SimpleFund(x.Code, x.Name, x.Assets, x.DailyProfit)).ToList()
                })
                .OrderByDescending(x => x.Weight)
                .ToList();

            var concentration = themes.Count == 0 ? 0 : Math.Round(themes.Take(3).Sum(x => x.Weight), 2);
            var riskLevel = concentration >= 75 ? "高集中" : concentration >= 55 ? "中集中" : "分散";

            return new ExposureResult
            {
                TotalAssets = Math.Round(totalAssets, 2),
                Top3Concentration = concentration,
                RiskLevel = riskLevel,
                Themes = themes
            };
        }

        private static object BuildRecoverySummary(List<MyFundConfig> funds, List<InsightFundRow> rows)
        {
            var lossRows = rows
                .Where(x => x.TotalProfit < 0 && x.Assets > 0)
                .Select(x =>
                {
                    var recoveryRate = Math.Round((-x.TotalProfit) / x.Assets * 100, 2);
                    var scenarios = new[] { 100, 300, 500, 1000, 2000 }
                        .Select(amount => new
                        {
                            addAmount = amount,
                            newRecoveryRate = Math.Round((-x.TotalProfit) / (x.Assets + amount) * 100, 2),
                            change = Math.Round(recoveryRate - ((-x.TotalProfit) / (x.Assets + amount) * 100), 2)
                        })
                        .ToList();

                    return new
                    {
                        x.Code,
                        x.Name,
                        x.Assets,
                        x.Cost,
                        x.TotalProfit,
                        x.TotalRate,
                        recoveryRate,
                        scenarios
                    };
                })
                .OrderByDescending(x => x.recoveryRate)
                .ToList();

            return new
            {
                lossCount = lossRows.Count,
                averageRecoveryRate = lossRows.Count == 0 ? 0 : Math.Round(lossRows.Average(x => x.recoveryRate), 2),
                items = lossRows
            };
        }

        private static object BuildConfidenceRadar(List<MyFundConfig> funds, List<InsightFundRow> rows)
        {
            var items = rows
                .Select(x => new
                {
                    x.Code,
                    x.Name,
                    score = x.Confidence.Score,
                    level = x.Confidence.Level,
                    reasons = x.Confidence.Reasons
                })
                .OrderBy(x => x.score)
                .ToList();

            return new
            {
                averageScore = items.Count == 0 ? 0 : Math.Round(items.Average(x => x.score), 2),
                lowConfidenceCount = items.Count(x => x.score < 70),
                items
            };
        }

        private async Task<object> BuildOcrLearningSummaryAsync(string username)
        {
            var corrections = await _context.OcrCorrections
                .AsNoTracking()
                .Where(x => x.Username == username)
                .OrderByDescending(x => x.UpdatedAt)
                .Take(30)
                .ToListAsync();

            return new
            {
                count = corrections.Count,
                recent = corrections.Select(x => new
                {
                    x.OcrName,
                    x.FundCode,
                    x.FundName,
                    updatedAt = x.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
            };
        }

        private static ConfidenceResult ScoreConfidence(MyFundConfig fund, DailyArchive? archive, DateTime archiveDate)
        {
            var score = 100;
            var reasons = new List<string>();

            if (archive == null)
            {
                score -= 20;
                reasons.Add("缺少当日封存档案");
            }

            if (!string.IsNullOrWhiteSpace(fund.LastTradeDate) && DateTime.TryParse(fund.LastTradeDate, out var tradeDate))
            {
                if ((archiveDate.Date - tradeDate.Date).TotalDays <= 1 && Math.Abs(fund.LastAddAmount) > 0.01)
                {
                    score -= 10;
                    reasons.Add("存在近期开仓/加减仓，收益口径可能偏移");
                }
            }

            if (fund.HoldShares <= 0)
            {
                score -= 15;
                reasons.Add("缺少份额，净值校准能力较弱");
            }

            if (fund.CostAmount <= 0)
            {
                score -= 10;
                reasons.Add("缺少成本金额，累计收益率可信度下降");
            }

            if (string.IsNullOrWhiteSpace(fund.LastSettledDate) || fund.LastSettledDate != archiveDate.ToString("yyyy-MM-dd"))
            {
                score -= 7;
                reasons.Add("未完成当日官方净值确认");
            }

            score = Math.Clamp(score, 0, 100);
            var level = score >= 85 ? "高" : score >= 70 ? "中" : score >= 55 ? "低" : "需复核";
            if (reasons.Count == 0) reasons.Add("数据完整，口径稳定");

            return new ConfidenceResult(score, level, reasons);
        }

        private static string ClassifyTheme(string name)
        {
            var n = name ?? string.Empty;
            if (ContainsAny(n, "恒生", "港股", "中概", "QDII", "互联网")) return "港股 / 恒生";
            if (ContainsAny(n, "人工智能", "AI", "机器人", "智能", "算力", "软件")) return "AI / 人工智能";
            if (ContainsAny(n, "半导体", "芯片", "集成电路", "电子", "科创")) return "半导体 / 芯片";
            if (ContainsAny(n, "白银", "黄金", "有色", "金属", "资源")) return "金属 / 贵金属";
            if (ContainsAny(n, "地产", "房地产", "基建", "建筑")) return "地产 / 基建";
            if (ContainsAny(n, "军工", "国防", "航天")) return "军工 / 国防";
            if (ContainsAny(n, "医药", "医疗", "生物", "创新药")) return "医药 / 医疗";
            if (ContainsAny(n, "证券", "银行", "金融", "保险")) return "金融";
            if (ContainsAny(n, "消费", "食品", "酒", "家电")) return "消费";
            return "其他主题";
        }

        private static bool ContainsAny(string text, params string[] keywords)
        {
            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class InsightFundRow
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public double Assets { get; set; }
            public double Cost { get; set; }
            public double HoldAmount { get; set; }
            public double HoldShares { get; set; }
            public double DailyProfit { get; set; }
            public double DailyRate { get; set; }
            public double TotalProfit { get; set; }
            public double TotalRate { get; set; }
            public double RealizedProfit { get; set; }
            public string? LastTradeDate { get; set; }
            public double LastAddAmount { get; set; }
            public string? LastSettledDate { get; set; }
            public double LastSettledRate { get; set; }
            public string Theme { get; set; } = string.Empty;
            public ConfidenceResult Confidence { get; set; } = new(0, "需复核", new List<string>());
        }

        private sealed record ConfidenceResult(int Score, string Level, List<string> Reasons);
        private sealed record SimpleFund(string Code, string Name, double Assets, double DailyProfit);

        private sealed class ExposureResult
        {
            public double TotalAssets { get; set; }
            public double Top3Concentration { get; set; }
            public string RiskLevel { get; set; } = string.Empty;
            public List<ThemeExposure> Themes { get; set; } = new();
        }

        private sealed class ThemeExposure
        {
            public string Theme { get; set; } = string.Empty;
            public int FundCount { get; set; }
            public double Assets { get; set; }
            public double Weight { get; set; }
            public double DailyProfit { get; set; }
            public List<SimpleFund> Funds { get; set; } = new();
        }
    }
}
