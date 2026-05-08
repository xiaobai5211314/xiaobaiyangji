"use strict";
function toNumber(value, fallback = 0) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}
function formatMoney(value) {
  const n = toNumber(value);
  return `¥ ${n.toFixed(2)}`;
}
function signedMoney(value) {
  const n = toNumber(value);
  return `${n >= 0 ? "+" : "-"}¥ ${Math.abs(n).toFixed(2)}`;
}
function signedPercent(value) {
  const n = toNumber(value);
  return `${n >= 0 ? "+" : ""}${n.toFixed(2)}%`;
}
function profitClass(value) {
  return toNumber(value) >= 0 ? "profit-text" : "loss-text";
}
function optionalProfitClass(value) {
  if (value === null || value === void 0 || value === "")
    return "";
  return profitClass(value);
}
function avatarInitial(username) {
  const text = (username || "").trim();
  return text ? text.slice(0, 1).toUpperCase() : "估";
}
exports.avatarInitial = avatarInitial;
exports.formatMoney = formatMoney;
exports.optionalProfitClass = optionalProfitClass;
exports.profitClass = profitClass;
exports.signedMoney = signedMoney;
exports.signedPercent = signedPercent;
