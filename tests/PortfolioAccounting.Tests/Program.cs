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
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(pendingAfterConfirm, "2026-06-19")), "pending.confirmDate.remainsOutstanding");
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(pendingAfterConfirm, "2026-06-18", "2026-06-19")), "pending.confirmDate.remainsVisibleUntilActuallyConfirmed");
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetReturnExcludedPendingBuyAmount(pendingAfterConfirm, "2026-06-19")), "pending.confirmDate.startsParticipatingInReturns");

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
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(futurePending, "2026-06-18")), "pending.futureTrade.outstandingImmediately");
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetReturnExcludedPendingBuyAmount(futurePending, "2026-06-18")), "pending.futureTrade.excludedFromReturns");

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

var nightOcrAmount = PortfolioAccounting.ResolveEstimatedHoldingAmount(
    snapshotDisplayAmount: 91942.89m,
    settledConfirmedAmount: 92747.14m,
    estimatedProfit: -804.25m,
    pendingBuyAmount: 0m,
    useCurrentOcrSnapshot: true);
Equal(91942.89m, nightOcrAmount.DisplayAmount, "ocr.night.currentAmount.mustNotAddTodayProfitAgain");
Equal(91942.89m, nightOcrAmount.ConfirmedAmount, "ocr.night.confirmedAmount");

var estimatedWithoutOcr = PortfolioAccounting.ResolveEstimatedHoldingAmount(
    snapshotDisplayAmount: 0m,
    settledConfirmedAmount: 92747.14m,
    estimatedProfit: -804.25m,
    pendingBuyAmount: 0m,
    useCurrentOcrSnapshot: false);
Equal(91942.89m, estimatedWithoutOcr.DisplayAmount, "estimate.withoutCurrentOcr.rollsFromSettledAmount");

var nightOcrWithPending = PortfolioAccounting.ResolveEstimatedHoldingAmount(
    snapshotDisplayAmount: 92942.89m,
    settledConfirmedAmount: 92747.14m,
    estimatedProfit: -804.25m,
    pendingBuyAmount: 1000m,
    useCurrentOcrSnapshot: true);
Equal(92942.89m, nightOcrWithPending.DisplayAmount, "ocr.night.pending.displayIncludesPendingOnce");
Equal(91942.89m, nightOcrWithPending.ConfirmedAmount, "ocr.night.pending.confirmedExcludesPending");

var officialNightOcrAmount = PortfolioAccounting.ResolveOfficialHoldingAmount(
    snapshotDisplayAmount: 91942.89m,
    officialConfirmedAmount: 91942.90m,
    rolledConfirmedAmount: 91942.89m,
    pendingBuyAmount: 0m,
    useCurrentOcrSnapshot: true);
Equal(91942.89m, officialNightOcrAmount.DisplayAmount, "ocr.night.officialBranch.currentSnapshotWinsShareNavPennyDrift");

var officialWithoutOcr = PortfolioAccounting.ResolveOfficialHoldingAmount(
    snapshotDisplayAmount: 91942.89m,
    officialConfirmedAmount: 91942.90m,
    rolledConfirmedAmount: 91942.89m,
    pendingBuyAmount: 0m,
    useCurrentOcrSnapshot: false);
Equal(91942.90m, officialWithoutOcr.DisplayAmount, "official.withoutCurrentOcr.usesOfficialAmount");

if (!PortfolioAccounting.IsOcrSnapshotCurrentForDisplay("2026-07-10", new DateTime(2026, 7, 10)))
    throw new InvalidOperationException("ocr.currentDisplay.sameNaturalDate: expected current snapshot");
if (PortfolioAccounting.IsOcrSnapshotCurrentForDisplay("2026-07-10", new DateTime(2026, 7, 11)))
    throw new InvalidOperationException("ocr.currentDisplay.nextNaturalDate: yesterday snapshot must expire at midnight");

Equal(
    88251.52m,
    PortfolioAccounting.ResolveSettledDisplayAmount(
        baseAmount: 89739.09m,
        settledProfit: -1487.57m,
        activePendingBuyAmount: 0m),
    "settlement.displayAmount.rollsFromOcrSnapshotAfterNavConfirmed");

Equal(
    85357.84m,
    PortfolioAccounting.ResolveSettledDisplayAmount(
        baseAmount: 88251.52m,
        settledProfit: -2893.68m,
        activePendingBuyAmount: 0m,
        exactConfirmedAssets: 85357.84m),
    "settlement.displayAmount.rollsFromRoundedProfitOnNextDay");

Equal(
    101.2394m,
    PortfolioAccounting.ResolveSettledLedgerAmount(
        baseAmount: 100.0049m,
        settledProfit: 1.2345m,
        activePendingBuyAmount: 0m),
    "settlement.ledgerAmount.keepsFourDecimals");

Equal(
    101.24m,
    PortfolioAccounting.ResolveSettledDisplayAmount(
        baseAmount: 100.0049m,
        settledProfit: 1.2345m,
        activePendingBuyAmount: 0m),
    "settlement.displayAmount.roundsLedgerAtBoundary");

var preciseSettlement = new PortfolioSettlementService();
var preciseFund = new MyFundConfig
{
    HoldAmount = 100.00,
    HoldAmountPrecise = 100.0049m
};
if (!preciseSettlement.ApplyOneDaySettlement(preciseFund, 1.23, "2026-07-07", exactProfit: 1.2345))
    throw new InvalidOperationException("settlement.precise.changed: first settlement should update the ledger amount");
Equal(101.24m, PortfolioAccounting.Money(preciseFund.HoldAmount), "settlement.precise.displayAmount");
Equal(101.2394m, PortfolioAccounting.LedgerMoney(preciseFund.HoldAmountPrecise), "settlement.precise.ledgerAmount");
Equal(1.23m, PortfolioAccounting.Money(preciseFund.LastSettledProfit), "settlement.precise.displayProfit");
Equal(1.2345m, PortfolioAccounting.LedgerMoney(preciseFund.LastSettledProfitPrecise), "settlement.precise.ledgerProfit");
if (preciseSettlement.ApplyOneDaySettlement(preciseFund, 1.23, "2026-07-07", exactProfit: 1.2345))
    throw new InvalidOperationException("settlement.precise.repeat: same-day repeat settlement must not drift");
