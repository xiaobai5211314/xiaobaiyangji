using Baidu.Aip.Ocr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using 估值助手.Models;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Microsoft.Extensions.Caching.Memory;

namespace 估值助手.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FundController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        static FundController()
        {
            System.Net.WebRequest.DefaultWebProxy = null;
        }

        private static readonly Baidu.Aip.Ocr.Ocr _baiduOcrClient = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c")
        {
            Timeout = 10000
        };

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; }
        }

        private static List<FundInfoCache> _globalFundCache = null;
        private static Dictionary<string, FundInfoCache> _exactMatchDict = null;

        public FundController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null && _exactMatchDict != null && _globalFundCache.Count > 0) return _globalFundCache;

            string cachedData = null;
            try
            {
                var options = ConfigurationOptions.Parse("localhost:6379");
                options.ConnectTimeout = 1500;
                options.SyncTimeout = 1500;
                using var redis = ConnectionMultiplexer.Connect(options);
                var db = redis.GetDatabase();
                var redisValue = await db.StringGetAsync("global_fund_db_cache_v3");
                if (redisValue.HasValue) cachedData = redisValue.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] Redis 异常: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(cachedData))
            {
                _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                BuildExactMatchDictionary(_globalFundCache);
                return _globalFundCache;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string jsData = await client.GetStringAsync("http://fund.eastmoney.com/js/fundcode_search.js");

                int startIndex = jsData.IndexOf('[');
                int endIndex = jsData.LastIndexOf(']');
                if (startIndex > 0 && endIndex > 0)
                {
                    string json = jsData.Substring(startIndex, endIndex - startIndex + 1);
                    var rawList = JsonSerializer.Deserialize<List<List<string>>>(json);

                    _globalFundCache = rawList.Select(x => new FundInfoCache
                    {
                        Code = x[0],
                        Name = x[2],
                        NormalizedName = NormalizeFundName(x[2])
                    }).ToList();

                    BuildExactMatchDictionary(_globalFundCache);

                    try
                    {
                        var options = ConfigurationOptions.Parse("localhost:6379");
                        options.ConnectTimeout = 1000;
                        using var redis = ConnectionMultiplexer.Connect(options);
                        await redis.GetDatabase().StringSetAsync("global_fund_db_cache_v3", JsonSerializer.Serialize(_globalFundCache), TimeSpan.FromHours(24));
                    }
                    catch { }

                    return _globalFundCache;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[致命] 东方财富接口异常: {ex.Message}");
            }

            return _globalFundCache ?? new List<FundInfoCache>();
        }

        private void BuildExactMatchDictionary(List<FundInfoCache> fundList)
        {
            _exactMatchDict = new Dictionary<string, FundInfoCache>(StringComparer.OrdinalIgnoreCase);
            foreach (var fund in fundList)
            {
                _exactMatchDict.TryAdd(fund.NormalizedName, fund);
                _exactMatchDict.TryAdd(fund.Name, fund);
            }
        }

        // 🚀 核心组件：基于历史净值与收益的「四维时空碰撞份额推演」
        private async Task<double> DeduceSharesFromHistoryAsync(string fundCode, double holdAmount, double yesterdayIncome)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=15";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                // 优先尝试使用昨日收益进行历史碰撞定位
                if (yesterdayIncome != 0)
                {
                    for (int i = 0; i < dataArray.GetArrayLength() - 1; i++)
                    {
                        if (double.TryParse(dataArray[i].GetProperty("DWJZ").GetString(), out double navToday) &&
                            double.TryParse(dataArray[i + 1].GetProperty("DWJZ").GetString(), out double navYest))
                        {
                            double diff = navToday - navYest;
                            if (diff == 0) continue;

                            double testShares = holdAmount / navToday;
                            double testYi = testShares * diff;

                            // 容错率设定为 1.5 元（化解四舍五入与尾差）
                            if (Math.Abs(testYi - yesterdayIncome) <= 1.5)
                            {
                                return Math.Round(testShares, 2);
                            }
                        }
                    }
                }

                // 如果没找到收益或碰撞失败，保底直接用最新净值折算
                if (double.TryParse(dataArray[0].GetProperty("DWJZ").GetString(), out double latestNav) && latestNav > 0)
                {
                    return Math.Round(holdAmount / latestNav, 2);
                }
            }
            catch { }
            return 0;
        }

        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            var allFunds = await GetAllFundsAsync();
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            // 🚀 终极防断网装甲：将用户已有的基金强行注入比对库！
            // 哪怕全网接口被封死，只要是买过的基金，相似度匹配绝对不会返回0！
            var robustFundPool = allFunds?.ToList() ?? new List<FundInfoCache>();
            foreach (var uf in userFundDict.Values)
            {
                if (!robustFundPool.Any(c => c.Code == uf.FundCode))
                {
                    robustFundPool.Add(new FundInfoCache { Code = uf.FundCode, Name = uf.FundName, NormalizedName = NormalizeFundName(uf.FundName) });
                }
            }

            byte[] finalProcessedBytes = null;
            List<string> debugLog = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                using (var inputStream = imageFile.OpenReadStream())
                {
                    using (Image image = Image.Load(inputStream))
                    {
                        int targetMaxWidth = 1080;
                        if (image.Width > targetMaxWidth)
                        {
                            int newHeight = (int)((double)image.Height / image.Width * targetMaxWidth);
                            image.Mutate(x => x.Resize(targetMaxWidth, newHeight));
                        }
                        image.Mutate(x => x.BackgroundColor(Color.White));
                        using (var outputStream = new MemoryStream())
                        {
                            image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = 60 });
                            finalProcessedBytes = outputStream.ToArray();
                        }
                    }
                }
                debugLog.Add($"⏱️ 图片压缩耗时: {watch.ElapsedMilliseconds} ms");
                watch.Restart();

                var ocrTask = Task.Run(() => _baiduOcrClient.AccurateBasic(finalProcessedBytes));
                if (await Task.WhenAny(ocrTask, Task.Delay(15000)) != ocrTask) return StatusCode(500, $"❌ 识别超时熔断！");

                var result = await ocrTask;
                debugLog.Add($"⏱️ 百度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

                var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
                if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字");

                int importedCount = 0;
                string amountPattern = @"^\d[\d,]*\.\d{2}$";
                debugLog.Add($"👁️ 视图检测: 智能混合视图 (搭载逆向推演与防污染系统)");

                for (int i = 1; i < texts.Count; i++)
                {
                    string currentLine = texts[i].Trim();
                    if (!Regex.IsMatch(currentLine, amountPattern)) continue;

                    string namePart1 = "";
                    for (int k = i - 1; k >= 0; k--)
                    {
                        string prev = texts[k].Trim();
                        if (string.IsNullOrWhiteSpace(prev) || prev.Contains("收益") || prev.Contains("金额") || prev.Contains("份额") || prev.Contains("金选") || prev.Contains("市场解读") || prev.Contains("基金经理") || prev.Contains("阶段") || prev.Contains("趋势") || prev.Contains("去看看") || Regex.IsMatch(prev, @"^[-\d\.,%+]+$"))
                            continue;
                        namePart1 = prev;
                        break;
                    }
                    if (string.IsNullOrEmpty(namePart1)) continue;

                    double holdAmount = double.Parse(currentLine.Replace(",", ""));
                    double holdingIncome = 0;
                    double yesterdayIncome = 0;
                    double holdingRate = 0;
                    double holdShares = 0;
                    string potentialFragment = "";
                    List<double> signedNumbers = new List<double>();

                    for (int j = 1; j <= 6 && (i + j) < texts.Count; j++)
                    {
                        string nextLine = texts[i + j].Trim();
                        if (Regex.IsMatch(nextLine, @"[\u4e00-\u9fa5]{4,}") && !nextLine.Contains("金选") && !nextLine.Contains("市场解读") && !nextLine.Contains("基金经理") && !nextLine.Contains("阶段") && !nextLine.Contains("趋势") && !nextLine.Contains("去看看") && !nextLine.Contains("更新")) break;

                        var pctMatch = Regex.Match(nextLine, @"^([-+]?\d+[\.,]\d{2})\s*%$");
                        if (pctMatch.Success) { holdingRate = double.Parse(pctMatch.Groups[1].Value.Replace(",", ".")); continue; }

                        if (Regex.IsMatch(nextLine, @"^[-+]\d[\d,]*\.\d{2}$")) { signedNumbers.Add(double.Parse(nextLine.Replace(",", ""))); }
                        else if (holdShares == 0 && Regex.IsMatch(nextLine, amountPattern))
                        {
                            string contextText = string.Join("", texts.Skip(Math.Max(0, i - 2)).Take(j + 3));
                            if (contextText.Contains("份额"))
                            {
                                double candidate = double.Parse(nextLine.Replace(",", ""));
                                if (Math.Abs(candidate - holdAmount) > 10) holdShares = candidate;
                            }
                        }
                        else if (string.IsNullOrEmpty(potentialFragment) && !Regex.IsMatch(nextLine, @"^[-\d\.,%+]+$") && !nextLine.Contains("金选") && !nextLine.Contains("市场解读") && !nextLine.Contains("更新")) { potentialFragment = nextLine; }
                    }

                    // 🚀 会计数学校验：绝对锚定收益项
                    if (holdingRate != 0 && signedNumbers.Count >= 1)
                    {
                        foreach (var num in signedNumbers)
                        {
                            double testCost = holdAmount - num;
                            if (testCost <= 0) continue;
                            if (Math.Abs(((num / testCost) * 100) - holdingRate) < 0.05)
                            {
                                holdingIncome = num;
                                yesterdayIncome = signedNumbers.FirstOrDefault(n => n != num);
                                break;
                            }
                        }
                    }
                    if (holdingIncome == 0 && signedNumbers.Count > 0)
                    {
                        holdingIncome = signedNumbers.Last();
                        if (signedNumbers.Count > 1) yesterdayIncome = signedNumbers.First();
                    }

                    string[] testNames = string.IsNullOrEmpty(potentialFragment) ? new[] { namePart1 } : new[] { namePart1, namePart1 + potentialFragment };
                    FundInfoCache finalBestMatch = null;
                    double finalBestScore = 0;

                    foreach (var testName in testNames)
                    {
                        string pureChinese = Regex.Replace(testName, @"[^\u4e00-\u9fa5]", "");
                        if (pureChinese.Length < 2) continue;

                        string normalizedOcr = NormalizeFundName(testName);
                        FundInfoCache bestMatch = null;
                        double bestScore = 0;

                        if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(testName, out exactFund)))
                        {
                            bestMatch = exactFund; bestScore = 100.0;
                        }
                        else
                        {
                            // 使用带有本地防断网基因的 robustFundPool
                            var candidates = robustFundPool.Where(f => f.NormalizedName.Contains(pureChinese) || pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));
                            foreach (var f in candidates)
                            {
                                double currentScore = CalculateSimilarity(normalizedOcr, f.NormalizedName) * 100;
                                if (currentScore > bestScore) { bestScore = currentScore; bestMatch = f; }
                            }
                        }

                        if (bestScore > finalBestScore) { finalBestScore = bestScore; finalBestMatch = bestMatch; }
                    }

                    string calcMethod = "识别提取";
                    if (finalBestMatch != null && finalBestScore > 65 && holdAmount > 0)
                    {
                        // 🚀 核心：若未扫出份额，全面启用时空碰撞推演！
                        if (holdShares == 0)
                        {
                            double calcShares = await DeduceSharesFromHistoryAsync(finalBestMatch.Code, holdAmount, yesterdayIncome);
                            if (calcShares > 0) { holdShares = calcShares; calcMethod = "AI物理推演"; }
                        }

                        if (userFundDict.TryGetValue(finalBestMatch.Code, out var exist))
                        {
                            // 🛡️ 强制指令保护：绝对不覆盖已更新的更高市值！
                            if (holdAmount >= exist.HoldAmount || exist.HoldAmount == 0)
                            {
                                exist.HoldAmount = holdAmount;
                            }
                            else
                            {
                                calcMethod += " (保护已有市值)";
                            }

                            if (holdingIncome != 0) exist.CostAmount = Math.Round(holdAmount - holdingIncome, 2);

                            // 更新推演份额（允许微调，防止误差积累）
                            if (holdShares > 0)
                            {
                                if (exist.HoldShares == 0 || Math.Abs(exist.HoldShares - holdShares) > 1) exist.HoldShares = holdShares;
                            }
                            _context.MyFunds.Update(exist);
                        }
                        else
                        {
                            var newFund = new MyFundConfig { Username = username, FundCode = finalBestMatch.Code, FundName = finalBestMatch.Name, HoldAmount = holdAmount, CostAmount = holdingIncome != 0 ? Math.Round(holdAmount - holdingIncome, 2) : holdAmount, HoldShares = holdShares };
                            _context.MyFunds.Add(newFund);
                            userFundDict[newFund.FundCode] = newFund;
                        }
                        importedCount++;

                        string sharesInfo = holdShares > 0 ? $"{holdShares:F2} ({calcMethod})" : "推演失败";
                        debugLog.Add($"{(finalBestScore >= 99.0 ? "⚡" : "✅")} 命中: {finalBestMatch.Name} [份额: {sharesInfo}]");
                    }
                }

                await _context.SaveChangesAsync();
                _cache.Remove($"Tactical_TodayData_{username}");
                return Ok($"识别完成！成功同步 {importedCount} 只。\n\n[诊断日志]\n{string.Join("\n", debugLog)}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ 代码执行出现异常: {ex.Message}");
            }
        }
        private string ExtractFundClass(string name)
        {
            if (Regex.IsMatch(name, @"C类?$|\(C\)$|（C）$", RegexOptions.IgnoreCase)) return "C";
            if (Regex.IsMatch(name, @"A类?$|\(A\)$|（A）$", RegexOptions.IgnoreCase)) return "A";
            if (name.Contains("QDII", StringComparison.OrdinalIgnoreCase)) return "QDII";
            return "";
        }

        private static string NormalizeFundName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name.ToUpper()
                       .Replace(" ", "").Replace("（", "(").Replace("）", ")")
                       .Replace("(QDII)", "").Replace("QDII", "")
                       .Replace("ETF联接", "ETF").Replace("证券投资基金", "")
                       .Replace("发起式", "").Replace("主题", "").Replace("指数型", "")
                       .Replace("混合", "").Replace("指数", "").Replace("C类", "C").Replace("A类", "A").Trim();
        }

        private double CalculateSimilarity(string s, string t)
        {
            if (s == t) return 1.0;
            int n = s.Length, m = t.Length;
            if (n == 0 || m == 0) return 0.0;

            int[] v0 = new int[m + 1];
            int[] v1 = new int[m + 1];
            for (int i = 0; i <= m; i++) v0[i] = i;

            for (int i = 0; i < n; i++)
            {
                v1[0] = i + 1;
                for (int j = 0; j < m; j++)
                {
                    int cost = (s[i] == t[j]) ? 0 : 1;
                    v1[j + 1] = Math.Min(v1[j] + 1, Math.Min(v0[j + 1] + 1, v0[j] + cost));
                }
                Array.Copy(v1, v0, m + 1);
            }
            return 1.0 - (double)v0[m] / Math.Max(n, m);
        }

        [HttpPost("add")]
        public async Task<IActionResult> AddFund([FromForm] string username, [FromForm] string code, [FromForm] double amount)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("未登录");
            using var client = new HttpClient();
            try
            {
                string url = $"http://fundgz.1234567.com.cn/js/{code}.js";
                string response = await client.GetStringAsync(url);
                var match = Regex.Match(response, @"jsonpgz\((.*?)\);");
                if (match.Success)
                {
                    var root = JsonDocument.Parse(match.Groups[1].Value).RootElement;
                    string name = root.GetProperty("name").GetString() ?? "未知";

                    var exist = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                    if (exist != null) exist.HoldAmount = amount;
                    else _context.MyFunds.Add(new MyFundConfig { Username = username, FundCode = code, FundName = name, HoldAmount = amount });

                    await _context.SaveChangesAsync();
                    return Ok(new { success = true, name = name });
                }
                return BadRequest("找不到该基金");
            }
            catch { return BadRequest("网络请求失败"); }
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteFund([FromForm] string username, [FromForm] string code)
        {
            var target = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
            if (target != null)
            {
                _context.MyFunds.Remove(target);
                await _context.SaveChangesAsync();
                return Ok(new { success = true });
            }
            return BadRequest("未找到该基金配置");
        }

        [HttpPost("settle-nightly")]
        public async Task<IActionResult> SettleNightly([FromForm] string username, [FromForm] string codes, [FromForm] string rates)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(codes)) return BadRequest("缺少清算参数！");

            try
            {
                var codeList = codes.Split(',');
                var rateList = rates.Split(',');

                var funds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                int count = 0;

                for (int i = 0; i < codeList.Length; i++)
                {
                    var fund = funds.FirstOrDefault(f => f.FundCode == codeList[i]);
                    if (fund != null && i < rateList.Length)
                    {
                        if (double.TryParse(rateList[i], out double rate))
                        {
                            double todayProfit = fund.HoldAmount * (rate / 100.0);
                            fund.HoldAmount = Math.Round(fund.HoldAmount + todayProfit, 2);
                            count++;
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Ok($"已清算 {count} 只基金！");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"清算异常: {ex.Message}");
            }
        }

        [HttpGet("auto-settle")]
        public async Task<IActionResult> AutoSettle()
        {
            try
            {
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("当前暂无基金需要清算。");

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                int successCount = 0;

                foreach (var fund in allFunds)
                {
                    try
                    {
                        string url = $"http://fundgz.1234567.com.cn/js/{fund.FundCode}.js?rt={DateTime.Now.Ticks}";
                        string jsData = await client.GetStringAsync(url);
                        var match = Regex.Match(jsData, @"\""gszzl\"":\""([^\""]+)\""");
                        if (match.Success && double.TryParse(match.Groups[1].Value, out double rate))
                        {
                            double todayProfit = fund.HoldAmount * (rate / 100.0);
                            fund.HoldAmount = Math.Round(fund.HoldAmount + todayProfit, 2);
                            successCount++;
                        }
                    }
                    catch { continue; }
                }

                await _context.SaveChangesAsync();
                return Ok($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动清算执行完毕！成功更新 {successCount} 只。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动清算引擎异常: {ex.Message}");
            }
        }
        [HttpGet("global-indices")]
        public async Task<IActionResult> GetGlobalIndices()
        {
            var indices = new[]
            {
        new { name = "上证指数", secid = "1.000001" },
        new { name = "科创50",   secid = "1.000688" },
        new { name = "创业板指", secid = "0.399006" },
        new { name = "恒生指数", secid = "124.HSI" },
        new { name = "纳斯达克", secid = "105.IXIC" },
        new { name = "标普500",  secid = "109.SPX" },
        new { name = "道琼斯",   secid = "100.DJIA" }
    };

            // 🛡️ 终极降维打击：无视证书 + 强制降级 HTTP/1.1
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            // 🚀 绝对核心：强制使用 HTTP/1.1！专治东方财富强行挂断电话！
            http.DefaultRequestVersion = new Version(1, 1);

            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.Add("Accept", "*/*");
            http.DefaultRequestHeaders.Add("Connection", "keep-alive");
            http.DefaultRequestHeaders.Add("Referer", "https://quote.eastmoney.com/");

            var tasks = indices.Select(async idx =>
            {
                var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={idx.secid}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=250";
                try
                {
                    var response = await http.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(response);

                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
                    {
                        if (data.TryGetProperty("klines", out var klines) && klines.GetArrayLength() > 0)
                        {
                            var klineArray = klines.EnumerateArray().Select(k => k.GetString()).ToArray();
                            var latestItem = klineArray[^1].Split(',');
                            var oldestItem = klineArray[0].Split(',');

                            double latestClose = 0, todayRate = 0, oldestClose = 0;
                            if (latestItem.Length > 2) double.TryParse(latestItem[2], out latestClose);
                            if (latestItem.Length > 8) double.TryParse(latestItem[8], out todayRate);
                            if (oldestItem.Length > 2) double.TryParse(oldestItem[2], out oldestClose);

                            double yearRate = oldestClose > 0 ? Math.Round((latestClose - oldestClose) / oldestClose * 100, 2) : 0;

                            var cleanKlines = klineArray.Reverse().Select(k =>
                            {
                                var p = k.Split(',');
                                double rate = 0;
                                if (p.Length > 8) double.TryParse(p[8], out rate);
                                return (object)new { date = p[0], rate = rate };
                            }).ToList();

                            return new
                            {
                                name = idx.name,
                                latest = latestClose,
                                todayRate = todayRate,
                                yearRate = yearRate,
                                klines = cleanKlines
                            };
                        }
                    }

                    return new { name = $"{idx.name} (无数据)", latest = 0.0, todayRate = 0.0, yearRate = 0.0, klines = new List<object>() };
                }
                catch (Exception ex)
                {
                    string innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    return new { name = $"{idx.name} (异常: {innerMsg})", latest = 0.0, todayRate = 0.0, yearRate = 0.0, klines = new List<object>() };
                }
            });

            var results = await Task.WhenAll(tasks);
            return Ok(results);
        }


        [HttpGet("test-load")]
        public async Task<IActionResult> TestLoad()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var funds = await GetAllFundsAsync();
                watch.Stop();
                return Ok($"✅ 基金库装载成功！共 {funds.Count} 只。总耗时: {watch.ElapsedMilliseconds} 毫秒。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ 装载失败: {ex.Message}");
            }
        }

        // 🚀 猎隼侦察兵升级版：双重刺探，获取单位净值计算绝对物理收益
        private async Task<(double? rate, double? exactProfit)> GetTodayRealRateAsync(string fundCode, string todayStr, double shares)
        {
            string cacheKey = $"RealRateV2_{fundCode}_{todayStr}_{shares}";
            if (_cache.TryGetValue(cacheKey, out (double?, double?) cached)) return cached;

            string missKey = $"NoRealRate_{fundCode}_{todayStr}";
            if (_cache.TryGetValue(missKey, out _)) return (null, null);

            try
            {
                // 🚀 修复 1：5秒超时，从容应对高并发
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=2";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                if (dataArray.GetArrayLength() > 0)
                {
                    var latest = dataArray[0];
                    // 🚀 修复 2：必须是网络通畅且返回了数据，才判断日期
                    if (latest.GetProperty("FSRQ").GetString() == todayStr)
                    {
                        if (double.TryParse(latest.GetProperty("JZZZL").GetString(), out double realRate))
                        {
                            double? exactProfit = null;
                            if (shares > 0 && dataArray.GetArrayLength() > 1)
                            {
                                if (double.TryParse(latest.GetProperty("DWJZ").GetString(), out double todayNav) &&
                                    double.TryParse(dataArray[1].GetProperty("DWJZ").GetString(), out double yesterdayNav))
                                {
                                    exactProfit = Math.Round(shares * (todayNav - yesterdayNav), 2);
                                }
                            }
                            var result = (realRate, exactProfit);
                            _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
                            return result;
                        }
                    }
                    else
                    {
                        // 🚀 修复 3：确凿证据表明官方日期还没更新，才关入小黑屋
                        _cache.Set(missKey, true, TimeSpan.FromMinutes(2));
                        return (null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                // 🚀 修复 4：网络超时报错绝不连坐！直接放行，等前端 15 秒后重试！
                Console.WriteLine($"[猎隼刺探异常] {fundCode}: {ex.Message}");
            }

            return (null, null);
        }

        // 🚀 升级版接口：带时间感知的战术减仓与利润结转
        [HttpPost("reduce-position")]
        public async Task<IActionResult> ReducePosition([FromForm] string username, [FromForm] string code, [FromForm] double reduceShares, [FromForm] double reduceAmount, [FromForm] string tradeDate)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未授权");
            try
            {
                var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                if (fund == null) return BadRequest("未找到该阵地");

                if (reduceShares <= 0 || reduceAmount < 0) return BadRequest("参数不合法");
                if (fund.HoldShares <= 0) return BadRequest("没有份额数据，请先用🔧补全份额");
                if (reduceShares > fund.HoldShares) return BadRequest("卖出份额超过了您当前的持有总量！");

                // 🚀 记录跨日/实时交易日志
                Console.WriteLine($"[战术减仓] 指挥官 {username} 减仓 {fund.FundName}, 交易归属日: {tradeDate}");

                // 1. 算出当前每份的平均成本
                double unitCost = fund.CostAmount / fund.HoldShares;
                // 2. 剥离本次卖出的成本
                double soldCost = unitCost * reduceShares;
                // 3. 算出本次落袋的真实利润
                double profit = reduceAmount - soldCost;

                // 4. 安全更新底仓，扣除弹药
                fund.HoldShares -= reduceShares;
                fund.CostAmount -= soldCost;

                // 5. 等比例扣减当前市值，防止大屏曲线突然暴跌断层
                double unitAmount = fund.HoldAmount / (fund.HoldShares + reduceShares);
                fund.HoldAmount -= (unitAmount * reduceShares);

                // 6. 利润装入小金库！永久保存！
                fund.RealizedProfit += profit;

                _context.MyFunds.Update(fund);
                await _context.SaveChangesAsync();
                _cache.Remove($"Tactical_TodayData_{username}");

                // 弹窗提示加上日期反馈
                return Ok(new { success = true, msg = $"核算完毕！按 [{tradeDate}] 结算，成功落袋: {profit:F2} 元" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"减仓核算异常: {ex.Message}");
            }
        }
    }
}