using System.ComponentModel.DataAnnotations;

namespace 估值助手.Models
{
    public class User
    {

        [Key]
        public string Username { get; set; } = string.Empty; //  账号
        public string PasswordHash { get; set; } = string.Empty; // 加密后的密码
    }
}