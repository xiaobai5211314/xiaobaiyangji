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

var june18DisplayAmount = 90343.59m;
var june18PendingBuy = 2000.00m;
var june18OcrSummary = PortfolioAccounting.Calculate(
    new[]
    {
        new ConfirmedHoldingMoney(35165.81m, -274.80m, -2751.11m),
        new ConfirmedHoldingMoney(34615.41m - 1000.00m, 69.13m, -4052.83m),
        new ConfirmedHoldingMoney(20482.35m - 1000.00m, 38.15m, -517.65m),
        new ConfirmedHoldingMoney(59.75m, -0.12m, -40.25m),
        new ConfirmedHoldingMoney(17.03m, 0.94m, 8.00m),
        new ConfirmedHoldingMoney(3.24m, -0.01m, -6.76m)
    },
    0m);
Equal(90343.59m, june18DisplayAmount, "ocr[2026-06-18].platformDisplayAmount");
Equal(2000.00m, june18PendingBuy, "ocr[2026-06-18].pendingBuy");
Equal(88343.59m, june18OcrSummary.AntConfirmedAmount, "ocr[2026-06-18].confirmedAmount.excludesPendingBuy");
Equal(-166.71m, june18OcrSummary.ConfirmedYesterdayProfit, "ocr[2026-06-18].yesterdayIncome");
Equal(-7360.60m, june18OcrSummary.AntHoldingProfit, "ocr[2026-06-18].holdingIncome");
Equal(95704.19m, june18OcrSummary.AntHoldingCost, "ocr[2026-06-18].holdingCost.excludesPendingBuy");

var pendingSameDay = new MyFundConfig
{
    HoldAmount = 34615.41,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = "2026-06-18",
    PendingTradeStatus = "pending_buy"
};
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(pendingSameDay, "2026-06-18")), "pending.sameDay.active");

var pendingAfterConfirm = new MyFundConfig
{
    HoldAmount = 34615.41,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = "2026-06-18",
    PendingConfirmDate = "2026-06-19",
    PendingTradeStatus = "pending_buy"
};
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(pendingAfterConfirm, "2026-06-19")), "pending.confirmDate.notActiveOnConfirmDate");
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(pendingAfterConfirm, "2026-06-18", "2026-06-19")), "pending.confirmDate.notActiveOnNaturalConfirmDate");

var cancelledPending = new MyFundConfig
{
    HoldAmount = 34615.41,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = "2026-06-18",
    PendingTradeStatus = "cancelled"
};
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(cancelledPending, "2026-06-18")), "pending.cancelled.notActive");

var futurePending = new MyFundConfig
{
    HoldAmount = 34615.41,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = "2026-06-19",
    PendingConfirmDate = "2026-06-20",
    PendingTradeStatus = "pending_buy"
};
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(futurePending, "2026-06-18")), "pending.futureTrade.notActiveBeforeTradeDate");

Equal(-1121.92m, PortfolioAccounting.OfficialTodayProfit(88340.36m, -1.27m), "officialTodayProfit.excludesPendingBuy");
Equal(-105.50m, PortfolioAccounting.OfficialTodayProfit(88340.36m, -1.27m, -105.50m), "officialTodayProfit.prefersExactSettledProfit");

Equal(
    88868.91m,
    PortfolioAccounting.ResolveAccountTotalAmount(
        snapshotDisplayAmount: 90850.95m,
        confirmedAmount: 88868.91m,
        pendingBuyAmount: 0m,
        useCurrentSnapshotSummary: false,
        antConfirmedAvailable: true),
    "summary.accountTotalAmount.rollsForwardWhenNavConfirmedWithoutFreshOcr");

Equal(
    90850.95m,
    PortfolioAccounting.ResolveAccountTotalAmount(
        snapshotDisplayAmount: 90850.95m,
        confirmedAmount: 88868.91m,
        pendingBuyAmount: 0m,
        useCurrentSnapshotSummary: true,
        antConfirmedAvailable: true),
    "summary.accountTotalAmount.prefersFreshOcrSnapshot");

Equal(
    90850.95m,
    PortfolioAccounting.ResolveAccountTotalAmount(
        snapshotDisplayAmount: 90000.00m,
        confirmedAmount: 88868.91m,
        pendingBuyAmount: 1982.04m,
        useCurrentSnapshotSummary: false,
        antConfirmedAvailable: true),
    "summary.accountTotalAmount.includesPendingBuyAfterArchiveRollForward");

var staleSnapshotWins = PortfolioAccounting.IsOcrSnapshotFreshForArchive(
    "2026-06-24",
    "2026-06-23",
    new DateTime(2026, 6, 24));
if (staleSnapshotWins)
    throw new InvalidOperationException("summary.snapshotFreshness: OCR confirmed for 2026-06-23 must not override 2026-06-24 archive");

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

var holidayProfitDate = PortfolioAccounting.ResolvePreviousWeekday(new DateTime(2026, 6, 22));
if (holidayProfitDate != new DateTime(2026, 6, 18))
{
    throw new InvalidOperationException($"profitDate.holiday: expected 2026-06-18, actual {holidayProfitDate:yyyy-MM-dd}");
}

