using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace 小白养基.Services
{
    public sealed class TokenService
    {
        private readonly byte[] _secret;
        private readonly int _expireHours;

        public TokenService(IConfiguration config)
        {
            var secret = config["Auth:TokenSecret"];
            if (string.IsNullOrWhiteSpace(secret))
                secret = "xiaobaiyangji-default-secret-change-me";
            _secret = Encoding.UTF8.GetBytes(secret);
            _expireHours = int.TryParse(config["Auth:TokenExpireHours"], out var h) && h > 0 ? h : 720;
        }

        public string GenerateToken(string username)
        {
            var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" }));
            var now = DateTimeOffset.UtcNow;
            var payload = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                sub = username,
                iat = now.ToUnixTimeSeconds(),
                exp = now.AddHours(_expireHours).ToUnixTimeSeconds()
            }));
            var signature = Base64UrlBytes(HmacSha256($"{header}.{payload}"));
            return $"{header}.{payload}.{signature}";
        }

        public string? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var expected = Base64UrlBytes(HmacSha256($"{parts[0]}.{parts[1]}"));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(parts[2])))
                return null;

            try
            {
                var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("exp", out var expEl))
                {
                    var exp = expEl.GetInt64();
                    if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
                        return null;
                }

                return root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private byte[] HmacSha256(string data)
        {
            using var hmac = new HMACSHA256(_secret);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        private static string Base64UrlEncode(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string Base64UrlBytes(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string input)
        {
            var padded = input.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
