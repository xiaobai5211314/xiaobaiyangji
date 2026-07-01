"use strict";
var __defProp = Object.defineProperty;
var __defNormalProp = (obj, key, value) => key in obj ? __defProp(obj, key, { enumerable: true, configurable: true, writable: true, value }) : obj[key] = value;
var __publicField = (obj, key, value) => {
  __defNormalProp(obj, typeof key !== "symbol" ? key + "" : key, value);
  return value;
};
const common_vendor = require("../common/vendor.js");
const services_config = require("./config.js");
const stores_session = require("../stores/session.js");
var define_import_meta_env_default = {};
class ApiRequestError extends Error {
  constructor(message, statusCode, responseData) {
    super(message);
    __publicField(this, "statusCode");
    __publicField(this, "responseData");
    this.name = "ApiRequestError";
    this.statusCode = statusCode;
    this.responseData = responseData;
  }
}
class NetworkRequestError extends Error {
  constructor(message, errMsg) {
    super(message);
    __publicField(this, "errMsg");
    this.name = "NetworkRequestError";
    this.errMsg = errMsg;
  }
}
const DEBUG_REQUEST = (define_import_meta_env_default == null ? void 0 : define_import_meta_env_default.VITE_DEBUG_REQUEST) === "true";
const GET_CACHE_TTL = 6e4;
const getCache = /* @__PURE__ */ new Map();
const inFlightGets = /* @__PURE__ */ new Map();
const recentErrorToasts = /* @__PURE__ */ new Map();
const ERROR_TOAST_DEDUP_MS = 6e4;
const LOGIN_PAGE = "/pages/login/index";
let loginRedirectTimer = null;
function normalizePath(path) {
  return path.startsWith("/") ? path : `/${path}`;
}
function toQueryString(data) {
  return Object.entries(data).filter(([, value]) => value !== void 0 && value !== null).map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`).join("&");
}
function extractErrorMessage(data, fallback) {
  if (typeof data === "string" && data.trim())
    return data;
  if (data && typeof data === "object") {
    const body = data;
    return body.message || body.msg || body.title || body.error || fallback;
  }
  return fallback;
}
function extractErrMsg(error) {
  if (error instanceof Error && error.message)
    return error.message;
  if (error && typeof error === "object" && "errMsg" in error) {
    return String(error.errMsg || "");
  }
  return String(error || "");
}
function isTimeoutError(error) {
  const message = extractErrMsg(error);
  return /timeout|time\s*out|超时/i.test(message);
}
function maskSensitiveString(value) {
  return value.replace(/(password=)[^&]*/gi, "$1***");
}
function maskSensitiveData(data) {
  if (typeof data === "string")
    return maskSensitiveString(data);
  if (Array.isArray(data))
    return data.map((item) => maskSensitiveData(item));
  if (data && typeof data === "object") {
    return Object.fromEntries(
      Object.entries(data).map(([key, value]) => {
        if (/password|token|secret/i.test(key))
          return [key, "***"];
        return [key, maskSensitiveData(value)];
      })
    );
  }
  return data;
}
function resolveFallback(options) {
  if (!Object.prototype.hasOwnProperty.call(options, "fallbackData")) {
    return { hasFallback: false, value: void 0 };
  }
  const fallback = options.fallbackData;
  const value = typeof fallback === "function" ? fallback() : fallback;
  return { hasFallback: true, value };
}
function logRequestFailure(params) {
  const errMsg = extractErrMsg(params.error);
  const label = isTimeoutError(params.error) ? "[request timeout]" : "[request fail]";
  console.warn(label, {
    method: params.method,
    path: params.path,
    fullUrl: params.fullUrl,
    timeout: params.timeout,
    errMsg
  });
}
function shouldShowErrorToast(message) {
  const now = Date.now();
  const lastShown = recentErrorToasts.get(message);
  if (lastShown && now - lastShown < ERROR_TOAST_DEDUP_MS)
    return false;
  recentErrorToasts.set(message, now);
  for (const [key, time] of recentErrorToasts) {
    if (now - time > ERROR_TOAST_DEDUP_MS * 2)
      recentErrorToasts.delete(key);
  }
  return true;
}
function showDedupedToast(message, duration = 2200) {
  if (shouldShowErrorToast(message)) {
    common_vendor.index.showToast({ title: message, icon: "none", duration });
  }
}
function isAuthEntryRequest(path) {
  return /^\/api\/auth\/(login|register|wechat-login)\b/i.test(path);
}
function isLoginPageActive() {
  try {
    const pages = getCurrentPages();
    const current = pages[pages.length - 1];
    return (current == null ? void 0 : current.route) === "pages/login/index";
  } catch {
    return false;
  }
}
function redirectToLoginOnce() {
  if (loginRedirectTimer || isLoginPageActive())
    return;
  loginRedirectTimer = setTimeout(() => {
    loginRedirectTimer = null;
    common_vendor.index.reLaunch({
      url: LOGIN_PAGE,
      fail: () => common_vendor.index.redirectTo({ url: LOGIN_PAGE })
    });
  }, 500);
}
function handleUnauthorized(message, showErrorToast, normalizedPath) {
  const loginMessage = message && !/^请求失败/.test(message) ? message : "未登录或登录已过期";
  stores_session.clearSession();
  getCache.clear();
  inFlightGets.clear();
  if (showErrorToast) {
    showDedupedToast(loginMessage, 2600);
  }
  if (!isAuthEntryRequest(normalizedPath)) {
    redirectToLoginOnce();
  }
  return loginMessage;
}
function getLocalStorageCache(key) {
  try {
    const raw = common_vendor.index.getStorageSync(key);
    if (!raw)
      return null;
    const parsed = typeof raw === "string" ? JSON.parse(raw) : raw;
    if (parsed && typeof parsed === "object" && "expiresAt" in parsed && parsed.expiresAt > Date.now()) {
      return parsed.data;
    }
    return null;
  } catch {
    return null;
  }
}
function setLocalStorageCache(key, data, ttlMs) {
  try {
    common_vendor.index.setStorageSync(key, JSON.stringify({ expiresAt: Date.now() + ttlMs, data }));
  } catch {
  }
}
async function request(path, options = {}) {
  const method = options.method ?? "GET";
  const loadingText = options.loadingText ?? "加载中";
  const timeout = options.timeout ?? 2e4;
  const showErrorToast = options.showErrorToast ?? true;
  const normalizedPath = normalizePath(path);
  const fullUrl = `${services_config.getApiBaseUrl()}${normalizedPath}`;
  const cacheKey = `${method}:${fullUrl}`;
  const canUseCache = method === "GET" && options.silent !== false && !/[?&]force=true\b/i.test(fullUrl) && !/[?&]_t=/.test(fullUrl) && !/\/api\/fund\//i.test(fullUrl) && !/\/api\/stock\//i.test(fullUrl);
  const token = stores_session.getToken();
  const header = {
    Accept: "application/json",
    ...options.header
  };
  if (token)
    header["Authorization"] = `Bearer ${token}`;
  if (canUseCache) {
    const cached = getCache.get(cacheKey);
    if (cached && cached.expiresAt > Date.now()) {
      return cached.data;
    }
    const pending = inFlightGets.get(cacheKey);
    if (pending) {
      return pending;
    }
  }
  if (!options.silent) {
    common_vendor.index.showLoading({ title: loadingText, mask: true });
  }
  const executor = (async () => {
    let usedNetworkFallback = false;
    const result = await new Promise((resolve, reject) => {
      common_vendor.index.request({
        url: fullUrl,
        method,
        data: options.data,
        header,
        timeout,
        success: resolve,
        fail: (error) => {
          logRequestFailure({ method, path: normalizedPath, fullUrl, timeout, error });
          const fallback = resolveFallback(options);
          if (fallback.hasFallback) {
            usedNetworkFallback = true;
            resolve({ statusCode: 200, data: fallback.value });
            return;
          }
          reject(error);
        }
      });
    });
    const statusCode = Number(result.statusCode || 0);
    if (DEBUG_REQUEST) {
      console.warn("[request:debug]", { method, url: fullUrl, statusCode });
    }
    if (statusCode < 200 || statusCode >= 300) {
      const message = extractErrorMessage(result.data, `请求失败：${statusCode}`);
      console.warn("[request:error]", { method, fullUrl, statusCode, message, data: result.data });
      if (statusCode === 401 && !isAuthEntryRequest(normalizedPath)) {
        const loginMessage = handleUnauthorized(message, showErrorToast, normalizedPath);
        throw new ApiRequestError(loginMessage, statusCode, result.data);
      }
      const fallback = resolveFallback(options);
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
    return result.data;
  })();
  const handled = executor.catch((error) => {
    if (error instanceof ApiRequestError) {
      throw error;
    }
    const timeoutError = isTimeoutError(error);
    const message = timeoutError ? "请求超时，请检查网络或后端接口" : error instanceof Error ? error.message : "网络请求失败";
    console.warn("[request:fail:throw]", {
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
    const fallback = resolveFallback(options);
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
      common_vendor.index.hideLoading();
    }
  }
}
function get(path, options = {}) {
  return request(path, { ...options, method: "GET" });
}
function postJson(path, data, options = {}) {
  return request(path, {
    ...options,
    method: "POST",
    data,
    header: {
      "content-type": "application/json"
    }
  });
}
function postForm(path, data, options = {}) {
  return request(path, {
    ...options,
    method: "POST",
    data: toQueryString(data),
    header: {
      "content-type": "application/x-www-form-urlencoded"
    }
  });
}
function clearGetCache(pathPattern) {
  for (const key of getCache.keys()) {
    if (key.includes(pathPattern))
      getCache.delete(key);
  }
}
exports.clearGetCache = clearGetCache;
exports.get = get;
exports.getLocalStorageCache = getLocalStorageCache;
exports.postForm = postForm;
exports.postJson = postJson;
exports.request = request;
exports.setLocalStorageCache = setLocalStorageCache;
