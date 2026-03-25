using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        public FundController(AppDbContext context) { _context = context; }

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

            var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
            var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

            // 🚀 【核心修复】：强行注入 UTC+8 亚洲时间！不管服务器在哪，永远对齐北京/新加坡时间
            var localTime = DateTime.UtcNow.AddHours(8);
            var today = localTime.Date;

            // 严格强制使用斜杠，防止 Linux 乱转横杠导致前端解析失败
            string todayStr = localTime.ToString("yyyy'/'MM'/'dd");

            // 抓取今天的盘中数据
            var todayRecords = await _context.FundRecords
                .Where(r => r.FetchTime >= today && myFundCodes.Contains(r.FundCode))
                .OrderBy(r => r.FetchTime)
                .ToListAsync();

            // 抓取昨天的真实净值做“基准点”
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

                // 魔法起点：把昨天的最终结果，强行钉在今天 09:30 作为起跑线
                if (lastRecord != null)
                {
                    dataPoints.Add(new object[] { todayStr + " 09:30:00", lastRecord.EstimatedRate });
                }

                // 加入今天的正常走势
                dataPoints.AddRange(fundRecords.Select(r => new object[] { r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"), r.EstimatedRate }));

                // 如果连历史数据都没有（比如刚加的新基金），塞个 0 防止图表崩盘
                if (dataPoints.Count == 0)
                {
                    dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });
                }

                return new
                {
                    code = config.FundCode,
                    name = config.FundName,
                    amount = config.HoldAmount,
                    data = dataPoints
                };
            });

            return Ok(result);
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