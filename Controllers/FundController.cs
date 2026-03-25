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

                    // 检查该用户是否已添加此基金
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

            // 1.查出当前用户自己的专属持仓
            var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
            var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

            // 2. 只从总历史记录里， 提取该用户持有的基金数据
            var today = DateTime.Today;
            var records = await _context.FundRecords
                .Where(r => r.FetchTime >= today && myFundCodes.Contains(r.FundCode))
                .OrderBy(r => r.FetchTime)
                .ToListAsync();

            var result = records.GroupBy(r => r.FundCode).Select(g => {
                var config = myFunds.First(f => f.FundCode == g.Key);
                return new
                {
                    code = g.Key,
                    name = config.FundName,
                    amount = config.HoldAmount,
                    data = g.Select(r => new object[] { r.FetchTime.ToString("yyyy/MM/dd HH:mm:ss"), r.EstimatedRate }).ToList()
                };
            });

            return Ok(result);
        }
    }
}