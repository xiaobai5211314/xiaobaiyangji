// 从旧版 wwwroot/index.html 提取，公式完全一致，禁止修改

const toNumber = (value, fallback = 0) => {
  const n = Number(value)
  return Number.isFinite(n) ? n : fallback
}

const round2 = (value) => Number(toNumber(value, 0).toFixed(2))

const toFiniteNumberOrNull = (value) => {
  const n = Number(value)
  return Number.isFinite(n) ? n : null
}

export const isClearedFund = (fund) => {
  if (!fund) return false
  if (fund.inactiveHolding === true || fund.isClearedHolding === true) return true
  const shares = toNumber(fund.shares, 0)
  const todayAmount = toNumber(fund.marketValue ?? fund.todayAmount, 0)
  const amount = toNumber(fund.amount, 0)
  const hasClearedProfit = toNumber(fund.realizedProfit, 0) !== 0 || toNumber(fund.platformCumulativeProfit, 0) !== 0
  return shares <= 0 && todayAmount <= 0 && amount <= 0 && hasClearedProfit
}

export const isActiveHoldingFund = (fund) => {
  if (!fund || isClearedFund(fund)) return false
  return [
    fund.marketValue,
    fund.todayAmount,
    fund.amount,
    fund.confirmedAmount,
    fund.shares,
    fund.pendingBuyAmount
  ].some(value => toNumber(value, 0) > 0) || fund.pendingBuy === true
}

const getClearedProfit = (fund) => {
  const platform = toNumber(fund?.platformCumulativeProfit, 0)
  if (platform !== 0) return { value: platform, source: '平台累计' }
  const realized = toNumber(fund?.realizedProfit, 0)
  if (realized !== 0) return { value: realized, source: '系统已实现' }
  return { value: 0, source: '系统已实现' }
}

const calcBreakEvenRateFromHoldingRate = (holdingRate) => {
  const r = Number(holdingRate)
  if (!Number.isFinite(r) || r >= 0 || r <= -100) return 0
  return Math.round((Math.abs(r) / (100 + r) * 100) * 100) / 100
}

export const calcBreakEvenRate = (fund) => {
  const marketValue = Number(fund?.marketValue ?? fund?.todayAmount ?? fund?.amount ?? 0)
  const holdingProfit = Number(fund?.holdingProfit ?? fund?.estimatedProfit ?? fund?.holdingIncome ?? 0)
  if (fund?.inactiveHolding || fund?.isClearedHolding || isClearedFund(fund)) return 0
  if (!Number.isFinite(marketValue) || marketValue <= 0) return 0
  if (!Number.isFinite(holdingProfit) || holdingProfit >= 0) return 0
  return Math.round((Math.abs(holdingProfit) / marketValue * 100) * 100) / 100
}

