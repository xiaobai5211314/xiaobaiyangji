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

            _client ??= new Ocr(_options.ApiKey, _options.SecretKey)
            {
                Timeout = _options.TimeoutMilliseconds <= 0 ? 10000 : _options.TimeoutMilliseconds
            };

            return _client.AccurateBasic(imageBytes);
        }
    }
}
