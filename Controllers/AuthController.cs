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

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
            return Convert.ToBase64String(bytes);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] string username, [FromForm] string password)
        {
            username = (username ?? string.Empty).Trim();
            password ??= string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return BadRequest("账号和密码不能为空");

            if (await _context.Users.AnyAsync(u => u.Username == username))
                return BadRequest("该账号已被注册！");

            _context.Users.Add(new User
            {
                Username = username,
                PasswordHash = HashPassword(password),
                AvatarDataUrl = string.Empty
            });

            await _context.SaveChangesAsync();
            return Ok(new { success = true, username });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
        {
            try
            {
                username = (username ?? string.Empty).Trim();
                password ??= string.Empty;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return BadRequest("账号和密码不能为空");

                var passwordHash = HashPassword(password);

                var user = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.Username == username)
                    .Select(u => new { u.Username, u.PasswordHash })
                    .FirstOrDefaultAsync();

                if (user == null)
                    return BadRequest("账号不存在");

                if (!string.Equals(user.PasswordHash, passwordHash, StringComparison.Ordinal))
                    return BadRequest("密码错误");

                return Ok(new { success = true, username = user.Username });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "登录接口异常",
                    message = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("profile")]
        public async Task<IActionResult> Profile([FromQuery] string username)
        {
            username = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("缺少账号");

            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Username == username)
                .Select(u => new
                {
                    u.Username,
                    AvatarDataUrl = u.AvatarDataUrl ?? string.Empty
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return NotFound("账号不存在");

            return Ok(user);
        }

        [HttpGet("profile-v2")]
        public Task<IActionResult> ProfileV2([FromQuery] string username) => Profile(username);

        [HttpGet("profile-v3")]
        public Task<IActionResult> ProfileV3([FromQuery] string username) => Profile(username);

        [HttpPost("avatar-file")]
        public async Task<IActionResult> AvatarFile([FromForm] string username, [FromForm] IFormFile avatarFile)
        {
            return await SaveAvatarFile(username, avatarFile);
        }

        [HttpPost("avatar-file-v2")]
        public async Task<IActionResult> AvatarFileV2([FromForm] string username, [FromForm] IFormFile avatarFile)
        {
            return await SaveAvatarFile(username, avatarFile);
        }

        [HttpPost("avatar-file-v3")]
        public async Task<IActionResult> AvatarFileV3([FromForm] string username, [FromForm] IFormFile avatarFile)
        {
            return await SaveAvatarFile(username, avatarFile);
        }

        [HttpPost("avatar-json-v3")]
        public async Task<IActionResult> AvatarJsonV3([FromBody] AvatarJsonRequest request)
        {
            var username = (request.Username ?? string.Empty).Trim();
            var avatarDataUrl = request.AvatarDataUrl ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("缺少账号");

            if (string.IsNullOrWhiteSpace(avatarDataUrl))
                return BadRequest("头像不能为空");

            if (!avatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("头像格式不正确");

            if (avatarDataUrl.Length > 1_500_000)
                return BadRequest("头像保存后过大，请换一张更小的图片");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("账号不存在");

            user.AvatarDataUrl = avatarDataUrl;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl ?? string.Empty });
        }

        [HttpPost("avatar/clear")]
        public async Task<IActionResult> ClearAvatar([FromForm] string username)
        {
            return await ClearAvatarInternal(username);
        }

        [HttpPost("avatar/clear-v2")]
        public async Task<IActionResult> ClearAvatarV2([FromForm] string username)
        {
            return await ClearAvatarInternal(username);
        }

        [HttpPost("avatar/clear-v3")]
        public async Task<IActionResult> ClearAvatarV3([FromForm] string username)
        {
            return await ClearAvatarInternal(username);
        }

        private async Task<IActionResult> SaveAvatarFile(string username, IFormFile avatarFile)
        {
            username = (username ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("缺少账号");

            if (avatarFile == null || avatarFile.Length == 0)
                return BadRequest("头像不能为空");

            if (avatarFile.Length > 3_000_000)
                return BadRequest("头像文件过大，请换一张更小的图片");

            if (string.IsNullOrWhiteSpace(avatarFile.ContentType) ||
                !avatarFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("头像格式不正确");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("账号不存在");

            await using var ms = new MemoryStream();
            await avatarFile.CopyToAsync(ms);

            var mime = string.IsNullOrWhiteSpace(avatarFile.ContentType) ? "image/jpeg" : avatarFile.ContentType;
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(ms.ToArray())}";

            if (dataUrl.Length > 1_500_000)
                return BadRequest("头像保存后过大，请换一张更小的图片");

            user.AvatarDataUrl = dataUrl;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl ?? string.Empty });
        }

        private async Task<IActionResult> ClearAvatarInternal(string username)
        {
            username = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("缺少账号");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("账号不存在");

            user.AvatarDataUrl = string.Empty;
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }

    public sealed class AvatarJsonRequest
    {
        public string? Username { get; set; }
        public string? AvatarDataUrl { get; set; }
    }
}
