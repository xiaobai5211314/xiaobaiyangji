using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class MarketDataCache
    {
        public int Id { get; set; }

        [MaxLength(200)]
        public string CacheKey { get; set; } = string.Empty;

        [MaxLength(50)]
        public string DataType { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = "{}";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string Source { get; set; } = string.Empty;

        public string? LastError { get; set; }

        public int HitCount { get; set; }

        public bool IsStale { get; set; }
    }
}
