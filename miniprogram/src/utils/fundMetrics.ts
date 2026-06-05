import type { FundTodayItem } from '../services/api/fund';

export type PrivacyMode = 0 | 1 | 2 | 3;

export interface ConfidenceView {
  score: number;
  level: string;
  reason: string;
  tone: 'high' | 'medium' | 'low';
}

export interface FundView extends FundTodayItem {
  viewKey: string;
  currentRateValue: number;
  todayProfitValue: number;
  todayAmountValue: number;
  confirmedAmountValue: number;
  pendingBuyAmountValue: number;
  pendingBuyValue: boolean;
  estimatedProfitValue: number;
  existingReturnRateValue: number;
  breakEvenRateValue: number;
  costValue: number | null;
  realizedProfitValue: number;
  statusLabel: string;
  trendLabel: string;
  isHolidayValue: boolean;
  isSettledValue: boolean;
  confidenceView: ConfidenceView;
}

export interface ExposureView {
  name: string;
  amount: number;
  dailyProfit: number;
  count: number;
  funds: string[];
  ratio: number;
}

export interface DailyBattleReportView {
  summary: string;
  todayProfitText: string;
  bestName: string;
  worstName: string;
  actionHint: string;
}

export interface PortfolioMetrics {
  funds: FundView[];
  totalPrincipal: number;
  totalTodayProfit: number;
  totalTodayRate: number;
  totalProfit: number;
  totalRate: number;
  totalAssets: number;
  totalCost: number;
  exposure: ExposureView[];
  dailyBattleReport: DailyBattleReportView;
  profitTop: FundView[];
  lossTop: FundView[];
}

const sectorRules = [
  { name: '半导体 / 芯片', words: ['半导体', '芯片', '集成电路', '电子'] },
  { name: 'AI / 人工智能', words: ['人工智能', 'AI', '智能', '机器人', '软件', '计算机'] },
  { name: '港股 / 恒生', words: ['恒生', '港股', '中概', '香港'] },
  { name: '新能源 / 电池', words: ['新能源', '光伏', '锂电', '电池', '碳中和'] },
  { name: '医药 / 医疗', words: ['医药', '医疗', '创新药', '生物'] },
  { name: '黄金 / 有色', words: ['黄金', '有色', '金属', '资源'] },
  { name: '银行 / 金融', words: ['银行', '证券', '金融', '保险'] },
  { name: '地产 / 基建', words: ['地产', '房地产', '基建', '工程'] },
  { name: '消费 / 白酒', words: ['消费', '白酒', '食品', '家电'] },
  { name: '债券 / 现金', words: ['债', '货币', '现金', '短融'] }
];

