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
            if (oldRow.IsFinal && !incoming.IsFinal) return false;
            if (oldRow.IsFinal && incoming.IsFinal && SourceRank(incoming.Source) < SourceRank(oldRow.Source)) return false;
            return true;
        }

        private static void CopyValues(DailyArchive target, DailyArchive source)
        {
            target.FundName = string.IsNullOrWhiteSpace(source.FundName) ? target.FundName : source.FundName;
            target.Assets = Math.Round(source.Assets, 2);
            target.DailyProfit = Math.Round(source.DailyProfit, 2);
            target.DailyRate = Math.Round(source.DailyRate, 2);
            target.TotalProfit = Math.Round(source.TotalProfit, 2);
            target.TotalRate = Math.Round(source.TotalRate, 2);
            target.Source = string.IsNullOrWhiteSpace(source.Source) ? "unknown" : source.Source;
            target.IsFinal = source.IsFinal;
            target.UpdatedAt = DateTime.UtcNow;
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
