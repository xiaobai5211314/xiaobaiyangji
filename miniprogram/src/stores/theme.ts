import { computed, reactive } from 'vue';

export type AppTheme = 'vivid' | 'light' | 'neon' | 'warm';

export const THEME_STORAGE_KEY = 'valuation_assistant_theme';

interface WxStorageLike {
  getStorageSync?: (key: string) => unknown;
  setStorageSync?: (key: string, value: unknown) => void;
}

declare const wx: WxStorageLike | undefined;

export const themeOptions: Array<{ value: AppTheme; label: string; description: string }> = [
  { value: 'vivid', label: '活力渐变', description: 'Ruby Pink粉色调、年轻活力' },
  { value: 'light', label: '浅色主题', description: '浅色卡片、高对比文字' },
  { value: 'neon', label: '霓虹渐变', description: '深色底赛博风、默认主题' },
  { value: 'warm', label: '暖色米黄', description: '柔和暖调、护眼舒适' }
];

export const themeState = reactive({
  theme: readStoredTheme()
});

export const themeClass = computed(() => themeState.theme === 'neon' ? '' : `theme-${themeState.theme}`);

export function normalizeTheme(value: unknown): AppTheme {
  const raw = String(value || '').toLowerCase();
  if (raw === 'neon' || raw === 'rainbow') return 'neon';
  if (raw === 'light') return 'light';
  if (raw === 'warm') return 'warm';
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
