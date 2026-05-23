using Baidu.Aip.Ocr;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace 小白养基.Services
{
    public interface IBaiduOcrService
    {
        JObject AccurateBasic(byte[] imageBytes);
        Task<JObject> AccurateWithLocationAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
    }

    public sealed class BaiduOcrService : IBaiduOcrService
    {
        private static readonly HttpClient SharedHttp = new();

        private readonly BaiduOcrOptions _options;
        private Ocr? _client;
        private string? _accessToken;
        private DateTime _accessTokenExpiresAtUtc;

        public BaiduOcrService(IOptions<BaiduOcrOptions> options)
        {
            _options = options.Value;
        }

        public JObject AccurateBasic(byte[] imageBytes)
        {
            ValidateOptionsAndImage(imageBytes);

            _client ??= new Ocr(_options.ApiKey, _options.SecretKey)
            {
                Timeout = _options.TimeoutMilliseconds <= 0 ? 10000 : _options.TimeoutMilliseconds
            };

            return ValidateOcrResult(_client.AccurateBasic(imageBytes));
        }

        public async Task<JObject> AccurateWithLocationAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
        {
            ValidateOptionsAndImage(imageBytes);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TimeoutMilliseconds <= 0 ? 15000 : _options.TimeoutMilliseconds);

            var token = await GetAccessTokenAsync(timeoutCts.Token);
            var url = $"https://aip.baidubce.com/rest/2.0/ocr/v1/accurate?access_token={Uri.EscapeDataString(token)}";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("image", Convert.ToBase64String(imageBytes))
            });

            using var response = await SharedHttp.PostAsync(url, content, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"百度 OCR 含位置接口 HTTP {(int)response.StatusCode}：{body}");
            }

            return ValidateOcrResult(JObject.Parse(body));
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return _accessToken;
            }

            var url = "https://aip.baidubce.com/oauth/2.0/token" +
                      $"?grant_type=client_credentials&client_id={Uri.EscapeDataString(_options.ApiKey)}&client_secret={Uri.EscapeDataString(_options.SecretKey)}";

            using var response = await SharedHttp.PostAsync(url, content: null, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"百度 OCR 获取 access_token 失败 HTTP {(int)response.StatusCode}：{body}");
            }

            var json = JObject.Parse(body);
            var token = json["access_token"]?.ToString();
            if (string.IsNullOrWhiteSpace(token))
            {
                var error = json["error"]?.ToString() ?? json["error_description"]?.ToString() ?? body;
                throw new InvalidOperationException($"百度 OCR 获取 access_token 失败：{error}");
            }

            var expiresIn = int.TryParse(json["expires_in"]?.ToString(), out var parsed) ? parsed : 86400;
            _accessToken = token;
            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expiresIn - 300));
            return token;
        }

        private void ValidateOptionsAndImage(byte[] imageBytes)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("请通过配置 BaiduOcr:ApiKey 与 BaiduOcr:SecretKey 提供 OCR 密钥，禁止硬编码到源码。");
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new InvalidOperationException("OCR 图片内容为空。");
            }
        }

        private static JObject ValidateOcrResult(JObject result)
        {
            if (result["error_code"] != null)
            {
                var code = result["error_code"]?.ToString() ?? "unknown";
                var message = result["error_msg"]?.ToString() ?? "百度 OCR 未返回错误说明";
                throw new InvalidOperationException($"百度 OCR 调用失败：{code} - {message}");
            }

            if (result["words_result"] == null)
            {
                throw new InvalidOperationException($"百度 OCR 未返回 words_result：{result.ToString(Newtonsoft.Json.Formatting.None)}");
            }

            return result;
        }
    }
}
