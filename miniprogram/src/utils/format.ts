export function toNumber(value: unknown, fallback = 0) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

export function firstNumber(values: unknown[], fallback: number | null = null) {
  for (const value of values) {
    const n = Number(value);
    if (Number.isFinite(n)) return n;
  }

  return fallback;
}

export function formatMoney(value: unknown) {
  const n = toNumber(value);
  return `¥\u00a0${n.toFixed(2)}`;
}

export function signedMoney(value: unknown) {
  const n = toNumber(value);
  return `${n >= 0 ? '+' : '-'}¥\u00a0${Math.abs(n).toFixed(2)}`;
}

export function signedPercent(value: unknown) {
  const n = toNumber(value);
  return `${n >= 0 ? '+' : ''}${n.toFixed(2)}%`;
}

export function profitClass(value: unknown) {
  return toNumber(value) >= 0 ? 'profit-text' : 'loss-text';
}

export function optionalProfitClass(value: unknown) {
  if (value === null || value === undefined || value === '') return '';
  return profitClass(value);
}

export function displayText(value: unknown, fallback = '暂无') {
  if (value === null || value === undefined) return fallback;
  const text = String(value).trim();
  return text || fallback;
}

export function avatarInitial(username: string) {
  const text = (username || '').trim();
  return text ? text.slice(0, 1).toUpperCase() : '估';
}
