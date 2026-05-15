import { computed, reactive } from 'vue';

export type AppTheme = 'light' | 'neon';

export const THEME_STORAGE_KEY = 'valuation_assistant_theme';

interface WxStorageLike {
  getStorageSync?: (key: string) => unknown;
  setStorageSync?: (key: string, value: unknown) => void;
}

declare const wx: WxStorageLike | undefined;

export const themeOptions: Array<{ value: AppTheme; label: string; description: string }> = [
  { value: 'light', label: '浅色主题', description: '浅色卡片、高对比文字' },
  { value: 'neon', label: '彩虹渐变主题', description: '深色底、粉紫蓝高亮' }
];

export const themeState = reactive({
  theme: readStoredTheme()
});

export const themeClass = computed(() => `theme-${themeState.theme}`);

export function normalizeTheme(value: unknown): AppTheme {
  const raw = String(value || '').toLowerCase();
  if (raw === 'neon' || raw === 'rainbow' || raw === 'gradient') return 'neon';
  return 'light';
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
    return 'light';
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
