using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using 小白养基.Models;
using 小白养基.Services;

namespace 小白养基.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TokenService _tokenService;

        public AuthController(
            AppDbContext context,
            IPasswordHasher<User> passwordHasher,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            TokenService tokenService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _tokenService = tokenService;
        }

        private static string LegacySha256Hash(string password)
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

            var user = new User
            {
                Username = username,
                AvatarDataUrl = string.Empty,
                CreatedAt = DateTime.Now
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            var token = _tokenService.GenerateToken(username);
            return Ok(new { success = true, username, token });
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

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user == null)
                    return BadRequest("账号不存在");

                var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    var legacyHash = LegacySha256Hash(password);
                    if (!string.Equals(user.PasswordHash, legacyHash, StringComparison.Ordinal))
                    {
                        return BadRequest("密码错误");
                    }

                    user.PasswordHash = _passwordHasher.HashPassword(user, password);
                }
                else if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    user.PasswordHash = _passwordHasher.HashPassword(user, password);
                }

                user.LastLoginAt = DateTime.Now;
                await _context.SaveChangesAsync();

                var token = _tokenService.GenerateToken(user.Username);
                return Ok(new
                {
                    success = true,
                    username = user.Username,
                    displayName = user.Nickname ?? user.Username,
                    avatarDataUrl = user.AvatarDataUrl ?? string.Empty,
                    token
                });
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

        [HttpPost("wechat-login")]
        public async Task<IActionResult> WechatLogin([FromBody] WechatLoginRequest request)
        {
            var code = (request.Code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("微信登录凭证不能为空");

            var appId = GetWechatConfig("AppId");
            var appSecret = GetWechatConfig("AppSecret");
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            {
                return StatusCode(500, new
                {
                    error = "微信登录未配置",
                    message = "微信登录未配置",
                    detail = "请在配置中设置 WeChatMiniProgram:AppId 和 WeChatMiniProgram:AppSecret"
                });
            }

            WechatCodeSessionResponse session;
            try
            {
                session = await FetchWechatSessionAsync(appId, appSecret, code);
            }
            catch (OperationCanceledException)
            {
                return StatusCode(504, new { error = "微信登录超时", message = "微信登录服务请求超时" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[auth:wechat-login] code2Session failed: {ex.Message}");
                return StatusCode(502, new { error = "微信登录失败", message = "暂时无法连接微信登录服务" });
            }

            if (session.ErrCode.HasValue && session.ErrCode.Value != 0)
            {
                Console.WriteLine($"[auth:wechat-login] code2Session error: {session.ErrCode} {session.ErrMsg}");
                return BadRequest(new { error = "微信登录失败", message = session.ErrMsg ?? "微信登录凭证无效" });
            }

            var openId = (session.OpenId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(openId))
                return BadRequest(new { error = "微信登录失败", message = "未获取到微信用户标识" });

            var now = DateTime.Now;
            var nickname = NormalizeOptionalText(request.Nickname, 120);
            var avatarDataUrl = NormalizeAvatarDataUrl(request.AvatarDataUrl);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.WechatOpenId == openId);
            if (user == null)
            {
                user = new User
                {
                    Username = await GenerateWechatUsernameAsync(openId),
                    WechatOpenId = openId,
                    WechatUnionId = NormalizeOptionalText(session.UnionId, 128),
                    Nickname = nickname,
                    AvatarDataUrl = avatarDataUrl,
                    CreatedAt = now,
                    LastLoginAt = now
                };
                user.PasswordHash = CreateUnusablePasswordHash(user);
                _context.Users.Add(user);
            }
            else
            {
                user.LastLoginAt = now;

                if (string.IsNullOrWhiteSpace(user.WechatUnionId))
                    user.WechatUnionId = NormalizeOptionalText(session.UnionId, 128);

                if (!string.IsNullOrWhiteSpace(nickname))
                    user.Nickname = nickname;

                if (!string.IsNullOrWhiteSpace(avatarDataUrl))
                    user.AvatarDataUrl = avatarDataUrl;
            }

            await _context.SaveChangesAsync();

            var token = _tokenService.GenerateToken(user.Username);
            return Ok(new
            {
                success = true,
                username = user.Username,
                displayName = user.Nickname ?? user.Username,
                avatarDataUrl = user.AvatarDataUrl ?? string.Empty,
                token
            });
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
                    u.Nickname,
                    DisplayName = u.Nickname ?? u.Username,
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

        [HttpPost("profile/update")]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateRequest request)
        {
            var username = (request.Username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(username))
                return BadRequest("缺少账号");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return NotFound("账号不存在");

            if (request.DisplayName != null)
                user.Nickname = NormalizeOptionalText(request.DisplayName, 120);

            if (request.AvatarDataUrl != null)
            {
                var avatarDataUrl = request.AvatarDataUrl.Trim();
                if (!string.IsNullOrWhiteSpace(avatarDataUrl))
                {
                    if (!avatarDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                        return BadRequest("头像格式不正确");

                    if (avatarDataUrl.Length > 1_500_000)
                        return BadRequest("头像保存后过大，请换一张更小的图片");

                    user.AvatarDataUrl = avatarDataUrl;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                username = user.Username,
                displayName = user.Nickname ?? string.Empty,
                avatarDataUrl = user.AvatarDataUrl ?? string.Empty
            });
        }

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

        private string? GetWechatConfig(string key)
        {
            return _configuration[$"WeChatMiniProgram:{key}"]
                ?? _configuration[$"WechatMiniProgram:{key}"];
        }

        private async Task<WechatCodeSessionResponse> FetchWechatSessionAsync(string appId, string appSecret, string code)
        {
            var url =
                "https://api.weixin.qq.com/sns/jscode2session" +
                $"?appid={Uri.EscapeDataString(appId)}" +
                $"&secret={Uri.EscapeDataString(appSecret)}" +
                $"&js_code={Uri.EscapeDataString(code)}" +
                "&grant_type=authorization_code";

            var client = _httpClientFactory.CreateClient("WeChatMiniProgram");
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var session = await client.GetFromJsonAsync<WechatCodeSessionResponse>(url, timeoutCts.Token);
            return session ?? throw new InvalidOperationException("微信登录服务返回为空");
        }

        private async Task<string> GenerateWechatUsernameAsync(string openId)
        {
            var hash = Sha256Hex(openId);
            for (var length = 10; length <= Math.Min(32, hash.Length); length += 2)
            {
                var candidate = $"wx_{hash[..length]}";
                if (!await _context.Users.AnyAsync(u => u.Username == candidate))
                    return candidate;
            }

            while (true)
            {
                var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
                var candidate = $"wx_{suffix}";
                if (!await _context.Users.AnyAsync(u => u.Username == candidate))
                    return candidate;
            }
        }

        private string CreateUnusablePasswordHash(User user)
        {
            var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            return _passwordHasher.HashPassword(user, randomPassword);
        }

        private static string Sha256Hex(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Length <= maxLength ? text : text[..maxLength];
        }

        private static string NormalizeAvatarDataUrl(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (!text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return text.Length <= 1_500_000 ? text : string.Empty;
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

    public sealed class ProfileUpdateRequest
    {
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarDataUrl { get; set; }
    }

    public sealed class WechatLoginRequest
    {
        public string? Code { get; set; }
        public string? Nickname { get; set; }
        public string? AvatarDataUrl { get; set; }
    }

    public sealed class WechatCodeSessionResponse
    {
        [JsonPropertyName("openid")]
        public string? OpenId { get; set; }

        [JsonPropertyName("session_key")]
        public string? SessionKey { get; set; }

        [JsonPropertyName("unionid")]
        public string? UnionId { get; set; }

        [JsonPropertyName("errcode")]
        public int? ErrCode { get; set; }

        [JsonPropertyName("errmsg")]
        public string? ErrMsg { get; set; }
    }
}
