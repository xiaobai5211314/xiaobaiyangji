using 小白养基.Models;

namespace 小白养基.Services
{
    public sealed class PortfolioSettlementService
    {
        public static DateTime ChinaNow() => DateTime.UtcNow.AddHours(8);

        public static string ChinaDateDash(DateTime? localTime = null)
            => (localTime ?? ChinaNow()).ToString("yyyy-MM-dd");

        public double GetEffectiveBaseAmount(MyFundConfig fund, string settleDate)
        {
            double baseAmount = fund.HoldAmount;
            if (fund.LastTradeDate == settleDate)
            {
                baseAmount -= fund.LastAddAmount;
            }
            return Math.Max(0, Math.Round(baseAmount, 4));
        }

        public double GetPendingTradeAmount(MyFundConfig fund, string settleDate)
            => fund.LastTradeDate == settleDate ? fund.LastAddAmount : 0;

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
            double pending = GetPendingTradeAmount(fund, settleDate);
            double settledProfit = Math.Round(exactProfit ?? (baseAmount * (actualRate / 100.0)), 2);
            double newHoldAmount = Math.Round(baseAmount + settledProfit + pending, 2);

            bool changed = fund.LastSettledDate != settleDate ||
                           Math.Abs(fund.LastSettledRate - actualRate) > 0.0001 ||
                           Math.Abs(fund.LastSettledProfit - settledProfit) > 0.01 ||
                           Math.Abs(fund.HoldAmount - newHoldAmount) > 0.01;

            if (!changed) return false;

            fund.HoldAmount = newHoldAmount;
            fund.LastSettledDate = settleDate;
            fund.LastSettledProfit = settledProfit;
            fund.LastSettledRate = Math.Round(actualRate, 4);
            return true;
        }

        public void AddPosition(MyFundConfig fund, double addAmount, string tradeDate)
        {
            if (addAmount <= 0) throw new ArgumentOutOfRangeException(nameof(addAmount), "加仓金额必须大于 0。");

            double previousAmount = fund.HoldAmount;
            double previousShares = fund.HoldShares;

            fund.HoldAmount = Math.Round(fund.HoldAmount + addAmount, 2);
            fund.CostAmount = Math.Round(fund.CostAmount + addAmount, 2);

            if (previousAmount > 0 && previousShares > 0)
            {
                double estimatedAddShares = addAmount / (previousAmount / previousShares);
                if (estimatedAddShares > 0 && !double.IsNaN(estimatedAddShares) && !double.IsInfinity(estimatedAddShares))
                {
                    fund.HoldShares = Math.Round(previousShares + estimatedAddShares, 6);
                }
            }

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

        public double GetRecordRateForToday(FundData? record)
        {
            if (record == null) return 0;
            return Math.Abs(record.ActualRate) > 0.000001 ? record.ActualRate : record.EstimatedRate;
        }

        public List<DailyArchive> BuildArchiveRowsFromCurrentHoldings(string username, DateTime date, List<MyFundConfig> funds, List<FundData> todayRecords)
        {
            string dateDash = date.ToString("yyyy-MM-dd");
            var latestRecordDict = todayRecords
                .GroupBy(r => r.FundCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.FetchTime).First());

            var rows = new List<DailyArchive>();
            double totalCost = 0;
            double totalRealized = 0;
            double totalDailyProfit = 0;
            double totalDailyBase = 0;
            double totalCurrentAssets = 0;

            foreach (var fund in funds)
            {
                if (fund.HoldShares <= 0)
                {
                    totalRealized += fund.RealizedProfit;
                    continue;
                }
                latestRecordDict.TryGetValue(fund.FundCode, out var record);

                double cost = fund.CostAmount > 0 ? fund.CostAmount : fund.HoldAmount;
                double baseAmount = GetDailyBaseAmount(fund, dateDash);
                double dailyRate = fund.LastSettledDate == dateDash ? fund.LastSettledRate : GetRecordRateForToday(record);
                double dailyProfit = fund.LastSettledDate == dateDash
                    ? fund.LastSettledProfit
                    : Math.Round(baseAmount * (dailyRate / 100.0), 2);
                double currentAssets = fund.LastSettledDate == dateDash
                    ? fund.HoldAmount
                    : Math.Round(fund.HoldAmount + dailyProfit, 2);

                double totalProfit = currentAssets - cost + fund.RealizedProfit;
                double totalRate = cost > 0 ? totalProfit / cost * 100.0 : 0;

                rows.Add(new DailyArchive
                {
                    Username = username,
                    FundCode = fund.FundCode,
                    FundName = fund.FundName,
                    RecordDate = date,
                    Assets = Math.Round(currentAssets, 2),
                    DailyProfit = Math.Round(dailyProfit, 2),
                    DailyRate = Math.Round(dailyRate, 2),
                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalRate = Math.Round(totalRate, 2)
                });

                totalCost += cost;
                totalRealized += fund.RealizedProfit;
                totalDailyProfit += dailyProfit;
                totalDailyBase += baseAmount;
                totalCurrentAssets += currentAssets;
            }

            rows.Add(new DailyArchive
            {
                Username = username,
                FundCode = "TOTAL",
                FundName = "总持仓",
                RecordDate = date,
                Assets = Math.Round(totalCurrentAssets, 2),
                DailyProfit = Math.Round(totalDailyProfit, 2),
                DailyRate = Math.Round(totalDailyBase > 0 ? totalDailyProfit / totalDailyBase * 100.0 : 0, 2),
                TotalProfit = Math.Round(totalCurrentAssets - totalCost + totalRealized, 2),
                TotalRate = Math.Round(totalCost > 0 ? (totalCurrentAssets - totalCost + totalRealized) / totalCost * 100.0 : 0, 2)
            });

            return rows;
        }
    }
}
