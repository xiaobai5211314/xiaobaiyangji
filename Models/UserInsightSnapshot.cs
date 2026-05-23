using System.ComponentModel.DataAnnotations;

namespace 小白养基.Models
{
    /// <summary>
    /// 前端产品化面板快照：战报、回本榜、赛道暴露、可信度雷达等可保存为跨设备可复用快照。
    /// </summary>
    public class UserInsightSnapshot
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(40)]
        public string SnapshotType { get; set; } = string.Empty;

        public DateTime SnapshotDate { get; set; }

        public string PayloadJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
