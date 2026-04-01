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

        // 🚀 杀招一：静态初始化，破除 Linux 幽灵死锁
        static FundController()
        {
            // 强制禁用全局代理寻找，防止 HttpClient 在 Linux 环境下傻等 20 秒
            System.Net.WebRequest.DefaultWebProxy = null;
        }

        // 🚀 杀招二：全局单例百度客户端 (Singleton)
        // 让程序和百度机房保持“热连接”，不用每次传图都重新 TCP 握手，直接白嫖省下 150ms 左右！
        private static readonly Baidu.Aip.Ocr.Ocr _baiduOcrClient = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c")
        {
            Timeout = 10000 // 内部缩短到 10 秒超时
        };

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; }
        }

        // 双引擎缓存
        private static List<FundInfoCache> _globalFundCache = null;
        private static Dictionary<string, FundInfoCache> _exactMatchDict = null;

        public FundController(AppDbContext context) { _context = context; }

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
                // 2. 监控：百度 OCR 阶段 (异步 + 熔断 + 单例高精度)
                // ==========================================
                
                // 🚀 直接调用上面定义好的全局单例客户端，省去连接耗时。并且指定为 AccurateBasic（高精度版）
                var ocrTask = Task.Run(() => _baiduOcrClient.AccurateBasic(finalProcessedBytes));

                // ⚡ 15秒熔断器保护
                if (await Task.WhenAny(ocrTask, Task.Delay(15000)) != ocrTask)
                {
                    return StatusCode(500, $"❌ 识别超时熔断！\n前置图片压缩成功 (耗时 {debugLog[0]})\n但呼叫百度 OCR 接口超过 15 秒无响应，请检查宝塔网络或代理配置！");
                }

                var result = await ocrTask;
                debugLog.Add($"⏱️ 百度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

                var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
                if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字，请确认图片清晰度");

                // ==========================================
                // 3. 🚀 杀招三：上下行断层缝合智能匹配算法
                // ==========================================
                // ==========================================
                // 3. 🚀 终极杀招：双轨试探智能匹配算法
                // ==========================================
                int importedCount = 0;
                string amountPattern = @"^\d[\d,]*\.\d{2}$"; // 寻找金额锚点

                for (int i = 1; i < texts.Count; i++)
                {
                    string currentLine = texts[i];

                    // 🎯 发现“金额锚点”！
                    if (Regex.IsMatch(currentLine, amountPattern))
                    {
                        string namePart1 = texts[i - 1];

                        // 过滤干扰标签
                        if (namePart1.Contains("收益") || namePart1.Contains("金额") || namePart1.Contains("包含") || Regex.IsMatch(namePart1, @"^[-\d\.,%]+$"))
                            continue;

                        double holdAmount = double.Parse(currentLine.Replace(",", ""));
                        double holdingIncome = 0;
                        string potentialFragment = "";

                        // 🔍 往下找，寻找收益和可能的半截名字
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

                        // 💡 【核心升级】：同时准备两个候选名字参与海选打分！
                        // 候选人1：单行名字 (应对华富这种没换行的)
                        // 候选人2：缝合名字 (应对天弘这种换行的)
                        string[] testNames = string.IsNullOrEmpty(potentialFragment)
                            ? new[] { namePart1 }
                            : new[] { namePart1, namePart1 + potentialFragment };

                        FundInfoCache finalBestMatch = null;
                        double finalBestScore = 0;

                        // 让两个候选人分别去数据库比对，谁分高听谁的
                        foreach (var testName in testNames)
                        {
                            string pureChinese = Regex.Replace(testName, @"[^\u4e00-\u9fa5]", "");
                            if (pureChinese.Length < 2) continue;

                            string normalizedOcr = NormalizeFundName(testName);
                            FundInfoCache bestMatch = null;
                            double bestScore = 0;

                            // 尝试精准秒杀
                            if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(testName, out exactFund)))
                            {
                                bestMatch = exactFund;
                                bestScore = 100.0;
                            }
                            else
                            {
                                // 模糊海选
                                var candidates = allFunds.Where(f => f.NormalizedName.Contains(pureChinese) ||
                                                                    pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));

                                foreach (var f in candidates)
                                {
                                    // 删除了坑爹的强行分类过滤，完全信任相似度算法
                                    double similarity = CalculateSimilarity(normalizedOcr, f.NormalizedName);
                                    double currentScore = similarity * 100;

                                    if (currentScore > bestScore)
                                    {
                                        bestScore = currentScore;
                                        bestMatch = f;
                                    }
                                }
                            }

                            // 记录本轮最高分
                            if (bestScore > finalBestScore)
                            {
                                finalBestScore = bestScore;
                                finalBestMatch = bestMatch;
                            }
                        }

                        // 🏆 最终评判：只要最高分超过 65 分，且金额合理，提取成功！
                        if (finalBestMatch != null && finalBestScore > 65 && holdAmount > 10)
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

                            if (finalBestScore >= 99.0)
                                debugLog.Add($"⚡ 精准命中: {finalBestMatch.Name}");
                            else
                                debugLog.Add($"✅ 模糊修复: {finalBestMatch.Name} ({finalBestScore:F1}%)");
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

            // 将所有字母转为大写，并无情地清洗掉 (QDII) 这个干扰项
            return name.ToUpper()
                       .Replace(" ", "").Replace("（", "(").Replace("）", ")")
                       .Replace("(QDII)", "").Replace("QDII", "") // 👈 就是加了这行！洗掉毒药！
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
                var funds = await GetAllFundsAsync(); 
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

                    // =======================================================
                    // 🧠 核心升级：AI 动态估值校准引擎 (滑动平均补偿算法)
                    // =======================================================
                    double avgDiff = 0;
                    
                    // 1. 去数据库里找出这只基金最近 3 天【已经清算出真实成绩】的记录
                    var past3DaysRecords = _context.FundRecords
                        .Where(r => r.FundCode == config.FundCode && r.ActualRate != 0) // ActualRate != 0 代表已出真实净值
                        .OrderByDescending(r => r.FetchTime)
                        .Take(3)
                        .ToList();

                    // 2. 算出这 3 天的平均“偷吃/砸盘”偏差率
                    if (past3DaysRecords.Count > 0)
                    {
                        // 逻辑：如果连续3天真实净值都比预估高 0.2%，那今天大概率也会高 0.2%
                        avgDiff = past3DaysRecords.Average(r => r.ActualRate - r.EstimatedRate);
                        
                        // 为了防止极端暴跌暴涨导致补偿过度，我们给补偿值加一个安全锁（最大不超过 ±0.5%）
                        if (avgDiff > 0.5) avgDiff = 0.5;
                        if (avgDiff < -0.5) avgDiff = -0.5;
                    }
                    // =======================================================

                    var dataPoints = new List<object[]>();

                    // 插入昨晚的收盘基准点
                    if (lastRecord != null)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", Math.Round(lastRecord.EstimatedRate + avgDiff, 2) });
                    }

                    // 3. 把算出来的偏差补偿值 (avgDiff) 动态加到今天的每一个预估数据点上！
                    dataPoints.AddRange(fundRecords.Select(r => new object[] { 
                        r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"), 
                        Math.Round(r.EstimatedRate + avgDiff, 2) // 👈 就在这里！系统每天都在自我修正！
                    }));

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
                        calibrationOffset = Math.Round(avgDiff, 2), // 把今天的校准补偿值也传给前端看看
                        data = dataPoints
                    };
                });


                // 让结果按照 amount (持仓金额) 从大到小降序排列后再发给大屏
                return Ok(result.OrderByDescending(x => x.amount));
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
            
            // 👇 核心伪装升级：全面模拟谷歌浏览器，突破防爬机制！
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            // 🚀 下面这行是最关键的“免死金牌”：告诉东财，咱们是“自己人”！
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

                [HttpGet("sector-funds")]
        public async Task<IActionResult> GetSectorFunds([FromQuery] string sectorName)
        {
            if (string.IsNullOrEmpty(sectorName)) return BadRequest("缺少板块名称");

                       // 🧠 1. 终极智能降维：把极度冷门的股市概念，强行翻译成宽泛的基金主题！
            string keyword = sectorName.Replace("概念", "").Replace("板块", "");

            // 🌟 核心映射字典：教系统什么是医药，什么是科技
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
                // 如果是没见过的生僻词，启动常规去尾法
                string[] suffixes = { "制造", "外包", "服务", "设备", "商业", "制剂", "用品", "耗材", "制品", "工程", "产业", "概念" };
                foreach (var suffix in suffixes)
                {
                    if (keyword.EndsWith(suffix) && keyword.Length > suffix.Length)
                    {
                        keyword = keyword.Substring(0, keyword.Length - suffix.Length);
                        break;
                    }
                }
                // 终极保底：如果名字还是太长，硬截取前2个字去搜
                if (keyword.Length >= 4) keyword = keyword.Substring(0, 2);
            }


            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resultList = new List<dynamic>();

            try
            {
                // 调用东财搜索 API
                string searchUrl = $"http://fundsuggest.eastmoney.com/FundSearch/api/FundSearchAPI.ashx?m=1&key={Uri.EscapeDataString(keyword)}";
                string searchRes = await client.GetStringAsync(searchUrl);

                using var doc = System.Text.Json.JsonDocument.Parse(searchRes);
                var datas = doc.RootElement.GetProperty("Datas");

                if (datas.ValueKind != System.Text.Json.JsonValueKind.Null && datas.GetArrayLength() > 0)
                {
                    var fundCodes = new List<string>();
                    var fundDict = new Dictionary<string, string>();

                    // 🛡️ 2. 第一轮海选：只要正宗的 ETF 和 指数基金！绝对不要股票！
                    foreach (var item in datas.EnumerateArray())
                    {
                        // 核心防线：验明正身，不是"基金"直接滚蛋！
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

                    // 🛡️ 3. 第二轮补录：如果指数基金凑不够 6 个，拿普通基金补齐（依然严格拒绝股票）
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

                    // 🚀 4. 并发轰炸获取实时估值
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

        [HttpGet("sectors")]
        public async Task<IActionResult> GetSectors()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            try
            {
                // pz=100：一次性把全市场近百个板块全拉过来！
                // 注意看 fs=m:90+t:3 这个参数，把原来的 t:2 改成了 t:3，直接切换到“概念板块”！
string url = "http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=100&po=1&np=1&fltt=2&invt=2&fid=f3&fs=m:90+t:3&fields=f12,f14,f3";

                string response = await client.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var diffArray = doc.RootElement.GetProperty("data").GetProperty("diff").EnumerateArray();

                var list = new List<dynamic>();
                foreach (var item in diffArray)
                {
                    // 过滤掉停牌或没有数据的异常板块
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

                // 按涨跌幅从高到低排序
                var sorted = list.OrderByDescending(x => x.rate).ToList();

                // 🔪 拔出排头兵（涨幅最高前 10）
                var top10 = sorted.Take(10).ToList();

                // 🔪 揪出吊车尾（跌幅最惨前 10，并且让跌得最惨的排在最前面）
                var bottom10 = sorted.TakeLast(10).OrderBy(x => x.rate).ToList();

                return Ok(new { top = top10, bottom = bottom10 });
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
                // 🕵️‍♂️ 东方财富深层 API：fs=b:{secCode} 代表查询这个板块下的成分股
                // pz=6 代表只取前 6 名龙头，po=1 代表按涨幅降序排列
                string url = $"http://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=6&po=1&np=1&fltt=2&invt=2&fid=f3&fs=b:{secCode}&fields=f12,f14,f3";

                string response = await client.GetStringAsync(url);
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                var dataProp = doc.RootElement.GetProperty("data");

                // 防止某些冷门板块没有成分股数据
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
            
            // 🛡️ 核心防线：加上满级伪装，冒充谷歌浏览器从东财官网访问！
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");

            try
            {
                // 📦 1. 抓取该基金最新公布的“十大重仓股”清单
                string positionUrl = $"https://fundmobapi.eastmoney.com/FundMNewApi/FundMNInverstPosition?FCODE={fundCode}&deviceid=Wap&plat=Wap&product=EFund&version=2.0.0";
                string posRes = await client.GetStringAsync(positionUrl);
                using var posDoc = System.Text.Json.JsonDocument.Parse(posRes);

                var dataElement = posDoc.RootElement.GetProperty("Data");
                if (dataElement.ValueKind == System.Text.Json.JsonValueKind.Null) return Ok(new List<object>());

                var fundPosition = dataElement.GetProperty("fundPosition");
                if (fundPosition.ValueKind == System.Text.Json.JsonValueKind.Null) return Ok(new List<object>());

                var stockList = new List<dynamic>();
                var secidList = new List<string>(); // 用来组装查询股票实时涨幅的代码列表

                foreach (var stock in fundPosition.EnumerateArray())
                {
                    string code = stock.GetProperty("GPDM").GetString();
                    string name = stock.GetProperty("GPJC").GetString();
                    string ratio = stock.GetProperty("JZBL").GetString(); // 仓位占比

                    // 东方财富股票行情前缀规则：6开头是沪市(1.)，0或3开头是深市(0.)
                    string prefix = code.StartsWith("6") ? "1." : "0.";
                    secidList.Add(prefix + code);

                    stockList.Add(new { code, name, ratio, rate = 0.0 });
                }

                // 📈 2. 拿着这10只股票代码，去查它们【今天的实时涨跌幅】！
                if (secidList.Count > 0)
                {
                    string secids = string.Join(",", secidList);
                    string quoteUrl = $"http://push2.eastmoney.com/api/qt/ulist.np/get?secids={secids}&fields=f12,f14,f3";
                    
                    // 带着伪装去请求实时股票涨跌，绝对畅通无阻
                    string quoteRes = await client.GetStringAsync(quoteUrl);
                    using var quoteDoc = System.Text.Json.JsonDocument.Parse(quoteRes);

                    var qData = quoteDoc.RootElement.GetProperty("data");
                    if (qData.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        var diffs = qData.GetProperty("diff");
                        var rateDict = new Dictionary<string, double>();
                        foreach (var diff in diffs.EnumerateArray())
                        {
                            string c = diff.GetProperty("f12").GetString();
                            double r = diff.GetProperty("f3").ValueKind == System.Text.Json.JsonValueKind.Number ? diff.GetProperty("f3").GetDouble() : 0;
                            rateDict[c] = r;
                        }

                        // 3. 将“持仓列表”和“实时涨跌幅”强行缝合返回
                        var finalResult = stockList.Select(s => new
                        {
                            code = s.code,
                            name = s.name,
                            ratio = s.ratio,
                            rate = rateDict.ContainsKey(s.code) ? rateDict[s.code] : 0.0
                        }).ToList();

                        return Ok(finalResult);
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