export const normalizeFundForDashboard = (rawFund, context = {}) => {
  const raw = rawFund || {}
  const data = Array.isArray(raw.data) ? raw.data : []
  const { raw: _ignoredRaw, ...rawSnapshot } = raw
  rawSnapshot.data = data
  const pendingBuyAmount = Math.max(0, round2(context.pendingBuyAmount ?? raw.pendingBuyAmount))
  const shares = toNumber(raw.shares, 0)
  const costAmount = round2(raw.costAmount ?? raw.cost ?? raw.rawCostAmount ?? raw.confirmedCost)
  const rawHoldAmount = round2(raw.rawHoldAmount ?? raw.amount)
  const confirmedAmount = Math.max(0, round2(context.confirmedAmount ?? raw.confirmedAmount ?? (rawHoldAmount - pendingBuyAmount)))
  const baseAmountForToday = Math.max(0, round2(context.baseAmountForToday ?? raw.todayBaseAmount ?? confirmedAmount))
  const clearedInfo = getClearedProfit(raw)
  const clearedHolding = isClearedFund(raw)
  let todayRate = clearedHolding ? 0 : round2(context.todayRate ?? raw.todayRate ?? raw.currentRate ?? raw.todayRateForSimulation)
  const fallbackTodayProfit = baseAmountForToday > 0 ? baseAmountForToday * todayRate / 100 : 0
  const todayProfit = clearedHolding ? 0 : round2(context.todayProfit ?? raw.todayProfit ?? raw.todayProfitPreview ?? fallbackTodayProfit)
  const rateMissing = raw.rateSource === 'missing' || !Number.isFinite(Number(context.todayRate ?? raw.todayRate ?? raw.currentRate ?? raw.todayRateForSimulation))
  if (!clearedHolding && rateMissing && baseAmountForToday > 0 && Number.isFinite(Number(todayProfit))) {
    todayRate = round2(todayProfit / baseAmountForToday * 100)
  }
  const rawMarketValue = context.marketValue ?? raw.marketValue ?? raw.todayAmount
  const marketValue = clearedHolding ? 0 : round2(toNumber(rawMarketValue, rawHoldAmount + todayProfit))
  const activeCandidate = {
    ...raw,
    pendingBuyAmount,
    shares,
    amount: rawHoldAmount,
    confirmedAmount,
    todayAmount: marketValue,
    marketValue
  }
  const activeHolding = !clearedHolding && isActiveHoldingFund(activeCandidate)
  const activeRealizedProfit = activeHolding ? round2(raw.displayedProfit ?? raw.realizedProfit) : 0
  const apiHoldingProfit = round2(raw.holdingIncome ?? raw.estimatedProfit)
  const holdingProfit = activeHolding
    ? round2(costAmount > 0 ? (marketValue - costAmount + activeRealizedProfit) : apiHoldingProfit)
    : 0
  const holdingRate = activeHolding
    ? round2(costAmount > 0 ? (holdingProfit / costAmount * 100) : (raw.existingReturnRate ?? raw.holdingRate ?? 0))
    : 0
  const breakEvenRate = activeHolding && holdingProfit < 0
    ? (calcBreakEvenRate({
        ...raw,
        inactiveHolding: !activeHolding,
        isClearedHolding: clearedHolding,
        marketValue,
        todayAmount: marketValue,
        amount: marketValue,
        holdingProfit,
        estimatedProfit: holdingProfit,
        holdingRate
      }) || calcBreakEvenRateFromHoldingRate(holdingRate))
    : 0
  const accountAmount = round2(confirmedAmount + pendingBuyAmount)
  const todayPendingBuyAmount = pendingBuyAmount
  const confirmedHoldingAmount = Math.max(0, round2(accountAmount - todayPendingBuyAmount))
  const todayProfitBaseAmount = baseAmountForToday
  const estimatedAccountAmount = clearedHolding ? 0 : round2(todayProfitBaseAmount + todayProfit + todayPendingBuyAmount)
  const estimatedConfirmedHoldingAmount = clearedHolding ? 0 : round2(todayProfitBaseAmount + todayProfit)
  return {
    ...rawSnapshot,
    raw: rawSnapshot,
    data,
    code: raw.code || raw.fundCode || '',
    name: raw.name || raw.fundName || '',
    isActiveHolding: activeHolding,
    isClearedHolding: clearedHolding,
    accountAmount,
    todayPendingBuyAmount,
    pendingBuyAmount: todayPendingBuyAmount,
    confirmedHoldingAmount,
    todayProfitBaseAmount,
    baseAmountForToday: todayProfitBaseAmount,
    estimatedAccountAmount,
    estimatedConfirmedHoldingAmount,
    costAmount,
    shares,
    marketValue: estimatedAccountAmount,
    todayProfit,
    todayRate,
    holdingProfit,
    holdingRate,
    breakEvenRate,
    clearedProfit: round2(clearedInfo.value),
    clearedProfitSource: clearedInfo.source,
    calibrationOffset: round2(raw.calibrationOffset),
    calibrationSamples: toNumber(raw.calibrationSamples, 0),
    calibrationNote: raw.calibrationNote || '',
    currentRate: todayRate,
    todayAmount: estimatedAccountAmount.toFixed(2),
    estimatedProfit: holdingProfit.toFixed(2),
    existingReturnRate: holdingRate.toFixed(2)
  }
}

export const calcActivePortfolioSummary = (activeFunds = []) => {
  const rows = Array.isArray(activeFunds) ? activeFunds.filter(isActiveHoldingFund) : []
  const safeNum = (v) => { const n = Number(v); return Number.isFinite(n) ? n : 0 }
  const accountTotalAmount = round2(rows.reduce((s, f) => s + safeNum(f.estimatedAccountAmount), 0))
  const todayPendingBuyTotal = round2(rows.reduce((s, f) => s + safeNum(f.todayPendingBuyAmount), 0))
  const confirmedHoldingTotalAmount = round2(rows.reduce((s, f) => s + safeNum(f.estimatedConfirmedHoldingAmount), 0))
  const todayProfitBaseTotal = round2(rows.reduce((s, f) => s + safeNum(f.todayProfitBaseAmount), 0))
  const totalTodayProfit = round2(rows.reduce((s, f) => s + safeNum(f.todayProfit), 0))
  const totalProfit = round2(rows.reduce((s, f) => s + safeNum(f.holdingProfit), 0))
  const totalCost = round2(rows.reduce((s, f) => s + safeNum(f.costAmount), 0))
  return {
    accountTotalAmount,
    todayPendingBuyTotal,
    confirmedHoldingTotalAmount,
    todayProfitBaseTotal,
    totalTodayProfit,
    totalTodayRate: todayProfitBaseTotal > 0 ? round2(totalTodayProfit / todayProfitBaseTotal * 100) : 0,
    totalProfit,
    totalRate: totalCost > 0 ? round2(totalProfit / totalCost * 100) : 0,
    totalCost
  }
}
