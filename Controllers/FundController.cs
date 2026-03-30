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

            List<string> texts = (result["words_result"] as JArray).Select(x => x["words"].ToString().Trim()).ToList();
            int importedCount = 0;

            string numPattern = @"^([-+]?\d{1,3}(,\d{3})*(\.\d{2}))$";

            for (int i = 0; i < texts.Count; i++)
            {
                // 🛡️ 终极防线 1：强制拼接向下 3 行，解决所有 OCR 断句问题
                string text1 = texts[i];
                string text2 = (i + 1 < texts.Count) ? texts[i + 1] : "";
                string text3 = (i + 2 < texts.Count) ? texts[i + 2] : "";

                string combinedName = text1 + text2 + text3;
                // 清洗掉支付宝的 UI 干扰词
                combinedName = Regex.Replace(combinedName, @"(金选|指数基金|市场解读|已更新)", "");

                // 🛡️ 终极防线 2：汉字门槛，防止短串(如"主题指数C")乱认亲戚
                string pureChinese = Regex.Replace(combinedName, @"[^\u4e00-\u9fa5]", "");
                if (pureChinese.Length < 5) continue; // 名字里连 5 个汉字都没有，说明是碎片，跳过！

                FundInfoCache matchedFund = null;

                // 找出所有名字重合的候选人
                var candidates = fundDb.Where(f => combinedName.Contains(f.Name.Replace("ETF联接", "").Replace("(QDII)", "")) || f.Name.Contains(pureChinese)).ToList();

                if (candidates.Any())
                {
                    // 🛡️ 终极防线 3：A/C 类绝对锁定
                    bool isC = combinedName.EndsWith("C") || combinedName.Contains("C") && !combinedName.Contains("A");
                    bool isA = combinedName.EndsWith("A") || combinedName.Contains("A") && !combinedName.Contains("C");

                    if (isC) candidates = candidates.Where(f => f.Name.EndsWith("C")).ToList();
                    else if (isA) candidates = candidates.Where(f => f.Name.EndsWith("A")).ToList();

                    // 选出名字长度最接近的，彻底击毙“短串匹配长串”的 Bug
                    matchedFund = candidates.OrderBy(f => Math.Abs(f.Name.Length - pureChinese.Length)).FirstOrDefault();
                }

                if (matchedFund != null)
                {
                    double marketValue = 0; double holdingIncome = 0;

                    // 扩大搜索范围，因为名字被拼起来了，数字可能在更下面
                    for (int j = 1; j <= 8 && (i + j) < texts.Count; j++)
                    {
                        string next = texts[i + j].Trim();
                        if (Regex.IsMatch(next, numPattern))
                        {
                            double val = double.Parse(next.Replace(",", ""));
                            // 市值必定是正数且较大，并且没有正负号
                            if (marketValue == 0 && val > 10 && !next.StartsWith("+") && !next.StartsWith("-"))
                            {
                                marketValue = val;
                            }
                            // 收益必定带有符号
                            else if (holdingIncome == 0 && (next.StartsWith("+") || next.StartsWith("-")))
                            {
                                holdingIncome = val; break;
                            }
                        }
                    }

                    if (marketValue > 0)
                    {
                        double costAmount = Math.Round(marketValue - holdingIncome, 2);

                        var exist = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == matchedFund.Code);
                        if (exist != null)
                        {
                            exist.HoldAmount = marketValue; exist.CostAmount = costAmount;
                        }
                        else
                        {
                            _context.MyFunds.Add(new MyFundConfig { Username = username, FundCode = matchedFund.Code, FundName = matchedFund.Name, HoldAmount = marketValue, CostAmount = costAmount });
                        }
                        importedCount++;
                        i += 2; // 战术跳跃，既然已经拼了后面两行并成功了，就直接跳过它们
                    }
                }
            }
            await _context.SaveChangesAsync();
            return Ok($"AI 拼图算法已部署！完美导入并校准了 {importedCount} 只基金。");
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