Equal(100.0049m, PortfolioAccounting.LedgerMoney(preciseSettlement.GetDailyBaseAmount(preciseFund, "2026-07-07")), "settlement.precise.repeatBase");

Equal(
    33198.62m,
    PortfolioAccounting.ResolveSettledDisplayAmount(
        baseAmount: 33220.53m,
        settledProfit: -21.91m,
        activePendingBuyAmount: 0m,
        exactConfirmedAssets: 33198.63m),
    "settlement.displayAmount.prefersRoundedProfitRollForwardOverShareNavPennyDrift");

Equal(
    35017.13m,
    PortfolioAccounting.ResolveSettledDisplayAmount(
        baseAmount: 35060.95m,
        settledProfit: 383.42m,
        activePendingBuyAmount: 0m,
        exactConfirmedAssets: 35017.1269m),
    "settlement.displayAmount.selfHealsMaterialRollingBaseDrift");

var july15OfficialReconciliation = PortfolioAccounting.ResolveOfficialHoldingAmount(
    snapshotDisplayAmount: 35444.37m,
    officialConfirmedAmount: 35017.13m,
    rolledConfirmedAmount: 35444.37m,
    pendingBuyAmount: 0m,
    useCurrentOcrSnapshot: true);
Equal(35017.13m, july15OfficialReconciliation.DisplayAmount, "official.currentOcr.selfHealsMaterialDrift");
Equal(35017.13m, july15OfficialReconciliation.ConfirmedAmount, "official.currentOcr.selfHealsConfirmedAmount");

var stalePreciseBasisFund = new MyFundConfig
{
    HoldAmount = 34633.71,
    HoldAmountPrecise = 35060.9500m,
    LastSettledDate = "2026-07-14",
    LastSettledProfit = 43.82,
    LastSettledProfitPrecise = 43.8200m
};
var stalePreciseBasisSettlement = new PortfolioSettlementService();
if (!stalePreciseBasisSettlement.ApplyOneDaySettlement(
        stalePreciseBasisFund,
        actualRate: 1.11,
        settleDate: "2026-07-15",
        exactProfit: 383.42,
        exactAssets: 35017.1269))
{
    throw new InvalidOperationException("settlement.stalePreciseBasis: expected exact official amount to repair the stale precise ledger");
}
Equal(35017.13m, PortfolioAccounting.Money(stalePreciseBasisFund.HoldAmount), "settlement.stalePreciseBasis.displayAmount");
Equal(35017.1269m, PortfolioAccounting.LedgerMoney(stalePreciseBasisFund.HoldAmountPrecise), "settlement.stalePreciseBasis.ledgerAmount");

Equal(
    -3651.12m,
    PortfolioAccounting.ResolveOfficialHoldingProfit(
        currentAssets: july15OfficialReconciliation.ConfirmedAmount,
        costAmount: 38668.25m,
        realizedProfit: 0m,
        fallbackHoldingProfit: -4034.54m),
    "official.currentOcr.holdingProfitRollsAfterNavConfirmation");

var july15AntSummary = PortfolioAccounting.Calculate(
    new[]
    {
        new ConfirmedHoldingMoney(43207.65m, 521.33m, -7709.27m),
        new ConfirmedHoldingMoney(35017.13m, 43.82m, -3651.12m),
        new ConfirmedHoldingMoney(20738.79m, 29.43m, -261.21m),
        new ConfirmedHoldingMoney(51.03m, -1.10m, -48.97m),
        new ConfirmedHoldingMoney(17.89m, 0.19m, 8.86m),
        new ConfirmedHoldingMoney(3.10m, 0.02m, -6.90m)
    },
    0m);
Equal(99035.59m, july15AntSummary.AntConfirmedAmount, "ant[2026-07-15].confirmedAmount");
Equal(593.69m, july15AntSummary.ConfirmedYesterdayProfit, "ant[2026-07-15].yesterdayProfit");
Equal(-11668.61m, july15AntSummary.AntHoldingProfit, "ant[2026-07-15].holdingProfit");

var july16PendingBuyFund = new MyFundConfig
{
    FundCode = "017968",
    HoldAmount = 47194.95,
    HoldAmountPrecise = 47194.9527m,
    CostAmount = 55916.92,
    CostAmountIsConfirmed = false,
    CostAmountSource = PortfolioSettlementService.CostSourcePurchaseAmount,
    PendingBuyAmount = 5000,
    PendingBuyShares = 3550.3799,
    PendingTradeDate = "2026-07-16",
    PendingConfirmDate = "2026-07-17",
    PendingTradeStatus = "pending_buy",
    HoldShares = 33512.0022,
    HoldSharesAreConfirmed = false,
    HoldSharesSource = PortfolioSettlementService.ShareSourcePurchaseNavDerived
};
var july16ConfirmedCost = PortfolioSettlementService.GetConfirmedCostAmount(
    july16PendingBuyFund,
    "2026-07-16",
    PortfolioAccounting.Money(july16PendingBuyFund.HoldAmount));
Equal(50916.92m, july16ConfirmedCost, "pendingBuy.confirmedCost.excludesUnconfirmedPurchasePrincipalOnce");
Equal(5000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(july16PendingBuyFund, "2026-07-17")), "pendingBuy.20260717.expectedConfirmDate.mustRemainOutstanding");
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetReturnExcludedPendingBuyAmount(july16PendingBuyFund, "2026-07-17")), "pendingBuy.20260717.firstProfitDate.mustEnterReturnBase");
Equal(29961.6223m, Convert.ToDecimal(PortfolioSettlementService.GetEffectiveShares(july16PendingBuyFund, "2026-07-16")), "pendingBuy.tradeDate.effectiveShares.excludePending");
Equal(33512.0022m, Convert.ToDecimal(PortfolioSettlementService.GetEffectiveShares(july16PendingBuyFund, "2026-07-17")), "pendingBuy.firstProfitDate.effectiveShares.includePending");
Equal(
    50916.92m,
    PortfolioSettlementService.GetConfirmedCostAmount(
        july16PendingBuyFund,
        "2026-07-16",
        PortfolioAccounting.Money(july16PendingBuyFund.HoldAmount),
        "2026-07-17"),
    "pendingBuy.20260717.confirmedCost.mustStillExcludeOutstandingPrincipal");