function finiteNumber(value: unknown): number | null {
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function numberOrZero(value: unknown): number {
  return finiteNumber(value) ?? 0;
}

function round(value: number, digits = 2) {
  const factor = 10 ** digits;
  return Math.round(value * factor) / factor;
}

function todayParts(now: Date) {
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return {
    slash: `${year}/${month}/${day}`,
    dash: `${year}-${month}-${day}`
  };
}

function pickLastRate(fund: FundTodayItem, slashDate: string, dashDate: string, now: Date) {
  const data = Array.isArray(fund.data) ? fund.data : [];
  const last = data[data.length - 1];
  if (!last) return { rate: 0, isHoliday: true };

  const lastTime = String(last[0] || '');
  const lastRate = numberOrZero(last[1]);
  const hasTodayDate = lastTime.includes(slashDate) || lastTime.includes(dashDate);
  const isPast935 = now.getHours() > 9 || (now.getHours() === 9 && now.getMinutes() >= 35);

  if (hasTodayDate && !(isPast935 && data.length <= 2)) {
    return { rate: lastRate, isHoliday: false };
  }

  return { rate: 0, isHoliday: true };
}

function deriveRateState(fund: FundTodayItem, now: Date, slashDate: string, dashDate: string) {
  const currentMinutes = now.getHours() * 60 + now.getMinutes();
  const isWeekend = now.getDay() === 0 || now.getDay() === 6;

  if (fund.isSettled) {
    return {
      rate: numberOrZero(fund.actualRate ?? fund.lastSettledRate ?? fund.currentRate),
      isHoliday: false,
      isSettled: true
    };
  }

  if (isWeekend || currentMinutes < 565) {
    return { rate: 0, isHoliday: true, isSettled: false };
  }

  const lastRate = pickLastRate(fund, slashDate, dashDate, now);
  return { rate: lastRate.rate, isHoliday: lastRate.isHoliday, isSettled: false };
}

function classifyFundSector(fund: FundTodayItem) {
  const text = `${fund.name || ''} ${fund.code || ''}`.toUpperCase();
  const hit = sectorRules.find((rule) => rule.words.some((word) => text.includes(word.toUpperCase())));
  return hit ? hit.name : '其他主题';
}

function buildConfidence(fund: FundView): ConfidenceView {
  let score = 62;
  const reasons: string[] = [];

  if (fund.isSettledValue) {
    score += 28;
    reasons.push('净值参考');
  } else if (fund.isHolidayValue) {
    score += 8;
    reasons.push('休市沿用');
  } else {
    score += 12;
    reasons.push('盘中估算');
  }

  const diff = Math.abs(numberOrZero(fund.diffRate));
  if (diff > 0.6) {
    score -= 25;
    reasons.push('偏离较大');
  } else if (diff > 0.15) {
    score -= 12;
    reasons.push('估值偏离');
  } else {
    score += 8;
    reasons.push('偏离可控');
  }

  if (numberOrZero(fund.shares) > 0) {
    score += 5;
    reasons.push('份额完整');
  }

  score = Math.max(20, Math.min(100, Math.round(score)));
  const tone = score >= 82 ? 'high' : score >= 65 ? 'medium' : 'low';
  const level = score >= 82 ? '高' : score >= 65 ? '中' : '低';

  return {
    score,
    level,
    tone,
    reason: reasons.slice(0, 2).join(' / ')
  };
}

function buildExposure(funds: FundView[], totalAssets: number) {
  const map = new Map<string, ExposureView>();

  for (const fund of funds) {
    const name = classifyFundSector(fund);
    const row = map.get(name) || { name, amount: 0, dailyProfit: 0, count: 0, funds: [], ratio: 0 };
    row.amount += fund.todayAmountValue;
    row.dailyProfit += fund.todayProfitValue;
    row.count += 1;
    row.funds.push(fund.name || fund.code || '未命名基金');
    map.set(name, row);
  }

  return Array.from(map.values())
    .map((row) => ({ ...row, ratio: totalAssets > 0 ? (row.amount / totalAssets) * 100 : 0 }))
    .sort((a, b) => b.amount - a.amount);
}

function buildDailyReport(funds: FundView[], totalTodayProfit: number, exposure: ExposureView[]): DailyBattleReportView {
  const rows = [...funds].map((fund) => ({ ...fund, p: fund.todayProfitValue, amountNum: fund.todayAmountValue }));
  const best = rows.slice().sort((a, b) => b.p - a.p)[0];
  const worst = rows.slice().sort((a, b) => a.p - b.p)[0];
  const topExposure = exposure[0];
  let actionHint = '保持观察';

  if (totalTodayProfit < 0 && worst && Math.abs(worst.p) > Math.abs(totalTodayProfit) * 0.5) {
    actionHint = `重点复盘 ${worst.name || worst.code || '拖累项'}`;
  } else if (topExposure && topExposure.ratio >= 45) {
    actionHint = `${topExposure.name} 集中度偏高`;
  } else if (totalTodayProfit > 0) {
    actionHint = '盈利日关注净值参考';
  }

  return {
    summary:
      funds.length > 0
        ? `${funds.length} 只持仓，主暴露 ${topExposure?.name || '待识别'} ${round(topExposure?.ratio || 0)}%。`
        : '暂无持仓数据。',
    todayProfitText: `${totalTodayProfit >= 0 ? '+' : ''}${round(totalTodayProfit).toFixed(2)}`,
    bestName: best?.name || '暂无',
    worstName: worst?.name || '暂无',
    actionHint
  };
}

export function buildPortfolioMetrics(rawFunds: FundTodayItem[], now = new Date()): PortfolioMetrics {
  const { slash, dash } = todayParts(now);
  let totalPrincipal = 0;
  let totalPrincipalForRate = 0;
  let totalCost = 0;
  let totalTodayProfit = 0;

  const funds = rawFunds.map((fund, index) => {
    const rateState = deriveRateState(fund, now, slash, dash);
    const pendingBuyAmount = Math.max(0, numberOrZero(fund.pendingBuyAmount));
    const pendingBuyValue = Boolean(fund.pendingBuy) || pendingBuyAmount > 0;
    const isAlreadySettled = fund.lastSettledDate === dash;
    const currentAmount = Math.max(0, finiteNumber(fund.confirmedAmount) ?? numberOrZero(fund.amount));
    const apiBaseAmount = finiteNumber(fund.todayBaseAmount);
    const todayBaseAmount = apiBaseAmount !== null ? Math.max(0, apiBaseAmount) : currentAmount;
    let todayProfitValue = 0;
    let todayAmountValue = todayBaseAmount;
    let currentRateValue = rateState.rate;

    if (isAlreadySettled) {
      todayProfitValue = numberOrZero(fund.lastSettledProfit);
      todayAmountValue = todayBaseAmount;
      currentRateValue = numberOrZero(fund.lastSettledRate) || currentRateValue;
    } else if (rateState.isSettled && fund.actualExactProfit !== null && fund.actualExactProfit !== undefined) {
      todayProfitValue = numberOrZero(fund.actualExactProfit);
      todayAmountValue = todayBaseAmount + todayProfitValue;
    } else {
      todayProfitValue = todayBaseAmount * (currentRateValue / 100);
      todayAmountValue = todayBaseAmount + todayProfitValue;
    }

    const realizedProfitValue = numberOrZero(fund.realizedProfit);
    const costValue = finiteNumber(fund.confirmedCost) ?? finiteNumber(fund.cost);
    const validCost = costValue !== null && costValue > 0 ? costValue : null;
    const floatProfit = validCost ? todayAmountValue - validCost : 0;
    const estimatedProfitValue = floatProfit + realizedProfitValue;
    const breakEvenRateValue =
      validCost && validCost > todayAmountValue && todayAmountValue > 0 ? ((validCost / todayAmountValue - 1) * 100) : 0;
    const existingReturnRateValue = validCost ? (estimatedProfitValue / validCost) * 100 : 0;
    const rateBaseForToday = todayBaseAmount;

    totalPrincipal += currentAmount;
    totalPrincipalForRate += rateBaseForToday;
    totalCost += validCost || currentAmount;
    totalTodayProfit += Number.isNaN(todayProfitValue) ? 0 : todayProfitValue;

    const view: FundView = {
      ...fund,
      viewKey: `${fund.code || fund.name || 'fund'}-${index}`,
      currentRate: round(currentRateValue),
      currentRateValue: round(currentRateValue),
      rawCurrentRate: finiteNumber(fund.rawCurrentRate) ?? round(currentRateValue),
      amount: round(currentAmount),
      confirmedAmount: round(currentAmount),
      pendingBuy: pendingBuyValue,
      pendingBuyAmount: round(pendingBuyAmount),
      confirmedAmountValue: round(currentAmount),
      pendingBuyAmountValue: round(pendingBuyAmount),
      pendingBuyValue,
      todayProfit: round(todayProfitValue),
      todayProfitValue: round(todayProfitValue),
      todayAmount: round(todayAmountValue),
      todayAmountValue: round(todayAmountValue),
      estimatedProfit: round(estimatedProfitValue),
      estimatedProfitValue: round(estimatedProfitValue),
      existingReturnRate: round(existingReturnRateValue),
      existingReturnRateValue: round(existingReturnRateValue),
      breakEvenRate: round(breakEvenRateValue),
      breakEvenRateValue: round(breakEvenRateValue),
      costValue: validCost,
      realizedProfit: round(realizedProfitValue),
      realizedProfitValue: round(realizedProfitValue),
      isHoliday: rateState.isHoliday,
      isHolidayValue: rateState.isHoliday,
      isSettledValue: rateState.isSettled || isAlreadySettled,
      statusLabel: rateState.isHoliday ? '休市' : rateState.isSettled || isAlreadySettled ? '净值参考' : '盘中参考',
      trendLabel: rateState.isHoliday ? '休市沿用' : rateState.isSettled || isAlreadySettled ? '真' : '估',
      confidenceView: { score: 0, level: '', reason: '', tone: 'medium' }
    };

    view.confidenceView = buildConfidence(view);
    return view;
  });

  const totalAssets = funds.reduce((sum, fund) => sum + fund.todayAmountValue, 0);
  const totalRealized = funds.reduce((sum, fund) => sum + fund.realizedProfitValue, 0);
  const totalProfit = totalAssets - totalCost + totalRealized;
  const totalRate = totalCost > 0 ? (totalProfit / totalCost) * 100 : 0;
  const totalTodayRate = totalPrincipalForRate > 0 ? (totalTodayProfit / totalPrincipalForRate) * 100 : 0;
  const exposure = buildExposure(funds, totalAssets);

  return {
    funds,
    totalPrincipal: round(totalPrincipal),
    totalTodayProfit: round(totalTodayProfit),
    totalTodayRate: round(totalTodayRate),
    totalProfit: round(totalProfit),
    totalRate: round(totalRate),
    totalAssets: round(totalAssets),
    totalCost: round(totalCost),
    exposure,
    dailyBattleReport: buildDailyReport(funds, round(totalTodayProfit), exposure),
    profitTop: dedupeFundsByCode(funds)
      .filter((fund) => fund.estimatedProfitValue > 0)
      .sort((a, b) => b.estimatedProfitValue - a.estimatedProfitValue)
      .slice(0, 5),
    lossTop: dedupeFundsByCode(funds)
      .filter((fund) => fund.estimatedProfitValue < 0)
      .sort((a, b) => a.estimatedProfitValue - b.estimatedProfitValue)
      .slice(0, 5)
  };
}

function dedupeFundsByCode(funds: FundView[]) {
  const bestByKey = new Map<string, FundView>();

  for (const fund of funds) {
    const key = String(fund.code || fund.name || fund.viewKey || '').trim() || fund.viewKey;
    const current = bestByKey.get(key);
    if (!current || Math.abs(fund.estimatedProfitValue) > Math.abs(current.estimatedProfitValue)) {
      bestByKey.set(key, fund);
    }
  }

  return Array.from(bestByKey.values());
}

export function maskByPrivacy(value: string, mode: PrivacyMode, requiredMode: PrivacyMode) {
  return mode <= requiredMode ? value : '****';
}

export function moneyDash(value: unknown, sign = false) {
  const n = finiteNumber(value);
  if (n === null) return '--';
  if (sign) return `${n >= 0 ? '+' : '-'}¥\u00a0${Math.abs(n).toFixed(2)}`;
  return `¥\u00a0${n.toFixed(2)}`;
}

export function percentDash(value: unknown, sign = true) {
  const n = finiteNumber(value);
  if (n === null) return '--';
  return `${sign && n > 0 ? '+' : ''}${n.toFixed(2)}%`;
}
