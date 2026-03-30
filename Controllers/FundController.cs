using Baidu.Aip.Ocr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;
using 估值助手.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;

namespace 估值助手.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FundController : ControllerBase
    {
        private readonly AppDbContext _context;

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; }
        }

        // 双引擎缓存：List 用于模糊遍历，Dictionary 用于 O(1) 极速秒杀
        private static List<FundInfoCache> _globalFundCache = null;
        private static Dictionary<string, FundInfoCache> _exactMatchDict = null;

        public FundController(AppDbContext context) { _context = context; }

        /// <summary>
        /// 获取全量基金库，并构建 O(1) 极速匹配字典
        /// </summary>
        /// <summary>
        /// 获取全量基金库（修复版：隔离 Redis 异常，防止连环崩溃）
        /// </summary>
        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null && _exactMatchDict != null && _globalFundCache.Count > 0) return _globalFundCache;

            string cachedData = null;

            // 🛡️ 阶段 1：尝试秒连 Redis（限制 1.5 秒超时，防止卡死服务器导致 504）
            try
            {
                var options = ConfigurationOptions.Parse("localhost:6379");
                options.ConnectTimeout = 1500; // 强制 1.5 秒超时
                options.SyncTimeout = 1500;
                // options.Password = "你的密码"; // 💡 如果宝塔Redis有密码，把这行注释解开填上去

                using var redis = ConnectionMultiplexer.Connect(options);
                var db = redis.GetDatabase();
                var redisValue = await db.StringGetAsync("global_fund_db_cache_v3");
                if (redisValue.HasValue) cachedData = redisValue.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] Redis 瘫痪或未开启，准备启动后备隐藏能源(直接下载): {ex.Message}");
            }

            // 如果 Redis 里有数据，直接起飞
            if (!string.IsNullOrEmpty(cachedData))
            {
                _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                BuildExactMatchDictionary(_globalFundCache);
                return _globalFundCache;
            }

            // 🛡️ 阶段 2：Redis 没数据或连不上，走东方财富接口下载
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

                    // 尝试顺手存入 Redis，存不进也无所谓，不抛异常
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
                Console.WriteLine($"[致命] 东方财富接口也挂了: {ex.Message}");
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
        // ==================================================        [HttpPost("import-ocr")]
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
                // 1. 压缩图片阶段
                using (var inputStream = imageFile.OpenReadStream())
                {
                    using (Image image = Image.Load(inputStream))
                    {
                        int targetMaxWidth = 800; // 降低宽度进一步提速
                        if (image.Width > targetMaxWidth)
                        {
                            int newHeight = (int)((double)image.Height / image.Width * targetMaxWidth);
                            image.Mutate(x => x.Resize(targetMaxWidth, newHeight));
                        }
                        image.Mutate(x => x.BackgroundColor(Color.White));
                        using (var outputStream = new MemoryStream())
                        {
                            image.SaveAsJpeg(outputStream, new JpegEncoder { Quality = 50 }); // 极限压缩
                            finalProcessedBytes = outputStream.ToArray();
                        }
                    }
                }
                debugLog.Add($"⏱️ 图片压缩耗时: {watch.ElapsedMilliseconds} ms");
                watch.Restart();

                // 2. OCR 熔断阶段
                  // 2. 调用 OCR（高精度熔断版）
            var client = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c");
            
            // 🚀 【核心变更】：正式切换为 AccurateBasic 高精度版文字识别接口！
            // 该接口能大幅提高模糊、换行字体的识别率，彻底消灭漏单。
            var ocrTask = Task.Run(() => client.AccurateBasic(finalProcessedBytes));

            // ⚡ 熔断机制：高精度处理慢，最多给 20 秒
            if (await Task.WhenAny(ocrTask, Task.Delay(20000)) != ocrTask)
            {
                return StatusCode(500, $"❌ 高精度识别超时！\n图片上传耗时 {debugLog[0]}\n但呼叫百度高精度接口超过 20 秒无响应，请稍后再试或切换回通用版！");
            }

            var result = await ocrTask;
            debugLog.Add($"⏱️ 百度高精度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

            var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
            if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字");
                // 3. 极速权重匹配引擎
                for (int i = 0; i < texts.Count; i++)
                {
                    string combinedName = texts[i];
                    string pureChinese = Regex.Replace(combinedName, @"[^\u4e00-\u9fa5]", "");
                    
                    if (pureChinese.Length < 3) continue; 

                    string ocrClass = ExtractFundClass(combinedName);

                    // 🚀 跨行打捞被金额打断的后缀 (如 "接(QDII)C")
                    for (int step = 1; step <= 5 && (i + step) < texts.Count; step++)
                    {
                        string nextLine = texts[i + step].Trim();
                        // 遇到数字行直接跳过，不刹车
                        if (Regex.IsMatch(nextLine, @"^[-+一_]?\s*[\d,\.]+\s*%?$")) continue;
                        
                        string cleanNext = Regex.Replace(nextLine, @"(金选|指数基金|市场解读)", "").Trim();
                        if (string.IsNullOrEmpty(cleanNext)) continue;

                        if (Regex.Replace(cleanNext, @"[^\u4e00-\u9fa5]", "").Length >= 4) break;

                        combinedName += cleanNext;
                        if (string.IsNullOrEmpty(ocrClass)) ocrClass = ExtractFundClass(cleanNext);
                    }

                    string normalizedOcr = NormalizeFundName(combinedName);
                    FundInfoCache bestMatch = null;
                    double bestScore = 0;

                    if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(combinedName, out exactFund)))
                    {
                        bestMatch = exactFund;
                        bestScore = 100.0;
                        debugLog.Add($"⚡ 字典秒杀: {bestMatch.Name}");
                    }
                    else
                    {
                        // 使用全新的降维特征库
                        var candidates = allFunds.Where(f => f.NormalizedName.Contains(pureChinese) || 
                                                            pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));

                        foreach (var f in candidates)
                        {
                            string dbClass = ExtractFundClass(f.Name);
                            if (!string.IsNullOrEmpty(ocrClass) && !string.IsNullOrEmpty(dbClass) && ocrClass != dbClass) continue;

                            // 🚀 核心升级：调用带权重的评分算法
                            double currentScore = CalculateWeightedScore(normalizedOcr, f.NormalizedName) * 100;

                            if (currentScore > bestScore)
                            {
                                bestScore = currentScore;
                                bestMatch = f;
                            }
                        }
                    }

                    // 🎯 门槛降为 60 分以容错，且金额门槛降低
                    if (bestMatch != null && bestScore > 60)
                    {
                        double marketValue = 0, holdingIncome = 0;
                        int linesConsumed = 0;

                        for (int j = 1; j <= 8 && (i + j) < texts.Count; j++)
                        {
                            string next = texts[i + j].Trim();
                            string cleanNumStr = next.Replace("一", "-").Replace("_", "-").Replace(" ", "");
                            
                            if (Regex.IsMatch(cleanNumStr, numPattern))
                            {
                                double val = double.Parse(cleanNumStr.Replace(",", ""));
                                
                                if (marketValue == 0 && val > 0 && !cleanNumStr.Contains("+") && !cleanNumStr.Contains("-"))
                                {
                                    marketValue = val;
                                    linesConsumed = Math.Max(linesConsumed, j);
                                }
                                else if (holdingIncome == 0 && (cleanNumStr.Contains("+") || cleanNumStr.Contains("-") || marketValue > 0))
                                {
                                    holdingIncome = val;
                                    linesConsumed = Math.Max(linesConsumed, j);
                                    break; 
                                }
                            }
                        }

                        // 🚀 核心修复：市场价值大于 0 就入库 (拯救你的国投瑞银)
                        if (marketValue > 0)
                        {
                            double costAmount = Math.Round(marketValue - holdingIncome, 2);

                            if (userFundDict.TryGetValue(bestMatch.Code, out var exist))
                            {
                                exist.HoldAmount = marketValue;
                                exist.CostAmount = costAmount;
                                _context.MyFunds.Update(exist);
                            }
                            else
                            {
                                var newFund = new MyFundConfig
                                {
                                    Username = username,
                                    FundCode = bestMatch.Code,
                                    FundName = bestMatch.Name,
                                    HoldAmount = marketValue,
                                    CostAmount = costAmount
                                };
                                _context.MyFunds.Add(newFund);
                                userFundDict[newFund.FundCode] = newFund; 
                            }
                            importedCount++;
                            i += linesConsumed; 
                            if(bestScore < 100) debugLog.Add($"✅ 权重匹配: {bestMatch.Name} ({bestScore:F1}%)");
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

        // ===============================================
        // 👉 新增的权重引擎算法 (必须加上这三个方法)
        // ===============================================

        private double CalculateWeightedScore(string ocr, string dbName)
        {
            if (string.IsNullOrEmpty(ocr) || string.IsNullOrEmpty(dbName)) return 0.0;
            if (ocr == dbName) return 1.0;

            double totalScore = 0.0;
            double maxPossibleScore = 0.0;

            // 1. 前缀权重 (基金公司名)
            double prefixWeight = 30.0;
            maxPossibleScore += prefixWeight;
            if (ocr.Length >= 2 && dbName.Length >= 2 && ocr.Substring(0, 2) == dbName.Substring(0, 2))
            {
                totalScore += prefixWeight;
                if (ocr.Length >= 4 && dbName.Length >= 4 && ocr.Substring(0, 4) == dbName.Substring(0, 4))
                {
                    totalScore += 10.0; 
                }
            }
            maxPossibleScore += 10.0; 

            // 2. 连续子串权重
            int maxLcsLen = GetLongestCommonSubsequenceLength(ocr, dbName);
            double lcsWeight = 50.0;
            maxPossibleScore += lcsWeight;
            double lcsRatio = (double)maxLcsLen / Math.Min(ocr.Length, dbName.Length);
            totalScore += lcsWeight * lcsRatio;

            // 3. 字符覆盖权重
            double coverageWeight = 20.0;
            maxPossibleScore += coverageWeight;
            int matchCount = ocr.Count(c => dbName.Contains(c));
            totalScore += coverageWeight * ((double)matchCount / ocr.Length);

            // 4. 长度差异惩罚
            double lengthPenalty = 1.0;
            if (Math.Abs(ocr.Length - dbName.Length) > 5) lengthPenalty = 0.85;

            return (totalScore / maxPossibleScore) * lengthPenalty;
        }

        private int GetLongestCommonSubsequenceLength(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2)) return 0;
            int[,] matrix = new int[str1.Length + 1, str2.Length + 1];
            int maxLen = 0;
            for (int i = 1; i <= str1.Length; i++)
            {
                for (int j = 1; j <= str2.Length; j++)
                {
                    if (str1[i - 1] == str2[j - 1])
                    {
                        matrix[i, j] = matrix[i - 1, j - 1] + 1;
                        if (matrix[i, j] > maxLen) maxLen = matrix[i, j];
                    }
                    else { matrix[i, j] = 0; }
                }
            }
            return maxLen;
        }



        // ================================= 基础业务接口 =================================

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
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(codes)) return BadRequest("指挥官，缺少清算参数！");

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
                return Ok($"夜间清算完毕！已将 {count} 只基金的今日收益滚入市值，明天将以新本金计算复利！");
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
        var funds = await GetAllFundsAsync(); // 只测试下载基金库，不搞OCR
        watch.Stop();
        return Ok($"✅ 基金库装载成功！共 {funds.Count} 只。总耗时: {watch.ElapsedMilliseconds} 毫秒。");
    }
    catch (Exception ex)
    {
        return StatusCode(500, $"❌ 装载彻底失败: {ex.Message}");
    }
}
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            try
            {
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE MyFunds ADD COLUMN LastSettledDate VARCHAR(20);");
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN ActualRate DOUBLE NOT NULL DEFAULT 0;");
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN DiffRate DOUBLE NOT NULL DEFAULT 0;");
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE MyFunds ADD COLUMN CostAmount DOUBLE NOT NULL DEFAULT 0;");
                }
                catch { /* 忽略已存在的报错 */ }

                var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                var localTime = DateTime.UtcNow.AddHours(8);
                var today = localTime.Date;
                string todayStr = localTime.ToString("yyyy'/'MM'/'dd");

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

                var result = myFunds.Select(config => {
                    var fundRecords = todayRecords.Where(r => r.FundCode == config.FundCode).ToList();
                    var lastRecord = lastRecords.FirstOrDefault(r => r.FundCode == config.FundCode);

                    var dataPoints = new List<object[]>();

                    if (lastRecord != null)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", lastRecord.EstimatedRate });
                    }

                    dataPoints.AddRange(fundRecords.Select(r => new object[] { r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"), r.EstimatedRate }));

                    if (dataPoints.Count == 0)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });
                    }

                    double cost = config.CostAmount;
                    double currentAmount = config.HoldAmount;
                    double existingReturnRate = cost > 0 ? Math.Round(((currentAmount - cost) / cost) * 100.0, 2) : 0;
                    double breakEvenRate = cost > 0 ? Math.Round((currentAmount / cost) * 100.0, 2) : 0;

                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = currentAmount,
                        cost = cost > 0 ? cost : (double?)null,
                        existingReturnRate = existingReturnRate,
                        breakEvenRate = breakEvenRate,
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        data = dataPoints
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"服务器当场阵亡，死因：{ex.Message}");
            }
        }

        [HttpPost("update-details")]
        public async Task<IActionResult> UpdateDetailsAsync([FromQuery] string username, [FromForm] string code, [FromForm] double costAmount, [FromForm] string originalCode)
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
                        _context.MyFunds.Update(existFund);

                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        existFund.CostAmount = costAmount;
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();
                    return Ok($"本金与 {code} 代码补给完成！");
                }

                return BadRequest("未能匹配到该基金阵地信息");
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
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");

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
    }
}