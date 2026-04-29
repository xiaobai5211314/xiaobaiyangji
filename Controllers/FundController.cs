using Baidu.Aip.Ocr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using 估值助手.Models;
using Color = SixLabors.ImageSharp.Color;
using Image = SixLabors.ImageSharp.Image;
using Microsoft.Extensions.Caching.Memory;

namespace 估值助手.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FundController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;

        static FundController()
        {
            System.Net.WebRequest.DefaultWebProxy = null;
        }

        private static readonly Baidu.Aip.Ocr.Ocr _baiduOcrClient = new Baidu.Aip.Ocr.Ocr("yjfCgtNuumSjxc34FDmXCv8e", "g3XGcMKX0Qsp4k4wDSbxYQoSdFPuDt0c")
        {
            Timeout = 10000
        };

        public class FundInfoCache
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string NormalizedName { get; set; }
        }

        private static List<FundInfoCache> _globalFundCache = null;
        private static Dictionary<string, FundInfoCache> _exactMatchDict = null;

        public FundController(AppDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        private static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        private static string ChinaDateDash(DateTime? localTime = null)
            => (localTime ?? ChinaNow()).ToString("yyyy-MM-dd");

        // 同一天买入/卖出是“在途交易”，不能直接参与当日收益率分母。
        // LastAddAmount > 0：当天加仓，收益基数要剥离这笔未确认资金。
        // LastAddAmount < 0：当天减仓，收益基数要把卖出份额当天仍应承担的涨跌补回。
        private static double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            double baseAmount = fund.HoldAmount;
            if (fund.LastTradeDate == settleDate)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        private static double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
        {
            return fund.LastTradeDate == settleDate ? fund.LastAddAmount : 0;
        }

        private static double GetEffectiveShares(MyFundConfig fund, string settleDate)
        {
            if (fund.HoldShares <= 0) return 0;

            double baseAmount = GetEffectiveBaseAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0 && fund.HoldAmount > 0)
            {
                return Math.Max(0, fund.HoldShares * (baseAmount / fund.HoldAmount));
            }

            return fund.HoldShares;
        }

        private sealed class OfficialNavSnapshot
        {
            public string Date { get; set; } = string.Empty;
            public double Rate { get; set; }
            public double? TodayNav { get; set; }
            public double? YesterdayNav { get; set; }
            public double? NavDiff { get; set; }
            public string Source { get; set; } = string.Empty;
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            if (!element.TryGetProperty(propertyName, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.Number) return prop.TryGetDouble(out value);
            if (prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (string.IsNullOrWhiteSpace(text) || text == "--") return false;
                return double.TryParse(text, out value);
            }
            return false;
        }

        private static OfficialNavSnapshot? TryBuildOfficialNavSnapshot(JsonElement dataArray, string settleDate)
        {
            if (dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() == 0) return null;

            var latest = dataArray[0];
            string fsrq = latest.TryGetProperty("FSRQ", out var fsrqElement) ? (fsrqElement.GetString() ?? string.Empty) : string.Empty;
            if (fsrq != settleDate) return null;

            double? apiRate = TryGetDouble(latest, "JZZZL", out var parsedRate) ? parsedRate : null;
            double? navRate = null;
            double? todayNav = null;
            double? yesterdayNav = null;
            double? navDiff = null;

            if (dataArray.GetArrayLength() > 1 &&
                TryGetDouble(latest, "DWJZ", out var navToday) &&
                TryGetDouble(dataArray[1], "DWJZ", out var navYesterday) &&
                navYesterday > 0)
            {
                todayNav = navToday;
                yesterdayNav = navYesterday;
                navDiff = navToday - navYesterday;
                navRate = Math.Round(navDiff.Value / navYesterday * 100.0, 4);
            }

            double? finalRate = null;
            string source = string.Empty;

            if (navRate.HasValue && (!apiRate.HasValue ||
                                     (Math.Abs(apiRate.Value) < 0.000001 && Math.Abs(navRate.Value) > 0.000001) ||
                                     Math.Abs(apiRate.Value - navRate.Value) > 0.05))
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }
            else if (apiRate.HasValue)
            {
                finalRate = apiRate.Value;
                source = "JZZZL";
            }
            else if (navRate.HasValue)
            {
                finalRate = navRate.Value;
                source = "DWJZ反算";
            }

            if (!finalRate.HasValue) return null;
            if (Math.Abs(finalRate.Value) < 0.000001 && !navRate.HasValue) return null;

            return new OfficialNavSnapshot
            {
                Date = fsrq,
                Rate = Math.Round(finalRate.Value, 4),
                TodayNav = todayNav,
                YesterdayNav = yesterdayNav,
                NavDiff = navDiff,
                Source = source
            };
        }

        private async Task<OfficialNavSnapshot?> FetchOfficialNavSnapshotAsync(HttpClient client, string fundCode, string settleDate)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=2&_={timestamp}";
            string res = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(res);
            if (!doc.RootElement.TryGetProperty("Data", out var data) ||
                !data.TryGetProperty("LSJZList", out var dataArray))
            {
                return null;
            }

            return TryBuildOfficialNavSnapshot(dataArray, settleDate);
        }

        private static bool ApplyOneDaySettlement(MyFundConfig fund, double actualRate, string settleDate, double? exactProfit = null)
        {
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            double pending = GetPendingTradeAmount(fund, settleDate);
            double settledProfit = Math.Round(exactProfit ?? (baseAmount * (actualRate / 100.0)), 2);
            double newHoldAmount = Math.Round(baseAmount + settledProfit + pending, 2);

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.HoldAmount - newHoldAmount) > 0.01;

            if (!changed) return false;

            fund.HoldAmount = newHoldAmount;
            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);
            return true;
        }

        private void ClearTodayCache(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            _cache.Remove($"Tactical_TodayData_{username}");
            _cache.Remove($"Tactical_TodayData_{username}_{ChinaDateDash()}");
        }


        private static void CopyArchiveValues(DailyArchive target, DailyArchive source)
        {
            target.FundName = string.IsNullOrWhiteSpace(source.FundName) ? target.FundName : source.FundName;
            target.Assets = Math.Round(source.Assets, 2);
            target.DailyProfit = Math.Round(source.DailyProfit, 2);
            target.DailyRate = Math.Round(source.DailyRate, 2);
            target.TotalProfit = Math.Round(source.TotalProfit, 2);
            target.TotalRate = Math.Round(source.TotalRate, 2);
        }

        private async Task UpsertDailyArchivesAsync(string username, DateTime date, IEnumerable<DailyArchive> incoming)
        {
            var normalizedIncoming = incoming
                .Where(x => !string.IsNullOrWhiteSpace(x.FundCode))
                .GroupBy(x => x.FundCode)
                .Select(g => g.Last())
                .ToList();

            var codes = normalizedIncoming.Select(x => x.FundCode).ToList();
            var existing = await _context.DailyArchives
                .Where(a => a.Username == username && a.RecordDate == date && codes.Contains(a.FundCode))
                .ToListAsync();

            var duplicateGroups = existing.GroupBy(x => x.FundCode).Where(g => g.Count() > 1).ToList();
            foreach (var group in duplicateGroups)
            {
                var keep = group.OrderByDescending(x => x.Id).First();
                var remove = group.Where(x => x.Id != keep.Id).ToList();
                if (remove.Count > 0) _context.DailyArchives.RemoveRange(remove);
            }

            var existingDict = existing
                .GroupBy(x => x.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

            foreach (var item in normalizedIncoming)
            {
                item.Username = username;
                item.RecordDate = date;

                if (existingDict.TryGetValue(item.FundCode, out var old))
                {
                    CopyArchiveValues(old, item);
                    _context.DailyArchives.Update(old);
                }
                else
                {
                    _context.DailyArchives.Add(item);
                }
            }
        }


        private static double GetDailyBaseAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetPendingTradeAmount(fund, settleDate);

            // 已完成真实净值清算时，HoldAmount 已经包含当日收益。
            // 今日收益率分母必须回到清算前有效基数，否则总收益率会被“已结算后的市值”稀释。
            if (fund.LastSettledDate == settleDate)
            {
                return Math.Max(0, Math.Round(fund.HoldAmount - pending - fund.LastSettledProfit, 4));
            }

            return GetEffectiveBaseAmount(fund, settleDate);
        }

        private static double GetRecordRateForToday(FundData? record)
        {
            if (record == null) return 0;
            return Math.Abs(record.ActualRate) > 0.000001 ? record.ActualRate : record.EstimatedRate;
        }

        private static List<DailyArchive> BuildArchiveRowsFromCurrentHoldings(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            string dateDash = date.ToString("yyyy-MM-dd");
            var latestRecordDict = todayRecords
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0;
            double totalRealized = 0;
            double totalDailyProfit = 0;
            double totalDailyBase = 0;
            double totalCurrentAssets = 0;

            foreach (var fund in funds)
            {
                latestRecordDict.TryGetValue(fund.FundCode, out var record);

                double cost = fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount;
                double baseAmount = GetDailyBaseAmount(fund, dateDash);
                double dailyRate = fund.LastSettledDate == dateDash ? fund.LastSettledRate : GetRecordRateForToday(record);
                double dailyProfit = fund.LastSettledDate == dateDash
                    ? fund.LastSettledProfit
                    : Math.Round(baseAmount * (dailyRate / 100.0), 2);

                double currentAssets = fund.LastSettledDate == dateDash
                    ? fund.HoldAmount
                    : Math.Round(fund.HoldAmount + dailyProfit, 2);

                double totalProfit = currentAssets - cost + fund.RealizedProfit;
                double totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;

                rows.Add(new DailyArchive
                {
                    Username = username,
                    FundCode = fund.FundCode,
                    FundName = fund.FundName,
                    RecordDate = date,
                    Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2),
                    DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalRate = Math.Round(totalRate, 2)
                });

                totalCost += cost;
                totalRealized += fund.RealizedProfit;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
            }

            double totalDailyRate = totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0;
            double totalCampProfit = totalCurrentAssets - totalCost + totalRealized;
            double totalCampRate = totalCost > 0 ? totalCampProfit / totalCost * 100.0 : 0;

            rows.Add(new DailyArchive
            {
                Username = username,
                FundCode = "TOTAL",
                FundName = "总持仓",
                RecordDate = date,
                Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyRate, 2),
                TotalProfit = Math.Round(totalCampProfit, 2),
                TotalRate = Math.Round(totalCampRate, 2)
            });

            return rows;
        }

        private async Task UpsertTodayArchivesFromCurrentHoldingsAsync(string username, DateTime today, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            if (funds.Count == 0) return;
            var rows = BuildArchiveRowsFromCurrentHoldings(username, today, funds, todayRecords);
            await UpsertDailyArchivesAsync(username, today, rows);
        }

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
                    catch { }

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

        // 🚀 核心组件：基于历史净值与收益的「四维时空碰撞份额推演」
        private async Task<double> DeduceSharesFromHistoryAsync(string fundCode, double holdAmount, double yesterdayIncome)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=15";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                // 优先尝试使用昨日收益进行历史碰撞定位
                if (yesterdayIncome != 0)
                {
                    for (int i = 0; i < dataArray.GetArrayLength() - 1; i++)
                    {
                        if (double.TryParse(dataArray[i].GetProperty("DWJZ").GetString(), out double navToday) &&
                            double.TryParse(dataArray[i + 1].GetProperty("DWJZ").GetString(), out double navYest))
                        {
                            double diff = navToday - navYest;
                            if (diff == 0) continue;

                            double testShares = holdAmount / navToday;
                            double testYi = testShares * diff;

                            // 容错率设定为 1.5 元（化解四舍五入与尾差）
                            if (Math.Abs(testYi - yesterdayIncome) <= 1.5)
                            {
                                return Math.Round(testShares, 2);
                            }
                        }
                    }
                }

                // 如果没找到收益或碰撞失败，保底直接用最新净值折算
                if (double.TryParse(dataArray[0].GetProperty("DWJZ").GetString(), out double latestNav) && latestNav > 0)
                {
                    return Math.Round(holdAmount / latestNav, 2);
                }
            }
            catch { }
            return 0;
        }

        [HttpPost("import-ocr")]
        public async Task<IActionResult> ImportOcrFunds([FromQuery] string username, IFormFile imageFile)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            var allFunds = await GetAllFundsAsync();
            var userFundDict = await _context.MyFunds
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.FundCode);

            // 🚀 终极防断网装甲：将用户已有的基金强行注入比对库！
            // 哪怕全网接口被封死，只要是买过的基金，相似度匹配绝对不会返回0！
            var robustFundPool = allFunds?.ToList() ?? new List<FundInfoCache>();
            foreach (var uf in userFundDict.Values)
            {
                if (!robustFundPool.Any(c => c.Code == uf.FundCode))
                {
                    robustFundPool.Add(new FundInfoCache { Code = uf.FundCode, Name = uf.FundName, NormalizedName = NormalizeFundName(uf.FundName) });
                }
            }

            byte[] finalProcessedBytes = null;
            List<string> debugLog = new List<string>();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
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

                var ocrTask = Task.Run(() => _baiduOcrClient.AccurateBasic(finalProcessedBytes));
                if (await Task.WhenAny(ocrTask, Task.Delay(15000)) != ocrTask) return StatusCode(500, $"❌ 识别超时熔断！");

                var result = await ocrTask;
                debugLog.Add($"⏱️ 百度 OCR 耗时: {watch.ElapsedMilliseconds} ms");

                var texts = (result["words_result"] as JArray)?.Select(x => x["words"].ToString().Trim()).ToList() ?? new List<string>();
                if (texts.Count == 0) return BadRequest("❌ OCR未能识别出任何文字");

                int importedCount = 0;
                string amountPattern = @"^\d[\d,]*\.\d{2}$";
                debugLog.Add($"👁️ 视图检测: 智能混合视图 (搭载逆向推演与防污染系统)");

                for (int i = 1; i < texts.Count; i++)
                {
                    string currentLine = texts[i].Trim();
                    if (!Regex.IsMatch(currentLine, amountPattern)) continue;

                    string namePart1 = "";
                    for (int k = i - 1; k >= 0; k--)
                    {
                        string prev = texts[k].Trim();
                        if (string.IsNullOrWhiteSpace(prev) || prev.Contains("收益") || prev.Contains("金额") || prev.Contains("份额") || prev.Contains("金选") || prev.Contains("市场解读") || prev.Contains("基金经理") || prev.Contains("阶段") || prev.Contains("趋势") || prev.Contains("去看看") || Regex.IsMatch(prev, @"^[-\d\.,%+]+$"))
                            continue;
                        namePart1 = prev;
                        break;
                    }
                    if (string.IsNullOrEmpty(namePart1)) continue;

                    double holdAmount = double.Parse(currentLine.Replace(",", ""));
                    double holdingIncome = 0;
                    double yesterdayIncome = 0;
                    double holdingRate = 0;
                    double holdShares = 0;
                    string potentialFragment = "";
                    List<double> signedNumbers = new List<double>();

                    for (int j = 1; j <= 6 && (i + j) < texts.Count; j++)
                    {
                        string nextLine = texts[i + j].Trim();
                        if (Regex.IsMatch(nextLine, @"[\u4e00-\u9fa5]{4,}") && !nextLine.Contains("金选") && !nextLine.Contains("市场解读") && !nextLine.Contains("基金经理") && !nextLine.Contains("阶段") && !nextLine.Contains("趋势") && !nextLine.Contains("去看看") && !nextLine.Contains("更新")) break;

                        // 🚀 终极防排版污染：地毯式搜索百分比，无视同行其他字符
                        var pctMatches = Regex.Matches(nextLine, @"([-+]?\d+[\.,]\d{2})\s*%");
                        foreach (Match m in pctMatches)
                        {
                            holdingRate = double.Parse(m.Groups[1].Value.Replace(",", "."));
                        }

                        // 🚀 核心破解：全面捕获所有金额（包括丢失符号的残疾数字）
                        var numMatches = Regex.Matches(nextLine, @"([-+]?\d[\d,]*\.\d{2})(?!\s*%)");
                        foreach (Match m in numMatches)
                        {
                            string valStr = m.Groups[1].Value.Replace(",", "");
                            double val = double.Parse(valStr);

                            if (valStr.StartsWith("+") || valStr.StartsWith("-"))
                            {
                                // 带有明确符号的，绝对信任
                                if (!signedNumbers.Contains(val)) signedNumbers.Add(val);
                            }
                            else
                            {
                                // 🚨 OCR 搞丢了弱鸡的绿色减号！我们把数字的【正、负】双重形态同时扔进候选池，让数学模型去严惩它！
                                if (val != 0)
                                {
                                    if (!signedNumbers.Contains(val)) signedNumbers.Add(val);
                                    if (!signedNumbers.Contains(-val)) signedNumbers.Add(-val);
                                }
                            }
                        }


                        if (holdShares == 0 && Regex.IsMatch(nextLine, amountPattern))
                        {
                            string contextText = string.Join("", texts.Skip(Math.Max(0, i - 2)).Take(j + 3));
                            if (contextText.Contains("份额"))
                            {
                                double candidate = double.Parse(nextLine.Replace(",", ""));
                                if (Math.Abs(candidate - holdAmount) > 10) holdShares = candidate;
                            }
                        }
                        else if (string.IsNullOrEmpty(potentialFragment) && !Regex.IsMatch(nextLine, @"^[-\d\.,%+]+$") && !nextLine.Contains("金选") && !nextLine.Contains("市场解读") && !nextLine.Contains("更新")) { potentialFragment = nextLine; }
                    }

                    // 🚀 终极优化版：智能模糊匹配与最小偏差校验
                    if (holdingRate != 0 && signedNumbers.Count >= 1)
                    {
                        double bestDiff = double.MaxValue;
                        double bestIncome = 0;

                        foreach (var num in signedNumbers)
                        {
                            double testCost = holdAmount - num;
                            if (testCost <= 0) continue;

                            // 用提取的盈亏反推纯数学收益率
                            double testRate = (num / testCost) * 100;

                            // 计算与支付宝表面收益率的偏差绝对值
                            double diff = Math.Abs(testRate - holdingRate);

                            // 寻找误差最小的那一个
                            if (diff < bestDiff)
                            {
                                bestDiff = diff;
                                bestIncome = num;
                            }
                        }

                        // 只要偏差在合理范围内（放宽至 8%，足以覆盖因历史减仓/定投造成的巨大摊薄误差），就精准锁定！
                        // 华富的 -3.68% 距离 -4.08% 只有 0.4 的偏差，完美命中！
                        if (bestDiff < 8.0)
                        {
                            holdingIncome = bestIncome;
                            yesterdayIncome = signedNumbers.FirstOrDefault(n => Math.Abs(n) != Math.Abs(bestIncome));

                        }
                    }

                    // 🛡️ 兜底防御体系：如果偏差离谱到超过 8%，利用金融常识进行最后锁定
                    if (holdingIncome == 0 && signedNumbers.Count > 0)
                    {
                        // 策略A：正负号一致性法则。收益率是负的（-4.08%），累计盈亏必定是负数！
                        if (holdingRate != 0)
                        {
                            var matchBySign = signedNumbers.Where(n => (n > 0 && holdingRate > 0) || (n < 0 && holdingRate < 0)).ToList();
                            if (matchBySign.Count == 1)
                            {
                                holdingIncome = matchBySign.First();
                                yesterdayIncome = signedNumbers.FirstOrDefault(n => n != holdingIncome);
                            }
                        }

                        // 策略B：绝对值碾压法则。90% 的情况下，累计总盈亏的数值大于单日盈亏
                        if (holdingIncome == 0)
                        {
                            holdingIncome = signedNumbers.OrderByDescending(n => Math.Abs(n)).First();
                            if (signedNumbers.Count > 1) yesterdayIncome = signedNumbers.FirstOrDefault(n => n != holdingIncome);
                        }
                    }

                    string[] testNames = string.IsNullOrEmpty(potentialFragment) ? new[] { namePart1 } : new[] { namePart1, namePart1 + potentialFragment };
                    FundInfoCache finalBestMatch = null;
                    double finalBestScore = 0;

                    foreach (var testName in testNames)
                    {
                        string pureChinese = Regex.Replace(testName, @"[^\u4e00-\u9fa5]", "");
                        if (pureChinese.Length < 2) continue;

                        string normalizedOcr = NormalizeFundName(testName);
                        FundInfoCache bestMatch = null;
                        double bestScore = 0;

                        if (_exactMatchDict != null && (_exactMatchDict.TryGetValue(normalizedOcr, out var exactFund) || _exactMatchDict.TryGetValue(testName, out exactFund)))
                        {
                            bestMatch = exactFund; bestScore = 100.0;
                        }
                        else
                        {
                            // 使用带有本地防断网基因的 robustFundPool
                            var candidates = robustFundPool.Where(f => f.NormalizedName.Contains(pureChinese) || pureChinese.Contains(f.NormalizedName.Substring(0, Math.Min(3, f.NormalizedName.Length))));
                            foreach (var f in candidates)
                            {
                                double currentScore = CalculateSimilarity(normalizedOcr, f.NormalizedName) * 100;
                                if (currentScore > bestScore) { bestScore = currentScore; bestMatch = f; }
                            }
                        }

                        if (bestScore > finalBestScore) { finalBestScore = bestScore; finalBestMatch = bestMatch; }
                    }

                    string calcMethod = "识别提取";
                    if (finalBestMatch != null && finalBestScore > 65 && holdAmount > 0)
                    {
                        // 🚀 核心：若未扫出份额，全面启用时空碰撞推演！
                        if (holdShares == 0)
                        {
                            // 🛡️ 在途资金剥离装甲：防止推演份额时，把今天刚加仓的钱当成已确认份额！
                            double effectiveAmountForShares = holdAmount;

                            if (userFundDict.TryGetValue(finalBestMatch.Code, out var tempExist))
                            {
                                // 检查是否在 4 天内有过加仓操作
                                if (DateTime.TryParse(tempExist.LastTradeDate, out DateTime ltd) && (DateTime.UtcNow.AddHours(8) - ltd).TotalDays <= 4)
                                {
                                    // 如果 OCR 扫出的总金额 > 在途金额，说明支付宝已将这笔钱计入总额，我们必须将它剥离出来再算份额！
                                    if (tempExist.LastAddAmount > 0 && holdAmount > tempExist.LastAddAmount)
                                    {
                                        effectiveAmountForShares = holdAmount - tempExist.LastAddAmount;
                                    }
                                }
                            }

                            // 使用剥离了在途资金后的【纯净本金】去推演真实份额
                            double calcShares = await DeduceSharesFromHistoryAsync(finalBestMatch.Code, effectiveAmountForShares, yesterdayIncome);
                            if (calcShares > 0) { holdShares = calcShares; calcMethod = "AI物理推演"; }
                        }
                        if (userFundDict.TryGetValue(finalBestMatch.Code, out var exist))
                        {
                            // 🛡️ 之前的逻辑（删掉）：
                            /*
                            if (holdAmount >= exist.HoldAmount || exist.HoldAmount == 0)
                            {
                                exist.HoldAmount = holdAmount;
                            }
                            */

                            // ==================== 🚀 终极防线装甲 ====================
                            // 1. 在途市值保护：如果今天有未确认的加仓资金，OCR扫描到的肯定没包含这笔钱！
                            var todayStr = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");
                            // ==================== 🚀 终极防线装甲 ====================
                            // 1. 在途市值保护：跨越周末的物理识别！
                            if (holdAmount > 0)
                            {
                                bool isRecentAdd = false;
                                if (DateTime.TryParse(exist.LastTradeDate, out DateTime lastTradeDate))
                                {
                                    // 只要是最近 4 天内加的仓，都处于“在途保护期”（完美覆盖周末逻辑）
                                    isRecentAdd = (DateTime.UtcNow.AddHours(8) - lastTradeDate).TotalDays <= 4;
                                }

                                // 如果处于保护期，且支付宝扫出来的钱比系统里少了很多（说明支付宝还没把那笔钱加上去）
                                if (isRecentAdd && exist.LastAddAmount > 0 && (exist.HoldAmount - holdAmount) > (exist.LastAddAmount * 0.5))
                                {
                                    exist.HoldAmount = holdAmount + exist.LastAddAmount; // 强行加回在途援军！
                                }
                                else
                                {
                                    exist.HoldAmount = holdAmount; // 正常覆盖
                                }
                            }


                            // 2. 🚀 本金铁律保护升级：强制校准！彻底解决大屏与支付宝收益率对不上的问题！
                            if (holdingIncome != 0)
                            {
                                // 只要扫出了真实的累计利润，直接反推并强制覆盖本金！绝不留死角！
                                exist.CostAmount = Math.Round(holdAmount - holdingIncome, 2);
                            }

                            // 更新推演份额（允许微调，防止误差积累）
                            if (holdShares > 0)
                            {
                                if (exist.HoldShares == 0 || Math.Abs(exist.HoldShares - holdShares) > 1) exist.HoldShares = holdShares;
                            }
                            _context.MyFunds.Update(exist);
                        }
                        else
                        {
                            var newFund = new MyFundConfig { Username = username, FundCode = finalBestMatch.Code, FundName = finalBestMatch.Name, HoldAmount = holdAmount, CostAmount = holdingIncome != 0 ? Math.Round(holdAmount - holdingIncome, 2) : holdAmount, HoldShares = holdShares };
                            _context.MyFunds.Add(newFund);
                            userFundDict[newFund.FundCode] = newFund;
                        }
                        importedCount++;

                        string sharesInfo = holdShares > 0 ? $"{holdShares:F2} ({calcMethod})" : "推演失败";
                        debugLog.Add($"{(finalBestScore >= 99.0 ? "⚡" : "✅")} 命中: {finalBestMatch.Name} [份额: {sharesInfo}]");
                    }
                }

                await _context.SaveChangesAsync();
                ClearTodayCache(username);
                return Ok($"识别完成！成功同步 {importedCount} 只。\n\n[诊断日志]\n{string.Join("\n", debugLog)}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"❌ 代码执行出现异常: {ex.Message}");
            }
        }
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
            return name.ToUpper()
                       .Replace(" ", "").Replace("（", "(").Replace("）", ")")
                       .Replace("(QDII)", "").Replace("QDII", "")
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
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(codes)) return BadRequest("缺少清算参数！");

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
                            var settleDate = ChinaDateDash();
                            if (ApplyOneDaySettlement(fund, rate, settleDate))
                            {
                                count++;
                            }
                        }
                    }
                }

                if (count > 0)
                {
                    var settleDate = ChinaNow().Date;
                    var fundCodes = funds.Select(f => f.FundCode).Distinct().ToList();
                    var todayRecords = await _context.FundRecords
                        .Where(r => r.FetchTime >= settleDate && fundCodes.Contains(r.FundCode))
                        .ToListAsync();
                    await UpsertTodayArchivesFromCurrentHoldingsAsync(username, settleDate, funds, todayRecords);
                }

                await _context.SaveChangesAsync();
                ClearTodayCache(username);
                return Ok($"已清算 {count} 只基金，并已同步今日收益档案！");
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
                            var settleDate = ChinaDateDash();
                            if (ApplyOneDaySettlement(fund, rate, settleDate))
                            {
                                successCount++;
                            }
                        }
                    }
                    catch { continue; }
                }

                if (successCount > 0)
                {
                    var settleDate = ChinaNow().Date;
                    var codesForToday = allFunds.Select(f => f.FundCode).Distinct().ToList();
                    var todayRecords = await _context.FundRecords
                        .Where(r => r.FetchTime >= settleDate && codesForToday.Contains(r.FundCode))
                        .ToListAsync();

                    foreach (var group in allFunds.GroupBy(f => f.Username))
                    {
                        await UpsertTodayArchivesFromCurrentHoldingsAsync(group.Key, settleDate, group.ToList(), todayRecords);
                        ClearTodayCache(group.Key);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动清算执行完毕！成功更新 {successCount} 只，并已同步今日收益档案。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动清算引擎异常: {ex.Message}");
            }
        }
        [HttpGet("global-indices")]
        public async Task<IActionResult> GetGlobalIndices()
        {
            var indices = new[]
            {
        new { name = "上证指数", secid = "1.000001" },
        new { name = "科创50",   secid = "1.000688" },
        new { name = "创业板指", secid = "0.399006" },
        new { name = "恒生指数", secid = "124.HSI" },
        new { name = "纳斯达克", secid = "105.IXIC" },
        new { name = "标普500",  secid = "109.SPX" },
        new { name = "道琼斯",   secid = "100.DJIA" }
    };

            // 🛡️ 终极降维打击：无视证书 + 强制降级 HTTP/1.1
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            // 🚀 绝对核心：强制使用 HTTP/1.1！专治东方财富强行挂断电话！
            http.DefaultRequestVersion = new Version(1, 1);

            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            http.DefaultRequestHeaders.Add("Accept", "*/*");
            http.DefaultRequestHeaders.Add("Connection", "keep-alive");
            http.DefaultRequestHeaders.Add("Referer", "https://quote.eastmoney.com/");

            var tasks = indices.Select(async idx =>
            {
                var url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?secid={idx.secid}&ut=fa5fd1943c7b386f172d6893dbfba10b&fields1=f1,f2,f3,f4,f5,f6&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59&klt=101&fqt=1&end=20500101&lmt=250";
                try
                {
                    var response = await http.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(response);

                    if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
                    {
                        if (data.TryGetProperty("klines", out var klines) && klines.GetArrayLength() > 0)
                        {
                            var klineArray = klines.EnumerateArray().Select(k => k.GetString()).ToArray();
                            var latestItem = klineArray[^1].Split(',');
                            var oldestItem = klineArray[0].Split(',');

                            double latestClose = 0, todayRate = 0, oldestClose = 0;
                            if (latestItem.Length > 2) double.TryParse(latestItem[2], out latestClose);
                            if (latestItem.Length > 8) double.TryParse(latestItem[8], out todayRate);
                            if (oldestItem.Length > 2) double.TryParse(oldestItem[2], out oldestClose);

                            double yearRate = oldestClose > 0 ? Math.Round((latestClose - oldestClose) / oldestClose * 100, 2) : 0;

                            var cleanKlines = klineArray.Reverse().Select(k =>
                            {
                                var p = k.Split(',');
                                double rate = 0;
                                if (p.Length > 8) double.TryParse(p[8], out rate);
                                return (object)new { date = p[0], rate = rate };
                            }).ToList();

                            return new
                            {
                                name = idx.name,
                                latest = latestClose,
                                todayRate = todayRate,
                                yearRate = yearRate,
                                klines = cleanKlines
                            };
                        }
                    }

                    return new { name = $"{idx.name} (无数据)", latest = 0.0, todayRate = 0.0, yearRate = 0.0, klines = new List<object>() };
                }
                catch (Exception ex)
                {
                    string innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    return new { name = $"{idx.name} (异常: {innerMsg})", latest = 0.0, todayRate = 0.0, yearRate = 0.0, klines = new List<object>() };
                }
            });

            var results = await Task.WhenAll(tasks);
            return Ok(results);
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
                return StatusCode(500, $"❌ 装载失败: {ex.Message}");
            }
        }
        [HttpGet("today")]
        public async Task<IActionResult> GetTodayData([FromQuery] string username, [FromQuery] bool force = false)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("请提供指挥官代号");

            try
            {
                string cacheKey = $"Tactical_TodayData_{username}_{ChinaDateDash()}";
                if (force)
                {
                    _cache.Remove(cacheKey);
                }
                else if (_cache.TryGetValue(cacheKey, out object cachedResult))
                {
                    return Ok(cachedResult);
                }

                // ✅ 优化后（只读查询，不跟踪变更）
                var myFunds = await _context.MyFunds
                    .AsNoTracking()  // 添加这行
                    .Where(f => f.Username == username)
                    .ToListAsync();
                var myFundCodes = myFunds.Select(f => f.FundCode).ToList();

                if (!myFundCodes.Any()) return Ok(new List<object>());

                var localTime = ChinaNow();
                var today = localTime.Date;
                string todayStr = localTime.ToString("yyyy'/'MM'/'dd");
                string todayDash = localTime.ToString("yyyy-MM-dd");

                // 只取最近 10 天的必要数据，避免首屏把历史全表拖回内存。
                var recentStart = today.AddDays(-10);
                var recentRecords = await _context.FundRecords
                    .AsNoTracking()
                    .Where(r => myFundCodes.Contains(r.FundCode) && r.FetchTime >= recentStart)
                    .OrderBy(r => r.FetchTime)
                    .ToListAsync();

                var todayRecords = recentRecords
                    .Where(r => r.FetchTime >= today)
                    .ToList();

                var lastRecordDict = recentRecords
                    .Where(r => r.FetchTime < today)
                    .GroupBy(r => r.FundCode)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

                var pastActualDict = recentRecords
                    .Where(r => r.ActualRate != 0)
                    .GroupBy(r => r.FundCode)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).Take(3).ToList());


                // ⚡ 首屏性能优化：today 接口不再发起任何外部净值 HTTP 请求。
                // 真实净值由 NavSettlementService 后台结算后写入 MyFunds.LastSettled* 字段。
                // 这样 App/Web 打开和下拉刷新只访问本机数据库，避免东方财富接口抖动拖慢用户请求。


                var result = myFunds.Select(config =>
                {
                    var fundRecords = todayRecords.Where(r => r.FundCode == config.FundCode).ToList();
                    lastRecordDict.TryGetValue(config.FundCode, out var lastRecord);

                    var past3DaysRecords = pastActualDict.TryGetValue(config.FundCode, out var pastList)
                        ? pastList
                        : new List<FundData>();

                    double avgDiff = 0;
                    if (past3DaysRecords.Count > 0)
                    {
                        avgDiff = past3DaysRecords.Average(r => r.ActualRate - r.EstimatedRate);
                        if (avgDiff > 0.5) avgDiff = 0.5;
                        if (avgDiff < -0.5) avgDiff = -0.5;
                    }

                    var dataPoints = new List<object[]>();

                    if (lastRecord != null)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", Math.Round(lastRecord.EstimatedRate + avgDiff, 2) });
                    }

                    dataPoints.AddRange(fundRecords.Select(r => new object[] {
                        r.FetchTime.ToString("yyyy'/'MM'/'dd HH:mm:ss"),
                        Math.Round(r.EstimatedRate + avgDiff, 2)
                    }));

                    if (dataPoints.Count == 0)
                    {
                        dataPoints.Add(new object[] { todayStr + " 09:30:00", 0 });
                    }

                    // 是否已完成今日真实净值清算。只读本地字段，不在 today 请求里访问外部接口。
                    bool isSettled = config.LastSettledDate == todayDash;
                    double? actualRate = isSettled ? config.LastSettledRate : null;
                    double? actualExactProfit = isSettled ? config.LastSettledProfit : null;

                    // FundController.cs 中 GetTodayData 方法内部
                    return new
                    {
                        code = config.FundCode,
                        name = config.FundName,
                        amount = config.HoldAmount,
                        shares = config.HoldShares,
                        cost = config.CostAmount > 0 ? config.CostAmount : (double?)null,
                        realizedProfit = config.RealizedProfit,
                        lastTradeDate = config.LastTradeDate,
                        lastAddAmount = config.LastAddAmount,
                        lastSettledDate = config.LastSettledDate,
                        lastSettledProfit = config.LastSettledProfit,
                        lastSettledRate = config.LastSettledRate,
                        existingReturnRate = 0,
                        breakEvenRate = 0,
                        diffRate = lastRecord != null ? lastRecord.DiffRate : 0,
                        calibrationOffset = Math.Round(avgDiff, 4),
                        data = dataPoints,
                        isSettled = isSettled,
                        actualRate = actualRate,
                        actualExactProfit = actualExactProfit
                    };

                });

                var finalResult = result.OrderByDescending(x => x.amount).ToList();

                // 真实净值一出现，后端立即修正“今日总持仓 + 单只基金”档案。
                // 这样不再依赖前端 save-archive，也能覆盖旧版本写坏的 TOTAL 记录。
                if (myFunds.Any(f => f.LastSettledDate == todayDash))
                {
                    await UpsertTodayArchivesFromCurrentHoldingsAsync(username, today, myFunds, todayRecords);
                    await _context.SaveChangesAsync();
                }

                // ✅ 优化后（60秒缓存，减少数据库压力）
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(60))
                    .SetSlidingExpiration(TimeSpan.FromSeconds(30));  // 滑动过期
                _cache.Set(cacheKey, finalResult, cacheOptions);

                return Ok(finalResult);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"服务器当场阵亡：{ex.Message}");
            }
        }

        [HttpPost("sync-real-nav")]
        public async Task<IActionResult> SyncRealNav([FromForm] string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供指挥官代号");

            var localTime = ChinaNow();
            string settleDate = localTime.ToString("yyyy-MM-dd");
            DateTime today = localTime.Date;
            DateTime tomorrow = today.AddDays(1);

            try
            {
                var funds = await _context.MyFunds
                    .Where(f => f.Username == username)
                    .ToListAsync();

                if (funds.Count == 0)
                {
                    return Ok(new { success = true, updated = 0, message = "暂无持仓" });
                }

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");

                int updated = 0;
                int skipped = 0;
                var diagnostics = new List<string>();

                foreach (var group in funds.GroupBy(f => f.FundCode))
                {
                    var snapshot = await FetchOfficialNavSnapshotAsync(client, group.Key, settleDate);
                    if (snapshot == null)
                    {
                        skipped++;
                        diagnostics.Add($"{group.Key}: 尚未拿到可校验真实净值");
                        continue;
                    }

                    var targetRecord = await _context.FundRecords
                        .Where(r => r.FundCode == group.Key && r.FetchTime >= today && r.FetchTime < tomorrow)
                        .OrderByDescending(r => r.FetchTime)
                        .FirstOrDefaultAsync();

                    if (targetRecord != null)
                    {
                        targetRecord.ActualRate = snapshot.Rate;
                        targetRecord.DiffRate = Math.Round(snapshot.Rate - targetRecord.EstimatedRate, 2);
                    }

                    foreach (var fund in group)
                    {
                        double? exactProfit = null;
                        if (snapshot.NavDiff.HasValue)
                        {
                            double effectiveShares = GetEffectiveShares(fund, settleDate);
                            if (effectiveShares > 0) exactProfit = Math.Round(effectiveShares * snapshot.NavDiff.Value, 2);
                        }

                        if (ApplyOneDaySettlement(fund, snapshot.Rate, settleDate, exactProfit))
                        {
                            updated++;
                        }
                    }

                    diagnostics.Add($"{group.Key}: {snapshot.Rate:F4}% [{snapshot.Source}]");
                }

                var todayRecords = await _context.FundRecords
                    .Where(r => r.FetchTime >= today && r.FetchTime < tomorrow)
                    .ToListAsync();

                await UpsertTodayArchivesFromCurrentHoldingsAsync(username, today, funds, todayRecords);
                await _context.SaveChangesAsync();

                ClearTodayCache(username);

                return Ok(new
                {
                    success = true,
                    updated,
                    skipped,
                    message = $"真实净值同步完成，更新 {updated} 条持仓。",
                    diagnostics
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"真实净值同步失败: {ex.Message}");
            }
        }

        // 🚀 猎隼侦察兵升级版：双重刺探，获取单位净值计算绝对物理收益
        private async Task<(double? rate, double? exactProfit)> GetTodayRealRateAsync(string fundCode, string todayStr, double shares)
        {
            string cacheKey = $"RealRateV2_{fundCode}_{todayStr}_{shares}";
            if (_cache.TryGetValue(cacheKey, out (double?, double?) cached)) return cached;

            string missKey = $"NoRealRate_{fundCode}_{todayStr}";
            if (_cache.TryGetValue(missKey, out _)) return (null, null);

            try
            {
                // 🚀 修复 1：5秒超时，从容应对高并发
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={fundCode}&pageIndex=1&pageSize=2";
                string res = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(res);
                var dataArray = doc.RootElement.GetProperty("Data").GetProperty("LSJZList");

                var snapshot = TryBuildOfficialNavSnapshot(dataArray, todayStr);
                if (snapshot != null)
                {
                    double? exactProfit = null;
                    if (shares > 0 && snapshot.NavDiff.HasValue)
                    {
                        exactProfit = Math.Round(shares * snapshot.NavDiff.Value, 2);
                    }

                    var result = (snapshot.Rate, exactProfit);
                    _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
                    return result;
                }

                if (dataArray.GetArrayLength() > 0)
                {
                    var latest = dataArray[0];
                    if ((latest.GetProperty("FSRQ").GetString() ?? "") != todayStr)
                    {
                        _cache.Set(missKey, true, TimeSpan.FromMinutes(2));
                        return (null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                // 🚀 修复 4：网络超时报错绝不连坐！直接放行，等前端 15 秒后重试！
                Console.WriteLine($"[猎隼刺探异常] {fundCode}: {ex.Message}");
            }

            return (null, null);
        }

        // 🚀 1. 战术加仓接口：支持日期，仅需金额
        [HttpPost("add-position")]
        public async Task<IActionResult> AddPosition([FromForm] string username, [FromForm] string code, [FromForm] double addAmount, [FromForm] string tradeDate)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未授权");
            try
            {
                var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
                if (fund == null) return BadRequest("未找到基金");

                // 核心逻辑：加仓即增加本金和市值。
                // 注意：若日期为“今天”，这笔钱产生的收益通常要明天才开始算，系统会自动在前端进行收益剥离。
                fund.HoldAmount += addAmount;
                fund.CostAmount += addAmount;

                // 🚀 终极补丁：把加仓的时间和金额烙印在数据库里
                if (fund.LastTradeDate == tradeDate)
                {
                    fund.LastAddAmount += addAmount; // 同日多次加仓累加
                }
                else
                {
                    fund.LastTradeDate = tradeDate;
                    fund.LastAddAmount = addAmount;
                }


                _context.MyFunds.Update(fund);
                await _context.SaveChangesAsync();
                ClearTodayCache(username);

                return Ok(new { success = true, msg = $"加仓成功！[{tradeDate}] 注入资金: {addAmount:F2} 元" });
            }
            catch (Exception ex) { return StatusCode(500, $"加仓异常: {ex.Message}"); }
        }

        // 🚀 2. 战术减仓接口：日期支持，金额可选 (留空则系统自动按份额比例算)
        [HttpPost("reduce-position")]
        public async Task<IActionResult> ReducePosition([FromForm] string username, [FromForm] string code, [FromForm] double reduceShares, [FromForm] double? reduceAmount, [FromForm] string tradeDate)
        {
            if (string.IsNullOrEmpty(username)) return Unauthorized("未授权");
            var fund = await _context.MyFunds.FirstOrDefaultAsync(f => f.Username == username && f.FundCode == code);
            if (fund == null) return BadRequest("未找到基金");

            // 养基宝逻辑：如果没填卖出金额，系统利用“当前市值/总份额”算出单价，自动推演卖出总额
            double finalReduceAmount = reduceAmount ?? 0;
            if (finalReduceAmount == 0)
            {
                double currentUnitPrice = fund.HoldAmount / fund.HoldShares;
                finalReduceAmount = currentUnitPrice * reduceShares;
            }

            // 财务核算：计算这部分卖出份额的平均成本
            double unitCost = fund.CostAmount / fund.HoldShares;
            double soldCost = unitCost * reduceShares;
            double profit = finalReduceAmount - soldCost;

            // 物理执行：扣减份额、扣减本金、等比例扣减当前市值
            fund.HoldShares -= reduceShares;
            fund.CostAmount -= soldCost;
            double unitAmount = fund.HoldAmount / (fund.HoldShares + reduceShares);
            fund.HoldAmount -= (unitAmount * reduceShares);

            // 利润封存：将变现利润锁入落袋小金库
            fund.RealizedProfit += profit;
            // 🚀 核心补丁：逆向记录减仓在途资金！(存入负数)
            // 这样前端计算今日收益率时，会用“减去负数(等于加回)”的魔法，完美保住当日收益率！
            if (fund.LastTradeDate == tradeDate)
            {
                fund.LastAddAmount -= finalReduceAmount; // 同日多次操作抵消/累加
            }
            else
            {
                fund.LastTradeDate = tradeDate;
                fund.LastAddAmount = -finalReduceAmount;
            }
            _context.MyFunds.Update(fund);
            await _context.SaveChangesAsync();
            ClearTodayCache(username);

            return Ok(new { success = true, msg = $"[{tradeDate}] 减仓完毕！归库利润: {profit:F2} 元" });
        }

        // =========================================================================
        // 🚀 以下为您昨日遗失的 9 大核心功能接口，现已从备份库中完美复原！
        // =========================================================================

        // 🚀 恢复带份额的修改接口
        [HttpPost("update-details")]
        public async Task<IActionResult> UpdateDetailsAsync([FromForm] string username, [FromForm] string code, [FromForm] double costAmount, [FromForm] double holdShares, [FromForm] string originalCode)
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
                        existFund.HoldShares = holdShares;
                        // 更新份额
                        _context.MyFunds.Update(existFund);
                        var oldRecords = await _context.FundRecords.Where(r => r.FundCode == originalCode).ToListAsync();
                        if (oldRecords.Any()) _context.FundRecords.RemoveRange(oldRecords);
                    }
                    else
                    {
                        existFund.CostAmount = costAmount;
                        existFund.HoldShares = holdShares; // 更新份额
                        _context.MyFunds.Update(existFund);
                    }

                    await _context.SaveChangesAsync();
                    ClearTodayCache(username);
                    return Ok($"本金、代码与份额补给完成！");
                }

                return BadRequest("未能匹配到该基金");
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
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Referer", "http://fundf10.eastmoney.com/");
            var targetFunds = await _context.MyFunds.Select(f => f.FundCode).Distinct().ToListAsync();
            var localTime = DateTime.UtcNow.AddHours(8);
            string todayStr = localTime.ToString("yyyy-MM-dd");
            var todayStart = localTime.Date;
            var tomorrowStart = todayStart.AddDays(1);

            var resultLog = new List<string>
            {
                $"===== 手动清算收益 =====",
                $"当前时间: {localTime:yyyy-MM-dd HH:mm:ss}"
            };
            foreach (var code in targetFunds)
            {
                try
                {
                    string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=1";
                    string response = await client.GetStringAsync(url);

                    if (!response.Contains("LSJZList")) continue;

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
                                resultLog.Add($"✅ [{code}] 覆盖成功: {actualRate}%");
                            }
                        }
                    }
                }
                catch { }
            }

            await _context.SaveChangesAsync();
            return Ok(resultLog);
        }

        private sealed class SectorDefinition
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string[] Include { get; init; } = Array.Empty<string>();
            public string[] Exclude { get; init; } = Array.Empty<string>();
        }

        private sealed class SectorFundQuote
        {
            public string Code { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public double Rate { get; init; }
            public double? MonthRate { get; init; }
            public bool HasQuote { get; init; }
            public string UpdatedAt { get; init; } = string.Empty;
        }

        private sealed class SectorSummaryDto
        {
            public string Key { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public double Rate { get; init; }
            public int FundCount { get; init; }
            public int QuotedCount { get; init; }
            public int StreakDays { get; init; }
            public int HoldingRank { get; init; }
            public string UpdatedAt { get; init; } = string.Empty;
            public List<SectorFundQuote> PreviewFunds { get; init; } = new();
        }

        private static readonly IReadOnlyList<SectorDefinition> SectorDefinitions = new List<SectorDefinition>
        {
            new() { Key = "gold", Name = "黄金", Include = new[] { "黄金", "上海金", "黄金ETF", "黄金基金" }, Exclude = new[] { "黄金股" } },
            new() { Key = "gold_stock", Name = "黄金股", Include = new[] { "黄金股", "有色金属", "贵金属" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "lithium", Name = "锂矿", Include = new[] { "锂", "锂矿", "锂电", "电池" }, Exclude = new[] { "货币", "债" } },
            new() { Key = "rare_earth", Name = "稀土永磁", Include = new[] { "稀土", "永磁", "稀有金属" }, Exclude = new[] { "债" } },
            new() { Key = "new_energy", Name = "新能源", Include = new[] { "新能源", "新能源车", "电动车", "电池", "碳中和" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "solid_battery", Name = "固态电池", Include = new[] { "固态电池", "电池", "锂电" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "storage", Name = "储能", Include = new[] { "储能", "电力设备", "新能源" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nonferrous", Name = "有色金属", Include = new[] { "有色", "有色金属", "金属", "资源", "矿业" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "gem", Name = "创业板", Include = new[] { "创业板", "创业", "创业成长" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "steel", Name = "钢铁", Include = new[] { "钢铁" }, Exclude = Array.Empty<string>() },
            new() { Key = "agri", Name = "农林牧渔", Include = new[] { "农业", "畜牧", "养殖", "农林牧渔", "粮食" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "media_game", Name = "传媒游戏", Include = new[] { "传媒", "游戏", "动漫", "影视", "文化", "娱乐" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "grid", Name = "电网设备", Include = new[] { "电网", "电力设备", "电力", "智能电网" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "home_appliance", Name = "家用电器", Include = new[] { "家电", "家用电器", "白色家电" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "pv", Name = "光伏", Include = new[] { "光伏", "太阳能", "新能源" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "double_innovation", Name = "双创50", Include = new[] { "双创", "科创创业", "科创50", "创业板" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "food_beverage", Name = "食品饮料", Include = new[] { "食品", "饮料", "白酒", "消费" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "innovative_drug", Name = "创新药", Include = new[] { "创新药", "医药", "生物医药", "港股通医药" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "oversea_medicine", Name = "海外医药", Include = new[] { "海外医药", "全球医药", "港股通医药", "恒生医疗", "中概互联医疗" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "medicine", Name = "医药", Include = new[] { "医药", "医疗", "生物", "药", "中药", "疫苗", "CRO" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "healthcare", Name = "医疗", Include = new[] { "医疗", "医药", "器械", "医美", "生物" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "nuclear", Name = "可控核聚变", Include = new[] { "核", "核电", "高端装备", "军工" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "semiconductor", Name = "半导体", Include = new[] { "半导体", "芯片", "集成电路", "科创芯片" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "semi_material", Name = "半导体材料设备", Include = new[] { "半导体材料", "半导体设备", "芯片设备", "芯片材料", "集成电路" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "bank", Name = "银行", Include = new[] { "银行" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "military", Name = "军工", Include = new[] { "军工", "国防", "航天", "航空", "高端装备" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "sp500", Name = "标普", Include = new[] { "标普", "S&P", "SP500", "美国500" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "asia_pacific", Name = "亚太", Include = new[] { "亚太", "日本", "越南", "印度", "东南亚" }, Exclude = new[] { "债", "货币" } },
            new() { Key = "bond", Name = "债基", Include = new[] { "债", "债券", "纯债", "信用债", "中短债" }, Exclude = new[] { "可转债", "转债" } },
            new() { Key = "convertible_bond", Name = "可转债", Include = new[] { "可转债", "转债" }, Exclude = Array.Empty<string>() },
            new() { Key = "mixed_bond", Name = "混债", Include = new[] { "混债", "二级债", "一级债", "固收+", "固收加" }, Exclude = Array.Empty<string>() },
            new() { Key = "money", Name = "货币基金", Include = new[] { "货币", "现金", "添利", "余额", "天天理财" }, Exclude = new[] { "股票", "混合" } },
            new() { Key = "real_estate", Name = "地产", Include = new[] { "地产", "房地产", "沪深300地产", "地产等权" }, Exclude = new[] { "债", "货币" } }
        };

        private static bool ContainsAny(string text, IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                if (!string.IsNullOrWhiteSpace(word) && text.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int ScoreFundForSector(string fundName, SectorDefinition def)
        {
            var name = fundName ?? string.Empty;
            int score = 0;
            if (name.Contains(def.Name, StringComparison.OrdinalIgnoreCase)) score += 80;
            foreach (var kw in def.Include)
            {
                if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) score += Math.Min(40, kw.Length * 8);
            }
            if (name.Contains("ETF", StringComparison.OrdinalIgnoreCase)) score += 25;
            if (name.Contains("联接", StringComparison.OrdinalIgnoreCase)) score += 20;
            if (name.Contains("指数", StringComparison.OrdinalIgnoreCase)) score += 15;
            if (name.Contains("C", StringComparison.OrdinalIgnoreCase)) score += 3;
            if (name.Contains("货币", StringComparison.OrdinalIgnoreCase) && def.Key != "money") score -= 200;
            if (name.Contains("债", StringComparison.OrdinalIgnoreCase) && def.Key != "bond" && def.Key != "convertible_bond" && def.Key != "mixed_bond") score -= 120;
            return score;
        }

        private static SectorDefinition ResolveSector(string keyOrName)
        {
            var clean = (keyOrName ?? string.Empty).Trim();
            return SectorDefinitions.FirstOrDefault(s => s.Key.Equals(clean, StringComparison.OrdinalIgnoreCase) || s.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))
                   ?? SectorDefinitions.FirstOrDefault(s => clean.Contains(s.Name, StringComparison.OrdinalIgnoreCase))
                   ?? new SectorDefinition { Key = clean, Name = clean, Include = new[] { clean }, Exclude = Array.Empty<string>() };
        }

        private static List<FundInfoCache> MatchFundsBySector(IEnumerable<FundInfoCache> allFunds, SectorDefinition def, int limit)
        {
            return allFunds
                .Where(f => !string.IsNullOrWhiteSpace(f.Code) && !string.IsNullOrWhiteSpace(f.Name))
                .Where(f => ContainsAny(f.Name, def.Include) && !ContainsAny(f.Name, def.Exclude))
                .Select(f => new { Fund = f, Score = ScoreFundForSector(f.Name, def) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Fund.Name.Length)
                .Take(limit)
                .Select(x => x.Fund)
                .ToList();
        }

        private static async Task<SectorFundQuote> FetchFundQuoteAsync(HttpClient client, FundInfoCache fund, bool withMonthRate = false)
        {
            double rate = 0;
            bool hasQuote = false;
            string updatedAt = string.Empty;
            try
            {
                string gzUrl = $"http://fundgz.1234567.com.cn/js/{fund.Code}.js?rt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                string gzRes = await client.GetStringAsync(gzUrl);
                var match = Regex.Match(gzRes, @"jsonpgz\((.*?)\);");
                if (match.Success)
                {
                    using var doc = JsonDocument.Parse(match.Groups[1].Value);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("gszzl", out var gszzl) && double.TryParse(gszzl.GetString(), out var parsedRate))
                    {
                        rate = Math.Round(parsedRate, 2);
                        hasQuote = true;
                    }
                    if (root.TryGetProperty("gztime", out var timeProp)) updatedAt = timeProp.GetString() ?? string.Empty;
                    if (root.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                    {
                        fund.Name = nameProp.GetString()!;
                    }
                }
            }
            catch
            {
                hasQuote = false;
            }

            double? monthRate = null;
            if (withMonthRate)
            {
                monthRate = await FetchFundMonthRateAsync(client, fund.Code);
            }

            return new SectorFundQuote
            {
                Code = fund.Code,
                Name = fund.Name,
                Rate = rate,
                MonthRate = monthRate,
                HasQuote = hasQuote,
                UpdatedAt = updatedAt
            };
        }

        private static async Task<double?> FetchFundMonthRateAsync(HttpClient client, string code)
        {
            try
            {
                string url = $"http://api.fund.eastmoney.com/f10/lsjz?fundCode={code}&pageIndex=1&pageSize=28";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Referrer = new Uri("http://fundf10.eastmoney.com/");
                var res = await client.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;
                string body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("Data", out var data)) return null;
                if (!data.TryGetProperty("LSJZList", out var list) || list.GetArrayLength() < 2) return null;

                var latest = list[0];
                var oldest = list[list.GetArrayLength() - 1];
                if (double.TryParse(latest.GetProperty("DWJZ").GetString(), out double latestNav) &&
                    double.TryParse(oldest.GetProperty("DWJZ").GetString(), out double oldestNav) && oldestNav > 0)
                {
                    return Math.Round((latestNav - oldestNav) / oldestNav * 100.0, 2);
                }
            }
            catch { }
            return null;
        }

        private async Task<List<SectorFundQuote>> BuildSectorQuotesAsync(SectorDefinition def, int fundLimit, bool withMonthRate)
        {
            var allFunds = await GetAllFundsAsync();
            var matched = MatchFundsBySector(allFunds, def, fundLimit);
            if (matched.Count == 0) return new List<SectorFundQuote>();

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(withMonthRate ? 8 : 5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/123.0 Safari/537.36");
            client.DefaultRequestVersion = new Version(1, 1);

            using var limiter = new SemaphoreSlim(8);
            var tasks = matched.Select(async fund =>
            {
                await limiter.WaitAsync();
                try { return await FetchFundQuoteAsync(client, fund, withMonthRate); }
                finally { limiter.Release(); }
            }).ToArray();

            var quotes = await Task.WhenAll(tasks);
            return quotes
                .Where(q => !string.IsNullOrWhiteSpace(q.Code))
                .OrderByDescending(q => q.Rate)
                .ThenBy(q => q.Name)
                .ToList();
        }

        [HttpGet("sectors")]
        public async Task<IActionResult> GetSectors([FromQuery] bool force = false)
        {
            string sectorCacheKey = "FundSectorRadarV5";
            if (!force && _cache.TryGetValue(sectorCacheKey, out object cachedSectors)) return Ok(cachedSectors);

            try
            {
                var allFunds = await GetAllFundsAsync();
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/123.0 Safari/537.36");
                client.DefaultRequestVersion = new Version(1, 1);

                var summaries = new List<SectorSummaryDto>();
                int rank = 1;
                foreach (var def in SectorDefinitions)
                {
                    var matched = MatchFundsBySector(allFunds, def, 22);
                    if (matched.Count == 0) continue;

                    using var limiter = new SemaphoreSlim(8);
                    var quoteTasks = matched.Take(18).Select(async fund =>
                    {
                        await limiter.WaitAsync();
                        try { return await FetchFundQuoteAsync(client, fund, false); }
                        finally { limiter.Release(); }
                    });
                    var quotes = (await Task.WhenAll(quoteTasks)).Where(q => q.HasQuote).ToList();
                    if (quotes.Count == 0) continue;

                    double avgRate = Math.Round(quotes.Average(q => q.Rate), 2);
                    summaries.Add(new SectorSummaryDto
                    {
                        Key = def.Key,
                        Name = def.Name,
                        Rate = avgRate,
                        FundCount = matched.Count,
                        QuotedCount = quotes.Count,
                        StreakDays = avgRate > 0.005 ? 1 : (avgRate < -0.005 ? -1 : 0),
                        HoldingRank = rank++,
                        UpdatedAt = quotes.Select(q => q.UpdatedAt).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                        PreviewFunds = quotes.OrderByDescending(q => q.Rate).Take(3).ToList()
                    });
                }

                var ordered = summaries.OrderByDescending(s => s.Rate).ToList();
                var payload = new
                {
                    source = "东方财富/天天基金估算 + 本地基金名称主题归类",
                    updatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    top = ordered.Take(30).ToList(),
                    bottom = ordered.OrderBy(s => s.Rate).Take(30).ToList(),
                    all = ordered
                };

                _cache.Set(sectorCacheKey, payload, TimeSpan.FromMinutes(3));
                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"板块基金雷达故障: {ex.Message}");
            }
        }

        [HttpGet("sector-details")]
        public async Task<IActionResult> GetSectorDetails([FromQuery] string secCode)
        {
            // 兼容旧前端：secCode 现在既可以传板块 key，也可以传板块名。
            if (string.IsNullOrWhiteSpace(secCode)) return BadRequest("缺少板块识别码");
            return await GetSectorFunds(secCode, 20, false);
        }

        [HttpGet("sector-funds")]
        public async Task<IActionResult> GetSectorFunds([FromQuery] string sectorName, [FromQuery] int limit = 20, [FromQuery] bool force = false)
        {
            if (string.IsNullOrWhiteSpace(sectorName)) return BadRequest("缺少板块名称");
            limit = Math.Clamp(limit, 5, 40);
            var def = ResolveSector(sectorName);
            string cacheKey = $"SectorFundsV5_{def.Key}_{limit}";
            if (!force && _cache.TryGetValue(cacheKey, out object cached)) return Ok(cached);

            try
            {
                var quotes = await BuildSectorQuotesAsync(def, limit, withMonthRate: true);
                var available = quotes.Where(q => q.HasQuote).ToList();
                double avgRate = available.Count > 0 ? Math.Round(available.Average(q => q.Rate), 2) : 0;
                var payload = new
                {
                    key = def.Key,
                    name = def.Name,
                    rate = avgRate,
                    fundCount = quotes.Count,
                    updatedAt = available.Select(q => q.UpdatedAt).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    funds = quotes.OrderByDescending(q => q.Rate).ToList()
                };
                _cache.Set(cacheKey, payload, TimeSpan.FromMinutes(3));
                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"找基失败: {ex.Message}");
            }
        }



        private sealed class NewsItemDto
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string Summary { get; init; } = string.Empty;
            public string ShowTime { get; init; } = string.Empty;
            public string TimeText { get; init; } = string.Empty;
            public string DateText { get; init; } = string.Empty;
            public string Source { get; init; } = "东方财富7x24";
            public string Url { get; init; } = string.Empty;
            public bool Important { get; init; }
            public string Sentiment { get; init; } = "neutral";
            public int ImpactScore { get; init; }
            public string? MatchedFundCode { get; init; }
            public string? MatchedFundName { get; init; }
            public string[] Tags { get; init; } = Array.Empty<string>();
            public long Sort { get; init; }
        }

        private static readonly string[] ImportantNewsKeywords = new[]
        {
            "突发", "重磅", "重大", "国务院", "国常会", "发改委", "工信部", "商务部", "财政部", "央行", "证监会", "交易所",
            "降息", "加息", "利率", "降准", "CPI", "PPI", "GDP", "PMI", "非农", "美联储", "鲍威尔", "特朗普", "关税",
            "制裁", "冲突", "战争", "油轮", "原油", "黄金", "稀土", "锂", "半导体", "人工智能", "地产", "政策", "收储", "限购", "补贴", "财报", "业绩", "订单"
        };

        private static readonly string[] PositiveNewsKeywords = new[]
        {
            "上涨", "涨", "走高", "新高", "增长", "上调", "突破", "利好", "扩产", "订单", "补贴", "降息", "降准", "回购", "增持", "放松", "优化", "超预期", "批准", "支持", "刺激"
        };

        private static readonly string[] NegativeNewsKeywords = new[]
        {
            "下跌", "跌", "走低", "下降", "下滑", "下调", "亏损", "制裁", "禁令", "关税", "调查", "处罚", "暂停", "限制", "拒绝", "战争", "冲突", "爆炸", "违约", "不及预期"
        };

        private static string StripJsonp(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "{}";
            var match = Regex.Match(text, @"^[^(]*\((.*)\)\s*;?\s*$", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : text;
        }

        private static string ToDateText(DateTime dt) => dt.ToString("MM月dd日 dddd", new System.Globalization.CultureInfo("zh-CN"));
        private static string ToTimeText(DateTime dt) => dt.ToString("HH:mm");

        private static string ClassifySentiment(string text)
        {
            int pos = PositiveNewsKeywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
            int neg = NegativeNewsKeywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (pos > neg) return "positive";
            if (neg > pos) return "negative";
            return "neutral";
        }

        private static int EstimateImpactScore(string text, bool important, int matchScore)
        {
            int score = important ? 3 : 1;
            if (text.Contains("突发") || text.Contains("重磅") || text.Contains("重大")) score += 2;
            if (text.Contains("国务院") || text.Contains("国常会") || text.Contains("央行") || text.Contains("证监会") || text.Contains("美联储")) score += 1;
            if (text.Contains("涨停") || text.Contains("跌停") || text.Contains("暴涨") || text.Contains("暴跌")) score += 1;
            if (matchScore >= 60) score += 1;
            return Math.Clamp(score, 0, 5);
        }

        private async Task<List<NewsItemDto>> FetchEastMoneyFastNewsAsync(int limit)
        {
            limit = Math.Clamp(limit, 20, 120);
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string callback = $"jQuery3510_{ts}";
            string url = $"https://np-weblist.eastmoney.com/comm/web/getFastNewsList?client=web&biz=web_724&fastColumn=102&sortEnd=&pageSize={limit}&req_trace={ts}&_={ts + 1}&callback={callback}";

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestVersion = new Version(1, 1);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/123.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Referer", "https://kuaixun.eastmoney.com/");
            client.DefaultRequestHeaders.Add("Accept", "*/*");

            string body = await client.GetStringAsync(url);
            string json = StripJsonp(body);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var list = root.GetProperty("data").GetProperty("fastNewsList");
            var result = new List<NewsItemDto>();

            foreach (var item in list.EnumerateArray())
            {
                string id = item.TryGetProperty("code", out var codeProp) ? codeProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                string title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                string summary = item.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() ?? title : title;
                string showTime = item.TryGetProperty("showTime", out var timeProp) ? timeProp.GetString() ?? "" : "";
                long sort = 0;
                if (item.TryGetProperty("realSort", out var sortProp)) long.TryParse(sortProp.GetString(), out sort);
                int titleColor = 0;
                if (item.TryGetProperty("titleColor", out var colorProp) && colorProp.ValueKind == JsonValueKind.Number) titleColor = colorProp.GetInt32();

                var stockCodes = new List<string>();
                if (item.TryGetProperty("stockList", out var stockProp) && stockProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in stockProp.EnumerateArray())
                    {
                        var sv = s.GetString();
                        if (!string.IsNullOrWhiteSpace(sv)) stockCodes.Add(sv);
                    }
                }

                DateTime dt = ChinaNow();
                DateTime.TryParse(showTime, out dt);
                string text = title + " " + summary;
                bool important = titleColor != 0 || ImportantNewsKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
                string sentiment = ClassifySentiment(text);

                result.Add(new NewsItemDto
                {
                    Id = id,
                    Title = string.IsNullOrWhiteSpace(title) ? summary : title,
                    Summary = summary,
                    ShowTime = string.IsNullOrWhiteSpace(showTime) ? dt.ToString("yyyy-MM-dd HH:mm:ss") : showTime,
                    DateText = ToDateText(dt),
                    TimeText = ToTimeText(dt),
                    Source = "东方财富7x24",
                    Url = id.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? id : $"https://finance.eastmoney.com/a/{id}.html",
                    Important = important,
                    Sentiment = sentiment,
                    ImpactScore = EstimateImpactScore(text, important, 0),
                    Tags = stockCodes.ToArray(),
                    Sort = sort == 0 ? new DateTimeOffset(dt).ToUnixTimeMilliseconds() : sort
                });
            }

            return result.OrderByDescending(x => x.Sort).ToList();
        }

        private static readonly Dictionary<string, string[]> SectorKeywordMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["黄金"] = new[] { "黄金", "金价", "上海金", "伦敦金", "贵金属", "央行购金" },
            ["半导体"] = new[] { "半导体", "芯片", "集成电路", "先进制程", "光刻", "晶圆", "封测", "存储" },
            ["人工智能"] = new[] { "人工智能", "AI", "大模型", "算力", "机器人", "数据中心", "云计算" },
            ["科技"] = new[] { "科技", "互联网", "软件", "数字经济", "信创", "计算机" },
            ["新能源"] = new[] { "新能源", "光伏", "储能", "锂电", "固态电池", "风电", "电动车", "新能源汽车" },
            ["地产"] = new[] { "地产", "房地产", "楼市", "住房", "房贷", "深圳", "限购", "收储" },
            ["医药"] = new[] { "医药", "创新药", "医疗", "生物", "CRO", "疫苗", "医保", "器械" },
            ["军工"] = new[] { "军工", "国防", "航空", "航天", "导弹", "卫星", "无人机" },
            ["银行"] = new[] { "银行", "息差", "信贷", "贷款", "存款", "不良率" },
            ["证券"] = new[] { "券商", "证券", "资本市场", "两融", "并购重组", "IPO" },
            ["有色"] = new[] { "有色", "铜", "铝", "锂", "稀土", "钨", "钼", "矿", "金属" },
            ["油气"] = new[] { "原油", "石油", "油气", "OPEC", "WTI", "布伦特", "天然气" },
            ["消费"] = new[] { "消费", "白酒", "食品", "饮料", "家电", "旅游", "零售" },
            ["农业"] = new[] { "农业", "农产品", "粮食", "养殖", "猪肉", "种业", "牧渔" },
            ["债券"] = new[] { "债券", "国债", "利率债", "信用债", "收益率", "转债" },
            ["港股"] = new[] { "港股", "恒生", "南向", "香港", "中概股" },
            ["美股"] = new[] { "美股", "纳斯达克", "标普", "道指", "英伟达", "苹果", "微软" }
        };

        private static HashSet<string> BuildFundKeywords(MyFundConfig fund)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(fund.FundCode)) set.Add(fund.FundCode);
            var name = fund.FundName ?? string.Empty;
            string clean = NormalizeFundName(name)
                .Replace("ETF", "")
                .Replace("联接", "")
                .Replace("指数", "")
                .Replace("增强", "")
                .Replace("混合", "")
                .Replace("股票", "")
                .Replace("债券", "")
                .Replace("基金", "")
                .Replace("C", "")
                .Replace("A", "");

            string[] commonPrefixes = { "天弘", "华富", "国泰", "招商", "南方", "易方达", "博时", "工银瑞信", "中银", "华夏", "嘉实", "建信", "鹏华", "广发", "富国", "银华", "汇添富", "前海开源", "国投瑞银" };
            foreach (var prefix in commonPrefixes)
            {
                if (clean.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) clean = clean[prefix.Length..];
            }

            if (clean.Length >= 2) set.Add(clean);
            foreach (Match m in Regex.Matches(name, @"[\u4e00-\u9fa5A-Za-z0-9]{2,}"))
            {
                string token = m.Value.Trim();
                if (token.Length >= 2 && !new[] { "联接", "指数", "基金", "混合", "股票", "债券", "增强", "发起", "定开" }.Contains(token)) set.Add(token);
            }

            foreach (var kv in SectorKeywordMap)
            {
                if (kv.Value.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase) || clean.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var k in kv.Value) set.Add(k);
                    set.Add(kv.Key);
                }
            }

            foreach (var def in SectorDefinitions)
            {
                if (def.Include.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    set.Add(def.Name);
                    foreach (var k in def.Include) set.Add(k);
                }
            }
            return set;
        }

        private static (int score, string[] tags) ScoreNewsForFund(NewsItemDto news, MyFundConfig fund)
        {
            var keywords = BuildFundKeywords(fund);
            var text = (news.Title + " " + news.Summary + " " + string.Join(" ", news.Tags)).ToUpperInvariant();
            var hit = new List<string>();
            int score = 0;

            if (!string.IsNullOrWhiteSpace(fund.FundCode) && text.Contains(fund.FundCode.ToUpperInvariant()))
            {
                score += 100;
                hit.Add(fund.FundCode);
            }

            foreach (var kw in keywords)
            {
                if (kw.Length < 2) continue;
                if (text.Contains(kw.ToUpperInvariant()))
                {
                    score += Math.Min(30, kw.Length * 4);
                    hit.Add(kw);
                }
            }

            return (score, hit.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray());
        }

        [HttpGet("news")]
        public async Task<IActionResult> GetNews([FromQuery] string? username, [FromQuery] string mode = "global", [FromQuery] bool important = false, [FromQuery] int limit = 80, [FromQuery] bool force = false)
        {
            limit = Math.Clamp(limit, 20, 120);
            mode = string.IsNullOrWhiteSpace(mode) ? "global" : mode.Trim().ToLowerInvariant();
            string cacheKey = $"NewsV3_{mode}_{username}_{important}_{limit}";
            if (!force && _cache.TryGetValue(cacheKey, out object cached)) return Ok(cached);

            try
            {
                var allNews = await FetchEastMoneyFastNewsAsync(mode == "holding" ? Math.Max(limit * 3, 120) : limit);
                List<NewsItemDto> finalList;

                if (mode == "holding")
                {
                    if (string.IsNullOrWhiteSpace(username)) return Unauthorized("请提供指挥官代号");
                    var funds = await _context.MyFunds.AsNoTracking().Where(f => f.Username == username).ToListAsync();
                    var matched = new List<NewsItemDto>();

                    foreach (var news in allNews)
                    {
                        MyFundConfig? bestFund = null;
                        int bestScore = 0;
                        string[] bestTags = Array.Empty<string>();
                        foreach (var fund in funds)
                        {
                            var score = ScoreNewsForFund(news, fund);
                            if (score.score > bestScore)
                            {
                                bestScore = score.score;
                                bestFund = fund;
                                bestTags = score.tags;
                            }
                        }

                        if (bestFund != null && bestScore >= 16)
                        {
                            string text = news.Title + " " + news.Summary;
                            bool isImportant = news.Important || bestScore >= 60;
                            if (important && !isImportant) continue;
                            matched.Add(new NewsItemDto
                            {
                                Id = news.Id,
                                Title = news.Title,
                                Summary = news.Summary,
                                ShowTime = news.ShowTime,
                                TimeText = news.TimeText,
                                DateText = news.DateText,
                                Source = news.Source,
                                Url = news.Url,
                                Important = isImportant,
                                Sentiment = news.Sentiment,
                                ImpactScore = EstimateImpactScore(text, isImportant, bestScore),
                                MatchedFundCode = bestFund.FundCode,
                                MatchedFundName = bestFund.FundName,
                                Tags = bestTags,
                                Sort = news.Sort
                            });
                        }
                    }

                    finalList = matched.OrderByDescending(n => n.Important).ThenByDescending(n => n.Sort).Take(limit).ToList();
                }
                else
                {
                    finalList = allNews.Where(n => !important || n.Important).Take(limit).ToList();
                }

                var payload = new
                {
                    mode,
                    source = "东方财富7x24快讯",
                    updatedAt = ChinaNow().ToString("yyyy-MM-dd HH:mm:ss"),
                    count = finalList.Count,
                    items = finalList
                };
                _cache.Set(cacheKey, payload, TimeSpan.FromSeconds(mode == "holding" ? 90 : 45));
                return Ok(payload);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"资讯雷达故障: {ex.Message}");
            }
        }

        [HttpGet("holding-news")]
        public async Task<IActionResult> GetHoldingNews([FromQuery] string username, [FromQuery] bool important = false, [FromQuery] int limit = 80, [FromQuery] bool force = false)
        {
            return await GetNews(username, "holding", important, limit, force);
        }


        [HttpPost("save-archive")]
        public async Task<IActionResult> SaveArchive([FromBody] ArchiveRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Username)) return Unauthorized();

            try
            {
                var date = DateTime.Parse(req.DateStr).Date;
                var incoming = new List<DailyArchive>();

                if (req.Total != null)
                {
                    req.Total.FundCode = "TOTAL";
                    req.Total.FundName = string.IsNullOrWhiteSpace(req.Total.FundName) ? "总持仓" : req.Total.FundName;
                    incoming.Add(req.Total);
                }

                if (req.Funds != null)
                {
                    foreach (var f in req.Funds)
                    {
                        if (string.IsNullOrWhiteSpace(f.FundCode)) continue;
                        incoming.Add(f);
                    }
                }

                if (incoming.Count == 0) return BadRequest("封存数据为空");

                // 核心修复：不要因为 TOTAL 已存在就直接 return。
                // 旧逻辑会导致“总持仓有今日记录，但单只基金今日记录缺失”，也会保留已写坏的 TOTAL 金额。
                await UpsertDailyArchivesAsync(req.Username, date, incoming);
                await _context.SaveChangesAsync();
                ClearTodayCache(req.Username);

                return Ok($"✅ 已写入/修正 {incoming.Count} 条收益档案");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"封存失败: {ex.Message}");
            }
        }

        [HttpGet("auto-archive-nightly")]
        public async Task<IActionResult> AutoArchiveNightly()
        {
            try
            {
                var localTime = ChinaNow();
                var today = localTime.Date;

                // 周末不封存
                if (localTime.DayOfWeek == DayOfWeek.Saturday || localTime.DayOfWeek == DayOfWeek.Sunday)
                    return Ok("周末休市，无需封存。");
                var allFunds = await _context.MyFunds.ToListAsync();
                if (!allFunds.Any()) return Ok("无基金需要封存。");

                var userGroups = allFunds.GroupBy(f => f.Username);
                int savedCount = 0;
                foreach (var group in userGroups)
                {
                    string username = group.Key;
                    var userFunds = group.ToList();

                    // 🚀 终极碾压法则：夜间机器人的数据永远是最准的！绝对覆盖！
                    var existingRecords = await _context.DailyArchives
                        .Where(a => a.Username == username && a.RecordDate == today)
                        .ToListAsync();
                    if (existingRecords.Any())
                    {
                        //bool hasRealData = existingRecords.Any(r => r.FundCode == "TOTAL" && r.DailyRate != 0);
                        //if (hasRealData) continue; // 有真实数据才跳过
                        // 🚀 撤销保护锁，允许一天内无限次测试覆写
                        _context.DailyArchives.RemoveRange(existingRecords);
                        // 估值数据删掉重写
                    }

                    double totalAssets = 0;
                    double totalCost = 0;
                    double totalDailyProfit = 0; // 直接累加今日总收益，杜绝误差
                    double totalDailyBase = 0;   // 今日收益率分母：剥离/补回在途交易后的有效基数
                    double totalCurrentAssets = 0;
                    double totalRealized = 0;

                    // 获取今天的字符串，用于判断今日加仓
                    string todayStrSlash = today.ToString("yyyy/MM/dd");
                    string todayStrDash = today.ToString("yyyy-MM-dd");

                    foreach (var fund in userFunds)
                    {
                        totalAssets += fund.HoldAmount;
                        totalCost += (fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount);
                        totalRealized += fund.RealizedProfit; // 🚀 累加单只基金的落袋利润

                        var todayRecord = await _context.FundRecords
                            .Where(r => r.FundCode == fund.FundCode && r.FetchTime >= today)
                            .OrderByDescending(r => r.FetchTime)
                            .FirstOrDefaultAsync();

                        // 算钱逻辑 (基础粗略版)
                        double dailyRate = todayRecord != null && Math.Abs(todayRecord.ActualRate) > 0.000001
                            ? todayRecord.ActualRate
                            : (todayRecord?.EstimatedRate ?? 0);

                        // ==========================================
                        // 🚀 核心补丁：后端必须像前端一样，精准剥离今日新军！
                        // 绝对禁止未确认的资金参与今日收益结算，掐断虚假印钞！
                        double baseAmount = GetDailyBaseAmount(fund, todayStrDash);
                        totalDailyBase += baseAmount;
                        double dailyProfit = fund.LastSettledDate == todayStrDash
                            ? fund.LastSettledProfit
                            : baseAmount * (dailyRate / 100.0);
                        // ==========================================



                        // 🚀 终极纠缠态补丁：用金额反推真实份额，再呼叫猎隼侦察兵！
                        double effectiveShares = GetEffectiveShares(fund, todayStrDash);


                        // 🚀 核心对齐补丁 1：高精度物理对齐！(调用猎隼侦察兵)
                        var realData = await GetTodayRealRateAsync(fund.FundCode, today.ToString("yyyy-MM-dd"), effectiveShares);

                        if (fund.LastSettledDate == todayStrDash)
                        {
                            dailyRate = fund.LastSettledRate;
                            dailyProfit = fund.LastSettledProfit;
                        }
                        else
                        {
                            // 1. 突击指令：只要拿到了官方真实收益率，立刻覆盖！绝对不能被物理利润的计算结果绑架
                            if (realData.rate.HasValue)
                            {
                                dailyRate = realData.rate.Value;
                                dailyProfit = baseAmount * (dailyRate / 100.0);
                            }

                            // 2. 狙击指令：如果东方财富数据完整，算出了高精度的绝对物理利润，再执行覆盖
                            if (realData.exactProfit.HasValue)
                            {
                                dailyProfit = realData.exactProfit.Value;
                            }
                        }

                        // 🚀 算历史总收益 (包含单只基金的落袋利润)
                        double cost = fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount;
                        double currentAssets = fund.LastSettledDate == todayStrDash
                            ? fund.HoldAmount
                            : fund.HoldAmount + dailyProfit;
                        double totalProfit = currentAssets - cost + fund.RealizedProfit;
                        double totalRate = cost > 0 ? (totalProfit / cost * 100.0) : 0;
                        totalCurrentAssets += currentAssets;

                        // 自动归档只写 DailyArchives，不再修改 MyFunds.HoldAmount，避免和夜间清算重复滚存。

                        // 保存单只基金收益
                        _context.DailyArchives.Add(new DailyArchive
                        {
                            Username = username,
                            FundCode = fund.FundCode,
                            FundName = fund.FundName,
                            RecordDate = today,
                            Assets = Math.Round(currentAssets, 2), // 单只基金市值不含落袋，与前端一致
                            DailyProfit = Math.Round(dailyProfit, 2),
                            DailyRate = Math.Round(dailyRate, 2),
                            TotalProfit = Math.Round(totalProfit, 2),
                            TotalRate = Math.Round(totalRate, 2)
                        });
                        // 累加总阵地今日收益
                        totalDailyProfit += dailyProfit;
                    }

                    // 今日总收益率分母必须是当日有效持仓基数，不能用已结算后的市值重复稀释。
                    double totalDailyRate = totalDailyBase > 0 ? (totalDailyProfit / totalDailyBase) * 100.0 : 0;

                    double currentTotalAssetsAfter = totalCurrentAssets;

                    // 🚀 核心修复：把已落袋收益加回盈亏分子
                    double totalCampProfit = currentTotalAssetsAfter - totalCost + totalRealized;
                    double totalCampRate = totalCost > 0 ? (totalCampProfit / totalCost * 100.0) : 0;

                    // 总持仓市值只记录仍在持仓里的资产；落袋利润只进入累计盈亏分子。
                    double alignedTotalAssets = currentTotalAssetsAfter;

                    // 保存总持仓收益
                    _context.DailyArchives.Add(new DailyArchive
                    {
                        Username = username,
                        FundCode = "TOTAL",
                        FundName = "总持仓",
                        RecordDate = today,
                        Assets = alignedTotalAssets, // 对齐前端大屏的展示逻辑
                        DailyProfit = Math.Round(totalDailyProfit, 2),
                        DailyRate = Math.Round(totalDailyRate, 2),
                        TotalProfit = Math.Round(totalCampProfit, 2), // 已包含落袋利润
                        TotalRate = Math.Round(totalCampRate, 2)
                    });

                    savedCount++;
                }

                await _context.SaveChangesAsync();
                return Ok($"[{localTime:yyyy-MM-dd HH:mm:ss}] 🌙 夜间自动收益封存完毕！成功为 {savedCount} 位指挥官生成了历史档案。");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"自动封存引擎异常: {ex.Message}");
            }
        }

        [HttpGet("get-archives")]
        public async Task<IActionResult> GetArchives([FromQuery] string username, [FromQuery] string? fundCode = null, [FromQuery] int limit = 120)
        {
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized("缺少账号");
            if (limit <= 0) limit = 120;
            if (limit > 500) limit = 500;

            var query = _context.DailyArchives
                .AsNoTracking()
                .Where(a => a.Username == username);

            if (!string.IsNullOrWhiteSpace(fundCode))
            {
                query = query.Where(a => a.FundCode == fundCode);
            }

            var records = await query
                .OrderByDescending(a => a.RecordDate)
                .ThenByDescending(a => a.Id)
                .Take(limit)
                .Select(a => new
                {
                    fundCode = a.FundCode,
                    fundName = a.FundName,
                    recordDate = a.RecordDate,
                    assets = a.Assets,
                    dailyProfit = a.DailyProfit,
                    dailyRate = a.DailyRate,
                    totalProfit = a.TotalProfit,
                    totalRate = a.TotalRate
                })
                .ToListAsync();

            return Ok(records);
        }

        [HttpGet("fund-holdings")]
        public async Task<IActionResult> GetFundHoldings([FromQuery] string fundCode)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 13; SM-G981B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Mobile Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
            client.DefaultRequestHeaders.Add("Host", "push2his.eastmoney.com");
            try
            {
                string positionUrl = $"https://fundmobapi.eastmoney.com/FundMNewApi/FundMNInverstPosition?FCODE={fundCode}&deviceid=Wap&plat=Wap&product=EFund&version=2.0.0";
                string posRes = await client.GetStringAsync(positionUrl);
                using var posDoc = System.Text.Json.JsonDocument.Parse(posRes);

                JsonElement dataElement;
                if (posDoc.RootElement.TryGetProperty("Datas", out var datasObj) && datasObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = datasObj;
                else if (posDoc.RootElement.TryGetProperty("Data", out var dataObj) && dataObj.ValueKind != System.Text.Json.JsonValueKind.Null)
                    dataElement = dataObj;
                else
                    return Ok(new List<object>());
                JsonElement fundPosition;
                if (dataElement.TryGetProperty("fundStocks", out var stocksObj) && stocksObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = stocksObj;
                else if (dataElement.TryGetProperty("fundPosition", out var posObj) && posObj.ValueKind == System.Text.Json.JsonValueKind.Array)
                    fundPosition = posObj;
                else
                    return Ok(new List<object>());
                var stockList = new List<dynamic>();
                var secidList = new List<string>();
                foreach (var stock in fundPosition.EnumerateArray())
                {
                    string code = stock.TryGetProperty("GPDM", out var cProp) ? cProp.GetString() : "";
                    string name = stock.TryGetProperty("GPJC", out var nProp) ? nProp.GetString() : "";
                    string ratio = stock.TryGetProperty("JZBL", out var rProp) ? rProp.GetString() : "0";
                    if (!string.IsNullOrEmpty(code))
                    {
                        string prefix = "0.";
                        if (code.StartsWith("6")) prefix = "1.";
                        else if (code.Length == 5) prefix = "116.";

                        secidList.Add(prefix + code);
                        stockList.Add(new { code, name, ratio, rate = 0.0 });
                    }
                }

                if (secidList.Count > 0)
                {
                    string secids = string.Join(",", secidList);
                    string quoteUrl = $"http://push2.eastmoney.com/api/qt/ulist.np/get?secids={secids}&fields=f12,f14,f3";

                    using var quoteClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    quoteClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

                    string quoteRes = await quoteClient.GetStringAsync(quoteUrl);
                    using var quoteDoc = System.Text.Json.JsonDocument.Parse(quoteRes);

                    if (quoteDoc.RootElement.TryGetProperty("data", out var qData) && qData.ValueKind != System.Text.Json.JsonValueKind.Null)
                    {
                        if (qData.TryGetProperty("diff", out var diffs) && diffs.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var rateDict = new Dictionary<string, double>();
                            foreach (var diff in diffs.EnumerateArray())
                            {
                                string c = diff.TryGetProperty("f12", out var f12) ? f12.GetString() : "";
                                double r = 0;
                                if (diff.TryGetProperty("f3", out var f3) && f3.ValueKind == System.Text.Json.JsonValueKind.Number)
                                {
                                    r = Math.Round(f3.GetDouble() / 100.0, 2);
                                }
                                if (!string.IsNullOrEmpty(c)) rateDict[c] = r;
                            }

                            var finalResult = stockList.Select(s => new
                            {
                                code = s.code,
                                name = s.name,
                                ratio = s.ratio,
                                rate = rateDict.ContainsKey((string)s.code) ? rateDict[(string)s.code] : 0.0
                            }).ToList();

                            return Ok(finalResult);
                        }
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