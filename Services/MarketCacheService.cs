using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class MarketCacheService
    {
        private readonly AppDbContext _db;
        private readonly IMemoryCache _memory;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public MarketCacheService(AppDbContext db, IMemoryCache memory)
        {
            _db = db;
            _memory = memory;
        }

        /// <summary>
        /// Try to get cached data. Returns (data, cacheSource) or (default, null) if nothing found.
        /// Priority: memory -> DB fresh.
        /// </summary>
        public async Task<(T? data, string? source)> TryGetAsync<T>(string key) where T : class
        {
            var memKey = $"mkt:{key}";
            if (_memory.TryGetValue<T>(memKey, out var memData) && memData != null)
                return (memData, "memory");

            try
            {
                var row = await _db.MarketDataCaches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CacheKey == key);

                if (row != null && row.ExpiresAt > DateTime.UtcNow)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _db.MarketDataCaches
                                .Where(x => x.CacheKey == key)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.HitCount, x => x.HitCount + 1));
                        }
                        catch { }
                    });

                    var data = JsonSerializer.Deserialize<T>(row.PayloadJson, JsonOptions);
                    if (data != null)
                    {
                        var freshTtl = row.ExpiresAt - DateTime.UtcNow;
                        if (freshTtl > TimeSpan.Zero)
                            _memory.Set(memKey, data, freshTtl);
                        return (data, "db");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketCache] DB read failed for {key}: {ex.Message}");
            }

            return (default, null);
        }

        /// <summary>
        /// Try to get stale (expired but still in DB) data for fallback.
        /// </summary>
        public async Task<(T? data, string? source)> TryGetStaleAsync<T>(string key, TimeSpan maxStaleAge) where T : class
        {
            try
            {
                var cutoff = DateTime.UtcNow - maxStaleAge;
                var row = await _db.MarketDataCaches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CacheKey == key && x.UpdatedAt > cutoff);

                if (row != null)
                {
                    var data = JsonSerializer.Deserialize<T>(row.PayloadJson, JsonOptions);
                    if (data != null)
                        return (data, "db-stale");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketCache] DB stale read failed for {key}: {ex.Message}");
            }

            return (default, null);
        }

        /// <summary>
        /// Write data to both memory and DB.
        /// </summary>
        public async Task SetAsync<T>(string key, T data, TimeSpan freshTtl, TimeSpan staleTtl, string source) where T : class
        {
            var memKey = $"mkt:{key}";
            _memory.Set(memKey, data, freshTtl);

            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                var now = DateTime.UtcNow;
                var existing = await _db.MarketDataCaches.FirstOrDefaultAsync(x => x.CacheKey == key);

                if (existing != null)
                {
                    existing.PayloadJson = json;
                    existing.UpdatedAt = now;
                    existing.ExpiresAt = now + freshTtl;
                    existing.Source = source;
                    existing.IsStale = false;
                    existing.LastError = null;
                }
                else
                {
                    _db.MarketDataCaches.Add(new MarketDataCache
                    {
                        CacheKey = key,
                        DataType = DeriveDataType(key),
                        PayloadJson = json,
                        UpdatedAt = now,
                        ExpiresAt = now + freshTtl,
                        Source = source,
                        HitCount = 0,
                        IsStale = false
                    });
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketCache] DB write failed for {key}: {ex.Message}");
            }
        }

        /// <summary>
        /// Record an external fetch failure on the cache entry (keeps stale data, marks error).
        /// </summary>
        public async Task RecordErrorAsync(string key, string error)
        {
            try
            {
                var existing = await _db.MarketDataCaches.FirstOrDefaultAsync(x => x.CacheKey == key);
                if (existing != null)
                {
                    existing.IsStale = true;
                    existing.LastError = error;
                    await _db.SaveChangesAsync();
                }
            }
            catch { }
        }

        /// <summary>
        /// Composite: try memory -> DB fresh -> fetch -> DB stale fallback.
        /// Returns (data, cacheSource, isFallback, updatedAt, lastError).
        /// </summary>
        public async Task<MarketCacheResult<T>> GetOrRefreshAsync<T>(
            string key,
            Func<Task<T?>> fetchFresh,
            TimeSpan freshTtl,
            TimeSpan staleTtl,
            string source = "external") where T : class
        {
            var (cached, cacheSource) = await TryGetAsync<T>(key);
            if (cached != null && cacheSource != null)
            {
                return new MarketCacheResult<T>
                {
                    Data = cached,
                    CacheSource = cacheSource,
                    IsFallback = false,
                    UpdatedAt = DateTime.UtcNow,
                    LastError = null
                };
            }

            try
            {
                var fresh = await fetchFresh();
                if (fresh != null)
                {
                    await SetAsync(key, fresh, freshTtl, staleTtl, source);
                    return new MarketCacheResult<T>
                    {
                        Data = fresh,
                        CacheSource = "build",
                        IsFallback = false,
                        UpdatedAt = DateTime.UtcNow,
                        LastError = null
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MarketCache] fetch failed for {key}: {ex.Message}");
                await RecordErrorAsync(key, ex.Message);
            }

            var (staleData, staleSource) = await TryGetStaleAsync<T>(key, staleTtl);
            if (staleData != null)
            {
                return new MarketCacheResult<T>
                {
                    Data = staleData,
                    CacheSource = staleSource,
                    IsFallback = true,
                    UpdatedAt = DateTime.UtcNow,
                    LastError = null
                };
            }

            return new MarketCacheResult<T>
            {
                Data = default,
                CacheSource = null,
                IsFallback = true,
                UpdatedAt = null,
                LastError = "no data available"
            };
        }

        private static string DeriveDataType(string key)
        {
            if (key.StartsWith("global_indices")) return "global_indices";
            if (key.StartsWith("capital_flow")) return "capital_flow";
            if (key.StartsWith("sector_radar")) return "sector_radar";
            if (key.StartsWith("sector_funds")) return "sector_funds";
            if (key.StartsWith("fund_nav")) return "fund_nav";
            return "other";
        }
    }

    public class MarketCacheResult<T>
    {
        public T? Data { get; set; }
        public string? CacheSource { get; set; }
        public bool IsFallback { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? LastError { get; set; }
    }
}
