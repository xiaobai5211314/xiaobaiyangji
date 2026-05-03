using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class StockKLineCache
    {
        public int Id { get; set; }

        [MaxLength(10)]
        public string StockCode { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Market { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Period { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = "[]";

        public DateTime RefreshedAt { get; set; } = DateTime.Now;
    }
}
