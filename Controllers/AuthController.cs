using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
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

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromForm] string username, [FromForm] string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return BadRequest("账号和密码不能为空");
            if (await _context.Users.AnyAsync(u => u.Username == username)) return BadRequest("该账号已被注册");

            _context.Users.Add(new User { Username = username, PasswordHash = HashPassword(password), AvatarDataUrl = string.Empty });
            await _context.SaveChangesAsync();
            return Ok(new { success = true, username, avatarDataUrl = "" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromForm] string username, [FromForm] string password)
        {
            var user = await _context.Users.FindAsync(username);
            if (user == null || user.PasswordHash != HashPassword(password)) return BadRequest("账号或密码错误");

            return Ok(new { success = true, username = user.Username, avatarDataUrl = user.AvatarDataUrl ?? "" });
        }

        [HttpGet("profile")]
        public async Task<IActionResult> Profile([FromQuery] string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("缺少账号");
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("账号不存在");
            return Ok(new { username = user.Username, avatarDataUrl = user.AvatarDataUrl ?? "" });
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> SaveAvatar([FromForm] string username, [FromForm] string avatarDataUrl)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("缺少账号");
            if (string.IsNullOrWhiteSpace(avatarDataUrl)) return BadRequest("头像不能为空");
            if (!avatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return BadRequest("头像格式不正确");
            if (avatarDataUrl.Length > 900_000) return BadRequest("头像过大，请换一张更小的图片");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("账号不存在");
            user.AvatarDataUrl = avatarDataUrl;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl });
        }


        [HttpPost("avatar-file")]
        [RequestSizeLimit(3_000_000)]
        public async Task<IActionResult> SaveAvatarFile([FromForm] string username, [FromForm] IFormFile avatarFile)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("缺少账号");
            if (avatarFile == null || avatarFile.Length == 0) return BadRequest("头像不能为空");
            if (avatarFile.Length > 2_000_000) return BadRequest("头像文件过大，请换一张更小的图片");
            if (string.IsNullOrWhiteSpace(avatarFile.ContentType) || !avatarFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("头像格式不正确");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("账号不存在");

            await using var ms = new MemoryStream();
            await avatarFile.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var mime = string.IsNullOrWhiteSpace(avatarFile.ContentType) ? "image/jpeg" : avatarFile.ContentType;
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            if (dataUrl.Length > 1_200_000) return BadRequest("头像保存后过大，请换一张更小的图片");

            user.AvatarDataUrl = dataUrl;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl });
        }

        [HttpPost("avatar/clear")]
        public async Task<IActionResult> ClearAvatar([FromForm] string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("缺少账号");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("账号不存在");
            user.AvatarDataUrl = string.Empty;
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        public class AvatarJsonRequest
        {
            public string Username { get; set; } = string.Empty;
            public string AvatarDataUrl { get; set; } = string.Empty;
        }

        [HttpGet("profile-v3")]
        public async Task<IActionResult> ProfileV3([FromQuery] string username) => await Profile(username);

        [HttpPost("avatar-file-v3")]
        [RequestSizeLimit(3_000_000)]
        public async Task<IActionResult> SaveAvatarFileV3([FromForm] string username, [FromForm] IFormFile avatarFile)
            => await SaveAvatarFile(username, avatarFile);

        [HttpPost("avatar-json-v3")]
        [RequestSizeLimit(2_000_000)]
        public async Task<IActionResult> SaveAvatarJsonV3([FromBody] AvatarJsonRequest req)
        {
            var username = req?.Username ?? string.Empty;
            var avatarDataUrl = req?.AvatarDataUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username)) return BadRequest("缺少账号");
            if (string.IsNullOrWhiteSpace(avatarDataUrl)) return BadRequest("头像不能为空");
            if (!avatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return BadRequest("头像格式不正确");
            if (avatarDataUrl.Length > 1_200_000) return BadRequest("头像过大，请换一张更小的图片");
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return NotFound("账号不存在");
            user.AvatarDataUrl = avatarDataUrl;
            await _context.SaveChangesAsync();
            return Ok(new { success = true, avatarDataUrl = user.AvatarDataUrl });
        }

        [HttpPost("avatar/clear-v3")]
        public async Task<IActionResult> ClearAvatarV3([FromForm] string username) => await ClearAvatar(username);
    }
}
