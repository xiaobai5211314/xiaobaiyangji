using 小白养基.Services;

static void Equal(decimal expected, decimal actual, string name)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{name}: expected {expected:F2}, actual {actual:F2}");
    }
}

var holdings = new[]
{
    new ConfirmedHoldingMoney(36735.47m, -441.16m, -4116.80m),
    new ConfirmedHoldingMoney(31497.93m, -733.09m, -3222.50m),
    new ConfirmedHoldingMoney(14447.98m, -194.60m, -552.02m),
    new ConfirmedHoldingMoney(55.00m, -1.02m, -45.00m),
    new ConfirmedHoldingMoney(15.23m, 0.10m, 6.20m),
    new ConfirmedHoldingMoney(3.34m, -0.01m, -6.66m)
};

var summary = PortfolioAccounting.Calculate(holdings, 913.23m);

Equal(-1369.78m, summary.ConfirmedYesterdayProfit, "calendar[2026-06-11].dailyPnl");
Equal(82754.95m, summary.AntConfirmedAmount, "summary.antConfirmedAmount");
Equal(-7936.78m, summary.AntHoldingProfit, "summary.antHoldingProfit");
Equal(913.23m, summary.IntradayEstimateProfit, "summary.intradayEstimateProfit");
Equal(83668.18m, summary.IntradayEstimatedAssets, "summary.intradayEstimatedAssets");
Equal(-7023.55m, summary.EstimatedHoldingProfit, "summary.estimatedHoldingProfit");

var profitDate = PortfolioAccounting.ResolvePreviousWeekday(new DateTime(2026, 6, 12));
if (profitDate != new DateTime(2026, 6, 11))
{
    throw new InvalidOperationException($"profitDate: expected 2026-06-11, actual {profitDate:yyyy-MM-dd}");
}

Console.WriteLine("Portfolio accounting regression passed: 7 assertions.");
