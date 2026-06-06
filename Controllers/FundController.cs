using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using StackExchange.Redis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using 小白养基.Models;
using 小白养基.Services;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Microsoft.Extensions.Caching.Memory;

namespace 小白养基.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FundController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBaiduOcrService _ocrService;
        private readonly PortfolioSettlementService _portfolioSettlement;

        static FundController()
        {
            System.Net.WebRequest.DefaultWebProxy = null;
        }


        public class CapitalFlowRowDto
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public double Rate { get; set; }
            public double MainNet { get; set; }
            public string MainNetText { get; set; } = string.Empty;
            public double MainRatio { get; set; }
            public double SuperNet { get; set; }
            public double BigNet { get; set; }
        }

        public class CapitalFlowPayloadDto
        {
            public string Source { get; set; } = string.Empty;
            public string UpdatedAt { get; set; } = string.Empty;
            public bool IsFallback { get; set; }
            public bool IsStale { get; set; }
            public string Message { get; set; } = string.Empty;
            public List<CapitalFlowRowDto> Rows { get; set; } = new();
            public List<CapitalFlowRowDto> Inflow { get; set; } = new();
            public List<CapitalFlowRowDto> Outflow { get; set; } = new();
            public object? Debug { get; set; }
        }

        private sealed class ExternalDataAttemptDto
        {
            public string Source { get; init; } = string.Empty;
            public string? Url { get; init; }
            public int StatusCode { get; init; }
            public string? Error { get; init; }
            public int RawRowsCount { get; init; }
            public int ParsedRowsCount { get; init; }
            public bool CacheHit { get; init; }
            public string? CacheSource { get; init; }
            public long TimeMs { get; init; }
        }

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; }
        }

        private static List<FundInfoCache> _globalFundCache = null;
        private static Dictionary<string, FundInfoCache> _exactMatchDict = null;
        private static readonly SemaphoreSlim _sectorsRefreshLock = new(1, 1);
        private static readonly SemaphoreSlim _capitalFlowRefreshLock = new(1, 1);
        private const string CapitalFlowLatestCacheKey = "capital_flow_latest";
        private static readonly TimeSpan _staleExternalDataTtl = TimeSpan.FromHours(6);
        private static readonly TimeSpan _marketRealtimeFreshTtl = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _historicalKlineFreshTtl = TimeSpan.FromDays(7);
        private static readonly TimeSpan _capitalFlowFreshTtl = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan _capitalFlowStaleTtl = TimeSpan.FromDays(1);

        private static TimeSpan GetExternalDataFreshTtl()
        {
            var now = ChinaNow();
            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            {
                return TimeSpan.FromMinutes(30);
            }

            var t = now.TimeOfDay;
            bool isTradingTime =
                (t >= new TimeSpan(9, 25, 0) && t <= new TimeSpan(11, 35, 0)) ||
                (t >= new TimeSpan(12, 55, 0) && t <= new TimeSpan(15, 10, 0));

            return isTradingTime ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(30);
        }
        private readonly IConnectionMultiplexer _redis;
        private readonly MarketCacheService _marketCache;
        public FundController(
      AppDbContext context,
      IMemoryCache cache,
      IHttpClientFactory httpClientFactory,
      IBaiduOcrService ocrService,
      PortfolioSettlementService portfolioSettlement,
      IConnectionMultiplexer redis,
      MarketCacheService marketCache)
        {
            _context = context;
            _cache = cache;
            _httpClientFactory = httpClientFactory;
            _ocrService = ocrService;
            _portfolioSettlement = portfolioSettlement;
            _redis = redis;
            _marketCache = marketCache;
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        private static string ChinaDateDash(DateTime? localTime = null)
            => (localTime ?? ChinaNow()).ToString("yyyy-MM-dd");

        private sealed record EffectiveFundDateInfo(
            DateTime NaturalDate,
            string NaturalDateText,
            DateTime EffectiveDate,
            string EffectiveDateText,
            DateTime EffectiveDateStart,
            DateTime EffectiveDateEndExclusive,
            string DateMode,
            bool MarketOpen,
            string MarketStatus,
            string MarketLabel);

        private static readonly IReadOnlySet<string> AShareClosedDates = new HashSet<string>(StringComparer.Ordinal)
        {
            // 可维护的特殊休市日期入口；当前先用周末规则兜底。
        };

        private static readonly IReadOnlySet<string> HkShareClosedDates = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        private static readonly IReadOnlySet<string> UsShareClosedDates = new HashSet<string>(StringComparer.Ordinal)
        {
        };

        /// <summary>
        /// 基金有效日期：0:00~9:25 返回上一交易日，9:25 以后返回当天，周末返回上周五。
        /// </summary>
        private static string GetEffectiveFundDate(DateTime? localTime = null)
            => GetEffectiveFundDateInfo(localTime).EffectiveDateText;

        private static EffectiveFundDateInfo GetEffectiveFundDateInfo(DateTime? localTime = null, string market = "cn")
        {
            var now = localTime ?? ChinaNow();
            var naturalDate = now.Date;
            var closedDates = market switch
            {
                "hk" => HkShareClosedDates,
                "us" => UsShareClosedDates,
                _ => AShareClosedDates
            };

            bool isWeekend = naturalDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isHoliday = closedDates.Contains(naturalDate.ToString("yyyy-MM-dd"));
            bool marketOpen = false;
            string marketStatus;
            DateTime effectiveDate;

            if (isWeekend)
            {
                marketStatus = "weekend";
                effectiveDate = GetPreviousTradingDate(naturalDate.AddDays(-1), closedDates);
            }
            else if (isHoliday)
            {
                marketStatus = "holiday";
                effectiveDate = GetPreviousTradingDate(naturalDate.AddDays(-1), closedDates);
            }
            else if (now.TimeOfDay < new TimeSpan(9, 25, 0))
            {
                marketStatus = "preopen";
                effectiveDate = GetPreviousTradingDate(naturalDate.AddDays(-1), closedDates);
            }
            else if (now.TimeOfDay < new TimeSpan(15, 0, 0))
            {
                marketStatus = "open";
                marketOpen = true;
                effectiveDate = naturalDate;
            }
            else
            {
                marketStatus = "afterclose";
                effectiveDate = naturalDate;
            }

            string dateMode = effectiveDate.Date == naturalDate ? "today" : "latest_trading_day";
            string marketLabel = marketOpen ? "盘中" : "休市";
            return new EffectiveFundDateInfo(
                naturalDate,
                naturalDate.ToString("yyyy-MM-dd"),
                effectiveDate.Date,
                effectiveDate.ToString("yyyy-MM-dd"),
                effectiveDate.Date,
                effectiveDate.Date.AddDays(1),
                dateMode,
                marketOpen,
                marketStatus,
                marketLabel);
        }

        private static DateTime GetPreviousTradingDate(DateTime date, IReadOnlySet<string> closedDates)
        {
            var cursor = date.Date;
            while (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                   closedDates.Contains(cursor.ToString("yyyy-MM-dd")))
            {
                cursor = cursor.AddDays(-1);
            }
            return cursor;
        }

        private static string DetectFundMarket(string fundCode, string fundName)
        {
            string text = $"{fundCode} {fundName}";
            if (Regex.IsMatch(text, @"恒生|港股|香港", RegexOptions.IgnoreCase)) return "hk";
            if (Regex.IsMatch(text, @"QDII|海外|全球|美元|纳斯达克|标普|日经", RegexOptions.IgnoreCase)) return "us";
            return "cn";
        }

        private static bool IsPendingStatusActive(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return !status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("settled", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("canceled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPendingDateEffective(string? pendingDate, string settleDate)
        {
            if (string.IsNullOrWhiteSpace(pendingDate)) return true;
            return string.CompareOrdinal(pendingDate, settleDate) <= 0;
        }

        private static double GetActivePendingBuyAmount(MyFundConfig fund, string settleDate)
        {
            double explicitPending = fund.PendingBuyAmount > 0
                && IsPendingStatusActive(fund.PendingTradeStatus)
                && IsPendingDateEffective(fund.PendingTradeDate, settleDate)
                ? fund.PendingBuyAmount
                : 0;
            double legacyTodayAdd = fund.LastTradeDate == settleDate && fund.LastAddAmount > 0
                ? fund.LastAddAmount
                : 0;
            return Math.Round(Math.Max(explicitPending, legacyTodayAdd), 2);
        }

        private static bool HasLegacyOcrPendingSignal(MyFundConfig fund, string settleDate)
        {
            if (fund.PendingBuyAmount > 0) return false;
            if (!IsPendingDateEffective(fund.PendingTradeDate, settleDate)) return false;
            if (string.IsNullOrWhiteSpace(fund.PendingSource)) return false;
            if (!fund.PendingSource.Contains("pending", StringComparison.OrdinalIgnoreCase)) return false;
            if (!fund.PendingSource.Contains("ocr", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(fund.PendingConfirmDate)) return false;
            return string.CompareOrdinal(fund.PendingConfirmDate, settleDate) > 0;
        }

        private static double NormalizePendingBuyCandidate(double amount)
        {
            if (amount <= 0) return 0;
            double rounded = Math.Round(amount, 2);
            if (rounded >= 1000)
            {
                double nearestThousand = Math.Round(rounded / 1000.0) * 1000.0;
                double tolerance = Math.Max(120, nearestThousand * 0.08);
                if (nearestThousand >= 1000 && Math.Abs(rounded - nearestThousand) <= tolerance)
                {
                    return Math.Round(nearestThousand, 2);
                }
            }
            return rounded;
        }

        private static double ResolvePendingBuyAmount(MyFundConfig fund, string settleDate, double? previousConfirmedAssets = null)
        {
            double activePending = GetActivePendingBuyAmount(fund, settleDate);
            if (activePending > 0) return activePending;
            if (!HasLegacyOcrPendingSignal(fund, settleDate)) return 0;

            if (previousConfirmedAssets.HasValue)
            {
                double diff = Math.Max(0, fund.HoldAmount - previousConfirmedAssets.Value);
                double pending = NormalizePendingBuyCandidate(diff);
                return Math.Round(Math.Min(fund.HoldAmount, pending), 2);
            }

            // 兼容删除后重新 OCR 的旧数据：无历史确认档案但整额买入形状明确时，全额视为待确认。
            if (IsRoundPendingBuyAmount(fund.HoldAmount))
            {
                return Math.Round(fund.HoldAmount, 2);
            }

            return 0;
        }

        private static void MarkPendingBuy(MyFundConfig fund, double amount, string tradeDate, string source, string? confirmDate = null)
        {
            if (amount <= 0) return;
            fund.PendingBuyAmount = Math.Round(amount, 2);
            fund.PendingSellAmount = 0;
            fund.PendingTradeDate = tradeDate;
            fund.PendingTradeTime = ChinaNow().ToString("HH:mm:ss");
            fund.PendingTradeStatus = "pending_buy";
            fund.PendingConfirmDate = string.IsNullOrWhiteSpace(confirmDate) ? fund.PendingConfirmDate : confirmDate;
            fund.PendingSource = source;
            fund.LastTradeDate = tradeDate;
            fund.LastAddAmount = Math.Round(amount, 2);
        }

        private static void ClearPendingBuy(MyFundConfig fund)
        {
            if (fund.PendingBuyAmount <= 0 && fund.LastAddAmount <= 0) return;
            fund.PendingBuyAmount = 0;
            if (fund.LastAddAmount > 0)
            {
                fund.LastAddAmount = 0;
                fund.LastTradeDate = null;
            }
            if (string.Equals(fund.PendingTradeStatus, "pending_buy", StringComparison.OrdinalIgnoreCase))
            {
                fund.PendingTradeStatus = "confirmed";
            }
        }

        // 同一天买入/卖出是“在途交易”，不能直接参与当日收益率分母。
        // LastAddAmount > 0：当天加仓，收益基数要剥离这笔未确认资金。
        // LastAddAmount < 0：当天减仓，收益基数要把卖出份额当天仍应承担的涨跌补回。
        private static double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate, double? resolvedPendingBuyAmount = null)
        {
            double baseAmount = fund.HoldAmount;
            double pendingBuy = resolvedPendingBuyAmount ?? GetActivePendingBuyAmount(fund, settleDate);
            baseAmount -= pendingBuy;
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        private static double GetPendingTradeAmount(MyFundConfig fund, string settleDate, double? resolvedPendingBuyAmount = null)
        {
            double pending = resolvedPendingBuyAmount ?? GetActivePendingBuyAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                pending += fund.LastAddAmount;
            }
            return Math.Round(pending, 2);
        }

        private static double GetEffectiveShares(MyFundConfig fund, string settleDate)
        {
            if (fund.HoldShares <= 0) return 0;

            double baseAmount = GetEffectiveBaseAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && Math.Abs(fund.LastAddAmount) > 0.000001 && fund.HoldAmount > 0)
            {
                return Math.Max(0, fund.HoldShares * (baseAmount / fund.HoldAmount));
            }

            return fund.HoldShares;
        }

        private sealed class OfficialNavSnapshot
        {
            public string Date { get; set; } = string.Empty;
            public double Rate { get; set; }
            public double? TodayNav { get; set; }
            public double? YesterdayNav { get; set; }
            public double? NavDiff { get; set; }
            public string Source { get; set; } = string.Empty;
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.Number) return prop.TryGetDouble(out value);
            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text) || text == "--") return false;
                return double.TryParse(text, out value);
            }
            return false;
        }

        private static OfficialNavSnapshot? TryBuildOfficialNavSnapshot(JsonElement dataArray, string settleDate)
        {
            if (dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() == 0) return null;

            var latest = dataArray[0];
            string fsrq = latest.TryGetProperty("FSRQ", out var fsrqElement) ? (fsrqElement.GetString() ?? string.Empty) : string.Empty;
            if (fsrq != settleDate) return null;

            double? apiRate = TryGetDouble(latest, "JZZZL", out var parsedRate) ? parsedRate : null;
            double? navRate = null;
            double? todayNav = null;
            double? yesterdayNav = null;
            double? navDiff = null;

            if (dataArray.GetArrayLength() > 1 &&
                TryGetDouble(latest, "DWJZ", out var navToday) &&
                TryGetDouble(dataArray[1], "DWJZ", out var navYesterday) &&
                navYesterday > 0)
            {
                todayNav = navToday;
                yesterdayNav = navYesterday;
                navDiff = navToday - navYesterday;
                navRate = Math.Round(navDiff.Value / navYesterday * 100.0, 4);
            }

            double? finalRate = null;
            string source = string.Empty;

            if (navRate.HasValue && (!apiRate.HasValue ||
                                     (Math.Abs(apiRate.Value) < 0.000001 && Math.Abs(navRate.Value) > 0.000001) ||
                                     Math.Abs(apiRate.Value - navRate.Value) > 0.05))
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }
            else if (apiRate.HasValue)
            {
                finalRate = apiRate.Value;
                source = "JZZZL";
            }
            else if (navRate.HasValue)
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }

            if (!finalRate.HasValue) return null;
            if (Math.Abs(finalRate.Value) < 0.000001 && !navRate.HasValue) return null;

            return new OfficialNavSnapshot
            {
                Date = fsrq,
                Rate = Math.Round(finalRate.Value, 4),
                TodayNav = todayNav,
                YesterdayNav = yesterdayNav,
                NavDiff = navDiff,
                Source = source
            };
        }

        private async Task<OfficialNavSnapshot?> FetchOfficialNavSnapshotAsync(HttpClient client, string fundCode, string settleDate)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=2&_={timestamp}";
            string res = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(res);
            if (!doc.RootElement.TryGetProperty("Data", out var data) ||
                !data.TryGetProperty("LSJZList", out var dataArray))
            {
                return null;
            }

            return TryBuildOfficialNavSnapshot(dataArray, settleDate);
        }
        private async Task<double> CalibrateSharesByOfficialNavAsync(
    HttpClient client,
    string fundCode,
    double antAmount,
    string settleDate)
        {
            if (string.IsNullOrWhiteSpace(fundCode) || antAmount <= 0) return 0;

            var snapshot = await FetchOfficialNavSnapshotAsync(client, fundCode, settleDate);

            if (snapshot?.TodayNav is > 0)
            {
                return Math.Round(antAmount / snapshot.TodayNav.Value, 6);
            }

            return 0;
        }
        private static bool ApplyOneDaySettlement(
     MyFundConfig fund,
     double actualRate,
     string settleDate,
     double? exactProfit = null,
     double? exactAssets = null)
        {
            double beforeHoldAmount = fund.HoldAmount;
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            double pending = GetPendingTradeAmount(fund, settleDate);

            double settledProfit;
            double newHoldAmount;

            if (exactAssets.HasValue && exactAssets.Value > 0)
            {
                // 关键：用“份额 × 官方单位净值”锚定今日真实市值，自动消除旧的几分钱尾差。
                double exactMarketAmount = Math.Round(exactAssets.Value, 2);

                newHoldAmount = Math.Round(exactMarketAmount + pending, 2);

                // 如果能用单位净值差算出真实昨日收益，优先保留真实昨日收益。
                // 如果没有 NavDiff，则用新市值反推收益。
                settledProfit = Math.Round(exactProfit ?? (exactMarketAmount - baseAmount), 2);
            }
            else
            {
                // 没有份额或没有单位净值时，保留旧逻辑。
                settledProfit = Math.Round(exactProfit ?? (baseAmount * (actualRate / 100.0)), 2);
                newHoldAmount = Math.Round(baseAmount + settledProfit + pending, 2);
            }

            Console.WriteLine(
                $"[夜间清算] code={fund.FundCode}, beforeHoldAmount={beforeHoldAmount:F2}, baseAmount={baseAmount:F2}, pending={pending:F2}, settledProfit={settledProfit:F2}, newHoldAmount={newHoldAmount:F2}");

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.HoldAmount - newHoldAmount) > 0.01;

            if (!changed) return false;

            fund.HoldAmount = newHoldAmount;
            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);

            return true;
        }
        private void ClearTodayCache(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            _cache.Remove($"Tactical_TodayData_{username}");
            _cache.Remove($"Tactical_TodayData_{username}_{ChinaDateDash()}");
            _cache.Remove($"Tactical_TodayData_{username}_{GetEffectiveFundDate()}");
        }


        private static void CopyArchiveValues(DailyArchive target, DailyArchive source)
        {
            target.FundName = string.IsNullOrWhiteSpace(source.FundName) ? target.FundName : source.FundName;
            target.Assets = Math.Round(source.Assets, 2);
            target.DailyProfit = Math.Round(source.DailyProfit, 2);
            target.DailyRate = Math.Round(source.DailyRate, 2);
            target.TotalProfit = Math.Round(source.TotalProfit, 2);
            target.TotalRate = Math.Round(source.TotalRate, 2);
        }

        private async Task UpsertDailyArchivesAsync(string username, DateTime date, IEnumerable<DailyArchive> incoming)
        {
            var normalizedIncoming = incoming
                .Where(x => !string.IsNullOrWhiteSpace(x.FundCode))
                .GroupBy(x => x.FundCode)
                .Select(g => g.Last())
                .ToList();

            var codes = normalizedIncoming.Select(x => x.FundCode).ToList();
            var existing = await _context.DailyArchives
                .Where(a => a.Username == username && a.RecordDate == date && codes.Contains(a.FundCode))
                .ToListAsync();

            var duplicateGroups = existing.GroupBy(x => x.FundCode).Where(g => g.Count() > 1).ToList();
            foreach (var group in duplicateGroups)
            {
                var keep = group.OrderByDescending(x => x.Id).First();
                var remove = group.Where(x => x.Id != keep.Id).ToList();
                if (remove.Count > 0) _context.DailyArchives.RemoveRange(remove);
            }

            var existingDict = existing
                .GroupBy(x => x.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            foreach (var item in normalizedIncoming)
            {
                item.Username = username;
                item.RecordDate = date;

                if (existingDict.TryGetValue(item.FundCode, out var old))
                {
                    CopyArchiveValues(old, item);
                    _context.DailyArchives.Update(old);
                }
                else
                {
                    _context.DailyArchives.Add(item);
                }
            }
        }


        private static double GetDailyBaseAmount(MyFundConfig fund, string settleDate, double? resolvedPendingBuyAmount = null)
        {
            double pending = GetPendingTradeAmount(fund, settleDate, resolvedPendingBuyAmount);

            // 已完成真实净值清算时，HoldAmount 已经包含当日收益。
            // 今日收益率分母必须回到清算前有效基数，否则总收益率会被“已结算后的市值”稀释。
            if (fund.LastSettledDate == settleDate)
            {
                return Math.Max(0, Math.Round(fund.HoldAmount - pending - fund.LastSettledProfit, 4));
            }

            return GetEffectiveBaseAmount(fund, settleDate, resolvedPendingBuyAmount);
        }

        private static double? FindPreviousArchiveAssets(
            MyFundConfig fund,
            IReadOnlyDictionary<string, List<DailyArchive>> archiveHistory,
            DateTime fallbackCutoff)
        {
            if (!archiveHistory.TryGetValue(fund.FundCode, out var rows) || rows.Count == 0) return null;
            var cutoff = fallbackCutoff.Date;
            if (!string.IsNullOrWhiteSpace(fund.PendingTradeDate) &&
                DateTime.TryParse(fund.PendingTradeDate, out var pendingTradeDate))
            {
                cutoff = pendingTradeDate.Date;
            }

            return rows
                .Where(a => a.RecordDate.Date < cutoff)
                .OrderByDescending(a => a.RecordDate)
                .ThenByDescending(a => a.Id)
                .Select(a => (double?)a.Assets)
                .FirstOrDefault();
        }

        private async Task<Dictionary<string, List<DailyArchive>>> LoadRecentFundArchiveHistoryAsync(
            string username,
            List<string> fundCodes,
            DateTime effectiveStart,
            DateTime effectiveEndExclusive)
        {
            if (fundCodes.Count == 0) return new Dictionary<string, List<DailyArchive>>();
            var archiveHistoryStart = effectiveStart.AddDays(-90);
            var rows = await _context.DailyArchives
                .AsNoTracking()
                .Where(a => a.Username == username
                            && a.FundCode != "TOTAL"
                            && fundCodes.Contains(a.FundCode)
                            && a.RecordDate >= archiveHistoryStart
                            && a.RecordDate < effectiveEndExclusive)
                .ToListAsync();

            return rows
                .GroupBy(a => a.FundCode)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        private static double GetRecordRateForToday(FundData? record)
        {
            if (record == null) return 0;
            return Math.Abs(record.ActualRate) > 0.000001 ? record.ActualRate : record.EstimatedRate;
        }

        private static List<DailyArchive> BuildArchiveRowsFromCurrentHoldings(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            string dateDash = date.ToString("yyyy-MM-dd");
            var latestRecordDict = todayRecords
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0;
            double totalRealized = 0;
            double totalDailyProfit = 0;
            double totalDailyBase = 0;
            double totalCurrentAssets = 0;

            foreach (var fund in funds)
            {
                double pendingBuyAmount = GetActivePendingBuyAmount(fund, dateDash);
                bool pendingBuy = pendingBuyAmount > 0;
                if (fund.HoldShares <= 0 && !pendingBuy)
                {
                    double soldProfit = fund.PlatformCumulativeProfit > 0 ? fund.PlatformCumulativeProfit : fund.RealizedProfit;
                    double soldCost = PortfolioSettlementService.GetSoldCost(fund);
                    double soldRate = soldCost > 0 ? soldProfit / soldCost * 100.0 : 0;
                    rows.Add(new DailyArchive
                    {
                        Username = username,
                        FundCode = fund.FundCode,
                        FundName = fund.FundName,
                        RecordDate = date,
                        Assets = 0,
                        DailyProfit = 0,
                        DailyRate = 0,
                        TotalProfit = Math.Round(soldProfit, 2),
                        TotalRate = Math.Round(soldRate, 2)
                    });
                    totalRealized += soldProfit;
                    continue;
                }
                latestRecordDict.TryGetValue(fund.FundCode, out var record);

                double confirmedHoldAmount = Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 2));
                double cost = Math.Max(0, Math.Round((fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount) - pendingBuyAmount, 2));
                double baseAmount = GetDailyBaseAmount(fund, dateDash);
                double dailyRate = fund.LastSettledDate == dateDash ? fund.LastSettledRate : GetRecordRateForToday(record);
                double dailyProfit = fund.LastSettledDate == dateDash
                    ? fund.LastSettledProfit
                    : Math.Round(baseAmount * (dailyRate / 100.0), 2);

                double currentAssets = fund.LastSettledDate == dateDash
                    ? confirmedHoldAmount
                    : Math.Round(confirmedHoldAmount + dailyProfit, 2);

                double totalProfit = currentAssets - cost + fund.RealizedProfit;
                double totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;

                rows.Add(new DailyArchive
                {
                    Username = username,
                    FundCode = fund.FundCode,
                    FundName = fund.FundName,
                    RecordDate = date,
                    Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2),
                    DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalRate = Math.Round(totalRate, 2)
                });

                totalCost += cost;
                totalRealized += fund.RealizedProfit;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
            }

            double totalDailyRate = totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0;
            double totalCampProfit = totalCurrentAssets - totalCost + totalRealized;
            double totalCampRate = totalCost > 0 ? totalCampProfit / totalCost * 100.0 : 0;

            rows.Add(new DailyArchive
            {
                Username = username,
                FundCode = "TOTAL",
                FundName = "总持仓",
                RecordDate = date,
                Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyRate, 2),
                TotalProfit = Math.Round(totalCampProfit, 2),
                TotalRate = Math.Round(totalCampRate, 2)
            });

            return rows;
        }

        private async Task UpsertTodayArchivesFromCurrentHoldingsAsync(string username, DateTime today, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            if (funds.Count == 0) return;
            var rows = BuildArchiveRowsFromCurrentHoldings(username, today, funds, todayRecords);
            await UpsertDailyArchivesAsync(username, today, rows);
        }

        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null && _exactMatchDict != null && _globalFundCache.Count > 0) return _globalFundCache;

            string cachedData = null;
            try
            {
                var options = ConfigurationOptions.Parse("localhost:6379");
                options.ConnectTimeout = 1500;
                options.SyncTimeout = 1500;
                using var redis = ConnectionMultiplexer.Connect(options);
                var db = redis.GetDatabase();
                var redisValue = await db.StringGetAsync("global_fund_db_cache_v3");
                if (redisValue.HasValue) cachedData = redisValue.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] Redis 异常: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(cachedData))
            {
                _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                BuildExactMatchDictionary(_globalFundCache);
                return _globalFundCache;
            }

            try
            {
                var client = _httpClientFactory.CreateClient("EastMoney");
                string jsData = await client.GetStringAsync("http://fund.eastmoney.com/js/fundcode_search.js");

                int startIndex = jsData.IndexOf('[');
                int endIndex = jsData.LastIndexOf(']');
                if (startIndex > 0 && endIndex > 0)
                {
                    string json = jsData.Substring(startIndex, endIndex - startIndex + 1);
                    var rawList = JsonSerializer.Deserialize<List<List<string>>>(json);

                    _globalFundCache = rawList.Select(x => new FundInfoCache
                    {
                        Code = x[0],
                        Name = x[2],
                        NormalizedName = NormalizeFundName(x[2])
                    }).ToList();

                    BuildExactMatchDictionary(_globalFundCache);

                    try
                    {
                        var options = ConfigurationOptions.Parse("localhost:6379");
                        options.ConnectTimeout = 1000;
                        using var redis = ConnectionMultiplexer.Connect(options);
                        await redis.GetDatabase().StringSetAsync("global_fund_db_cache_v3", JsonSerializer.Serialize(_globalFundCache), TimeSpan.FromHours(24));
                    }
                    catch { }

                    return _globalFundCache;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[致命] 东方财富接口异常: {ex.Message}");
            }

            return _globalFundCache ?? new List<FundInfoCache>();
        }

        private void BuildExactMatchDictionary(List<FundInfoCache> fundList)
        {
            _exactMatchDict = new Dictionary<string, FundInfoCache>(StringComparer.OrdinalIgnoreCase);
            foreach (var fund in fundList)
            {
                _exactMatchDict.TryAdd(fund.NormalizedName, fund);
                _exactMatchDict.TryAdd(fund.Name, fund);
            }
        }

        // 🚀 核心组件：基于历史净值与收益的「四维时空碰撞份额推演」
        private async Task<double> DeduceSharesFromHistoryAsync(string fundCode, double holdAmount, double yesterdayIncome)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("EastMoney");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=15";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                // 优先尝试使用昨日收益进行历史碰撞定位
                if (yesterdayIncome != 0)
                {
                    for (int i = 0; i < dataArray.GetArrayLength() - 1; i++)
                    {
                        if (double.TryParse(dataArray[i].GetProperty("DWJZ").GetString(), out double navToday) &&
                            double.TryParse(dataArray[i + 1].GetProperty("DWJZ").GetString(), out double navYest))
                        {
                            double diff = navToday - navYest;
                            if (diff == 0) continue;

                            double testShares = holdAmount / navToday;
                            double testYi = testShares * diff;

                            // 容错率设定为 1.5 元（化解四舍五入与尾差）
                            if (Math.Abs(testYi - yesterdayIncome) <= 1.5)
                            {
                                return Math.Round(testShares, 2);
                            }
                        }
                    }
                }

                // 如果没找到收益或碰撞失败，保底直接用最新净值折算
                if (double.TryParse(dataArray[0].GetProperty("DWJZ").GetString(), out double latestNav) && latestNav > 0)
                {
                    return Math.Round(holdAmount / latestNav, 2);
                }
            }
            catch { }
            return 0;
        }

        public sealed class OcrImportPreviewItem
        {
            public string OcrName { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public double MatchScore { get; set; }
            public double HoldAmount { get; set; }
            public double CostAmount { get; set; }
            public double HoldingIncome { get; set; }
            public double YesterdayIncome { get; set; }
            public double HoldingRate { get; set; }
            public double HoldShares { get; set; }
            public string CalcMethod { get; set; } = string.Empty;
            public string Warning { get; set; } = string.Empty;
            public bool IsPendingBuy { get; set; }
            public bool IsSuspiciousPendingBuy { get; set; }
            public double PendingBuyAmount { get; set; }
            public string PendingReason { get; set; } = string.Empty;
            public string? PendingConfirmDate { get; set; }
            public string PendingSource { get; set; } = string.Empty;
            public string PendingEvidence { get; set; } = string.Empty;
            public string PendingDecisionReason { get; set; } = string.Empty;
            public double RawTodayProfit { get; set; }
            public double RawPendingAmount { get; set; }
            public double ConfirmedAmount { get; set; }
            public double TodayBaseAmount { get; set; }
            public bool ParticipatesToday { get; set; } = true;
            public string ProfitUpdateState { get; set; } = "UNKNOWN";
        }

        public sealed class OcrImportPreviewResponse
        {
            public bool Success { get; set; }
            public int Count { get; set; }
            public string ProfitUpdateState { get; set; } = "UNKNOWN";
            public List<OcrImportPreviewItem> Items { get; set; } = new();
            public List<string> Diagnostics { get; set; } = new();
        }

        public sealed class OcrImportConfirmRequest
        {
            public string Username { get; set; } = string.Empty;
            public List<OcrImportPreviewItem> Items { get; set; } = new();
        }

        private sealed record OcrPendingBuyAssessment(
            bool IsPendingBuy,
            bool IsSuspicious,
            double PendingBuyAmount,
            string Reason,
            string Source,
            string? ConfirmDate);

        private sealed class OcrBox
        {
            public string Words { get; set; } = "";
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int CenterX => Left + Width / 2;
            public int CenterY => Top + Height / 2;
        }

        private sealed class OcrRow
        {
            public int Top { get; set; }
            public int Bottom { get; set; }
            public List<OcrBox> Boxes { get; set; } = new();
            public string FullText => string.Join(" ", Boxes.OrderBy(b => b.Left).Select(b => b.Words));
        }

        private static List<OcrBox> ParseOcrBoxes(JArray wordsResult)
        {
            var boxes = new List<OcrBox>();
            if (wordsResult == null) return boxes;
            foreach (var item in wordsResult)
            {
                var words = item["words"]?.ToString()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(words)) continue;
                var loc = item["location"];
                if (loc == null) continue;
                boxes.Add(new OcrBox
                {
                    Words = words,
                    Left = loc["left"]?.Value<int>() ?? 0,
                    Top = loc["top"]?.Value<int>() ?? 0,
                    Width = loc["width"]?.Value<int>() ?? 0,
                    Height = loc["height"]?.Value<int>() ?? 0
                });
            }
            return boxes;
        }

        private static List<OcrRow> ClusterBoxesIntoRows(List<OcrBox> boxes, int rowThreshold = 12)
        {
            if (boxes.Count == 0) return new List<OcrRow>();
            var sorted = boxes.OrderBy(b => b.Top).ToList();
            var rows = new List<OcrRow>();
            var current = new OcrRow { Top = sorted[0].Top, Bottom = sorted[0].Top + sorted[0].Height };
            current.Boxes.Add(sorted[0]);

            for (int i = 1; i < sorted.Count; i++)
            {
                var box = sorted[i];
                if (box.CenterY <= current.Bottom + rowThreshold)
                {
                    current.Boxes.Add(box);
                    current.Bottom = Math.Max(current.Bottom, box.Top + box.Height);
                }
                else
                {
                    rows.Add(current);
                    current = new OcrRow { Top = box.Top, Bottom = box.Top + box.Height };
                    current.Boxes.Add(box);
                }
            }
            rows.Add(current);
            return rows;
        }

        private static double? ParseChineseMoney(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            // 处理 "61.51亿", "-148.7亿" 等
            var yMatch = Regex.Match(text, @"^([+-]?\d[\d,]*\.?\d*)\s*亿$");
            if (yMatch.Success && double.TryParse(yMatch.Groups[1].Value.Replace(",", ""), out var yi))
                return Math.Round(yi * 1e8, 2);
            var wanMatch = Regex.Match(text, @"^([+-]?\d[\d,]*\.?\d*)\s*万$");
            if (wanMatch.Success && double.TryParse(wanMatch.Groups[1].Value.Replace(",", ""), out var wan))
                return Math.Round(wan * 1e4, 2);
            // 普通数字
            var numMatch = Regex.Match(text, @"^[+-]?\d[\d,]*\.?\d*$");
            if (numMatch.Success && double.TryParse(text.Replace(",", ""), out var num))
                return Math.Round(num, 2);
            return null;
        }

        private static bool IsAmountLine(string text)
        {
            return Regex.IsMatch(text.Trim(), @"^\d[\d,]*\.\d{2}$");
        }

        private static bool IsPercentText(string text)
        {
            return Regex.IsMatch(text.Trim(), @"^[+-]?\d+\.?\d*%$");
        }

        private static bool IsDashOrEmpty(string text)
        {
            var t = text.Trim();
            return string.IsNullOrEmpty(t) || Regex.IsMatch(t, @"^[-—–]{1,3}$") || t == "--";
        }

        private static double? ParseSignedNumber(string text)
        {
            var t = text.Trim().Replace(",", "");
            if (double.TryParse(t, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                return Math.Round(v, 2);
            return null;
        }

        private static bool IsNumericBox(string text)
        {
            var w = text.Trim().Replace(",", "").Replace("%", "").Replace("+", "");
            return double.TryParse(w, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        [HttpPost("import-ocr")]
        public Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
            => ImportOcrPreview(username, imageFile);

        [HttpPost("import-ocr-preview")]
        public async Task<IActionResult> ImportOcrPreview([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供用户名");
            if (imageFile == null || imageFile.Length == 0) return BadRequest("请上传持仓截图");

            try
            {
                var preview = await BuildOcrImportPreviewAsync(username, imageFile);
                preview.Success = true;
                preview.Count = preview.Items.Count;
                return Ok(preview);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"OCR 预览失败: {ex.Message}" });
            }
        }

        [HttpPost("import-ocr-confirm")]
        public async Task<IActionResult> ConfirmOcrImport([FromBody] OcrImportConfirmRequest request)
        {
            var username = (request.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供用户名");
            if (request.Items == null || request.Items.Count == 0) return BadRequest("没有可确认导入的持仓行");

            try
            {
                int imported = await ApplyOcrImportRowsAsync(username, request.Items);
                await _context.SaveChangesAsync();
                ClearTodayCache(username);
                return Ok(new { success = true, imported, message = $"确认完成，已同步 {imported} 只基金。" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"OCR 确认导入失败: {ex.Message}" });
            }
        }

        private async Task<OcrImportPreviewResponse> BuildOcrImportPreviewAsync(string username, IFormFile imageFile)
        {
            var allFunds = await GetAllFundsAsync();

            var userFundDict = await _context.MyFunds
                .AsNoTracking()
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            var corrections = await _context.OcrCorrections
                .AsNoTracking()
                .Where(x => x.Username == username)
                .ToListAsync();

            var robustFundPool = allFunds?.ToList() ?? new List<FundInfoCache>();

            foreach (var uf in userFundDict.Values)
            {
                if (!robustFundPool.Any(c => c.Code == uf.FundCode))
                {
                    robustFundPool.Add(new FundInfoCache
                    {
                        Code = uf.FundCode,
                        Name = uf.FundName,
                        NormalizedName = NormalizeFundName(uf.FundName)
                    });
                }
            }

            foreach (var correction in corrections)
            {
                if (!robustFundPool.Any(c => c.Code == correction.FundCode))
                {
                    robustFundPool.Add(new FundInfoCache
                    {
                        Code = correction.FundCode,
                        Name = correction.FundName,
                        NormalizedName = NormalizeFundName(correction.FundName)
                    });
                }
            }

            byte[] finalProcessedBytes;
            var diagnostics = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            using (var inputStream = imageFile.OpenReadStream())
            using (Image image = Image.Load(inputStream))
            {
                const int targetMaxWidth = 1800;

                if (image.Width > targetMaxWidth)
                {
                    int newHeight = (int)((double)image.Height / image.Width * targetMaxWidth);
                    image.Mutate(x => x.Resize(targetMaxWidth, newHeight));
                }

                image.Mutate(x => x.BackgroundColor(Color.White));

                using var outputStream = new MemoryStream();
                image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = 90 });
                finalProcessedBytes = outputStream.ToArray();
            }

            diagnostics.Add($"图片压缩耗时: {watch.ElapsedMilliseconds} ms");
            watch.Restart();

            // 优先使用带坐标的 OCR，失败则 fallback 到基础版
            JObject result;
            try
            {
                var locTask = _ocrService.AccurateWithLocationAsync(finalProcessedBytes);
                if (await Task.WhenAny(locTask, Task.Delay(20000)) != locTask)
                    throw new TimeoutException("AccurateWithLocationAsync 超时");
                result = await locTask;
                diagnostics.Add($"OCR(含坐标) 耗时: {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"[fallback] AccurateWithLocationAsync 失败: {ex.Message}，回退到 AccurateBasic");
                var basicTask = Task.Run(() => _ocrService.AccurateBasic(finalProcessedBytes));
                if (await Task.WhenAny(basicTask, Task.Delay(15000)) != basicTask)
                    throw new TimeoutException("OCR 识别超时");
                result = await basicTask;
                diagnostics.Add($"OCR(基础) 耗时: {watch.ElapsedMilliseconds} ms");
            }
            watch.Restart();

            var wordsResult = result["words_result"] as JArray;
            var boxes = ParseOcrBoxes(wordsResult);
            var texts = boxes.Select(b => b.Words).ToList();
            string fullOcrText = string.Join(" ", texts);
            string profitUpdateState = DetectProfitUpdateState(fullOcrText, texts);
            diagnostics.Add($"[收益更新状态] {profitUpdateState}");
            diagnostics.Add($"[坐标解析] 识别 {boxes.Count} 个文字框");

            if (texts.Count == 0)
            {
                throw new InvalidOperationException("OCR 未能识别出任何文字");
            }

            // 诊断：输出所有 OCR 行（带坐标）
            for (int di = 0; di < Math.Min(texts.Count, 80); di++)
            {
                diagnostics.Add($"[OCR行#{di}] {texts[di]}");
            }

            // 按坐标聚类成视觉行
            var rows = ClusterBoxesIntoRows(boxes);
            diagnostics.Add($"[坐标聚类] {rows.Count} 个视觉行");

            // 诊断：输出聚类后的行
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var r = rows[ri];
                var boxInfo = string.Join(" | ", r.Boxes.OrderBy(b => b.Left).Select(b => $"{b.Words}({b.Left},{b.Top})"));
                diagnostics.Add($"[视觉行#{ri}] Y={r.Top}-{r.Bottom}: {boxInfo}");
            }

            var items = new List<OcrImportPreviewItem>();
            var rejectedCandidates = new List<string>(); // 记录被丢弃的候选
            var navClient = _httpClientFactory.CreateClient("EastMoney");
            string settleDate = ChinaDateDash();

            // ── Two-Line Fund Card Parser（蚂蚁持仓截图两行式解析） ──
            // Step 1: 动态检测表头行，获取三列 X 坐标
            static (int nameX, int amountX, int profitX, int headerIdx)? FindFundHeaderRow(List<OcrRow> allRows)
            {
                for (int i = 0; i < allRows.Count; i++)
                {
                    var r = allRows[i];
                    var nameBox = r.Boxes.FirstOrDefault(b => b.Words.Contains("名称"));
                    var amountBox = r.Boxes.FirstOrDefault(b => b.Words.Contains("金额"));
                    var profitBox = r.Boxes.FirstOrDefault(b => b.Words.Contains("持有"));
                    if (nameBox != null && amountBox != null && profitBox != null)
                        return (nameBox.CenterX, amountBox.CenterX, profitBox.CenterX, i);
                }
                return null;
            }

            // 在名称列（左列）是否有实质性中文文本（>= 2个汉字，过滤"定投"等短标签）
            static bool HasNameColumnText(OcrRow row, int nameX)
            {
                var nameBoxes = row.Boxes.Where(b => b.CenterX < nameX).ToList();
                if (nameBoxes.Count == 0) return false;
                var nameText = string.Join("", nameBoxes.Select(b => b.Words));
                int cnCount = Regex.Matches(nameText, @"[一-鿿]").Count;
                return cnCount >= 2;
            }

            // 检查名称列文本是否为纯噪声标签（短标签 + 无实质基金名关键词）
            static bool IsNameColumnNoise(OcrRow row, int nameX)
            {
                var nameBoxes = row.Boxes.Where(b => b.CenterX < nameX).ToList();
                var nameText = string.Join("", nameBoxes.Select(b => b.Words)).Trim();
                string[] nameNoise = { "进阶类", "主要包含", "股票", "混合", "指数", "金选",
                    "指数基金", "市场解读", "更多产品", "基金市场", "机会", "自选", "持有",
                    "金额排序", "全部", "偏股", "偏债", "黄金", "全球" };
                // 如果名称列文本全部由噪声词组成（无剩余实质文字），则视为噪声
                string cleaned = nameText;
                foreach (var n in nameNoise) cleaned = cleaned.Replace(n, "");
                return cleaned.Trim().Length < 2;
            }

            var header = FindFundHeaderRow(rows);
            int headerIdx = -1;
            int nameX = 0, amountX = 0, profitX = 0;
            if (header.HasValue)
            {
                (nameX, amountX, profitX, headerIdx) = header.Value;
                diagnostics.Add($"[表头检测] 行#{headerIdx} nameX={nameX} amountX={amountX} profitX={profitX}");
            }
            else
            {
                diagnostics.Add("[警告] 未检测到表头行（名称/金额/持有），尝试兜底列位置");
                // 兜底：使用图片宽度的近似比例（蚂蚁默认布局）
                nameX = 100; amountX = 430; profitX = 660;
                diagnostics.Add($"[兜底列位置] nameX={nameX} amountX={amountX} profitX={profitX}");
            }

            // 计算列间中点作为分类阈值（兜底方案用）
            int midThreshold = (nameX + amountX) / 2 + (amountX - nameX) / 2; // 偏向 amountX
            int rightThreshold = (amountX + profitX) / 2 + (profitX - amountX) / 2; // 偏向 profitX

            // Step 2: 扫描表头之后的行，分类为 card-line / name-continuation / noise
            var cardLines = new List<(OcrRow primaryRow, OcrRow? continuationRow)>();

            for (int ri = headerIdx + 1; ri < rows.Count; ri++)
            {
                var row = rows[ri];

                // 跳过纯噪声行（无中文内容）
                if (!HasNameColumnText(row, nameX)) continue;
                // 跳过噪声标签行（"进阶类"、"金选 指数基金"等）
                if (IsNameColumnNoise(row, nameX)) continue;

                // 计算该行右列（金额/收益列）的数值数量
                int rightNumCount = row.Boxes.Count(b => b.CenterX > nameX && IsNumericBox(b.Words));

                if (rightNumCount >= 2)
                {
                    // 主行：名称 + 金额/昨日收益 + 持有收益/率 → 等待配对
                    bool paired = false;
                    for (int ni = ri + 1; ni < rows.Count; ni++)
                    {
                        var next = rows[ni];
                        // 中断条件：遇到下一个主行、遇到实质性噪声、或遇到无名称内容的行
                        if (HasNameColumnText(next, nameX) && IsNameColumnNoise(next, nameX))
                            break; // 噪声行（"金选 指数基金"、"定投"、"市场解读"等）中断配对
                        if (!HasNameColumnText(next, nameX)) break; // 无中文内容行中断
                        int nextRightNum = next.Boxes.Count(b => b.CenterX > nameX && IsNumericBox(b.Words));
                        if (nextRightNum >= 2) break; // 下一个是新主行，中断

                        // name-continuation：名称列有中文 + 右列有数值 + 名称列无噪声
                        if (nextRightNum >= 1 && !IsNameColumnNoise(next, nameX))
                        {
                            cardLines.Add((row, next));
                            paired = true;
                            ri = ni; // 跳过 continuation 行
                            break;
                        }
                    }
                    if (!paired)
                    {
                        // 单行卡片（无 continuation）
                        cardLines.Add((row, null));
                    }
                }
                // 其他行：跳过（无右列数值、或仅有1个数值且为噪声等）
            }

            diagnostics.Add($"[TwoLineCard] 发现 {cardLines.Count} 个基金卡片（表头后扫描）");

            // 3. 解析每个基金卡片（TwoLineCard 结构）
            for (int ci = 0; ci < cardLines.Count; ci++)
            {
                var (primaryRow, continuationRow) = cardLines[ci];

                // ── 合并基金名称（两行 name 列文本拼接） ──
                var primaryNameBoxes = primaryRow.Boxes
                    .Where(b => b.CenterX < nameX)
                    .OrderBy(b => b.Top).ThenBy(b => b.Left).ToList();
                string nameText1 = string.Join("", primaryNameBoxes.Select(b => b.Words)).Trim();
                // 过滤噪声前缀（"进阶类"、"主要包含股票..."等）
                foreach (var prefix in new[] { "进阶类", "主要包含股票、混合、指数", "主要包含股票混合指数" })
                    if (nameText1.StartsWith(prefix, StringComparison.Ordinal))
                        nameText1 = nameText1[prefix.Length..].TrimStart('｜', '|', '：', ':', ' ');
                string fundName = nameText1;

                if (continuationRow != null)
                {
                    var contNameBoxes = continuationRow.Boxes
                        .Where(b => b.CenterX < nameX)
                        .OrderBy(b => b.Top).ThenBy(b => b.Left).ToList();
                    string nameText2 = string.Join("", contNameBoxes.Select(b => b.Words)).Trim();
                    if (!string.IsNullOrEmpty(nameText2))
                        fundName += nameText2;
                }

                // 提取基金代码
                string? fundCode = null;
                var codeRgxMatch = Regex.Match(fundName, @"(\d{6})");
                if (codeRgxMatch.Success) fundCode = codeRgxMatch.Groups[1].Value;
                if (fundCode == null)
                {
                    foreach (var nb in primaryRow.Boxes.Concat(continuationRow?.Boxes ?? Enumerable.Empty<OcrBox>()))
                    {
                        var cm = Regex.Match(nb.Words, @"\b(\d{6})\b");
                        if (cm.Success) { fundCode = cm.Groups[1].Value; break; }
                    }
                }

                if (string.IsNullOrWhiteSpace(fundName) || fundName.Length < 2)
                {
                    rejectedCandidates.Add($"卡片#{ci} 名称过短或为空: '{fundName}'");
                    continue;
                }

                // ── 解析金额/收益（按动态列位置分配） ──
                double holdAmount = 0, yesterdayIncome = 0, holdingIncome = 0, holdingRate = 0;

                // 主行数值：中列 → amount；右列 → holdingIncome / holdingRate
                var primaryMidNums = primaryRow.Boxes
                    .Where(b => b.CenterX > nameX && b.CenterX <= amountX && IsNumericBox(b.Words))
                    .OrderBy(b => b.Left).ToList();
                var primaryRightNums = primaryRow.Boxes
                    .Where(b => b.CenterX > amountX && IsNumericBox(b.Words))
                    .OrderBy(b => b.Left).ToList();

                // 从主行中列提取 amount
                foreach (var box in primaryMidNums)
                {
                    var w = box.Words.Trim().Replace(",", "").Replace("+", "");
                    var numVal = ParseSignedNumber(w);
                    if (!numVal.HasValue) continue;
                    if (holdAmount == 0 && Math.Abs(numVal.Value) > 0)
                    {
                        holdAmount = Math.Abs(numVal.Value);
                        break;
                    }
                }

                // 从主行右列提取 holdingIncome 和 holdingRate
                foreach (var box in primaryRightNums)
                {
                    var w = box.Words.Trim().Replace(",", "").Replace("+", "");
                    if (IsPercentText(box.Words.Trim()))
                    {
                        var rateVal = ParseSignedNumber(w.Replace("%", ""));
                        if (rateVal.HasValue && holdingRate == 0) holdingRate = rateVal.Value;
                        continue;
                    }
                    var numVal = ParseSignedNumber(w);
                    if (!numVal.HasValue) continue;
                    if (holdingIncome == 0) holdingIncome = numVal.Value;
                    else if (holdingRate == 0) holdingRate = numVal.Value;
                }

                // continuation 行数值：中列 → yesterdayIncome；右列百分比 → holdingRate（如有）
                if (continuationRow != null)
                {
                    var contMidNums = continuationRow.Boxes
                        .Where(b => b.CenterX > nameX && b.CenterX <= amountX && IsNumericBox(b.Words))
                        .OrderBy(b => b.Left).ToList();
                    var contRightNums = continuationRow.Boxes
                        .Where(b => b.CenterX > amountX && IsNumericBox(b.Words))
                        .OrderBy(b => b.Left).ToList();

                    foreach (var box in contMidNums)
                    {
                        var w = box.Words.Trim().Replace(",", "").Replace("+", "");
                        var numVal = ParseSignedNumber(w);
                        if (numVal.HasValue && yesterdayIncome == 0) yesterdayIncome = numVal.Value;
                    }
                    foreach (var box in contRightNums)
                    {
                        if (IsPercentText(box.Words.Trim()))
                        {
                            var rateVal = ParseSignedNumber(box.Words.Trim().Replace("%", "").Replace(",", "").Replace("+", ""));
                            if (rateVal.HasValue && holdingRate == 0) holdingRate = rateVal.Value;
                        }
                        else
                        {
                            var numVal = ParseSignedNumber(box.Words.Trim().Replace(",", "").Replace("+", ""));
                            if (numVal.HasValue && yesterdayIncome == 0) yesterdayIncome = numVal.Value;
                        }
                    }
                }
                else
                {
                    // 单行卡片：中列第二个数值 → yesterdayIncome
                    if (primaryMidNums.Count >= 2)
                    {
                        var w = primaryMidNums[1].Words.Trim().Replace(",", "").Replace("+", "");
                        if (!IsPercentText(primaryMidNums[1].Words.Trim()))
                        {
                            var numVal = ParseSignedNumber(w);
                            if (numVal.HasValue) yesterdayIncome = numVal.Value;
                        }
                    }
                }

                if (holdAmount <= 0)
                {
                    var rightNumDesc = string.Join(", ", primaryRow.Boxes
                        .Where(b => b.CenterX > nameX)
                        .Select(b => $"{b.Words}({b.Left},{b.Top})"));
                    rejectedCandidates.Add($"卡片#{ci} '{fundName}' 金额为0，右列boxes=[{rightNumDesc}]");
                    continue;
                }

                diagnostics.Add($"[卡片#{ci}] '{fundName}' code={fundCode ?? "无"} amount={holdAmount} yesterdayIncome={yesterdayIncome} holdingIncome={holdingIncome} holdingRate={holdingRate}%");

                // ── pending 判定（仅检测明确"买入待确认"文字） ──
                double pendingAmountFromOcr = 0;
                string cardContext = primaryRow.FullText + " " + (continuationRow?.FullText ?? "");
                bool hasPendingText = Regex.IsMatch(cardContext, @"买入待确认|交易进行中|待确认金额|预计.{0,24}(可|可以)?查看收益", RegexOptions.IgnoreCase);
                bool hasZeroPending = HasZeroPendingAmountEvidence(cardContext);

                if (hasPendingText && !hasZeroPending)
                {
                    var pendingMatch = Regex.Match(cardContext, @"待确认金额\s*[:：]?\s*([+-]?\d[\d,]*\.?\d*)");
                    if (pendingMatch.Success && double.TryParse(pendingMatch.Groups[1].Value.Replace(",", ""), out var pVal) && pVal > 0)
                    {
                        pendingAmountFromOcr = pVal;
                    }
                    else if (cardContext.Contains("交易进行中", StringComparison.OrdinalIgnoreCase)
                          || cardContext.Contains("买入待确认", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingAmountFromOcr = holdAmount;
                    }
                }

                // ── 匹配基金 ──
                var best = (fund: (FundInfoCache?)null, score: 0d);
                if (fundCode != null)
                {
                    var codeMatch = robustFundPool.FirstOrDefault(f => f.Code == fundCode);
                    if (codeMatch != null) best = (codeMatch, 100.0);
                }
                if (best.fund == null)
                {
                    best = MatchOcrFund(fundName, "", robustFundPool, corrections);
                }
                if (best.fund == null || best.score <= 60)
                {
                    // 输出详细丢弃原因
                    string rejectReason = best.fund == null
                        ? $"未找到匹配基金 (名称='{fundName}')"
                        : $"匹配分数过低: {best.fund.Name}({best.fund.Code}) 分={best.score:F1} < 60";
                    rejectedCandidates.Add($"卡片#{ci} '{fundName}': {rejectReason}");
                    diagnostics.Add($"[丢弃] {rejectReason}");
                    continue;
                }

                diagnostics.Add($"[坐标匹配] {fundName} → {best.fund.Name}({best.fund.Code}) 分={best.score:F1} 金额={holdAmount} 昨日={yesterdayIncome} 持有={holdingIncome} 持有率={holdingRate}% 待确认={pendingAmountFromOcr}");

                // 份额校准
                double holdShares = 0;
                string calcMethod = "坐标识别提取";
                double navCalibratedShares = await CalibrateSharesByOfficialNavAsync(navClient, best.fund.Code, holdAmount, settleDate);
                if (navCalibratedShares > 0)
                {
                    holdShares = navCalibratedShares;
                    calcMethod = "坐标识别+净值校准";
                }

                userFundDict.TryGetValue(best.fund.Code, out var existingForPending);

                // pending 判定：只凭文字证据
                double pendingBuyAmount = 0;
                string pendingReason = "";
                string pendingSource = "";
                string? pendingConfirmDate = null;
                bool isSuspicious = false;
                string pendingEvidence = "";

                if (pendingAmountFromOcr > 0)
                {
                    pendingBuyAmount = pendingAmountFromOcr;
                    pendingReason = "OCR坐标识别到待确认金额";
                    pendingSource = "explicit_row_text";
                    pendingEvidence = "待确认金额";
                    pendingConfirmDate = EstimatePendingConfirmDate(best.fund.Name);
                }
                else if (hasZeroPending)
                {
                    pendingBuyAmount = 0;
                    pendingReason = "OCR识别到待确认金额为0";
                    pendingSource = "explicit_zero_pending";
                }
                else if (hasPendingText && Math.Abs(holdingIncome) < 0.01 && Math.Abs(holdingRate) < 0.01
                         && Regex.IsMatch(cardContext, @"--|——|–|—"))
                {
                    pendingBuyAmount = holdAmount;
                    pendingReason = "坐标识别: 有pending文字+持有收益为0";
                    pendingSource = "explicit_row_text";
                    pendingEvidence = "交易进行中/待确认";
                    pendingConfirmDate = EstimatePendingConfirmDate(best.fund.Name);
                }
                else
                {
                    // 无 pending 证据 → 清除旧 pending
                    var oldPending = existingForPending == null ? 0 : GetActivePendingBuyAmount(existingForPending, settleDate);
                    if (oldPending > 0)
                    {
                        diagnostics.Add($"[清除旧pending] {best.fund.Code} 旧={oldPending:F2}");
                    }
                    pendingBuyAmount = 0;
                }

                double confirmedAmount = Math.Max(0, Math.Round(holdAmount - pendingBuyAmount, 2));
                double todayBaseAmount = confirmedAmount;

                // 检测旧 pending 清除
                var existingPendingCheck = existingForPending == null ? 0 : GetActivePendingBuyAmount(existingForPending, settleDate);
                bool clearedOldPending = pendingBuyAmount <= 0 && existingPendingCheck > 0;

                string warning;
                if (pendingBuyAmount > 0)
                    warning = "买入待确认，不参与今日收益";
                else if (clearedOldPending)
                    warning = $"本次OCR无pending证据，清除旧遗留pending {existingPendingCheck:F2}元";
                else
                    warning = holdShares > 0 ? "" : "未能推算份额";

                items.Add(new OcrImportPreviewItem
                {
                    OcrName = fundName,
                    Code = best.fund.Code,
                    Name = best.fund.Name,
                    MatchScore = Math.Round(best.score, 2),
                    HoldAmount = Math.Round(holdAmount, 2),
                    CostAmount = holdingIncome != 0
                        ? Math.Round(holdAmount - holdingIncome, 2)
                        : Math.Round(holdAmount, 2),
                    HoldingIncome = Math.Round(holdingIncome, 2),
                    YesterdayIncome = Math.Round(yesterdayIncome, 2),
                    HoldingRate = Math.Round(holdingRate, 2),
                    HoldShares = Math.Round(holdShares, 6),
                    CalcMethod = calcMethod,
                    Warning = warning,
                    IsPendingBuy = pendingBuyAmount > 0,
                    IsSuspiciousPendingBuy = isSuspicious,
                    PendingBuyAmount = Math.Round(pendingBuyAmount, 2),
                    PendingReason = clearedOldPending
                        ? $"本次OCR无pending证据，清除旧遗留({pendingSource ?? pendingReason ?? "无"})"
                        : pendingReason,
                    PendingConfirmDate = pendingConfirmDate,
                    PendingSource = pendingBuyAmount > 0 ? pendingSource : (clearedOldPending ? "cleared_old" : ""),
                    PendingEvidence = pendingEvidence,
                    PendingDecisionReason = pendingReason,
                    RawTodayProfit = Math.Round(yesterdayIncome, 2),
                    RawPendingAmount = Math.Round(pendingBuyAmount, 2),
                    ConfirmedAmount = confirmedAmount,
                    TodayBaseAmount = todayBaseAmount,
                    ParticipatesToday = pendingBuyAmount <= 0,
                    ProfitUpdateState = profitUpdateState
                });
            }

            // 如果最终 0 只基金，输出每个候选的丢弃原因
            if (items.Count == 0 && rejectedCandidates.Count > 0)
            {
                diagnostics.Add($"[结果] 成功识别 0 只基金，丢弃原因：");
                foreach (var reason in rejectedCandidates)
                {
                    diagnostics.Add($"  - {reason}");
                }
            }
            else if (items.Count == 0 && cardLines.Count == 0)
            {
                diagnostics.Add($"[结果] 成功识别 0 只基金，未发现任何候选基金卡片。请检查截图是否为蚂蚁持仓列表页。");
            }


            return new OcrImportPreviewResponse
            {
                Success = true,
                Count = items.Count,
                ProfitUpdateState = profitUpdateState,
                Items = items,
                Diagnostics = diagnostics
            };
        }
        private static string FindPreviousFundName(List<string> texts, int amountIndex)
        {
            // Legacy wrapper
            var candidates = CollectNameCandidateLines(texts, amountIndex);
            return candidates.Count > 0 ? candidates[0] : string.Empty;
        }

        private static int FindCardStartIndex(List<string> texts, int amountIndex)
        {
            // 从金额行往上找卡片起点：
            // 遇到"财富号"行 → 该行之后
            // 遇到上一个金额行 → 该行之后
            // 到达顶部 → 0
            for (int k = amountIndex - 1; k >= 0; k--)
            {
                string line = texts[k].Trim();
                if (line.Contains("财富号")) return k + 1;
                if (Regex.IsMatch(line, @"^\d[\d,]*\.\d{2}$")) return k + 1;
            }
            return 0;
        }

        private static string BuildOcrPendingContext(List<string> texts, int amountIndex)
        {
            int start = Math.Max(0, FindCardStartIndex(texts, amountIndex) - 4);
            int end = Math.Min(texts.Count, amountIndex + 32);
            return string.Join(" ", texts.Skip(start).Take(end - start));
        }

        private static OcrPendingBuyAssessment AssessOcrPendingBuy(
            string localContext,
            string fullText,
            string fundCode,
            string fundName,
            double holdAmount,
            double holdingIncome,
            double yesterdayIncome,
            double holdingRate,
            double holdShares,
            MyFundConfig? existing,
            string settleDate)
        {
            // 强制规则：如果 OCR 上下文有"待确认金额 0.00"或"待确认金额0"，直接返回无 pending
            if (HasZeroPendingAmountEvidence(localContext) || HasZeroPendingAmountEvidence(fullText))
            {
                return new OcrPendingBuyAssessment(false, false, 0, "OCR识别到待确认金额为0，无买入待确认", "none", null);
            }

            // 保留已有 pending 状态：仅限 manual_add 来源，且本次 OCR 无明确 pending 字段
            double existingPending = existing == null ? 0 : GetActivePendingBuyAmount(existing, settleDate);
            bool isManualSource = !string.IsNullOrWhiteSpace(existing?.PendingSource)
                && existing.PendingSource.Equals("manual_add", StringComparison.OrdinalIgnoreCase);
            if (existingPending > 0 && isManualSource
                && holdAmount >= existingPending
                && !HasPendingAmountField(localContext) && !HasPendingAmountField(fullText))
            {
                return new OcrPendingBuyAssessment(
                    true,
                    false,
                    existingPending,
                    "保留手工标记的买入待确认",
                    "manual",
                    existing?.PendingConfirmDate ?? EstimatePendingConfirmDate(fundName));
            }

            // 旧 OCR/suspicious 来源的 pending 在新 OCR 无证据时直接清除
            if (existingPending > 0 && !isManualSource)
            {
                Console.WriteLine($"[OCR清除旧pending] code={existing?.FundCode}, 旧Amount={existingPending:F2}, 旧Source={existing?.PendingSource}");
            }

            // 核心规则：只凭 OCR 文字证据判断 pending，禁止差额推断
            bool localStrong = HasStrongPendingBuyEvidence(localContext);
            bool fundScopedFullSignal = HasFundScopedPendingEvidence(fullText, fundCode, fundName);
            string? confirmDate = TryExtractPendingConfirmDate(localContext)
                ?? TryExtractPendingConfirmDate(fullText)
                ?? EstimatePendingConfirmDate(fundName);

            // 路径1：本卡片区域有强文字证据（交易进行中/待确认/预计可查看收益/买入XX元）
            if (localStrong)
            {
                var localPendingAmounts = ExtractPendingBuyAmounts(localContext);
                double pendingAmount = PickMatchingPendingAmount(localPendingAmounts, holdAmount) ?? localPendingAmounts.FirstOrDefault();
                if (pendingAmount <= 0) pendingAmount = holdAmount;
                return new OcrPendingBuyAssessment(
                    true,
                    false,
                    Math.Min(Math.Round(pendingAmount, 2), Math.Round(holdAmount, 2)),
                    pendingAmount > 0 && pendingAmount < holdAmount ? "OCR识别到交易进行中的买入金额" : "OCR识别到交易进行中/待确认/预计可查看收益",
                    "explicit_row_text",
                    confirmDate);
            }

            // 路径2：全文中有该基金名称/代码附近的强文字证据
            if (fundScopedFullSignal)
            {
                var fullPendingAmounts = ExtractPendingBuyAmounts(fullText);
                double? exactMatch = PickExactPendingAmount(fullPendingAmounts, holdAmount);
                double pendingAmount = exactMatch ?? fullPendingAmounts.FirstOrDefault();
                if (pendingAmount <= 0) pendingAmount = holdAmount;
                return new OcrPendingBuyAssessment(
                    true,
                    false,
                    Math.Min(Math.Round(pendingAmount, 2), Math.Round(holdAmount, 2)),
                    "OCR全文识别到该基金的交易进行中/预计可查看收益",
                    "explicit_row_text",
                    confirmDate);
            }

            // 路径3：全文有"买入待确认 XX 元"且金额精确匹配当前持仓
            var fullPendingAmountsForMatch = ExtractPendingBuyAmounts(fullText);
            bool fullStrong = HasStrongPendingBuyEvidence(fullText);
            if (fullStrong && fullPendingAmountsForMatch.Count > 0)
            {
                double? exactMatch = PickExactPendingAmount(fullPendingAmountsForMatch, holdAmount);
                if (exactMatch.HasValue)
                {
                    return new OcrPendingBuyAssessment(
                        true,
                        false,
                        Math.Round(exactMatch.Value, 2),
                        "OCR全文买入金额精确匹配当前持仓",
                        "explicit_top_total",
                        confirmDate);
                }
            }

            if (existing == null &&
                fullStrong &&
                fullPendingAmountsForMatch.Count > 0 &&
                IsZeroProfitOcrShape(yesterdayIncome, holdingIncome, holdingRate) &&
                IsRoundPendingBuyAmount(holdAmount))
            {
                double? exactMatch = PickExactPendingAmount(fullPendingAmountsForMatch, holdAmount);
                if (exactMatch.HasValue)
                {
                    return new OcrPendingBuyAssessment(
                        true,
                        false,
                        Math.Round(exactMatch.Value, 2),
                        "新基金整额买入且全文存在买入待确认金额",
                        "heuristic_new_fund",
                        confirmDate);
                }
            }

            // 无文字证据 → 不标记 pending
            return new OcrPendingBuyAssessment(false, false, 0, "无待确认证据，按已确认持仓处理", "none", null);
        }

        private static string DetectProfitUpdateState(string fullOcrText, List<string> texts)
        {
            if (string.IsNullOrWhiteSpace(fullOcrText)) return "UNKNOWN";

            // 已更新信号
            if (Regex.IsMatch(fullOcrText, @"已更新|收益更新|当日收益|今日收益\s*\d|收益已更新"))
                return "UPDATED";

            // 未更新信号
            if (Regex.IsMatch(fullOcrText, @"收益未更新|暂未更新|市场频繁轮动|收益尚未更新"))
                return "NOT_UPDATED";

            // 启发式：多数基金昨日收益为 "--" → 可能未更新
            int dashCount = 0;
            int totalProfitSlots = 0;
            foreach (var line in texts)
            {
                if (Regex.IsMatch(line, @"^[-—–]{2,}$"))
                {
                    dashCount++;
                    totalProfitSlots++;
                }
                else if (Regex.IsMatch(line, @"^[+-]?\d[\d,]*\.\d{2}$"))
                {
                    totalProfitSlots++;
                }
            }
            if (totalProfitSlots >= 3 && dashCount > totalProfitSlots * 0.6)
                return "NOT_UPDATED";

            return "UNKNOWN";
        }

        private static bool HasPendingEvidenceInContext(string context)
        {
            if (string.IsNullOrWhiteSpace(context)) return false;
            // 排除"待确认金额 0.00"（明确无 pending）
            if (HasZeroPendingAmountEvidence(context)) return false;
            return context.Contains("交易进行中", StringComparison.OrdinalIgnoreCase)
                || context.Contains("买入待确认", StringComparison.OrdinalIgnoreCase)
                || context.Contains("确认中", StringComparison.OrdinalIgnoreCase)
                || context.Contains("待确认", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(context, @"预计.{0,24}(可|可以)?查看收益", RegexOptions.IgnoreCase)
                || context.Contains("买入金额待确认", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasZeroPendingAmountEvidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"待确认金额\s*[:：]?\s*0+\.?0*")
                || Regex.IsMatch(text, @"买入待确认\s*[:：]?\s*0+\.?0*\s*元");
        }

        private static bool HasPendingAmountField(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"待确认金额|买入待确认\s*[:：]?\s*\d");
        }

        private static List<string> CollectNameCandidateLines(List<string> texts, int amountIndex)
        {
            var result = new List<string>();
            int cardStart = FindCardStartIndex(texts, amountIndex);

            for (int k = amountIndex - 1; k >= cardStart; k--)
            {
                string prev = texts[k].Trim();
                if (string.IsNullOrWhiteSpace(prev)) continue;
                if (IsNoiseLine(prev)) continue;
                if (Regex.IsMatch(prev, @"^[-\d\.,%+]+$")) continue;
                // 过滤短片段（单个中文字符/2字符非截断），但保留截断行和有意义的名称行
                if (prev.Length <= 2 && !prev.EndsWith("...") && !prev.EndsWith("…")) continue;
                result.Add(prev);
                if (result.Count >= 4) break;
            }
            return result;
        }

        private static string? TryExtractFundCode(List<string> texts, int amountIndex)
        {
            // 在金额行前1~5行中查找6位基金代码
            for (int k = Math.Max(0, amountIndex - 5); k < amountIndex; k++)
            {
                var m = Regex.Match(texts[k], @"\b(\d{6})\b");
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        private static bool HasStrongPendingBuyEvidence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // 排除"待确认金额 0.00"
            if (HasZeroPendingAmountEvidence(text)) return false;
            return text.Contains("交易进行中", StringComparison.OrdinalIgnoreCase)
                || text.Contains("确认中", StringComparison.OrdinalIgnoreCase)
                || text.Contains("待确认", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(text, @"预计.{0,24}(可|可以)?查看收益", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"(?:买入|加仓)[/／\s:：|]*[0-9][0-9,]*(?:\.\d{1,2})?\s*元?", RegexOptions.IgnoreCase);
        }

        private static bool HasYieldDashSignal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"(--|——|–|—)")
                || Regex.IsMatch(text, @"昨日收益.{0,8}(--|——|–|—)", RegexOptions.IgnoreCase);
        }

        private static bool IsZeroProfitOcrShape(double yesterdayIncome, double holdingIncome, double holdingRate)
            => Math.Abs(yesterdayIncome) < 0.01 && Math.Abs(holdingIncome) < 0.01 && Math.Abs(holdingRate) < 0.01;

        private static bool IsRoundPendingBuyAmount(double amount)
        {
            if (amount <= 0) return false;
            var rounded = Math.Round(amount, 2);
            if (Math.Abs(rounded - 1000) < 0.01 || Math.Abs(rounded - 5000) < 0.01 || Math.Abs(rounded - 10000) < 0.01) return true;
            return rounded >= 1000 && Math.Abs(rounded % 1000) < 0.01;
        }

        private static List<double> ExtractPendingBuyAmounts(string text)
        {
            var amounts = new List<double>();
            if (string.IsNullOrWhiteSpace(text)) return amounts;
            foreach (Match match in Regex.Matches(text, @"(?:买入待确认|待确认买入|买入|加仓)[/／\s:：|]*([0-9][0-9,]*(?:\.\d{1,2})?)\s*元?", RegexOptions.IgnoreCase))
            {
                if (double.TryParse(match.Groups[1].Value.Replace(",", ""), out var amount) && amount > 0)
                {
                    double rounded = Math.Round(amount, 2);
                    if (!amounts.Any(x => Math.Abs(x - rounded) < 0.01)) amounts.Add(rounded);
                }
            }
            return amounts;
        }

        private static double? PickMatchingPendingAmount(IEnumerable<double> amounts, double holdAmount)
        {
            foreach (var amount in amounts)
            {
                if (amount <= 0 || amount > holdAmount + 1) continue;
                double tolerance = Math.Max(1, holdAmount * 0.005);
                if (Math.Abs(amount - holdAmount) <= tolerance || amount < holdAmount)
                {
                    return Math.Round(amount, 2);
                }
            }
            return null;
        }

        private static double? PickExactPendingAmount(IEnumerable<double> amounts, double expectedAmount)
        {
            if (expectedAmount <= 0) return null;
            double tolerance = Math.Max(1, expectedAmount * 0.01);
            foreach (var amount in amounts)
            {
                if (amount > 0 && Math.Abs(amount - expectedAmount) <= tolerance)
                {
                    return Math.Round(amount, 2);
                }
            }
            return null;
        }

        private static bool HasFundScopedPendingEvidence(string fullText, string fundCode, string fundName)
        {
            if (string.IsNullOrWhiteSpace(fullText)) return false;
            var anchors = new List<string>();
            if (!string.IsNullOrWhiteSpace(fundCode)) anchors.Add(fundCode);
            foreach (var token in BuildFundNameTokens(fundName).Take(3))
            {
                if (token.Length >= 2) anchors.Add(token);
            }
            foreach (var anchor in anchors.Distinct())
            {
                int index = fullText.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    int start = Math.Max(0, index - 80);
                    int length = Math.Min(fullText.Length - start, anchor.Length + 180);
                    string window = fullText.Substring(start, length);
                    if (HasStrongPendingBuyEvidence(window)) return true;
                    int nextStart = index + anchor.Length;
                    index = nextStart < fullText.Length
                        ? fullText.IndexOf(anchor, nextStart, StringComparison.OrdinalIgnoreCase)
                        : -1;
                }
            }
            return false;
        }

        private static IEnumerable<string> BuildFundNameTokens(string fundName)
        {
            if (string.IsNullOrWhiteSpace(fundName)) yield break;
            string normalized = Regex.Replace(fundName, @"[\s（）()A-Za-z]+", "");
            string withoutGenericWords = Regex.Replace(
                normalized,
                @"(ETF|联接|混合|指数|发起式|QDII|LOF|增强|债券|股票|基金|行业|主题|精选)",
                "|",
                RegexOptions.IgnoreCase);
            foreach (var token in withoutGenericWords.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var t = token.Trim();
                if (t.Length >= 2) yield return t;
            }
            if (normalized.Length >= 4) yield return normalized[..Math.Min(6, normalized.Length)];
        }

        private static string? TryExtractPendingConfirmDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, @"预计\s*(\d{1,2})[./月-]?\s*(\d{1,2})\s*日?.{0,24}(可|可以)?查看收益");
            if (!match.Success) return null;
            var now = ChinaNow();
            if (!int.TryParse(match.Groups[1].Value, out var month) || !int.TryParse(match.Groups[2].Value, out var day)) return null;
            try
            {
                var date = new DateTime(now.Year, month, day);
                if (date.Date < now.Date.AddDays(-7)) date = date.AddYears(1);
                return date.ToString("yyyy-MM-dd");
            }
            catch
            {
                return null;
            }
        }

        private static string EstimatePendingConfirmDate(string fundName)
        {
            var today = ChinaNow().Date;
            bool overseas = !string.IsNullOrWhiteSpace(fundName)
                && Regex.IsMatch(fundName, @"QDII|恒生|港股|海外|全球|美元|纳斯达克|标普|日经", RegexOptions.IgnoreCase);
            return today.AddDays(overseas ? 3 : 1).ToString("yyyy-MM-dd");
        }

        private static bool IsNoiseLine(string text)
        {
            string[] noise = { "收益", "金额", "份额", "金选", "市场解读", "基金经理", "阶段", "趋势", "去看看", "更新",
                               "财富号", "基金财富号", "我的持有", "持有收益", "昨日收益", "持有收益率", "更多产品",
                               "买入", "卖出", "定投", "自选", "关注", "首页", "理财", "资产", "总资产" };
            return noise.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
        }

        private static void SelectIncomeCandidates(
       double holdAmount,
       double holdingRate,
       List<double> signedNumbers,
       out double holdingIncome,
       out double yesterdayIncome)
        {
            double selectedHoldingIncome = 0;
            double selectedYesterdayIncome = 0;

            holdingIncome = 0;
            yesterdayIncome = 0;

            if (signedNumbers.Count == 0)
            {
                return;
            }

            if (holdingRate != 0 && signedNumbers.Count >= 1)
            {
                double bestDiff = double.MaxValue;
                double bestIncome = 0;

                foreach (var num in signedNumbers)
                {
                    double testCost = holdAmount - num;
                    if (testCost <= 0) continue;

                    double testRate = (num / testCost) * 100;
                    double diff = Math.Abs(testRate - holdingRate);

                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIncome = num;
                    }
                }

                if (bestDiff < 8.0)
                {
                    selectedHoldingIncome = bestIncome;
                    selectedYesterdayIncome = signedNumbers.FirstOrDefault(n => Math.Abs(n) != Math.Abs(selectedHoldingIncome));

                    holdingIncome = selectedHoldingIncome;
                    yesterdayIncome = selectedYesterdayIncome;
                    return;
                }
            }

            if (holdingRate != 0)
            {
                var matchBySign = signedNumbers
                    .Where(n => (n > 0 && holdingRate > 0) || (n < 0 && holdingRate < 0))
                    .ToList();

                if (matchBySign.Count == 1)
                {
                    selectedHoldingIncome = matchBySign.First();
                    selectedYesterdayIncome = signedNumbers.FirstOrDefault(n => n != selectedHoldingIncome);

                    holdingIncome = selectedHoldingIncome;
                    yesterdayIncome = selectedYesterdayIncome;
                    return;
                }
            }

            selectedHoldingIncome = signedNumbers
                .OrderByDescending(n => Math.Abs(n))
                .First();

            if (signedNumbers.Count > 1)
            {
                selectedYesterdayIncome = signedNumbers.FirstOrDefault(n => n != selectedHoldingIncome);
            }

            holdingIncome = selectedHoldingIncome;
            yesterdayIncome = selectedYesterdayIncome;
        }
        private (FundInfoCache? fund, double score) MatchOcrFund(string namePart, string potentialFragment, List<FundInfoCache> fundPool, List<OcrCorrection> corrections)
        {
            string[] testNames = string.IsNullOrWhiteSpace(potentialFragment) ? new[] { namePart } : new[] { namePart, namePart + potentialFragment };
            FundInfoCache? finalBestMatch = null;
            double finalBestScore = 0;

            foreach (var testName in testNames)
            {
                string normalizedOcr = NormalizeFundName(testName);
                if (normalizedOcr.Length < 3) continue;

                // \u5b66\u4e60\u8fc7\u7684\u4fee\u6b63\u4f18\u5148
                var learned = corrections.FirstOrDefault(x => string.Equals(NormalizeFundName(x.OcrName), normalizedOcr, StringComparison.OrdinalIgnoreCase));
                if (learned != null)
                {
                    return (new FundInfoCache { Code = learned.FundCode, Name = learned.FundName, NormalizedName = NormalizeFundName(learned.FundName) }, 99.5);
                }

                string pureChinese = Regex.Replace(testName, @"[^\u4e00-\u9fa5]", "");
                if (pureChinese.Length < 2) continue;

                var ocrTokens = ExtractChineseTokens(testName);

                FundInfoCache? bestMatch = null;
                double bestScore = 0;

                // \u7cbe\u786e\u5339\u914d\uff08\u89c4\u8303\u5316\u540e\uff09
                if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(testName, out exactFund)))
                {
                    bestMatch = exactFund;
                    bestScore = 100.0;
                }
                else
                {
                    // \u6269\u5927\u5019\u9009\u8303\u56f4\uff1a\u4e0d\u518d\u8981\u6c42 pureChinese \u524d\u7f00\u5339\u914d
                    var candidates = fundPool.Where(f =>
                        !string.IsNullOrWhiteSpace(f.NormalizedName) &&
                        HasCommonSubstring(normalizedOcr, f.NormalizedName, 3));

                    foreach (var f in candidates)
                    {
                        double score = FuzzyScoreFundName(normalizedOcr, f.NormalizedName, ocrTokens);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestMatch = f;
                        }
                    }
                }

                if (bestScore > finalBestScore)
                {
                    finalBestScore = bestScore;
                    finalBestMatch = bestMatch;
                }
            }

            return (finalBestMatch, finalBestScore);
        }

        /// <summary>
        /// \u8ba1\u7b97 OCR \u540d\u79f0\u4e0e\u57fa\u91d1\u540d\u79f0\u7684\u6a21\u7cca\u5339\u914d\u5206\u6570\uff080-100\uff09
        /// </summary>
        private static double FuzzyScoreFundName(string normalizedOcr, string normalizedFund, List<string> ocrTokens)
        {
            // 1. \u5305\u542b\u5173\u7cfb\u52a0\u5206\uff08>= 0.85 \u6743\u91cd\uff09
            if (normalizedFund.Contains(normalizedOcr) || normalizedOcr.Contains(normalizedFund))
            {
                double shorter = Math.Min(normalizedOcr.Length, normalizedFund.Length);
                double longer = Math.Max(normalizedOcr.Length, normalizedFund.Length);
                double containRatio = shorter / longer;
                if (containRatio >= 0.85) return 95.0; // \u5f3a\u5305\u542b
                return 80.0 + containRatio * 15; // \u90e8\u5206\u5305\u542b
            }

            // 2. \u6700\u957f\u516c\u5171\u5b50\u4e32\uff08>= 6 \u4e2d\u6587\u5b57\u7b26 = \u5f3a\u8bc1\u636e\uff09
            int lcsLen = LongestCommonSubstringLength(normalizedOcr, normalizedFund);
            double lcsScore = 0;
            if (lcsLen >= 6)
            {
                double lcsRatio = (double)lcsLen / Math.Max(normalizedOcr.Length, normalizedFund.Length);
                lcsScore = 60.0 + lcsRatio * 35; // 60-95
            }

            // 3. Levenshtein \u76f8\u4f3c\u5ea6
            double levRatio = CalculateSimilarity(normalizedOcr, normalizedFund);
            double levScore = levRatio * 100;

            // 4. Token \u547d\u4e2d\u52a0\u5206
            double tokenBonus = 0;
            if (ocrTokens.Count >= 2)
            {
                int hits = ocrTokens.Count(t => normalizedFund.Contains(t));
                tokenBonus = (double)hits / ocrTokens.Count * 15; // \u6700\u591a\u52a015\u5206
            }

            return Math.Min(100, Math.Max(lcsScore, levScore) + tokenBonus);
        }

        /// <summary>
        /// \u68c0\u67e5\u4e24\u4e2a\u5b57\u7b26\u4e32\u662f\u5426\u6709\u957f\u5ea6 >= minLength \u7684\u516c\u5171\u5b50\u4e32
        /// </summary>
        private static bool HasCommonSubstring(string a, string b, int minLength)
        {
            if (a.Length < minLength || b.Length < minLength) return false;
            // \u5feb\u901f\u68c0\u67e5\uff1a\u770b a \u7684\u6bcf\u4e2a minLength-gram \u662f\u5426\u5728 b \u4e2d
            for (int i = 0; i <= a.Length - minLength; i++)
            {
                if (b.Contains(a.Substring(i, minLength))) return true;
            }
            return false;
        }

        /// <summary>
        /// \u8ba1\u7b97\u6700\u957f\u516c\u5171\u5b50\u4e32\u957f\u5ea6
        /// </summary>
        private static int LongestCommonSubstringLength(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            int maxLen = 0;
            var prev = new int[b.Length + 1];
            for (int i = 0; i < a.Length; i++)
            {
                var curr = new int[b.Length + 1];
                for (int j = 0; j < b.Length; j++)
                {
                    if (a[i] == b[j])
                    {
                        curr[j + 1] = prev[j] + 1;
                        if (curr[j + 1] > maxLen) maxLen = curr[j + 1];
                    }
                }
                prev = curr;
            }
            return maxLen;
        }

        private static List<string> ExtractChineseTokens(string text)
        {
            // \u63d0\u53d6\u8fde\u7eed\u4e2d\u6587\u7247\u6bb5\u4f5c\u4e3a\u5173\u952e\u8bcd\uff082\u5b57\u4ee5\u4e0a\uff09
            var tokens = new List<string>();
            var matches = Regex.Matches(text, @"[\u4e00-\u9fa5]{2,}");
            foreach (Match m in matches) tokens.Add(m.Value);
            return tokens;
        }

        private List<(FundInfoCache fund, double score)> FindTopNCandidates(string namePart, string potentialFragment, List<FundInfoCache> fundPool, int n)
        {
            string testName = string.IsNullOrWhiteSpace(potentialFragment) ? namePart : namePart + potentialFragment;
            string normalizedOcr = NormalizeFundName(testName);
            if (normalizedOcr.Length < 3) return new();

            var ocrTokens = ExtractChineseTokens(testName);
            var scored = new List<(FundInfoCache fund, double score)>();

            foreach (var f in fundPool)
            {
                if (string.IsNullOrWhiteSpace(f.NormalizedName)) continue;
                if (!HasCommonSubstring(normalizedOcr, f.NormalizedName, 3)) continue;

                double score = FuzzyScoreFundName(normalizedOcr, f.NormalizedName, ocrTokens);
                scored.Add((f, score));
            }

            return scored.OrderByDescending(x => x.score).Take(n).ToList();
        }

        private async Task<int> ApplyOcrImportRowsAsync(string username, List<OcrImportPreviewItem> items)
        {
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            int imported = 0;

            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.Code) && x.HoldAmount > 0))
            {
                if (userFundDict.TryGetValue(item.Code, out var exist))
                {
                    ApplyOcrRowToExistingFund(exist, item);
                    _context.MyFunds.Update(exist);
                }
                else
                {
                    var newFund = new MyFundConfig
                    {
                        Username = username,
                        FundCode = item.Code,
                        FundName = item.Name,
                        HoldAmount = Math.Round(item.HoldAmount, 2),
                        CostAmount = item.CostAmount > 0 ? Math.Round(item.CostAmount, 2) : Math.Round(item.HoldAmount, 2),
                        HoldShares = item.IsPendingBuy && item.PendingBuyAmount >= item.HoldAmount - 0.01 ? 0 : Math.Round(item.HoldShares, 6),
                        LastTradeDate = item.IsPendingBuy ? ChinaDateDash() : null,
                        LastAddAmount = item.IsPendingBuy ? Math.Round(item.PendingBuyAmount > 0 ? item.PendingBuyAmount : item.HoldAmount, 2) : 0,
                        PendingBuyAmount = item.IsPendingBuy ? Math.Round(item.PendingBuyAmount > 0 ? item.PendingBuyAmount : item.HoldAmount, 2) : 0,
                        PendingTradeDate = item.IsPendingBuy ? ChinaDateDash() : null,
                        PendingTradeTime = item.IsPendingBuy ? ChinaNow().ToString("HH:mm:ss") : null,
                        PendingTradeStatus = item.IsPendingBuy ? "pending_buy" : null,
                        PendingConfirmDate = item.PendingConfirmDate,
                        PendingSource = item.IsPendingBuy ? (string.IsNullOrWhiteSpace(item.PendingSource) ? "ocr" : item.PendingSource) : null,
                        LastSettledDate = null,
                        LastSettledProfit = 0,
                        LastSettledRate = 0
                    };
                    _context.MyFunds.Add(newFund);
                    userFundDict[newFund.FundCode] = newFund;
                }

                await UpsertOcrCorrectionAsync(username, item);
                imported++;
            }

            return imported;
        }

        private static void ApplyOcrRowToExistingFund(MyFundConfig exist, OcrImportPreviewItem item)
        {
            Console.WriteLine(
                $"[OCR校准前] code={exist.FundCode}, oldHoldAmount={exist.HoldAmount:F2}, oldLastTradeDate={exist.LastTradeDate ?? "null"}, oldLastAddAmount={exist.LastAddAmount:F2}");

            string todayDash = ChinaDateDash();
            double oldHoldAmount = exist.HoldAmount;
            if (item.HoldAmount > 0)
            {
                exist.HoldAmount = Math.Round(item.HoldAmount, 2);
            }

            if (item.CostAmount > 0) exist.CostAmount = Math.Round(item.CostAmount, 2);
            if (item.HoldShares > 0)
            {
                exist.HoldShares = Math.Round(item.HoldShares, 6);
            }

            double pendingAmount = item.PendingBuyAmount > 0 ? item.PendingBuyAmount : 0;
            if (pendingAmount > 0)
            {
                MarkPendingBuy(exist, pendingAmount, todayDash, string.IsNullOrWhiteSpace(item.PendingSource) ? "ocr" : item.PendingSource, item.PendingConfirmDate);
            }
            else
            {
                // 本次 OCR 无 pending 证据 → 清除旧 pending 状态（含旧 OCR 遗留）
                if (GetActivePendingBuyAmount(exist, todayDash) > 0)
                {
                    Console.WriteLine($"[OCR清除旧pending] code={exist.FundCode}, 旧PendingBuyAmount={exist.PendingBuyAmount:F2}, 旧Source={exist.PendingSource ?? "null"}");
                }
                ClearPendingBuy(exist);
                exist.LastSettledDate = null;
                exist.LastSettledProfit = 0;
                exist.LastSettledRate = 0;
            }

            Console.WriteLine(
                $"[OCR校准后] code={exist.FundCode}, newHoldAmount={exist.HoldAmount:F2}, newLastTradeDate={exist.LastTradeDate ?? "null"}, newLastAddAmount={exist.LastAddAmount:F2}");
        }

        private async Task UpsertOcrCorrectionAsync(string username, OcrImportPreviewItem item)
        {
            if (string.IsNullOrWhiteSpace(item.OcrName)) return;

            string normalized = NormalizeFundName(item.OcrName);
            var existing = await _context.OcrCorrections.FirstOrDefaultAsync(x => x.Username == username && x.OcrName == normalized);
            if (existing == null)
            {
                _context.OcrCorrections.Add(new OcrCorrection
                {
                    Username = username,
                    OcrName = normalized,
                    FundCode = item.Code,
                    FundName = item.Name,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.FundCode = item.Code;
                existing.FundName = item.Name;
                existing.UpdatedAt = DateTime.UtcNow;
                _context.OcrCorrections.Update(existing);
            }
        }

        private string ExtractFundClass(string name)
        {
            if (Regex.IsMatch(name, @"C类?$|\(C\)$|（C）$", RegexOptions.IgnoreCase)) return "C";
            if (Regex.IsMatch(name, @"A类?$|\(A\)$|（A）$", RegexOptions.IgnoreCase)) return "A";
            if (name.Contains("QDII", StringComparison.OrdinalIgnoreCase)) return "QDII";
            return "";
        }

        private static string NormalizeFundName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name
                .ToUpper()
                .Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "")
                .Replace("（", "(").Replace("）", ")").Replace("，", ",").Replace("。", ".")
                .Replace("：", ":").Replace("；", ";").Replace("！", "!").Replace("？", "?")
                // 全角数字/字母 → 半角
                .Replace("Ａ", "A").Replace("Ｂ", "B").Replace("Ｃ", "C").Replace("Ｄ", "D")
                // 去除常见噪声词
                .Replace("(QDII)", "").Replace("QDII", "")
                .Replace("ETF联接", "ETF").Replace("ETF联结", "ETF")
                .Replace("证券投资基金", "").Replace("证券基金", "")
                .Replace("发起式", "").Replace("主题", "").Replace("指数型", "")
                .Replace("混合型", "").Replace("混合", "").Replace("指数", "")
                .Replace("C类", "C").Replace("A类", "A").Replace("B类", "B")
                .Replace("C份额", "C").Replace("A份额", "A").Replace("B份额", "B")
                .Replace("基金", "").Replace("｜", "").Replace("|", "")
                .Trim();
            // 处理 OCR 截断词：设→设备，材料设→材料设备，质量精→质量精选
            s = Regex.Replace(s, @"材料设$", "材料设备");
            s = Regex.Replace(s, @"质量精$", "质量精选");
            s = Regex.Replace(s, @"(?<![一-龥])设$", "设备");
            return s;
        }

        private static double CalculateSimilarity(string s, string t)
        {
            if (s == t) return 1.0;
            int n = s.Length, m = t.Length;
            if (n == 0 || m == 0) return 0.0;

            int[] v0 = new int[m + 1];
            int[] v1 = new int[m + 1];
            for (int i = 0; i <= m; i++) v0[i] = i;

            for (int i = 0; i < n; i++)
            {
                v1[0] = i + 1;
                for (int j = 0; j < m; j++)
                {
                    int cost = (s[i] == t[j]) ? 0 : 1;
                    v1[j + 1] = Math.Min(v1[j] + 1, Math.Min(v0[j + 1] + 1, v0[j] + cost));
                }
                Array.Copy(v1, v0, m + 1);
            }
            return 1.0 - (double)v0[m] / Math.Max(n, m);
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddFund([FromForm] string username, [FromForm] string code, [FromForm] double amount)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("未登录");
            var client = _httpClientFactory.CreateClient("FundGz");
            try
            {
                string url = $"http://fundgz.1234567.com.cn/js/{code}.js";
                string response = await client.GetStringAsync(url);
                var match = Regex.Match(response, @"jsonpgz\((.*?)\);");
                if (match.Success)
                {
                    var root = JsonDocument.Parse(match.Groups[1].Value).RootElement;
                    string name = root.GetProperty("name").GetString() ?? "未知";

                    var exist = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                    var todayDash = ChinaDateDash();
                    if (exist != null)
                    {
                        double oldAmount = exist.HoldAmount;
                        double pendingAmount = amount > oldAmount ? Math.Round(amount - oldAmount, 2) : amount;
                        exist.HoldAmount = Math.Round(amount, 2);
                        if (amount > oldAmount)
                        {
                            exist.CostAmount = Math.Round(Math.Max(exist.CostAmount, oldAmount) + pendingAmount, 2);
                            MarkPendingBuy(exist, pendingAmount, todayDash, "manual_add");
                        }
                    }
                    else
                    {
                        var newFund = new MyFundConfig
                        {
                            Username = username,
                            FundCode = code,
                            FundName = name,
                            HoldAmount = Math.Round(amount, 2),
                            CostAmount = Math.Round(amount, 2),
                            HoldShares = 0
                        };
                        MarkPendingBuy(newFund, amount, todayDash, "manual_add");
                        _context.MyFunds.Add(newFund);
                    }

                    await _context.SaveChangesAsync();
                    ClearTodayCache(username);
                    return Ok(new { success = true, name = name });
                }
                return BadRequest("找不到该基金");
            }
            catch { return BadRequest("网络请求失败"); }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteFund([FromForm] string username, [FromForm] string code)
        {
            var target = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
            if (target != null)
            {
                _context.MyFunds.Remove(target);
                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            return BadRequest("未找到该基金配置");
        }

        [HttpPost("settle-nightly")]
        public async Task<IActionResult> SettleNightly([FromForm] string username, [FromForm] string codes, [FromForm] string rates)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(codes)) return BadRequest("缺少清算参数！");

            try
            {
                var codeList = codes.Split(',');
                var rateList = rates.Split(',');

                var funds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                int count = 0;

                for (int i = 0; i < codeList.Length; i++)
                {
                    var fund = funds.FirstOrDefault(f => f.FundCode == codeList[i]);
                    if (fund != null && i < rateList.Length)
                    {
                        if (double.TryParse(rateList[i], out double rate))
                        {
                            var settleDate = ChinaDateDash();
                            if (ApplyOneDaySettlement(fund, rate, settleDate))
                            {
                                count++;
                            }
                        }
                    }
                }

                if (count > 0)
                {
                    var settleDate = ChinaNow().Date;
                    var fundCodes = funds.Select(f => f.FundCode).Distinct().ToList();
                    var todayRecords = await _context.FundRecords
                        .Where(r => r.FetchTime >= settleDate && fundCodes.Contains(r.FundCode))
                        .ToListAsync();
                    await UpsertTodayArchivesFromCurrentHoldingsAsync(username, settleDate, funds, todayRecords);
                }

                await _context.SaveChangesAsync();
                ClearTodayCache(username);
                return Ok($"已清算 {count} 只基金，并已同步今日收益档案！");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"清算异常: {ex.Message}");
            }
        }

        [HttpGet("auto-settle")]
        public async Task<IActionResult> AutoSettle()
        {
            try
            {
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("当前暂无基金需要清算。");

                var client = _httpClientFactory.CreateClient("FundGz");
                int successCount = 0;

                foreach (var fund in allFunds)
                {
                    try
                    {
                        string url = $"http://fundgz.1234567.com.cn/js/{fund.FundCode}.js?rt={DateTime.Now.Ticks}";
                        string jsData = await client.GetStringAsync(url);
                        var match = Regex.Match(jsData, @"\""gszzl\"":\""([^\""]+)\""");
                        if (match.Success && double.TryParse(match.Groups[1].Value, out double rate))
                        {
                            var settleDate = ChinaDateDash();
                            if (ApplyOneDaySettlement(fund, rate, settleDate))
                            {
                                successCount++;
                            }
                        }
                    }
                    catch { continue; }
                }

                if (successCount > 0)
                {
                    var settleDate = ChinaNow().Date;
                    var codesForToday = allFunds.Select(f => f.FundCode).Distinct().ToList();
                    var todayRecords = await _context.FundRecords
                        .Where(r => r.FetchTime >= settleDate && codesForToday.Contains(r.FundCode))
                        .ToListAsync();

                    foreach (var group in allFunds.GroupBy(f => f.Username))
                    {
                        await UpsertTodayArchivesFromCurrentHoldingsAsync(group.Key, settleDate, group.ToList(), todayRecords);
                        ClearTodayCache(group.Key);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动清算执行完毕！成功更新 {successCount} 只，并已同步今日收益档案。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动清算引擎异常: {ex.Message}");
            }
        }
        private const string GlobalIndicesCacheKey = "api:fund:global-indices:v1";
        private const string GlobalIndexCachePrefix = "api:fund:gi:";

        private sealed class GlobalIndexDefinition
        {
            public string Name { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public string Market { get; init; } = string.Empty;
            public string[] Secids { get; init; } = Array.Empty<string>();
            public string? SinaFuturesSymbol { get; init; }
        }

        private sealed class GlobalIndexKlineDto
        {
            public string Date { get; init; } = string.Empty;
            public double Rate { get; init; }
        }

        private sealed class GlobalIndexDto
        {
            public string Name { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public string Market { get; init; } = string.Empty;
            public string Secid { get; init; } = string.Empty;
            public double? Latest { get; init; }
            public double? Point { get; init; }
            public double? Close { get; init; }
            public double? TodayRate { get; init; }
            public double? YearRate { get; init; }
            public bool TodayAvailable { get; init; }
            public bool YearAvailable { get; init; }
            public int KlineCount { get; init; }
            public string Message { get; init; } = string.Empty;
            public List<GlobalIndexKlineDto> Klines { get; init; } = new();
            public string? Source { get; set; }
        }

        private sealed class GlobalIndexDebugDto
        {
            public string Name { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public string Secid { get; init; } = string.Empty;
            public string Source { get; init; } = "none";
            public double? Latest { get; init; }
            public double? TodayRate { get; init; }
            public double? YearRate { get; init; }
            public int KlinesCount { get; init; }
            public List<GlobalIndexAttemptDto> Attempts { get; init; } = new();
        }

        private sealed class GlobalIndexAttemptDto
        {
            public string Source { get; init; } = string.Empty;
            public string? Url { get; init; }
            public int Status { get; init; }
            public string? Error { get; init; }
            public int ParsedCount { get; init; }
        }

        private static readonly JsonSerializerOptions GlobalIndicesJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private sealed record PerformanceIndexDefinition(string Key, string Name, string Secid);

        private sealed record PerformanceArchivePoint(DateTime Date, double TotalRate, double Assets, double DailyProfit);

        private sealed record PerformanceIndexClose(DateTime Date, double Close);

        private sealed record PerformanceIndexCloseResult(
            List<PerformanceIndexClose> Closes,
            int RawRowsCount);

        private sealed record PerformanceIndexTick(DateTime Time, double Price);

        private sealed record MarketKlinePoint(DateTime Date, string DateText, double Close, double Rate);

        private sealed record PerformanceFundIntradayPoint(DateTime Time, double Rate, double? ProfitOverride);

        private sealed record PerformanceFundIntradaySeries(double Amount, List<PerformanceFundIntradayPoint> Points);

        private sealed record PerformanceCurvePoint(
            string Time,
            string Date,
            double MyRate,
            double MyProfit,
            double? IndexRate,
            double Assets,
            double DailyProfit,
            double TotalRate);

        private static readonly IReadOnlyDictionary<string, PerformanceIndexDefinition> PerformanceIndexDefinitions =
            new Dictionary<string, PerformanceIndexDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["hs300"] = new("hs300", "沪深300", "1.000300"),
                ["sz50"] = new("sz50", "上证50", "1.000016"),
                ["cyb"] = new("cyb", "创业板", "0.399006"),
                ["kc50"] = new("kc50", "科创50", "1.000688")
            };

        private static DateTime GetPerformanceStartDate(string period, DateTime today)
        {
            return period switch
            {
                "7d" => today.AddDays(-6),
                "1m" => today.AddMonths(-1),
                "3m" => today.AddMonths(-3),
                "1y" => today.AddYears(-1),
                _ => today.AddDays(-6)
            };
        }

        private static string ResolveIndexCode(PerformanceIndexDefinition index)
        {
            var parts = index.Secid.Split('.', 2);
            return parts.Length == 2 ? parts[1] : index.Secid;
        }

        private static string? ResolveTencentSymbolFromSecid(string secid)
        {
            var parts = secid.Split('.', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1])) return null;
            return parts[0] switch
            {
                "1" => "sh" + parts[1],
                "0" => "sz" + parts[1],
                _ => null
            };
        }

        private static string? ResolveSinaCnSymbolFromSecid(string secid)
        {
            var symbol = ResolveTencentSymbolFromSecid(secid);
            return symbol?.StartsWith("sh", StringComparison.OrdinalIgnoreCase) == true ||
                   symbol?.StartsWith("sz", StringComparison.OrdinalIgnoreCase) == true
                ? symbol
                : null;
        }

        [HttpGet("performance-curve")]
        public async Task<IActionResult> GetPerformanceCurve(
            [FromQuery] string username,
            [FromQuery] string period = "today",
            [FromQuery(Name = "index")] string indexKey = "hs300",
            [FromQuery] bool debug = false)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { error = "缺少 username 参数" });
            }

            var normalizedPeriod = period?.Trim().ToLowerInvariant() ?? "today";
            if (normalizedPeriod is not ("today" or "7d" or "1m" or "3m" or "1y"))
            {
                normalizedPeriod = "today";
            }

            var normalizedIndex = indexKey?.Trim().ToLowerInvariant() ?? "hs300";
            if (!PerformanceIndexDefinitions.TryGetValue(normalizedIndex, out var indexDefinition))
            {
                normalizedIndex = "hs300";
                indexDefinition = PerformanceIndexDefinitions[normalizedIndex];
            }

            if (normalizedPeriod == "today")
            {
                return await GetTodayPerformanceCurveAsync(username, normalizedIndex, indexDefinition, debug);
            }

            var today = ChinaNow().Date;
            var startDate = GetPerformanceStartDate(normalizedPeriod, today);
            var endExclusive = today.AddDays(1);

            var archiveRows = await _context.DailyArchives
                .AsNoTracking()
                .Where(a => a.Username == username && a.RecordDate >= startDate && a.RecordDate < endExclusive)
                .ToListAsync();

            var myArchives = BuildPerformanceArchivePoints(archiveRows);
            if (myArchives.Count == 0)
            {
                return Ok(new
                {
                    period = normalizedPeriod,
                    index = normalizedIndex,
                    indexName = indexDefinition.Name,
                    selectedIndex = normalizedIndex,
                    resolvedIndexCode = ResolveIndexCode(indexDefinition),
                    resolvedSecId = indexDefinition.Secid,
                    indexSource = "none",
                    hasMyData = false,
                    indexAvailable = false,
                    indexRawRowsCount = 0,
                    indexParsedRowsCount = 0,
                    pointsWithIndexRate = 0,
                    indexMessage = "暂无历史收益曲线数据，请先保存每日档案",
                    attempts = Array.Empty<ExternalDataAttemptDto>(),
                    myTotalRate = 0d,
                    indexTotalRate = 0d,
                    excessRate = 0d,
                    myTotalProfit = 0d,
                    message = "暂无历史收益曲线数据，请先保存每日档案",
                    points = Array.Empty<PerformanceCurvePoint>()
                });
            }

            var indexCloses = Array.Empty<PerformanceIndexClose>();
            var indexAvailable = false;
            var indexSource = "none";
            var indexRawCount = 0;
            var indexParsedCount = 0;
            var indexError = (string?)null;
            var attempts = new List<ExternalDataAttemptDto>();
            var idxMemFreshKey = $"PerfIdxClose:{indexDefinition.Key}:{startDate:yyyyMMdd}:{today:yyyyMMdd}";
            var idxMemStaleKey = idxMemFreshKey + ":stale";
            var idxRedisFreshKey = $"PerformanceIndexCloses:{indexDefinition.Key}:{normalizedPeriod}:{startDate:yyyyMMdd}:{today:yyyyMMdd}:F";
            var idxRedisStaleKey = $"PerformanceIndexCloses:{indexDefinition.Key}:{normalizedPeriod}:{startDate:yyyyMMdd}:{today:yyyyMMdd}:S";

            // A. IMemoryCache fresh
            if (_cache.TryGetValue<List<PerformanceIndexClose>>(idxMemFreshKey, out var cachedFresh) && cachedFresh!.Count > 0)
            {
                indexCloses = cachedFresh.ToArray();
                indexAvailable = true;
                indexSource = "cache";
                indexParsedCount = indexCloses.Length;
                attempts.Add(new ExternalDataAttemptDto
                {
                    Source = "daily-kline-cache",
                    ParsedRowsCount = indexParsedCount,
                    CacheHit = true,
                    CacheSource = "memory-fresh"
                });
            }

            // B. Redis fresh
            if (!indexAvailable)
            {
                try
                {
                    var db = _redis.GetDatabase();
                    var redisFresh = await db.StringGetAsync(idxRedisFreshKey);
                    if (redisFresh.HasValue)
                    {
                        var deserialized = JsonSerializer.Deserialize<List<PerformanceIndexClose>>(redisFresh.ToString());
                        if (deserialized is { Count: > 0 })
                        {
                            indexCloses = deserialized.ToArray();
                            indexAvailable = true;
                            indexSource = "cache";
                            indexParsedCount = indexCloses.Length;
                            _cache.Set(idxMemFreshKey, deserialized, _historicalKlineFreshTtl);
                            attempts.Add(new ExternalDataAttemptDto
                            {
                                Source = "daily-kline-cache",
                                ParsedRowsCount = indexParsedCount,
                                CacheHit = true,
                                CacheSource = "redis-fresh"
                            });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] redis fresh read failed: {ex.Message}"); }
            }

            // C. kline API
            if (!indexAvailable)
            {
                try
                {
                    var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                    var fetched = await FetchPerformanceIndexClosesAsync(http, indexDefinition, startDate, today, attempts);
                    indexRawCount = fetched.RawRowsCount;
                    indexParsedCount = fetched.Closes.Count;
                    indexCloses = fetched.Closes.ToArray();
                    indexAvailable = indexCloses.Length > 0;
                    indexSource = indexAvailable ? "fresh" : "none";
                    if (indexAvailable)
                    {
                        _cache.Set(idxMemFreshKey, fetched.Closes, _historicalKlineFreshTtl);
                        _cache.Set(idxMemStaleKey, fetched.Closes, TimeSpan.FromDays(30));
                        try
                        {
                            var db = _redis.GetDatabase();
                            var json = JsonSerializer.Serialize(fetched.Closes);
                            await db.StringSetAsync(idxRedisFreshKey, json, _historicalKlineFreshTtl);
                            await db.StringSetAsync(idxRedisStaleKey, json, TimeSpan.FromDays(30));
                        }
                        catch (Exception rex) { Console.WriteLine($"[performance-curve] redis write failed: {rex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    indexError = ex.Message;
                    Console.WriteLine($"[performance-curve] index failed: {indexDefinition.Key} {ex.Message}");
                }
            }

            // D. IMemoryCache stale
            if (!indexAvailable && _cache.TryGetValue<List<PerformanceIndexClose>>(idxMemStaleKey, out var cachedStale) && cachedStale!.Count > 0)
            {
                indexCloses = cachedStale.ToArray();
                indexAvailable = true;
                indexSource = "cache-stale";
                indexParsedCount = indexCloses.Length;
                attempts.Add(new ExternalDataAttemptDto
                {
                    Source = "daily-kline-cache",
                    ParsedRowsCount = indexParsedCount,
                    CacheHit = true,
                    CacheSource = "memory-stale"
                });
            }

            // E. Redis stale
            if (!indexAvailable)
            {
                try
                {
                    var db = _redis.GetDatabase();
                    var redisStale = await db.StringGetAsync(idxRedisStaleKey);
                    if (redisStale.HasValue)
                    {
                        var deserialized = JsonSerializer.Deserialize<List<PerformanceIndexClose>>(redisStale.ToString());
                        if (deserialized is { Count: > 0 })
                        {
                            indexCloses = deserialized.ToArray();
                            indexAvailable = true;
                            indexSource = "cache-stale";
                            indexParsedCount = indexCloses.Length;
                            attempts.Add(new ExternalDataAttemptDto
                            {
                                Source = "daily-kline-cache",
                                ParsedRowsCount = indexParsedCount,
                                CacheHit = true,
                                CacheSource = "redis-stale"
                            });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] redis stale read failed: {ex.Message}"); }
            }

            var indexRatesByDate = indexAvailable
                ? BuildAlignedIndexRates(myArchives.Select(a => a.Date), indexCloses)
                : new Dictionary<DateTime, double?>();

            var baseMyRate = myArchives[0].TotalRate;
            var points = myArchives
                .Select(a =>
                {
                    indexRatesByDate.TryGetValue(a.Date, out var indexRate);
                    return new PerformanceCurvePoint(
                        a.Date.ToString("yyyy-MM-dd"),
                        a.Date.ToString("yyyy-MM-dd"),
                        Math.Round(a.TotalRate - baseMyRate, 2),
                        Math.Round(a.DailyProfit, 2),
                        indexRate.HasValue ? Math.Round(indexRate.Value, 2) : null,
                        Math.Round(a.Assets, 2),
                        Math.Round(a.DailyProfit, 2),
                        Math.Round(a.TotalRate, 2));
                })
                .ToList();

            var myTotalRate = points.Count > 0 ? points[^1].MyRate : 0d;
            var hasIndexRate = points.Any(p => p.IndexRate.HasValue);
            var pointsWithIndexRate = points.Count(p => p.IndexRate.HasValue);
            var indexTotalRate = hasIndexRate
                ? points.Where(p => p.IndexRate.HasValue).Select(p => p.IndexRate!.Value).Last()
                : 0d;
            var indexMessage = hasIndexRate ? indexSource switch
            {
                "cache" => "指数历史K线使用缓存",
                "cache-stale" => "指数历史K线使用过期缓存",
                _ => ""
            } : "指数数据暂不可用";

            Console.WriteLine($"[performance-curve] period={normalizedPeriod} indexSource={indexSource} indexParsedCount={indexParsedCount}");

            return Ok(new
            {
                period = normalizedPeriod,
                index = normalizedIndex,
                indexName = indexDefinition.Name,
                selectedIndex = normalizedIndex,
                resolvedIndexCode = ResolveIndexCode(indexDefinition),
                resolvedSecId = indexDefinition.Secid,
                hasMyData = true,
                indexAvailable = hasIndexRate,
                indexSource,
                indexRawCount,
                indexParsedCount,
                indexRawRowsCount = indexRawCount,
                indexParsedRowsCount = indexParsedCount,
                pointsWithIndexRate,
                indexMessage,
                attempts,
                indexError,
                myTotalRate = Math.Round(myTotalRate, 2),
                indexTotalRate = Math.Round(indexTotalRate, 2),
                excessRate = hasIndexRate ? Math.Round(myTotalRate - indexTotalRate, 2) : 0d,
                myTotalProfit = points.Count > 0 ? points[^1].MyProfit : 0d,
                message = indexMessage,
                points
            });
        }

        private async Task<IActionResult> GetTodayPerformanceCurveAsync(
            string username,
            string normalizedIndex,
            PerformanceIndexDefinition indexDefinition,
            bool debug)
        {
            var localTime = ChinaNow();
            var dateInfo = GetEffectiveFundDateInfo(localTime);
            var today = dateInfo.EffectiveDateStart;
            var todayDash = dateInfo.EffectiveDateText;

            var myFunds = await _context.MyFunds
                .AsNoTracking()
                .Where(f => f.Username == username)
                .ToListAsync();

            var fundCodes = myFunds.Select(f => f.FundCode).ToList();
            if (fundCodes.Count == 0)
            {
                return Ok(new
                {
                    period = "today",
                    index = normalizedIndex,
                    indexName = indexDefinition.Name,
                    selectedIndex = normalizedIndex,
                    resolvedIndexCode = ResolveIndexCode(indexDefinition),
                    resolvedSecId = indexDefinition.Secid,
                    hasMyData = false,
                    indexAvailable = false,
                    indexSource = "none",
                    indexRawRowsCount = 0,
                    indexParsedRowsCount = 0,
                    pointsWithIndexRate = 0,
                    indexMessage = "暂无今日盘中收益数据",
                    attempts = Array.Empty<ExternalDataAttemptDto>(),
                    myTotalRate = 0d,
                    indexTotalRate = 0d,
                    excessRate = 0d,
                    myTotalProfit = 0d,
                    message = "暂无今日盘中收益数据",
                    points = Array.Empty<PerformanceCurvePoint>()
                });
            }

            var effectiveArchiveTotal = await _context.DailyArchives
                .AsNoTracking()
                .Where(a => a.Username == username
                            && a.FundCode == "TOTAL"
                            && a.RecordDate >= dateInfo.EffectiveDateStart
                            && a.RecordDate < dateInfo.EffectiveDateEndExclusive)
                .OrderByDescending(a => a.Id)
                .FirstOrDefaultAsync();

            if (!dateInfo.MarketOpen && effectiveArchiveTotal != null)
            {
                var archivePoint = new PerformanceCurvePoint(
                    $"{todayDash} 15:00",
                    todayDash,
                    Math.Round(effectiveArchiveTotal.DailyRate, 2),
                    Math.Round(effectiveArchiveTotal.DailyProfit, 2),
                    null,
                    Math.Round(effectiveArchiveTotal.Assets, 2),
                    Math.Round(effectiveArchiveTotal.DailyProfit, 2),
                    Math.Round(effectiveArchiveTotal.TotalRate, 2));
                return Ok(new
                {
                    period = "today",
                    index = normalizedIndex,
                    indexName = indexDefinition.Name,
                    selectedIndex = normalizedIndex,
                    resolvedIndexCode = ResolveIndexCode(indexDefinition),
                    resolvedSecId = indexDefinition.Secid,
                    hasMyData = true,
                    indexAvailable = false,
                    indexSource = "none",
                    indexRawRowsCount = 0,
                    indexParsedRowsCount = 0,
                    pointsWithIndexRate = 0,
                    indexMessage = "休市，指数盘中数据暂不可用",
                    attempts = Array.Empty<ExternalDataAttemptDto>(),
                    myTotalRate = Math.Round(effectiveArchiveTotal.DailyRate, 2),
                    indexTotalRate = 0d,
                    excessRate = 0d,
                    myTotalProfit = Math.Round(effectiveArchiveTotal.DailyProfit, 2),
                    marketOpen = dateInfo.MarketOpen,
                    marketStatus = dateInfo.MarketStatus,
                    effectiveDate = todayDash,
                    summarySource = "daily_archive_total",
                    message = "休市，使用最近交易日收益档案",
                    points = new[] { archivePoint }
                });
            }

            var recentStart = today.AddDays(-10);
            var recentRecords = await _context.FundRecords
                .AsNoTracking()
                .Where(r => fundCodes.Contains(r.FundCode) && r.FetchTime >= recentStart)
                .OrderBy(r => r.FetchTime)
                .ToListAsync();

            var todayRecords = recentRecords
                .Where(r => r.FetchTime >= dateInfo.EffectiveDateStart && r.FetchTime < dateInfo.EffectiveDateEndExclusive)
                .ToList();

            var lastRecordDict = recentRecords
                .Where(r => r.FetchTime < dateInfo.EffectiveDateStart)
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var pastActualDict = recentRecords
                .Where(r => r.ActualRate != 0)
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).Take(3).ToList());

            var archiveHistoryDict = await LoadRecentFundArchiveHistoryAsync(
                username,
                fundCodes,
                dateInfo.EffectiveDateStart,
                dateInfo.EffectiveDateEndExclusive);

            var series = BuildTodayPerformanceSeries(myFunds, todayRecords, lastRecordDict, pastActualDict, archiveHistoryDict, today, todayDash);
            var totalPrincipal = series.Sum(s => s.Amount);
            var timeline = series
                .SelectMany(s => s.Points.Select(p => p.Time))
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (series.Count == 0 || totalPrincipal <= 0 || timeline.Count == 0)
            {
                return Ok(new
                {
                    period = "today",
                    index = normalizedIndex,
                    indexName = indexDefinition.Name,
                    selectedIndex = normalizedIndex,
                    resolvedIndexCode = ResolveIndexCode(indexDefinition),
                    resolvedSecId = indexDefinition.Secid,
                    hasMyData = false,
                    indexAvailable = false,
                    indexSource = "none",
                    indexRawRowsCount = 0,
                    indexParsedRowsCount = 0,
                    pointsWithIndexRate = 0,
                    indexMessage = "暂无今日盘中收益数据",
                    attempts = Array.Empty<ExternalDataAttemptDto>(),
                    myTotalRate = 0d,
                    indexTotalRate = 0d,
                    excessRate = 0d,
                    myTotalProfit = 0d,
                    message = "暂无今日盘中收益数据",
                    points = Array.Empty<PerformanceCurvePoint>()
                });
            }

            var indexTicks = Array.Empty<PerformanceIndexTick>();
            var indexAvailable = false;
            var indexSource = "none";
            var indexRawCount = 0;
            var indexParsedCount = 0;
            var indexError = (string?)null;
            var attempts = new List<ExternalDataAttemptDto>();
            double? fallbackIndexRate = null;
            double? preCloseFromTrends2 = null;
            var idxDateStr = today.ToString("yyyyMMdd");
            var idxFreshMemKey = $"PerfIdxTick:{indexDefinition.Key}:{idxDateStr}";
            var idxStaleMemKey = idxFreshMemKey + ":stale";
            var idxFreshPreCloseMemKey = idxFreshMemKey + ":preclose";
            var idxStalePreCloseMemKey = idxStaleMemKey + ":preclose";
            var idxRedisFreshKey = $"PerformanceIndexTicks:{indexDefinition.Key}:{idxDateStr}:F";
            var idxRedisStaleKey = $"PerformanceIndexTicks:{indexDefinition.Key}:{idxDateStr}:S";
            var idxRedisFreshPreCloseKey = idxRedisFreshKey + ":PC";
            var idxRedisStalePreCloseKey = idxRedisStaleKey + ":PC";

            // A. IMemoryCache fresh
            if (_cache.TryGetValue<List<PerformanceIndexTick>>(idxFreshMemKey, out var cachedFreshTicks) && cachedFreshTicks!.Count > 0)
            {
                indexTicks = cachedFreshTicks.ToArray();
                indexAvailable = true;
                indexSource = "cache";
                indexParsedCount = indexTicks.Length;
                if (_cache.TryGetValue<double>(idxFreshPreCloseMemKey, out var cachedPreClose) && cachedPreClose > 0)
                {
                    preCloseFromTrends2 = cachedPreClose;
                }
                attempts.Add(new ExternalDataAttemptDto
                {
                    Source = "trends2-cache",
                    ParsedRowsCount = indexParsedCount,
                    CacheHit = true,
                    CacheSource = "memory-fresh"
                });
            }

            // B. Redis fresh
            if (!indexAvailable)
            {
                try
                {
                    var db = _redis.GetDatabase();
                    var redisFresh = await db.StringGetAsync(idxRedisFreshKey);
                    if (redisFresh.HasValue)
                    {
                        var deserialized = JsonSerializer.Deserialize<List<PerformanceIndexTick>>(redisFresh.ToString());
                        if (deserialized is { Count: > 0 })
                        {
                            indexTicks = deserialized.ToArray();
                            indexAvailable = true;
                            indexSource = "cache";
                            indexParsedCount = indexTicks.Length;
                            _cache.Set(idxFreshMemKey, deserialized, _marketRealtimeFreshTtl);
                            var redisPreClose = await db.StringGetAsync(idxRedisFreshPreCloseKey);
                            if (redisPreClose.HasValue && TryParseInvariantDouble(redisPreClose.ToString(), out var pc) && pc > 0)
                            {
                                preCloseFromTrends2 = pc;
                                _cache.Set(idxFreshPreCloseMemKey, pc, _marketRealtimeFreshTtl);
                            }
                            attempts.Add(new ExternalDataAttemptDto
                            {
                                Source = "trends2-cache",
                                ParsedRowsCount = indexParsedCount,
                                CacheHit = true,
                                CacheSource = "redis-fresh"
                            });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] redis fresh read failed: {ex.Message}"); }
            }

            // C. trends2 API
            if (!indexAvailable)
            {
                try
                {
                    var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                    var trendResult = await FetchPerformanceIndexTicksAsync(http, indexDefinition, today, attempts);
                    indexRawCount = trendResult.RawRowsCount;
                    indexParsedCount = trendResult.Ticks.Count;
                    indexTicks = trendResult.Ticks.ToArray();
                    preCloseFromTrends2 = trendResult.PreClose;
                    indexAvailable = indexTicks.Length > 0;
                    indexSource = indexAvailable ? "trends2" : "none";
                    if (!indexAvailable)
                    {
                        indexError = $"trends2 empty: rawCount={trendResult.Ticks.Count} preClose={trendResult.PreClose}";
                    }
                    if (indexAvailable)
                    {
                        _cache.Set(idxFreshMemKey, trendResult.Ticks, _marketRealtimeFreshTtl);
                        _cache.Set(idxStaleMemKey, trendResult.Ticks, TimeSpan.FromHours(24));
                        if (trendResult.PreClose.HasValue && trendResult.PreClose.Value > 0)
                        {
                            _cache.Set(idxFreshPreCloseMemKey, trendResult.PreClose.Value, _marketRealtimeFreshTtl);
                            _cache.Set(idxStalePreCloseMemKey, trendResult.PreClose.Value, TimeSpan.FromHours(24));
                        }
                        try
                        {
                            var db = _redis.GetDatabase();
                            var json = JsonSerializer.Serialize(trendResult.Ticks);
                            await db.StringSetAsync(idxRedisFreshKey, json, _marketRealtimeFreshTtl);
                            await db.StringSetAsync(idxRedisStaleKey, json, TimeSpan.FromDays(7));
                            if (trendResult.PreClose.HasValue && trendResult.PreClose.Value > 0)
                            {
                                await db.StringSetAsync(idxRedisFreshPreCloseKey, trendResult.PreClose.Value.ToString(CultureInfo.InvariantCulture), _marketRealtimeFreshTtl);
                                await db.StringSetAsync(idxRedisStalePreCloseKey, trendResult.PreClose.Value.ToString(CultureInfo.InvariantCulture), TimeSpan.FromDays(7));
                            }
                        }
                        catch (Exception rex) { Console.WriteLine($"[performance-curve] redis write failed: {rex.Message}"); }
                    }
                }
                catch (Exception ex)
                {
                    indexError = $"{ex.GetType().Name}: {ex.Message} | inner={ex.InnerException?.Message}";
                    Console.WriteLine($"[performance-curve] intraday index failed: {indexDefinition.Key} {ex.GetType().Name}: {ex.Message} | inner={ex.InnerException?.Message}");
                }
            }

            // D. IMemoryCache stale
            if (!indexAvailable && _cache.TryGetValue<List<PerformanceIndexTick>>(idxStaleMemKey, out var cachedStaleTicks) && cachedStaleTicks!.Count > 0)
            {
                indexTicks = cachedStaleTicks.ToArray();
                indexAvailable = true;
                indexSource = "cache-stale";
                indexParsedCount = indexTicks.Length;
                if (_cache.TryGetValue<double>(idxStalePreCloseMemKey, out var cachedStalePreClose) && cachedStalePreClose > 0)
                {
                    preCloseFromTrends2 = cachedStalePreClose;
                }
                attempts.Add(new ExternalDataAttemptDto
                {
                    Source = "trends2-cache",
                    ParsedRowsCount = indexParsedCount,
                    CacheHit = true,
                    CacheSource = "memory-stale"
                });
            }

            // E. Redis stale
            if (!indexAvailable)
            {
                try
                {
                    var db = _redis.GetDatabase();
                    var redisStale = await db.StringGetAsync(idxRedisStaleKey);
                    if (redisStale.HasValue)
                    {
                        var deserialized = JsonSerializer.Deserialize<List<PerformanceIndexTick>>(redisStale.ToString());
                        if (deserialized is { Count: > 0 })
                        {
                            indexTicks = deserialized.ToArray();
                            indexAvailable = true;
                            indexSource = "cache-stale";
                            indexParsedCount = indexTicks.Length;
                            var redisPreClose = await db.StringGetAsync(idxRedisStalePreCloseKey);
                            if (redisPreClose.HasValue && TryParseInvariantDouble(redisPreClose.ToString(), out var pc) && pc > 0)
                            {
                                preCloseFromTrends2 = pc;
                                _cache.Set(idxStalePreCloseMemKey, pc, TimeSpan.FromHours(24));
                            }
                            attempts.Add(new ExternalDataAttemptDto
                            {
                                Source = "trends2-cache",
                                ParsedRowsCount = indexParsedCount,
                                CacheHit = true,
                                CacheSource = "redis-stale"
                            });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] redis stale read failed: {ex.Message}"); }
            }

            // F. Daily kline fallback
            if (!indexAvailable)
            {
                try
                {
                    var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                    var klineResult = await FetchPerformanceIndexClosesAsync(http, indexDefinition, today.AddDays(-10), today, attempts);
                    var klineCloses = klineResult.Closes;
                    if (klineCloses.Count >= 2)
                    {
                        var sorted = klineCloses.OrderBy(c => c.Date).ToList();
                        var prevClose = sorted[^2].Close;
                        var todayClose = sorted[^1].Close;
                        if (prevClose > 0)
                        {
                            fallbackIndexRate = Math.Round((todayClose - prevClose) / prevClose * 100d, 4);
                            indexAvailable = true;
                            indexSource = "daily-kline-fallback";
                            indexRawCount = klineResult.RawRowsCount;
                            indexParsedCount = 1;
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] daily kline fallback failed: {ex.Message}"); }
            }

            // G. Quote fallback
            if (!indexAvailable)
            {
                try
                {
                    var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                    var quoteUrl = $"https://push2.eastmoney.com/api/qt/stock/get?secid={Uri.EscapeDataString(indexDefinition.Secid)}&fields=f43,f60,f170";
                    var quoteResponse = await FetchWithRetryAsync(http, quoteUrl, attempts: attempts, source: "quote");
                    using var quoteDoc = JsonDocument.Parse(quoteResponse);
                    if (quoteDoc.RootElement.TryGetProperty("data", out var qData) && qData.ValueKind != JsonValueKind.Null)
                    {
                        double? qRate = null;
                        if (qData.TryGetProperty("f170", out var qf170) && qf170.ValueKind == JsonValueKind.Number)
                            qRate = Math.Round(qf170.GetDouble(), 4);
                        if (!qRate.HasValue || Math.Abs(qRate.Value) < 0.000001)
                        {
                            if (qData.TryGetProperty("f43", out var qf43) && qf43.ValueKind == JsonValueKind.Number &&
                                qData.TryGetProperty("f60", out var qf60) && qf60.ValueKind == JsonValueKind.Number && qf60.GetDouble() > 0)
                            {
                                qRate = Math.Round((qf43.GetDouble() - qf60.GetDouble()) / qf60.GetDouble() * 100d, 4);
                            }
                        }
                        if (qRate.HasValue)
                        {
                            fallbackIndexRate = qRate;
                            indexAvailable = true;
                            indexSource = "quote-fallback";
                            indexParsedCount = 1;
                            attempts.Add(new ExternalDataAttemptDto
                            {
                                Source = "quote-parse",
                                Url = quoteUrl,
                                StatusCode = 200,
                                ParsedRowsCount = 1
                            });
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] quote fallback failed: {ex.Message}"); }
            }

            // H. Tencent quote fallback
            if (!indexAvailable)
            {
                var tencentSymbol = ResolveTencentSymbolFromSecid(indexDefinition.Secid);
                if (!string.IsNullOrWhiteSpace(tencentSymbol))
                {
                    try
                    {
                        var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                        var quote = await FetchTencentQuoteRateAsync(http, tencentSymbol);
                        attempts.Add(new ExternalDataAttemptDto
                        {
                            Source = "tencent-quote",
                            Url = quote.Url,
                            StatusCode = 200,
                            ParsedRowsCount = quote.Rate.HasValue ? 1 : 0
                        });
                        if (quote.Rate.HasValue)
                        {
                            fallbackIndexRate = quote.Rate;
                            indexAvailable = true;
                            indexSource = "tencent-quote-fallback";
                            indexParsedCount = 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        attempts.Add(new ExternalDataAttemptDto { Source = "tencent-quote", StatusCode = 0, Error = ex.Message });
                        Console.WriteLine($"[performance-curve] tencent quote fallback failed: {ex.Message}");
                    }
                }
            }

            // I. ulist.np batch fallback
            if (!indexAvailable)
            {
                try
                {
                    var http = _httpClientFactory.CreateClient("EastMoneyQuote");
                    var batchSecids = "1.000001,1.000688,0.399006,100.HSI,100.NDX,100.SPX,100.DJIA";
                    var batchUrl = $"https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&secids={batchSecids}&fields=f2,f3,f12,f14";
                    var batchResp = await FetchWithRetryAsync(http, batchUrl, attempts: attempts, source: "ulist-batch");
                        using var batchDoc = JsonDocument.Parse(batchResp);
                        if (batchDoc.RootElement.TryGetProperty("data", out var bData) && bData.ValueKind != JsonValueKind.Null &&
                            bData.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.Array && diff.GetArrayLength() > 0)
                        {
                            var item = diff[0];
                            double bRate = item.TryGetProperty("f3", out var bf3) && bf3.ValueKind == JsonValueKind.Number ? bf3.GetDouble() : 0;
                            double bPrice = item.TryGetProperty("f2", out var bf2) && bf2.ValueKind == JsonValueKind.Number ? bf2.GetDouble() : 0;
                            if (bPrice > 0)
                            {
                                fallbackIndexRate = Math.Round(bRate, 4);
                                indexAvailable = true;
                                indexSource = "ulist-batch-fallback";
                                indexParsedCount = 1;
                                attempts.Add(new ExternalDataAttemptDto
                                {
                                    Source = "ulist-batch-parse",
                                    Url = batchUrl,
                                    StatusCode = 200,
                                    RawRowsCount = diff.GetArrayLength(),
                                    ParsedRowsCount = 1
                                });
                            }
                        }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] ulist.np fallback failed: {ex.Message}"); }
            }

            // J. Global-indices stale cache as last resort (use A-stock index todayRate)
            if (!indexAvailable)
            {
                try
                {
                    var giCode = normalizedIndex switch
                    {
                        "cyb" => "399006",
                        "kc50" => "000688",
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(giCode))
                    {
                        var db = _redis.GetDatabase();
                        var cached = await db.StringGetAsync($"{GlobalIndexCachePrefix}{giCode}:S");
                        if (cached.HasValue)
                        {
                            var gi = JsonSerializer.Deserialize<GlobalIndexDto>(cached.ToString(), GlobalIndicesJsonOptions);
                            if (gi != null && gi.TodayRate.HasValue && gi.Latest.HasValue && gi.Latest.Value > 0)
                            {
                                fallbackIndexRate = Math.Round(gi.TodayRate.Value, 4);
                                indexAvailable = true;
                                indexSource = "global-cache-fallback";
                                indexParsedCount = 1;
                                attempts.Add(new ExternalDataAttemptDto
                                {
                                    Source = "global-index-cache",
                                    ParsedRowsCount = 1,
                                    CacheHit = true,
                                    CacheSource = $"redis-stale:{giCode}"
                                });
                                Console.WriteLine($"[performance-curve] using global-indices cache for {giCode}: rate={gi.TodayRate.Value}");
                            }
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[performance-curve] global-cache fallback failed: {ex.Message}"); }
            }

            Console.WriteLine($"[performance-curve] period=today indexSource={indexSource} indexParsedCount={indexParsedCount}");

            bool hasRealTicks = indexTicks.Length > 0;
            bool isFallbackRate = fallbackIndexRate.HasValue && !hasRealTicks;
            var indexRatesByTime = hasRealTicks
                ? BuildAlignedIntradayIndexRates(timeline, indexTicks, preCloseFromTrends2)
                : new Dictionary<DateTime, double?>();

            var cursors = new int[series.Count];
            Array.Fill(cursors, -1);
            var points = new List<PerformanceCurvePoint>();

            foreach (var time in timeline)
            {
                var totalProfit = 0d;
                for (var i = 0; i < series.Count; i++)
                {
                    var item = series[i];
                    while (cursors[i] + 1 < item.Points.Count && item.Points[cursors[i] + 1].Time <= time)
                    {
                        cursors[i]++;
                    }

                    if (cursors[i] < 0)
                    {
                        continue;
                    }

                    var point = item.Points[cursors[i]];
                    totalProfit += point.ProfitOverride ?? item.Amount * point.Rate / 100d;
                }

                double? idxRate = null;
                if (hasRealTicks)
                {
                    indexRatesByTime.TryGetValue(time, out idxRate);
                }
                var myRate = totalPrincipal > 0 ? totalProfit / totalPrincipal * 100d : 0d;
                points.Add(new PerformanceCurvePoint(
                    time.ToString("yyyy-MM-dd HH:mm"),
                    time.ToString("yyyy-MM-dd"),
                    Math.Round(myRate, 2),
                    Math.Round(totalProfit, 2),
                    idxRate.HasValue ? Math.Round(idxRate.Value, 2) : null,
                    Math.Round(totalPrincipal + totalProfit, 2),
                    Math.Round(totalProfit, 2),
                    Math.Round(myRate, 2)));
            }

            // For fallback rates: only fill the last point's indexRate
            if (isFallbackRate && points.Count > 0)
            {
                var last = points[^1];
                points[^1] = new PerformanceCurvePoint(
                    last.Time, last.Date, last.MyRate, last.MyProfit,
                    Math.Round(fallbackIndexRate.Value, 2),
                    last.Assets, last.DailyProfit, last.TotalRate);
            }

            var compactPoints = points
                .GroupBy(p => p.Time)
                .Select(g => g.Last())
                .ToList();
            var hasIndexRate = compactPoints.Any(p => p.IndexRate.HasValue);
            var pointsWithIndexRate = compactPoints.Count(p => p.IndexRate.HasValue);
            var myTotalRate = compactPoints.Count > 0 ? compactPoints[^1].MyRate : 0d;
            var myTotalProfit = compactPoints.Count > 0 ? compactPoints[^1].MyProfit : 0d;
            var indexTotalRate = hasIndexRate
                ? compactPoints.Where(p => p.IndexRate.HasValue).Select(p => p.IndexRate!.Value).Last()
                : 0d;

            // 用与 /api/fund/today 完全相同的口径重算 summary，覆盖最后一个点
            if (compactPoints.Count > 0 && myFunds.Count > 0)
            {
                double sProfit = 0, sBase = 0, sAssets = 0, sCost = 0, sRealized = 0;
                foreach (var config in myFunds)
                {
                    bool settled = config.LastSettledDate == todayDash;
                    double? previousArchiveAssets = FindPreviousArchiveAssets(config, archiveHistoryDict, dateInfo.EffectiveDateStart);
                    double pendingBuyAmount = ResolvePendingBuyAmount(config, todayDash, previousArchiveAssets);
                    double confirmedHoldAmount = Math.Max(0, Math.Round(config.HoldAmount - pendingBuyAmount, 2));
                    double confirmedCost = Math.Max(0, Math.Round((config.CostAmount > 0 ? config.CostAmount : config.HoldAmount) - pendingBuyAmount, 2));
                    double baseAmt = GetDailyBaseAmount(config, todayDash, pendingBuyAmount);
                    if (baseAmt <= 0) continue;

                    // 与 /api/fund/today 相同的 rate 来源
                    var fundRecords = recentRecords.Where(r => r.FundCode == config.FundCode && r.FetchTime >= today).OrderBy(r => r.FetchTime).ToList();
                    lastRecordDict.TryGetValue(config.FundCode, out var lastRec);
                    var pastRecs = pastActualDict.TryGetValue(config.FundCode, out var pList) ? pList : new();
                    double aDiff = pastRecs.Count > 0 ? Math.Clamp(pastRecs.Average(r => r.ActualRate - r.EstimatedRate), -0.5, 0.5) : 0;
                    double rateSim = settled ? config.LastSettledRate :
                        (fundRecords.Count > 0 ? Math.Round(fundRecords.Last().EstimatedRate + aDiff, 2) :
                         (lastRec != null ? Math.Round(lastRec.EstimatedRate + aDiff, 2) : 0));

                    double profitVal = settled ? config.LastSettledProfit : Math.Round(baseAmt * rateSim / 100.0, 2);
                    sProfit += profitVal;
                    sBase += baseAmt;
                    sAssets += settled ? confirmedHoldAmount : (confirmedHoldAmount + profitVal);
                    sCost += confirmedCost;
                    sRealized += config.RealizedProfit;
                }

                if (sBase > 0)
                {
                    myTotalRate = Math.Round(sProfit / sBase * 100, 2);
                    myTotalProfit = Math.Round(sProfit, 2);
                    // 覆盖最后一个点
                    var lastIdx = compactPoints.Count - 1;
                    var old = compactPoints[lastIdx];
                    compactPoints[lastIdx] = new PerformanceCurvePoint(
                        old.Time, old.Date, myTotalRate, myTotalProfit,
                        old.IndexRate, Math.Round(sAssets, 2), myTotalProfit, myTotalRate);
                }
            }

            var indexMessage = hasIndexRate ? indexSource switch
            {
                "cache" => "指数盘中走势使用缓存",
                "cache-stale" => "指数盘中走势使用过期缓存",
                "daily-kline-fallback" or "quote-fallback" or "ulist-batch-fallback" or "global-cache-fallback" => "指数仅有当前涨跌幅，暂无盘中走势",
                _ => ""
            } : "指数盘中数据暂不可用";

            return Ok(new
            {
                period = "today",
                index = normalizedIndex,
                indexName = indexDefinition.Name,
                selectedIndex = normalizedIndex,
                resolvedIndexCode = ResolveIndexCode(indexDefinition),
                resolvedSecId = indexDefinition.Secid,
                hasMyData = compactPoints.Count > 0,
                indexAvailable = hasIndexRate,
                indexSource,
                indexRawCount,
                indexParsedCount,
                indexRawRowsCount = indexRawCount,
                indexParsedRowsCount = indexParsedCount,
                pointsWithIndexRate,
                indexMessage,
                attempts,
                indexError,
                myTotalRate = Math.Round(myTotalRate, 2),
                indexTotalRate = Math.Round(indexTotalRate, 2),
                excessRate = hasIndexRate ? Math.Round(myTotalRate - indexTotalRate, 2) : 0d,
                myTotalProfit = Math.Round(myTotalProfit, 2),
                indexCurrentRate = isFallbackRate ? Math.Round(fallbackIndexRate!.Value, 2) : (double?)null,
                message = indexMessage,
                points = compactPoints
            });
        }

        private static List<PerformanceFundIntradaySeries> BuildTodayPerformanceSeries(
            List<MyFundConfig> myFunds,
            List<FundData> todayRecords,
            Dictionary<string, FundData> lastRecordDict,
            Dictionary<string, List<FundData>> pastActualDict,
            IReadOnlyDictionary<string, List<DailyArchive>> archiveHistory,
            DateTime today,
            string todayDash)
        {
            var result = new List<PerformanceFundIntradaySeries>();
            foreach (var config in myFunds)
            {
                double? previousArchiveAssets = FindPreviousArchiveAssets(config, archiveHistory, today);
                double pendingBuyAmount = ResolvePendingBuyAmount(config, todayDash, previousArchiveAssets);
                var amount = Math.Max(0d, GetDailyBaseAmount(config, todayDash, pendingBuyAmount));
                if (amount <= 0)
                {
                    continue;
                }

                var fundRecords = todayRecords
                    .Where(r => r.FundCode == config.FundCode)
                    .OrderBy(r => r.FetchTime)
                    .ToList();
                lastRecordDict.TryGetValue(config.FundCode, out var lastRecord);

                var past3DaysRecords = pastActualDict.TryGetValue(config.FundCode, out var pastList)
                    ? pastList
                    : new List<FundData>();

                var avgDiff = 0d;
                if (past3DaysRecords.Count > 0)
                {
                    avgDiff = past3DaysRecords.Average(r => r.ActualRate - r.EstimatedRate);
                    avgDiff = Math.Clamp(avgDiff, -0.5, 0.5);
                }

                var points = new List<PerformanceFundIntradayPoint>();
                if (lastRecord != null)
                {
                    points.Add(new PerformanceFundIntradayPoint(today.AddHours(9).AddMinutes(30), Math.Round(lastRecord.EstimatedRate + avgDiff, 2), null));
                }

                points.AddRange(fundRecords.Select(r =>
                    new PerformanceFundIntradayPoint(r.FetchTime, Math.Round(r.EstimatedRate + avgDiff, 2), null)));

                if (config.LastSettledDate == todayDash)
                {
                    var settledProfit = config.LastSettledProfit;
                    var settledRate = amount > 0
                        ? settledProfit / amount * 100d
                        : config.LastSettledRate;
                    points.Add(new PerformanceFundIntradayPoint(today.AddHours(15), Math.Round(settledRate, 2), Math.Round(settledProfit, 2)));
                }

                if (points.Count == 0)
                {
                    continue;
                }

                var normalizedPoints = points
                    .GroupBy(p => p.Time)
                    .Select(g => g.Last())
                    .OrderBy(p => p.Time)
                    .ToList();
                result.Add(new PerformanceFundIntradaySeries(amount, normalizedPoints));
            }

            return result;
        }

        private sealed record PerformanceIndexTickResult(
            List<PerformanceIndexTick> Ticks,
            double? PreClose,
            int RawRowsCount);

        private static async Task<PerformanceIndexTickResult> FetchPerformanceIndexTicksAsync(
            HttpClient http,
            PerformanceIndexDefinition index,
            DateTime today,
            List<ExternalDataAttemptDto>? attempts = null)
        {
            var fields1 = "f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11";
            var fields2 = "f51,f52,f53,f54,f55,f56,f57,f58";
            var url = $"https://push2his.eastmoney.com/api/qt/stock/trends2/get?secid={Uri.EscapeDataString(index.Secid)}&fields1={fields1}&fields2={fields2}&iscr=0&iscca=0";

            string response;
            try
            {
                response = await FetchWithRetryAsync(http, url, attempts: attempts, source: "trends2");
            }
            catch (Exception ex)
            {
                throw new HttpRequestException($"trends2 failed for {index.Secid}: {ex.Message}", ex);
            }
            Console.WriteLine($"[performance-curve] trends2 response len={response.Length} secid={index.Secid}");
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind == JsonValueKind.Null)
            {
                Console.WriteLine($"[performance-curve] trends2 data=null");
                attempts?.Add(new ExternalDataAttemptDto { Source = "trends2-parse", Url = url, StatusCode = 200, Error = "data=null" });
                return new PerformanceIndexTickResult(new List<PerformanceIndexTick>(), null, 0);
            }
            if (!data.TryGetProperty("trends", out var trends) ||
                trends.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine($"[performance-curve] trends2 trends missing or not array");
                attempts?.Add(new ExternalDataAttemptDto { Source = "trends2-parse", Url = url, StatusCode = 200, Error = "trends missing" });
                return new PerformanceIndexTickResult(new List<PerformanceIndexTick>(), null, 0);
            }
            var rawRowsCount = trends.GetArrayLength();
            Console.WriteLine($"[performance-curve] trends2 trends count={rawRowsCount}");

            double? preClose = null;
            if (data.TryGetProperty("preClose", out var preCloseElem) && preCloseElem.ValueKind == JsonValueKind.Number)
                preClose = preCloseElem.GetDouble();
            if (!preClose.HasValue && data.TryGetProperty("preSettlement", out var preSetElem) && preSetElem.ValueKind == JsonValueKind.Number)
                preClose = preSetElem.GetDouble();

            var result = new List<PerformanceIndexTick>();
            foreach (var trend in trends.EnumerateArray())
            {
                var raw = trend.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var parts = raw.Split(',');
                if (parts.Length < 3 ||
                    !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
                    !TryParseInvariantDouble(parts[2], out var price) ||
                    price <= 0)
                {
                    continue;
                }

                if (time.Date == today)
                {
                    result.Add(new PerformanceIndexTick(time, price));
                }
            }

            var ordered = result.OrderBy(p => p.Time).ToList();
            attempts?.Add(new ExternalDataAttemptDto
            {
                Source = "trends2-parse",
                Url = url,
                StatusCode = 200,
                RawRowsCount = rawRowsCount,
                ParsedRowsCount = ordered.Count
            });

            return new PerformanceIndexTickResult(
                ordered,
                preClose,
                rawRowsCount);
        }

        private sealed class RetryAttemptDto
        {
            public int Attempt { get; init; }
            public int StatusCode { get; init; }
            public string? Error { get; init; }
            public long TimeMs { get; init; }
            public string Source { get; init; } = "http";
        }

        private static async Task<string> FetchWithRetryAsync(
            HttpClient http,
            string url,
            int maxAttempts = 2,
            List<ExternalDataAttemptDto>? attempts = null,
            string source = "http")
        {
            maxAttempts = Math.Clamp(maxAttempts, 1, 2);
            Exception? lastException = null;

            for (int i = 1; i <= maxAttempts; i++)
            {
                if (i > 1) await Task.Delay(350);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    var statusCode = (int)resp.StatusCode;
                    var body = await resp.Content.ReadAsStringAsync(cts.Token);
                    sw.Stop();

                    if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body) && body.Length > 20)
                    {
                        attempts?.Add(new ExternalDataAttemptDto
                        {
                            Source = source,
                            Url = url,
                            StatusCode = statusCode,
                            TimeMs = sw.ElapsedMilliseconds
                        });
                        return body;
                    }

                    var error = resp.IsSuccessStatusCode ? "empty body" : resp.ReasonPhrase;
                    attempts?.Add(new ExternalDataAttemptDto
                    {
                        Source = source,
                        Url = url,
                        StatusCode = statusCode,
                        Error = error,
                        TimeMs = sw.ElapsedMilliseconds
                    });

                    lastException = new HttpRequestException($"{source} returned {statusCode}: {error}");
                    if (!IsRetryableStatus(statusCode))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    sw.Stop();
                    lastException = ex;
                    attempts?.Add(new ExternalDataAttemptDto
                    {
                        Source = source,
                        Url = url,
                        StatusCode = 0,
                        Error = "timeout",
                        TimeMs = sw.ElapsedMilliseconds
                    });
                }
                catch (HttpRequestException ex)
                {
                    sw.Stop();
                    lastException = ex;
                    if (i == maxAttempts)
                    {
                        attempts?.Add(new ExternalDataAttemptDto
                        {
                            Source = source,
                            Url = url,
                            StatusCode = 0,
                            Error = ex.Message,
                            TimeMs = sw.ElapsedMilliseconds
                        });
                    }
                }
            }

            throw new HttpRequestException($"{source}: all attempts failed", lastException);
        }

        private static async Task<(double? Rate, string Url)> FetchTencentQuoteRateAsync(HttpClient http, string symbol)
        {
            var url = $"https://qt.gtimg.cn/q={Uri.EscapeDataString(symbol)}";
            var response = await FetchWithRetryAsync(http, url, source: "tencent-quote");
            var firstQuote = response
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(line => line.Contains($"v_{symbol}=", StringComparison.OrdinalIgnoreCase)) ?? response;
            var start = firstQuote.IndexOf('"');
            var end = firstQuote.LastIndexOf('"');
            if (start < 0 || end <= start) return (null, url);

            var parts = firstQuote.Substring(start + 1, end - start - 1).Split('~');
            if (parts.Length <= 4) return (null, url);
            if (TryParseInvariantDouble(parts[3], out var latest) &&
                TryParseInvariantDouble(parts[4], out var previousClose) &&
                previousClose > 0)
            {
                return (Math.Round((latest - previousClose) / previousClose * 100d, 4), url);
            }

            return (null, url);
        }

        private static bool IsRetryableStatus(int statusCode)
            => statusCode is 0 or 502 or 503 or 504;

        private static async Task<string> CurlFetchAsync(string url)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("curl")
            {
                Arguments = $"-4 -sS --connect-timeout 10 --max-time 20 --http1.1 -H \"Referer: https://quote.eastmoney.com/\" -H \"User-Agent: Mozilla/5.0\" \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) throw new InvalidOperationException("Failed to start curl");
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)) return output;
            throw new HttpRequestException($"curl exit={process.ExitCode}");
        }

        private static Dictionary<DateTime, double?> BuildAlignedIntradayIndexRates(
            IEnumerable<DateTime> targetTimes,
            IReadOnlyList<PerformanceIndexTick> indexTicks,
            double? preClose = null)
        {
            var result = new Dictionary<DateTime, double?>();
            var orderedTargets = targetTimes
                .Distinct()
                .OrderBy(t => t)
                .ToList();
            if (indexTicks.Count == 0 || orderedTargets.Count == 0)
            {
                return result;
            }

            var cursor = 0;
            double? latestPrice = null;
            double basePrice = preClose ?? 0;

            foreach (var targetTime in orderedTargets)
            {
                while (cursor < indexTicks.Count && indexTicks[cursor].Time <= targetTime)
                {
                    latestPrice = indexTicks[cursor].Price;
                    cursor++;
                }

                if (!latestPrice.HasValue)
                {
                    result[targetTime] = null;
                    continue;
                }

                if (basePrice <= 0) basePrice = latestPrice.Value;
                result[targetTime] = basePrice > 0 ? (latestPrice.Value - basePrice) / basePrice * 100d : null;
            }

            return result;
        }

        private static List<PerformanceArchivePoint> BuildPerformanceArchivePoints(List<DailyArchive> archiveRows)
        {
            var totalRows = archiveRows
                .Where(a => string.Equals(a.FundCode, "TOTAL", StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.RecordDate.Date)
                .Select(g => g.OrderByDescending(a => a.Id).First())
                .OrderBy(a => a.RecordDate)
                .Select(a => new PerformanceArchivePoint(a.RecordDate.Date, a.TotalRate, a.Assets, a.DailyProfit))
                .ToList();

            if (totalRows.Count > 0)
            {
                return totalRows;
            }

            // DailyArchives 是收盘后的每日档案；缺失日期不插值，避免制造不存在的组合收益。
            return archiveRows
                .Where(a => !string.Equals(a.FundCode, "TOTAL", StringComparison.OrdinalIgnoreCase))
                .GroupBy(a => a.RecordDate.Date)
                .Select(g =>
                {
                    var assets = g.Sum(a => a.Assets);
                    var totalProfit = g.Sum(a => a.TotalProfit);
                    var cost = assets - totalProfit;
                    var totalRate = cost > 0 ? totalProfit / cost * 100 : 0d;
                    var dailyProfit = g.Sum(a => a.DailyProfit);
                    return new PerformanceArchivePoint(g.Key, totalRate, assets, dailyProfit);
                })
                .OrderBy(a => a.Date)
                .ToList();
        }

        private static async Task<PerformanceIndexCloseResult> FetchPerformanceIndexClosesAsync(
            HttpClient http,
            PerformanceIndexDefinition index,
            DateTime startDate,
            DateTime endDate,
            List<ExternalDataAttemptDto>? attempts = null)
        {
            var limit = Math.Clamp((endDate - startDate).Days + 40, 80, 430);
            var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={Uri.EscapeDataString(index.Secid)}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt={limit}";

            try
            {
                string response = await FetchWithRetryAsync(http, url, attempts: attempts, source: "daily-kline");
                using var doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null ||
                    !data.TryGetProperty("klines", out var klines) || klines.ValueKind != JsonValueKind.Array)
                {
                    attempts?.Add(new ExternalDataAttemptDto { Source = "daily-kline-parse", Url = url, StatusCode = 200, Error = "klines missing" });
                }
                else
                {
                    var result = new List<PerformanceIndexClose>();
                    var rawRowsCount = klines.GetArrayLength();
                    foreach (var kline in klines.EnumerateArray())
                    {
                        var raw = kline.GetString();
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            continue;
                        }

                        var parts = raw.Split(',');
                        if (parts.Length < 3 ||
                            !DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                            !TryParseInvariantDouble(parts[2], out var close))
                        {
                            continue;
                        }

                        var archiveDate = date.Date;
                        if (archiveDate >= startDate && archiveDate <= endDate)
                        {
                            result.Add(new PerformanceIndexClose(archiveDate, close));
                        }
                    }

                    var ordered = result
                        .OrderBy(p => p.Date)
                        .ToList();
                    attempts?.Add(new ExternalDataAttemptDto
                    {
                        Source = "daily-kline-parse",
                        Url = url,
                        StatusCode = 200,
                        RawRowsCount = rawRowsCount,
                        ParsedRowsCount = ordered.Count
                    });
                    if (ordered.Count > 0)
                    {
                        return new PerformanceIndexCloseResult(ordered, rawRowsCount);
                    }
                }
            }
            catch (Exception ex)
            {
                attempts?.Add(new ExternalDataAttemptDto { Source = "daily-kline", Url = url, StatusCode = 0, Error = ex.Message });
            }

            var tencentSymbol = ResolveTencentSymbolFromSecid(index.Secid);
            if (!string.IsNullOrWhiteSpace(tencentSymbol))
            {
                try
                {
                    var tencent = await FetchTencentKlinePointsAsync(http, tencentSymbol, limit);
                    var closes = tencent.Points
                        .Where(p => p.Date >= startDate && p.Date <= endDate)
                        .Select(p => new PerformanceIndexClose(p.Date, p.Close))
                        .OrderBy(p => p.Date)
                        .ToList();
                    attempts?.Add(new ExternalDataAttemptDto
                    {
                        Source = "tencent-daily-kline",
                        Url = tencent.Url,
                        StatusCode = 200,
                        RawRowsCount = tencent.RawRowsCount,
                        ParsedRowsCount = closes.Count
                    });
                    if (closes.Count > 0)
                    {
                        return new PerformanceIndexCloseResult(closes, tencent.RawRowsCount);
                    }
                }
                catch (Exception ex)
                {
                    attempts?.Add(new ExternalDataAttemptDto { Source = "tencent-daily-kline", StatusCode = 0, Error = ex.Message });
                }

                var sinaSymbol = ResolveSinaCnSymbolFromSecid(index.Secid);
                if (!string.IsNullOrWhiteSpace(sinaSymbol))
                {
                    try
                    {
                        var sina = await FetchSinaCnKlinePointsAsync(http, sinaSymbol, limit);
                        var closes = sina.Points
                            .Where(p => p.Date >= startDate && p.Date <= endDate)
                            .Select(p => new PerformanceIndexClose(p.Date, p.Close))
                            .OrderBy(p => p.Date)
                            .ToList();
                        attempts?.Add(new ExternalDataAttemptDto
                        {
                            Source = "sina-cn-daily-kline",
                            Url = sina.Url,
                            StatusCode = 200,
                            RawRowsCount = sina.RawRowsCount,
                            ParsedRowsCount = closes.Count
                        });
                        if (closes.Count > 0)
                        {
                            return new PerformanceIndexCloseResult(closes, sina.RawRowsCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        attempts?.Add(new ExternalDataAttemptDto { Source = "sina-cn-daily-kline", StatusCode = 0, Error = ex.Message });
                    }
                }
            }

            return new PerformanceIndexCloseResult(new List<PerformanceIndexClose>(), 0);
        }

        private static async Task<(List<MarketKlinePoint> Points, int RawRowsCount, string Url)> FetchTencentKlinePointsAsync(HttpClient http, string symbol, int limit)
        {
            var normalizedLimit = Math.Clamp(limit, 20, 430);
            var url = $"https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?param={Uri.EscapeDataString(symbol)},day,,,{normalizedLimit},qfq";
            var response = await FetchWithRetryAsync(http, url, source: "tencent-kline");
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty(symbol, out var symbolNode))
            {
                return (new List<MarketKlinePoint>(), 0, url);
            }

            JsonElement rows;
            if (symbolNode.TryGetProperty("day", out rows) && rows.ValueKind == JsonValueKind.Array)
            {
                return (ParseTencentKlineRows(rows), rows.GetArrayLength(), url);
            }

            if (symbolNode.TryGetProperty("qfqday", out rows) && rows.ValueKind == JsonValueKind.Array)
            {
                return (ParseTencentKlineRows(rows), rows.GetArrayLength(), url);
            }

            return (new List<MarketKlinePoint>(), 0, url);
        }

        private static List<MarketKlinePoint> ParseTencentKlineRows(JsonElement rows)
        {
            var closeRows = new List<(DateTime Date, string DateText, double Close)>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 3) continue;
                var dateText = row[0].GetString() ?? string.Empty;
                var closeText = row[2].GetString() ?? string.Empty;
                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!TryParseInvariantDouble(closeText, out var close) || close <= 0) continue;
                closeRows.Add((date.Date, dateText, close));
            }

            return BuildMarketKlinePoints(closeRows);
        }

        private static async Task<(List<MarketKlinePoint> Points, int RawRowsCount, string Url)> FetchSinaCnKlinePointsAsync(HttpClient http, string symbol, int limit)
        {
            var normalizedLimit = Math.Clamp(limit, 20, 430);
            var url = $"https://quotes.sina.cn/cn/api/json_v2.php/CN_MarketData.getKLineData?symbol={Uri.EscapeDataString(symbol)}&scale=240&ma=no&datalen={normalizedLimit}";
            var response = await FetchWithRetryAsync(http, url, source: "sina-cn-kline");
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (new List<MarketKlinePoint>(), 0, url);
            }

            var closeRows = new List<(DateTime Date, string DateText, double Close)>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var dateText = row.TryGetProperty("day", out var day) ? day.GetString() ?? string.Empty : string.Empty;
                var closeText = row.TryGetProperty("close", out var closeNode) ? closeNode.GetString() ?? string.Empty : string.Empty;
                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!TryParseInvariantDouble(closeText, out var close) || close <= 0) continue;
                closeRows.Add((date.Date, dateText, close));
            }

            return (BuildMarketKlinePoints(closeRows), doc.RootElement.GetArrayLength(), url);
        }

        private static async Task<(List<MarketKlinePoint> Points, int RawRowsCount, string Url)> FetchSinaFuturesKlinePointsAsync(HttpClient http, string symbol)
        {
            var url = $"https://stock2.finance.sina.com.cn/futures/api/jsonp.php/var%20_{Uri.EscapeDataString(symbol)}=/GlobalFuturesService.getGlobalFuturesDailyKLine?symbol={Uri.EscapeDataString(symbol)}";
            var response = await FetchWithRetryAsync(http, url, maxAttempts: 1, source: "sina-futures-kline");
            var start = response.IndexOf('[');
            var end = response.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                return (new List<MarketKlinePoint>(), 0, url);
            }

            var json = response.Substring(start, end - start + 1);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (new List<MarketKlinePoint>(), 0, url);
            }

            var cutoff = ChinaNow().Date.AddYears(-1).AddDays(-10);
            var closeRows = new List<(DateTime Date, string DateText, double Close)>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var dateText = row.TryGetProperty("date", out var dateNode) ? dateNode.GetString() ?? string.Empty : string.Empty;
                var closeText = row.TryGetProperty("close", out var closeNode) ? closeNode.GetString() ?? string.Empty : string.Empty;
                if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (date.Date < cutoff) continue;
                if (!TryParseInvariantDouble(closeText, out var close) || close <= 0) continue;
                closeRows.Add((date.Date, dateText, close));
            }

            return (BuildMarketKlinePoints(closeRows), doc.RootElement.GetArrayLength(), url);
        }

        private static List<MarketKlinePoint> BuildMarketKlinePoints(List<(DateTime Date, string DateText, double Close)> closeRows)
        {
            var ordered = closeRows
                .OrderBy(x => x.Date)
                .ToList();
            var result = new List<MarketKlinePoint>();
            double? prevClose = null;
            foreach (var row in ordered)
            {
                var rate = prevClose.HasValue && prevClose.Value > 0
                    ? Math.Round((row.Close - prevClose.Value) / prevClose.Value * 100d, 2)
                    : 0d;
                result.Add(new MarketKlinePoint(row.Date, row.DateText, row.Close, rate));
                prevClose = row.Close;
            }

            return result;
        }

        private static Dictionary<DateTime, double?> BuildAlignedIndexRates(
            IEnumerable<DateTime> targetDates,
            IReadOnlyList<PerformanceIndexClose> indexCloses)
        {
            var result = new Dictionary<DateTime, double?>();
            var orderedTargets = targetDates
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            if (indexCloses.Count == 0 || orderedTargets.Count == 0)
            {
                return result;
            }

            var cursor = 0;
            double? latestClose = null;
            double? baseClose = null;

            foreach (var targetDate in orderedTargets)
            {
                // 指数周末/节假日没有 K 线时，用最近一个交易日收盘价延续指数线；第一条可对齐数据作为 0 点。
                while (cursor < indexCloses.Count && indexCloses[cursor].Date <= targetDate)
                {
                    latestClose = indexCloses[cursor].Close;
                    cursor++;
                }

                if (!latestClose.HasValue)
                {
                    result[targetDate] = null;
                    continue;
                }

                baseClose ??= latestClose.Value;
                result[targetDate] = baseClose > 0 ? (latestClose.Value - baseClose.Value) / baseClose.Value * 100 : null;
            }

            return result;
        }

        [HttpGet("global-indices")]
        public async Task<IActionResult> GetGlobalIndices([FromQuery] bool force = false, [FromQuery] bool debug = false)
        {
            var definitions = new[]
            {
                new GlobalIndexDefinition { Name = "上证指数", Code = "000001", Market = "A股指数", Secids = new[] { "1.000001" } },
                new GlobalIndexDefinition { Name = "科创50", Code = "000688", Market = "A股指数", Secids = new[] { "1.000688" } },
                new GlobalIndexDefinition { Name = "创业板指", Code = "399006", Market = "A股指数", Secids = new[] { "0.399006" } },
                new GlobalIndexDefinition { Name = "恒生指数", Code = "HSI", Market = "港股指数", Secids = new[] { "100.HSI", "124.HSI" }, SinaFuturesSymbol = "HSI" },
                new GlobalIndexDefinition { Name = "纳斯达克", Code = "NDX", Market = "美股指数", Secids = new[] { "100.NDX", "105.IXIC" }, SinaFuturesSymbol = "NQ" },
                new GlobalIndexDefinition { Name = "标普500", Code = "SPX", Market = "美股指数", Secids = new[] { "100.SPX", "109.SPX" }, SinaFuturesSymbol = "ES" },
                new GlobalIndexDefinition { Name = "道琼斯", Code = "DJIA", Market = "美股指数", Secids = new[] { "100.DJIA" }, SinaFuturesSymbol = "YM" }
            };

            // Non-debug, non-force: try combined cache first
            if (!debug && !force)
            {
                string? legacyCache = await TryReadLegacyGlobalIndicesCacheAsync();
                if (!string.IsNullOrWhiteSpace(legacyCache))
                {
                    var cached = JsonSerializer.Deserialize<List<GlobalIndexDto>>(legacyCache, GlobalIndicesJsonOptions);
                    if (cached != null)
                    {
                        Response.Headers["X-App-Cache"] = "redis";
                        return Ok(cached.Select(NormalizeGlobalIndexAvailability).ToList());
                    }
                }

                var (dbCached, dbSource) = await _marketCache.TryGetAsync<List<GlobalIndexDto>>("global_indices_1y_v2");
                if (dbCached != null && dbSource != null && dbCached.Count >= 5)
                {
                    Response.Headers["X-App-Cache"] = dbSource;
                    Response.Headers["X-Cache-Source"] = dbSource;
                    return Ok(dbCached.Select(NormalizeGlobalIndexAvailability).ToList());
                }
            }

            var http = _httpClientFactory.CreateClient("EastMoneyQuote");
            var debugList = debug ? new List<GlobalIndexDebugDto>() : null;
            var results = new List<GlobalIndexDto>();

            var fetchTasks = definitions
                .Select(idx => FetchGlobalIndexWithCacheAsync(http, idx, force, debug))
                .ToArray();
            var fetchedResults = await Task.WhenAll(fetchTasks);
            foreach (var item in fetchedResults)
            {
                results.Add(item.result);
                if (item.debug != null) debugList!.Add(item.debug);
            }

            // Per-index stale merge: fill missing data from per-index stale cache or legacy combined cache
            List<GlobalIndexDto>? legacyCacheData = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Latest.HasValue && results[i].Latest.Value > 0) continue;

                // D. per-index stale cache
                var stale = await ReadPerIndexStaleCacheAsync(definitions[i].Code);
                if (stale != null && stale.Latest.HasValue && stale.Latest.Value > 0)
                {
                    stale.Source = "stale";
                    results[i] = stale;
                    if (debugList != null && i < debugList.Count)
                        debugList[i] = new GlobalIndexDebugDto
                        {
                            Name = definitions[i].Name, Code = definitions[i].Code,
                            Secid = stale.Secid, Source = "stale",
                            Latest = stale.Latest, TodayRate = stale.TodayRate,
                            YearRate = stale.YearRate, KlinesCount = stale.Klines.Count,
                            Attempts = debugList[i].Attempts
                        };
                    continue;
                }

                // E. legacy combined cache
                legacyCacheData ??= await TryReadLegacyGlobalIndicesCacheListAsync();
                if (legacyCacheData != null)
                {
                    var legacyItem = legacyCacheData.FirstOrDefault(x => x.Code == definitions[i].Code);
                    if (legacyItem != null && legacyItem.Latest.HasValue && legacyItem.Latest.Value > 0)
                    {
                        legacyItem.Source = "legacy-cache";
                        results[i] = legacyItem;
                        if (debugList != null && i < debugList.Count)
                            debugList[i] = new GlobalIndexDebugDto
                            {
                                Name = definitions[i].Name, Code = definitions[i].Code,
                                Secid = legacyItem.Secid, Source = "legacy-cache",
                                Latest = legacyItem.Latest, TodayRate = legacyItem.TodayRate,
                                YearRate = legacyItem.YearRate, KlinesCount = legacyItem.Klines.Count,
                                Attempts = debugList[i].Attempts
                            };
                    }
                }
            }

            // F. ulist.np batch fallback for any still-missing indices
            int missingCount = results.Count(x => !x.Latest.HasValue || x.Latest.Value <= 0);
            if (missingCount > 0)
            {
                try
                {
                    var missingSecids = definitions
                        .Where((d, i) => !results[i].Latest.HasValue || results[i].Latest.Value <= 0)
                        .Select(d => d.Secids[0])
                        .ToList();
                    var batchUrl = $"https://push2.eastmoney.com/api/qt/ulist.np/get?fltt=2&secids={Uri.EscapeDataString(string.Join(",", missingSecids))}&fields=f2,f3,f12,f14";
                    using var bcts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    var batchResp = await http.GetStringAsync(batchUrl, bcts.Token);
                    using var batchDoc = JsonDocument.Parse(batchResp);
                    if (batchDoc.RootElement.TryGetProperty("data", out var bData) && bData.ValueKind != JsonValueKind.Null &&
                        bData.TryGetProperty("diff", out var diff) && diff.ValueKind == JsonValueKind.Array)
                    {
                        var batchMap = new Dictionary<string, (double price, double rate)>();
                        foreach (var item in diff.EnumerateArray())
                        {
                            var code = item.TryGetProperty("f12", out var f12) ? f12.GetString() : null;
                            double price = item.TryGetProperty("f2", out var f2) && f2.ValueKind == JsonValueKind.Number ? f2.GetDouble() : 0;
                            double rate = item.TryGetProperty("f3", out var f3) && f3.ValueKind == JsonValueKind.Number ? f3.GetDouble() : 0;
                            if (!string.IsNullOrEmpty(code) && price > 0) batchMap[code] = (price, rate);
                        }
                        for (int i = 0; i < results.Count; i++)
                        {
                            if (results[i].Latest.HasValue && results[i].Latest.Value > 0) continue;
                            if (batchMap.TryGetValue(definitions[i].Code, out var bm))
                            {
                                // Merge with stale klines if available
                                var existingStale = await ReadPerIndexStaleCacheAsync(definitions[i].Code);
                                var batchResult = new GlobalIndexDto
                                {
                                    Name = definitions[i].Name, Code = definitions[i].Code,
                                    Market = definitions[i].Market, Secid = definitions[i].Secids[0],
                                    Latest = Math.Round(bm.price, 2), Point = Math.Round(bm.price, 2), Close = Math.Round(bm.price, 2),
                                    TodayRate = Math.Round(bm.rate, 2),
                                    YearRate = existingStale?.YearRate,
                                    TodayAvailable = true,
                                    YearAvailable = existingStale?.YearRate.HasValue == true,
                                    KlineCount = existingStale?.Klines.Count ?? 0,
                                    Message = existingStale?.YearRate.HasValue == true ? string.Empty : "一年K线不可用",
                                    Klines = existingStale?.Klines ?? new List<GlobalIndexKlineDto>(),
                                    Source = "ulist-batch"
                                };
                                results[i] = batchResult;
                                await WritePerIndexCacheAsync(definitions[i].Code, batchResult);
                                if (debugList != null && i < debugList.Count)
                                    debugList[i] = new GlobalIndexDebugDto
                                    {
                                        Name = definitions[i].Name, Code = definitions[i].Code,
                                        Secid = definitions[i].Secids[0], Source = "ulist-batch",
                                        Latest = batchResult.Latest, TodayRate = batchResult.TodayRate,
                                        YearRate = batchResult.YearRate, KlinesCount = batchResult.Klines.Count,
                                        Attempts = debugList[i].Attempts
                                    };
                            }
                        }
                        Console.WriteLine($"[global-indices] ulist.np batch filled {batchMap.Count}/{missingSecids.Count}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[global-indices] ulist.np batch failed: {ex.Message}");
                }
            }

            results = results
                .Select(NormalizeGlobalIndexAvailability)
                .ToList();

            // Write combined cache if enough valid results
            int validCount = results.Count(x => x.Latest.HasValue && x.Latest.Value > 0);
            if (validCount >= 5)
            {
                await TryWriteLegacyGlobalIndicesCacheAsync(results);
                try
                {
                    await _marketCache.SetAsync("global_indices_1y_v2", results, _marketRealtimeFreshTtl, TimeSpan.FromDays(7), "build");
                }
                catch { }
            }
            else if (validCount < 3)
            {
                // Not enough fresh data - try DB stale fallback
                var (staleData, _) = await _marketCache.TryGetStaleAsync<List<GlobalIndexDto>>("global_indices_1y_v2", TimeSpan.FromDays(7));
                if (staleData != null && staleData.Count >= 5)
                {
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (results[i].Latest.HasValue && results[i].Latest.Value > 0) continue;
                        var staleItem = staleData.FirstOrDefault(x => x.Code == definitions[i].Code);
                        if (staleItem != null && staleItem.Latest.HasValue && staleItem.Latest.Value > 0)
                        {
                            staleItem.Source = "db-stale";
                            results[i] = staleItem;
                        }
                    }
                }
            }

            results = results.Select(NormalizeGlobalIndexAvailability).ToList();

            if (debug)
            {
                return Ok(new
                {
                    indices = results,
                    debug = debugList,
                    validCount,
                    cacheSource = validCount >= 5 ? "build" : "db-stale-fallback",
                    returnedCount = results.Count(x => x.Latest.HasValue && x.Latest.Value > 0)
                });
            }

            Response.Headers["X-App-Cache"] = validCount >= 7 ? "build" : "partial";
            Response.Headers["X-Cache-Source"] = validCount >= 5 ? "build" : "stale-fallback";
            return Ok(results);
        }

        private async Task<(GlobalIndexDto result, GlobalIndexDebugDto? debug)> FetchGlobalIndexWithCacheAsync(
            HttpClient http, GlobalIndexDefinition idx, bool force, bool debug)
        {
            var attempts = debug ? new List<GlobalIndexAttemptDto>() : null;

            // 1. Try per-index fresh cache (skip if force=true)
            if (!force)
            {
                var fresh = await ReadPerIndexFreshCacheAsync(idx.Code);
                if (fresh != null && fresh.Latest.HasValue && fresh.Latest.Value > 0)
                {
                    fresh.Source = "fresh";
                    return (fresh, debug ? new GlobalIndexDebugDto
                    {
                        Name = idx.Name, Code = idx.Code,
                        Secid = fresh.Secid, Source = "fresh",
                        Latest = fresh.Latest, TodayRate = fresh.TodayRate,
                        YearRate = fresh.YearRate, KlinesCount = fresh.Klines.Count,
                        Attempts = attempts!
                    } : null);
                }
            }

            // 2. Try kline API
            var (klineResult, klineAttempts) = await FetchGlobalIndexKlineAsync(http, idx);
            attempts?.AddRange(klineAttempts);

            if (klineResult != null && klineResult.Latest.HasValue && klineResult.Latest.Value > 0)
            {
                klineResult.Source ??= "fresh";
                await WritePerIndexCacheAsync(idx.Code, klineResult);
                return (klineResult, debug ? new GlobalIndexDebugDto
                {
                    Name = idx.Name, Code = idx.Code,
                    Secid = klineResult.Secid, Source = klineResult.Source ?? "fresh",
                    Latest = klineResult.Latest, TodayRate = klineResult.TodayRate,
                    YearRate = klineResult.YearRate, KlinesCount = klineResult.Klines.Count,
                    Attempts = attempts!
                } : null);
            }

            // 3. Try quote API fallback
            var (quoteResult, quoteAttempts) = await FetchGlobalIndexQuoteAsync(http, idx);
            attempts?.AddRange(quoteAttempts);

            if (quoteResult != null && quoteResult.Latest.HasValue && quoteResult.Latest.Value > 0)
            {
                var stale = await ReadPerIndexStaleCacheAsync(idx.Code);
                if (stale != null && stale.Klines.Count > 0)
                {
                    quoteResult = new GlobalIndexDto
                    {
                        Name = quoteResult.Name, Code = quoteResult.Code,
                        Market = quoteResult.Market, Secid = quoteResult.Secid,
                        Latest = quoteResult.Latest, Point = quoteResult.Point,
                        Close = quoteResult.Close, TodayRate = quoteResult.TodayRate,
                        YearRate = stale.YearRate ?? quoteResult.YearRate,
                        TodayAvailable = quoteResult.TodayRate.HasValue,
                        YearAvailable = stale.YearRate.HasValue || quoteResult.YearRate.HasValue,
                        KlineCount = stale.Klines.Count,
                        Message = stale.YearRate.HasValue || quoteResult.YearRate.HasValue ? string.Empty : "一年K线不可用",
                        Klines = stale.Klines, Source = quoteResult.Source
                    };
                }
                quoteResult.Source = "partial";
                await WritePerIndexCacheAsync(idx.Code, quoteResult);
                return (quoteResult, debug ? new GlobalIndexDebugDto
                {
                    Name = idx.Name, Code = idx.Code,
                    Secid = quoteResult.Secid, Source = "partial",
                    Latest = quoteResult.Latest, TodayRate = quoteResult.TodayRate,
                    YearRate = quoteResult.YearRate, KlinesCount = quoteResult.Klines.Count,
                    Attempts = attempts!
                } : null);
            }

            // 4. All failed
            var empty = new GlobalIndexDto
            {
                Name = idx.Name, Code = idx.Code, Market = idx.Market,
                Secid = idx.Secids.FirstOrDefault() ?? string.Empty,
                TodayAvailable = false,
                YearAvailable = false,
                KlineCount = 0,
                Message = "指数数据暂不可用",
                Source = "none"
            };
            return (empty, debug ? new GlobalIndexDebugDto
            {
                Name = idx.Name, Code = idx.Code,
                Secid = empty.Secid, Source = "none",
                Attempts = attempts!
            } : null);
        }

        private static GlobalIndexDto NormalizeGlobalIndexAvailability(GlobalIndexDto item)
        {
            var klineCount = item.KlineCount > 0 ? item.KlineCount : item.Klines.Count;
            var todayAvailable = item.TodayAvailable || item.TodayRate.HasValue;
            var yearRate = item.YearRate.HasValue && double.IsFinite(item.YearRate.Value) && klineCount >= 2
                ? item.YearRate
                : null;
            var yearAvailable = yearRate.HasValue;
            var message = item.Message;
            if (!yearAvailable && string.IsNullOrWhiteSpace(message) && item.Latest.HasValue)
            {
                message = "缺少一年K线基准";
            }
            var klines = (item.Klines ?? new List<GlobalIndexKlineDto>())
                .OrderByDescending(x => DateTime.TryParse(x.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue)
                .ToList();

            return new GlobalIndexDto
            {
                Name = item.Name,
                Code = item.Code,
                Market = item.Market,
                Secid = item.Secid,
                Latest = item.Latest,
                Point = item.Point,
                Close = item.Close,
                TodayRate = item.TodayRate,
                YearRate = yearRate,
                TodayAvailable = todayAvailable,
                YearAvailable = yearAvailable,
                KlineCount = klineCount,
                Message = message,
                Klines = klines,
                Source = item.Source
            };
        }

        private async Task<(GlobalIndexDto? result, List<GlobalIndexAttemptDto> attempts)> FetchGlobalIndexKlineAsync(
            HttpClient http, GlobalIndexDefinition idx)
        {
            var attempts = new List<GlobalIndexAttemptDto>();
            foreach (string secid in idx.Secids)
            {
                string url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={Uri.EscapeDataString(secid)}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=370";
                try
                {
                    string response = await FetchWithRetryAsync(http, url);
                    using var doc = JsonDocument.Parse(response);

                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 200, Error = "data=null" });
                        continue;
                    }

                    if (!data.TryGetProperty("klines", out var klines) || klines.ValueKind != JsonValueKind.Array || klines.GetArrayLength() == 0)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 200, Error = "klines empty" });
                        continue;
                    }

                    var parsedKlines = new List<(DateTime Date, string DateText, double Close, double Rate)>();
                    foreach (var kline in klines.EnumerateArray())
                    {
                        string? raw = kline.GetString();
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var parts = raw.Split(',');
                        if (parts.Length <= 8) continue;
                        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var klineDate)) continue;
                        if (!TryParseInvariantDouble(parts[2], out double close)) continue;
                        if (!TryParseInvariantDouble(parts[8], out double rate)) rate = 0;
                        parsedKlines.Add((klineDate.Date, parts[0], close, Math.Round(rate, 2)));
                    }

                    if (parsedKlines.Count == 0)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 200, Error = "parse empty" });
                        continue;
                    }

                    parsedKlines = parsedKlines
                        .OrderBy(x => x.Date)
                        .ToList();

                    double latestClose = Math.Round(parsedKlines[^1].Close, 2);
                    if (latestClose <= 0)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 200, Error = $"latest={latestClose}" });
                        continue;
                    }

                    var latestDate = parsedKlines[^1].Date;
                    var yearAgoTarget = latestDate.AddYears(-1);
                    var yearAgoPoint = parsedKlines
                        .Where(x => x.Date <= yearAgoTarget && x.Close > 0)
                        .OrderByDescending(x => x.Date)
                        .FirstOrDefault();
                    if (yearAgoPoint.Close <= 0)
                    {
                        yearAgoPoint = parsedKlines
                            .Where(x => x.Date >= yearAgoTarget && x.Close > 0)
                            .OrderBy(x => x.Date)
                            .FirstOrDefault();
                    }
                    double? yearRate = yearAgoPoint.Close > 0 && yearAgoPoint.Date < latestDate
                        ? Math.Round((latestClose - yearAgoPoint.Close) / yearAgoPoint.Close * 100, 2)
                        : null;
                    double todayRate = parsedKlines[^1].Rate;

                    attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 200, ParsedCount = parsedKlines.Count });
                    return (new GlobalIndexDto
                    {
                        Name = idx.Name, Code = idx.Code, Market = idx.Market, Secid = secid,
                        Latest = latestClose, Point = latestClose, Close = latestClose,
                        TodayRate = todayRate, YearRate = yearRate,
                        TodayAvailable = true,
                        YearAvailable = yearRate.HasValue,
                        KlineCount = parsedKlines.Count,
                        Message = yearRate.HasValue ? string.Empty : "缺少一年K线基准",
                        Klines = parsedKlines
                            .OrderByDescending(x => x.Date)
                            .Select(x => new GlobalIndexKlineDto { Date = x.DateText, Rate = x.Rate })
                            .ToList()
                    }, attempts);
                }
                catch (OperationCanceledException)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 0, Error = "timeout" });
                }
                catch (Exception ex)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "kline", Url = url, Status = 0, Error = ex.Message });
                }
            }

            var tencentSymbol = idx.Secids
                .Select(ResolveTencentSymbolFromSecid)
                .FirstOrDefault(symbol => !string.IsNullOrWhiteSpace(symbol));
            if (!string.IsNullOrWhiteSpace(tencentSymbol))
            {
                try
                {
                    var tencent = await FetchTencentKlinePointsAsync(http, tencentSymbol, 370);
                    var result = BuildGlobalIndexDtoFromKlinePoints(idx, tencent.Points, tencentSymbol, "tencent-kline", "腾讯日K");
                    attempts.Add(new GlobalIndexAttemptDto { Source = "tencent-kline", Url = tencent.Url, Status = 200, ParsedCount = tencent.Points.Count });
                    if (result != null) return (result, attempts);
                }
                catch (Exception ex)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "tencent-kline", Status = 0, Error = ex.Message });
                }

                var sinaSymbol = idx.Secids
                    .Select(ResolveSinaCnSymbolFromSecid)
                    .FirstOrDefault(symbol => !string.IsNullOrWhiteSpace(symbol));
                if (!string.IsNullOrWhiteSpace(sinaSymbol))
                {
                    try
                    {
                        var sina = await FetchSinaCnKlinePointsAsync(http, sinaSymbol, 370);
                        var result = BuildGlobalIndexDtoFromKlinePoints(idx, sina.Points, sinaSymbol, "sina-cn-kline", "新浪日K");
                        attempts.Add(new GlobalIndexAttemptDto { Source = "sina-cn-kline", Url = sina.Url, Status = 200, ParsedCount = sina.Points.Count });
                        if (result != null) return (result, attempts);
                    }
                    catch (Exception ex)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "sina-cn-kline", Status = 0, Error = ex.Message });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(idx.SinaFuturesSymbol))
            {
                try
                {
                    var sinaFutures = await FetchSinaFuturesKlinePointsAsync(http, idx.SinaFuturesSymbol);
                    var result = BuildGlobalIndexDtoFromKlinePoints(idx, sinaFutures.Points, idx.Secids.FirstOrDefault() ?? idx.Code, "sina-futures-kline", "新浪期货日K");
                    attempts.Add(new GlobalIndexAttemptDto { Source = "sina-futures-kline", Url = sinaFutures.Url, Status = 200, ParsedCount = sinaFutures.Points.Count });
                    if (result != null) return (result, attempts);
                }
                catch (Exception ex)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "sina-futures-kline", Status = 0, Error = ex.Message });
                }
            }

            return (null, attempts);
        }

        private static GlobalIndexDto? BuildGlobalIndexDtoFromKlinePoints(
            GlobalIndexDefinition idx,
            List<MarketKlinePoint> points,
            string secid,
            string source,
            string sourceLabel)
        {
            var ordered = points
                .Where(x => x.Close > 0)
                .OrderBy(x => x.Date)
                .ToList();
            if (ordered.Count == 0) return null;

            var latest = ordered[^1];
            var yearAgoTarget = latest.Date.AddYears(-1);
            var yearAgoPoint = ordered
                .Where(x => x.Date <= yearAgoTarget && x.Close > 0)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault();
            if (yearAgoPoint == null)
            {
                yearAgoPoint = ordered
                    .Where(x => x.Date >= yearAgoTarget && x.Close > 0)
                    .OrderBy(x => x.Date)
                    .FirstOrDefault();
            }

            double? yearRate = yearAgoPoint != null && yearAgoPoint.Close > 0 && yearAgoPoint.Date < latest.Date
                ? Math.Round((latest.Close - yearAgoPoint.Close) / yearAgoPoint.Close * 100d, 2)
                : null;

            return new GlobalIndexDto
            {
                Name = idx.Name,
                Code = idx.Code,
                Market = idx.Market,
                Secid = secid,
                Latest = Math.Round(latest.Close, 2),
                Point = Math.Round(latest.Close, 2),
                Close = Math.Round(latest.Close, 2),
                TodayRate = Math.Round(latest.Rate, 2),
                YearRate = yearRate,
                TodayAvailable = true,
                YearAvailable = yearRate.HasValue,
                KlineCount = ordered.Count,
                Message = yearRate.HasValue ? string.Empty : $"{sourceLabel}缺少一年K线基准",
                Klines = ordered
                    .OrderByDescending(x => x.Date)
                    .Select(x => new GlobalIndexKlineDto { Date = x.DateText, Rate = x.Rate })
                    .ToList(),
                Source = source
            };
        }

        private async Task<(GlobalIndexDto? result, List<GlobalIndexAttemptDto> attempts)> FetchGlobalIndexQuoteAsync(
            HttpClient http, GlobalIndexDefinition idx)
        {
            var attempts = new List<GlobalIndexAttemptDto>();
            foreach (string secid in idx.Secids)
            {
                string url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={Uri.EscapeDataString(secid)}&fields=f43,f44,f45,f46,f60,f170,f171";
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    string response = await http.GetStringAsync(url, cts.Token);
                    using var doc = JsonDocument.Parse(response);

                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "quote", Url = url, Status = 200, Error = "data=null" });
                        continue;
                    }

                    double? latest = null;
                    double? todayRate = null;
                    if (data.TryGetProperty("f43", out var f43) && f43.ValueKind == JsonValueKind.Number && f43.GetDouble() > 0)
                        latest = Math.Round(f43.GetDouble(), 2);
                    if (data.TryGetProperty("f170", out var f170) && f170.ValueKind == JsonValueKind.Number)
                        todayRate = Math.Round(f170.GetDouble(), 2);

                    // f43 fallback: try f46 (open), f44 (high), f45 (low)
                    if (!latest.HasValue || latest.Value <= 0)
                    {
                        foreach (var fbField in new[] { "f46", "f44", "f45" })
                        {
                            if (data.TryGetProperty(fbField, out var fb) && fb.ValueKind == JsonValueKind.Number && fb.GetDouble() > 0)
                            {
                                latest = Math.Round(fb.GetDouble(), 2);
                                break;
                            }
                        }
                    }

                    // Calculate from previous close + change rate if still missing
                    if ((!latest.HasValue || latest.Value <= 0) && todayRate.HasValue && data.TryGetProperty("f60", out var f60) && f60.ValueKind == JsonValueKind.Number && f60.GetDouble() > 0)
                    {
                        latest = Math.Round(f60.GetDouble() * (1 + todayRate.Value / 100d), 2);
                    }

                    if (latest.HasValue && latest.Value > 0)
                    {
                        attempts.Add(new GlobalIndexAttemptDto { Source = "quote", Url = url, Status = 200, ParsedCount = 1 });
                        return (new GlobalIndexDto
                        {
                            Name = idx.Name, Code = idx.Code, Market = idx.Market, Secid = secid,
                            Latest = latest, Point = latest, Close = latest,
                            TodayRate = todayRate, YearRate = null,
                            TodayAvailable = todayRate.HasValue,
                            YearAvailable = false,
                            KlineCount = 0,
                            Message = "一年K线不可用",
                            Klines = new List<GlobalIndexKlineDto>()
                        }, attempts);
                    }
                    attempts.Add(new GlobalIndexAttemptDto { Source = "quote", Url = url, Status = 200, Error = $"latest={latest} todayRate={todayRate}" });
                }
                catch (OperationCanceledException)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "quote", Url = url, Status = 0, Error = "timeout" });
                }
                catch (Exception ex)
                {
                    attempts.Add(new GlobalIndexAttemptDto { Source = "quote", Url = url, Status = 0, Error = ex.Message });
                }
            }
            return (null, attempts);
        }

        private async Task<string?> TryReadLegacyGlobalIndicesCacheAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync(GlobalIndicesCacheKey);
                if (!cached.HasValue) return null;
                string json = cached.ToString();
                var result = JsonSerializer.Deserialize<List<GlobalIndexDto>>(json, GlobalIndicesJsonOptions);
                if (result == null) return null;
                int validCount = result.Count(x => x.Latest.HasValue && x.Latest.Value > 0);
                if (validCount < 5)
                {
                    Console.WriteLine($"[global-indices] redis cache ignored: only {validCount}/7 valid");
                    return null;
                }
                return json;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[global-indices] redis read failed: {ex.Message}");
                return null;
            }
        }

        private async Task<List<GlobalIndexDto>?> TryReadLegacyGlobalIndicesCacheListAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync(GlobalIndicesCacheKey);
                if (!cached.HasValue) return null;
                return JsonSerializer.Deserialize<List<GlobalIndexDto>>(cached.ToString(), GlobalIndicesJsonOptions);
            }
            catch { return null; }
        }

        private async Task TryWriteLegacyGlobalIndicesCacheAsync(List<GlobalIndexDto> result)
        {
            try
            {
                var db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(result, GlobalIndicesJsonOptions);
                await db.StringSetAsync(GlobalIndicesCacheKey, json, _marketRealtimeFreshTtl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[global-indices] redis write failed: {ex.Message}");
            }
        }

        private async Task<GlobalIndexDto?> ReadPerIndexFreshCacheAsync(string code)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync($"{GlobalIndexCachePrefix}{code}:F");
                if (!cached.HasValue) return null;
                return JsonSerializer.Deserialize<GlobalIndexDto>(cached.ToString(), GlobalIndicesJsonOptions);
            }
            catch { return null; }
        }

        private async Task<GlobalIndexDto?> ReadPerIndexStaleCacheAsync(string code)
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync($"{GlobalIndexCachePrefix}{code}:S");
                if (!cached.HasValue) return null;
                return JsonSerializer.Deserialize<GlobalIndexDto>(cached.ToString(), GlobalIndicesJsonOptions);
            }
            catch { return null; }
        }

        private async Task WritePerIndexCacheAsync(string code, GlobalIndexDto result)
        {
            try
            {
                var db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(result, GlobalIndicesJsonOptions);
                await db.StringSetAsync($"{GlobalIndexCachePrefix}{code}:F", json, _marketRealtimeFreshTtl);
                await db.StringSetAsync($"{GlobalIndexCachePrefix}{code}:S", json, TimeSpan.FromDays(7));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[global-indices] per-index cache write failed: {ex.Message}");
            }
        }

        private async Task<string?> TryReadGlobalIndicesCacheAsync()
        {
            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync(GlobalIndicesCacheKey);
                if (!cached.HasValue) return null;

                string json = cached.ToString();
                var result = JsonSerializer.Deserialize<List<GlobalIndexDto>>(json, GlobalIndicesJsonOptions);
                if (result == null || !IsCompleteGlobalIndicesResult(result))
                {
                    Console.WriteLine("[global-indices] redis cache ignored: invalid or partial");
                    return null;
                }

                return json;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[global-indices] redis read failed: {ex.Message}");
                return null;
            }
        }

        private async Task TryWriteGlobalIndicesCacheAsync(List<GlobalIndexDto> result)
        {
            try
            {
                var db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(result, GlobalIndicesJsonOptions);
                await db.StringSetAsync(GlobalIndicesCacheKey, json, _marketRealtimeFreshTtl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[global-indices] redis write failed: {ex.Message}");
            }
        }

        private static async Task<GlobalIndexDto> FetchGlobalIndexAsync(HttpClient http, GlobalIndexDefinition idx)
        {
            var failures = new List<string>();
            foreach (string secid in idx.Secids)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    string url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={Uri.EscapeDataString(secid)}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=250";
                    string response = await http.GetStringAsync(url, cts.Token);
                    using var doc = JsonDocument.Parse(response);

                    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                    {
                        failures.Add($"{secid}: data=null");
                        continue;
                    }

                    if (!data.TryGetProperty("klines", out var klines) || klines.ValueKind != JsonValueKind.Array || klines.GetArrayLength() == 0)
                    {
                        failures.Add($"{secid}: klines empty");
                        continue;
                    }

                    var parsedKlines = new List<(DateTime Date, string DateText, double Close, double Rate)>();
                    foreach (var kline in klines.EnumerateArray())
                    {
                        string? raw = kline.GetString();
                        if (string.IsNullOrWhiteSpace(raw)) continue;

                        var parts = raw.Split(',');
                        if (parts.Length <= 8) continue;
                        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var klineDate)) continue;
                        if (!TryParseInvariantDouble(parts[2], out double close)) continue;
                        if (!TryParseInvariantDouble(parts[8], out double rate)) rate = 0;

                        parsedKlines.Add((klineDate.Date, parts[0], close, Math.Round(rate, 2)));
                    }

                    if (parsedKlines.Count == 0)
                    {
                        failures.Add($"{secid}: parse empty");
                        continue;
                    }

                    parsedKlines = parsedKlines.OrderBy(x => x.Date).ToList();
                    double latestClose = Math.Round(parsedKlines[^1].Close, 2);
                    if (latestClose <= 0)
                    {
                        failures.Add($"{secid}: latest={latestClose}");
                        continue;
                    }

                    double firstClose = parsedKlines[0].Close;
                    double? yearRate = firstClose > 0 && parsedKlines[0].Date < parsedKlines[^1].Date
                        ? Math.Round((latestClose - firstClose) / firstClose * 100, 2)
                        : null;
                    double todayRate = parsedKlines[^1].Rate;

                    return new GlobalIndexDto
                    {
                        Name = idx.Name,
                        Code = idx.Code,
                        Market = idx.Market,
                        Secid = secid,
                        Latest = latestClose,
                        Point = latestClose,
                        Close = latestClose,
                        TodayRate = todayRate,
                        YearRate = yearRate,
                        Klines = parsedKlines
                            .OrderByDescending(x => x.Date)
                            .Select(x => new GlobalIndexKlineDto { Date = x.DateText, Rate = x.Rate })
                            .ToList()
                    };
                }
                catch (OperationCanceledException ex)
                {
                    failures.Add($"{secid}: timeout {ex.Message}");
                }
                catch (Exception ex)
                {
                    failures.Add($"{secid}: {ex.Message}");
                }
            }

            Console.WriteLine($"[global-indices] {idx.Name} failed: {string.Join(" | ", failures)}");
            return new GlobalIndexDto
            {
                Name = idx.Name,
                Code = idx.Code,
                Market = idx.Market,
                Secid = idx.Secids.FirstOrDefault() ?? string.Empty,
                Latest = null,
                Point = null,
                Close = null,
                TodayRate = null,
                YearRate = null,
                Klines = new List<GlobalIndexKlineDto>()
            };
        }

        private static bool TryParseInvariantDouble(string? value, out double result)
        {
            return double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out result);
        }

        private static bool IsCompleteGlobalIndicesResult(List<GlobalIndexDto> result)
        {
            var expectedNames = new HashSet<string>
            {
                "上证指数",
                "科创50",
                "创业板指",
                "恒生指数",
                "纳斯达克",
                "标普500",
                "道琼斯"
            };

            return result.Count == expectedNames.Count &&
                   result.All(x =>
                       expectedNames.Contains(x.Name) &&
                       x.Latest > 0 &&
                       x.Klines != null &&
                       x.Klines.Count > 0);
        }


        [HttpGet("test-load")]
        public async Task<IActionResult> TestLoad()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var funds = await GetAllFundsAsync();
                watch.Stop();
                return Ok($"✅ 基金库装载成功！共 {funds.Count} 只。总耗时: {watch.ElapsedMilliseconds} 毫秒");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ 装载失败: {ex.Message}");
            }
        }
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username, [FromQuery] bool force = false)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供用户名");

            try
            {
                var localTime = ChinaNow();
                var dateInfo = GetEffectiveFundDateInfo(localTime);
                string cacheKey = $"Tactical_TodayData_{username}_{dateInfo.EffectiveDateText}_{dateInfo.MarketStatus}";
                if (force)
                {
                    _cache.Remove(cacheKey);
                }
                else if (_cache.TryGetValue(cacheKey, out object cachedResult))
                {
                    return Ok(cachedResult);
                }

                // ✅ 优化后（只读查询，不跟踪变更）
                var myFunds = await _context.MyFunds
                    .AsNoTracking()  // 添加这行
                    .Where(f => f.Username == username)
                    .ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                if (!myFundCodes.Any()) return Ok(new List<object>());

                var today = dateInfo.EffectiveDateStart;
                string todayStr = today.ToString("yyyy'/'MM'/'dd");
                string naturalDate = dateInfo.NaturalDateText;
                string todayDash = dateInfo.EffectiveDateText;
                string dateMode = dateInfo.DateMode;

                // 只取最近 10 天的必要数据，避免首屏把历史全表拖回内存。
                var recentStart = today.AddDays(-10);
                var recentRecords = await _context.FundRecords
                    .AsNoTracking()
                    .Where(r => myFundCodes.Contains(r.FundCode) && r.FetchTime >= recentStart)
                    .OrderBy(r => r.FetchTime)
                    .ToListAsync();

                var todayRecords = recentRecords
                    .Where(r => r.FetchTime >= dateInfo.EffectiveDateStart && r.FetchTime < dateInfo.EffectiveDateEndExclusive)
                    .ToList();

                var lastRecordDict = recentRecords
                    .Where(r => r.FetchTime < dateInfo.EffectiveDateStart)
                    .GroupBy(r => r.FundCode)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

                var pastActualDict = recentRecords
                    .Where(r => r.ActualRate != 0)
                    .GroupBy(r => r.FundCode)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).Take(3).ToList());

                var effectiveArchiveTotal = await _context.DailyArchives
                    .AsNoTracking()
                    .Where(a => a.Username == username
                                && a.FundCode == "TOTAL"
                                && a.RecordDate >= dateInfo.EffectiveDateStart
                                && a.RecordDate < dateInfo.EffectiveDateEndExclusive)
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefaultAsync();

                var archiveHistoryDict = await LoadRecentFundArchiveHistoryAsync(
                    username,
                    myFundCodes,
                    dateInfo.EffectiveDateStart,
                    dateInfo.EffectiveDateEndExclusive);


                // ⚡ 首屏性能优化：today 接口不再发起任何外部净值 HTTP 请求。
                // 真实净值由 NavSettlementService 后台结算后写入 MyFunds.LastSettled* 字段。
                // 这样 App/Web 打开和下拉刷新只访问本机数据库，避免东方财富接口抖动拖慢用户请求。


                var result = myFunds.Select(config =>
                {
                    var fundDateInfo = GetEffectiveFundDateInfo(localTime, DetectFundMarket(config.FundCode, config.FundName));
                    var fundRecords = todayRecords.Where(r => r.FundCode == config.FundCode).ToList();
                    lastRecordDict.TryGetValue(config.FundCode, out var lastRecord);

                    var past3DaysRecords = pastActualDict.TryGetValue(config.FundCode, out var pastList)
                        ? pastList
                        : new List<FundData>();

                    double avgDiff = 0;
                    if (past3DaysRecords.Count > 0)
                    {
                        avgDiff = past3DaysRecords.Average(r => r.ActualRate - r.EstimatedRate);
                        if (avgDiff > 0.5) avgDiff = 0.5;
                        if (avgDiff < -0.5) avgDiff = -0.5;
                    }

                    var dataPoints = new List<object[]>();

                    if (lastRecord != null)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", Math.Round(lastRecord.EstimatedRate + avgDiff, 2) });
                    }

                    dataPoints.AddRange(fundRecords.Select(r => new object[] {
                        r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"),
                        Math.Round(r.EstimatedRate + avgDiff, 2)
                    }));

                    if (dataPoints.Count == 0)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });
                    }

                    // 是否已完成今日真实净值清算。只读本地字段，不在 today 请求里访问外部接口。
                    bool isSettled = config.LastSettledDate == todayDash;
                    double? actualRate = isSettled ? config.LastSettledRate : null;
                    double? actualExactProfit = isSettled ? config.LastSettledProfit : null;

                    double activePendingBuyAmount = GetActivePendingBuyAmount(config, todayDash);
                    double? previousArchiveAssets = FindPreviousArchiveAssets(config, archiveHistoryDict, dateInfo.EffectiveDateStart);
                    double pendingBuyAmount = ResolvePendingBuyAmount(config, todayDash, previousArchiveAssets);
                    bool legacyPendingFallback = activePendingBuyAmount <= 0 && pendingBuyAmount > 0;
                    string? resolvedPendingStatus = legacyPendingFallback ? "pending_buy" : config.PendingTradeStatus;
                    string? resolvedPendingSource = legacyPendingFallback ? "legacy_ocr_pending_fallback" : config.PendingSource;
                    bool pendingBuy = pendingBuyAmount > 0;
                    double rawHoldAmount = Math.Max(0, config.HoldAmount);
                    double confirmedHoldAmount = Math.Max(0, Math.Round(rawHoldAmount - pendingBuyAmount, 2));
                    double todayRateForSimulation = isSettled ? config.LastSettledRate : (dataPoints.Count > 0 ? Convert.ToDouble(dataPoints.Last()[1]) : 0);
                    double todayBaseAmount = GetDailyBaseAmount(config, todayDash, pendingBuyAmount);
                    double todayProfitPreview = isSettled ? config.LastSettledProfit : Math.Round(todayBaseAmount * todayRateForSimulation / 100.0, 2);
                    double currentAssetsPreview = Math.Round(todayBaseAmount + todayProfitPreview, 2);
                    double rawCostBasis = config.CostAmount > 0 ? config.CostAmount : rawHoldAmount;
                    double costBasis = Math.Max(0, Math.Round(rawCostBasis - pendingBuyAmount, 2));
                    bool pendingRedeem = PortfolioSettlementService.IsPendingRedeem(config);
                    double soldCost = PortfolioSettlementService.GetSoldCost(config);
                    // For sold-out pending funds, use soldCost as display cost
                    if (config.HoldShares <= 0 && pendingRedeem && soldCost > 0)
                        costBasis = soldCost;

                    // 累计收益显示优先级：平台累计收益 > 已确认落袋 > 待确认
                    bool isSoldOut = config.HoldShares <= 0 && !pendingBuy;
                    double displayedProfit;
                    string displayedProfitSource;
                    if (isSoldOut && config.PlatformCumulativeProfit > 0)
                    {
                        displayedProfit = config.PlatformCumulativeProfit;
                        displayedProfitSource = "platform_cumulative_profit";
                    }
                    else if (isSoldOut && pendingRedeem && config.RealizedProfit <= 0)
                    {
                        displayedProfit = 0;
                        displayedProfitSource = "pending_redeem";
                    }
                    else
                    {
                        displayedProfit = config.RealizedProfit;
                        displayedProfitSource = config.RealizedProfit > 0 ? "confirmed_redeem" : "none";
                    }

                    double totalProfitPreview = Math.Round(currentAssetsPreview - costBasis + displayedProfit, 2);
                    double existingReturnRateValue = isSoldOut && displayedProfit > 0 && costBasis > 0
                        ? Math.Round(displayedProfit / costBasis * 100.0, 2)
                        : (costBasis > 0 ? Math.Round(totalProfitPreview / costBasis * 100.0, 2) : 0);
                    double breakEvenRateValue = !isSoldOut && currentAssetsPreview > 0 && totalProfitPreview < 0 ? Math.Round(-totalProfitPreview / currentAssetsPreview * 100.0, 2) : 0;
                    double diffAbs = lastRecord != null ? Math.Abs(lastRecord.DiffRate) : 0;
                    int reliabilityScore = isSettled ? 100 : Math.Clamp(80 - (int)Math.Round(Math.Abs(avgDiff) * 40) - (diffAbs > 0.15 ? 10 : 0), 35, 92);
                    string reliabilityLevel = isSettled ? "真实净值确认" : reliabilityScore >= 80 ? "估值较稳" : reliabilityScore >= 60 ? "估值需观察" : "估值偏弱";

                    // FundController.cs 中 GetTodayData 方法内部
                    bool isInactiveHolding = config.HoldShares <= 0 && !pendingBuy;
                    double displayAmount = isInactiveHolding ? 0 : confirmedHoldAmount;
                    // marketValue = 当前确认市值（显示用），todayBaseAmount = 收益率分母
                    double marketValue = isInactiveHolding ? 0 : Math.Round(currentAssetsPreview, 2);
                    double todayProfit = Math.Round(todayProfitPreview, 2);
                    string profitSource = isSettled ? "nav_settlement" : (dataPoints.Count > 0 ? "estimate" : "none");

                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = displayAmount,
                        marketValue,
                        rawHoldAmount = isInactiveHolding ? 0 : rawHoldAmount,
                        confirmedAmount = displayAmount,
                        pendingBuy = pendingBuy,
                        pendingBuyAmount = pendingBuy ? pendingBuyAmount : 0,
                        pendingTradeDate = config.PendingTradeDate,
                        pendingTradeTime = config.PendingTradeTime,
                        pendingTradeStatus = resolvedPendingStatus,
                        pendingConfirmDate = config.PendingConfirmDate,
                        pendingSource = resolvedPendingSource,
                        pendingNote = pendingBuy ? "买入待确认，不参与今日收益" : string.Empty,
                        shares = config.HoldShares,
                        cost = isInactiveHolding ? (soldCost > 0 ? soldCost : (double?)null) : (costBasis > 0 ? costBasis : (double?)null),
                        rawCostAmount = isInactiveHolding ? (double?)null : (rawCostBasis > 0 ? rawCostBasis : (double?)null),
                        confirmedCost = isInactiveHolding ? (double?)null : costBasis,
                        realizedProfit = config.RealizedProfit,
                        platformCumulativeProfit = config.PlatformCumulativeProfit,
                        displayedProfit = displayedProfit,
                        displayedProfitSource = displayedProfitSource,
                        pendingRedeem = pendingRedeem,
                        soldCost = soldCost,
                        inactiveHolding = isInactiveHolding,
                        lastTradeDate = config.LastTradeDate,
                        lastAddAmount = config.LastAddAmount,
                        lastSettledDate = config.LastSettledDate,
                        lastSettledProfit = config.LastSettledProfit,
                        lastSettledRate = config.LastSettledRate,
                        existingReturnRate = existingReturnRateValue,
                        breakEvenRate = breakEvenRateValue,
                        reliabilityScore = reliabilityScore,
                        reliabilityLevel = reliabilityLevel,
                        breakEvenSimulator = new[]
                        {
                            new { scenario = "+1%", projectedAssets = Math.Round(currentAssetsPreview * 1.01, 2), projectedProfit = Math.Round(currentAssetsPreview * 1.01 - costBasis + displayedProfit, 2) },
                            new { scenario = "+3%", projectedAssets = Math.Round(currentAssetsPreview * 1.03, 2), projectedProfit = Math.Round(currentAssetsPreview * 1.03 - costBasis + displayedProfit, 2) },
                            new { scenario = "+5%", projectedAssets = Math.Round(currentAssetsPreview * 1.05, 2), projectedProfit = Math.Round(currentAssetsPreview * 1.05 - costBasis + displayedProfit, 2) },
                            new { scenario = "-3%", projectedAssets = Math.Round(currentAssetsPreview * 0.97, 2), projectedProfit = Math.Round(currentAssetsPreview * 0.97 - costBasis + displayedProfit, 2) }
                        },
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        calibrationOffset = Math.Round(avgDiff, 4),
                        data = dataPoints,
                        isSettled = isSettled,
                        actualRate = actualRate,
                        actualExactProfit = actualExactProfit,
                        todayBaseAmount = todayBaseAmount,
                        todayRateForSimulation = todayRateForSimulation,
                        todayProfitPreview = todayProfitPreview,
                        todayProfit = todayProfit,
                        todayRate = todayRateForSimulation,
                        profitSource = profitSource,
                        marketOpen = fundDateInfo.MarketOpen,
                        marketStatus = fundDateInfo.MarketStatus,
                        marketLabel = fundDateInfo.MarketLabel,
                        effectiveDate = fundDateInfo.EffectiveDateText,
                        debug = new
                        {
                            code = config.FundCode,
                            name = config.FundName,
                            rawHoldAmount,
                            confirmedAmount = displayAmount,
                            pendingBuyAmount = pendingBuy ? pendingBuyAmount : 0,
                            activePendingBuyAmount,
                            legacyPendingFallback,
                            previousArchiveAssets,
                            pendingTradeStatus = config.PendingTradeStatus,
                            resolvedPendingStatus,
                            pendingTradeDate = config.PendingTradeDate,
                            pendingSource = config.PendingSource,
                            resolvedPendingSource,
                            lastTradeDate = config.LastTradeDate,
                            lastAddAmount = config.LastAddAmount,
                            lastSettledDate = config.LastSettledDate,
                            lastSettledProfit = config.LastSettledProfit,
                            lastSettledRate = config.LastSettledRate,
                            todayBaseAmount,
                            todayRateForSimulation,
                            todayProfitPreview
                        }
                    };

                });

                var finalResult = result.OrderByDescending(x => x.amount).ToList();

                // 统一计算今日组合收益摘要（首页和曲线共用同一口径）
                double summaryProfit = 0, summaryBase = 0, summaryAssets = 0, summaryCost = 0, summaryRealized = 0;
                foreach (var fund in finalResult)
                {
                    if (fund.inactiveHolding)
                    {
                        summaryRealized += fund.displayedProfit;
                        continue;
                    }
                    double profitVal = fund.todayProfit;
                    double baseVal = fund.todayBaseAmount;
                    summaryProfit += profitVal;
                    summaryBase += baseVal;
                    summaryAssets += fund.marketValue;
                    summaryCost += fund.cost ?? 0;
                    summaryRealized += fund.realizedProfit;
                }
                var useArchiveTotal = !dateInfo.MarketOpen && effectiveArchiveTotal != null;
                double summaryTodayProfit = summaryProfit;
                double summaryTodayBase = summaryBase;
                double summaryTodayRate = summaryBase > 0 ? Math.Round(summaryProfit / summaryBase * 100, 2) : 0;
                double summaryTotalAssets = summaryAssets;
                double summaryTotalProfit = summaryAssets - summaryCost + summaryRealized;
                double summaryTotalRate = summaryCost > 0 ? Math.Round((summaryAssets - summaryCost + summaryRealized) / summaryCost * 100, 2) : 0;
                string summarySource = "current_holdings_effective_date";

                if (useArchiveTotal)
                {
                    summaryTodayProfit = effectiveArchiveTotal!.DailyProfit;
                    summaryTodayRate = effectiveArchiveTotal.DailyRate;
                    summaryTodayBase = Math.Abs(effectiveArchiveTotal.DailyRate) > 0.000001
                        ? effectiveArchiveTotal.DailyProfit / (effectiveArchiveTotal.DailyRate / 100.0)
                        : summaryBase;
                    summaryTotalAssets = effectiveArchiveTotal.Assets;
                    summaryTotalProfit = effectiveArchiveTotal.TotalProfit;
                    summaryTotalRate = effectiveArchiveTotal.TotalRate;
                    summarySource = "daily_archive_total";
                }

                var summary = new
                {
                    totalTodayProfit = Math.Round(summaryTodayProfit, 2),
                    totalTodayBaseAmount = Math.Round(Math.Abs(summaryTodayBase), 2),
                    totalTodayRate = Math.Round(summaryTodayRate, 2),
                    totalAssets = Math.Round(summaryTotalAssets, 2),
                    totalProfit = Math.Round(summaryTotalProfit, 2),
                    totalRate = Math.Round(summaryTotalRate, 2),
                    date = naturalDate,
                    effectiveDate = todayDash,
                    naturalDate,
                    dateMode,
                    marketOpen = dateInfo.MarketOpen,
                    marketStatus = dateInfo.MarketStatus,
                    summarySource
                };

                // 真实净值一出现，后端立即修正”今日总持仓 + 单只基金”档案。
                // 这样不再依赖前端 save-archive，也能覆盖旧版本写坏的 TOTAL 记录。
                if (dateInfo.DateMode == "today" && myFunds.Any(f => f.LastSettledDate == todayDash))
                {
                    await UpsertTodayArchivesFromCurrentHoldingsAsync(username, today, myFunds, todayRecords);
                    await _context.SaveChangesAsync();
                }

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(60))
                    .SetSlidingExpiration(TimeSpan.FromSeconds(30));
                var payload = new { funds = finalResult, summary };
                _cache.Set(cacheKey, payload, cacheOptions);

                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"服务器当场阵亡：{ex.Message}");
            }
        }

        [HttpPost("sync-real-nav")]
        public async Task<IActionResult> SyncRealNav([FromForm] string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供用户名");

            var localTime = ChinaNow();
            string settleDate = localTime.ToString("yyyy-MM-dd");
            DateTime today = localTime.Date;
            DateTime tomorrow = today.AddDays(1);

            try
            {
                var funds = await _context.MyFunds
                    .Where(f => f.Username == username)
                    .ToListAsync();

                if (funds.Count == 0)
                {
                    return Ok(new { success = true, updated = 0, message = "暂无持仓" });
                }

                var client = _httpClientFactory.CreateClient("EastMoney");

                int updated = 0;
                int skipped = 0;
                var diagnostics = new List<string>();

                foreach (var group in funds.GroupBy(f => f.FundCode))
                {
                    var snapshot = await FetchOfficialNavSnapshotAsync(client, group.Key, settleDate);
                    if (snapshot == null)
                    {
                        skipped++;
                        diagnostics.Add($"{group.Key}: 尚未拿到可校验真实净值");
                        continue;
                    }

                    var targetRecord = await _context.FundRecords
                        .Where(r => r.FundCode == group.Key && r.FetchTime >= today && r.FetchTime < tomorrow)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefaultAsync();

                    if (targetRecord != null)
                    {
                        targetRecord.ActualRate = snapshot.Rate;
                        targetRecord.DiffRate = Math.Round(snapshot.Rate - targetRecord.EstimatedRate, 2);
                    }

                    foreach (var fund in group)
                    {
                        double? exactProfit = null;
                        double? exactAssets = null;

                        double effectiveShares = GetEffectiveShares(fund, settleDate);

                        if (effectiveShares > 0)
                        {
                            if (snapshot.NavDiff.HasValue)
                            {
                                exactProfit = Math.Round(effectiveShares * snapshot.NavDiff.Value, 2);
                            }

                            if (snapshot.TodayNav.HasValue && snapshot.TodayNav.Value > 0)
                            {
                                exactAssets = Math.Round(effectiveShares * snapshot.TodayNav.Value, 2);
                            }
                        }

                        if (ApplyOneDaySettlement(fund, snapshot.Rate, settleDate, exactProfit, exactAssets))
                        {
                            updated++;
                        }
                    }

                    diagnostics.Add($"{group.Key}: {snapshot.Rate:F4}% [{snapshot.Source}]");
                }

                var todayRecords = await _context.FundRecords
                    .Where(r => r.FetchTime >= today && r.FetchTime < tomorrow)
                    .ToListAsync();

                await UpsertTodayArchivesFromCurrentHoldingsAsync(username, today, funds, todayRecords);
                await _context.SaveChangesAsync();

                ClearTodayCache(username);

                return Ok(new
                {
                    success = true,
                    updated,
                    skipped,
                    message = $"真实净值同步完成，更新 {updated} 条持仓。",
                    diagnostics
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"真实净值同步失败: {ex.Message}");
            }
        }

        // 🚀 猎隼侦察兵升级版：双重刺探，获取单位净值计算绝对物理收益
        private async Task<(double? rate, double? exactProfit)> GetTodayRealRateAsync(string fundCode, string todayStr, double shares)
        {
            string cacheKey = $"RealRateV2_{fundCode}_{todayStr}_{shares}";
            if (_cache.TryGetValue(cacheKey, out (double?, double?) cached)) return cached;

            string missKey = $"NoRealRate_{fundCode}_{todayStr}";
            if (_cache.TryGetValue(missKey, out _)) return (null, null);

            try
            {
                // 🚀 修复 1：5秒超时，从容应对高并发
                var client = _httpClientFactory.CreateClient("EastMoney");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=2";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                var snapshot = TryBuildOfficialNavSnapshot(dataArray, todayStr);
                if (snapshot != null)
                {
                    double? exactProfit = null;
                    if (shares > 0 && snapshot.NavDiff.HasValue)
                    {
                        exactProfit = Math.Round(shares * snapshot.NavDiff.Value, 2);
                    }

                    var result = (snapshot.Rate, exactProfit);
                    _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
                    return result;
                }

                if (dataArray.GetArrayLength() > 0)
                {
                    var latest = dataArray[0];
                    if ((latest.GetProperty("FSRQ").GetString() ?? "") != todayStr)
                    {
                        _cache.Set(missKey, true, TimeSpan.FromMinutes(2));
                        return (null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                // 🚀 修复 4：网络超时报错绝不连坐！直接放行，等前端 15 秒后重试！
                Console.WriteLine($"[猎隼刺探异常] {fundCode}: {ex.Message}");
            }

            return (null, null);
        }

        // 🚀 1. 战术加仓接口：支持日期，仅需金额
        [HttpPost("add-position")]
        public async Task<IActionResult> AddPosition([FromForm] string username, [FromForm] string code, [FromForm] double addAmount, [FromForm] string tradeDate)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未授权");
            try
            {
                var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                if (fund == null) return BadRequest("未找到基金");

                _portfolioSettlement.AddPosition(fund, addAmount, tradeDate);
                _context.MyFunds.Update(fund);
                await _context.SaveChangesAsync();
                ClearTodayCache(username);

                return Ok(new { success = true, msg = $"加仓成功！[{tradeDate}] 注入资金: {addAmount:F2} 元" });
            }
            catch (Exception ex) { return StatusCode(500, $"加仓异常: {ex.Message}"); }
        }

        // 🚀 2. 战术减仓接口：日期支持，金额可选 (留空则系统自动按份额比例算)
        [HttpPost("reduce-position")]
        public async Task<IActionResult> ReducePosition([FromForm] string username, [FromForm] string code, [FromForm] double reduceShares, [FromForm] double? reduceAmount, [FromForm] string tradeDate, [FromForm] double? platformCumulativeProfit)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未授权");
            var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
            if (fund == null) return BadRequest("未找到基金");

            if (platformCumulativeProfit.HasValue)
                fund.PlatformCumulativeProfit = platformCumulativeProfit.Value;

            double profit;
            try
            {
                profit = _portfolioSettlement.ReducePosition(fund, reduceShares, reduceAmount, tradeDate);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            _context.MyFunds.Update(fund);
            await _context.SaveChangesAsync();
            ClearTodayCache(username);

            bool isPending = fund.HoldShares <= 0 && profit == 0 && !(reduceAmount.GetValueOrDefault() > 0);
            string msg = isPending
                ? $"[{tradeDate}] 减仓已记录，赎回金额待确认。请在蚂蚁财富查看确认金额后，再次减仓并填写实际到手金额。"
                : $"[{tradeDate}] 减仓完毕！归库利润: {profit:F2} 元";
            return Ok(new { success = true, msg, pendingRedeem = isPending });
        }

        // =========================================================================
        // 🚀 以下为您昨日遗失的 9 大核心功能接口，现已从备份库中完美复原！
        // =========================================================================

        // 🚀 恢复带份额的修改接口
        [HttpPost("update-details")]
        public async Task<IActionResult> UpdateDetailsAsync([FromForm] string username, [FromForm] string code, [FromForm] double costAmount, [FromForm] double holdShares, [FromForm] string originalCode)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("用户名身份未确认");
            if (string.IsNullOrEmpty(code) || costAmount <= 0) return BadRequest("本金或东财代码信息不完整");

            try
            {
                var existFund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && (f.FundCode == code || f.FundCode == originalCode));
                if (existFund != null)
                {
                    if (originalCode.StartsWith("待核对"))
                    {
                        var checkNewCode = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                        if (checkNewCode != null) return BadRequest($"东财代码 [{code}] 已经在库中，请不要输入重复代码。");

                        existFund.FundCode = code;
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares;
                        // 更新份额
                        _context.MyFunds.Update(existFund);
                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares; // 更新份额
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();
                    ClearTodayCache(username);
                    return Ok($"本金、代码与份额补给完成！");
                }

                return BadRequest("未能匹配到该基金");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"云端接收异常: {ex.Message}");
            }
        }

        [HttpGet("force-settle")]
        public async Task<IActionResult> ForceSettle()
        {
            var client = _httpClientFactory.CreateClient("EastMoney");
            var targetFunds = await _context.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            var localTime = DateTime.UtcNow.AddHours(8);
            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            var resultLog = new List<string>
            {
                $"===== 手动清算收益 =====",
                $"当前时间: {localTime:yyyy-MM-dd HH:mm:ss}"
            };
            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await client.GetStringAsync(url);

                    if (!response.Contains("LSJZList")) continue;

                    using var doc = JsonDocument.Parse(response);
                    var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");
                    if (dataArray.GetArrayLength() > 0)
                    {
                        var latestData = dataArray[0];
                        string fsrq = latestData.GetProperty("FSRQ").GetString() ?? "";
                        string jzzzlStr = latestData.GetProperty("JZZZL").GetString() ?? "";
                        if (fsrq == todayStr && double.TryParse(jzzzlStr, out double actualRate))
                        {
                            var targetRecord = await _context.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();
                            if (targetRecord != null)
                            {
                                targetRecord.EstimatedRate = actualRate;
                                resultLog.Add($"✅ [{code}] 覆盖成功: {actualRate}%");
                            }
                        }
                    }
                }
                catch { }
            }

            await _context.SaveChangesAsync();
            return Ok(resultLog);
        }

        private sealed class SectorDefinition
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string[] Include { get; init; } = Array.Empty<string>();
            public string[] Exclude { get; init; } = Array.Empty<string>();
        }

        private sealed class SectorFundQuote
        {
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public double Rate { get; init; }
            public double? MonthRate { get; init; }
            public bool HasQuote { get; init; }
            public string UpdatedAt { get; init; } = string.Empty;
        }

        private sealed class SectorSummaryDto
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public double Rate { get; init; }
            public int FundCount { get; init; }
            public int QuotedCount { get; init; }
            public int StreakDays { get; init; }
            public int HoldingRank { get; init; }
            public string UpdatedAt { get; init; } = string.Empty;
            public List<SectorFundQuote> PreviewFunds { get; init; } = new();
        }

        private static readonly IReadOnlyList<SectorDefinition> SectorDefinitions = new List<SectorDefinition>
        {
            new() { Key = "gold", Name = "黄金", Include = new[] { "黄金", "上海金", "黄金ETF", "黄金基金" }, Exclude = new[] { "黄金股" } },
            new() { Key = "gold_stock", Name = "黄金股", Include = new[] { "黄金股", "有色金属", "贵金属" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "lithium", Name = "锂矿", Include = new[] { "锂", "锂矿", "锂电", "电池" }, Exclude = new[] { "货币", "债" } },
            new() { Key = "rare_earth", Name = "稀土永磁", Include = new[] { "稀土", "永磁", "稀有金属" }, Exclude = new[] { "债" } },
            new() { Key = "new_energy", Name = "新能源", Include = new[] { "新能源", "新能源车", "电动车", "电池", "碳中和" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "solid_battery", Name = "固态电池", Include = new[] { "固态电池", "电池", "锂电" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "storage", Name = "储能", Include = new[] { "储能", "电力设备", "新能源" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nonferrous", Name = "有色金属", Include = new[] { "有色", "有色金属", "金属", "资源", "矿业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "gem", Name = "创业板", Include = new[] { "创业板", "创业", "创业成长" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "steel", Name = "钢铁", Include = new[] { "钢铁" }, Exclude = Array.Empty<string>() },
            new() { Key = "agri", Name = "农林牧渔", Include = new[] { "农业", "畜牧", "养殖", "农林牧渔", "粮食" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "media_game", Name = "传媒游戏", Include = new[] { "传媒", "游戏", "动漫", "影视", "文化", "娱乐" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "grid", Name = "电网设备", Include = new[] { "电网", "电力设备", "电力", "智能电网" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "home_appliance", Name = "家用电器", Include = new[] { "家电", "家用电器", "白色家电" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "pv", Name = "光伏", Include = new[] { "光伏", "太阳能", "新能源" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "double_innovation", Name = "双创50", Include = new[] { "双创", "科创创业", "科创50", "创业板" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "food_beverage", Name = "食品饮料", Include = new[] { "食品", "饮料", "白酒", "消费" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "innovative_drug", Name = "创新药", Include = new[] { "创新药", "医药", "生物医药", "港股通医药" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "oversea_medicine", Name = "海外医药", Include = new[] { "海外医药", "全球医药", "港股通医药", "恒生医疗", "中概互联医疗" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "medicine", Name = "医药", Include = new[] { "医药", "医疗", "生物", "药", "中药", "疫苗", "CRO" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "healthcare", Name = "医疗", Include = new[] { "医疗", "医药", "器械", "医美", "生物" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nuclear", Name = "可控核聚变", Include = new[] { "核", "核电", "高端装备", "军工" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "semiconductor", Name = "半导体", Include = new[] { "半导体", "芯片", "集成电路", "科创芯片" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "semi_material", Name = "半导体材料设备", Include = new[] { "半导体材料", "半导体设备", "芯片设备", "芯片材料", "集成电路" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "bank", Name = "银行", Include = new[] { "银行" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "military", Name = "军工", Include = new[] { "军工", "国防", "航天", "航空", "高端装备" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "sp500", Name = "标普", Include = new[] { "标普", "S&P", "SP500", "美国500" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "asia_pacific", Name = "亚太", Include = new[] { "亚太", "日本", "越南", "印度", "东南亚" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "bond", Name = "债基", Include = new[] { "债", "债券", "纯债", "信用债", "中短债" }, Exclude = new[] { "可转债", "转债" } },
            new() { Key = "convertible_bond", Name = "可转债", Include = new[] { "可转债", "转债" }, Exclude = Array.Empty<string>() },
            new() { Key = "mixed_bond", Name = "混债", Include = new[] { "混债", "二级债", "一级债", "固收+", "固收加" }, Exclude = Array.Empty<string>() },
            new() { Key = "money", Name = "货币基金", Include = new[] { "货币", "现金", "添利", "余额", "天天理财" }, Exclude = new[] { "股票", "混合" } },
            new() { Key = "real_estate", Name = "地产", Include = new[] { "地产", "房地产", "沪深300地产", "地产等权" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "robot", Name = "机器人", Include = new[] { "机器人", "智能制造", "高端制造", "机床", "自动化", "工业母机", "人形机器人" }, Exclude = new[] { "债", "货币" } },

new() { Key = "cpo", Name = "CPO", Include = new[] { "CPO", "光模块", "光通信", "通信设备", "数据中心", "算力" }, Exclude = new[] { "债", "货币" } },

new() { Key = "communication", Name = "通信", Include = new[] { "通信", "通信设备", "5G", "光通信", "信息技术", "TMT" }, Exclude = new[] { "债", "货币" } },

new() { Key = "hs_tech", Name = "恒生科技", Include = new[] { "恒生科技", "恒生互联网", "港股科技", "港股通科技", "中概互联", "港股互联网" }, Exclude = new[] { "债", "货币" } },

new() { Key = "ai", Name = "人工智能", Include = new[] { "人工智能", "AI", "智能", "智能化", "算力", "大模型", "ChatGPT" }, Exclude = new[] { "债", "货币" } },

new() { Key = "ai_app", Name = "AI应用", Include = new[] { "AI应用", "传媒", "游戏", "软件", "计算机", "云计算", "大数据", "数字经济" }, Exclude = new[] { "债", "货币" } },

new() { Key = "big_tech", Name = "大科技", Include = new[] { "科技", "半导体", "芯片", "人工智能", "计算机", "通信", "电子", "软件", "云计算", "信息技术", "数字经济" }, Exclude = new[] { "债", "货币" } },

new() { Key = "north_exchange", Name = "北证", Include = new[] { "北证", "北交所", "北证50", "专精特新" }, Exclude = new[] { "债", "货币" } },

new() { Key = "consumer_electronics", Name = "消费电子", Include = new[] { "消费电子", "电子", "苹果", "智能终端", "智能汽车", "半导体", "芯片" }, Exclude = new[] { "债", "货币" } },

new() { Key = "cloud", Name = "云计算", Include = new[] { "云计算", "计算机", "软件", "大数据", "数据中心", "算力", "信创" }, Exclude = new[] { "债", "货币" } },

new() { Key = "transport", Name = "交通运输", Include = new[] { "交通运输", "运输", "物流", "航运", "航空", "港口", "高速公路" }, Exclude = new[] { "债", "货币" } }
        };

        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                if (!string.IsNullOrWhiteSpace(word) && text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int ScoreFundForSector(string fundName, SectorDefinition def)
        {
            var name = fundName ?? string.Empty;
            int score = 0;
            if (name.Contains(def.Name, StringComparison.OrdinalIgnoreCase)) score += 80;
            foreach (var kw in def.Include)
            {
                if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) score += Math.Min(40, kw.Length * 8);
            }
            if (name.Contains("ETF", StringComparison.OrdinalIgnoreCase)) score += 25;
            if (name.Contains("联接", StringComparison.OrdinalIgnoreCase)) score += 20;
            if (name.Contains("指数", StringComparison.OrdinalIgnoreCase)) score += 15;
            if (name.Contains("C", StringComparison.OrdinalIgnoreCase)) score += 3;
            if (name.Contains("货币", StringComparison.OrdinalIgnoreCase) && def.Key != "money") score -= 200;
            if (name.Contains("债", StringComparison.OrdinalIgnoreCase) && def.Key != "bond" && def.Key != "convertible_bond" && def.Key != "mixed_bond") score -= 120;
            return score;
        }

        private static SectorDefinition ResolveSector(string keyOrName)
        {
            var clean = (keyOrName ?? string.Empty).Trim();
            return SectorDefinitions.FirstOrDefault(s => s.Key.Equals(clean, StringComparison.OrdinalIgnoreCase) || s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))
                   ?? SectorDefinitions.FirstOrDefault(s => clean.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                   ?? new SectorDefinition { Key = clean, Name = clean, Include = new[] { clean }, Exclude = Array.Empty<string>() };
        }

        private static List<FundInfoCache> MatchFundsBySector(IEnumerable<FundInfoCache> allFunds, SectorDefinition def, int limit)
        {
            return allFunds
                .Where(f => !string.IsNullOrWhiteSpace(f.Code) && !string.IsNullOrWhiteSpace(f.Name))
                .Where(f => ContainsAny(f.Name, def.Include) && !ContainsAny(f.Name, def.Exclude))
                .Select(f => new { Fund = f, Score = ScoreFundForSector(f.Name, def) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Fund.Name.Length)
                .Take(limit)
                .Select(x => x.Fund)
                .ToList();
        }

        private static async Task<SectorFundQuote> FetchFundQuoteAsync(HttpClient client, FundInfoCache fund, bool withMonthRate = false)
        {
            double rate = 0;
            bool hasQuote = false;
            string updatedAt = string.Empty;
            try
            {
                string gzUrl = $"http://fundgz.1234567.com.cn/js/{fund.Code}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                string gzRes = await client.GetStringAsync(gzUrl);
                var match = Regex.Match(gzRes, @"jsonpgz\((.*?)\);");
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("gszzl", out var gszzl) && double.TryParse(gszzl.GetString(), out var parsedRate))
                    {
                        rate = Math.Round(parsedRate, 2);
                        hasQuote = true;
                    }
                    if (root.TryGetProperty("gztime", out var timeProp)) updatedAt = timeProp.GetString() ?? string.Empty;
                    if (root.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                    {
                        fund.Name = nameProp.GetString()!;
                    }
                }
            }
            catch
            {
                hasQuote = false;
            }

            double? monthRate = null;
            if (withMonthRate)
            {
                monthRate = await FetchFundMonthRateAsync(client, fund.Code);
            }

            return new SectorFundQuote
            {
                Code = fund.Code,
                Name = fund.Name,
                Rate = rate,
                MonthRate = monthRate,
                HasQuote = hasQuote,
                UpdatedAt = updatedAt
            };
        }

        private static async Task<double?> FetchFundMonthRateAsync(HttpClient client, string code)
        {
            try
            {
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=28";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri("http://fundf10.eastmoney.com/");
                var res = await client.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;
                string body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("Data", out var data)) return null;
                if (!data.TryGetProperty("LSJZList", out var list) || list.GetArrayLength() < 2) return null;

                var latest = list[0];
                var oldest = list[list.GetArrayLength() - 1];
                if (double.TryParse(latest.GetProperty("DWJZ").GetString(), out double latestNav) &&
                    double.TryParse(oldest.GetProperty("DWJZ").GetString(), out double oldestNav) && oldestNav > 0)
                {
                    return Math.Round((latestNav - oldestNav) / oldestNav * 100.0, 2);
                }
            }
            catch { }
            return null;
        }

        private async Task<List<SectorFundQuote>> BuildSectorQuotesAsync(SectorDefinition def, int fundLimit, bool withMonthRate)
        {
            var allFunds = await GetAllFundsAsync();
            var matched = MatchFundsBySector(allFunds, def, fundLimit);
            if (matched.Count == 0) return new List<SectorFundQuote>();

            var client = _httpClientFactory.CreateClient("EastMoneyQuote");

            using var limiter = new SemaphoreSlim(8);
            var tasks = matched.Select(async fund =>
            {
                await limiter.WaitAsync();
                try { return await FetchFundQuoteAsync(client, fund, withMonthRate); }
                finally { limiter.Release(); }
            }).ToArray();

            var quotes = await Task.WhenAll(tasks);
            return quotes
                .Where(q => !string.IsNullOrWhiteSpace(q.Code))
                .OrderByDescending(q => q.Rate)
                .ThenBy(q => q.Name)
                .ToList();
        }

        [HttpGet("sectors")]
        public async Task<IActionResult> GetSectors([FromQuery] bool force = false)
        {
            const string redisKey = "api:fund:sectors:v1";
            const string freshKey = "FundSectorRadarV5";
            const string staleKey = "FundSectorRadarV5_Stale";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            IDatabase? redisDb = null;

            try
            {
                redisDb = _redis.GetDatabase();
            }
            catch
            {
                redisDb = null;
            }

            async Task TrySetRedisAsync(string json, TimeSpan ttl)
            {
                if (redisDb == null) return;

                try
                {
                    await redisDb.StringSetAsync(redisKey, json, ttl);
                }
                catch
                {
                    // Redis 只是缓存，写入失败不能影响接口主流程。
                }
            }

            async Task<RedisValue> TryGetRedisAsync()
            {
                if (redisDb == null) return RedisValue.Null;

                try
                {
                    return await redisDb.StringGetAsync(redisKey);
                }
                catch
                {
                    return RedisValue.Null;
                }
            }

            void SetCacheHeader(string source, TimeSpan? ttl = null)
            {
                Response.Headers["X-App-Cache"] = source;

                if (ttl.HasValue)
                {
                    Response.Headers["X-App-Cache-Ttl"] = ((int)ttl.Value.TotalSeconds).ToString();
                }
            }

            // 1. 普通请求：优先读 Redis。
            if (!force)
            {
                var cached = await TryGetRedisAsync();
                if (cached.HasValue)
                {
                    SetCacheHeader("redis");
                    return Content(cached.ToString(), "application/json");
                }
            }

            // 1b. 普通请求：Redis 没命中，读数据库缓存。
            if (!force)
            {
                var (dbData, dbSource) = await _marketCache.TryGetAsync<object>("sector_radar_v2");
                if (dbData != null && dbSource != null)
                {
                    SetCacheHeader(dbSource);
                    var json = JsonSerializer.Serialize(dbData, jsonOptions);
                    return Content(json, "application/json");
                }
            }

            // 2. 普通请求：Redis 没命中，再读内存 fresh，并回填 Redis。
            if (!force && _cache.TryGetValue(freshKey, out object fresh))
            {
                var ttl = GetExternalDataFreshTtl();
                var json = JsonSerializer.Serialize(fresh, jsonOptions);

                await TrySetRedisAsync(json, ttl);

                SetCacheHeader("memory-fresh", ttl);
                return Content(json, "application/json");
            }

            // 3. 普通请求：fresh 没有，再读 stale，并后台刷新。
            // force=true 时不走 stale，确保强制刷新真的重新构建。
            if (!force && _cache.TryGetValue(staleKey, out object stale))
            {
                _ = Task.Run(RefreshSectorCacheQuietlyAsync);

                var ttl = TimeSpan.FromMinutes(10);
                var json = JsonSerializer.Serialize(stale, jsonOptions);

                await TrySetRedisAsync(json, ttl);

                SetCacheHeader("memory-stale", ttl);
                return Content(json, "application/json");
            }

            // 4. 防止并发重复构建。
            if (!await _sectorsRefreshLock.WaitAsync(0))
            {
                SetCacheHeader("refreshing");
                return StatusCode(503, "板块基金雷达正在刷新，请稍后重试");
            }

            try
            {
                // 5. 拿到锁后再检查一次 Redis，避免等待期间别的请求已经写入。
                if (!force)
                {
                    var cached = await TryGetRedisAsync();
                    if (cached.HasValue)
                    {
                        SetCacheHeader("redis-after-lock");
                        return Content(cached.ToString(), "application/json");
                    }
                }

                // 6. 拿到锁后再检查一次内存 fresh。
                if (!force && _cache.TryGetValue(freshKey, out fresh))
                {
                    var ttl = GetExternalDataFreshTtl();
                    var json = JsonSerializer.Serialize(fresh, jsonOptions);

                    await TrySetRedisAsync(json, ttl);

                    SetCacheHeader("memory-fresh-after-lock", ttl);
                    return Content(json, "application/json");
                }

                // 7. 拿到锁后再检查一次 stale。
                if (!force && _cache.TryGetValue(staleKey, out stale))
                {
                    var ttl = TimeSpan.FromMinutes(10);
                    var json = JsonSerializer.Serialize(stale, jsonOptions);

                    await TrySetRedisAsync(json, ttl);

                    SetCacheHeader("memory-stale-after-lock", ttl);
                    return Content(json, "application/json");
                }

                // 8. 真正重新构建。
                var payload = await BuildSectorRadarPayloadAsync();

                SetSectorRadarCache(payload);

                var payloadTtl = GetExternalDataFreshTtl();
                var payloadJson = JsonSerializer.Serialize(payload, jsonOptions);

                await TrySetRedisAsync(payloadJson, payloadTtl);

                try { await _marketCache.SetAsync("sector_radar_v2", payload, payloadTtl, TimeSpan.FromDays(3), "build"); } catch { }

                SetCacheHeader("build", payloadTtl);
                return Content(payloadJson, "application/json");
            }
            catch (Exception ex)
            {
                // 9. 构建失败时，如果还有 stale，就兜底返回 stale。
                if (_cache.TryGetValue(staleKey, out object fallbackStale))
                {
                    var json = JsonSerializer.Serialize(fallbackStale, jsonOptions);

                    SetCacheHeader("memory-stale-fallback", TimeSpan.FromMinutes(10));
                    return Content(json, "application/json");
                }

                // 9b. 内存 stale 也没有，尝试数据库 stale。
                try
                {
                    var (dbStale, _) = await _marketCache.TryGetStaleAsync<object>("sector_radar_v2", TimeSpan.FromDays(3));
                    if (dbStale != null)
                    {
                        SetCacheHeader("db-stale-fallback");
                        var json = JsonSerializer.Serialize(dbStale, jsonOptions);
                        return Content(json, "application/json");
                    }
                }
                catch { }

                SetCacheHeader("error");
                return StatusCode(500, $"板块基金雷达故障: {ex.Message}");
            }
            finally
            {
                _sectorsRefreshLock.Release();
            }
        }
        private async Task RefreshSectorCacheQuietlyAsync()
        {
            if (!await _sectorsRefreshLock.WaitAsync(0)) return;

            try
            {
                var payload = await BuildSectorRadarPayloadAsync();
                SetSectorRadarCache(payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 板块基金雷达后台刷新失败: {ex.Message}");
            }
            finally
            {
                _sectorsRefreshLock.Release();
            }
        }

        private void SetSectorRadarCache(object payload)
        {
            _cache.Set("FundSectorRadarV5", payload, GetExternalDataFreshTtl());
            _cache.Set("FundSectorRadarV5_Stale", payload, _staleExternalDataTtl);
        }

        private async Task<object> BuildSectorRadarPayloadAsync()
        {
            var allFunds = await GetAllFundsAsync();
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/123.0 Safari/537.36");
            client.DefaultRequestVersion = new Version(1, 1);

            var summaries = new List<SectorSummaryDto>();
            int rank = 1;
            foreach (var def in SectorDefinitions)
            {
                var matched = MatchFundsBySector(allFunds, def, 22);
                if (matched.Count == 0) continue;

                using var limiter = new SemaphoreSlim(8);
                var quoteTasks = matched.Take(18).Select(async fund =>
                {
                    await limiter.WaitAsync();
                    try { return await FetchFundQuoteAsync(client, fund, false); }
                    finally { limiter.Release(); }
                });

                var quotes = (await Task.WhenAll(quoteTasks)).Where(q => q.HasQuote).ToList();
                if (quotes.Count == 0) continue;

                double avgRate = Math.Round(quotes.Average(q => q.Rate), 2);
                summaries.Add(new SectorSummaryDto
                {
                    Key = def.Key,
                    Name = def.Name,
                    Rate = avgRate,
                    FundCount = matched.Count,
                    QuotedCount = quotes.Count,
                    StreakDays = avgRate > 0.005 ? 1 : (avgRate < -0.005 ? -1 : 0),
                    HoldingRank = rank++,
                    UpdatedAt = quotes.Select(q => q.UpdatedAt).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    PreviewFunds = quotes.OrderByDescending(q => q.Rate).Take(3).ToList()
                });
            }

            var ordered = summaries.OrderByDescending(s => s.Rate).ToList();
            return new
            {
                source = "东方财富/天天基金估算 + 本地基金名称主题归类",
                updatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                top = ordered.Take(30).ToList(),
                bottom = ordered.OrderBy(s => s.Rate).Take(30).ToList(),
                all = ordered
            };
        }
        private static string FormatMoneyWanYi(double value)
        {
            var abs = Math.Abs(value);
            if (abs >= 100000000) return Math.Round(value / 100000000, 2) + "亿";
            if (abs >= 10000) return Math.Round(value / 10000, 2) + "万";
            return Math.Round(value, 0).ToString("0");
        }

        private static readonly string[] CapitalFlowExcludedNameKeywords =
        {
            "概念",
            "新高",
            "近期新高",
            "百日新高",
            "融资",
            "融券",
            "沪股通",
            "深股通",
            "昨日涨停",
            "昨日连板",
            "预盈预增",
            "预亏预减",
            "ST",
            "次新",
            "高送转",
            "机构重仓",
            "MSCI",
            "富时罗素",
            "小米",
            "华为",
            "苹果",
            "中特估",
            "机器人概念",
            "CPO概念",
            "5G概念",
            "锂矿概念",
            "光纤概念",
            "周期股",
            "最近多板",
            "今日涨停",
            "昨日触板",
            "连板",
            "破净",
            "龙虎榜",
            "重仓",
            "社保",
            "QFII",
            "养老金",
            "证金",
            "中字头",
            "国企改革",
            "央企改革",
            "一带一路",
            "参股",
            "股权",
            "转债",
            "互联金融",
            "数字货币",
            "区块链",
            "元宇宙",
            "虚拟现实",
            "增强现实",
            "数据要素",
            "数据确权",
            "东数西算",
            "算力",
            "ChatGPT",
            "AIGC",
            "Sora",
            "人工智能",
            "低空经济",
            "飞行汽车",
            "商业航天",
            "卫星导航",
            "军民融合",
            "人形机器人",
            "工业母机",
            "减肥药",
            "预制菜",
            "网红",
            "直播",
            "短剧",
            "鸿蒙",
            "华为昇腾",
            "小米汽车",
            "特斯拉",
            "汽车芯片"
        };

        private static bool IsIndustryCapitalFlowRow(CapitalFlowRowDto row)
        {
            if (string.IsNullOrWhiteSpace(row.Name)) return false;
            return !CapitalFlowExcludedNameKeywords.Any(keyword =>
                row.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class CapitalFlowSourceException : HttpRequestException
        {
            public CapitalFlowSourceException(string message, IReadOnlyList<ExternalDataAttemptDto> attempts)
                : base(message)
            {
                Attempts = attempts;
            }

            public IReadOnlyList<ExternalDataAttemptDto> Attempts { get; }
        }

        [HttpGet("capital-flow")]
        public async Task<IActionResult> GetCapitalFlow(
            [FromQuery] bool force = false,
            [FromQuery] int limit = 30,
            [FromQuery] bool debug = false)
        {
            limit = Math.Clamp(limit, 10, 80);
            string freshKey = $"CapitalFlowV4_{limit}";
            string staleKey = $"CapitalFlowV4_{limit}_Stale";

            if (!force && _cache.TryGetValue<CapitalFlowPayloadDto>(freshKey, out var fresh) && fresh != null)
            {
                Response.Headers["X-App-Cache"] = "memory-fresh";
                return Ok(debug ? AttachCapitalFlowDebug(fresh, new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "memory-fresh", ParsedRowsCount = fresh.Rows.Count } }) : fresh);
            }

            if (!force && _cache.TryGetValue<CapitalFlowPayloadDto>(staleKey, out var stale) && stale != null)
            {
                _ = Task.Run(() => RefreshCapitalFlowCacheQuietlyAsync(limit));
                Response.Headers["X-App-Cache"] = "memory-stale";
                return Ok(CloneCapitalFlowPayload(stale, true, true, "主力资金流使用缓存数据", "cache", debug
                    ? new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "memory-stale", ParsedRowsCount = stale.Rows.Count } }
                    : null));
            }

            if (!await _capitalFlowRefreshLock.WaitAsync(0))
            {
                var fallbackWhileBusy = await TryGetCapitalFlowFallbackAsync(limit);
                if (fallbackWhileBusy != null)
                {
                    Response.Headers["X-App-Cache"] = "fallback-busy";
                    return Ok(CloneCapitalFlowPayload(fallbackWhileBusy, true, true, "主力资金流使用缓存数据", "cache", debug
                        ? new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "fallback-while-refresh", ParsedRowsCount = fallbackWhileBusy.Rows.Count } }
                        : null));
                }

                Response.Headers["X-App-Cache"] = "empty-busy";
                return Ok(CreateUnavailableCapitalFlowPayload(debug
                    ? new[] { new ExternalDataAttemptDto { Source = "capital-flow-lock", Error = "refresh in progress" } }
                    : null));
            }

            try
            {
                if (!force && _cache.TryGetValue<CapitalFlowPayloadDto>(freshKey, out fresh) && fresh != null)
                {
                    Response.Headers["X-App-Cache"] = "memory-fresh-after-lock";
                    return Ok(debug ? AttachCapitalFlowDebug(fresh, new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "memory-fresh-after-lock", ParsedRowsCount = fresh.Rows.Count } }) : fresh);
                }

                if (!force && _cache.TryGetValue<CapitalFlowPayloadDto>(staleKey, out stale) && stale != null)
                {
                    Response.Headers["X-App-Cache"] = "memory-stale-after-lock";
                    return Ok(CloneCapitalFlowPayload(stale, true, true, "主力资金流使用缓存数据", "cache", debug
                        ? new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "memory-stale-after-lock", ParsedRowsCount = stale.Rows.Count } }
                        : null));
                }

                var payload = await BuildCapitalFlowPayloadAsync(limit, debug);
                await SetCapitalFlowCacheAsync(limit, payload);
                Response.Headers["X-App-Cache"] = "build";
                return Ok(payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 主力资金流向获取失败: {ex.Message}");
                var failureAttempts = ex is CapitalFlowSourceException sourceException
                    ? sourceException.Attempts.ToList()
                    : new List<ExternalDataAttemptDto> { new() { Source = "capital-flow", Error = ex.Message } };

                var fallback = await TryGetCapitalFlowFallbackAsync(limit);
                if (fallback != null)
                {
                    Response.Headers["X-App-Cache"] = "error-stale";
                    var fallbackAttempts = failureAttempts
                        .Concat(new[] { new ExternalDataAttemptDto { Source = "capital-flow-cache", CacheHit = true, CacheSource = "fallback-after-error", Error = ex.Message, ParsedRowsCount = fallback.Rows.Count } });
                    return Ok(CloneCapitalFlowPayload(fallback, true, true, "主力资金流使用缓存数据", "cache", debug
                        ? fallbackAttempts
                        : null));
                }

                Response.Headers["X-App-Cache"] = "empty";
                return Ok(CreateUnavailableCapitalFlowPayload(debug
                    ? failureAttempts
                    : null));
            }
            finally
            {
                _capitalFlowRefreshLock.Release();
            }
        }

        private async Task RefreshCapitalFlowCacheQuietlyAsync(int limit)
        {
            if (!await _capitalFlowRefreshLock.WaitAsync(0)) return;

            try
            {
                var payload = await BuildCapitalFlowPayloadAsync(limit, false);
                await SetCapitalFlowCacheAsync(limit, payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 主力资金流向后台刷新失败: {ex.Message}");
            }
            finally
            {
                _capitalFlowRefreshLock.Release();
            }
        }

        private async Task SetCapitalFlowCacheAsync(int limit, CapitalFlowPayloadDto payload)
        {
            var cachePayload = CloneCapitalFlowPayload(payload, payload.IsFallback, payload.IsStale, payload.Message, payload.Source);
            _cache.Set($"CapitalFlowV4_{limit}", cachePayload, _capitalFlowFreshTtl);
            _cache.Set($"CapitalFlowV4_{limit}_Stale", cachePayload, _capitalFlowStaleTtl);

            try
            {
                var db = _redis.GetDatabase();
                var json = JsonSerializer.Serialize(cachePayload, GlobalIndicesJsonOptions);
                await db.StringSetAsync(CapitalFlowLatestCacheKey, json, _capitalFlowStaleTtl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[capital-flow] redis cache write failed: {ex.Message}");
            }

            try
            {
                await _marketCache.SetAsync($"capital_flow_sector_v2_{limit}", cachePayload, _capitalFlowFreshTtl, TimeSpan.FromDays(7), "build");
            }
            catch { }
        }

        private async Task<CapitalFlowPayloadDto?> TryGetCapitalFlowFallbackAsync(int limit)
        {
            if (_cache.TryGetValue<CapitalFlowPayloadDto>($"CapitalFlowV4_{limit}_Stale", out var stale) && stale != null)
            {
                return stale;
            }

            if (_cache.TryGetValue<CapitalFlowPayloadDto>(CapitalFlowLatestCacheKey, out var latest) && latest != null)
            {
                return latest;
            }

            try
            {
                var db = _redis.GetDatabase();
                var cached = await db.StringGetAsync(CapitalFlowLatestCacheKey);
                if (!cached.HasValue) return null;
                var payload = JsonSerializer.Deserialize<CapitalFlowPayloadDto>(cached.ToString(), GlobalIndicesJsonOptions);
                if (payload == null) return null;
                payload.Rows ??= new List<CapitalFlowRowDto>();
                payload.Inflow ??= new List<CapitalFlowRowDto>();
                payload.Outflow ??= new List<CapitalFlowRowDto>();
                if (payload.Rows.Count == 0 && payload.Inflow.Count == 0 && payload.Outflow.Count == 0) return null;
                _cache.Set(CapitalFlowLatestCacheKey, payload, _capitalFlowStaleTtl);
                return payload;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[capital-flow] redis cache read failed: {ex.Message}");
            }

            try
            {
                var (dbStale, _) = await _marketCache.TryGetStaleAsync<CapitalFlowPayloadDto>($"capital_flow_sector_v2_{limit}", TimeSpan.FromDays(7));
                if (dbStale != null && (dbStale.Rows?.Count > 0 || dbStale.Inflow?.Count > 0))
                {
                    _cache.Set($"CapitalFlowV4_{limit}_Stale", dbStale, _capitalFlowStaleTtl);
                    return dbStale;
                }
            }
            catch { }

            return null;
        }

        private static CapitalFlowPayloadDto AttachCapitalFlowDebug(CapitalFlowPayloadDto payload, IEnumerable<ExternalDataAttemptDto> attempts)
        {
            return CloneCapitalFlowPayload(payload, payload.IsFallback, payload.IsStale, payload.Message, payload.Source, attempts);
        }

        private static object BuildCapitalFlowDebug(IEnumerable<ExternalDataAttemptDto> attempts, bool cacheHit, string? cacheSource = null)
        {
            var list = attempts.ToList();
            var selected = list.LastOrDefault(x => x.ParsedRowsCount > 0)
                ?? list.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.Error))
                ?? list.LastOrDefault();
            return new
            {
                source = selected?.Source ?? string.Empty,
                statusCode = selected?.StatusCode ?? 0,
                url = selected?.Url,
                parsedRowsCount = selected?.ParsedRowsCount ?? 0,
                cacheHit,
                cacheSource,
                error = selected?.Error,
                attempts = list
            };
        }

        private static CapitalFlowPayloadDto CloneCapitalFlowPayload(
            CapitalFlowPayloadDto payload,
            bool isFallback,
            bool isStale,
            string message,
            string? sourceOverride = null,
            IEnumerable<ExternalDataAttemptDto>? attempts = null)
        {
            return new CapitalFlowPayloadDto
            {
                Source = sourceOverride ?? payload.Source,
                UpdatedAt = payload.UpdatedAt,
                IsFallback = isFallback,
                IsStale = isStale,
                Message = message,
                Rows = payload.Rows?.ToList() ?? new List<CapitalFlowRowDto>(),
                Inflow = payload.Inflow?.ToList() ?? new List<CapitalFlowRowDto>(),
                Outflow = payload.Outflow?.ToList() ?? new List<CapitalFlowRowDto>(),
                Debug = attempts == null ? null : BuildCapitalFlowDebug(attempts, true, sourceOverride ?? payload.Source)
            };
        }

        private static CapitalFlowPayloadDto CreateUnavailableCapitalFlowPayload(IEnumerable<ExternalDataAttemptDto>? attempts = null)
        {
            return new CapitalFlowPayloadDto
            {
                Source = string.Empty,
                UpdatedAt = string.Empty,
                IsFallback = false,
                IsStale = true,
                Message = "主力资金流暂不可用：实时源失败且暂无缓存",
                Rows = new List<CapitalFlowRowDto>(),
                Inflow = new List<CapitalFlowRowDto>(),
                Outflow = new List<CapitalFlowRowDto>(),
                Debug = attempts == null ? null : BuildCapitalFlowDebug(attempts, false)
            };
        }

        private async Task<CapitalFlowPayloadDto> BuildCapitalFlowPayloadAsync(int limit, bool debug)
        {
            var attempts = new List<ExternalDataAttemptDto>();
            var sources = new[]
            {
                new { Name = "东方财富行业板块主力资金", Key = "industry", Fs = "m:90+t:2" }
            };

            var http = _httpClientFactory.CreateClient("EastMoneyQuote");
            foreach (var source in sources)
            {
                try
                {
                    var inflowTask = FetchCapitalFlowRowsAsync(http, source.Key, source.Fs, 1, limit, attempts);
                    var outflowTask = FetchCapitalFlowRowsAsync(http, source.Key, source.Fs, 0, limit, attempts);
                    await Task.WhenAll(inflowTask, outflowTask);

                    var inflow = inflowTask.Result
                        .Where(IsIndustryCapitalFlowRow)
                        .OrderByDescending(x => x.MainNet)
                        .Take(limit)
                        .ToList();

                    var outflow = outflowTask.Result
                        .Where(IsIndustryCapitalFlowRow)
                        .OrderBy(x => x.MainNet)
                        .Take(limit)
                        .ToList();

                    var rows = inflow.Concat(outflow)
                        .GroupBy(x => x.Code)
                        .Select(g => g.OrderByDescending(x => Math.Abs(x.MainNet)).First())
                        .OrderByDescending(x => x.MainNet)
                        .ToList();

                    if (rows.Count == 0)
                    {
                        attempts.Add(new ExternalDataAttemptDto { Source = source.Key, Error = "parsed rows empty" });
                        continue;
                    }

                    return new CapitalFlowPayloadDto
                    {
                        Source = source.Name,
                        UpdatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                        IsFallback = false,
                        IsStale = false,
                        Message = string.Empty,
                        Rows = rows,
                        Inflow = inflow,
                        Outflow = outflow,
                        Debug = debug ? BuildCapitalFlowDebug(attempts, false) : null
                    };
                }
                catch (Exception ex)
                {
                    attempts.Add(new ExternalDataAttemptDto { Source = source.Key, Error = ex.Message });
                }
            }

            throw new CapitalFlowSourceException("主力资金流实时源全部不可用", attempts);
        }

        private static async Task<List<CapitalFlowRowDto>> FetchCapitalFlowRowsAsync(
            HttpClient http,
            string source,
            string fs,
            int po,
            int limit,
            List<ExternalDataAttemptDto> attempts)
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int requestSize = Math.Clamp(limit * 5, 120, 300);
            var hosts = new[] { "push2.eastmoney.com", "82.push2.eastmoney.com", "84.push2.eastmoney.com" };
            string? body = null;
            string url = string.Empty;
            var flowSide = po > 0 ? "inflow" : "outflow";
            foreach (var host in hosts)
            {
                url = $"https://{host}/api/qt/clist/get?pn=1&pz={requestSize}&po={po}&np=1&fltt=2&invt=2&fid=f62&fs={Uri.EscapeDataString(fs)}&fields=f12,f14,f3,f62,f184,f66,f72&_={ts}";
                try
                {
                    body = await FetchWithRetryAsync(http, url, attempts: attempts, source: $"capital-flow-{source}-{flowSide}-{host}");
                    break;
                }
                catch when (host != hosts[^1])
                {
                    // Try another EastMoney quote host before falling back to stale cache.
                }
            }
            if (string.IsNullOrWhiteSpace(body)) throw new HttpRequestException($"capital-flow-{source}-{flowSide}: all hosts failed");
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind == JsonValueKind.Null ||
                !data.TryGetProperty("diff", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                attempts.Add(new ExternalDataAttemptDto { Source = $"capital-flow-{source}-parse", Url = url, StatusCode = 200, Error = "diff missing" });
                return new List<CapitalFlowRowDto>();
            }

            var result = new List<CapitalFlowRowDto>();
            foreach (var item in list.EnumerateArray())
            {
                string code = item.TryGetProperty("f12", out var f12) ? f12.GetString() ?? "" : "";
                string name = item.TryGetProperty("f14", out var f14) ? f14.GetString() ?? "" : "";
                double rate = item.TryGetProperty("f3", out var f3) && f3.ValueKind == JsonValueKind.Number ? f3.GetDouble() : 0;
                double mainNet = item.TryGetProperty("f62", out var f62) && f62.ValueKind == JsonValueKind.Number ? f62.GetDouble() : 0;
                double mainRatio = item.TryGetProperty("f184", out var f184) && f184.ValueKind == JsonValueKind.Number ? f184.GetDouble() : 0;
                double superNet = item.TryGetProperty("f66", out var f66) && f66.ValueKind == JsonValueKind.Number ? f66.GetDouble() : 0;
                double bigNet = item.TryGetProperty("f72", out var f72) && f72.ValueKind == JsonValueKind.Number ? f72.GetDouble() : 0;

                if (string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new CapitalFlowRowDto
                {
                    Code = code,
                    Name = name,
                    Rate = Math.Round(rate, 2),
                    MainNet = mainNet,
                    MainNetText = FormatMoneyWanYi(mainNet),
                    MainRatio = Math.Round(mainRatio, 2),
                    SuperNet = superNet,
                    BigNet = bigNet
                });
            }

            attempts.Add(new ExternalDataAttemptDto
            {
                Source = $"capital-flow-{source}-parse",
                Url = url,
                StatusCode = 200,
                RawRowsCount = list.GetArrayLength(),
                ParsedRowsCount = result.Count
            });
            return result;
        }
        [HttpGet("sector-details")]
        public async Task<IActionResult> GetSectorDetails([FromQuery] string secCode)
        {
            // 兼容旧前端：secCode 现在既可以传板块 key，也可以传板块名。
            if (string.IsNullOrWhiteSpace(secCode)) return BadRequest("缺少板块识别码");
            return await GetSectorFunds(secCode, 20, false);
        }

        [HttpGet("sector-funds")]
        public async Task<IActionResult> GetSectorFunds([FromQuery] string sectorName, [FromQuery] int limit = 20, [FromQuery] bool force = false)
        {
            if (string.IsNullOrWhiteSpace(sectorName)) return BadRequest("缺少板块名称");
            limit = Math.Clamp(limit, 5, 40);
            var def = ResolveSector(sectorName);
            string cacheKey = $"SectorFundsV5_{def.Key}_{limit}";
            string dbCacheKey = $"sector_funds_v2_{def.Key}_{limit}";
            if (!force && _cache.TryGetValue(cacheKey, out object cached)) return Ok(cached);

            if (!force)
            {
                var (dbData, _) = await _marketCache.TryGetAsync<object>(dbCacheKey);
                if (dbData != null)
                {
                    _cache.Set(cacheKey, dbData, TimeSpan.FromMinutes(3));
                    return Ok(dbData);
                }
            }

            try
            {
                var quotes = await BuildSectorQuotesAsync(def, limit, withMonthRate: true);
                var available = quotes.Where(q => q.HasQuote).ToList();
                double avgRate = available.Count > 0 ? Math.Round(available.Average(q => q.Rate), 2) : 0;
                var payload = new
                {
                    key = def.Key,
                    name = def.Name,
                    rate = avgRate,
                    fundCount = quotes.Count,
                    updatedAt = available.Select(q => q.UpdatedAt).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    funds = quotes.OrderByDescending(q => q.Rate).ToList()
                };
                _cache.Set(cacheKey, payload, TimeSpan.FromMinutes(3));
                try { await _marketCache.SetAsync(dbCacheKey, payload, TimeSpan.FromMinutes(10), TimeSpan.FromDays(3), "build"); } catch { }
                return Ok(payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sector-funds] build failed for {def.Key}: {ex.Message}");
                try
                {
                    var (dbStale, _) = await _marketCache.TryGetStaleAsync<object>(dbCacheKey, TimeSpan.FromDays(3));
                    if (dbStale != null)
                    {
                        _cache.Set(cacheKey, dbStale, TimeSpan.FromMinutes(3));
                        return Ok(dbStale);
                    }
                }
                catch { }
                return StatusCode(500, $"找基失败: {ex.Message}");
            }
        }



        private sealed class NewsItemDto
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string Summary { get; init; } = string.Empty;
            public string ShowTime { get; init; } = string.Empty;
            public string TimeText { get; init; } = string.Empty;
            public string DateText { get; init; } = string.Empty;
            public string Source { get; init; } = "东方财富7x24";
            public string Url { get; init; } = string.Empty;
            public bool Important { get; init; }
            public string Sentiment { get; init; } = "neutral";
            public int ImpactScore { get; init; }
            public string? MatchedFundCode { get; init; }
            public string? MatchedFundName { get; init; }
            public string[] Tags { get; init; } = Array.Empty<string>();
            public long Sort { get; init; }
        }

        private static readonly string[] ImportantNewsKeywords = new[]
        {
            "突发", "重磅", "重大", "国务院", "国常会", "发改委", "工信部", "商务部", "财政部", "央行", "证监会", "交易所",
            "降息", "加息", "利率", "降准", "CPI", "PPI", "GDP", "PMI", "非农", "美联储", "鲍威尔", "特朗普", "关税",
            "制裁", "冲突", "战争", "油轮", "原油", "黄金", "稀土", "锂", "半导体", "人工智能", "地产", "政策", "收储", "限购", "补贴", "财报", "业绩", "订单"
        };

        private static readonly string[] PositiveNewsKeywords = new[]
        {
            "上涨", "涨", "走高", "新高", "增长", "上调", "突破", "利好", "扩产", "订单", "补贴", "降息", "降准", "回购", "增持", "放松", "优化", "超预期", "批准", "支持", "刺激"
        };

        private static readonly string[] NegativeNewsKeywords = new[]
        {
            "下跌", "跌", "走低", "下降", "下滑", "下调", "亏损", "制裁", "禁令", "关税", "调查", "处罚", "暂停", "限制", "拒绝", "战争", "冲突", "爆炸", "违约", "不及预期"
        };

        private static string StripJsonp(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "{}";
            var match = Regex.Match(text, @"^[^(]*\((.*)\)\s*;?\s*$", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : text;
        }

        private static string ToDateText(DateTime dt) => dt.ToString("MM月dd日 dddd", new System.Globalization.CultureInfo("zh-CN"));
        private static string ToTimeText(DateTime dt) => dt.ToString("HH:mm");

        private static string ClassifySentiment(string text)
        {
            int pos = PositiveNewsKeywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
            int neg = NegativeNewsKeywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (pos > neg) return "positive";
            if (neg > pos) return "negative";
            return "neutral";
        }

        private static int EstimateImpactScore(string text, bool important, int matchScore)
        {
            int score = important ? 3 : 1;
            if (text.Contains("突发") || text.Contains("重磅") || text.Contains("重大")) score += 2;
            if (text.Contains("国务院") || text.Contains("国常会") || text.Contains("央行") || text.Contains("证监会") || text.Contains("美联储")) score += 1;
            if (text.Contains("涨停") || text.Contains("跌停") || text.Contains("暴涨") || text.Contains("暴跌")) score += 1;
            if (matchScore >= 60) score += 1;
            return Math.Clamp(score, 0, 5);
        }

        private async Task<List<NewsItemDto>> FetchEastMoneyFastNewsAsync(int limit)
        {
            limit = Math.Clamp(limit, 20, 120);
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string callback = $"jQuery3510_{ts}";
            string url = $"https://np-weblist.eastmoney.com/comm/web/getFastNewsList?client=web&biz=web_724&fastColumn=102&sortEnd=&pageSize={limit}&req_trace={ts}&_={ts + 1}&callback={callback}";

            var client = _httpClientFactory.CreateClient("EastMoneyQuote");

            string body = await client.GetStringAsync(url);
            string json = StripJsonp(body);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var list = root.GetProperty("data").GetProperty("fastNewsList");
            var result = new List<NewsItemDto>();

            foreach (var item in list.EnumerateArray())
            {
                string id = item.TryGetProperty("code", out var codeProp) ? codeProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                string title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                string summary = item.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() ?? title : title;
                string showTime = item.TryGetProperty("showTime", out var timeProp) ? timeProp.GetString() ?? "" : "";
                long sort = 0;
                if (item.TryGetProperty("realSort", out var sortProp)) long.TryParse(sortProp.GetString(), out sort);
                int titleColor = 0;
                if (item.TryGetProperty("titleColor", out var colorProp) && colorProp.ValueKind == JsonValueKind.Number) titleColor = colorProp.GetInt32();

                var stockCodes = new List<string>();
                if (item.TryGetProperty("stockList", out var stockProp) && stockProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in stockProp.EnumerateArray())
                    {
                        var sv = s.GetString();
                        if (!string.IsNullOrWhiteSpace(sv)) stockCodes.Add(sv);
                    }
                }

                DateTime dt = ChinaNow();
                DateTime.TryParse(showTime, out dt);
                string text = title + " " + summary;
                bool important = titleColor != 0 || ImportantNewsKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
                string sentiment = ClassifySentiment(text);

                result.Add(new NewsItemDto
                {
                    Id = id,
                    Title = string.IsNullOrWhiteSpace(title) ? summary : title,
                    Summary = summary,
                    ShowTime = string.IsNullOrWhiteSpace(showTime) ? dt.ToString("yyyy-MM-dd HH:mm:ss") : showTime,
                    DateText = ToDateText(dt),
                    TimeText = ToTimeText(dt),
                    Source = "东方财富7x24",
                    Url = id.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? id : $"https://finance.eastmoney.com/a/{id}.html",
                    Important = important,
                    Sentiment = sentiment,
                    ImpactScore = EstimateImpactScore(text, important, 0),
                    Tags = stockCodes.ToArray(),
                    Sort = sort == 0 ? new DateTimeOffset(dt).ToUnixTimeMilliseconds() : sort
                });
            }

            return result.OrderByDescending(x => x.Sort).ToList();
        }

        private static readonly Dictionary<string, string[]> SectorKeywordMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["黄金"] = new[] { "黄金", "金价", "上海金", "伦敦金", "贵金属", "央行购金" },
            ["半导体"] = new[] { "半导体", "芯片", "集成电路", "先进制程", "光刻", "晶圆", "封测", "存储" },
            ["人工智能"] = new[] { "人工智能", "AI", "大模型", "算力", "机器人", "数据中心", "云计算" },
            ["科技"] = new[] { "科技", "互联网", "软件", "数字经济", "信创", "计算机" },
            ["新能源"] = new[] { "新能源", "光伏", "储能", "锂电", "固态电池", "风电", "电动车", "新能源汽车" },
            ["地产"] = new[] { "地产", "房地产", "楼市", "住房", "房贷", "深圳", "限购", "收储" },
            ["医药"] = new[] { "医药", "创新药", "医疗", "生物", "CRO", "疫苗", "医保", "器械" },
            ["军工"] = new[] { "军工", "国防", "航空", "航天", "导弹", "卫星", "无人机" },
            ["银行"] = new[] { "银行", "息差", "信贷", "贷款", "存款", "不良率" },
            ["证券"] = new[] { "券商", "证券", "资本市场", "两融", "并购重组", "IPO" },
            ["有色"] = new[] { "有色", "铜", "铝", "锂", "稀土", "钨", "钼", "矿", "金属" },
            ["油气"] = new[] { "原油", "石油", "油气", "OPEC", "WTI", "布伦特", "天然气" },
            ["消费"] = new[] { "消费", "白酒", "食品", "饮料", "家电", "旅游", "零售" },
            ["农业"] = new[] { "农业", "农产品", "粮食", "养殖", "猪肉", "种业", "牧渔" },
            ["债券"] = new[] { "债券", "国债", "利率债", "信用债", "收益率", "转债" },
            ["港股"] = new[] { "港股", "恒生", "南向", "香港", "中概股" },
            ["美股"] = new[] { "美股", "纳斯达克", "标普", "道指", "英伟达", "苹果", "微软" }
        };

        private static HashSet<string> BuildFundKeywords(MyFundConfig fund)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fund.FundCode)) set.Add(fund.FundCode);
            var name = fund.FundName ?? string.Empty;
            string clean = NormalizeFundName(name)
                .Replace("ETF", "")
                .Replace("联接", "")
                .Replace("指数", "")
                .Replace("增强", "")
                .Replace("混合", "")
                .Replace("股票", "")
                .Replace("债券", "")
                .Replace("基金", "")
                .Replace("C", "")
                .Replace("A", "");

            string[] commonPrefixes = { "天弘", "华富", "国泰", "招商", "南方", "易方达", "博时", "工银瑞信", "中银", "华夏", "嘉实", "建信", "鹏华", "广发", "富国", "银华", "汇添富", "前海开源", "国投瑞银" };
            foreach (var prefix in commonPrefixes)
            {
                if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) clean = clean[prefix.Length..];
            }

            if (clean.Length >= 2) set.Add(clean);
            foreach (Match m in Regex.Matches(name, @"[\u4e00-\u9fa5A-Za-z0-9]{2,}"))
            {
                string token = m.Value.Trim();
                if (token.Length >= 2 && !new[] { "联接", "指数", "基金", "混合", "股票", "债券", "增强", "发起", "定开" }.Contains(token)) set.Add(token);
            }

            foreach (var kv in SectorKeywordMap)
            {
                if (kv.Value.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase) || clean.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var k in kv.Value) set.Add(k);
                    set.Add(kv.Key);
                }
            }

            foreach (var def in SectorDefinitions)
            {
                if (def.Include.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    set.Add(def.Name);
                    foreach (var k in def.Include) set.Add(k);
                }
            }
            return set;
        }

        private static (int score, string[] tags) ScoreNewsForFund(NewsItemDto news, MyFundConfig fund)
        {
            var keywords = BuildFundKeywords(fund);
            var text = (news.Title + " " + news.Summary + " " + string.Join(" ", news.Tags)).ToUpperInvariant();
            var hit = new List<string>();
            int score = 0;

            if (!string.IsNullOrWhiteSpace(fund.FundCode) && text.Contains(fund.FundCode.ToUpperInvariant()))
            {
                score += 100;
                hit.Add(fund.FundCode);
            }

            foreach (var kw in keywords)
            {
                if (kw.Length < 2) continue;
                if (text.Contains(kw.ToUpperInvariant()))
                {
                    score += Math.Min(30, kw.Length * 4);
                    hit.Add(kw);
                }
            }

            return (score, hit.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray());
        }

        [HttpGet("news")]
        public async Task<IActionResult> GetNews([FromQuery] string? username, [FromQuery] string mode = "global", [FromQuery] bool important = false, [FromQuery] int limit = 80, [FromQuery] bool force = false)
        {
            limit = Math.Clamp(limit, 20, 120);
            mode = string.IsNullOrWhiteSpace(mode) ? "global" : mode.Trim().ToLowerInvariant();
            string cacheKey = $"NewsV3_{mode}_{username}_{important}_{limit}";
            if (!force && _cache.TryGetValue(cacheKey, out object cached)) return Ok(cached);

            try
            {
                var allNews = await FetchEastMoneyFastNewsAsync(mode == "holding" ? Math.Max(limit * 3, 120) : limit);
                List<NewsItemDto> finalList;

                if (mode == "holding")
                {
                    if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供用户名");
                    var funds = await _context.MyFunds.AsNoTracking().Where(f => f.Username == username).ToListAsync();
                    var matched = new List<NewsItemDto>();

                    foreach (var news in allNews)
                    {
                        MyFundConfig? bestFund = null;
                        int bestScore = 0;
                        string[] bestTags = Array.Empty<string>();
                        foreach (var fund in funds)
                        {
                            var score = ScoreNewsForFund(news, fund);
                            if (score.score > bestScore)
                            {
                                bestScore = score.score;
                                bestFund = fund;
                                bestTags = score.tags;
                            }
                        }

                        if (bestFund != null && bestScore >= 16)
                        {
                            string text = news.Title + " " + news.Summary;
                            bool isImportant = news.Important || bestScore >= 60;
                            if (important && !isImportant) continue;
                            matched.Add(new NewsItemDto
                            {
                                Id = news.Id,
                                Title = news.Title,
                                Summary = news.Summary,
                                ShowTime = news.ShowTime,
                                TimeText = news.TimeText,
                                DateText = news.DateText,
                                Source = news.Source,
                                Url = news.Url,
                                Important = isImportant,
                                Sentiment = news.Sentiment,
                                ImpactScore = EstimateImpactScore(text, isImportant, bestScore),
                                MatchedFundCode = bestFund.FundCode,
                                MatchedFundName = bestFund.FundName,
                                Tags = bestTags,
                                Sort = news.Sort
                            });
                        }
                    }

                    finalList = matched.OrderByDescending(n => n.Important).ThenByDescending(n => n.Sort).Take(limit).ToList();
                }
                else
                {
                    finalList = allNews.Where(n => !important || n.Important).Take(limit).ToList();
                }

                var payload = new
                {
                    mode,
                    source = "东方财富7x24快讯",
                    updatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    count = finalList.Count,
                    items = finalList
                };
                _cache.Set(cacheKey, payload, TimeSpan.FromSeconds(mode == "holding" ? 90 : 45));
                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"资讯雷达故障: {ex.Message}");
            }
        }

        [HttpGet("holding-news")]
        public async Task<IActionResult> GetHoldingNews([FromQuery] string username, [FromQuery] bool important = false, [FromQuery] int limit = 80, [FromQuery] bool force = false)
        {
            return await GetNews(username, "holding", important, limit, force);
        }


        [HttpPost("save-archive")]
        public async Task<IActionResult> SaveArchive([FromBody] ArchiveRequestDto req)
        {
            if (req == null) return BadRequest(new { success = false, message = "请求体为空" });
            if (string.IsNullOrWhiteSpace(req.Username)) return BadRequest(new { success = false, message = "缺少用户名" });
            if (string.IsNullOrWhiteSpace(req.DateStr)) return BadRequest(new { success = false, message = "缺少日期 DateStr" });

            try
            {
                if (!DateTime.TryParse(req.DateStr, out var parsedDate))
                    return BadRequest(new { success = false, message = $"日期格式无效: {req.DateStr}" });
                var date = parsedDate.Date;
                var incoming = new List<DailyArchive>();

                if (req.Total != null)
                {
                    incoming.Add(new DailyArchive
                    {
                        Username = req.Username,
                        FundCode = "TOTAL",
                        FundName = string.IsNullOrWhiteSpace(req.Total.FundName) ? "总持仓" : req.Total.FundName!,
                        RecordDate = date,
                        Assets = req.Total.Assets,
                        DailyProfit = req.Total.DailyProfit,
                        DailyRate = req.Total.DailyRate,
                        TotalProfit = req.Total.TotalProfit,
                        TotalRate = req.Total.TotalRate
                    });
                }

                var skippedMissingCode = 0;
                if (req.Funds != null)
                {
                    foreach (var f in req.Funds)
                    {
                        if (string.IsNullOrWhiteSpace(f.FundCode))
                        {
                            skippedMissingCode++;
                            continue;
                        }
                        incoming.Add(new DailyArchive
                        {
                            Username = req.Username,
                            FundCode = f.FundCode.Trim(),
                            FundName = string.IsNullOrWhiteSpace(f.FundName) ? f.FundCode.Trim() : f.FundName!,
                            RecordDate = date,
                            Assets = f.Assets,
                            DailyProfit = f.DailyProfit,
                            DailyRate = f.DailyRate,
                            TotalProfit = f.TotalProfit,
                            TotalRate = f.TotalRate
                        });
                    }
                }

                if (incoming.Count == 0)
                {
                    return BadRequest(new { success = false, message = "封存数据为空，且没有可保存的基金代码" });
                }

                // 核心修复：不要因为 TOTAL 已存在就直接 return。
                // 旧逻辑会导致“总持仓有今日记录，但单只基金今日记录缺失”，也会保留已写坏的 TOTAL 金额。
                await UpsertDailyArchivesAsync(req.Username, date, incoming);
                await _context.SaveChangesAsync();
                ClearTodayCache(req.Username);

                return Ok(new
                {
                    success = true,
                    saved = incoming.Count,
                    date = date.ToString("yyyy-MM-dd"),
                    skippedMissingCode,
                    message = skippedMissingCode > 0
                        ? $"已保存 {incoming.Count} 条，跳过 {skippedMissingCode} 条缺少基金代码的数据"
                        : $"已保存 {incoming.Count} 条收益档案"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"封存失败: {ex.Message}" });
            }
        }

        [HttpPost("clear-pending")]
        public async Task<IActionResult> ClearPending([FromBody] ClearPendingRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username))
                return BadRequest(new { success = false, message = "缺少用户名" });
            if (string.IsNullOrWhiteSpace(req.FundCode))
                return BadRequest(new { success = false, message = "缺少基金代码" });

            var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == req.Username && f.FundCode == req.FundCode);
            if (fund == null) return NotFound(new { success = false, message = $"未找到基金 {req.FundCode}" });

            double oldPending = fund.PendingBuyAmount;
            ClearPendingBuy(fund);
            fund.LastSettledDate = null;
            fund.LastSettledProfit = 0;
            fund.LastSettledRate = 0;
            await _context.SaveChangesAsync();
            ClearTodayCache(req.Username);

            return Ok(new { success = true, message = $"已清除 {req.FundCode} 的 pending {oldPending:F2} 元", oldPending });
        }

        public class ClearPendingRequest
        {
            public string Username { get; set; } = "";
            public string FundCode { get; set; } = "";
        }

        [HttpGet("auto-archive-nightly")]
        public async Task<IActionResult> AutoArchiveNightly()
        {
            try
            {
                var localTime = ChinaNow();
                var today = localTime.Date;

                // 周末不封存
                if (localTime.DayOfWeek == DayOfWeek.Saturday || localTime.DayOfWeek == DayOfWeek.Sunday)
                    return Ok("周末休市，无需封存。");
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("无基金需要封存。");

                var userGroups = allFunds.GroupBy(f => f.Username);
                int savedCount = 0;
                foreach (var group in userGroups)
                {
                    string username = group.Key;
                    var userFunds = group.ToList();

                    // 🚀 终极碾压法则：夜间机器人的数据永远是最准的！绝对覆盖！
                    var existingRecords = await _context.DailyArchives
                        .Where(a => a.Username == username && a.RecordDate == today)
                        .ToListAsync();
                    if (existingRecords.Any())
                    {
                        //bool hasRealData = existingRecords.Any(r => r.FundCode == "TOTAL" && r.DailyRate != 0);
                        //if (hasRealData) continue; // 有真实数据才跳过
                        // 🚀 撤销保护锁，允许一天内无限次测试覆写
                        _context.DailyArchives.RemoveRange(existingRecords);
                        // 估值数据删掉重写
                    }

                    double totalAssets = 0;
                    double totalCost = 0;
                    double totalDailyProfit = 0; // 直接累加今日总收益，杜绝误差
                    double totalDailyBase = 0;   // 今日收益率分母：剥离/补回在途交易后的有效基数
                    double totalCurrentAssets = 0;
                    double totalRealized = 0;

                    // 获取今天的字符串，用于判断今日加仓
                    string todayStrSlash = today.ToString("yyyy/MM/dd");
                    string todayStrDash = today.ToString("yyyy-MM-dd");

                    foreach (var fund in userFunds)
                    {
                        totalRealized += fund.RealizedProfit;
                        if (fund.HoldShares <= 0) continue;
                        double pendingBuyAmount = GetActivePendingBuyAmount(fund, todayStrDash);
                        double confirmedHoldAmount = Math.Max(0, Math.Round(fund.HoldAmount - pendingBuyAmount, 2));
                        double confirmedCost = Math.Max(0, Math.Round((fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount) - pendingBuyAmount, 2));
                        totalAssets += confirmedHoldAmount;
                        totalCost += confirmedCost;

                        var todayRecord = await _context.FundRecords
                            .Where(r => r.FundCode == fund.FundCode && r.FetchTime >= today)
                            .OrderByDescending(r => r.FetchTime)
                            .FirstOrDefaultAsync();

                        // 算钱逻辑 (基础粗略版)
                        double dailyRate = todayRecord != null && Math.Abs(todayRecord.ActualRate) > 0.000001
                            ? todayRecord.ActualRate
                            : (todayRecord?.EstimatedRate ?? 0);

                        // ==========================================
                        // 🚀 核心补丁：后端必须像前端一样，精准剥离今日新军！
                        // 绝对禁止未确认的资金参与今日收益结算，掐断虚假印钞！
                        double baseAmount = GetDailyBaseAmount(fund, todayStrDash);
                        totalDailyBase += baseAmount;
                        double dailyProfit = fund.LastSettledDate == todayStrDash
                            ? fund.LastSettledProfit
                            : baseAmount * (dailyRate / 100.0);
                        // ==========================================



                        // 🚀 终极纠缠态补丁：用金额反推真实份额，再呼叫猎隼侦察兵！
                        double effectiveShares = GetEffectiveShares(fund, todayStrDash);


                        // 🚀 核心对齐补丁 1：高精度物理对齐！(调用猎隼侦察兵)
                        var realData = await GetTodayRealRateAsync(fund.FundCode, today.ToString("yyyy-MM-dd"), effectiveShares);

                        if (fund.LastSettledDate == todayStrDash)
                        {
                            dailyRate = fund.LastSettledRate;
                            dailyProfit = fund.LastSettledProfit;
                        }
                        else
                        {
                            // 1. 突击指令：只要拿到了官方真实收益率，立刻覆盖！绝对不能被物理利润的计算结果绑架
                            if (realData.rate.HasValue)
                            {
                                dailyRate = realData.rate.Value;
                                dailyProfit = baseAmount * (dailyRate / 100.0);
                            }

                            // 2. 狙击指令：如果东方财富数据完整，算出了高精度的绝对物理利润，再执行覆盖
                            if (realData.exactProfit.HasValue)
                            {
                                dailyProfit = realData.exactProfit.Value;
                            }
                        }

                        // 🚀 算历史总收益 (包含单只基金的落袋利润)
                        double cost = confirmedCost;
                        double currentAssets = fund.LastSettledDate == todayStrDash
                            ? confirmedHoldAmount
                            : confirmedHoldAmount + dailyProfit;
                        double totalProfit = currentAssets - cost + fund.RealizedProfit;
                        double totalRate = cost > 0 ? (totalProfit / cost * 100.0) : 0;
                        totalCurrentAssets += currentAssets;

                        // 自动归档只写 DailyArchives，不再修改 MyFunds.HoldAmount，避免和夜间清算重复滚存。

                        // 保存单只基金收益
                        _context.DailyArchives.Add(new DailyArchive
                        {
                            Username = username,
                            FundCode = fund.FundCode,
                            FundName = fund.FundName,
                            RecordDate = today,
                            Assets = Math.Round(currentAssets, 2), // 单只基金市值不含落袋，与前端一致
                            DailyProfit = Math.Round(dailyProfit, 2),
                            DailyRate = Math.Round(dailyRate, 2),
                            TotalProfit = Math.Round(totalProfit, 2),
                            TotalRate = Math.Round(totalRate, 2)
                        });
                        // 累加总阵地今日收益
                        totalDailyProfit += dailyProfit;
                    }

                    // 今日总收益率分母必须是当日有效持仓基数，不能用已结算后的市值重复稀释。
                    double totalDailyRate = totalDailyBase > 0 ? (totalDailyProfit / totalDailyBase) * 100.0 : 0;

                    double currentTotalAssetsAfter = totalCurrentAssets;

                    // 🚀 核心修复：把已落袋收益加回盈亏分子
                    double totalCampProfit = currentTotalAssetsAfter - totalCost + totalRealized;
                    double totalCampRate = totalCost > 0 ? (totalCampProfit / totalCost * 100.0) : 0;

                    // 总持仓市值只记录仍在持仓里的资产；落袋利润只进入累计盈亏分子。
                    double alignedTotalAssets = currentTotalAssetsAfter;

                    // 保存总持仓收益
                    _context.DailyArchives.Add(new DailyArchive
                    {
                        Username = username,
                        FundCode = "TOTAL",
                        FundName = "总持仓",
                        RecordDate = today,
                        Assets = alignedTotalAssets, // 对齐前端大屏的展示逻辑
                        DailyProfit = Math.Round(totalDailyProfit, 2),
                        DailyRate = Math.Round(totalDailyRate, 2),
                        TotalProfit = Math.Round(totalCampProfit, 2), // 已包含落袋利润
                        TotalRate = Math.Round(totalCampRate, 2)
                    });

                    savedCount++;
                }

                await _context.SaveChangesAsync();
                return Ok($"[{localTime:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动收益封存完毕！成功为 {savedCount} 位用户名生成了历史档案。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动封存引擎异常: {ex.Message}");
            }
        }

        [HttpGet("get-archives")]
        public async Task<IActionResult> GetArchives([FromQuery] string username, [FromQuery] string? fundCode = null, [FromQuery] int limit = 120)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("缺少账号");
            if (limit <= 0) limit = 120;
            if (limit > 500) limit = 500;

            var query = _context.DailyArchives
                .AsNoTracking()
                .Where(a => a.Username == username);

            if (!string.IsNullOrWhiteSpace(fundCode))
            {
                query = query.Where(a => a.FundCode == fundCode);
            }

            var records = await query
                .OrderByDescending(a => a.RecordDate)
                .ThenByDescending(a => a.Id)
                .Take(limit)
                .Select(a => new
                {
                    fundCode = a.FundCode,
                    fundName = a.FundName,
                    recordDate = a.RecordDate,
                    assets = a.Assets,
                    dailyProfit = a.DailyProfit,
                    dailyRate = a.DailyRate,
                    totalProfit = a.TotalProfit,
                    totalRate = a.TotalRate
                })
                .ToListAsync();

            return Ok(records);
        }

        [HttpGet("nav-history")]
        public async Task<IActionResult> GetNavHistory([FromQuery] string code, [FromQuery] string period = "1y")
        {
            if (string.IsNullOrWhiteSpace(code)) return BadRequest("缺少基金代码");

            string dbKey = $"fund_nav_history_{code}_{period}_v1";
            var (dbCached, dbSource) = await _marketCache.TryGetAsync<NavHistoryResponse>(dbKey);
            if (dbCached != null && dbSource != null && dbCached.Points.Count > 0)
            {
                Response.Headers["X-App-Cache"] = dbSource;
                return Ok(dbCached);
            }

            try
            {
                int limit = period switch
                {
                    "1m" => 30, "3m" => 90, "6m" => 180, "1y" => 365, "3y" => 365 * 3, "5y" => 365 * 5, _ => 365
                };

                var (navData, fetchSource) = await FetchFundNavHistoryAsync(code, limit);
                if (navData != null && navData.Count > 0)
                {
                    bool hasNav = navData.Any(p => p is NavPoint np && np.Nav > 0);
                    var response = new NavHistoryResponse
                    {
                        Available = true,
                        NavAvailable = hasNav,
                        Points = navData,
                        Message = hasNav ? "" : "当前仅有收益率归档，无真实净值",
                        Source = fetchSource,
                        UpdatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    var freshTtl = GetExternalDataFreshTtl();
                    await _marketCache.SetAsync(dbKey, response, freshTtl, TimeSpan.FromDays(30), fetchSource);
                    Response.Headers["X-App-Cache"] = "build";
                    return Ok(response);
                }

                Response.Headers["X-App-Cache"] = "empty";
                return Ok(new NavHistoryResponse { Available = false, Message = "基金历史净值暂无缓存，外部源不可达" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[nav-history] fetch failed for {code}: {ex.Message}");
                try
                {
                    var (stale, _) = await _marketCache.TryGetStaleAsync<NavHistoryResponse>(dbKey, TimeSpan.FromDays(30));
                    if (stale != null && stale.Points.Count > 0)
                    {
                        stale.IsFallback = true;
                        stale.Message = "使用缓存数据";
                        Response.Headers["X-App-Cache"] = "db-stale";
                        return Ok(stale);
                    }
                }
                catch { }
                Response.Headers["X-App-Cache"] = "empty";
                return Ok(new NavHistoryResponse { Available = false, Message = "基金历史净值暂无缓存，外部源不可达" });
            }
        }

        public class NavHistoryResponse
        {
            public bool Available { get; set; } = true;
            public bool NavAvailable { get; set; } = true;
            public bool IsFallback { get; set; }
            public List<NavPoint> Points { get; set; } = new();
            public string Message { get; set; } = "";
            public string Source { get; set; } = "";
            public string UpdatedAt { get; set; } = "";
        }

        public class NavPoint
        {
            public string Date { get; set; } = "";
            public double? Nav { get; set; }
            public double Rate { get; set; }
        }

        private async Task<(List<NavPoint>? data, string source)> FetchFundNavHistoryAsync(string code, int limit)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                string url = $"https://fund.eastmoney.com/pingzhongdata/{code}.js";
                string jsBody = await http.GetStringAsync(url);
                if (!string.IsNullOrWhiteSpace(jsBody) && jsBody.Contains("Data_netWorthTrend"))
                {
                    int start = jsBody.IndexOf("var Data_netWorthTrend=");
                    if (start >= 0)
                    {
                        start = jsBody.IndexOf('[', start);
                        if (start >= 0)
                        {
                            int depth = 0; int end = start;
                            for (int i = start; i < jsBody.Length && i < start + 500000; i++)
                            {
                                if (jsBody[i] == '[') depth++;
                                else if (jsBody[i] == ']') { depth--; if (depth == 0) { end = i + 1; break; } }
                            }
                            using var doc = JsonDocument.Parse(jsBody[start..end]);
                            var result = new List<NavPoint>();
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                if (!item.TryGetProperty("x", out var xProp) || !item.TryGetProperty("y", out var yProp)) continue;
                                long ts = xProp.GetInt64();
                                double nav = yProp.GetDouble();
                                string date = DateTimeOffset.FromUnixTimeMilliseconds(ts).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd");
                                double rate = item.TryGetProperty("equityReturn", out var rProp) && rProp.ValueKind == JsonValueKind.Number ? rProp.GetDouble() : 0;
                                result.Add(new NavPoint { Date = date, Nav = Math.Round(nav, 4), Rate = Math.Round(rate, 2) });
                            }
                            if (result.Count > limit) result = result.Skip(result.Count - limit).ToList();
                            if (result.Count > 0) return (result, "pingzhongdata");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[nav-history] pingzhongdata failed for {code}: {ex.Message}");
            }

            try
            {
                var archives = await _context.DailyArchives
                    .AsNoTracking()
                    .Where(a => a.FundCode == code)
                    .OrderBy(a => a.RecordDate)
                    .Select(a => new { a.RecordDate, a.DailyRate })
                    .ToListAsync();
                if (archives.Count > 0)
                {
                    var deduped = archives
                        .GroupBy(a => a.RecordDate)
                        .Select(g => new NavPoint
                        {
                            Date = g.Key.ToString("yyyy-MM-dd"),
                            Nav = null,
                            Rate = Math.Round(g.Last().DailyRate, 2)
                        })
                        .OrderBy(p => p.Date)
                        .TakeLast(limit)
                        .ToList();
                    if (deduped.Count > 0)
                    {
                        Console.WriteLine($"[nav-history] {code} DailyArchives fallback: {deduped.Count} rows (deduped from {archives.Count})");
                        return (deduped, "daily-archives");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[nav-history] DailyArchives fallback failed for {code}: {ex.Message}");
            }

            return (null, "none");
        }
        public async Task<IActionResult> GetPortfolioExposure([FromQuery] string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("缺少账号");

            var funds = await _context.MyFunds.AsNoTracking().Where(f => f.Username == username).ToListAsync();
            double total = funds.Sum(f => Math.Max(0, f.HoldAmount));
            if (total <= 0) return Ok(new { totalAmount = 0, exposures = Array.Empty<object>() });

            var exposures = SectorDefinitions.Select(def =>
            {
                var matched = funds.Where(f => def.Include.Any(k => f.FundName.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                                               !def.Exclude.Any(k => f.FundName.Contains(k, StringComparison.OrdinalIgnoreCase))).ToList();
                double amount = matched.Sum(f => Math.Max(0, f.HoldAmount));
                return new
                {
                    key = def.Key,
                    name = def.Name,
                    amount = Math.Round(amount, 2),
                    ratio = Math.Round(amount / total * 100.0, 2),
                    fundCount = matched.Count,
                    funds = matched.Select(f => new { code = f.FundCode, name = f.FundName, amount = Math.Round(f.HoldAmount, 2) }).ToList()
                };
            })
            .Where(x => x.amount > 0)
            .OrderByDescending(x => x.amount)
            .ToList();

            return Ok(new { totalAmount = Math.Round(total, 2), exposures });
        }

        [HttpGet("daily-report")]
        public async Task<IActionResult> GetDailyReport([FromQuery] string username, [FromQuery] string? date = null)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("缺少账号");
            DateTime targetDate = DateTime.TryParse(date, out var parsed) ? parsed.Date : ChinaNow().Date;

            var rows = await _context.DailyArchives
                .AsNoTracking()
                .Where(x => x.Username == username && x.RecordDate == targetDate)
                .ToListAsync();

            var total = rows.FirstOrDefault(x => x.FundCode == "TOTAL");
            var fundRows = rows.Where(x => x.FundCode != "TOTAL").ToList();
            var best = fundRows.OrderByDescending(x => x.DailyProfit).FirstOrDefault();
            var worst = fundRows.OrderBy(x => x.DailyProfit).FirstOrDefault();
            int winCount = fundRows.Count(x => x.DailyProfit > 0);
            int lossCount = fundRows.Count(x => x.DailyProfit < 0);

            return Ok(new
            {
                date = targetDate.ToString("yyyy-MM-dd"),
                hasRecord = total != null,
                total = total == null ? null : new
                {
                    assets = total.Assets,
                    dailyProfit = total.DailyProfit,
                    dailyRate = total.DailyRate,
                    totalProfit = total.TotalProfit,
                    totalRate = total.TotalRate
                },
                bestContributor = best == null ? null : new { best.FundCode, best.FundName, best.DailyProfit, best.DailyRate },
                worstContributor = worst == null ? null : new { worst.FundCode, worst.FundName, worst.DailyProfit, worst.DailyRate },
                winCount,
                lossCount,
                summary = total == null
                    ? "当天暂无收盘档案。"
                    : $"今日总收益 {total.DailyProfit:+0.00;-0.00;0.00} 元，收益率 {total.DailyRate:+0.00;-0.00;0.00}%。盈利 {winCount} 只，亏损 {lossCount} 只。"
            });
        }

        [HttpGet("news-impact-timeline")]
        public async Task<IActionResult> GetNewsImpactTimeline([FromQuery] string username, [FromQuery] int limit = 30)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("缺少账号");
            limit = Math.Clamp(limit, 10, 60);

            var funds = await _context.MyFunds.AsNoTracking().Where(f => f.Username == username).ToListAsync();
            var newsList = await FetchEastMoneyFastNewsAsync(Math.Max(limit * 3, 80));
            var result = new List<object>();

            foreach (var news in newsList)
            {
                var matches = funds
                    .Select(f => new { fund = f, score = ScoreNewsForFund(news, f) })
                    .Where(x => x.score.score >= 16)
                    .OrderByDescending(x => x.score.score)
                    .Take(3)
                    .ToList();

                if (matches.Count == 0) continue;

                result.Add(new
                {
                    news.Id,
                    news.Title,
                    news.Summary,
                    news.TimeText,
                    news.DateText,
                    news.Sentiment,
                    news.ImpactScore,
                    matchedFunds = matches.Select(x => new
                    {
                        code = x.fund.FundCode,
                        name = x.fund.FundName,
                        score = x.score.score,
                        tags = x.score.tags
                    }).ToList()
                });

                if (result.Count >= limit) break;
            }

            return Ok(result);
        }

        [HttpGet("fund-holdings")]
        public async Task<IActionResult> GetFundHoldings([FromQuery] string fundCode)
        {
            var client = _httpClientFactory.CreateClient("EastMoney");
            try
            {
                string positionUrl = $"https://fundmobapi.eastmoney.com/FundMNewApi/FundMNInverstPosition?FCODE={fundCode}&deviceid=Wap&plat=Wap&product=EFund&version=2.0.0";
                string posRes = await client.GetStringAsync(positionUrl);
                using var posDoc = System.Text.Json.JsonDocument.Parse(posRes);

                JsonElement dataElement;
                if (posDoc.RootElement.TryGetProperty("Datas", out var datasObj) && datasObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = datasObj;
                else if (posDoc.RootElement.TryGetProperty("Data", out var dataObj) && dataObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = dataObj;
                else
                    return Ok(new List<object>());
                JsonElement fundPosition;
                if (dataElement.TryGetProperty("fundStocks", out var stocksObj) && stocksObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = stocksObj;
                else if (dataElement.TryGetProperty("fundPosition", out var posObj) && posObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = posObj;
                else
                    return Ok(new List<object>());
                var stockList = new List<dynamic>();
                var secidList = new List<string>();
                foreach (var stock in fundPosition.EnumerateArray())
                {
                    string code = stock.TryGetProperty("GPDM", out var cProp) ? cProp.GetString() : "";
                    string name = stock.TryGetProperty("GPJC", out var nProp) ? nProp.GetString() : "";
                    string ratio = stock.TryGetProperty("JZBL", out var rProp) ? rProp.GetString() : "0";
                    if (!string.IsNullOrEmpty(code))
                    {
                        string prefix = "0.";
                        if (code.StartsWith("6")) prefix = "1.";
                        else if (code.Length == 5) prefix = "116.";

                        secidList.Add(prefix + code);
                        stockList.Add(new { code, name, ratio, rate = 0.0 });
                    }
                }

                if (secidList.Count > 0)
                {
                    string secids = string.Join(",", secidList);
                    string quoteUrl = $"http://push2.eastmoney.com/api/qt/ulist.np/get?secids={secids}&fields=f12,f14,f3";

                    var quoteClient = _httpClientFactory.CreateClient("EastMoneyQuote");
                    string quoteRes = await quoteClient.GetStringAsync(quoteUrl);
                    using var quoteDoc = System.Text.Json.JsonDocument.Parse(quoteRes);

                    if (quoteDoc.RootElement.TryGetProperty("data", out var qData) && qData.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (qData.TryGetProperty("diff", out var diffs) && diffs.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var rateDict = new Dictionary<string, double>();
                            foreach (var diff in diffs.EnumerateArray())
                            {
                                string c = diff.TryGetProperty("f12", out var f12) ? f12.GetString() : "";
                                double r = 0;
                                if (diff.TryGetProperty("f3", out var f3) && f3.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    r = Math.Round(f3.GetDouble() / 100.0, 2);
                                }
                                if (!string.IsNullOrEmpty(c)) rateDict[c] = r;
                            }

                            var finalResult = stockList.Select(s => new
                            {
                                code = s.code,
                                name = s.name,
                                ratio = s.ratio,
                                rate = rateDict.ContainsKey((string)s.code) ? rateDict[(string)s.code] : 0.0
                            }).ToList();

                            return Ok(finalResult);
                        }
                    }
                }
                return Ok(stockList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"获取持仓失败: {ex.Message}");
            }
        }
    }
}
