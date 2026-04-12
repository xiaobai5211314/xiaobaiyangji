using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using 估值助手.Models;

namespace 估值助手.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        public AuthController(AppDbContext context) { _context = context; }

        // 核心机密：SHA256 密码加密算法
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] string username, [FromForm] string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return BadRequest("账号和密码不能为空");

            // 查重：防止别人注册同样的账号
            if (await _context.Users.AnyAsync(u => u.Username == username)) return BadRequest("该指挥官代号已被注册！");

            // 存入数据库（注意：这里存的是加密后的密码）
            _context.Users.Add(new User { Username = username, PasswordHash = HashPassword(password) });
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
        {
            var user = await _context.Users.FindAsync(username);
            // 校验账号是否存在，以及密码哈希值是否对得上
            if (user == null || user.PasswordHash != HashPassword(password)) return BadRequest("账号或密码错误！");

            return Ok(new { success = true });
        }
    }
}