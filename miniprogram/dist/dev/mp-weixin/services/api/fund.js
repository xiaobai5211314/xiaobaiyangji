"use strict";
const common_vendor = require("../../common/vendor.js");
const services_config = require("../config.js");
const services_request = require("../request.js");
function getTodayFunds(username, force = false) {
  const query = `username=${encodeURIComponent(username)}${force ? "&force=true" : ""}`;
  return services_request.get(`/api/fund/today?${query}`, {
    loadingText: "读取持仓",
    fallbackData: []
  });
}
function getFundArchives(username, fundCode, limit = 365) {
  const query = `username=${encodeURIComponent(username)}&fundCode=${encodeURIComponent(fundCode)}&limit=${limit}`;
  return services_request.get(`/api/fund/get-archives?${query}`, {
    loadingText: "读取历史",
    fallbackData: []
  });
}
function parseUploadData(data) {
  if (typeof data !== "string")
    return data || {};
  const text = data.trim();
  if (!text)
    return {};
  try {
    return JSON.parse(text);
  } catch {
    return { success: false, message: text };
  }
}
function extractUploadMessage(data, fallback) {
  const parsed = parseUploadData(data);
  const body = parsed;
  const value = parsed.message || body.msg || body.title || body.error;
  return value ? String(value) : fallback;
}
function previewFundOcr(username, filePath) {
  const url = `${services_config.getApiBaseUrl()}/api/Fund/import-ocr-preview?username=${encodeURIComponent(username)}`;
  return new Promise((resolve, reject) => {
    common_vendor.index.uploadFile({
      url,
      filePath,
      name: "imageFile",
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(extractUploadMessage(result.data, `OCR 预览失败：${statusCode}`)));
          return;
        }
        resolve(parseUploadData(result.data));
      },
      fail: (error) => {
        reject(error);
      }
    });
  });
}
function confirmFundOcr(payload) {
  return services_request.postJson("/api/Fund/import-ocr-confirm", payload, {
    loadingText: "确认导入"
  });
}
exports.confirmFundOcr = confirmFundOcr;
exports.getFundArchives = getFundArchives;
exports.getTodayFunds = getTodayFunds;
exports.previewFundOcr = previewFundOcr;
