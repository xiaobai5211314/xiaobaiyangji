using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class User
    {
        [Key]
        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string? AvatarDataUrl { get; set; }

        [MaxLength(128)]
        public string? WechatOpenId { get; set; }

        [MaxLength(128)]
        public string? WechatUnionId { get; set; }

        [MaxLength(120)]
        public string? Nickname { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? LastLoginAt { get; set; }
    }
}
