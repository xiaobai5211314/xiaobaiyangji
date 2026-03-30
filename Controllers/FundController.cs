using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baidu.Aip.Ocr;
using Newtonsoft.Json.Linq;
using 估值助手.Models;

namespace 估值助手.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FundController : ControllerBase
    {
        private readonly AppDbContext _context;
        // 🏆 战术中枢：全网基金数据库缓存 (只在程序启动时抓取一次，不拖慢速度)
        private static List<FundInfoCache> _globalFundCache = null;

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
        }

        // 📡 从东方财富拉取全量 10000+ 基金名单
        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null) return _globalFundCache;

            try
            {
                using var client = new HttpClient();
                // 这是天天基金的隐藏全量接口
                string jsData = await client.GetStringAsync("http://fund.eastmoney.com/js/fundcode_search.js");

                // 返回格式是 var r = [["000001","HXCZHH","华夏成长混合","混合型"...],...];
                int startIndex = jsData.IndexOf('[');
                int endIndex = jsData.LastIndexOf(']');
                if (startIndex > 0 && endIndex > 0)
                {
                    string json = jsData.Substring(startIndex, endIndex - startIndex + 1);
                    var rawList = System.Text.Json.JsonSerializer.Deserialize<List<List<string>>>(json);

                    // 提取 代码[0] 和 名称[2] 存入记忆中枢
                    _globalFundCache = rawList.Select(x => new FundInfoCache { Code = x[0], Name = x[2] }).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("抓取基金总库失败: " + ex.Message);
            }
            return _globalFundCache ?? new List<FundInfoCache>();
        }
        public FundController(AppDbContext context) { _context = context; }


        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("指挥官身份未确认");
            if (imageFile == null || imageFile.Length == 0) return BadRequest("未检测到图像弹药");

            try
            {
                // 1. 直接从内存读取预加载的基金库，速度飞快！
                var fundDb = await GetAllFundsAsync();
                byte[] imageBytes;
                using (var ms = new MemoryStream()) { await imageFile.CopyToAsync(ms); imageBytes = ms.ToArray(); }

                // 🔑 修正后的 AppID
                var APP_ID = "122647395";
                var API_KEY = "yjfCgtNuumSjxc34FDmXCv8e";
                var SECRET_KEY = "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c";

                var client = new Baidu.Aip.Ocr.Ocr(API_KEY, SECRET_KEY) { Timeout = 60000 };
                // 🚀 使用高精度含位置版，这是解决“漏解析”的关键
                var options = new Dictionary<string, object> { { "recognize_granularity", "small" } };
                var result = client.Accurate(imageBytes, options);

                JArray wordsResult = result["words_result"] as JArray;
                if (wordsResult == null) return BadRequest("识别失败");

                // 将 OCR 结果转化为带坐标的“战术目标”
                var allWords = wordsResult.Select(w => new {
                    Text = w["words"].ToString().Trim(),
                    Top = (int)w["location"]["top"],
                    Left = (int)w["location"]["left"]
                }).OrderBy(w => w.Top).ToList();

                int importedCount = 0;
                string numPattern = @"^([-+]?\d{1,3}(,\d{3})*(\.\d{2}))$";

                // 🎯 核心逻辑：以“金额”为锚点，向上寻找“基金名”
                for (int i = 0; i < allWords.Count; i++)
                {
                    var current = allWords[i];

                    // 如果这一行是金额 (如 20,387.49)
                    if (Regex.IsMatch(current.Text, numPattern))
                    {
                        double marketValue = double.Parse(current.Text.Replace(",", ""));
                        if (marketValue < 10) continue; // 过滤掉太小的金额（可能是收益）

                        // 🕵️ 向上回溯 150 像素，寻找匹配全量库的基金名字
                        var matchedFund = fundDb.FirstOrDefault(f =>
                            allWords.Any(w => w.Top < current.Top && w.Top > current.Top - 150 &&
                            (f.Name.Contains(w.Text) || w.Text.Contains(f.Name)) && w.Text.Length >= 4)
                        );

                        if (matchedFund != null)
                        {
                            // 💰 自动推算成本：寻找这一行右侧或下方的“持有收益”
                            // 支付宝排版：金额在左，持有收益在右
                            double holdingIncome = 0;
                            var incomeWord = allWords.FirstOrDefault(w =>
                                Math.Abs(w.Top - current.Top) < 40 && w.Left > current.Left + 50 && Regex.IsMatch(w.Text, numPattern));

                            if (incomeWord != null) holdingIncome = double.Parse(incomeWord.Text.Replace(",", ""));

                            // 💥 终极公式：本金 = 当前市值 - 持有收益
                            double costAmount = marketValue - holdingIncome;

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
                            // 跳过已处理的行，防止重复抓取同一卡片
                            i += 2;
                        }
                    }
                }
                await _context.SaveChangesAsync();
                return Ok($"解析引擎升级成功！已精准导入 {importedCount} 只基金，成本与回本率已自动同步。");
            }
            catch (Exception ex) { return StatusCode(500, $"战线受阻: {ex.Message}"); }
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