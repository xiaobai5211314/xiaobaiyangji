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

        public static decimal ResolveAccountTotalAmount(
            decimal snapshotDisplayAmount,
            decimal confirmedAmount,
            decimal pendingBuyAmount,
            bool useCurrentSnapshotSummary,
            bool antConfirmedAvailable)
        {
            if (useCurrentSnapshotSummary)
                return Money(snapshotDisplayAmount);

            if (antConfirmedAvailable)
                return Money(Money(confirmedAmount) + Money(pendingBuyAmount));

            return Money(snapshotDisplayAmount);
        }

        public static decimal ResolveSettledDisplayAmount(
            decimal baseAmount,
            decimal settledProfit,
            decimal activePendingBuyAmount,
            decimal? exactConfirmedAssets = null)
        {
            var confirmedAssets = exactConfirmedAssets.HasValue && exactConfirmedAssets.Value > 0m
                ? Money(exactConfirmedAssets.Value)
                : Money(Money(baseAmount) + Money(settledProfit));

            return Money(Math.Max(0m, confirmedAssets) + Math.Max(0m, Money(activePendingBuyAmount)));
        }

        public static decimal ResolveOfficialHoldingProfit(
            decimal currentAssets,
            decimal costAmount,
            decimal realizedProfit = 0m,
            decimal fallbackHoldingProfit = 0m)
        {
            var assets = Money(currentAssets);
            var cost = Money(costAmount);
            if (cost > 0m)
                return Money(assets - cost + Money(realizedProfit));

            return Money(fallbackHoldingProfit);
        }

        public static bool IsOcrSnapshotFreshForArchive(
            string? snapshotDate,
            string? confirmedProfitDate,
            DateTime? archiveRecordDate)
        {
            if (!DateTime.TryParse(snapshotDate, out var capturedAt)) return false;
            if (!archiveRecordDate.HasValue) return true;

            if (DateTime.TryParse(confirmedProfitDate, out var confirmedAt)
                && confirmedAt.Date >= archiveRecordDate.Value.Date)
            {
                return true;
            }

            return capturedAt.Date > archiveRecordDate.Value.Date;
        }

        public static DateTime ResolvePreviousWeekday(DateTime chinaDate)
        {
            return MarketCalendar.GetPreviousTradingDate(chinaDate.Date.AddDays(-1));
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
