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
        private readonly record struct NumberToken(string Raw, decimal Value, bool IsPercent, bool HasExplicitSign);

        private static readonly Regex CodeRegex = new(@"(?<!\d)([0-9]{6})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new(@"[-+]?\d+(?:,\d{3})*(?:\.\d+)?%?", RegexOptions.Compiled);
        private static readonly Regex ChineseNameRegex = new(@"^[\u4e00-\u9fa5A-Za-z0-9（）()·\-]{2,12}$", RegexOptions.Compiled);

        private static readonly string[] HoldingAnchors =
        {
            "持仓股", "股票持仓", "持有股票", "持仓股票", "持仓明细"
        };

        private static readonly string[] BlockEndAnchors =
        {
            "相关基金", "持仓管理", "批量买入", "批量卖出", "止盈止损", "持仓资讯", "资产分析", "查看已清仓"
        };

        private static readonly string[] ExcludedWords =
        {
            "KB/s", "KBS", "WW", "--", "-", "中国银河证券", "银河证券", "同花顺", "东方财富", "涨乐财富通",
            "买入", "卖出", "撤单", "持仓", "查询", "自选", "搜索", "搜索股票", "加自选", "交易", "行情",
            "人民币账户", "A股", "仓位", "总资产", "总市值", "浮动盈亏", "当日参考盈亏", "当日盈亏",
            "可用", "可取", "逆回购", "转账", "市值", "盈亏", "持仓/可用", "成本/现价", "成本", "现价",
            "均价", "最新", "首页", "理财", "资讯", "会员", "我的", "账户汇总", "默认账户", "新增持有",
            "分时", "五日", "日K", "周K", "月K", "年K", "成交量", "相关基金"
        };

        public IReadOnlyList<StockOcrCandidate> Parse(IReadOnlyList<string> words)
        {
            var cleaned = words
                .Select(NormalizeText)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (cleaned.Count == 0) return Array.Empty<StockOcrCandidate>();

            var result = ParseCodeCandidates(cleaned);
            if (result.Count > 0) return result;

            // 券商截图经常不露股票代码，只露股票名称。此时必须限制在“持仓股”表格区域，避免把券商名、按钮、逆回购等 UI 文案当股票。
            return ParseNameCandidatesFromHoldingBlock(cleaned);
        }

        private static List<StockOcrCandidate> ParseCodeCandidates(IReadOnlyList<string> cleaned)
        {
            var result = new List<StockOcrCandidate>();
            var seen = new HashSet<string>();

            for (var i = 0; i < cleaned.Count; i++)
            {
                var line = cleaned[i];
                var codeMatch = CodeRegex.Match(line);
                if (!codeMatch.Success) continue;

                var code = codeMatch.Groups[1].Value;
                if (!seen.Add(code)) continue;

                var windowStart = Math.Max(0, i - 5);
                var windowEnd = Math.Min(cleaned.Count - 1, i + 12);
                var window = cleaned.Skip(windowStart).Take(windowEnd - windowStart + 1).ToList();
                var name = GuessName(window, code);

                result.Add(BuildCandidateFromHoldingWindow(code, name, name, window, "按 OCR 邻近文本推断，确认后写库"));
            }

            return result;
        }

        private static IReadOnlyList<StockOcrCandidate> ParseNameCandidatesFromHoldingBlock(IReadOnlyList<string> cleaned)
        {
            var start = -1;
            for (var i = 0; i < cleaned.Count; i++)
            {
                if (HoldingAnchors.Any(anchor => cleaned[i].Contains(anchor)))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0) return Array.Empty<StockOcrCandidate>();

            var end = cleaned.Count;
            for (var i = start + 1; i < cleaned.Count; i++)
            {
                if (BlockEndAnchors.Any(anchor => cleaned[i].Contains(anchor)))
                {
                    end = i;
                    break;
                }
            }

            var block = cleaned.Skip(start + 1).Take(Math.Max(0, end - start - 1)).ToList();
            var result = new List<StockOcrCandidate>();
            var seen = new HashSet<string>();

            for (var i = 0; i < block.Count; i++)
            {
                var text = block[i];
                if (!IsLikelyStockName(text)) continue;
                if (!seen.Add(text)) continue;

                var tail = block.Skip(i + 1).Take(18).ToList();
                var tokens = ExtractNumberTokens(tail).ToList();
                if (!HasHoldingNumericEvidence(tokens.Select(x => x.Raw).ToList())) continue;

                result.Add(BuildCandidateFromHoldingWindow(
                    code: string.Empty,
                    name: text,
                    recognizedName: text,
                    lines: tail,
                    note: "未在截图中识别到代码，已按持仓表格区域提取名称；请确认代码后写库"));

                if (result.Count >= 5) break;
            }

            return result;
        }

        private static StockOcrCandidate BuildCandidateFromHoldingWindow(string code, string name, string recognizedName, IReadOnlyList<string> lines, string note)
        {
            var tokens = ExtractNumberTokens(lines).ToList();

            var marketIndex = tokens.FindIndex(x => x.Value >= 100 && x.Value < 1000000000 && !x.IsPercent);
            decimal? marketValue = marketIndex >= 0 ? tokens[marketIndex].Value : GuessMarketValue(tokens.Select(x => x.Raw));

            var profitIndex = -1;
            if (marketIndex >= 0)
            {
                profitIndex = tokens.FindIndex(marketIndex + 1, x => !x.IsPercent && x.HasExplicitSign && Math.Abs(x.Value) >= 0.01m && Math.Abs(x.Value) < 100000000);
            }
            if (profitIndex < 0)
            {
                profitIndex = tokens.FindIndex(x => !x.IsPercent && x.HasExplicitSign && Math.Abs(x.Value) >= 0.01m && Math.Abs(x.Value) < 100000000);
            }
            decimal? floatingProfit = profitIndex >= 0 ? tokens[profitIndex].Value : GuessFloatingProfit(tokens.Select(x => x.Raw));

            var rateIndex = -1;
            if (profitIndex >= 0)
            {
                rateIndex = tokens.FindIndex(profitIndex + 1, x => x.IsPercent && Math.Abs(x.Value) < 1000);
            }
            if (rateIndex < 0) rateIndex = tokens.FindIndex(x => x.IsPercent && Math.Abs(x.Value) < 1000);
            decimal? floatingProfitRate = rateIndex >= 0 ? tokens[rateIndex].Value : GuessRate(tokens.Select(x => x.Raw));

            var sharesIndex = -1;
            if (rateIndex >= 0)
            {
                sharesIndex = tokens.FindIndex(rateIndex + 1, x => IsLikelyShareCount(x.Value));
            }
            if (sharesIndex < 0) sharesIndex = tokens.FindIndex(x => IsLikelyShareCount(x.Value));
            decimal? shares = sharesIndex >= 0 ? tokens[sharesIndex].Value : GuessShares(tokens.Select(x => x.Raw));

            var priceStart = sharesIndex >= 0 ? sharesIndex + 1 : 0;
            if (sharesIndex >= 0 && sharesIndex + 1 < tokens.Count && tokens[sharesIndex + 1].Value == tokens[sharesIndex].Value)
            {
                // 券商截图经常是“持仓/可用”两列，例如 600 / 600；成本价在这两个数量后面。
                priceStart = sharesIndex + 2;
            }

            var costPriceIndex = tokens.FindIndex(priceStart, x => IsLikelyPrice(x.Value));
            decimal? costPrice = costPriceIndex >= 0 ? tokens[costPriceIndex].Value : GuessCostPrice(tokens.Select(x => x.Raw));

            var costAmount = shares.HasValue && costPrice.HasValue
                ? Math.Round(shares.Value * costPrice.Value, 2)
                : (decimal?)null;

            return new StockOcrCandidate(
                StockCode: code,
                StockName: name,
                RecognizedName: recognizedName,
                Shares: shares,
                CostPrice: costPrice,
                CostAmount: costAmount,
                MarketValue: marketValue,
                FloatingProfit: floatingProfit,
                FloatingProfitRate: floatingProfitRate,
                Action: shares.GetValueOrDefault() > 0 || marketValue.GetValueOrDefault() > 0 ? "holding" : "watch",
                Note: note);
        }

        private static IEnumerable<NumberToken> ExtractNumberTokens(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                foreach (Match match in NumberRegex.Matches(line))
                {
                    var raw = match.Value;
                    var value = ParsePlainNumber(raw);
                    if (!value.HasValue) continue;

                    yield return new NumberToken(
                        Raw: raw,
                        Value: value.Value,
                        IsPercent: raw.EndsWith('%'),
                        HasExplicitSign: raw.StartsWith('+') || raw.StartsWith('-'));
                }
            }
        }

        private static bool IsLikelyShareCount(decimal value)
        {
            return value >= 1 && value <= 100000000 && value % 1 == 0;
        }

        private static bool IsLikelyPrice(decimal value)
        {
            return value > 0 && value < 10000 && value % 1 != 0;
        }

        private static string GuessName(IEnumerable<string> window, string code)
        {
            var candidate = window
                .Select(x => NormalizeText(x.Replace(code, string.Empty)))
                .FirstOrDefault(IsLikelyStockName);
            return string.IsNullOrWhiteSpace(candidate) ? code : candidate;
        }

        private static bool IsLikelyStockName(string text)
        {
            text = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length < 2 || text.Length > 12) return false;
            if (!ChineseNameRegex.IsMatch(text)) return false;
            if (CodeRegex.IsMatch(text)) return false;
            if (NumberRegex.IsMatch(text)) return false;
            if (ExcludedWords.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase))) return false;
            if (text.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) return false;
            if (text.Count(char.IsDigit) > 0 && !text.Any(IsChinese)) return false;
            return true;
        }

        private static bool HasHoldingNumericEvidence(IReadOnlyList<string> numbers)
        {
            if (numbers.Count < 4) return false;
            var plain = numbers.Select(ParsePlainNumber).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var hasShares = plain.Any(x => x >= 1 && x % 1 == 0 && x <= 100000000);
            var hasPrice = plain.Any(x => x > 0 && x < 10000 && x % 1 != 0);
            var hasValue = plain.Any(x => Math.Abs(x) >= 100 && Math.Abs(x) < 1000000000);
            var hasRate = numbers.Any(x => x.EndsWith('%'));
            return hasValue && (hasShares || hasPrice || hasRate);
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
                .Where(x => x.HasValue && x.Value > 0 && x.Value < 10000 && x.Value % 1 != 0)
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

        private static string NormalizeText(string raw)
        {
            return (raw ?? string.Empty)
                .Replace("：", ":")
                .Replace("／", "/")
                .Replace(" ", string.Empty)
                .Trim();
        }

        private static bool IsChinese(char c) => c >= '\u4e00' && c <= '\u9fa5';
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
