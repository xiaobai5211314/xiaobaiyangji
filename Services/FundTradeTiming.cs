using System.Text.RegularExpressions;

namespace 小白养基.Services
{
    // Rule sources:
    // - 15:00 cutoff and ordinary T+1 confirmation:
    //   https://www.csrc.gov.cn/shenzhen/c105614/c7602071/content.shtml
    // - Product-specific query/confirmation examples:
    //   https://m.chinaamc.com/cjwt/gmwt/5501300.shtml
    // QDII/FOF dates differ by product. Platform/OCR dates always override this estimate.
    public sealed record FundTradeTimingResult(
        string TradeDate,
        string ConfirmDate,
        string FirstProfitDate,
        string Market,
        int ConfirmTradingDays);

    public static class FundTradeTiming
    {
        public static string DetectMarket(string? fundName)
        {
            var text = fundName ?? string.Empty;
            if (Regex.IsMatch(text, @"恒生|港股|香港", RegexOptions.IgnoreCase)) return "hk";
            if (Regex.IsMatch(text, @"QDII|海外|全球|美元|纳斯达克|标普|日经", RegexOptions.IgnoreCase)) return "us";
            return "cn";
        }

        public static int ConfirmTradingDays(string? fundName)
        {
            var text = fundName ?? string.Empty;
            if (Regex.IsMatch(text, @"FOF", RegexOptions.IgnoreCase)) return 3;
            if (Regex.IsMatch(text, @"QDII|恒生|港股|海外|全球|美元|纳斯达克|标普|日经", RegexOptions.IgnoreCase)) return 2;
            return 1;
        }

        public static DateTime ResolveTradeDate(DateTime submitDate, bool afterCutoff, string market = "cn")
        {
            var start = submitDate.Date;
            if (afterCutoff || !MarketCalendar.IsTradingDay(start, market))
            {
                start = start.AddDays(1);
            }

            return MarketCalendar.GetNextTradingDate(start, market);
        }

        public static FundTradeTimingResult Resolve(DateTime submitDate, bool afterCutoff, string? fundName)
        {
            var market = DetectMarket(fundName);
            var confirmTradingDays = ConfirmTradingDays(fundName);
            // OTC fund sales use the domestic sales-day calendar as the safe default.
            // Overseas-market opening rules are product-specific and cannot be inferred from a name.
            var tradeDate = ResolveTradeDate(submitDate, afterCutoff, "cn");
            var confirmDate = MarketCalendar.AddTradingDays(tradeDate, confirmTradingDays, "cn");
            var firstProfitDate = confirmDate;

            return new FundTradeTimingResult(
                tradeDate.ToString("yyyy-MM-dd"),
                confirmDate.ToString("yyyy-MM-dd"),
                firstProfitDate.ToString("yyyy-MM-dd"),
                market,
                confirmTradingDays);
        }
    }
}