var firstProfitDayEstimate = PortfolioAccounting.ResolveEstimatedHoldingAmount(
    47194.95m,
    42194.95m,
    471.95m,
    5000.00m,
    false);
Equal(47666.90m, firstProfitDayEstimate.DisplayAmount, "pendingBuy.firstProfitDay.estimate.mustNotAddPrincipalTwice");
Equal(42666.90m, firstProfitDayEstimate.ConfirmedAmount, "pendingBuy.firstProfitDay.estimate.confirmedSplit");
var firstProfitDayOfficial = PortfolioAccounting.ResolveOfficialHoldingAmount(
    47194.95m,
    42666.90m,
    42666.90m,
    5000.00m,
    false);
Equal(47666.90m, firstProfitDayOfficial.DisplayAmount, "pendingBuy.firstProfitDay.official.mustNotAddPrincipalTwice");
Equal(42666.90m, firstProfitDayOfficial.ConfirmedAmount, "pendingBuy.firstProfitDay.official.confirmedSplit");
Equal(
    -8721.97m,
    PortfolioAccounting.ResolveOfficialHoldingProfit(
        currentAssets: 42194.95m,
        costAmount: july16ConfirmedCost),
    "pendingBuy.holdingProfit.excludesUnconfirmedPurchasePrincipal");

var detailPageConfirmedCost = new MyFundConfig
{
    CostAmount = 50916.92,
    CostAmountIsConfirmed = true,
    CostAmountSource = PortfolioSettlementService.CostSourceOcrAssetDetail,
    PendingBuyAmount = 5000,
    PendingTradeDate = "2026-07-16",
    PendingConfirmDate = "2026-07-17",
    PendingTradeStatus = "pending_buy"
};
Equal(
    50916.92m,
    PortfolioSettlementService.GetConfirmedCostAmount(
        detailPageConfirmedCost,
        "2026-07-16",
        47194.95m),
    "pendingBuy.confirmedDetailCost.mustNotSubtractPendingTwice");

var pendingBuyWithoutCost = new MyFundConfig
{
    HoldAmount = 5000,
    CostAmount = 0,
    PendingBuyAmount = 5000,
    PendingTradeDate = "2026-07-16",
    PendingConfirmDate = "2026-07-17",
    PendingTradeStatus = "pending_buy"
};
Equal(
    0m,
    PortfolioSettlementService.GetConfirmedCostAmount(
        pendingBuyWithoutCost,
        "2026-07-16",
        PortfolioSettlementService.GetHoldAmountBasis(pendingBuyWithoutCost)),
    "pendingBuy.newPositionWithoutCost.mustExcludePendingOnce");

var july16AntSummary = PortfolioAccounting.Calculate(
    new[]
    {
        new ConfirmedHoldingMoney(42194.95m, -1012.70m, -8721.97m),
        new ConfirmedHoldingMoney(35668.94m, 651.81m, -2999.31m),
        new ConfirmedHoldingMoney(21126.76m, 387.97m, 126.76m),
        new ConfirmedHoldingMoney(49.77m, -1.27m, -50.23m),
        new ConfirmedHoldingMoney(16.92m, -0.97m, 7.89m),
        new ConfirmedHoldingMoney(3.16m, 0.06m, -6.84m)
    },
    0m);
Equal(99060.50m, july16AntSummary.AntConfirmedAmount, "ant[2026-07-16].confirmedAmount");
Equal(-11643.70m, july16AntSummary.AntHoldingProfit, "ant[2026-07-16].holdingProfit");
Equal(110704.20m, july16AntSummary.AntHoldingCost, "ant[2026-07-16].holdingCost");
Equal(-10.52m, july16AntSummary.AntHoldingRate, "ant[2026-07-16].holdingRate");
Equal(
    104060.50m,
    PortfolioAccounting.ResolveAccountTotalAmount(
        snapshotDisplayAmount: 0m,
        confirmedAmount: july16AntSummary.AntConfirmedAmount,
        pendingBuyAmount: 5000m,
        useCurrentSnapshotSummary: false,
        antConfirmedAvailable: true),
    "ant[2026-07-16].displayAmount.includesPendingBuyOnce");

if (!PortfolioSettlementService.HasReliableExactShares(new MyFundConfig { HoldSharesAreConfirmed = true }))
    throw new InvalidOperationException("shares.reliable.platformConfirmed: expected reliable exact shares");
if (!PortfolioSettlementService.HasReliableExactShares(new MyFundConfig { HoldSharesSource = PortfolioSettlementService.ShareSourcePurchaseNavDerived }))
    throw new InvalidOperationException("shares.reliable.purchaseNavDerived: expected reliable exact shares");
if (PortfolioSettlementService.HasReliableExactShares(new MyFundConfig { HoldSharesSource = PortfolioSettlementService.ShareSourceOcrNavDerived }))
    throw new InvalidOperationException("shares.reliable.ocrNavDerived: ordinary amount/nav-derived shares must not override platform amount");

Equal(
    -6970.44m,
    PortfolioAccounting.ResolveOfficialHoldingProfit(
        currentAssets: 31697.81m,
        costAmount: 38668.25m,
        realizedProfit: 0m,
        fallbackHoldingProfit: -5360.08m),
    "settlement.holdingProfit.recomputesFromCurrentAssetsAndCost");

var semiconductorDetailCost = PortfolioAccounting.CostAmountFromCostPrice(
    costPrice: 1.6639m,
    shares: 6.01m);
var semiconductorHoldingAdjustment = PortfolioAccounting.PlatformHoldingAdjustmentFromPlatformHolding(
    currentAssets: 18.83m,
    costAmount: semiconductorDetailCost,
    realizedProfit: 0m,
    platformHoldingProfit: 9.80m);
