using System.Text.RegularExpressions;

namespace 估值助手.Services
{
    public record StockOcrCandidate(
        string StockCode,
        string StockName,
        string RecognizedName,
        decimal? Shares,
        decimal? CostPrice,
        decimal? CostAmount,
        decimal? MarketValue,
        decimal? FloatingProfit,
        decimal? FloatingProfitRate,
        string Action,
        string Note);

    public sealed class StockOcrParserService
    {
        private static readonly Regex CodeRegex = new(@"(?<!\d)([0-9]{6})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new(@"[-+]?\d+(?:,\d{3})*(?:\.\d+)?%?", RegexOptions.Compiled);

        public IReadOnlyList<StockOcrCandidate> Parse(IReadOnlyList<string> words)
        {
            var cleaned = words
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (cleaned.Count == 0) return Array.Empty<StockOcrCandidate>();

            var result = new List<StockOcrCandidate>();
            var seen = new HashSet<string>();

            for (var i = 0; i < cleaned.Count; i++)
            {
                var line = cleaned[i];
                var codeMatch = CodeRegex.Match(line);
                if (!codeMatch.Success) continue;

                var code = codeMatch.Groups[1].Value;
                if (!seen.Add(code)) continue;

                var windowStart = Math.Max(0, i - 4);
                var windowEnd = Math.Min(cleaned.Count - 1, i + 8);
                var window = cleaned.Skip(windowStart).Take(windowEnd - windowStart + 1).ToList();
                var name = GuessName(window, code);
                var numbers = window.SelectMany(x => NumberRegex.Matches(x).Select(m => m.Value)).ToList();

                var shares = GuessShares(numbers);
                var marketValue = GuessMarketValue(numbers);
                var costPrice = GuessCostPrice(numbers);
                var floatingProfit = GuessFloatingProfit(numbers);
                var floatingProfitRate = GuessRate(numbers);
                var costAmount = shares.HasValue && costPrice.HasValue ? Math.Round(shares.Value * costPrice.Value, 2) : (decimal?)null;

                result.Add(new StockOcrCandidate(
                    StockCode: code,
                    StockName: name,
                    RecognizedName: name,
                    Shares: shares,
                    CostPrice: costPrice,
                    CostAmount: costAmount,
                    MarketValue: marketValue,
                    FloatingProfit: floatingProfit,
                    FloatingProfitRate: floatingProfitRate,
                    Action: shares.GetValueOrDefault() > 0 || marketValue.GetValueOrDefault() > 0 ? "holding" : "watch",
                    Note: "按 OCR 邻近文本推断，确认后写库"));
            }

            // 有些同花顺持仓截图不露代码，只露股票名。先返回名称候选，前端可搜索补代码。
            if (result.Count == 0)
            {
                foreach (var name in cleaned.Where(IsLikelyStockName).Distinct().Take(8))
                {
                    result.Add(new StockOcrCandidate("", name, name, null, null, null, null, null, null, "watch", "未识别到代码，请搜索确认后加入自选或持仓"));
                }
            }

            return result;
        }

        private static string GuessName(IEnumerable<string> window, string code)
        {
            var candidate = window
                .Select(x => x.Replace(code, string.Empty).Trim())
                .FirstOrDefault(IsLikelyStockName);
            return string.IsNullOrWhiteSpace(candidate) ? code : candidate;
        }

        private static bool IsLikelyStockName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < 2 || text.Length > 12) return false;
            if (CodeRegex.IsMatch(text)) return false;
            if (NumberRegex.IsMatch(text)) return false;
            var excludes = new[] { "持仓", "查询", "买入", "卖出", "撤单", "成本", "现价", "市值", "盈亏", "收益", "账户", "资金", "可用", "可取", "总资产", "搜索股票" };
            return !excludes.Any(text.Contains);
        }

        private static decimal? GuessShares(IEnumerable<string> numbers)
        {
            return numbers.Select(ParsePlainNumber)
                .Where(x => x.HasValue && x.Value >= 1 && x.Value % 1 == 0 && x.Value <= 100000000)
                .Select(x => x!.Value)
                .FirstOrDefaultOrNull();
        }

        private static decimal? GuessCostPrice(IEnumerable<string> numbers)
        {
            return numbers.Select(ParsePlainNumber)
                .Where(x => x.HasValue && x.Value > 0 && x.Value < 10000)
                .Select(x => x!.Value)
                .OrderBy(x => x)
                .FirstOrDefaultOrNull();
        }

        private static decimal? GuessMarketValue(IEnumerable<string> numbers)
        {
            return numbers.Select(ParsePlainNumber)
                .Where(x => x.HasValue && x.Value > 100 && x.Value < 1000000000)
                .Select(x => x!.Value)
                .OrderByDescending(x => x)
                .FirstOrDefaultOrNull();
        }

        private static decimal? GuessFloatingProfit(IEnumerable<string> numbers)
        {
            return numbers.Select(ParsePlainNumber)
                .Where(x => x.HasValue && Math.Abs(x.Value) > 1 && Math.Abs(x.Value) < 100000000)
                .Select(x => x!.Value)
                .OrderBy(x => Math.Abs(x))
                .FirstOrDefaultOrNull();
        }

        private static decimal? GuessRate(IEnumerable<string> numbers)
        {
            return numbers.Where(x => x.EndsWith('%'))
                .Select(ParsePlainNumber)
                .FirstOrDefault(x => x.HasValue && Math.Abs(x.Value) < 1000);
        }

        private static decimal? ParsePlainNumber(string raw)
        {
            raw = (raw ?? string.Empty).Replace(",", string.Empty).Replace("%", string.Empty).Trim();
            return decimal.TryParse(raw, out var value) ? value : null;
        }
    }

    internal static class EnumerableDecimalExtensions
    {
        public static decimal? FirstOrDefaultOrNull(this IEnumerable<decimal> values)
        {
            foreach (var value in values) return value;
            return null;
        }
    }
}
