using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class DailyArchiveService
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> UpsertLocks = new();
        private readonly AppDbContext _dbContext;

        public DailyArchiveService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public static bool HasFinancialData(DailyArchive row)
            => row.Assets > 0.01
               || Math.Abs(row.DailyProfit) > 0.001
               || Math.Abs(row.DailyRate) > 0.001
               || Math.Abs(row.TotalProfit) > 0.001
               || Math.Abs(row.TotalRate) > 0.001;

        public static bool IsAntConfirmedSource(string? source)
        {
            var value = (source ?? string.Empty).Trim().ToLowerInvariant();
            return value.Contains("alipay") || value.Contains("ocr-confirmed");
        }

        public static bool IsOfficialNavPendingSource(string? source)
        {
            var value = (source ?? string.Empty).Trim().ToLowerInvariant();
            return value.Contains("official-nav-pending")
                || value.Contains("mixed-confirmation-pending");
        }

        public static string GetSettlementStatus(DailyArchive row)
        {
            if (row.IsFinal && IsAntConfirmedSource(row.Source)) return "confirmed";
            if (IsOfficialNavPendingSource(row.Source)) return "pending_nav";
            return "legacy";
        }

        /// <summary>
        /// 不变式守卫：剔除"单基金行被错写成 TOTAL 汇总值"的损坏数据。
        /// 多基金组合下，若某基金行 Assets 约等于 TOTAL Assets（抄写指纹），判定为损坏并剔除；
        /// 剔除后若存在有效基金行，则用剩余基金重算 TOTAL，使其 = Σ基金Assets（保持内部一致，绝不落库伪造的 -4.32% 之类）。
        /// 单基金组合（只有 1 只基金）视为合法，不触发守卫。
        /// </summary>
        public static (List<DailyArchive> Rows, int DroppedCount, List<string> Warnings)
            SanitizeArchiveRows(List<DailyArchive> input)
        {
            var warnings = new List<string>();
            if (input == null) return (new List<DailyArchive>(), 0, warnings);

            var totalRow = input.FirstOrDefault(r => r.FundCode == "TOTAL");
            var fundRows = input.Where(r => r.FundCode != "TOTAL").ToList();

            // 单基金组合（0 或 1 只基金）视为合法，不触发守卫。
            if (totalRow != null && fundRows.Count > 1)
            {
                // 抄写指纹：某单基金行 Assets 约等于 TOTAL Assets，说明把汇总值错抄进了单基金行。
                var corrupt = fundRows
                    .Where(f => Math.Abs((decimal)f.Assets - (decimal)totalRow.Assets) < 0.01m)
                    .ToList();

                foreach (var f in corrupt)
                {
                    fundRows.Remove(f);
                    warnings.Add(
                        $"ARCHIVE_GUARD: fund {f.FundCode} row assets {f.Assets} equals TOTAL assets {totalRow.Assets} (corrupt copy of summary) — dropped");
                }

                if (corrupt.Count > 0)
                {
                    // 用剩余基金重算 TOTAL，使其 = Σ基金Assets，保持内部一致。
                    var sumAssets = fundRows.Sum(f => (decimal)f.Assets);
                    var sumProfit = fundRows.Sum(f => (decimal)f.DailyProfit);
                    var baseAmt = Math.Max(0m, sumAssets - sumProfit);
                    totalRow.Assets = (double)sumAssets;
                    totalRow.DailyProfit = (double)sumProfit;
                    totalRow.DailyRate = (double)PortfolioAccounting.Percent(sumProfit, baseAmt);
                    totalRow.Source = "guard-recomputed-total";
                }
            }

            var result = fundRows.ToList();
            if (totalRow != null) result.Add(totalRow);

            return (result, warnings.Count, warnings);
        }

        public static DailyArchive? PickLatestPortfolioSummaryTotal(IEnumerable<DailyArchive> rows)
        {
            return rows
                .Where(HasFinancialData)
                .Where(x => string.Equals(x.FundCode, "TOTAL", StringComparison.OrdinalIgnoreCase))
                .Where(x => GetSettlementStatus(x) != "legacy")
                .OrderByDescending(x => x.RecordDate.Date)
                .ThenByDescending(x => x.IsFinal && IsAntConfirmedSource(x.Source))
                .ThenByDescending(x => x.IsFinal)
                .ThenByDescending(x => HasFinancialData(x))
                .ThenByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
        }

        private static int SourceRank(string? source)
        {
            var value = (source ?? string.Empty).ToLowerInvariant();
            if (value.Contains("alipay") || value.Contains("ocr")) return 5;
            if (value.Contains("official") || value.Contains("nav")) return 4;
            if (value.Contains("mixed-final")) return 4;
            if (value.Contains("settlement")) return 3;
            if (value.Contains("estimate")) return 1;
            return 0;
        }

        private static bool ShouldReplace(DailyArchive oldRow, DailyArchive incoming)
        {
            var oldHasData = HasFinancialData(oldRow);
            var incomingHasData = HasFinancialData(incoming);

            // 空数据永远不能覆盖已存在的有效档案，TOTAL 同样受保护。
            if (!incomingHasData && oldHasData) return false;
            // 蚂蚁确认快照是正式金额唯一事实源，净值或估值归档不得覆盖。
            if (IsAntConfirmedSource(oldRow.Source) && !IsAntConfirmedSource(incoming.Source)) return false;
            if (oldRow.IsFinal && !incoming.IsFinal) return false;
            if (oldRow.IsFinal && incoming.IsFinal && SourceRank(incoming.Source) < SourceRank(oldRow.Source)) return false;
            return true;
        }

        private static void CopyValues(DailyArchive target, DailyArchive source)
        {
            target.FundName = string.IsNullOrWhiteSpace(source.FundName) ? target.FundName : source.FundName;
            target.Assets = RoundMoney(source.Assets);
            target.DailyProfit = RoundMoney(source.DailyProfit);
            target.DailyRate = RoundMoney(source.DailyRate);
            target.TotalProfit = RoundMoney(source.TotalProfit);
            target.TotalRate = RoundMoney(source.TotalRate);
            target.Source = string.IsNullOrWhiteSpace(source.Source) ? "unknown" : source.Source;
            target.IsFinal = source.IsFinal;
            target.UpdatedAt = DateTime.UtcNow;
        }

        private static double RoundMoney(double value)
            => Convert.ToDouble(decimal.Round(Convert.ToDecimal(value), 2, MidpointRounding.AwayFromZero));

        private static void NormalizeValues(DailyArchive row)
        {
            row.Assets = RoundMoney(row.Assets);
            row.DailyProfit = RoundMoney(row.DailyProfit);
            row.DailyRate = RoundMoney(row.DailyRate);
            row.TotalProfit = RoundMoney(row.TotalProfit);
            row.TotalRate = RoundMoney(row.TotalRate);
        }

        public async Task<int> UpsertAsync(
            string username,
            DateTime recordDate,
            IEnumerable<DailyArchive> incoming,
            CancellationToken cancellationToken = default)
        {
            var dayStart = recordDate.Date;
            var dayEnd = dayStart.AddDays(1);
            var lockKey = $"{username.Trim().ToLowerInvariant()}:{dayStart:yyyy-MM-dd}";
            var gate = UpsertLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var normalized = incoming
                    .Where(x => !string.IsNullOrWhiteSpace(x.FundCode))
                    .GroupBy(x => x.FundCode)
                    .Select(g => g.Last())
                    .ToList();
                if (normalized.Count == 0) return 0;

                var codes = normalized.Select(x => x.FundCode).ToList();
                var existing = await _dbContext.DailyArchives
                    .Where(a => a.Username == username
                                && a.RecordDate >= dayStart
                                && a.RecordDate < dayEnd
                                && codes.Contains(a.FundCode))
                    .ToListAsync(cancellationToken);

                foreach (var group in existing.GroupBy(x => x.FundCode).Where(g => g.Count() > 1))
                {
                    var keep = group
                        .OrderByDescending(x => x.IsFinal)
                        .ThenByDescending(x => HasFinancialData(x))
                        .ThenByDescending(x => x.UpdatedAt)
                        .ThenByDescending(x => x.Id)
                        .First();
                    _dbContext.DailyArchives.RemoveRange(group.Where(x => x.Id != keep.Id));
                }

                var existingByCode = existing
                    .GroupBy(x => x.FundCode)
                    .ToDictionary(g => g.Key, g => g
                        .OrderByDescending(x => x.IsFinal)
                        .ThenByDescending(x => HasFinancialData(x))
                        .ThenByDescending(x => x.UpdatedAt)
                        .ThenByDescending(x => x.Id)
                        .First());

                var changed = 0;
                foreach (var item in normalized)
                {
                    item.Username = username;
                    item.RecordDate = dayStart;
                    item.Source = string.IsNullOrWhiteSpace(item.Source) ? "unknown" : item.Source;
                    NormalizeValues(item);
                    item.UpdatedAt = DateTime.UtcNow;

                    if (existingByCode.TryGetValue(item.FundCode, out var oldRow))
                    {
                        if (!ShouldReplace(oldRow, item)) continue;
                        CopyValues(oldRow, item);
                        changed++;
                    }
                    else
                    {
                        // 没有任何有效数据时不创建假 0 档案。
                        if (!HasFinancialData(item)) continue;
                        _dbContext.DailyArchives.Add(item);
                        changed++;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return changed;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