Equal(10.00m, semiconductorDetailCost, "assetDetail.costAmount.fromCostPriceAndShares");
Equal(0.97m, semiconductorHoldingAdjustment, "assetDetail.platformAdjustment.preservesPlatformHoldingProfit");
Equal(
    9.80m,
    PortfolioAccounting.ResolveOfficialHoldingProfit(
        currentAssets: 18.83m,
        costAmount: semiconductorDetailCost,
        realizedProfit: 0m,
        fallbackHoldingProfit: 0m,
        platformHoldingAdjustment: semiconductorHoldingAdjustment),
    "assetDetail.holdingProfit.includesPlatformAdjustmentWithoutRealizedProfit");
Equal(
    9.80m,
    PortfolioAccounting.Money(18.83m - semiconductorDetailCost + semiconductorHoldingAdjustment),
    "assetDetail.frontendAggregate.keepsConfirmedCostWithoutDoubleCountingAdjustment");

var holdingAdjustmentWithActualSale = PortfolioAccounting.PlatformHoldingAdjustmentFromPlatformHolding(
    currentAssets: 18m,
    costAmount: 15m,
    realizedProfit: 2m,
    platformHoldingProfit: 4m);
Equal(-1.00m, holdingAdjustmentWithActualSale, "assetDetail.platformAdjustment.excludesActualRealizedProfit");
Equal(
    4.00m,
    PortfolioAccounting.ResolveOfficialHoldingProfit(
        currentAssets: 18m,
        costAmount: 15m,
        realizedProfit: 2m,
        fallbackHoldingProfit: 0m,
        platformHoldingAdjustment: holdingAdjustmentWithActualSale),
    "assetDetail.holdingProfit.keepsRealizedAndAdjustmentSeparate");

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

var saturdaySession = MarketCalendar.ResolveFundDisplaySession(new DateTime(2026, 7, 11, 12, 0, 0));
if (saturdaySession.MarketStatus != "weekend"
    || saturdaySession.EffectiveDate != new DateTime(2026, 7, 10)
    || saturdaySession.IsCurrentNaturalDate)
{
    throw new InvalidOperationException("session.weekend: Saturday must carry Friday assets without Friday today-performance");
}

var mondayPreopenSession = MarketCalendar.ResolveFundDisplaySession(new DateTime(2026, 7, 13, 9, 29, 59));
if (mondayPreopenSession.MarketStatus != "preopen"
    || mondayPreopenSession.EffectiveDate != new DateTime(2026, 7, 10)
    || mondayPreopenSession.MarketOpen)
{
    throw new InvalidOperationException("session.preopen: before 09:30 must carry the previous trading day");
}

var mondayOpenSession = MarketCalendar.ResolveFundDisplaySession(new DateTime(2026, 7, 13, 9, 30, 0));
if (mondayOpenSession.MarketStatus != "open"
    || mondayOpenSession.EffectiveDate != new DateTime(2026, 7, 13)
    || !mondayOpenSession.MarketOpen)
{
    throw new InvalidOperationException("session.open: 09:30 must start the current natural trading day");
}

var weekendPolicy = PortfolioAccounting.ResolveTodayPerformancePolicy(
    saturdaySession.NaturalDate,
    saturdaySession.EffectiveDate,
    saturdaySession.MarketStatus,
    hasPortfolioData: true,
    hasTodayData: true,
    hasTodayEstimate: false,
    hasTodayConfirmed: true);
if (!weekendPolicy.ForceZero || !weekendPolicy.Available || weekendPolicy.Status != "closed")
    throw new InvalidOperationException("todayPanel.weekend: prior trading-day archive must display as current-day zero");

var preopenPolicy = PortfolioAccounting.ResolveTodayPerformancePolicy(
    mondayPreopenSession.NaturalDate,
    mondayPreopenSession.EffectiveDate,
    mondayPreopenSession.MarketStatus,
    hasPortfolioData: true,
    hasTodayData: true,
    hasTodayEstimate: true,
    hasTodayConfirmed: false);
if (!preopenPolicy.ForceZero || preopenPolicy.Status != "preopen")
    throw new InvalidOperationException("todayPanel.preopen: before 09:30 must display zero instead of Friday estimate");

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

var qdiiDomesticHoliday = FundTradeTiming.Resolve(new DateTime(2026, 10, 7), false, "天弘恒生科技ETF联接(QDII)C");
if (qdiiDomesticHoliday.TradeDate != "2026-10-08")
    throw new InvalidOperationException($"trade.qdii.domesticHoliday: expected estimated T=2026-10-08, actual T={qdiiDomesticHoliday.TradeDate}");

var qdiiPending = new MyFundConfig
{
    HoldAmount = 34190.04,
    PendingBuyAmount = 1000.00,
    PendingTradeDate = qdiiBeforeCutoff.TradeDate,
    PendingConfirmDate = qdiiBeforeCutoff.ConfirmDate,
    PendingTradeStatus = "pending_buy"
};
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(qdiiPending, "2026-06-22")), "pending.qdii.beforeConfirm.active");
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(qdiiPending, "2026-06-23")), "pending.qdii.confirmDate.remainsOutstanding");
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(qdiiPending, "2026-06-22", "2026-06-23")), "pending.qdii.confirmDate.remainsVisibleUntilActuallyConfirmed");
Equal(0.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetReturnExcludedPendingBuyAmount(qdiiPending, "2026-06-23")), "pending.qdii.firstProfitDate.participatesInReturns");

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
Equal(1000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(staleLegacyPending, "2026-06-18", "2026-06-19")), "pending.explicitStatus.confirmDateDoesNotHideDisplay");

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
Equal(10000.00m, PortfolioAccounting.Money(settlement.GetDailyBaseAmount(manualAddFund, normalBeforeCutoff.TradeDate)), "manualAdd.todayBase.excludesPendingBuy");
if (manualAddFund.PendingConfirmDate != "2026-06-22")
    throw new InvalidOperationException($"manualAdd.confirmDate: expected 2026-06-22, actual {manualAddFund.PendingConfirmDate}");

