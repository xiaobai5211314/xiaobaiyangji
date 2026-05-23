using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public sealed class OcrCorrection
    {
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(160)]
        public string OcrName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string FundCode { get; set; } = string.Empty;

        [MaxLength(160)]
        public string FundName { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
