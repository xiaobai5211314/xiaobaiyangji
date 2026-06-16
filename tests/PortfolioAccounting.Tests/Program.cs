using 小白养基.Services;
using 小白养基.Models;

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

var currentSummary = PortfolioAccounting.Calculate(
    new[] { new ConfirmedHoldingMoney(81903.46m, 0m, -7800.74m) },
    -492.66m);
Equal(-0.60m, currentSummary.IntradayEstimateRate, "summary.portfolioTodayEstimateRate");
Equal(89704.20m, currentSummary.AntHoldingCost, "summary.holdingCost");
Equal(-8.70m, currentSummary.AntHoldingRate, "summary.holdingProfitRate");

Equal(-8.50m, PortfolioAccounting.Percent(-3222.50m, 37916.92m), "fund.huafu.holdingProfitRate");
Equal(-11.23m, PortfolioAccounting.Percent(-4116.80m, 36668.25m), "fund.tianhong.holdingProfitRate");
Equal(68.66m, PortfolioAccounting.Percent(6.20m, 9.03m), "fund.semiconductor.holdingProfitRate");
Equal(-66.60m, PortfolioAccounting.Percent(-6.66m, 10.00m), "fund.realEstate.holdingProfitRate");
Equal(-0.26m, PortfolioAccounting.PortfolioTodayEstimateRate(-211.27m, 81556.13m), "summary.confirmedTodayRate");

var june15OcrSummary = PortfolioAccounting.Calculate(
    new[] { new ConfirmedHoldingMoney(83445.65m, 1889.51m, -6258.55m) },
    0m);
Equal(83445.65m, june15OcrSummary.AntConfirmedAmount, "ocr.currentAmount.mustNotSubtractYesterdayIncome");
Equal(1889.51m, june15OcrSummary.ConfirmedYesterdayProfit, "ocr.yesterdayIncome");
Equal(-6258.55m, june15OcrSummary.AntHoldingProfit, "ocr.holdingIncome.mustNotSubtractYesterdayIncome");

var selectedLatestTotal = DailyArchiveService.PickLatestPortfolioSummaryTotal(new[]
{
    new DailyArchive
    {
        FundCode = "TOTAL",
        RecordDate = new DateTime(2026, 6, 11),
        Assets = 81556.13,
        DailyProfit = -1369.78,
        TotalProfit = -8148.07,
        Source = "alipay-confirmed-total",
        IsFinal = true,
        UpdatedAt = new DateTime(2026, 6, 12, 13, 35, 48)
    },
    new DailyArchive
    {
        FundCode = "TOTAL",
        RecordDate = new DateTime(2026, 6, 15),
        Assets = 83445.65,
        DailyProfit = 1889.51,
        TotalProfit = -6258.55,
        Source = "official-nav-pending-total",
        IsFinal = false,
        UpdatedAt = new DateTime(2026, 6, 16, 8, 30, 0)
    }
});
if (selectedLatestTotal?.RecordDate != new DateTime(2026, 6, 15))
    throw new InvalidOperationException("summary.latestArchive: expected 2026-06-15 to win over older confirmed archive");
Equal(83445.65m, PortfolioAccounting.Money(selectedLatestTotal.Assets), "summary.latestArchive.assets");
Equal(1889.51m, PortfolioAccounting.Money(selectedLatestTotal.DailyProfit), "summary.latestArchive.dailyProfit");

var sameDayPreferredConfirmed = DailyArchiveService.PickLatestPortfolioSummaryTotal(new[]
{
    new DailyArchive
    {
        FundCode = "TOTAL",
        RecordDate = new DateTime(2026, 6, 15),
        Assets = 83445.64,
        DailyProfit = 1889.51,
        TotalProfit = -6258.56,
        Source = "official-nav-pending-total",
        IsFinal = false,
        UpdatedAt = new DateTime(2026, 6, 16, 8, 30, 0)
    },
    new DailyArchive
    {
        FundCode = "TOTAL",
        RecordDate = new DateTime(2026, 6, 15),
        Assets = 83445.65,
        DailyProfit = 1889.51,
        TotalProfit = -6258.55,
        Source = "alipay-confirmed-total",
        IsFinal = true,
        UpdatedAt = new DateTime(2026, 6, 16, 9, 0, 0)
    }
});
if (sameDayPreferredConfirmed == null || !DailyArchiveService.IsAntConfirmedSource(sameDayPreferredConfirmed.Source))
    throw new InvalidOperationException("summary.sameDay: alipay confirmed archive should win over same-day pending archive");

var pendingNavSummary = PortfolioAccounting.Calculate(new[]
{
    new ConfirmedHoldingMoney(15.01m, -0.11m, 5.98m),
    new ConfirmedHoldingMoney(33170.49m, 309.52m, -3497.76m),
    new ConfirmedHoldingMoney(3.36m, 0.02m, -6.64m),
    new ConfirmedHoldingMoney(33379.62m, -657.40m, -4537.30m),
    new ConfirmedHoldingMoney(14720.05m, 136.03m, -279.95m),
    new ConfirmedHoldingMoney(56.33m, 0.67m, -43.67m)
}, 0m);
Equal(-211.27m, pendingNavSummary.ConfirmedYesterdayProfit, "calendar[2026-06-12].pendingNavProfit");
Equal(81344.86m, pendingNavSummary.AntConfirmedAmount, "calendar[2026-06-12].pendingNavAssets");
if (!DailyArchiveService.IsOfficialNavPendingSource("official-nav-pending-total"))
    throw new InvalidOperationException("official-nav-pending-total should be recognized as pending NAV source");
if (DailyArchiveService.IsAntConfirmedSource("official-nav-pending-total"))
    throw new InvalidOperationException("official NAV pending source must not be treated as Ant confirmed");

var profitDate = PortfolioAccounting.ResolvePreviousWeekday(new DateTime(2026, 6, 12));
if (profitDate != new DateTime(2026, 6, 11))
{
    throw new InvalidOperationException($"profitDate: expected 2026-06-11, actual {profitDate:yyyy-MM-dd}");
}

Console.WriteLine("Portfolio accounting regression passed.");
