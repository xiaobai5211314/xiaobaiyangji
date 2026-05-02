namespace 估值助手.Services
{
    public sealed class BaiduOcrOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public int TimeoutMilliseconds { get; set; } = 10000;
    }
}