if (MarketCalendar.IsTradingDay(new DateTime(2026, 6, 19)))
    throw new InvalidOperationException("calendar.cn.duanwu: 2026-06-19 must be A-share closed");
if (MarketCalendar.GetPreviousTradingDate(new DateTime(2026, 6, 21)) != new DateTime(2026, 6, 18))
    throw new InvalidOperationException("calendar.cn.previousTradingDate: 2026-06-21 should resolve to 2026-06-18");
if (MarketCalendar.GetNextTradingDate(new DateTime(2026, 6, 19)) != new DateTime(2026, 6, 22))
    throw new InvalidOperationException("calendar.cn.nextTradingDate: 2026-06-19 should resolve to 2026-06-22");
if (MarketCalendar.IsTradingDay(new DateTime(2026, 7, 1), "hk"))
    throw new InvalidOperationException("calendar.hk.sarDay: 2026-07-01 must be HK closed");

var normalBeforeCutoff = FundTradeTiming.Resolve(new DateTime(2026, 6, 18), false, "华富科技动能混合C");
if (normalBeforeCutoff.TradeDate != "2026-06-18" || normalBeforeCutoff.ConfirmDate != "2026-06-22")
    throw new InvalidOperationException($"trade.normal.beforeCutoff: expected T=2026-06-18 confirm=2026-06-22, actual T={normalBeforeCutoff.TradeDate} confirm={normalBeforeCutoff.ConfirmDate}");

var normalAfterCutoff = FundTradeTiming.Resolve(new DateTime(2026, 6, 18), true, "华富科技动能混合C");
if (normalAfterCutoff.TradeDate != "2026-06-22" || normalAfterCutoff.ConfirmDate != "2026-06-23")
    throw new InvalidOperationException($"trade.normal.afterCutoff: expected T=2026-06-22 confirm=2026-06-23, actual T={normalAfterCutoff.TradeDate} confirm={normalAfterCutoff.ConfirmDate}");

var holidayBeforeCutoff = FundTradeTiming.Resolve(new DateTime(2026, 6, 19), false, "华富科技动能混合C");
if (holidayBeforeCutoff.TradeDate != "2026-06-22" || holidayBeforeCutoff.ConfirmDate != "2026-06-23")
    throw new InvalidOperationException($"trade.normal.holiday: expected T=2026-06-22 confirm=2026-06-23, actual T={holidayBeforeCutoff.TradeDate} confirm={holidayBeforeCutoff.ConfirmDate}");

var qdiiBeforeCutoff = FundTradeTiming.Resolve(new DateTime(2026, 6, 18), false, "天弘恒生科技ETF联接(QDII)C");
if (qdiiBeforeCutoff.TradeDate != "2026-06-18" || qdiiBeforeCutoff.ConfirmDate != "2026-06-23")
    throw new InvalidOperationException($"trade.qdii.beforeCutoff: expected T=2026-06-18 confirm=2026-06-23, actual T={qdiiBeforeCutoff.TradeDate} confirm={qdiiBeforeCutoff.ConfirmDate}");

var qdiiPending = new MyFundConfig
{
    HoldAmount = 34190.04,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = qdiiBeforeCutoff.TradeDate,
    PendingConfirmDate = qdiiBeforeCutoff.ConfirmDate,
    PendingTradeStatus = "pending_buy"
};
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(qdiiPending, "2026-06-22")), "pending.qdii.beforeConfirm.active");
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(qdiiPending, "2026-06-23")), "pending.qdii.confirmDate.notActive");

var staleLegacyPending = new MyFundConfig
{
    HoldAmount = 34190.04,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = "2026-06-19",
    PendingConfirmDate = "2026-06-10",
    PendingTradeStatus = "pending_buy",
    LastTradeDate = "2026-06-19",
    LastAddAmount = 1000.00
};
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(staleLegacyPending, "2026-06-18", "2026-06-19")), "pending.legacy.confirmReached.notActive");

var manualAddFund = new MyFundConfig
{
    FundName = "华富科技动能混合C",
    HoldAmount = 10000,
    CostAmount = 10000,
    HoldShares = 10000
};
var settlement = new PortfolioSettlementService();
settlement.AddPosition(manualAddFund, 2000, normalBeforeCutoff.TradeDate, normalBeforeCutoff.ConfirmDate);
Equal(12000.00m, PortfolioAccounting.Money(manualAddFund.HoldAmount), "manualAdd.displayAmount.includesPending");
Equal(2000.00m, PortfolioAccounting.Money(manualAddFund.PendingBuyAmount), "manualAdd.pendingAmount");
if (manualAddFund.PendingConfirmDate != "2026-06-22")
    throw new InvalidOperationException($"manualAdd.confirmDate: expected 2026-06-22, actual {manualAddFund.PendingConfirmDate}");

Console.WriteLine("Portfolio accounting regression passed.");
