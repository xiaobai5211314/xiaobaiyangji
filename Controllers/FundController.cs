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
        public FundController(AppDbContext context) { _context = context; }

        // 🏆 OCR 坐标雷达辅助类 (放在 Controller 内部)
        public class OcrWordInfo
        {
            public string Words { get; set; }
            public int Top { get; set; }  // Y轴坐标
            public int Left { get; set; } // X轴坐标
            public int Width { get; set; }
        }

        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("指挥官身份未确认");
            if (imageFile == null || imageFile.Length == 0) return BadRequest("未检测到图像弹药");

            try
            {
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    await imageFile.CopyToAsync(ms);
                    imageBytes = ms.ToArray();
                }

                // 🔑 你的专属发射密钥已就位
                var APP_ID = "122647395";
                var API_KEY = "yjfCgtNuumSjxc34FDmXCv8e";
                var SECRET_KEY = "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c";

                var client = new Ocr(API_KEY, SECRET_KEY);
                client.Timeout = 60000;

                // 💥 启动含位置版的高精度识别！
                var options = new Dictionary<string, object> { { "recognize_granularity", "small" } };
                var result = client.Accurate(imageBytes, options);

                JArray wordsResult = result["words_result"] as JArray;
                if (wordsResult == null || wordsResult.Count == 0)
                    return BadRequest("图像中未识别到有效文字。");

                // 将返回结果转化为带坐标的雷达目标
                var allWords = new List<OcrWordInfo>();
                foreach (var wordObj in wordsResult)
                {
                    var location = wordObj["location"];
                    allWords.Add(new OcrWordInfo
                    {
                        Words = wordObj["words"].ToString().Trim(),
                        Top = (int)location["top"],
                        Left = (int)location["left"],
                        Width = (int)location["width"]
                    });
                }

                int successCount = 0;
                var ocrFoundFunds = new List<Tuple<string, double>>();

                // 匹配金额的正则：带有逗号和两位小数的数字 (如 20,387.49 或 0.50)
                string amountPattern = @"^(\d{1,3}(,\d{3})*(\.\d{2}))$";
                // 匹配基金名：至少包含中文，通常以字母或特定词汇结尾
                string namePattern = @"([\u4e00-\u9fa5]+[A-Za-z0-9]*(联接|混合|指数|股票|债券|A|C|E|F|LOF|ETF|QDII|科技)+[A-Za-z]*)";

                // 按 Y 轴从上到下排序目标
                var sortedWords = allWords.OrderBy(w => w.Top).ToList();

                for (int i = 0; i < sortedWords.Count; i++)
                {
                    var currentWord = sortedWords[i];

                    // 如果锁定到基金名称
                    if (Regex.IsMatch(currentWord.Words, namePattern) && currentWord.Words.Length > 4)
                    {
                        string fundName = currentWord.Words;

                        // 🎯 战术核心：在同一行（Y轴差距<30像素），寻找在它右边（X轴更大）的金额数字
                        var sameRowWords = sortedWords.Where(w =>
                                Math.Abs(w.Top - currentWord.Top) < 30 &&
                                w.Left > currentWord.Left)
                            .OrderBy(w => w.Left).ToList();

                        var amountWord = sameRowWords.FirstOrDefault(w => Regex.IsMatch(w.Words, amountPattern));

                        // 防御机制：如果同一行没找到，可能排版歪了，向下多看 2 行
                        if (amountWord == null)
                        {
                            for (int k = 1; k <= 2 && (i + k) < sortedWords.Count; k++)
                            {
                                if (Regex.IsMatch(sortedWords[i + k].Words, amountPattern))
                                {
                                    amountWord = sortedWords[i + k];
                                    break;
                                }
                            }
                        }

                        // 洗出纯净数据
                        if (amountWord != null)
                        {
                            double holdAmount = double.Parse(amountWord.Words.Replace(",", ""));
                            ocrFoundFunds.Add(new Tuple<string, double>(fundName, holdAmount));
                            successCount++;
                        }
                    }
                }

                // 将清洗出的战利品录入数据库
                if (ocrFoundFunds.Count > 0)
                {
                    var existFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();

                    foreach (var found in ocrFoundFunds)
                    {
                        string fundName = found.Item1;
                        double holdAmount = found.Item2;

                        var existMatch = existFunds.FirstOrDefault(f => f.FundName.Contains(fundName) || fundName.Contains(f.FundName));
                        if (existMatch != null)
                        {
                            existMatch.HoldAmount = holdAmount;
                        }
                        else
                        {
                            _context.MyFunds.Add(new MyFundConfig
                            {
                                Username = username,
                                FundCode = "未定_" + DateTime.Now.Ticks.ToString().Substring(12),
                                FundName = fundName,
                                HoldAmount = holdAmount,
                                CostAmount = 0 // 注意：这里依然是 0，算收益率还需要你去数据库补齐本金！
                            });
                        }
                    }
                    await _context.SaveChangesAsync();
                    return Ok($"云端坐标雷达解析完毕！精准匹配了 {ocrFoundFunds.Count} 只基金。");
                }

                return BadRequest("未能从图像中识别出基金与金额的对应关系。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"云端连接异常: {ex.Message} | {ex.StackTrace}");
            }
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