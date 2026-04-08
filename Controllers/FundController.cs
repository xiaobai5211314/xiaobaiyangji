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
        private readonly IMemoryCache _cache; // 🚀 缓存弹药库

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
                    catch { /* 忽略写入失败 */ }

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

        // =========================================================
        // OCR 极速导入引擎
        // =========================================================
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

                            if (userFundDict.TryGetValue(finalBestMatch.Code, out var exist))
                            {
                                exist.HoldAmount = holdAmount;
                                exist.CostAmount = costAmount;
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
                                    CostAmount = costAmount
                                };
                                _context.MyFunds.Add(newFund);
                                userFundDict[newFund.FundCode] = newFund;
                            }
                            importedCount++;
                            debugLog.Add(finalBestScore >= 99.0 ? $"⚡ 精准命中: {finalBestMatch.Name}" : $"✅ 模糊修复: {finalBestMatch.Name}");
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return Ok($"识别完成！成功同步 {importedCount} 只。\n提示：为了绝对精准计算，建议您去列表点击【扳手图标】手动补全[持仓份额]。\n\n[诊断日志]\n{string.Join("\n", debugLog)}");
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

        // 🚀 核心秘密武器：高速获取官方绝对净值 (dwjz)
        private async Task<double> GetFundDwjzAsync(string fundCode)
        {
            string cacheKey = $"Tactical_dwjz_{fundCode}";
            if (_cache.TryGetValue(cacheKey, out double cachedDwjz)) return cachedDwjz;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=1";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                if (dataArray.GetArrayLength() > 0)
                {
                    string dwjzStr = dataArray[0].GetProperty("DWJZ").GetString();
                    if (double.TryParse(dwjzStr, out double dwjz))
                    {
                        _cache.Set(cacheKey, dwjz, TimeSpan.FromHours(4)); // 净值一天只变一次，直接缓存4小时
                        return dwjz;
                    }
                }
            }
            catch { }
            return 1.0; // 极端防御保底
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

                // 🚀 获取全军今日估值记录
                var todayRecords = await _context.FundRecords
                    .Where(r => r.FetchTime >= today && myFundCodes.Contains(r.FundCode))
                    .OrderBy(r => r.FetchTime)
                    .ToListAsync();

                // 🚀 获取全军昨日记录
                var lastRecords = new List<FundData>();
                foreach (var code in myFundCodes)
                {
                    var lr = await _context.FundRecords
                        .Where(r => r.FetchTime < today && r.FundCode == code)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefaultAsync();
                    if (lr != null) lastRecords.Add(lr);
                }

                // 🚀 军工级内存优化
                var allPastRecords = await _context.FundRecords
                    .Where(r => myFundCodes.Contains(r.FundCode) && r.ActualRate != 0)
                    .OrderByDescending(r => r.FetchTime)
                    .ToListAsync();

                // 🔥 并发侦察：获取所有持有基金的官方最新净值！
                var dwjzTasks = myFundCodes.Select(code => GetFundDwjzAsync(code)).ToList();
                var dwjzResults = await Task.WhenAll(dwjzTasks);
                var dwjzDict = new Dictionary<string, double>();
                for (int i = 0; i < myFundCodes.Count; i++) dwjzDict[myFundCodes[i]] = dwjzResults[i];

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

                    // 💎 终极绝杀：份额计算引擎启动！
                    double dwjz = dwjzDict.ContainsKey(config.FundCode) ? dwjzDict[config.FundCode] : 1.0;
                    double currentAmount = config.HoldShares > 0 ? Math.Round(config.HoldShares * dwjz, 2) : config.HoldAmount;

                    double cost = config.CostAmount;
                    double existingReturnRate = cost > 0 ? Math.Round(((currentAmount - cost) / cost) * 100.0, 2) : 0;
                    double breakEvenRate = cost > 0 ? Math.Round((currentAmount / cost) * 100.0, 2) : 0;

                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = currentAmount, // 发给前端的已经是 100% 精确的市值了！
                        cost = cost > 0 ? cost : (double?)null,
                        shares = config.HoldShares, // 推给前端展示
                        existingReturnRate = existingReturnRate,
                        breakEvenRate = breakEvenRate,
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        calibrationOffset = Math.Round(avgDiff, 4),
                        data = dataPoints
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
                return StatusCode(500, $"服务器当场阵亡，死因：{ex.Message}");
            }
        }

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
                        existFund.HoldShares = holdShares; // 🚀 更新份额

                        _context.MyFunds.Update(existFund);

                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares; // 🚀 更新份额
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();

                    // 强制清空该用户的缓存，立即生效！
                    _cache.Remove($"Tactical_TodayData_{username}");

                    return Ok($"本金、份额与代码补给完成！精度已达最高级！");
                }

                return BadRequest("未能匹配到该基金阵地信息");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"云端接收异常: {ex.Message}");
            }
        }

        [HttpPost("save-archive")]
        public async Task<IActionResult> SaveArchive([FromBody] ArchiveRequest req)
        {
            if (string.IsNullOrEmpty(req.Username)) return Unauthorized();

            try
            {
                var date = DateTime.Parse(req.DateStr).Date;

                // 1. 清理防线：如果今天已经封存过了，先撤销旧档案，防止重复记录
                var oldRecords = await _context.DailyArchives
                    .Where(a => a.Username == req.Username && a.RecordDate == date)
                    .ToListAsync();
                if (oldRecords.Any()) _context.DailyArchives.RemoveRange(oldRecords);

                // 2. 封存总指挥部数据
                req.Total.RecordDate = date;
                req.Total.FundCode = "TOTAL";
                req.Total.Username = req.Username;
                _context.DailyArchives.Add(req.Total);

                // 3. 封存各单兵连队数据
                foreach (var f in req.Funds)
                {
                    f.RecordDate = date;
                    f.Username = req.Username;
                    _context.DailyArchives.Add(f);
                }

                await _context.SaveChangesAsync();
                return Ok("✅ 今日战报已永久封存入库！");
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

                // 1. 获取所有用户的持仓阵地
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("无阵地需要封存。");

                // 按指挥官代号分组处理
                var userGroups = allFunds.GroupBy(f => f.Username);
                int savedCount = 0;

                foreach (var group in userGroups)
                {
                    string username = group.Key;
                    var userFunds = group.ToList();

                    // 🚀 终极防重复锁定：改为覆盖模式！只要今天触发过，就用最新的覆盖之前的。
                    var oldRecords = await _context.DailyArchives
                        .Where(a => a.Username == username && a.RecordDate == today)
                        .ToListAsync();
                    if (oldRecords.Any()) 
                    {
                        _context.DailyArchives.RemoveRange(oldRecords);
                    }

                    double totalAssets = 0;
                    double totalCost = 0;

                    // 2. 封存各个单兵连队 (单只基金)
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
                            DailyRate = Math.Round(dailyRate, 2),
                            TotalProfit = Math.Round((fund.CostAmount > 0 ? fund.HoldAmount - fund.CostAmount : 0), 2),
                            TotalRate = Math.Round((fund.CostAmount > 0 ? ((fund.HoldAmount - fund.CostAmount) / fund.CostAmount * 100) : 0), 2)
                        });
                    }

                    // 3. 封存总指挥部 (总阵地)
                    double totalDailyProfit = _context.DailyArchives.Local
                        .Where(a => a.Username == username && a.FundCode != "TOTAL" && a.RecordDate == today)
                        .Sum(a => a.DailyProfit);

                    double totalDailyRate = totalCost > 0 ? (totalDailyProfit / totalCost) * 100 : 0;
                    double totalCumulativeProfit = totalAssets - totalCost;
                    double totalCumulativeRate = totalCost > 0 ? (totalCumulativeProfit / totalCost) * 100 : 0;

                    _context.DailyArchives.Add(new DailyArchive
                    {
                        Username = username,
                        FundCode = "TOTAL",
                        FundName = "总阵地",
                        RecordDate = today,
                        Assets = totalAssets,
                        DailyProfit = Math.Round(totalDailyProfit, 2),
                        DailyRate = Math.Round(totalDailyRate, 2),
                        TotalProfit = Math.Round(totalCumulativeProfit, 2),
                        TotalRate = Math.Round(totalCumulativeRate, 2)
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
                $"===== 科技军团 T+1 手动清算诊断战报 =====",
                $"当前系统校验时间: {localTime:yyyy-MM-dd HH:mm:ss}"
            };

            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await client.GetStringAsync(url);

                    if (!response.Contains("LSJZList"))
                    {
                        resultLog.Add($"❌ [{code}] 遭防火墙拦截！");
                        continue;
                    }

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
                                resultLog.Add($"✅ [{code}] 清算成功！已精准覆盖为真实净值: {actualRate}%");
                            }
                            else
                            {
                                resultLog.Add($"⚠️ [{code}] 找不到今天({todayStr})的盘中估值记录，无法覆盖！");
                            }
                        }
                        else
                        {
                            resultLog.Add($"⏳ [{code}] 今日({todayStr})真实财报还未出，其最新停留在: {fsrq}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultLog.Add($"🔥 [{code}] 发生致命代码报错: {ex.Message}");
                }
            }
            await _context.SaveChangesAsync();
            return Ok(resultLog);
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
                return Ok($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动清算执行完毕！共成功滚动更新了 {successCount} 只基金的市值。");
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
                return StatusCode(500, $"❌ 装载彻底失败: {ex.Message}");
            }
        }

        [HttpGet("global-indices")]
        public async Task<IActionResult> GetGlobalIndices()
        {
            var indices = new[]
            {
                new { name="上证指数", secid="1.000001" },
                new { name="科创50",   secid="1.000688" },
                new { name="创业板指", secid="0.399006" },
              new { name="恒生指数", secid="124.HSI"   },  // ✅ 正确
new { name="纳斯达克", secid="105.IXIC"  },  // ✅ 正确
new { name="标普500",  secid="109.SPX"   },  // ✅ 正确
            };

            // ... 下面的代码保持不变 ...
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
                string urlIndustry = "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=100&po=1&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:2&fields=f12,f14,f3";
                string urlConcept = "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=300&po=1&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:3&fields=f12,f14,f3";

                var task1 = client.GetStringAsync(urlIndustry);
                var task2 = client.GetStringAsync(urlConcept);
                await Task.WhenAll(task1, task2);

                var list = new List<dynamic>();

                // 🛡️ 终极扩容版：基民专属主题过滤白名单（超大词库，对标养基宝）
                var themeMap = new Dictionary<string, string>
                {
                    // 🤖 科技 / AI / 芯片产业链
                    {"人工智能", "人工智能"}, {"AI语料", "AI应用"}, {"AIGC概念", "AIGC"}, {"ChatGPT概念", "ChatGPT"},
                    {"CPO概念", "CPO"}, {"云计算概念", "云计算"}, {"算力概念", "算力"}, {"数据中心", "数据中心"},
                    {"存储芯片", "存储芯片"}, {"半导体", "半导体"}, {"电子元件", "电子元件"}, {"消费电子", "消费电子"},
                    {"通信设备", "通信"}, {"光学光电子", "面板显示"}, {"软件开发", "软件服务"}, {"游戏", "游戏"},
                    {"文化传媒", "传媒"}, {"互联网服务", "互联网"}, {"半导体材料设备", "半导体设备"}, {"大基金概念", "大基金"},

                    // 🔋 新能源 / 汽车 / 高端制造
                    {"光伏设备", "光伏"}, {"电池", "电池"}, {"汽车整车", "汽车整车"}, {"汽车零部件", "汽车零部件"},
                    {"能源金属", "能源金属"}, {"风电设备", "风电"}, {"储能", "储能"}, {"电网设备", "电网设备"},
                    {"航天航空", "军工"}, {"船舶制造", "船舶制造"}, {"机器人概念", "机器人"}, {"工业母机", "工业母机"}, {"低空经济", "低空经济"},

                    // 💊 医药 / 医疗
                    {"医疗器械", "医疗器械"}, {"中药", "中药"}, {"化学制药", "化学制药"}, {"生物制品", "生物医药"},
                    {"医疗服务", "医疗服务"}, {"医药商业", "医药商业"}, {"创新药", "创新药"}, {"CRO", "CRO"},

                    // 🍺 大消费 / 金融 / 周期
                    {"酿酒行业", "白酒"}, {"食品饮料", "食品饮料"}, {"家电行业", "家电"}, {"旅游酒店", "旅游酒店"},
                    {"农牧饲渔", "农业"}, {"煤炭行业", "煤炭"}, {"证券", "券商"}, {"银行", "银行"},
                    {"保险", "保险"}, {"房地产开发", "房地产"}, {"贵金属", "黄金"}, {"小金属", "小金属"},
                    {"钢铁行业", "钢铁"}, {"石油行业", "石油"}, {"电力行业", "电力"}, {"燃气", "燃气"}
                };

                void ParseAndAdd(string json)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataObj) && dataObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (dataObj.TryGetProperty("diff", out var diffArray) && diffArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in diffArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("f3", out var f3Element) && f3Element.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    string rawName = item.GetProperty("f14").GetString() ?? "";

                                    // ⚡ 拦截核验：只要命中我们的豪华词库，直接放行并重命名！
                                    if (themeMap.ContainsKey(rawName))
                                    {
                                        list.Add(new
                                        {
                                            code = item.GetProperty("f12").GetString(),
                                            name = themeMap[rawName],
                                            rate = f3Element.GetDouble()
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                ParseAndAdd(task1.Result);
                ParseAndAdd(task2.Result);

                // 🧹 去重，并按涨跌幅全员降序排列
                var distinctSorted = list.GroupBy(x => x.name)
                                         .Select(g => g.First())
                                         .OrderByDescending(x => (double)x.rate)
                                         .ToList();

                return Ok(distinctSorted);
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
    }
}