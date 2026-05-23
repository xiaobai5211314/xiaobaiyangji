using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    public class StockOcrImportBatch
    {
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Source { get; set; } = "ocr";

        [MaxLength(30)]
        public string Status { get; set; } = "preview";

        public string RawText { get; set; } = string.Empty;

        public string DiagnosticsJson { get; set; } = "[]";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ConfirmedAt { get; set; }

        public List<StockOcrImportItem> Items { get; set; } = new();
    }
}
