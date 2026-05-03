using Baidu.Aip.Ocr;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace 估值助手.Services
{
    public interface IBaiduOcrService
    {
        JObject AccurateBasic(byte[] imageBytes);
    }

    public sealed class BaiduOcrService : IBaiduOcrService
    {
        private readonly BaiduOcrOptions _options;
        private Ocr? _client;

        public BaiduOcrService(IOptions<BaiduOcrOptions> options)
        {
            _options = options.Value;
        }

        public JObject AccurateBasic(byte[] imageBytes)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
            {
                throw new InvalidOperationException("请通过配置 BaiduOcr:ApiKey 与 BaiduOcr:SecretKey 提供 OCR 密钥，禁止硬编码到源码。");
            }

            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new InvalidOperationException("OCR 图片内容为空。");
            }

            _client ??= new Ocr(_options.ApiKey, _options.SecretKey)
            {
                Timeout = _options.TimeoutMilliseconds <= 0 ? 10000 : _options.TimeoutMilliseconds
            };

            var result = _client.AccurateBasic(imageBytes);

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
