using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class PortfolioSettlementService
    {
        public const string ShareSourceOcrAssetDetail = "ocr_asset_detail";
        public const string ShareSourceOcrNavDerived = "ocr_nav_derived";
        public const string ShareSourcePurchaseNavDerived = "purchase_nav_derived";
        public const string ShareSourcePurchaseConfirmed = "purchase_confirmed";
        public const string ShareSourceManual = "manual";
        public const string CostSourceOcrAssetDetail = "ocr_asset_detail";
        public const string CostSourceOcrHoldingDerived = "ocr_holding_derived";
        public const string CostSourcePurchaseAmount = "purchase_amount";
        public const string CostSourcePurchaseConfirmed = "purchase_confirmed";
        public const string CostSourceManual = "manual";

        public static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        public static string ChinaDateDash(DateTime? localTime = null)
            => (localTime ?? ChinaNow()).ToString("yyyy-MM-dd");

        private static bool IsPendingStatusActive(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            return !status.Equals("confirmed", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("settled", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
                && !status.Equals("canceled", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPendingDateEffective(string? pendingDate, string settleDate)
        {
            if (string.IsNullOrWhiteSpace(pendingDate)) return true;
            return string.CompareOrdinal(pendingDate, settleDate) <= 0;
        }

        private static bool IsPendingDateEffective(string? pendingDate, string settleDate, string? asOfDate)
        {
            if (IsPendingDateEffective(pendingDate, settleDate)) return true;
            return !string.IsNullOrWhiteSpace(asOfDate)
                && string.CompareOrdinal(pendingDate, asOfDate) <= 0;
        }

        private static bool IsPendingConfirmReachedForSettlement(string? confirmDate, string settleDate, string? asOfDate = null)
        {
            if (string.IsNullOrWhiteSpace(confirmDate)) return false;

            // asOfDate is supplied by current-display queries. The estimated confirmation
            // date alone is not proof that the platform has completed the transaction.
            if (!string.IsNullOrWhiteSpace(asOfDate)) return false;

            return string.CompareOrdinal(confirmDate, settleDate) <= 0;
        }

        public static double GetActivePendingBuyAmount(MyFundConfig fund, string settleDate, string? asOfDate = null)
        {
            double explicitPending = fund.PendingBuyAmount > 0
                && IsPendingStatusActive(fund.PendingTradeStatus)
                ? fund.PendingBuyAmount
                : 0;
            double legacyTodayAdd = explicitPending <= 0
                && (fund.LastTradeDate == settleDate
                    || (!string.IsNullOrWhiteSpace(asOfDate) && fund.LastTradeDate == asOfDate))
                && !IsPendingConfirmReachedForSettlement(fund.PendingConfirmDate, settleDate, asOfDate)
                && fund.LastAddAmount > 0
                ? fund.LastAddAmount
                : 0;
            return Math.Round(Math.Max(explicitPending, legacyTodayAdd), 2);
        }

        public static double GetReturnExcludedPendingBuyAmount(MyFundConfig fund, string settleDate)
        {
            double outstandingPending = GetActivePendingBuyAmount(fund, settleDate);
            if (outstandingPending <= 0) return 0;

            return IsPendingConfirmReachedForSettlement(fund.PendingConfirmDate, settleDate)
                ? 0
                : outstandingPending;
        }

        public static decimal GetConfirmedCostAmount(
            MyFundConfig fund,
            string settleDate,
            decimal fallbackDisplayAmount = 0m,
            string? asOfDate = null)
        {
            var cost = fund.CostAmount > 0
                ? PortfolioAccounting.Money(fund.CostAmount)
                : Math.Max(0m, PortfolioAccounting.Money(fallbackDisplayAmount));
            var pending = Math.Max(
                0m,
                PortfolioAccounting.Money(GetActivePendingBuyAmount(fund, settleDate, asOfDate)));

            // Manual buys temporarily add the submitted amount to CostAmount before
            // the registrar confirms shares. OCR/detail-page costs already describe
            // the confirmed position and must not have pending principal deducted twice.
            bool costIncludesPending = pending > 0m
                && (fund.CostAmount <= 0
                    || (!fund.CostAmountIsConfirmed
                        && string.Equals(
                            fund.CostAmountSource,
                            CostSourcePurchaseAmount,
                            StringComparison.OrdinalIgnoreCase)));

            return costIncludesPending
                ? Math.Max(0m, PortfolioAccounting.Money(cost - pending))
                : cost;
        }

        public static decimal GetHoldAmountBasis(MyFundConfig fund)
        {
            return fund.HoldAmountPrecise > 0m
                ? PortfolioAccounting.LedgerMoney(fund.HoldAmountPrecise)
                : PortfolioAccounting.LedgerMoney(fund.HoldAmount);
        }

        public static void SetHoldAmount(MyFundConfig fund, decimal ledgerAmount)
        {
            var amount = PortfolioAccounting.LedgerMoney(Math.Max(0m, ledgerAmount));
            fund.HoldAmountPrecise = amount;
            fund.HoldAmount = PortfolioAccounting.ToDouble(amount);
        }

        public static decimal GetLastSettledProfitBasis(MyFundConfig fund)
        {
            return fund.LastSettledProfitPrecise != 0m
                ? PortfolioAccounting.LedgerMoney(fund.LastSettledProfitPrecise)
                : PortfolioAccounting.LedgerMoney(fund.LastSettledProfit);
        }

        public static void SetLastSettledProfit(MyFundConfig fund, decimal ledgerProfit)
        {
            var profit = PortfolioAccounting.LedgerMoney(ledgerProfit);
            fund.LastSettledProfitPrecise = profit;
            fund.LastSettledProfit = PortfolioAccounting.ToDouble(profit);
        }

        public static bool ApplyShareCalibration(
            MyFundConfig fund,
            double shares,
            bool isConfirmed,
            string source)
        {
            if (shares < 0
                || (!isConfirmed && shares == 0)
                || string.IsNullOrWhiteSpace(source)) return false;

            bool protectsPurchaseNavShares = string.Equals(
                fund.HoldSharesSource,
                ShareSourcePurchaseNavDerived,
                StringComparison.OrdinalIgnoreCase);
            if (!isConfirmed && (fund.HoldSharesAreConfirmed || protectsPurchaseNavShares))
            {
                return false;
            }

            double normalizedShares = Math.Round(shares, 6);
            bool changed = Math.Abs(fund.HoldShares - normalizedShares) > 0.0000001
                || fund.HoldSharesAreConfirmed != isConfirmed
                || !string.Equals(fund.HoldSharesSource, source, StringComparison.OrdinalIgnoreCase);

            fund.HoldShares = normalizedShares;
            fund.HoldSharesAreConfirmed = isConfirmed;
            fund.HoldSharesSource = source;
            return changed;
        }

        public static bool ApplyCostCalibration(
            MyFundConfig fund,
            double costAmount,
            bool isConfirmed,
            string source)
        {
            if (costAmount <= 0 || string.IsNullOrWhiteSpace(source)) return false;

            bool protectsPurchaseCost = string.Equals(
                fund.CostAmountSource,
                CostSourcePurchaseAmount,
                StringComparison.OrdinalIgnoreCase);
            if (!isConfirmed && (fund.CostAmountIsConfirmed || protectsPurchaseCost))
            {
                return false;
            }

            double normalizedCost = Math.Round(costAmount, 2);
            bool changed = Math.Abs(fund.CostAmount - normalizedCost) > 0.001
                || fund.CostAmountIsConfirmed != isConfirmed
                || !string.Equals(fund.CostAmountSource, source, StringComparison.OrdinalIgnoreCase);

            fund.CostAmount = normalizedCost;
            fund.CostAmountIsConfirmed = isConfirmed;
            fund.CostAmountSource = source;
            return changed;
        }

        public double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            decimal baseAmount = GetHoldAmountBasis(fund);
            baseAmount -= PortfolioAccounting.LedgerMoney(GetReturnExcludedPendingBuyAmount(fund, settleDate));
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                baseAmount -= PortfolioAccounting.LedgerMoney(fund.LastAddAmount);
            }
            return Convert.ToDouble(Math.Max(0m, PortfolioAccounting.LedgerMoney(baseAmount)));
        }

        public double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetReturnExcludedPendingBuyAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                pending += fund.LastAddAmount;
            }
            return Math.Round(pending, 2);
        }

        public double GetDailyBaseAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetPendingTradeAmount(fund, settleDate);
            if (fund.LastSettledDate == settleDate)
            {
                var baseAmount = GetHoldAmountBasis(fund)
                    - PortfolioAccounting.LedgerMoney(pending)
                    - GetLastSettledProfitBasis(fund);
                return Convert.ToDouble(Math.Max(0m, PortfolioAccounting.LedgerMoney(baseAmount)));
            }
            return GetEffectiveBaseAmount(fund, settleDate);
        }

        public static double GetEffectiveShares(MyFundConfig fund, string settleDate)
        {
            if (fund.HoldShares <= 0) return 0;

            var activePendingBuy = GetReturnExcludedPendingBuyAmount(fund, settleDate);
            return activePendingBuy > 0 && fund.PendingBuyShares > 0
                ? Math.Max(0, fund.HoldShares - fund.PendingBuyShares)
                : fund.HoldShares;
        }

        public static bool HasReliableExactShares(MyFundConfig fund)
            => fund.HoldSharesAreConfirmed
               || string.Equals(fund.HoldSharesSource, ShareSourcePurchaseNavDerived, StringComparison.OrdinalIgnoreCase);

        public static bool CapturePendingBuyShares(MyFundConfig fund, string settleDate, double? purchaseNav)
        {
            if (!string.Equals(fund.PendingTradeStatus, "pending_buy", StringComparison.OrdinalIgnoreCase)
                || fund.PendingBuyAmount <= 0
                || fund.PendingBuyShares > 0
                || fund.PendingTradeDate != settleDate
                || !purchaseNav.HasValue
                || purchaseNav.Value <= 0)
            {
                return false;
            }

            var pendingShares = Math.Round(fund.PendingBuyAmount / purchaseNav.Value, 4);
            if (pendingShares <= 0) return false;

            fund.PendingBuyShares = pendingShares;
            fund.HoldShares = Math.Round(fund.HoldShares + pendingShares, 4);
            fund.HoldSharesAreConfirmed = false;
            fund.HoldSharesSource = ShareSourcePurchaseNavDerived;
            return true;
        }

        public static bool ConfirmPendingBuyIfDue(MyFundConfig fund, string settleDate)
        {
            if (!string.Equals(fund.PendingTradeStatus, "pending_buy", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(fund.PendingConfirmDate)
                || string.CompareOrdinal(fund.PendingConfirmDate, settleDate) > 0
                || fund.PendingBuyShares <= 0)
            {
                return false;
            }

            fund.PendingBuyAmount = 0;
            fund.PendingBuyShares = 0;
            fund.PendingTradeStatus = "confirmed";
            if (fund.LastAddAmount > 0) fund.LastAddAmount = 0;
            return true;
        }

        public static bool ConfirmPendingBuyWithPlatformShares(
            MyFundConfig fund,
            double confirmedShares,
            double? confirmedPurchaseAmount,
            string confirmDate)
        {
            if (!string.Equals(fund.PendingTradeStatus, "pending_buy", StringComparison.OrdinalIgnoreCase)
                || fund.PendingBuyAmount <= 0
                || confirmedShares <= 0)
            {
                return false;
            }

            if (confirmedPurchaseAmount.GetValueOrDefault() > 0
                && Math.Abs(confirmedPurchaseAmount.Value - fund.PendingBuyAmount) > 0.01)
            {
                throw new InvalidOperationException(
                    $"确认买入金额必须与待确认金额 {fund.PendingBuyAmount:F2} 一致。");
            }

            double pendingDerivedShares = Math.Max(0, fund.PendingBuyShares);
            double priorShares = Math.Max(0, fund.HoldShares - pendingDerivedShares);
            bool allSharesConfirmed = fund.HoldSharesAreConfirmed || priorShares <= 0.000001;

            fund.HoldShares = Math.Round(priorShares + confirmedShares, 6);
            fund.HoldSharesAreConfirmed = allSharesConfirmed;
            fund.HoldSharesSource = ShareSourcePurchaseConfirmed;
            fund.PendingBuyAmount = 0;
            fund.PendingBuyShares = 0;
            fund.PendingTradeStatus = "confirmed";
            fund.PendingConfirmDate = confirmDate;
            fund.PendingSource = "platform_transaction_confirmed";
            if (fund.LastAddAmount > 0) fund.LastAddAmount = 0;

            if (confirmedPurchaseAmount.GetValueOrDefault() > 0
                && string.Equals(fund.CostAmountSource, CostSourcePurchaseAmount, StringComparison.OrdinalIgnoreCase))
            {
                fund.CostAmountSource = CostSourcePurchaseConfirmed;
            }

            return true;
        }

        public bool ApplyOneDaySettlement(
            MyFundConfig fund,
            double actualRate,
            string settleDate,
            double? exactProfit = null,
            double? exactAssets = null)
        {
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            decimal settledProfitBasis = fund.OcrYesterdayDate == settleDate
                ? PortfolioAccounting.LedgerMoney(fund.OcrYesterdayIncome)
                : PortfolioAccounting.LedgerMoney(exactProfit ?? (baseAmount * (actualRate / 100.0)));
            double settledProfit = PortfolioAccounting.ToDouble(settledProfitBasis);
            decimal settledLedgerAmount = PortfolioAccounting.ResolveSettledLedgerAmount(
                Convert.ToDecimal(baseAmount),
                settledProfitBasis,
                Convert.ToDecimal(GetReturnExcludedPendingBuyAmount(fund, settleDate)),
                exactAssets.HasValue ? Convert.ToDecimal(exactAssets.Value) : null);
            double settledDisplayAmount = PortfolioAccounting.ToDouble(settledLedgerAmount);

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.LastSettledProfitPrecise - settledProfitBasis) > 0.0001m ||
                           Math.Abs(fund.HoldAmount - settledDisplayAmount) > 0.004 ||
                           Math.Abs(fund.HoldAmountPrecise - settledLedgerAmount) > 0.0001m;

            if (!changed) return false;

            SetHoldAmount(fund, settledLedgerAmount);
            fund.LastSettledDate = settleDate;
            SetLastSettledProfit(fund, settledProfitBasis);
            fund.LastSettledRate = Math.Round(actualRate, 4);
            return true;
        }

        public void AddPosition(MyFundConfig fund, double addAmount, string tradeDate, string? confirmDate = null)
        {
            if (addAmount <= 0) throw new ArgumentOutOfRangeException(nameof(addAmount), "加仓金额必须大于 0。");

            var existingPending = GetActivePendingBuyAmount(fund, tradeDate);
            if (string.Equals(fund.PendingTradeStatus, "pending_buy", StringComparison.OrdinalIgnoreCase)
                && fund.PendingBuyAmount > 0
                && existingPending <= 0)
            {
                throw new InvalidOperationException(
                    $"已有买入待确认（{fund.PendingTradeDate ?? "日期待核实"}，{fund.PendingBuyAmount:F2} 元），原记录仍在，请勿重复加仓；平台确认后系统会自动结转。");
            }
            if (existingPending > 0
                && (!string.Equals(fund.PendingTradeDate, tradeDate, StringComparison.Ordinal)
                    || fund.PendingBuyShares > 0))
            {
                throw new InvalidOperationException(
                    $"已有买入待确认（{fund.PendingTradeDate ?? "日期待核实"}，{fund.PendingBuyAmount:F2} 元），原记录仍在，请勿重复加仓；平台确认后系统会自动结转。");
            }

            SetHoldAmount(fund, GetHoldAmountBasis(fund) + PortfolioAccounting.LedgerMoney(addAmount));
            fund.CostAmount = Math.Round(fund.CostAmount + addAmount, 2);
            fund.CostAmountIsConfirmed = false;
            fund.CostAmountSource = CostSourcePurchaseAmount;
            fund.PendingBuyAmount = Math.Round(existingPending + addAmount, 2);
            fund.PendingBuyShares = 0;
            fund.PendingSellAmount = 0;
            fund.PendingSellShares = 0;
            fund.PendingSellCostAmount = 0;
            fund.PendingSellEstimatedProceeds = 0;
            fund.PendingTradeDate = tradeDate;
            fund.PendingTradeTime = ChinaNow().ToString("HH:mm:ss");
            fund.PendingTradeStatus = "pending_buy";
            fund.PendingSource = "manual_add_position";
            fund.PendingConfirmDate = string.IsNullOrWhiteSpace(confirmDate) ? fund.PendingConfirmDate : confirmDate;

            if (fund.LastTradeDate == tradeDate)
            {
                fund.LastAddAmount = Math.Round(fund.LastAddAmount + addAmount, 2);
            }
            else
            {
                fund.LastTradeDate = tradeDate;
                fund.LastAddAmount = Math.Round(addAmount, 2);
            }
        }

        public static bool IsPendingRedeem(MyFundConfig fund)
            => string.Equals(fund.PendingTradeStatus, "pending_sell", StringComparison.OrdinalIgnoreCase)
               && fund.PendingSellShares > 0;

        public static double GetSoldCost(MyFundConfig fund)
            => IsPendingRedeem(fund) ? Math.Max(0, fund.PendingSellAmount) : 0;

        public static bool ConfirmPendingSellSharesIfDue(
            MyFundConfig fund,
            string settleDate,
            double? tradeNav)
        {
            if (!IsPendingRedeem(fund)
                || string.IsNullOrWhiteSpace(fund.PendingConfirmDate)
                || string.CompareOrdinal(fund.PendingConfirmDate, settleDate) > 0
                || !tradeNav.HasValue
                || tradeNav.Value <= 0)
            {
                return false;
            }

            double remainingShares = Math.Max(0, fund.HoldShares - fund.PendingSellShares);
            double remainingAmount = Math.Round(remainingShares * tradeNav.Value, 4);
            double estimatedProceeds = Math.Round(fund.PendingSellShares * tradeNav.Value, 2);
            return ApplyPendingSellShareSettlement(
                fund,
                remainingShares,
                remainingAmount,
                estimatedProceeds,
                settleDate,
                "official_nav_share_settlement");
        }

        public static bool ConfirmPendingSellFromPlatformHolding(
            MyFundConfig fund,
            double confirmedRemainingShares,
            double confirmedHoldAmount,
            string confirmDate)
        {
            if (!IsPendingRedeem(fund)
                || confirmedRemainingShares < 0
                || confirmedHoldAmount < 0)
            {
                return false;
            }

            double expectedRemainingShares = Math.Max(0, fund.HoldShares - fund.PendingSellShares);
            if (Math.Abs(expectedRemainingShares - confirmedRemainingShares) > 0.0001)
            {
                return false;
            }

            return ApplyPendingSellShareSettlement(
                fund,
                confirmedRemainingShares,
                confirmedHoldAmount,
                estimatedProceeds: 0,
                confirmDate,
                "ocr_asset_detail_sell_settlement");
        }

        private static bool ApplyPendingSellShareSettlement(
            MyFundConfig fund,
            double remainingShares,
            double remainingAmount,
            double estimatedProceeds,
            string confirmDate,
            string source)
        {
            double oldShares = fund.HoldShares;
            if (oldShares <= 0 || fund.PendingSellShares <= 0) return false;

            double soldCost = fund.CostAmount / oldShares * fund.PendingSellShares;
            fund.PendingSellCostAmount = Math.Round(soldCost, 2);
            fund.PendingSellEstimatedProceeds = Math.Round(Math.Max(0, estimatedProceeds), 2);
            fund.PendingSellAmount = 0;
            fund.HoldShares = Math.Round(Math.Max(0, remainingShares), 6);
            fund.CostAmount = Math.Round(Math.Max(0, fund.CostAmount - soldCost), 2);
            SetHoldAmount(fund, PortfolioAccounting.LedgerMoney(remainingAmount));
            fund.PendingTradeStatus = "shares_confirmed";
            fund.PendingConfirmDate = confirmDate;
            fund.PendingSource = source;
            fund.LastAddAmount = 0;

            if (fund.HoldShares <= 0)
            {
                fund.CostAmount = 0;
                fund.CostAmountIsConfirmed = false;
                fund.CostAmountSource = null;
                fund.HoldSharesAreConfirmed = false;
                fund.HoldSharesSource = null;
                fund.PlatformHoldingAdjustment = 0;
                SetHoldAmount(fund, 0m);
            }

            return true;
        }

        public double ReducePosition(MyFundConfig fund, double reduceShares, double? reduceAmount, string tradeDate, string? confirmDate = null)
        {
            if (reduceShares <= 0) throw new ArgumentOutOfRangeException(nameof(reduceShares), "减仓份额必须大于 0。");

            bool confirmsSharesSettledSell = reduceAmount.GetValueOrDefault() > 0
                && string.Equals(fund.PendingTradeStatus, "shares_confirmed", StringComparison.OrdinalIgnoreCase)
                && fund.PendingSellShares > 0;
            if (confirmsSharesSettledSell)
            {
                if (Math.Abs(reduceShares - fund.PendingSellShares) > 0.0001)
                    throw new InvalidOperationException($"到账份额必须与已确认赎回份额 {fund.PendingSellShares:F4} 一致。");

                double profit = reduceAmount!.Value - fund.PendingSellCostAmount;
                fund.RealizedProfit = Math.Round(fund.RealizedProfit + profit, 2);
                if (fund.HoldShares > 0)
                {
                    fund.PlatformHoldingAdjustment = Math.Round(fund.PlatformHoldingAdjustment - profit, 2);
                }
                else
                {
                    fund.PlatformHoldingAdjustment = 0;
                }

                fund.PendingSellAmount = 0;
                fund.PendingSellShares = 0;
                fund.PendingSellCostAmount = 0;
                fund.PendingSellEstimatedProceeds = 0;
                fund.PendingTradeStatus = "confirmed";
                fund.PendingConfirmDate = string.IsNullOrWhiteSpace(confirmDate)
                    ? fund.PendingConfirmDate
                    : confirmDate;
                fund.PendingSource = "platform_proceeds_confirmed";
                return Math.Round(profit, 2);
            }

            if (fund.HoldShares <= 0) throw new InvalidOperationException("当前基金未记录有效份额，无法按份额减仓。");

            bool confirmsPendingSell = reduceAmount.GetValueOrDefault() > 0
                && string.Equals(fund.PendingTradeStatus, "pending_sell", StringComparison.OrdinalIgnoreCase)
                && fund.PendingSellShares > 0;
            if (confirmsPendingSell)
            {
                if (Math.Abs(reduceShares - fund.PendingSellShares) > 0.0001)
                    throw new InvalidOperationException($"确认份额必须与待确认赎回份额 {fund.PendingSellShares:F4} 一致。");
                tradeDate = string.IsNullOrWhiteSpace(fund.PendingTradeDate) ? tradeDate : fund.PendingTradeDate;
            }
            else if (!reduceAmount.GetValueOrDefault().Equals(0d)
                     && string.Equals(fund.PendingTradeStatus, "pending_sell", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("已有赎回待确认，请先完成原交易确认。");
            }

            if (reduceShares > fund.HoldShares) throw new InvalidOperationException("减仓份额不能大于持仓份额。");

            double oldShares = fund.HoldShares;
            double unitCost = fund.CostAmount / oldShares;
            decimal holdAmountBasis = GetHoldAmountBasis(fund);
            decimal unitAmount = holdAmountBasis / Convert.ToDecimal(oldShares);
            double soldCost = unitCost * reduceShares;
            double confirmedAmount = reduceAmount.GetValueOrDefault();
            bool hasConfirmed = confirmedAmount > 0;

            if (hasConfirmed)
            {
                // Confirmed: calculate realized profit normally
                double profit = confirmedAmount - soldCost;
                fund.HoldShares = Math.Round(fund.HoldShares - reduceShares, 4);
                fund.CostAmount = Math.Round(fund.CostAmount - soldCost, 2);
                SetHoldAmount(fund, holdAmountBasis - unitAmount * Convert.ToDecimal(reduceShares));
                if (fund.HoldShares <= 0)
                {
                    fund.CostAmount = 0;
                    fund.CostAmountIsConfirmed = false;
                    fund.CostAmountSource = null;
                    fund.HoldSharesAreConfirmed = false;
                    fund.HoldSharesSource = null;
                    fund.PlatformHoldingAdjustment = 0;
                    SetHoldAmount(fund, 0m);
                }
                fund.RealizedProfit = Math.Round(fund.RealizedProfit + profit, 2);
                if (fund.HoldShares > 0)
                {
                    fund.PlatformHoldingAdjustment = Math.Round(fund.PlatformHoldingAdjustment - profit, 2);
                }

                if (fund.LastTradeDate == tradeDate)
                    fund.LastAddAmount = Math.Round(fund.LastAddAmount - confirmedAmount, 2);
                else
                {
                    fund.LastTradeDate = tradeDate;
                    fund.LastAddAmount = Math.Round(-confirmedAmount, 2);
                }
                fund.PendingSellAmount = 0;
                fund.PendingSellShares = 0;
                fund.PendingSellCostAmount = 0;
                fund.PendingSellEstimatedProceeds = 0;
                if (fund.PendingTradeStatus == "pending_sell")
                {
                    fund.PendingTradeStatus = "confirmed";
                    fund.PendingConfirmDate = string.IsNullOrWhiteSpace(confirmDate) ? fund.PendingConfirmDate : confirmDate;
                }
                return Math.Round(profit, 2);
            }
            else
            {
                if (string.Equals(fund.PendingTradeStatus, "pending_sell", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("已有赎回待确认，请勿重复提交。");

                // A redemption only becomes effective after registration confirms it.
                // Keep confirmed shares/assets unchanged so the later confirmation can settle once.
                fund.PendingSellAmount = Math.Round(Convert.ToDouble(unitAmount * Convert.ToDecimal(reduceShares)), 2);
                fund.PendingSellShares = Math.Round(reduceShares, 4);
                fund.PendingSellCostAmount = Math.Round(soldCost, 2);
                fund.PendingSellEstimatedProceeds = 0;
                fund.PendingTradeDate = tradeDate;
                fund.PendingTradeTime = ChinaNow().ToString("HH:mm:ss");
                fund.PendingTradeStatus = "pending_sell";
                fund.PendingConfirmDate = string.IsNullOrWhiteSpace(confirmDate) ? fund.PendingConfirmDate : confirmDate;
                fund.PendingSource = "manual_reduce_position";
                fund.LastTradeDate = tradeDate;
                fund.LastAddAmount = 0;
                return 0;
            }
        }

        public List<DailyArchive> BuildArchiveRowsFromCurrentHoldings(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            string dateDash = date.ToString("yyyy-MM-dd");
            var rows = new List<DailyArchive>();
            var confirmedMoney = new List<ConfirmedHoldingMoney>();
            var expectedActiveCount = 0;

            foreach (var fund in funds)
            {
                decimal pendingBuyAmount = PortfolioAccounting.Money(GetActivePendingBuyAmount(fund, dateDash));
                decimal confirmedHoldAmount = Math.Max(0m, PortfolioAccounting.Money(fund.HoldAmount) - pendingBuyAmount);
                if (confirmedHoldAmount <= 0.01m) continue;
                expectedActiveCount++;

                // 正式历史档案只能来自蚂蚁 OCR 确认字段。官方净值和盘中估值仅用于临时估算。
                if (fund.OcrYesterdayDate != dateDash) continue;

                decimal dailyProfit = PortfolioAccounting.Money(fund.OcrYesterdayIncome);
                decimal baseAmount = Math.Max(0m, confirmedHoldAmount - dailyProfit);
                decimal totalProfit = PortfolioAccounting.Money(fund.OcrHoldingIncome);

                rows.Add(new DailyArchive
                {
                    Username = username,
                    FundCode = fund.FundCode,
                    FundName = fund.FundName,
                    RecordDate = date,
                    Assets = PortfolioAccounting.ToDouble(confirmedHoldAmount),
                    DailyProfit = PortfolioAccounting.ToDouble(dailyProfit),
                    DailyRate = Convert.ToDouble(PortfolioAccounting.Percent(dailyProfit, baseAmount)),
                    TotalProfit = PortfolioAccounting.ToDouble(totalProfit),
                    TotalRate = Convert.ToDouble(PortfolioAccounting.HoldingProfitRate(totalProfit, confirmedHoldAmount)),
                    Source = "alipay-confirmed",
                    IsFinal = true,
                    UpdatedAt = DateTime.UtcNow
                });
                confirmedMoney.Add(new ConfirmedHoldingMoney(confirmedHoldAmount, dailyProfit, totalProfit));
            }

            if (rows.Count == 0) return rows;

            var summary = PortfolioAccounting.Calculate(confirmedMoney, 0m);
            decimal totalDailyBase = confirmedMoney.Sum(x => x.ConfirmedAmount - x.YesterdayProfit);
            decimal totalCost = summary.AntConfirmedAmount - summary.AntHoldingProfit;
            bool totalIsFinal = rows.Count == expectedActiveCount;
            rows.Add(new DailyArchive
            {
                Username = username,
                FundCode = "TOTAL",
                FundName = "总持仓",
                RecordDate = date,
                Assets = PortfolioAccounting.ToDouble(summary.AntConfirmedAmount),
                DailyProfit = PortfolioAccounting.ToDouble(summary.ConfirmedYesterdayProfit),
                DailyRate = Convert.ToDouble(PortfolioAccounting.Percent(summary.ConfirmedYesterdayProfit, totalDailyBase)),
                TotalProfit = PortfolioAccounting.ToDouble(summary.AntHoldingProfit),
                TotalRate = Convert.ToDouble(PortfolioAccounting.Percent(summary.AntHoldingProfit, totalCost)),
                Source = totalIsFinal ? "alipay-confirmed-total" : "alipay-confirmed-partial",
                IsFinal = totalIsFinal,
                UpdatedAt = DateTime.UtcNow
            });

            return rows;
        }
    }
}
