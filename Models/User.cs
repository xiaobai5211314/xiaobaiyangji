using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class User
    {
        [Key]
        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string? AvatarDataUrl { get; set; }
    }
}