Equal(10000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetEffectiveShares(manualAddFund, "2026-06-18")), "manualAdd.tradeDate.confirmedSharesOnly");
if (!PortfolioSettlementService.CapturePendingBuyShares(manualAddFund, "2026-06-18", 2.0))
    throw new InvalidOperationException("manualAdd.capturePendingShares: expected pending shares to be captured");
Equal(11000.00m, PortfolioAccounting.Money(manualAddFund.HoldShares), "manualAdd.totalSharesAfterPurchaseNav");
if (manualAddFund.HoldSharesAreConfirmed
    || manualAddFund.HoldSharesSource != PortfolioSettlementService.ShareSourcePurchaseNavDerived)
    throw new InvalidOperationException("manualAdd.shareSource: purchase-NAV shares must remain marked as derived");
if (manualAddFund.CostAmountIsConfirmed
    || manualAddFund.CostAmountSource != PortfolioSettlementService.CostSourcePurchaseAmount)
    throw new InvalidOperationException("manualAdd.costSource: purchase amount must remain marked as transaction-derived");
Equal(10000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetEffectiveShares(manualAddFund, "2026-06-19")), "manualAdd.beforeConfirm.excludesPendingShares");
Equal(2000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetActivePendingBuyAmount(manualAddFund, "2026-06-18", "2026-06-22")), "manualAdd.confirmDate.displayStillShowsPending");
var duplicateAddRejected = false;
try
{
    settlement.AddPosition(manualAddFund, 500, "2026-06-22", "2026-06-23");
}
catch (InvalidOperationException ex)
{
    duplicateAddRejected = ex.Message.Contains("原记录仍在", StringComparison.Ordinal);
}
if (!duplicateAddRejected)
    throw new InvalidOperationException("manualAdd.duplicatePending: expected a visible duplicate-record explanation");
if (!PortfolioSettlementService.ConfirmPendingBuyIfDue(manualAddFund, "2026-06-22"))
    throw new InvalidOperationException("manualAdd.confirmPending: expected pending buy to confirm");
Equal(0.00m, PortfolioAccounting.Money(manualAddFund.PendingBuyAmount), "manualAdd.confirmed.pendingAmountCleared");
Equal(11000.00m, PortfolioAccounting.Money(PortfolioSettlementService.GetEffectiveShares(manualAddFund, "2026-06-22")), "manualAdd.confirmed.allSharesEffective");

var july13BeforeCutoff = FundTradeTiming.Resolve(new DateTime(2026, 7, 13), false, "示例混合基金");
if (july13BeforeCutoff.TradeDate != "2026-07-13" || july13BeforeCutoff.ConfirmDate != "2026-07-14")
    throw new InvalidOperationException($"trade.normal.20260713.beforeCutoff: expected T=2026-07-13 confirm=2026-07-14, actual T={july13BeforeCutoff.TradeDate} confirm={july13BeforeCutoff.ConfirmDate}");

var pendingBuyEarningsFund = new MyFundConfig
{
    FundName = "示例混合基金",
    HoldAmount = 35000.00,
    HoldAmountPrecise = 35000.0000m,
    CostAmount = 40000.00,
    HoldShares = 20000.0000
};
settlement.AddPosition(pendingBuyEarningsFund, 10000, july13BeforeCutoff.TradeDate, july13BeforeCutoff.ConfirmDate);
Equal(45000.00m, PortfolioAccounting.Money(pendingBuyEarningsFund.HoldAmount), "manualAdd.regression.displayIncludesPendingBeforeEstimate");
Equal(35000.00m, PortfolioAccounting.Money(settlement.GetDailyBaseAmount(pendingBuyEarningsFund, "2026-07-13")), "manualAdd.regression.todayBaseExcludesPending");
Equal(-1358.00m, PortfolioAccounting.OfficialTodayProfit(35000.00m, -3.88m), "manualAdd.regression.profitUsesConfirmedBaseOnly");
if (!settlement.ApplyOneDaySettlement(pendingBuyEarningsFund, -3.88, "2026-07-13", exactProfit: -1358.00))
    throw new InvalidOperationException("manualAdd.regression.settlement: expected settlement to update the confirmed ledger");
Equal(43642.00m, PortfolioAccounting.Money(pendingBuyEarningsFund.HoldAmount), "manualAdd.regression.displayKeepsPendingAfterProfit");
Equal(10000.00m, PortfolioAccounting.Money(pendingBuyEarningsFund.PendingBuyAmount), "manualAdd.regression.pendingRemainsUntilConfirm");
Equal(-1358.00m, PortfolioAccounting.Money(pendingBuyEarningsFund.LastSettledProfit), "manualAdd.regression.pendingDoesNotParticipateInProfit");

var shareCalibrationFund = new MyFundConfig();
if (!PortfolioSettlementService.ApplyShareCalibration(
        shareCalibrationFund,
        6.009846,
        false,
        PortfolioSettlementService.ShareSourceOcrNavDerived))
    throw new InvalidOperationException("shares.derived.initial: expected initial derived shares to apply");
if (!PortfolioSettlementService.ApplyShareCalibration(
        shareCalibrationFund,
        6.01,
        true,
        PortfolioSettlementService.ShareSourceOcrAssetDetail))
    throw new InvalidOperationException("shares.confirmed.assetDetail: expected platform shares to replace derived shares");
Equal(6.01m, Convert.ToDecimal(shareCalibrationFund.HoldShares), "shares.confirmed.assetDetail.value");
if (!shareCalibrationFund.HoldSharesAreConfirmed
    || shareCalibrationFund.HoldSharesSource != PortfolioSettlementService.ShareSourceOcrAssetDetail)
    throw new InvalidOperationException("shares.confirmed.assetDetail.provenance: exact platform source was not persisted");
if (PortfolioSettlementService.ApplyShareCalibration(
        shareCalibrationFund,
        6.009846,
        false,
        PortfolioSettlementService.ShareSourceOcrNavDerived))
    throw new InvalidOperationException("shares.confirmed.protected: ordinary OCR must not replace platform-confirmed shares");
Equal(6.01m, Convert.ToDecimal(shareCalibrationFund.HoldShares), "shares.confirmed.protected.value");

