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

        // 基金基础信息缓存结构
        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; } // 预存清洗后的名字用于匹配加速
        }

        // 静态内存缓存，防止每次请求都去撞 Redis 或下载 JS
        private static List<FundInfoCache> _globalFundCache = null;

        public FundController(AppDbContext context) { _context = context; }

        /// <summary>
        /// 核心：获取全量基金库（内存 + Redis + 远程下载三级缓存）
        /// </summary>
        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null) return _globalFundCache;

            try
            {
                // 1. 尝试从 Redis 获取
                ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost:6379");
                IDatabase db = redis.GetDatabase();
                string cacheKey = "global_fund_db_cache_v2"; 
                var cachedData = await db.StringGetAsync(cacheKey);

                if (!cachedData.IsNull)
                {
                    _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                    Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} 成功从 Redis 加载了 {_globalFundCache.Count} 条基金数据");
                    return _globalFundCache;
                }

                // 2. Redis 没有，则从东财下载（设置 10 秒超时防止卡死整个请求）
                Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} Redis 无数据，开始下载全量基金列表...");
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

                    // 写入 Redis，有效期 24 小时
                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(_globalFundCache), TimeSpan.FromHours(24));
                    Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} 下载并解析完毕，共 {_globalFundCache.Count} 条数据");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} 加载基金库失败: {ex.Message}");
            }

            return _globalFundCache ?? new List<FundInfoCache>();
        }

        /// <summary>
        /// 🚀 核心导入功能
        /// </summary>
        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");
            
            Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} 收到导入请求，用户: {username}");

            // 1. 预装载基金库（这是最容易卡住的地方）
            var allFunds = await GetAllFundsAsync();
            
            // 2. 预查用户现有持仓，避免循环内查询数据库
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            byte[] finalProcessedBytes;
            List<string> debugLog = new List<string>();

            // 3. 图片压缩处理
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

            // 4. 调用 OCR
            Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} 正在调用百度 OCR...");
            var client = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c") { Timeout = 30000 };
            var result = client.GeneralBasic(finalProcessedBytes);
            var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
            Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} OCR 识别成功，获得 {texts.Count} 行文本");

            int importedCount = 0;
            string numPattern = @"^([-+]?\d{1,3}(,\d{3})*(\.\d{2}))$";

            // 5. 匹配循环
            for (int i = 0; i < texts.Count; i++)
            {
                string combinedName = texts[i];
                if (i + 1 < texts.Count) combinedName += texts[i + 1];

                string pureChinese = Regex.Replace(combinedName, @"[^\u4e00-\u9fa5]", "");
                if (pureChinese.Length < 3) continue; 

                string ocrClass = ExtractFundClass(combinedName);
                string normalizedOcr = NormalizeFundName(combinedName);

                FundInfoCache bestMatch = null;
                double bestScore = 0;

                // 🚀 高性能过滤：先通过简单包含筛选候选者，再计算相似度
                var candidates = allFunds.Where(f => f.NormalizedName.Contains(pureChinese) || 
                                                    pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));

                foreach (var f in candidates)
                {
                    string dbClass = ExtractFundClass(f.Name);
                    if (!string.IsNullOrEmpty(ocrClass) && !string.IsNullOrEmpty(dbClass) && ocrClass != dbClass) continue;

                    double similarity = CalculateSimilarity(normalizedOcr, f.NormalizedName);
                    double currentScore = similarity * 100;

                    if (currentScore > bestScore)
                    {
                        bestScore = currentScore;
                        bestMatch = f;
                    }
                }

                // 6. 解析数据点
                if (bestMatch != null && bestScore > 70)
                {
                    double marketValue = 0, holdingIncome = 0;
                    int linesConsumed = 0;

                    for (int j = 1; j <= 8 && (i + j) < texts.Count; j++)
                    {
                        string next = texts[i + j].Trim();
                        if (Regex.IsMatch(next, numPattern))
                        {
                            double val = double.Parse(next.Replace(",", ""));
                            if (marketValue == 0 && val > 10 && !next.Contains("+") && !next.Contains("-"))
                            {
                                marketValue = val;
                                linesConsumed = Math.Max(linesConsumed, j);
                            }
                            else if (holdingIncome == 0 && (next.Contains("+") || next.Contains("-")))
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
                        debugLog.Add($"✅ {bestMatch.Name} ({bestScore:F1}%)");
                    }
                }
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"[LOG] {DateTime.Now:HH:mm:ss} 导入流程结束，共入库 {importedCount} 只");
            return Ok($"识别完成！成功同步 {importedCount} 只基金。\n\n详细日志：\n{string.Join("\n", debugLog)}");
        }

        // ================================= 核心辅助方法 =================================

        private string ExtractFundClass(string name)
        {
            if (Regex.IsMatch(name, @"C类?$|\(C\)$|（C）$", RegexOptions.IgnoreCase)) return "C";
            if (Regex.IsMatch(name, @"A类?$|\(A\)$|（A）$", RegexOptions.IgnoreCase)) return "A";
            return "";
        }

        private static string NormalizeFundName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return name.Replace(" ", "").Replace("（", "(").Replace("）", ")")
                       .Replace("ETF联接", "ETF").Replace("证券投资基金", "")
                       .Replace("混合", "").Replace("指数", "").Replace("主题", "").Trim();
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

        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供代号");
            try
            {
                var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                var localTime = DateTime.UtcNow.AddHours(8);
                var today = localTime.Date;

                var todayRecords = await _context.FundRecords
                    .Where(r => r.FetchTime >= today && myFundCodes.Contains(r.FundCode))
                    .OrderBy(r => r.FetchTime).ToListAsync();

                var lastRecords = new List<FundData>();
                foreach (var code in myFundCodes)
                {
                    var lr = await _context.FundRecords.Where(r => r.FundCode == code && r.FetchTime < today)
                        .OrderByDescending(r => r.FetchTime).FirstOrDefaultAsync();
                    if (lr != null) lastRecords.Add(lr);
                }

                var result = myFunds.Select(config => {
                    var fundRecords = todayRecords.Where(r => r.FundCode == config.FundCode).ToList();
                    var lastRecord = lastRecords.FirstOrDefault(r => r.FundCode == config.FundCode);
                    var dataPoints = fundRecords.Select(r => new object[] { r.FetchTime.ToString("yyyy/MM/dd HH:mm:ss"), r.EstimatedRate }).ToList();

                    double cost = config.CostAmount;
                    double currentAmount = config.HoldAmount;
                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = currentAmount,
                        cost = cost > 0 ? cost : (double?)null,
                        existingReturnRate = cost > 0 ? Math.Round(((currentAmount - cost) / cost) * 100.0, 2) : 0,
                        data = dataPoints
                    };
                });
                return Ok(result);
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
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
            return BadRequest("未找到基金");
        }
    }
}
