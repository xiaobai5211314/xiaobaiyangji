using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    /// <summary>
    /// 用户 UI 偏好持久化：隐私模式、盈亏页视图、回本模拟默认金额等轻量前端状态。
    /// </summary>
    public class UserUiState
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(80)]
        public string StateKey { get; set; } = string.Empty;

        public string StateJson { get; set; } = "{}";

        public DateTime UpdatedAt { get; set; }
    }
}
