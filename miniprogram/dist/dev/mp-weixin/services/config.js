"use strict";
const API_BASE_URL = "https://guzhi.21212121.xyz";
function getApiBaseUrl() {
  return API_BASE_URL.replace(/\/$/, "");
}
exports.getApiBaseUrl = getApiBaseUrl;
