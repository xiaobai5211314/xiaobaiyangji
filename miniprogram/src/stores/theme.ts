import { computed, reactive } from 'vue';

export type AppTheme = 'neon' | 'light';

export const THEME_STORAGE_KEY = 'valuation_assistant_theme';

interface WxStorageLike {
  getStorageSync?: (key: string) => unknown;
  setStorageSync?: (key: string, value: unknown) => void;
}

declare const wx: WxStorageLike | undefined;

export const themeOptions: Array<{ value: AppTheme; label: string; description: string }> = [
  { value: 'neon', label: '曜石流光', description: '默认主题 · 深色行情工作台' },
  { value: 'light', label: '雾光银蓝', description: '浅色主题 · 清晰的系统信息层级' }
];

export const themeState = reactive({
  theme: readStoredTheme()
});

export const themeClass = computed(() => themeState.theme === 'neon' ? '' : `theme-${themeState.theme}`);

export function normalizeTheme(value: unknown): AppTheme {
  const raw = String(value || '').toLowerCase();
  if (raw === 'light' || raw === 'apple' || raw === 'frost' || raw === 'warm' || raw === 'vivid_gold') return 'light';
  return 'neon';
}

export function loadTheme() {
  themeState.theme = readStoredTheme();
  return themeState.theme;
}

export function setTheme(theme: AppTheme) {
  const next = normalizeTheme(theme);
  themeState.theme = next;
  writeStoredTheme(next);
  return next;
}

function readStoredTheme(): AppTheme {
  try {
    const value =
      typeof wx !== 'undefined' && wx.getStorageSync
        ? wx.getStorageSync(THEME_STORAGE_KEY)
        : uni.getStorageSync(THEME_STORAGE_KEY);
    return normalizeTheme(value);
  } catch {
    return 'neon';
  }
}

function writeStoredTheme(theme: AppTheme) {
  try {
    if (typeof wx !== 'undefined' && wx.setStorageSync) {
      wx.setStorageSync(THEME_STORAGE_KEY, theme);
      return;
    }

    uni.setStorageSync(THEME_STORAGE_KEY, theme);
  } catch {
    uni.setStorageSync(THEME_STORAGE_KEY, theme);
  }
}
