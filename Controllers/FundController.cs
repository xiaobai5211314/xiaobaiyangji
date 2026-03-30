using Baidu.Aip.Ocr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;
using 估值助手.Models;

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
        }
        private static List<FundInfoCache> _globalFundCache = null;

        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            // 第一层：内存缓存
            if (_globalFundCache != null) return _globalFundCache;

            try
            {
                // 🔑 连接你的宝塔 Redis (对齐 127.0.0.1:6379)
                ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost:6379");
                IDatabase db = redis.GetDatabase();

                string cacheKey = "global_fund_db_cache";
                var cachedData = await db.StringGetAsync(cacheKey);

                // 第二层：Redis 缓存
                if (!cachedData.IsNull)
                {
                    _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                    return _globalFundCache;
                }

                // 第三层：远程抓取 (仅在 Redis 为空时执行一次)
                using var client = new HttpClient();
                string jsData = await client.GetStringAsync("http://fund.eastmoney.com/js/fundcode_search.js");

                int startIndex = jsData.IndexOf('[');
                int endIndex = jsData.LastIndexOf(']');
                if (startIndex > 0 && endIndex > 0)
                {
                    string json = jsData.Substring(startIndex, endIndex - startIndex + 1);
                    var rawList = JsonSerializer.Deserialize<List<List<string>>>(json);
                    _globalFundCache = rawList.Select(x => new FundInfoCache { Code = x[0], Name = x[2] }).ToList();

                    // 💾 写入 Redis，有效期 24 小时
                    string serialData = JsonSerializer.Serialize(_globalFundCache);
                    await db.StringSetAsync(cacheKey, serialData, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Redis 连接异常: " + ex.Message);
            }

            return _globalFundCache ?? new List<FundInfoCache>();
        }
        public FundController(AppDbContext context) { _context = context; }


        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            var fundDb = await GetAllFundsAsync();
            byte[] imageBytes;
            using (var ms = new MemoryStream()) { await imageFile.CopyToAsync(ms); imageBytes = ms.ToArray(); }

            var client = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c") { Timeout = 60000 };
            var result = client.AccurateBasic(imageBytes);

            List<string> texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
            int importedCount = 0;
            List<string> debugLog = new List<string>(); // 调试信息

            string numPattern = @"[-+]?\d{1,3}(?:,\d{3})*(?:\.\d{2})?";

            for (int i = 0; i < texts.Count; i++)
            {
                string text1 = texts[i];
                if (string.IsNullOrWhiteSpace(text1)) continue;

                // 防线1：至少3个汉字 + 排除干扰词
                if (Regex.Matches(text1, @"[\u4e00-\u9fa5]").Count < 3 ||
                    text1.Contains("金选") || text1.Contains("收益") || text1.Contains("金额") || text1.Contains("市场"))
                {
                    continue;
                }

                // 防线2：智能拼接下一行（处理长名称拆行）
                string text2 = (i + 1 < texts.Count) ? texts[i + 1] : "";
                string combinedName = (text1 + text2).Replace(" ", "").Trim();

                // 归一化处理
                string normalizedOcr = NormalizeFundName(combinedName);

                // 提取类属后缀（C/A/QDII）
                string ocrClass = ExtractFundClass(combinedName);

                FundInfoCache matchedFund = null;
                double bestScore = 0;

                foreach (var f in fundDb)
                {
                    string normalizedDb = NormalizeFundName(f.Name);
                    double score = CalculateSimilarity(normalizedOcr, normalizedDb);

                    // 类属优先加分
                    string dbClass = ExtractFundClass(f.Name);
                    if (ocrClass == dbClass && !string.IsNullOrEmpty(ocrClass)) score += 0.15;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        matchedFund = f;
                    }
                }

                debugLog.Add($"OCR文本: {combinedName} | 归一化: {normalizedOcr} | 最佳匹配: {matchedFund?.Name} (分数:{bestScore:F3})");

                if (matchedFund != null && bestScore >= 0.78) // 阈值可根据实际调优
                {
                    // 金额解析（向后查找最近的数字）
                    double marketValue = 0;
                    for (int j = 1; j <= 8 && (i + j) < texts.Count; j++) // 扩大搜索范围
                    {
                        string next = texts[i + j];
                        if (Regex.IsMatch(next, numPattern))
                        {
                            double val = double.Parse(next.Replace(",", ""));
                            if (marketValue == 0) marketValue = val;
                            else if (next.Contains("+") || next.Contains("-"))
                            {
                                // 持仓收益（可选）
                                break;
                            }
                        }
                    }

                    if (marketValue > 100) // 合理市值下限
                    {
                        double costAmount = Math.Round(marketValue, 2); // 简化成本计算（可保留原逻辑）

                        var exist = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == matchedFund.Code);
                        if (exist != null)
                        {
                            exist.HoldAmount = marketValue;
                            exist.CostAmount = costAmount;
                        }
                        else
                        {
                            _context.MyFunds.Add(new MyFundConfig
                            {
                                Username = username,
                                FundCode = matchedFund.Code,
                                FundName = matchedFund.Name,
                                HoldAmount = marketValue,
                                CostAmount = costAmount
                            });
                        }
                        importedCount++;
                        i += 1; // 跳过下一行
                        debugLog.Add($"✅ 成功导入: {matchedFund.Name} ({matchedFund.Code}) 市值 {marketValue}");
                    }
                }
            }

            await _context.SaveChangesAsync();

            // 返回调试信息（生产环境可移除或改为日志）
            string debugInfo = string.Join("\n", debugLog.Take(10)); // 仅返回前10条
            return Ok($"逻辑装甲升级完成！成功导入 {importedCount} 只基金。\n\n调试详情（供排查）：\n{debugInfo}");
        }

        // ====================== 新增辅助方法 ======================
        private string NormalizeFundName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name
                .Replace(" ", "")
                .Replace("（", "(").Replace("）", ")")
                .Replace("ETF联接", "ETF")
                .Replace("主题", "")
                .Replace("指数型", "")
                .Replace("发起式", "")
                .Replace("证券投资基金", "")
                .Replace("C类", "C").Replace("A类", "A")
                .Trim();
        }

        private string ExtractFundClass(string name)
        {
            if (name.Contains("C") || name.EndsWith("C类")) return "C";
            if (name.Contains("A") || name.EndsWith("A类")) return "A";
            if (name.Contains("QDII")) return "QDII";
            return "";
        }

        private double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;
            s1 = s1.ToUpperInvariant();
            s2 = s2.ToUpperInvariant();

            int common = 0;
            int minLen = Math.Min(s1.Length, s2.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (s1[i] == s2[i]) common++;
            }

            // 最长公共子串加分
            int lcs = LongestCommonSubstringLength(s1, s2);
            double score = (double)common / Math.Max(s1.Length, s2.Length) + (double)lcs / Math.Max(s1.Length, s2.Length) * 0.5;

            return Math.Min(score, 1.0);
        }

        private int LongestCommonSubstringLength(string s1, string s2)
        {
            int[,] dp = new int[s1.Length + 1, s2.Length + 1];
            int maxLen = 0;
            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    if (s1[i - 1] == s2[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                        maxLen = Math.Max(maxLen, dp[i, j]);
                    }
                }
            }
            return maxLen;
        }

        // 👉 添加基金 (绑定用户)
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

        // 👉 删除基金 (核对用户身份)
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

        // 🌙 战术指令：执行夜间清算，滚动市值，开启复利
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
                    // 确保匹配到了基金，并且有对应的涨跌幅数据
                    if (fund != null && i < rateList.Length)
                    {
                        if (double.TryParse(rateList[i], out double rate))
                        {
                            // 💰 核心复利公式：今日收益 = 昨日市值 * (今日涨跌幅 / 100)
                            double todayProfit = fund.HoldAmount * (rate / 100.0);

                            // 📈 滚动市值：新市值 = 昨日市值 + 今日收益
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

        // 🤖 终极自动化：无人值守夜间清算接口
        [HttpGet("auto-settle")]
        public async Task<IActionResult> AutoSettle()
        {
            try
            {
                // 1. 获取全库所有指挥官的所有基金阵地
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("当前暂无基金需要清算。");

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                int successCount = 0;

                foreach (var fund in allFunds)
                {
                    try
                    {
                        // 2. 呼叫东方财富接口，获取该基金今日的最终涨跌幅
                        string url = $"http://fundgz.1234567.com.cn/js/{fund.FundCode}.js?rt={DateTime.Now.Ticks}";
                        string jsData = await client.GetStringAsync(url);

                        // 3. 用正则提取 gszzl (估值涨跌幅)
                        var match = Regex.Match(jsData, @"\""gszzl\"":\""([^\""]+)\""");
                        if (match.Success && double.TryParse(match.Groups[1].Value, out double rate))
                        {
                            // 💰 自动复利推演：今日收益 = 昨日市值 * (涨跌幅 / 100)
                            double todayProfit = fund.HoldAmount * (rate / 100.0);

                            // 📈 滚入本金：新市值 = 老市值 + 今日收益
                            fund.HoldAmount = Math.Round(fund.HoldAmount + todayProfit, 2);
                            successCount++;
                        }
                    }
                    catch
                    {
                        // 如果单只基金网络波动获取失败，忽略并继续下一只，保证系统不崩溃
                        continue;
                    }
                }

                // 4. 一次性保存所有战果
                await _context.SaveChangesAsync();
                return Ok($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动清算执行完毕！共成功滚动更新了 {successCount} 只基金的市值。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动清算引擎异常: {ex.Message}");
            }
        }
        // 👉 获取今日数据 (只返回当前用户的基金！)
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            // 🛡️ 注入吐真剂：捕获一切异常并返回给前端！
            try
            {
                // 💥 紧急战地补丁
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE MyFunds ADD COLUMN LastSettledDate VARCHAR(20);");
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN ActualRate DOUBLE NOT NULL DEFAULT 0;");
                    await _context.Database.ExecuteSqlRawAsync("ALTER TABLE FundRecords ADD COLUMN DiffRate DOUBLE NOT NULL DEFAULT 0;");
                    // 强行增加 CostAmount 字段
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

                    // 💰 挂载核心金融算法
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
                // 🚨 核武级排雷：把内部报错直接变成文字吐在屏幕上！
                return StatusCode(500, $"服务器当场阵亡，死因：{ex.Message} | 详细：{ex.StackTrace}");
            }
        }
        // 👉 指挥官手动补给：更新本金和代码
        [HttpPost("update-details")]
        public async Task<IActionResult> UpdateDetailsAsync([FromQuery] string username, [FromForm] string code, [FromForm] double costAmount, [FromForm] string originalCode)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("指挥官身份未确认");
            if (string.IsNullOrEmpty(code) || costAmount <= 0) return BadRequest("本金或东财代码信息不完整");

            try
            {
                // 💥 战术细节：我们要处理 OCR 导入产生的临时代码，将其替换为 6 位正规代码
                var existFund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && (f.FundCode == code || f.FundCode == originalCode));

                if (existFund != null)
                {
                    // 1. 如果它是ocr产生的不正规代码 (待核对_xxx)，我们要更新基金代码和本金
                    if (originalCode.StartsWith("待核对"))
                    {
                        // 先检查新的东财代码是否已经存在了
                        var checkNewCode = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                        if (checkNewCode != null) return BadRequest($"东财代码 [{code}] 已经在库中，请不要输入重复代码。");

                        existFund.FundCode = code; // 替换代码
                        existFund.CostAmount = costAmount; // 更新本金
                        _context.MyFunds.Update(existFund);

                        // 同时，我们需要清理旧的OCR代码在 FundData 数据表里的数据
                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        // 2. 如果原本就是正规代码，只更新本金
                        existFund.CostAmount = costAmount;
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();
                    return Ok($"本金与 012804 代码补给完成，大屏将自动下周一动算收益！");
                }

                return BadRequest("未能匹配到该基金阵地信息");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"云端接收异常: {ex.Message}");
            }
        }
        // 🚀🚀🚀 【新增的核心武器】：手动核武按钮与战报输出
        [HttpGet("force-settle")]
        public async Task<IActionResult> ForceSettle()
        {
            using var client = new HttpClient();
            // 👇 加装强制熔断器：如果东方财富5秒不理我，直接强行切断，防止拖死服务器！
            client.Timeout = TimeSpan.FromSeconds(5);
         
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");

            var targetFunds = await _context.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            var localTime = DateTime.UtcNow.AddHours(8);
            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            var resultLog = new List<string>();
            resultLog.Add($"===== 科技军团 T+1 手动清算诊断战报 =====");
            resultLog.Add($"当前系统校验时间: {localTime.ToString("yyyy-MM-dd HH:mm:ss")}");

            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await client.GetStringAsync(url);

                    // 查杀 WAF 拦截
                    if (!response.Contains("LSJZList"))
                    {
                        resultLog.Add($"❌ [{code}] 遭防火墙拦截！东方财富返回: {response.Substring(0, Math.Min(response.Length, 60))}...");
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
                            resultLog.Add($"⏳ [{code}] 东方财富接口慢了！今日({todayStr})真实财报还未出，其最新停留在: {fsrq}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultLog.Add($"🔥 [{code}] 发生致命代码报错: {ex.Message}");
                }
            }
            await _context.SaveChangesAsync();
            return Ok(resultLog); // 直接在浏览器打印战报！
        }
    }
}