import { getApiBaseUrl } from './config';

export type RequestMethod = 'GET' | 'POST' | 'PUT' | 'DELETE';

export interface RequestOptions<TData = unknown> {
  method?: RequestMethod;
  data?: TData;
  header?: Record<string, string>;
  loadingText?: string;
  silent?: boolean;
  timeout?: number;
  showErrorToast?: boolean;
  fallbackData?: unknown | (() => unknown);
}

export interface ApiErrorBody {
  message?: string;
  msg?: string;
  title?: string;
  error?: string;
  [key: string]: unknown;
}

class ApiRequestError extends Error {
  statusCode: number;
  responseData: unknown;

  constructor(message: string, statusCode: number, responseData: unknown) {
    super(message);
    this.name = 'ApiRequestError';
    this.statusCode = statusCode;
    this.responseData = responseData;
  }
}

class NetworkRequestError extends Error {
  errMsg: string;

  constructor(message: string, errMsg: string) {
    super(message);
    this.name = 'NetworkRequestError';
    this.errMsg = errMsg;
  }
}

const DEBUG_REQUEST =
  (import.meta as ImportMeta & { env?: { VITE_DEBUG_REQUEST?: string } }).env?.VITE_DEBUG_REQUEST === 'true';
const GET_CACHE_TTL = 60000;
const getCache = new Map<string, { expiresAt: number; data: unknown }>();
const inFlightGets = new Map<string, Promise<unknown>>();
const recentErrorToasts = new Map<string, number>();
const ERROR_TOAST_DEDUP_MS = 60000;

function normalizePath(path: string) {
  return path.startsWith('/') ? path : `/${path}`;
}

function toQueryString(data: Record<string, unknown>) {
  return Object.entries(data)
    .filter(([, value]) => value !== undefined && value !== null)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
    .join('&');
}

function extractErrorMessage(data: unknown, fallback: string) {
  if (typeof data === 'string' && data.trim()) return data;
  if (data && typeof data === 'object') {
    const body = data as ApiErrorBody;
    return body.message || body.msg || body.title || body.error || fallback;
  }

  return fallback;
}

function extractErrMsg(error: unknown) {
  if (error instanceof Error && error.message) return error.message;
  if (error && typeof error === 'object' && 'errMsg' in error) {
    return String((error as { errMsg?: unknown }).errMsg || '');
  }

  return String(error || '');
}

function isTimeoutError(error: unknown) {
  const message = extractErrMsg(error);
  return /timeout|time\s*out|超时/i.test(message);
}

function maskSensitiveString(value: string) {
  return value.replace(/(password=)[^&]*/gi, '$1***');
}

function maskSensitiveData(data: unknown): unknown {
  if (typeof data === 'string') return maskSensitiveString(data);
  if (Array.isArray(data)) return data.map((item) => maskSensitiveData(item));
  if (data && typeof data === 'object') {
    return Object.fromEntries(
      Object.entries(data as Record<string, unknown>).map(([key, value]) => {
        if (/password|token|secret/i.test(key)) return [key, '***'];
        return [key, maskSensitiveData(value)];
      })
    );
  }

  return data;
}

function resolveFallback<TResponse>(options: RequestOptions) {
  if (!Object.prototype.hasOwnProperty.call(options, 'fallbackData')) {
    return { hasFallback: false, value: undefined as TResponse };
  }

  const fallback = options.fallbackData;
  const value = typeof fallback === 'function' ? (fallback as () => unknown)() : fallback;
  return { hasFallback: true, value: value as TResponse };
}

function logRequestFailure(params: {
  method: RequestMethod;
  path: string;
  fullUrl: string;
  timeout: number;
  error: unknown;
}) {
  const errMsg = extractErrMsg(params.error);
  const label = isTimeoutError(params.error) ? '[request timeout]' : '[request fail]';
  console.warn(label, {
    method: params.method,
    path: params.path,
    fullUrl: params.fullUrl,
    timeout: params.timeout,
    errMsg
  });
}

function shouldShowErrorToast(message: string): boolean {
  const now = Date.now();
  const lastShown = recentErrorToasts.get(message);
  if (lastShown && now - lastShown < ERROR_TOAST_DEDUP_MS) return false;
  recentErrorToasts.set(message, now);
  for (const [key, time] of recentErrorToasts) {
    if (now - time > ERROR_TOAST_DEDUP_MS * 2) recentErrorToasts.delete(key);
  }
  return true;
}

function showDedupedToast(message: string, duration = 2200) {
  if (shouldShowErrorToast(message)) {
    uni.showToast({ title: message, icon: 'none', duration });
  }
}

export function getLocalStorageCache<T>(key: string): T | null {
  try {
    const raw = uni.getStorageSync(key);
    if (!raw) return null;
    const parsed = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (parsed && typeof parsed === 'object' && 'expiresAt' in parsed && parsed.expiresAt > Date.now()) {
      return parsed.data as T;
    }
    return null;
  } catch {
    return null;
  }
}

export function setLocalStorageCache<T>(key: string, data: T, ttlMs: number) {
  try {
    uni.setStorageSync(key, JSON.stringify({ expiresAt: Date.now() + ttlMs, data }));
  } catch { /* ignore */ }
}

