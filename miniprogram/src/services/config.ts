export const API_BASE_URL = 'https://guzhi.21212121.xyz';
export const CDN_BASE_URL = 'https://guzhicdn.21212121.xyz';

// 待核实：微信小程序 request/uploadFile/downloadFile 合法域名需在微信公众平台配置。
// 小程序端 /api/* 请求只允许使用 API_BASE_URL，不使用 CDN_BASE_URL。

export function getApiBaseUrl() {
  return API_BASE_URL.replace(/\/$/, '');
}