var purchaseNavShareFund = new MyFundConfig
{
    HoldShares = 26756.2743,
    HoldSharesSource = PortfolioSettlementService.ShareSourcePurchaseNavDerived
};
if (PortfolioSettlementService.ApplyShareCalibration(
        purchaseNavShareFund,
        26756.26758,
        false,
        PortfolioSettlementService.ShareSourceOcrNavDerived))
    throw new InvalidOperationException("shares.purchaseNav.protected: rounded-amount OCR must not replace purchase-NAV shares");
Equal(26756.2743m, Convert.ToDecimal(purchaseNavShareFund.HoldShares), "shares.purchaseNav.protected.value");

var costCalibrationFund = new MyFundConfig();
if (!PortfolioSettlementService.ApplyCostCalibration(
        costCalibrationFund,
        9.03,
        false,
        PortfolioSettlementService.CostSourceOcrHoldingDerived))
    throw new InvalidOperationException("cost.derived.initial: expected initial derived cost to apply");
if (!PortfolioSettlementService.ApplyCostCalibration(
        costCalibrationFund,
        10.00,
        true,
        PortfolioSettlementService.CostSourceOcrAssetDetail))
    throw new InvalidOperationException("cost.confirmed.assetDetail: expected exact platform cost to replace derived cost");
if (PortfolioSettlementService.ApplyCostCalibration(
        costCalibrationFund,
        9.03,
        false,
        PortfolioSettlementService.CostSourceOcrHoldingDerived))
    throw new InvalidOperationException("cost.confirmed.protected: ordinary OCR must not replace platform-confirmed cost");
Equal(10.00m, PortfolioAccounting.Money(costCalibrationFund.CostAmount), "cost.confirmed.protected.value");

var pendingSellFund = new MyFundConfig
{
    HoldAmount = 10000,
    HoldAmountPrecise = 10000m,
    CostAmount = 10000,
    HoldShares = 10000,
    PlatformHoldingAdjustment = 1.25
};
var pendingSellProfit = settlement.ReducePosition(pendingSellFund, 2500, null, "2026-06-18", "2026-06-22");
Equal(0.00m, PortfolioAccounting.Money(pendingSellProfit), "manualSell.pending.noProfit");
Equal(10000.00m, PortfolioAccounting.Money(pendingSellFund.HoldAmount), "manualSell.pending.keepsAmount");
Equal(10000.00m, PortfolioAccounting.Money(pendingSellFund.HoldShares), "manualSell.pending.keepsShares");
Equal(10000.00m, PortfolioAccounting.Money(pendingSellFund.CostAmount), "manualSell.pending.keepsCost");
Equal(2500.00m, PortfolioAccounting.Money(pendingSellFund.PendingSellShares), "manualSell.pending.shares");

var confirmedSellProfit = settlement.ReducePosition(pendingSellFund, 2500, 2700, "2026-06-22", "2026-06-22");
Equal(200.00m, PortfolioAccounting.Money(confirmedSellProfit), "manualSell.confirmed.realizedProfit");
Equal(7500.00m, PortfolioAccounting.Money(pendingSellFund.HoldAmount), "manualSell.confirmed.amountReducedOnce");
Equal(7500.00m, PortfolioAccounting.Money(pendingSellFund.HoldShares), "manualSell.confirmed.sharesReducedOnce");
Equal(7500.00m, PortfolioAccounting.Money(pendingSellFund.CostAmount), "manualSell.confirmed.costReducedOnce");
Equal(0.00m, PortfolioAccounting.Money(pendingSellFund.PendingSellShares), "manualSell.confirmed.pendingSharesCleared");
Equal(1.25m, PortfolioAccounting.Money(pendingSellFund.PlatformHoldingAdjustment), "manualSell.partial.keepsPlatformAdjustment");
if (pendingSellFund.LastTradeDate != "2026-06-18")
    throw new InvalidOperationException($"manualSell.confirmed.tradeDate: expected original 2026-06-18, actual {pendingSellFund.LastTradeDate}");

var fullSellFund = new MyFundConfig
{
    HoldAmount = 100,
    HoldAmountPrecise = 100m,
    CostAmount = 80,
    HoldShares = 10,
    PlatformHoldingAdjustment = 0.97
};
settlement.ReducePosition(fullSellFund, 10, 120, "2026-07-15", "2026-07-15");
Equal(40.00m, PortfolioAccounting.Money(fullSellFund.RealizedProfit), "manualSell.full.realizedProfit");
Equal(0.00m, PortfolioAccounting.Money(fullSellFund.PlatformHoldingAdjustment), "manualSell.full.clearsPlatformAdjustment");

var aerospaceSector = SectorFundCatalog.Resolve("aerospace");
var satelliteSector = SectorFundCatalog.Resolve("卫星产业");
var militarySector = SectorFundCatalog.Resolve("military");
if (SectorFundCatalog.Definitions.Count < 70)
    throw new InvalidOperationException($"sector.catalog.coverage: expected at least 70 themes, actual {SectorFundCatalog.Definitions.Count}");
if (!SectorFundCatalog.IsMatch("长盛航天海工混合A", aerospaceSector))
    throw new InvalidOperationException("sector.aerospace.activeFund: expected mixed aerospace fund to match");
if (!SectorFundCatalog.IsMatch("博时中证卫星产业指数A", satelliteSector))
    throw new InvalidOperationException("sector.satellite.indexFund: expected satellite index fund to match");
if (SectorFundCatalog.IsMatch("长盛航天海工混合A", militarySector))
    throw new InvalidOperationException("sector.military.boundary: aerospace-only fund must not be folded into military");
if (SectorFundCatalog.ClassifyFundGroup("长盛航天海工混合A", "混合型-灵活") != SectorFundCatalog.GroupMixed)
    throw new InvalidOperationException("sector.group.mixed: expected mixed fund classification");
if (SectorFundCatalog.ClassifyFundGroup("博时中证卫星产业指数A", "指数型-股票") != SectorFundCatalog.GroupIndex)
    throw new InvalidOperationException("sector.group.index: expected index fund classification");
if (!SectorFundCatalog.MatchesGroup(SectorFundCatalog.GroupMixed, SectorFundCatalog.GroupActive))
    throw new InvalidOperationException("sector.group.active: active filter must include mixed funds");

