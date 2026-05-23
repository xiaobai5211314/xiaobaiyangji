namespace 小白养基.Services
{
    public sealed class BaiduOcrOptions
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public int TimeoutMilliseconds { get; set; } = 10000;
    }
}
