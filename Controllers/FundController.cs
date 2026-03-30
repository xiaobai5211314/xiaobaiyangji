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
            public string NormalizedName { get; set; } // 预存清洗后的名字用于提速
        }

        private static List<FundInfoCache> _globalFundCache = null;

        public FundController(AppDbContext context) { _context = context; }

        /// <summary>
        /// 获取全量基金库（带 Redis 和内存双重缓存）
        /// </summary>
        private async Task<List<FundInfoCache>> GetAllFundsAsync()
        {
            if (_globalFundCache != null) return _globalFundCache;

            try
            {
                ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost:6379");
                IDatabase db = redis.GetDatabase();
                string cacheKey = "global_fund_db_cache_v2"; 
                var cachedData = await db.StringGetAsync(cacheKey);

                if (!cachedData.IsNull)
                {
                    _globalFundCache = JsonSerializer.Deserialize<List<FundInfoCache>>(cachedData);
                    return _globalFundCache;
                }

                using var client = new HttpClient();
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

                    await db.StringSetAsync(cacheKey, JsonSerializer.Serialize(_globalFundCache), TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Redis 缓存异常: " + ex.Message);
            }

            return _globalFundCache ?? new List<FundInfoCache>();
        }

        /// <summary>
        /// 🚀 高性能 OCR 导入引擎
        /// </summary>
        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未提供指挥官代号");

            // 1. 预装载：全量基金库 + 用户现有持仓（内存字典化）
            var allFunds = await GetAllFundsAsync();
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            byte[] finalProcessedBytes;
            List<string> debugLog = new List<string>();

            // 2. 图片压缩处理
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

            // 3. 调用百度 OCR
            var client = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c") { Timeout = 30000 };
            var result = client.GeneralBasic(finalProcessedBytes);
            var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();

            int importedCount = 0;
            string numPattern = @"^([-+]?\d{1,3}(,\d{3})*(\.\d{2}))$";

            // 4. 核心匹配循环
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

                // 🚀 性能优化：两阶段过滤（先通过关键词包含缩小范围，再进行相似度计算）
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

                // 5. 数据解析与入库
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

                        // 使用内存字典进行更新判断，避免循环内执行 SQL
                        if (userFundDict.TryGetValue(bestMatch.Code, out var exist))
                        {
                            exist.HoldAmount = marketValue;
                            exist.CostAmount = costAmount;
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
            return Ok($"识别完成！成功同步 {importedCount} 只基金。\n\n详细日志：\n{string.Join("\n", debugLog)}");
        }

        // ================================= 辅助核心算法 =================================

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

        // ================================= 业务逻辑接口 =================================

        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            try
            {
                // 确保数据库字段存在（仅针对 SQLite/调试环境补齐）
                try { await _context.Database.ExecuteSqlRawAsync("ALTER TABLE MyFunds ADD COLUMN CostAmount DOUBLE NOT NULL DEFAULT 0;"); } catch { }

                // 核心修复：确保使用 EF Core 的异步扩展方法
                var myFunds = await _context.MyFunds.Where(f => f.Username == username).ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                var localTime = DateTime.UtcNow.AddHours(8);
                var today = localTime.Date;
                string todayStr = localTime.ToString("yyyy/MM/dd");

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

                    if (lastRecord != null) dataPoints.Add(new object[] { todayStr + " 09:30:00", lastRecord.EstimatedRate });
                    dataPoints.AddRange(fundRecords.Select(r => new object[] { r.FetchTime.ToString("yyyy/MM/dd HH:mm:ss"), r.EstimatedRate }));
                    if (dataPoints.Count == 0) dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });

                    double cost = config.CostAmount;
                    double currentAmount = config.HoldAmount;
                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = currentAmount,
                        cost = cost > 0 ? cost : (double?)null,
                        existingReturnRate = cost > 0 ? Math.Round(((currentAmount - cost) / cost) * 100.0, 2) : 0,
                        breakEvenRate = cost > 0 ? Math.Round((currentAmount / cost) * 100.0, 2) : 0,
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        data = dataPoints
                    };
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"获取数据失败: {ex.Message}");
            }
        }

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

        [HttpGet("auto-settle")]
        public async Task<IActionResult> AutoSettle()
        {
            try
            {
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("当前暂无基金。");
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
                    } catch { continue; }
                }
                await _context.SaveChangesAsync();
                return Ok($"[{DateTime.Now}] 自动清算完毕，更新 {successCount} 只。");
            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("force-settle")]
        public async Task<IActionResult> ForceSettle()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var targetFunds = await _context.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            var localTime = DateTime.UtcNow.AddHours(8);
            string todayStr = localTime.ToString("yyyy-MM-dd");
            var resultLog = new List<string> { $"战报时间: {localTime}" };

            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    client.DefaultRequestHeaders.Referer = new Uri("http://fund.eastmoney.com/");
                    string response = await client.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(response);
                    var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");
                    if (dataArray.GetArrayLength() > 0)
                    {
                        var latest = dataArray[0];
                        if (latest.GetProperty("FSRQ").GetString() == todayStr && double.TryParse(latest.GetProperty("JZZZL").GetString(), out double actualRate))
                        {
                            var record = await _context.FundRecords
                                .Where(r => r.FundCode == code && r.FetchTime >= localTime.Date)
                                .OrderByDescending(r => r.FetchTime).FirstOrDefaultAsync();
                            if (record != null) { record.EstimatedRate = actualRate; resultLog.Add($"✅ [{code}] 覆盖成功: {actualRate}%"); }
                        }
                    }
                } catch { continue; }
            }
            await _context.SaveChangesAsync();
            return Ok(resultLog);
        }
    }
}