Console.WriteLine("Portfolio accounting regression passed.");

// ===== 数据损坏不变式守卫（ARCHIVE_GUARD）回归测试 =====
var today = DateTime.Today;
var archiveGuardCorruptCode = "017968";
var archiveGuardAssets = new Dictionary<string, (double Assets, double DailyProfit)>
{
    [archiveGuardCorruptCode] = (1000.00, -1883.37),
    ["110011"] = (2000.00, 100.00),
    ["110022"] = (3000.00, 200.00),
    ["110033"] = (4000.00, 300.00),
    ["110044"] = (5000.00, 400.00),
    ["110055"] = (6000.00, 500.00),
};
var archiveGuardInput = new List<DailyArchive>();
foreach (var kv in archiveGuardAssets)
{
    archiveGuardInput.Add(new DailyArchive
    {
        Username = "test",
        FundCode = kv.Key,
        RecordDate = today,
        Assets = kv.Value.Assets,
        DailyProfit = kv.Value.DailyProfit,
        DailyRate = 0.0,
        Source = "alipay-confirmed",
        IsFinal = true,
        UpdatedAt = DateTime.UtcNow
    });
}
// 损坏指纹：TOTAL Assets 等于被错抄的基金 017968 的 Assets。
archiveGuardInput.Add(new DailyArchive
{
    Username = "test",
    FundCode = "TOTAL",
    RecordDate = today,
    Assets = archiveGuardAssets[archiveGuardCorruptCode].Assets,
    DailyProfit = -1883.37,
    DailyRate = -4.32,
    Source = "alipay-confirmed-total",
    IsFinal = true,
    UpdatedAt = DateTime.UtcNow
});

// 用例 A：多基金抄写指纹应被剔除 + 重算 TOTAL。
var archiveGuardResult = DailyArchiveService.SanitizeArchiveRows(archiveGuardInput);
if (archiveGuardResult.DroppedCount != 1)
    throw new InvalidOperationException($"archiveGuard.caseA.dropped: expected 1, actual {archiveGuardResult.DroppedCount}");
if (archiveGuardResult.Rows.Any(r => r.FundCode == archiveGuardCorruptCode))
    throw new InvalidOperationException("archiveGuard.caseA: corrupt fund row must be dropped");
var archiveGuardTotal = archiveGuardResult.Rows.FirstOrDefault(r => r.FundCode == "TOTAL");
if (archiveGuardTotal == null)
    throw new InvalidOperationException("archiveGuard.caseA: TOTAL row must remain");
if (archiveGuardTotal.Source != "guard-recomputed-total")
    throw new InvalidOperationException($"archiveGuard.caseA.source: expected guard-recomputed-total, actual {archiveGuardTotal.Source}");
// 重算后 TOTAL.Assets == 其余 5 只基金 Assets 之和（20000.00）。
Equal(20000.00m, PortfolioAccounting.Money((decimal)archiveGuardTotal.Assets), "archiveGuard.caseA.totalAssets");

// 用例 B：单基金组合合法，不触发守卫。
var archiveGuardSingle = new List<DailyArchive>
{
    new DailyArchive
    {
        Username = "test", FundCode = "017968", RecordDate = today,
        Assets = 1234.56, DailyProfit = -10.0, DailyRate = -0.81,
        Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow
    },
    new DailyArchive
    {
        Username = "test", FundCode = "TOTAL", RecordDate = today,
        Assets = 1234.56, DailyProfit = -10.0, DailyRate = -0.81,
        Source = "alipay-confirmed-total", IsFinal = true, UpdatedAt = DateTime.UtcNow
    }
};
var archiveGuardSingleResult = DailyArchiveService.SanitizeArchiveRows(archiveGuardSingle);
if (archiveGuardSingleResult.DroppedCount != 0)
    throw new InvalidOperationException($"archiveGuard.caseB.dropped: expected 0 for single-fund portfolio, actual {archiveGuardSingleResult.DroppedCount}");
if (!archiveGuardSingleResult.Rows.Any(r => r.FundCode == "017968"))
    throw new InvalidOperationException("archiveGuard.caseB: single fund row must be preserved");
if (archiveGuardSingleResult.Rows.Count != 2)
    throw new InvalidOperationException($"archiveGuard.caseB.count: expected 2, actual {archiveGuardSingleResult.Rows.Count}");

Console.WriteLine("Archive guard regression passed.");

