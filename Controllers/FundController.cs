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
                // ==========================================
                // 1. 监控：图片压缩阶段
                // ==========================================
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

                // ==========================================
                // 2. 监控：百度 OCR 阶段 (加装 15 秒防挂死熔断器)
                // ==========================================
                var client = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c");
                
                // 🚀 将同步的百度请求放入后台任务，防止卡死 ASP.NET 线程
                var ocrTask = Task.Run(() => client.GeneralBasic(finalProcessedBytes));

                // ⚡ 熔断机制：最多等 15 秒，等不到就报警，绝对不给 Nginx 报 504 的机会！
                if (await Task.WhenAny(ocrTask, Task.Delay(15000)) != ocrTask)
                {
                    return StatusCode(500, $"❌ 识别超时熔断！\n前置图片压缩成功 (耗时 {debugLog[0]})\n但呼叫百度 OCR 接口超过 15 秒无响应，请检查宝塔服务器的外网连接！");
                }

                var result = await ocrTask;
                debugLog.Add($"⏱️ 百度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

                var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
                if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字");
debugLog.Add($"[🔍 百度OCR原文前10行]\n{string.Join("\n", texts.Take(10))}\n");

                // ==========================================
                  // ==========================================
                // 3. 极速匹配引擎 (带跨行打捞 A/C 后缀功能)
                // ==========================================
                int importedCount = 0;

                for (int i = 0; i < texts.Count; i++)
                {
                    string combinedName = texts[i];
                    string pureChinese = Regex.Replace(combinedName, @"[^\u4e00-\u9fa5]", "");
                    
                    // 起始行必须有足够的汉字才配叫基金名字
                    if (pureChinese.Length < 3) continue; 

                    string ocrClass = ExtractFundClass(combinedName);

                    // 🚀 终极杀招：跨越金额行，寻找“被打断的名字尾巴”（重点捞 A/C 类后缀）
                    for (int step = 1; step <= 5 && (i + step) < texts.Count; step++)
                    {
                        string nextLine = texts[i + step].Trim();
                        
                        // 发现是金额或收益率，说明到了数据区，跳过这行，继续往下找尾巴！
                        if (Regex.IsMatch(nextLine, @"^[-+一_]?\s*[\d,\.]+\s*%?$")) continue;
                        
                        // 清洗掉支付宝的干扰词
                        string cleanNext = Regex.Replace(nextLine, @"(金选|指数基金|市场解读|已更新)", "").Trim();
                        if (string.IsNullOrEmpty(cleanNext)) continue;

                        // 如果下一行的汉字超过4个，说明大概率是下一只基金了，赶紧刹车！
                        if (Regex.Replace(cleanNext, @"[^\u4e00-\u9fa5]", "").Length >= 4) break;

                        // 把碎掉的尾巴拼起来（比如 "接(QDI)C"）
                        combinedName += cleanNext;
                        
                        // 重新尝试提取 A/C 类 (只要捞到了就锁定)
                        if (string.IsNullOrEmpty(ocrClass)) ocrClass = ExtractFundClass(cleanNext);
                    }

                    string normalizedOcr = NormalizeFundName(combinedName);
                    FundInfoCache bestMatch = null;
                    double bestScore = 0;

                    // ⚡ 降维打击：字典秒杀
                    if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(combinedName, out exactFund)))
                    {
                        bestMatch = exactFund;
                        bestScore = 100.0;
                        debugLog.Add($"⚡ 字典秒杀: {bestMatch.Name}");
                    }
                    else
                    {
                        // 🐌 模糊匹配
                        var candidates = allFunds.Where(f => f.NormalizedName.Contains(pureChinese) || 
                                                            pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));

                        foreach (var f in candidates)
                        {
                            string dbClass = ExtractFundClass(f.Name);
                            // 🛡️ 绝对隔离：OCR提取到了C，就绝不允许匹配A！
                            if (!string.IsNullOrEmpty(ocrClass) && !string.IsNullOrEmpty(dbClass) && ocrClass != dbClass) continue;

                            double similarity = CalculateSimilarity(normalizedOcr, f.NormalizedName);
                            double currentScore = similarity * 100;

                            if (currentScore > bestScore)
                            {
                                bestScore = currentScore;
                                bestMatch = f;
                            }
                        }
                    }

                    // 🎯 寻找金额并入库
                    if (bestMatch != null && bestScore > 70)
                    {
                        double marketValue = 0, holdingIncome = 0;
                        int linesConsumed = 0;

                        for (int j = 1; j <= 8 && (i + j) < texts.Count; j++)
                        {
                            string next = texts[i + j].Trim();
                            
                            // 修复 OCR 把负号认成汉字 "一" 的情况
                            string cleanNumStr = next.Replace("一", "-").Replace("_", "-").Replace(" ", "");
                            
                            if (Regex.IsMatch(cleanNumStr, @"^[-+]?\d{1,3}(,\d{3})*(\.\d{2})?$"))
                            {
                                double val = double.Parse(cleanNumStr.Replace(",", ""));
                                
                                if (marketValue == 0 && val > 10 && !cleanNumStr.Contains("+") && !cleanNumStr.Contains("-"))
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

                        if (marketValue > 100)
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
                            if(bestScore < 100) debugLog.Add($"✅ 模糊匹配: {bestMatch.Name} ({bestScore:F1}%)");
                        }
                    }
                }


                await _context.SaveChangesAsync();
                return Ok($"识别完成！成功同步 {importedCount} 只。\n\n[诊断日志]\n{string.Join("\n", debugLog)}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ 代码执行出现异常: {ex.Message}\n请检查上传的图片格式是否正确。");
            }
        }


        // ================================= 核心辅助方法 =================================

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
            return name.Replace(" ", "").Replace("（", "(").Replace("）", ")")
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