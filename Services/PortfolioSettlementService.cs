using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class PortfolioSettlementService
    {
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

        private static bool IsPendingConfirmAfter(string? confirmDate, string settleDate)
        {
            return !string.IsNullOrWhiteSpace(confirmDate)
                && string.CompareOrdinal(confirmDate, settleDate) > 0;
        }

        public static double GetActivePendingBuyAmount(MyFundConfig fund, string settleDate)
        {
            double explicitPending = fund.PendingBuyAmount > 0
                && IsPendingStatusActive(fund.PendingTradeStatus)
                && (IsPendingDateEffective(fund.PendingTradeDate, settleDate)
                    || IsPendingConfirmAfter(fund.PendingConfirmDate, settleDate))
                ? fund.PendingBuyAmount
                : 0;
            double legacyTodayAdd = fund.LastTradeDate == settleDate && fund.LastAddAmount > 0
                ? fund.LastAddAmount
                : 0;
            return Math.Round(Math.Max(explicitPending, legacyTodayAdd), 2);
        }

        public double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            double baseAmount = fund.HoldAmount;
            baseAmount -= GetActivePendingBuyAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && fund.LastAddAmount < 0)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        public double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
        {
            double pending = GetActivePendingBuyAmount(fund, settleDate);
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
                return Math.Max(0, Math.Round(fund.HoldAmount - pending - fund.LastSettledProfit, 4));
            }
            return GetEffectiveBaseAmount(fund, settleDate);
        }

        public double GetEffectiveShares(MyFundConfig fund, string settleDate)
        {
            if (fund.HoldShares <= 0) return 0;

            double baseAmount = GetEffectiveBaseAmount(fund, settleDate);
            if (fund.LastTradeDate == settleDate && Math.Abs(fund.LastAddAmount) > 0.000001 && fund.HoldAmount > 0)
            {
                return Math.Max(0, fund.HoldShares * (baseAmount / fund.HoldAmount));
            }

            return fund.HoldShares;
        }

        public bool ApplyOneDaySettlement(MyFundConfig fund, double actualRate, string settleDate, double? exactProfit = null)
        {
            double baseAmount = GetDailyBaseAmount(fund, settleDate);
            double settledProfit = fund.OcrYesterdayDate == settleDate
                ? Math.Round(fund.OcrYesterdayIncome, 2)
                : Math.Round(exactProfit ?? (baseAmount * (actualRate / 100.0)), 2);

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01;

            if (!changed) return false;

            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);
            return true;
        }

        public void AddPosition(MyFundConfig fund, double addAmount, string tradeDate)
        {
            if (addAmount <= 0) throw new ArgumentOutOfRangeException(nameof(addAmount), "加仓金额必须大于 0。");

            fund.HoldAmount = Math.Round(fund.HoldAmount + addAmount, 2);
            fund.CostAmount = Math.Round(fund.CostAmount + addAmount, 2);
            fund.PendingBuyAmount = Math.Round(GetActivePendingBuyAmount(fund, tradeDate) + addAmount, 2);
            fund.PendingSellAmount = 0;
            fund.PendingTradeDate = tradeDate;
            fund.PendingTradeTime = ChinaNow().ToString("HH:mm:ss");
            fund.PendingTradeStatus = "pending_buy";
            fund.PendingSource = "manual_add_position";

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

        // Pending sell marker: -(soldCost + 1) in LastAddAmount when HoldShares=0 and no confirmed amount
        private const double PendingMarkerOffset = 1.0;

        public static bool IsPendingRedeem(MyFundConfig fund)
            => fund.HoldShares <= 0 && fund.LastAddAmount < -PendingMarkerOffset;

        public static double GetSoldCost(MyFundConfig fund)
            => IsPendingRedeem(fund) ? Math.Abs(fund.LastAddAmount) - PendingMarkerOffset : 0;

        public double ReducePosition(MyFundConfig fund, double reduceShares, double? reduceAmount, string tradeDate)
        {
            if (reduceShares <= 0) throw new ArgumentOutOfRangeException(nameof(reduceShares), "减仓份额必须大于 0。");
            if (fund.HoldShares <= 0) throw new InvalidOperationException("当前基金未记录有效份额，无法按份额减仓。");
            if (reduceShares > fund.HoldShares) throw new InvalidOperationException("减仓份额不能大于持仓份额。");

            double oldShares = fund.HoldShares;
            double unitCost = fund.CostAmount / oldShares;
            double unitAmount = fund.HoldAmount / oldShares;
            double soldCost = unitCost * reduceShares;
            bool isFullSell = Math.Abs(reduceShares - oldShares) < 0.0001;

            double confirmedAmount = reduceAmount.GetValueOrDefault();
            bool hasConfirmed = confirmedAmount > 0;

            if (hasConfirmed)
            {
                // Confirmed: calculate realized profit normally
                double profit = confirmedAmount - soldCost;
                fund.HoldShares = Math.Round(fund.HoldShares - reduceShares, 4);
                fund.CostAmount = Math.Round(fund.CostAmount - soldCost, 2);
                fund.HoldAmount = Math.Round(fund.HoldAmount - unitAmount * reduceShares, 2);
                if (fund.HoldShares <= 0) { fund.CostAmount = 0; fund.HoldAmount = 0; }
                fund.RealizedProfit = Math.Round(fund.RealizedProfit + profit, 2);

                if (fund.LastTradeDate == tradeDate)
                    fund.LastAddAmount = Math.Round(fund.LastAddAmount - confirmedAmount, 2);
                else
                {
                    fund.LastTradeDate = tradeDate;
                    fund.LastAddAmount = Math.Round(-confirmedAmount, 2);
                }
                return Math.Round(profit, 2);
            }
            else
            {
                // No confirmed amount: mark as pending
                fund.HoldShares = Math.Round(fund.HoldShares - reduceShares, 4);
                if (isFullSell)
                {
                    // Preserve original cost for display; soldCost encoded in LastAddAmount
                    fund.LastTradeDate = tradeDate;
                    fund.LastAddAmount = Math.Round(-(soldCost + PendingMarkerOffset), 2);
                    fund.PendingSellAmount = Math.Round(soldCost, 2);
                    fund.PendingTradeDate = tradeDate;
                    fund.PendingTradeTime = ChinaNow().ToString("HH:mm:ss");
                    fund.PendingTradeStatus = "pending_sell";
                    fund.PendingSource = "manual_reduce_position";
                    fund.CostAmount = 0;
                    fund.HoldAmount = 0;
                }
                else
                {
                    fund.CostAmount = Math.Round(fund.CostAmount - soldCost, 2);
                    fund.LastTradeDate = tradeDate;
                    fund.LastAddAmount = Math.Round(-soldCost, 2);
                    fund.HoldAmount = Math.Round(fund.HoldAmount - unitAmount * reduceShares, 2);
                }
                // RealizedProfit NOT updated — waiting for confirmed amount
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
                    TotalRate = Math.Round(fund.OcrHoldingRate, 2, MidpointRounding.AwayFromZero),
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