// ===== 数据损坏不变式守卫（经写入汇聚点 / nav-missing）回归测试 =====
// 用例 C：守卫在写入汇聚点生效 —— 多基金组合，一只基金 Assets≈TOTAL（损坏抄写指纹），
// 剔除损坏行并重算 TOTAL 为剩余基金 Assets 之和（容差 0.01）。
var caseCInput = new List<DailyArchive>
{
    new DailyArchive { Username = "test", FundCode = "110011", RecordDate = today, Assets = 2000.00, DailyProfit = 100.00, DailyRate = 5.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "110022", RecordDate = today, Assets = 3000.00, DailyProfit = 200.00, DailyRate = 6.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "110033", RecordDate = today, Assets = 5000.00, DailyProfit = 400.00, DailyRate = 8.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    // 损坏指纹：110044 的 Assets 等于 TOTAL 汇总值。
    new DailyArchive { Username = "test", FundCode = "110044", RecordDate = today, Assets = 10000.00, DailyProfit = 700.00, DailyRate = 7.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "TOTAL", RecordDate = today, Assets = 10000.00, DailyProfit = 1400.00, DailyRate = 7.0, Source = "alipay-confirmed-total", IsFinal = true, UpdatedAt = DateTime.UtcNow },
};
var caseCResult = DailyArchiveService.SanitizeArchiveRows(caseCInput);
if (caseCResult.DroppedCount != 1)
    throw new InvalidOperationException($"archiveGuard.caseC.dropped: expected 1, actual {caseCResult.DroppedCount}");
if (caseCResult.Rows.Any(r => r.FundCode == "110044"))
    throw new InvalidOperationException("archiveGuard.caseC: corrupt fund 110044 must be dropped");
var caseCTotal = caseCResult.Rows.FirstOrDefault(r => r.FundCode == "TOTAL");
if (caseCTotal == null)
    throw new InvalidOperationException("archiveGuard.caseC: TOTAL row must remain");
if (caseCTotal.Source != "guard-recomputed-total")
    throw new InvalidOperationException($"archiveGuard.caseC.source: expected guard-recomputed-total, actual {caseCTotal.Source}");
// 重算后 TOTAL.Assets == 剩余 3 只基金 Assets 之和 10000.00（容差 0.01）。
if (Math.Abs(caseCTotal.Assets - 10000.00) > 0.01)
    throw new InvalidOperationException($"archiveGuard.caseC.totalAssets: expected ~10000.00, actual {caseCTotal.Assets}");

// 用例 D：nav-missing 标记行（assets=0）不应被守卫误删，且 TOTAL 仅汇总有效基金。
var caseDInput = new List<DailyArchive>
{
    new DailyArchive { Username = "test", FundCode = "110011", RecordDate = today, Assets = 2000.00, DailyProfit = 100.00, DailyRate = 5.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "110022", RecordDate = today, Assets = 3000.00, DailyProfit = 200.00, DailyRate = 6.0, Source = "alipay-confirmed", IsFinal = true, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "110033", RecordDate = today, Assets = 0, DailyProfit = 0, DailyRate = 0, TotalProfit = 0, TotalRate = 0, Source = "nav-missing", IsFinal = false, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "TOTAL", RecordDate = today, Assets = 5000.00, DailyProfit = 300.00, DailyRate = 6.0, Source = "alipay-confirmed-total", IsFinal = true, UpdatedAt = DateTime.UtcNow },
};
var caseDResult = DailyArchiveService.SanitizeArchiveRows(caseDInput);
if (caseDResult.DroppedCount != 0)
    throw new InvalidOperationException($"archiveGuard.caseD.dropped: expected 0, actual {caseDResult.DroppedCount}");
if (!caseDResult.Rows.Any(r => r.FundCode == "110033" && r.Source == "nav-missing"))
    throw new InvalidOperationException("archiveGuard.caseD: nav-missing row must be preserved");
var caseDTotal = caseDResult.Rows.FirstOrDefault(r => r.FundCode == "TOTAL");
if (caseDTotal == null)
    throw new InvalidOperationException("archiveGuard.caseD: TOTAL row must remain");
// TOTAL 应保持 5000（仅汇总有效基金，不含 nav-missing 的 0）。
if (Math.Abs(caseDTotal.Assets - 5000.00) > 0.01)
    throw new InvalidOperationException($"archiveGuard.caseD.totalAssets: expected ~5000.00, actual {caseDTotal.Assets}");

// 用例 D2：全部基金净值缺失（TOTAL=0，多只 nav-missing）——守卫不应把 assets=0 误判为抄写指纹而误删。
var caseD2Input = new List<DailyArchive>
{
    new DailyArchive { Username = "test", FundCode = "110011", RecordDate = today, Assets = 0, DailyProfit = 0, DailyRate = 0, TotalProfit = 0, TotalRate = 0, Source = "nav-missing", IsFinal = false, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "110022", RecordDate = today, Assets = 0, DailyProfit = 0, DailyRate = 0, TotalProfit = 0, TotalRate = 0, Source = "nav-missing", IsFinal = false, UpdatedAt = DateTime.UtcNow },
    new DailyArchive { Username = "test", FundCode = "TOTAL", RecordDate = today, Assets = 0, DailyProfit = 0, DailyRate = 0, TotalProfit = 0, TotalRate = 0, Source = "nav-missing-total", IsFinal = false, UpdatedAt = DateTime.UtcNow },
};
var caseD2Result = DailyArchiveService.SanitizeArchiveRows(caseD2Input);
if (caseD2Result.DroppedCount != 0)
    throw new InvalidOperationException($"archiveGuard.caseD2.dropped: expected 0 (all-missing edge), actual {caseD2Result.DroppedCount}");
if (!caseD2Result.Rows.Any(r => r.FundCode == "110011" && r.Source == "nav-missing"))
    throw new InvalidOperationException("archiveGuard.caseD2: nav-missing fund must be preserved when TOTAL=0");

Console.WriteLine("Archive guard (write-path / nav-missing) regression passed.");

// ===== 板块雷达定时预热间隔（SectorRadarScheduleHelper）回归测试 =====
// 交易时段（周一 10:00）→ 2min；非交易（周一 20:00、周六 10:00）→ 30min。
var warmupTrading = SectorRadarScheduleHelper.GetWarmupInterval(new DateTime(2026, 7, 13, 10, 0, 0));
if (warmupTrading != TimeSpan.FromMinutes(2))
    throw new InvalidOperationException($"sectorWarmup.trading: expected 2min, actual {warmupTrading.TotalMinutes}min");

var warmupEvening = SectorRadarScheduleHelper.GetWarmupInterval(new DateTime(2026, 7, 13, 20, 0, 0));
if (warmupEvening != TimeSpan.FromMinutes(30))
    throw new InvalidOperationException($"sectorWarmup.evening: expected 30min, actual {warmupEvening.TotalMinutes}min");

var warmupWeekend = SectorRadarScheduleHelper.GetWarmupInterval(new DateTime(2026, 7, 11, 10, 0, 0));
if (warmupWeekend != TimeSpan.FromMinutes(30))
    throw new InvalidOperationException($"sectorWarmup.weekend: expected 30min, actual {warmupWeekend.TotalMinutes}min");

// 边界：11:35 仍属交易时段（闭区间），12:54 属午休非交易。
if (!SectorRadarScheduleHelper.IsTradingTime(new DateTime(2026, 7, 13, 11, 35, 0)))
    throw new InvalidOperationException("sectorWarmup.boundary.1135: inclusive upper bound must be trading");
if (SectorRadarScheduleHelper.IsTradingTime(new DateTime(2026, 7, 13, 12, 54, 0)))
    throw new InvalidOperationException("sectorWarmup.boundary.1254: lunch break must not be trading");

Console.WriteLine("Sector radar warmup interval regression passed.");
