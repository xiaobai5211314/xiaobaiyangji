export function getStorage<T>(key: string, fallback: T): T {
  try {
    const value = uni.getStorageSync(key);
    return value === '' || value === undefined || value === null ? fallback : (value as T);
  } catch {
    return fallback;
  }
}

export function setStorage<T>(key: string, value: T) {
  uni.setStorageSync(key, value);
}

export function removeStorage(key: string) {
  uni.removeStorageSync(key);
}
