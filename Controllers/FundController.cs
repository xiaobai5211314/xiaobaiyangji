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

        // 🚀 新增核心：单位净值刺探器 (逆向演算引擎组件)
        private async Task<double> GetLatestNavAsync(string fundCode)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                // 优先刺探高速缓存接口获取单位净值 (DWJZ)
                string gzUrl = $"http://fundgz.1234567.com.cn/js/{fundCode}.js?rt={DateTime.Now.Ticks}";
                string gzRes = await client.GetStringAsync(gzUrl);
                var match = Regex.Match(gzRes, @"\""dwjz\"":\""([^\""]+)\""");
                if (match.Success && double.TryParse(match.Groups[1].Value, out double dwjz) && dwjz > 0)
                {
                    return dwjz;
                }
            }
            catch { }

            try
            {
                // 备用方案：直捣东方财富 F10 底层接口
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=1";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");
                if (dataArray.GetArrayLength() > 0)
                {
                    var latest = dataArray[0];
                    if (double.TryParse(latest.GetProperty("DWJZ").GetString(), out double realNav) && realNav > 0)
                    {
                        return realNav;
                    }
                }
            }
            catch { }

            return 0; // 无法获取净值时返回 0
        }

        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            var allFunds = await GetAllFundsAsync();
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

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

                if (await Task.WhenAny(ocrTask, Task.Delay(15000)) != ocrTask)
                {
                    return StatusCode(500, $"❌ 识别超时熔断！");
                }

                var result = await ocrTask;
                debugLog.Add($"⏱️ 百度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

                var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
                if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字");

                int importedCount = 0;
                string amountPattern = @"^\d[\d,]*\.\d{2}$";

                for (int i = 1; i < texts.Count; i++)
                {
                    string currentLine = texts[i];

                    if (Regex.IsMatch(currentLine, amountPattern))
                    {
                        string namePart1 = texts[i - 1];

                        if (namePart1.Contains("收益") || namePart1.Contains("金额") || namePart1.Contains("包含") || Regex.IsMatch(namePart1, @"^[-\d\.,%]+$"))
                            continue;

                        double holdAmount = double.Parse(currentLine.Replace(",", ""));
                        double holdingIncome = 0;
                        string potentialFragment = "";

                        for (int j = 1; j <= 5 && (i + j) < texts.Count; j++)
                        {
                            string nextLine = texts[i + j];
                            if (holdingIncome == 0 && Regex.IsMatch(nextLine, @"^[-+]\d[\d,]*\.\d{2}$"))
                            {
                                holdingIncome = double.Parse(nextLine.Replace(",", ""));
                            }
                            else if (string.IsNullOrEmpty(potentialFragment) && !Regex.IsMatch(nextLine, @"^[-\d\.,%+]+$") && !nextLine.Contains("金选") && !nextLine.Contains("收益") && !nextLine.Contains("更新"))
                            {
                                potentialFragment = nextLine;
                            }
                        }

                        string[] testNames = string.IsNullOrEmpty(potentialFragment)
                            ? new[] { namePart1 }
                            : new[] { namePart1, namePart1 + potentialFragment };

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
                                bestMatch = exactFund;
                                bestScore = 100.0;
                            }
                            else
                            {
                                var candidates = allFunds.Where(f => f.NormalizedName.Contains(pureChinese) ||
                                                                    pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));

                                foreach (var f in candidates)
                                {
                                    double similarity = CalculateSimilarity(normalizedOcr, f.NormalizedName);
                                    double currentScore = similarity * 100;

                                    if (currentScore > bestScore)
                                    {
                                        bestScore = currentScore;
                                        bestMatch = f;
                                    }
                                }
                            }

                            if (bestScore > finalBestScore)
                            {
                                finalBestScore = bestScore;
                                finalBestMatch = bestMatch;
                            }
                        }

                        if (finalBestMatch != null && finalBestScore > 65 && holdAmount > 0)
                        {
                            double costAmount = Math.Round(holdAmount - holdingIncome, 2);

                            // 🧠 逆向演算引擎启动：通过总金额反推物理份额
                            double dwjz = await GetLatestNavAsync(finalBestMatch.Code);
                            double calculatedShares = 0;
                            if (dwjz > 0)
                            {
                                calculatedShares = Math.Round(holdAmount / dwjz, 2);
                            }

                            if (userFundDict.TryGetValue(finalBestMatch.Code, out var exist))
                            {
                                exist.HoldAmount = holdAmount;
                                exist.CostAmount = costAmount;
                                if (calculatedShares > 0) exist.HoldShares = calculatedShares; // 自动注入精确份额
                                _context.MyFunds.Update(exist);
                            }
                            else
                            {
                                var newFund = new MyFundConfig
                                {
                                    Username = username,
                                    FundCode = finalBestMatch.Code,
                                    FundName = finalBestMatch.Name,
                                    HoldAmount = holdAmount,
                                    CostAmount = costAmount,
                                    HoldShares = calculatedShares // 自动注入精确份额
                                };
                                _context.MyFunds.Add(newFund);
                                userFundDict[newFund.FundCode] = newFund;
                            }
                            importedCount++;

                            string shareLog = calculatedShares > 0 ? $" [反推份额: {calculatedShares}]" : "";

                            if (finalBestScore >= 99.0)
                                debugLog.Add($"⚡ 精准命中: {finalBestMatch.Name}{shareLog}");
                            else
                                debugLog.Add($"✅ 模糊修复: {finalBestMatch.Name} ({finalBestScore:F1}%){shareLog}");
                        }
                    }
                }

                await _context.SaveChangesAsync();
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

        // 🚀 新增：猎隼侦察兵，去东方财富底层接口刺探真实净值
        private async Task<double?> GetTodayRealRateAsync(string fundCode, string todayStr)
        {
            string cacheKey = $"RealRate_{fundCode}_{todayStr}";
            if (_cache.TryGetValue(cacheKey, out double cachedRate)) return cachedRate;

            string missKey = $"NoRealRate_{fundCode}_{todayStr}";
            if (_cache.TryGetValue(missKey, out _)) return null;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=1";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");
                if (dataArray.GetArrayLength() > 0)
                {
                    var latest = dataArray[0];
                    if (latest.GetProperty("FSRQ").GetString() == todayStr)
                    {
                        if (double.TryParse(latest.GetProperty("JZZZL").GetString(), out double realRate))
                        {
                            _cache.Set(cacheKey, realRate, TimeSpan.FromHours(12));
                            return realRate;
                        }
                    }
                }
            }
            catch { }

            _cache.Set(missKey, true, TimeSpan.FromMinutes(1));
            return null;
        }


        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            try
            {
                string cacheKey = $"Tactical_TodayData_{username}";
                if (_cache.TryGetValue(cacheKey, out object cachedResult))
                {
                    return Ok(cachedResult);
                }

                var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                if (!myFundCodes.Any()) return Ok(new List<object>());

                var localTime = DateTime.UtcNow.AddHours(8);
                var today = localTime.Date;
                string todayStr = localTime.ToString("yyyy'/'MM'/'dd");
                string todayDash = localTime.ToString("yyyy-MM-dd");

                var todayRecords = await _context.FundRecords
                    .Where(r => r.FetchTime >= today && myFundCodes.Contains(r.FundCode))
                    .OrderBy(r => r.FetchTime)
                    .ToListAsync();

                var lastRecords = new List<FundData>();
                foreach (var code in myFundCodes)
                {
                    var lr = await _context.FundRecords
                        .Where(r => r.FetchTime < today && r.FundCode == code)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefaultAsync();
                    if (lr != null) lastRecords.Add(lr);
                }

                var allPastRecords = await _context.FundRecords
                    .Where(r => myFundCodes.Contains(r.FundCode) && r.ActualRate != 0)
                    .OrderByDescending(r => r.FetchTime)
                    .ToListAsync();

                // 🌟 猎隼侦察兵启动：下午 17:00 后刺探真实净值
                var realRateDict = new Dictionary<string, double>();
                if (localTime.Hour >= 17)
                {
                    var realRateTasks = myFundCodes.Select(async code =>
                    {
                        var rate = await GetTodayRealRateAsync(code, todayDash);
                        return new { code, rate };
                    });
                    var realRateResults = await Task.WhenAll(realRateTasks);
                    foreach (var res in realRateResults)
                    {
                        if (res.rate.HasValue) realRateDict[res.code] = res.rate.Value;
                    }
                }

                var result = myFunds.Select(config =>
                {
                    var fundRecords = todayRecords.Where(r => r.FundCode == config.FundCode).ToList();
                    var lastRecord = lastRecords.FirstOrDefault(r => r.FundCode == config.FundCode);

                    var past3DaysRecords = allPastRecords
                        .Where(r => r.FundCode == config.FundCode)
                        .Take(3)
                        .ToList();

                    double avgDiff = 0;
                    if (past3DaysRecords.Count > 0)
                    {
                        avgDiff = past3DaysRecords.Average(r => r.ActualRate - r.EstimatedRate);
                        if (avgDiff > 0.5) avgDiff = 0.5;
                        if (avgDiff < -0.5) avgDiff = -0.5;
                    }

                    var dataPoints = new List<object[]>();

                    if (lastRecord != null)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", Math.Round(lastRecord.EstimatedRate + avgDiff, 2) });
                    }

                    dataPoints.AddRange(fundRecords.Select(r => new object[] {
                        r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"),
                        Math.Round(r.EstimatedRate + avgDiff, 2)
                    }));

                    if (dataPoints.Count == 0)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });
                    }

                    // 是否已截获真实净值
                    bool isSettled = realRateDict.ContainsKey(config.FundCode);
                    double? actualRate = isSettled ? realRateDict[config.FundCode] : null;

                    double cost = config.CostAmount;
                    double currentAmount = config.HoldAmount;
                    double existingReturnRate = cost > 0 ? Math.Round(((currentAmount - cost) / cost) * 100.0, 2) : 0;
                    double breakEvenRate = cost > 0 ? Math.Round((currentAmount / cost) * 100.0, 2) : 0;

                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = currentAmount,
                        shares = config.HoldShares, // 修复前端需要的份额字段
                        cost = cost > 0 ? cost : (double?)null,
                        existingReturnRate = existingReturnRate,
                        breakEvenRate = breakEvenRate,
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        calibrationOffset = Math.Round(avgDiff, 4),
                        data = dataPoints,
                        isSettled = isSettled,    // 传给前端的真实引信
                        actualRate = actualRate
                    };
                });

                var finalResult = result.OrderByDescending(x => x.amount).ToList();

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(15));
                _cache.Set(cacheKey, finalResult, cacheOptions);

                return Ok(finalResult);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"服务器当场阵亡：{ex.Message}");
            }
        }

        // 🚀 恢复带份额的修改接口
        [HttpPost("update-details")]
        public async Task<IActionResult> UpdateDetailsAsync([FromQuery] string username, [FromForm] string code, [FromForm] double costAmount, [FromForm] double holdShares, [FromForm] string originalCode)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("指挥官身份未确认");
            if (string.IsNullOrEmpty(code) || costAmount <= 0) return BadRequest("本金或东财代码信息不完整");

            try
            {
                var existFund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && (f.FundCode == code || f.FundCode == originalCode));

                if (existFund != null)
                {
                    if (originalCode.StartsWith("待核对"))
                    {
                        var checkNewCode = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                        if (checkNewCode != null) return BadRequest($"东财代码 [{code}] 已经在库中，请不要输入重复代码。");

                        existFund.FundCode = code;
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares; // 更新份额
                        _context.MyFunds.Update(existFund);

                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares; // 更新份额
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();
                    _cache.Remove($"Tactical_TodayData_{username}");
                    return Ok($"本金、代码与份额补给完成！");
                }

                return BadRequest("未能匹配到该基金阵地");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"云端接收异常: {ex.Message}");
            }
        }

        [HttpGet("force-settle")]
        public async Task<IActionResult> ForceSettle()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");

            var targetFunds = await _context.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            var localTime = DateTime.UtcNow.AddHours(8);
            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            var resultLog = new List<string>
            {
                $"===== 手动清算战报 =====",
                $"当前时间: {localTime:yyyy-MM-dd HH:mm:ss}"
            };

            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await client.GetStringAsync(url);

                    if (!response.Contains("LSJZList")) continue;

                    using var doc = JsonDocument.Parse(response);
                    var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                    if (dataArray.GetArrayLength() > 0)
                    {
                        var latestData = dataArray[0];
                        string fsrq = latestData.GetProperty("FSRQ").GetString() ?? "";
                        string jzzzlStr = latestData.GetProperty("JZZZL").GetString() ?? "";

                        if (fsrq == todayStr && double.TryParse(jzzzlStr, out double actualRate))
                        {
                            var targetRecord = await _context.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= todayStart && r.FetchTime < tomorrowStart)
                                .OrderByDescending(r => r.FetchTime)
                                .FirstOrDefaultAsync();

                            if (targetRecord != null)
                            {
                                targetRecord.EstimatedRate = actualRate;
                                resultLog.Add($"✅ [{code}] 覆盖成功: {actualRate}%");
                            }
                        }
                    }
                }
                catch { }
            }
            await _context.SaveChangesAsync();
            return Ok(resultLog);
        }

        [HttpGet("global-indices")]
        public async Task<IActionResult> GetGlobalIndices()
        {
            var indices = new[]
            {
                new { name="上证指数", secid="1.000001" },
                new { name="科创50",   secid="1.000688" },
                new { name="创业板指", secid="0.399006" },
                new { name="恒生指数", secid="124.HSI"   },  // 🚀 绝对正确的港股前缀
                new { name="纳斯达克", secid="105.IXIC"  },  // 🚀 绝对正确的纳斯达克
                new { name="标普500",  secid="109.SPX"   },  // 🚀 绝对正确的标普500
                new { name="道琼斯",   secid="100.DJIA"  },
            };

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Referer", "https://www.eastmoney.com/");
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            var tasks = indices.Select(async idx =>
            {
                var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={idx.secid}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=250";
                try
                {
                    var json = await http.GetStringAsync(url);
                    return new { idx.name, data = json };
                }
                catch { return new { idx.name, data = "{}" }; }
            });

            var results = await Task.WhenAll(tasks);
            return Ok(results);
        }

        [HttpGet("sectors")]
        public async Task<IActionResult> GetSectors()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                // 🚀 四向暴击雷达！绝不遗漏跌幅榜！
                var urls = new[]
                {
                    "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=50&po=1&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:2&fields=f12,f14,f3",
                    "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=50&po=0&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:2&fields=f12,f14,f3",
                    "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=50&po=1&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:3&fields=f12,f14,f3",
                    "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=50&po=0&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:3&fields=f12,f14,f3"
                };

                var tasks = urls.Select(url => client.GetStringAsync(url)).ToArray();
                await Task.WhenAll(tasks);

                var list = new List<dynamic>();

                foreach (var task in tasks)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(task.Result);
                    if (doc.RootElement.TryGetProperty("data", out var dataObj) && dataObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (dataObj.TryGetProperty("diff", out var diffArray) && diffArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in diffArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("f3", out var f3Element) && f3Element.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    string name = item.GetProperty("f14").GetString() ?? "";
                                    if (name.Contains("昨日") || name.Contains("ST") || name.Contains("退市") || name.EndsWith("股") || name.Length > 6) continue;
                                    name = name.Replace("概念", "");

                                    list.Add(new
                                    {
                                        code = item.GetProperty("f12").GetString(),
                                        name = name,
                                        rate = f3Element.GetDouble()
                                    });
                                }
                            }
                        }
                    }
                }

                var distinctSorted = list.GroupBy(x => x.name)
                                         .Select(g => g.First())
                                         .OrderByDescending(x => (double)x.rate)
                                         .ToList();

                var top20 = distinctSorted.Take(20).ToList();
                var bottom20 = distinctSorted.TakeLast(20).OrderBy(x => (double)x.rate).ToList();

                return Ok(new { top = top20, bottom = bottom20 });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"雷达故障: {ex.Message}");
            }
        }

        [HttpGet("sector-details")]
        public async Task<IActionResult> GetSectorDetails([FromQuery] string secCode)
        {
            if (string.IsNullOrEmpty(secCode)) return BadRequest("缺少板块雷达识别码");

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                string url = $"http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=6&po=1&np=1&fltt=2&invt=2&fid=f3&fs=b:{secCode}&fields=f12,f14,f3";
                string response = await client.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var dataProp = doc.RootElement.GetProperty("data");

                if (dataProp.ValueKind == System.Text.Json.JsonValueKind.Null) return Ok(new List<dynamic>());

                var diffArray = dataProp.GetProperty("diff").EnumerateArray();
                var list = new List<dynamic>();

                foreach (var item in diffArray)
                {
                    if (item.TryGetProperty("f3", out var f3Element) && f3Element.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        list.Add(new
                        {
                            code = item.GetProperty("f12").GetString(),
                            name = item.GetProperty("f14").GetString(),
                            rate = f3Element.GetDouble()
                        });
                    }
                }
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"X光机故障: {ex.Message}");
            }
        }

        [HttpGet("sector-funds")]
        public async Task<IActionResult> GetSectorFunds([FromQuery] string sectorName)
        {
            if (string.IsNullOrEmpty(sectorName)) return BadRequest("缺少板块名称");

            string keyword = sectorName.Replace("概念", "").Replace("板块", "");

            if (keyword.Contains("蛋白") || keyword.Contains("CRO") || keyword.Contains("药") || keyword.Contains("单抗") || keyword.Contains("肝炎") || keyword.Contains("阿兹海默") || keyword.Contains("医疗") || keyword.Contains("医美") || keyword.Contains("生物"))
                keyword = "医药";
            else if (keyword.Contains("CPO") || keyword.Contains("光通信") || keyword.Contains("算力") || keyword.Contains("服务器") || keyword.Contains("宽带") || keyword.Contains("脑机"))
                keyword = "通信";
            else if (keyword.Contains("低空经济") || keyword.Contains("飞行") || keyword.Contains("卫星") || keyword.Contains("航天"))
                keyword = "军工";
            else if (keyword.Contains("电池") || keyword.Contains("锂") || keyword.Contains("钠") || keyword.Contains("储能") || keyword.Contains("光伏"))
                keyword = "新能源";
            else if (keyword.Contains("半导体") || keyword.Contains("光刻") || keyword.Contains("封装") || keyword.Contains("芯片"))
                keyword = "半导体";
            else if (keyword.Contains("苹果") || keyword.Contains("华为") || keyword.Contains("消费电子") || keyword.Contains("面板") || keyword.Contains("元器件"))
                keyword = "电子";
            else if (keyword.Contains("汽车") || keyword.Contains("整车"))
                keyword = "汽车";
            else if (keyword.Contains("游戏") || keyword.Contains("传媒") || keyword.Contains("短剧") || keyword.Contains("影视"))
                keyword = "传媒";
            else if (keyword.Contains("AI") || keyword.Contains("大模型") || keyword.Contains("数据") || keyword.Contains("软件"))
                keyword = "人工智能";
            else
            {
                string[] suffixes = { "制造", "外包", "服务", "设备", "商业", "制剂", "用品", "耗材", "制品", "工程", "产业", "概念" };
                foreach (var suffix in suffixes)
                {
                    if (keyword.EndsWith(suffix) && keyword.Length > suffix.Length)
                    {
                        keyword = keyword.Substring(0, keyword.Length - suffix.Length);
                        break;
                    }
                }
                if (keyword.Length >= 4) keyword = keyword.Substring(0, 2);
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resultList = new List<dynamic>();

            try
            {
                string searchUrl = $"http://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx?m=1&key={Uri.EscapeDataString(keyword)}";
                string searchRes = await client.GetStringAsync(searchUrl);

                using var doc = System.Text.Json.JsonDocument.Parse(searchRes);
                var datas = doc.RootElement.GetProperty("Datas");

                if (datas.ValueKind != System.Text.Json.JsonValueKind.Null && datas.GetArrayLength() > 0)
                {
                    var fundCodes = new List<string>();
                    var fundDict = new Dictionary<string, string>();

                    foreach (var item in datas.EnumerateArray())
                    {
                        if (item.TryGetProperty("CATEGORYDESC", out var cat) && cat.GetString() != "基金") continue;

                        string fCode = item.GetProperty("CODE").GetString();
                        string fName = item.GetProperty("NAME").GetString();

                        if ((fName.Contains("ETF") || fName.Contains("联接") || fName.Contains("指数")) && !fundDict.ContainsKey(fCode))
                        {
                            fundCodes.Add(fCode);
                            fundDict[fCode] = fName;
                            if (fundCodes.Count >= 6) break;
                        }
                    }

                    if (fundCodes.Count < 6)
                    {
                        foreach (var item in datas.EnumerateArray())
                        {
                            if (item.TryGetProperty("CATEGORYDESC", out var cat) && cat.GetString() != "基金") continue;

                            string fCode = item.GetProperty("CODE").GetString();
                            string fName = item.GetProperty("NAME").GetString();

                            if (!fundDict.ContainsKey(fCode))
                            {
                                fundCodes.Add(fCode);
                                fundDict[fCode] = fName;
                                if (fundCodes.Count >= 6) break;
                            }
                        }
                    }

                    var tasks = fundCodes.Select(async code =>
                    {
                        try
                        {
                            string gzUrl = $"http://fundgz.1234567.com.cn/js/{code}.js?rt={DateTime.Now.Ticks}";
                            string gzRes = await client.GetStringAsync(gzUrl);
                            var match = System.Text.RegularExpressions.Regex.Match(gzRes, @"\""gszzl\"":\""([^\""]+)\""");
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double rate))
                            {
                                return new { code = code, name = fundDict[code], rate = rate };
                            }
                        }
                        catch { }
                        return null;
                    });

                    var results = await Task.WhenAll(tasks);
                    foreach (var res in results) if (res != null) resultList.Add(res);
                }

                resultList = resultList.OrderByDescending(x => (double)x.GetType().GetProperty("rate").GetValue(x)).ToList();
                return Ok(resultList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"找基失败: {ex.Message}");
            }
        }

        [HttpPost("save-archive")]
        public async Task<IActionResult> SaveArchive([FromBody] ArchiveRequest req)
        {
            if (string.IsNullOrEmpty(req.Username)) return Unauthorized();

            try
            {
                var date = DateTime.Parse(req.DateStr).Date;

                var oldRecords = await _context.DailyArchives
                    .Where(a => a.Username == req.Username && a.RecordDate == date)
                    .ToListAsync();
                if (oldRecords.Any()) _context.DailyArchives.RemoveRange(oldRecords);

                req.Total.RecordDate = date;
                req.Total.FundCode = "TOTAL";
                req.Total.Username = req.Username;
                _context.DailyArchives.Add(req.Total);

                foreach (var f in req.Funds)
                {
                    f.RecordDate = date;
                    f.Username = req.Username;
                    _context.DailyArchives.Add(f);
                }

                await _context.SaveChangesAsync();
                return Ok("✅ 今日战报已永久封存！");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"封存失败: {ex.Message}");
            }
        }

        [HttpGet("auto-archive-nightly")]
        public async Task<IActionResult> AutoArchiveNightly()
        {
            try
            {
                var localTime = DateTime.UtcNow.AddHours(8);
                var today = localTime.Date;

                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("无阵地需要封存。");

                var userGroups = allFunds.GroupBy(f => f.Username);
                int savedCount = 0;

                foreach (var group in userGroups)
                {
                    string username = group.Key;
                    var userFunds = group.ToList();

                    // 如果今晚已经成功封存过，直接跳过，防重复
                    bool alreadyArchived = await _context.DailyArchives
                        .AnyAsync(a => a.Username == username && a.RecordDate == today);
                    if (alreadyArchived) continue;

                    double totalAssets = 0;
                    double totalCost = 0;

                    foreach (var fund in userFunds)
                    {
                        totalAssets += fund.HoldAmount;
                        totalCost += (fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount);

                        var todayRecord = await _context.FundRecords
                            .Where(r => r.FundCode == fund.FundCode && r.FetchTime >= today)
                            .OrderByDescending(r => r.FetchTime)
                            .FirstOrDefaultAsync();

                        double dailyRate = todayRecord?.ActualRate > 0 ? todayRecord.ActualRate : (todayRecord?.EstimatedRate ?? 0);
                        double dailyProfit = fund.HoldAmount * (dailyRate / 100.0);

                        _context.DailyArchives.Add(new DailyArchive
                        {
                            Username = username,
                            FundCode = fund.FundCode,
                            FundName = fund.FundName,
                            RecordDate = today,
                            Assets = fund.HoldAmount,
                            DailyProfit = Math.Round(dailyProfit, 2),
                            DailyRate = Math.Round(dailyRate, 2)
                        });
                    }

                    double totalDailyProfit = _context.DailyArchives.Local
                        .Where(a => a.Username == username && a.FundCode != "TOTAL" && a.RecordDate == today)
                        .Sum(a => a.DailyProfit);

                    double totalDailyRate = totalCost > 0 ? (totalDailyProfit / totalCost) * 100 : 0;

                    _context.DailyArchives.Add(new DailyArchive
                    {
                        Username = username,
                        FundCode = "TOTAL",
                        FundName = "总阵地",
                        RecordDate = today,
                        Assets = totalAssets,
                        DailyProfit = Math.Round(totalDailyProfit, 2),
                        DailyRate = Math.Round(totalDailyRate, 2)
                    });

                    savedCount++;
                }

                await _context.SaveChangesAsync();
                return Ok($"[{localTime:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动战报封存完毕！成功为 {savedCount} 位指挥官生成了历史档案。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动封存引擎异常: {ex.Message}");
            }
        }

        [HttpGet("get-archives")]
        public async Task<IActionResult> GetArchives([FromQuery] string username)
        {
            var records = await _context.DailyArchives
                .Where(a => a.Username == username)
                .OrderBy(a => a.RecordDate)
                .ToListAsync();
            return Ok(records);
        }

        [HttpGet("fund-holdings")]
        public async Task<IActionResult> GetFundHoldings([FromQuery] string fundCode)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 13; SM-G981B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
            client.DefaultRequestHeaders.Add("Host", "push2his.eastmoney.com");
            try
            {
                string positionUrl = $"https://fundmobapi.eastmoney.com/FundMNewApi/FundMNInverstPosition?FCODE={fundCode}&deviceid=Wap&plat=Wap&product=EFund&version=2.0.0";
                string posRes = await client.GetStringAsync(positionUrl);
                using var posDoc = System.Text.Json.JsonDocument.Parse(posRes);

                JsonElement dataElement;
                if (posDoc.RootElement.TryGetProperty("Datas", out var datasObj) && datasObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = datasObj;
                else if (posDoc.RootElement.TryGetProperty("Data", out var dataObj) && dataObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = dataObj;
                else
                    return Ok(new List<object>());

                JsonElement fundPosition;
                if (dataElement.TryGetProperty("fundStocks", out var stocksObj) && stocksObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = stocksObj;
                else if (dataElement.TryGetProperty("fundPosition", out var posObj) && posObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = posObj;
                else
                    return Ok(new List<object>());

                var stockList = new List<dynamic>();
                var secidList = new List<string>();

                foreach (var stock in fundPosition.EnumerateArray())
                {
                    string code = stock.TryGetProperty("GPDM", out var cProp) ? cProp.GetString() : "";
                    string name = stock.TryGetProperty("GPJC", out var nProp) ? nProp.GetString() : "";
                    string ratio = stock.TryGetProperty("JZBL", out var rProp) ? rProp.GetString() : "0";

                    if (!string.IsNullOrEmpty(code))
                    {
                        string prefix = "0.";
                        if (code.StartsWith("6")) prefix = "1.";
                        else if (code.Length == 5) prefix = "116.";

                        secidList.Add(prefix + code);
                        stockList.Add(new { code, name, ratio, rate = 0.0 });
                    }
                }

                if (secidList.Count > 0)
                {
                    string secids = string.Join(",", secidList);
                    string quoteUrl = $"http://push2.eastmoney.com/api/qt/ulist.np/get?secids={secids}&fields=f12,f14,f3";

                    using var quoteClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    quoteClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

                    string quoteRes = await quoteClient.GetStringAsync(quoteUrl);
                    using var quoteDoc = System.Text.Json.JsonDocument.Parse(quoteRes);

                    if (quoteDoc.RootElement.TryGetProperty("data", out var qData) && qData.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (qData.TryGetProperty("diff", out var diffs) && diffs.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var rateDict = new Dictionary<string, double>();
                            foreach (var diff in diffs.EnumerateArray())
                            {
                                string c = diff.TryGetProperty("f12", out var f12) ? f12.GetString() : "";
                                double r = 0;
                                if (diff.TryGetProperty("f3", out var f3) && f3.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    r = Math.Round(f3.GetDouble() / 100.0, 2);
                                }
                                if (!string.IsNullOrEmpty(c)) rateDict[c] = r;
                            }

                            var finalResult = stockList.Select(s => new
                            {
                                code = s.code,
                                name = s.name,
                                ratio = s.ratio,
                                rate = rateDict.ContainsKey((string)s.code) ? rateDict[(string)s.code] : 0.0
                            }).ToList();

                            return Ok(finalResult);
                        }
                    }
                }
                return Ok(stockList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"获取持仓失败: {ex.Message}");
            }
        }
    }
}