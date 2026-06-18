namespace 小白养基.Services
{
    public sealed record ConfirmedHoldingMoney(
        decimal ConfirmedAmount,
        decimal YesterdayProfit,
        decimal HoldingProfit);

    public sealed record PortfolioAccountingSummary(
        decimal AntConfirmedAmount,
        decimal ConfirmedYesterdayProfit,
        decimal AntHoldingProfit,
        decimal AntHoldingCost,
        decimal AntHoldingRate,
        decimal IntradayEstimateProfit,
        decimal IntradayEstimateRate,
        decimal IntradayEstimatedAssets,
        decimal EstimatedHoldingProfit);

    public static class PortfolioAccounting
    {
        public static decimal Money(decimal value)
            => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

        public static decimal Money(double value)
            => Money(Convert.ToDecimal(value));

        public static double ToDouble(decimal value)
            => Convert.ToDouble(Money(value));

        public static decimal Percent(decimal profit, decimal baseAmount)
        {
            if (baseAmount <= 0) return 0;
            return decimal.Round(profit / baseAmount * 100m, 2, MidpointRounding.AwayFromZero);
        }

        public static decimal HoldingCost(decimal confirmedAmount, decimal holdingProfit)
            => Math.Max(0m, Money(confirmedAmount - holdingProfit));

        public static decimal HoldingProfitRate(decimal holdingProfit, decimal confirmedAmount)
            => Percent(Money(holdingProfit), HoldingCost(confirmedAmount, holdingProfit));

        public static decimal PortfolioTodayEstimateRate(decimal intradayEstimateProfit, decimal antConfirmedAmount)
            => Percent(Money(intradayEstimateProfit), Money(antConfirmedAmount));

        public static decimal OfficialTodayProfit(decimal todayBaseAmount, decimal todayRate, decimal? settledProfit = null)
            => settledProfit.HasValue
                ? Money(settledProfit.Value)
                : Money(Money(todayBaseAmount) * todayRate / 100m);

        public static DateTime ResolvePreviousWeekday(DateTime chinaDate)
        {
            var previous = chinaDate.Date.AddDays(-1);
            while (previous.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                previous = previous.AddDays(-1);
            }
            return previous;
        }

        public static PortfolioAccountingSummary Calculate(
            IEnumerable<ConfirmedHoldingMoney> holdings,
            decimal intradayEstimateProfit)
        {
            var rows = holdings.ToList();
            var confirmedAmount = Money(rows.Sum(x => Money(x.ConfirmedAmount)));
            var yesterdayProfit = Money(rows.Sum(x => Money(x.YesterdayProfit)));
            var holdingProfit = Money(rows.Sum(x => Money(x.HoldingProfit)));
            var holdingCost = HoldingCost(confirmedAmount, holdingProfit);
            var estimateProfit = Money(intradayEstimateProfit);

            return new PortfolioAccountingSummary(
                confirmedAmount,
                yesterdayProfit,
                holdingProfit,
                holdingCost,
                Percent(holdingProfit, holdingCost),
                estimateProfit,
                PortfolioTodayEstimateRate(estimateProfit, confirmedAmount),
                Money(confirmedAmount + estimateProfit),
                Money(holdingProfit + estimateProfit));
        }
    }
}
