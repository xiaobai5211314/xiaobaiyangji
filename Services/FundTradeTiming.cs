using System.Text.RegularExpressions;

namespace 小白养基.Services
{
    // Rule sources:
    // - T-day cutoff at 15:00 and holiday rollover: https://help.1234567.com.cn/question_243.html
    // - Common open-end fund T+1 and QDII T+2 examples: https://www.chinaamc.com/c/2014-02-21/601825.shtml
    // - QDII/FOF confirmation may differ by product; OCR/extracted platform dates must override this default.
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
            var tradeDate = ResolveTradeDate(submitDate, afterCutoff, market);
            var confirmDate = MarketCalendar.AddTradingDays(tradeDate, confirmTradingDays, market);
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