export async function request<TResponse = unknown, TData = unknown>(
  path: string,
  options: RequestOptions<TData> = {}
): Promise<TResponse> {
  const method = options.method ?? 'GET';
  const loadingText = options.loadingText ?? '加载中';
  const timeout = options.timeout ?? 20000;
  const showErrorToast = options.showErrorToast ?? true;
  const normalizedPath = normalizePath(path);
  const fullUrl = `${getApiBaseUrl()}${normalizedPath}`;
  const cacheKey = `${method}:${fullUrl}`;
  const canUseCache =
    method === 'GET' &&
    options.silent !== false &&
    !/[?&]force=true\b/i.test(fullUrl) &&
    !/[?&]_t=/.test(fullUrl);
  const header = {
    Accept: 'application/json',
    ...options.header
  };

  if (canUseCache) {
    const cached = getCache.get(cacheKey);
    if (cached && cached.expiresAt > Date.now()) {
      return cached.data as TResponse;
    }

    const pending = inFlightGets.get(cacheKey);
    if (pending) {
      return pending as Promise<TResponse>;
    }
  }

  if (!options.silent) {
    uni.showLoading({ title: loadingText, mask: true });
  }

  const executor = (async () => {
    let usedNetworkFallback = false;
    const result = await new Promise<UniApp.RequestSuccessCallbackResult>((resolve, reject) => {
      uni.request({
        url: fullUrl,
        method,
        data: options.data as UniApp.RequestOptions['data'],
        header,
        timeout,
        success: resolve,
        fail: (error) => {
          logRequestFailure({ method, path: normalizedPath, fullUrl, timeout, error });
          const fallback = resolveFallback<TResponse>(options);
          if (fallback.hasFallback) {
            usedNetworkFallback = true;
            resolve({ statusCode: 200, data: fallback.value } as unknown as UniApp.RequestSuccessCallbackResult);
            return;
          }
          reject(error);
        }
      });
    });

    const statusCode = Number(result.statusCode || 0);
    if (DEBUG_REQUEST) {
      console.warn('[request:debug]', { method, url: fullUrl, statusCode });
    }

    if (statusCode < 200 || statusCode >= 300) {
      const message = extractErrorMessage(result.data, `请求失败：${statusCode}`);
      console.warn('[request:error]', { method, fullUrl, statusCode, message, data: result.data });
      const fallback = resolveFallback<TResponse>(options);
      if (fallback.hasFallback) {
        if (showErrorToast) {
          showDedupedToast(message, 2200);
        }
        return fallback.value;
      }
      throw new ApiRequestError(message, statusCode, result.data);
    }

    if (canUseCache && !usedNetworkFallback) {
      getCache.set(cacheKey, { expiresAt: Date.now() + GET_CACHE_TTL, data: result.data });
    }

    return result.data as TResponse;
  })();

  const handled = executor.catch((error) => {
    if (error instanceof ApiRequestError) {
      throw error;
    }

    const timeoutError = isTimeoutError(error);
    const message = timeoutError
      ? '请求超时，请检查网络或后端接口'
      : error instanceof Error
        ? error.message
        : '网络请求失败';

    console.warn('[request:fail:throw]', {
      method,
      path: normalizedPath,
      fullUrl,
      timeout,
      errMsg: extractErrMsg(error),
      message,
      data: maskSensitiveData(options.data)
    });
    if (showErrorToast) {
      showDedupedToast(message, 2600);
    }

    const fallback = resolveFallback<TResponse>(options);
    if (fallback.hasFallback) {
      return fallback.value;
    }

    throw new NetworkRequestError(message, extractErrMsg(error));
  });

  if (canUseCache) {
    inFlightGets.set(cacheKey, handled);
  }

  try {
    return await handled;
  } finally {
    if (canUseCache && inFlightGets.get(cacheKey) === handled) {
      inFlightGets.delete(cacheKey);
    }
    if (!options.silent) {
      uni.hideLoading();
    }
  }
}

export function get<TResponse = unknown>(path: string, options: Omit<RequestOptions, 'method'> = {}) {
  return request<TResponse>(path, { ...options, method: 'GET' });
}

export function postJson<TResponse = unknown, TData = unknown>(
  path: string,
  data: TData,
  options: Omit<RequestOptions<TData>, 'method' | 'data' | 'header'> = {}
) {
  return request<TResponse, TData>(path, {
    ...options,
    method: 'POST',
    data,
    header: {
      'content-type': 'application/json'
    }
  });
}

export function postForm<TResponse = unknown>(
  path: string,
  data: Record<string, unknown>,
  options: Omit<RequestOptions<string>, 'method' | 'data' | 'header'> = {}
) {
  return request<TResponse, string>(path, {
    ...options,
    method: 'POST',
    data: toQueryString(data),
    header: {
      'content-type': 'application/x-www-form-urlencoded'
    }
  });
}

export function clearGetCache(pathPattern?: string) {
  if (!pathPattern) {
    getCache.clear();
    return;
  }
  for (const key of getCache.keys()) {
    if (key.includes(pathPattern)) getCache.delete(key);
  }
}
