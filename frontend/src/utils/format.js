export const formatAmount = (value, decimals = 2) => {
  const n = Number(value)
  if (!Number.isFinite(n)) return '--'
  return n.toFixed(decimals)
}

export const formatPercent = (value) => {
  const n = Number(value)
  if (!Number.isFinite(n)) return '--'
  return n > 0 && n < 1 ? `${n.toFixed(3)}%` : `${n.toFixed(2)}%`
}

export const signed = (value, decimals = 2) => {
  const n = Number(value)
  if (!Number.isFinite(n)) return '--'
  return (n >= 0 ? '+' : '') + n.toFixed(decimals)
}

export const profitColor = (value) => {
  const n = Number(value)
  if (!Number.isFinite(n)) return 'var(--td-text-color-primary)'
  return n >= 0 ? '#ff4d4f' : '#10b981'
}
