"use strict";
const common_vendor = require("../common/vendor.js");
const THEME_STORAGE_KEY = "valuation_assistant_theme";
const themeOptions = [
  { value: "light", label: "浅色主题", description: "浅色卡片、高对比文字" },
  { value: "neon", label: "彩虹渐变主题", description: "深色底、粉紫蓝高亮" }
];
const themeState = common_vendor.reactive({
  theme: readStoredTheme()
});
const themeClass = common_vendor.computed(() => `theme-${themeState.theme}`);
function normalizeTheme(value) {
  const raw = String(value || "").toLowerCase();
  if (raw === "neon" || raw === "rainbow" || raw === "gradient")
    return "neon";
  return "light";
}
function loadTheme() {
  themeState.theme = readStoredTheme();
  return themeState.theme;
}
function setTheme(theme) {
  const next = normalizeTheme(theme);
  themeState.theme = next;
  writeStoredTheme(next);
  return next;
}
function readStoredTheme() {
  try {
    const value = typeof common_vendor.wx$1 !== "undefined" && common_vendor.wx$1.getStorageSync ? common_vendor.wx$1.getStorageSync(THEME_STORAGE_KEY) : common_vendor.index.getStorageSync(THEME_STORAGE_KEY);
    return normalizeTheme(value);
  } catch {
    return "light";
  }
}
function writeStoredTheme(theme) {
  try {
    if (typeof common_vendor.wx$1 !== "undefined" && common_vendor.wx$1.setStorageSync) {
      common_vendor.wx$1.setStorageSync(THEME_STORAGE_KEY, theme);
      return;
    }
    common_vendor.index.setStorageSync(THEME_STORAGE_KEY, theme);
  } catch {
    common_vendor.index.setStorageSync(THEME_STORAGE_KEY, theme);
  }
}
exports.loadTheme = loadTheme;
exports.setTheme = setTheme;
exports.themeClass = themeClass;
exports.themeOptions = themeOptions;
exports.themeState = themeState;
