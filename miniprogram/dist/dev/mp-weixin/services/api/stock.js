"use strict";
const common_vendor = require("../../common/vendor.js");
const services_config = require("../config.js");
const services_request = require("../request.js");
function getStockDashboard(username) {
  return services_request.get(`/api/stock/dashboard?username=${encodeURIComponent(username)}`, {
    loadingText: "读取股票"
  });
}
function searchStocks(keyword) {
  return services_request.get(`/api/stock/search?keyword=${encodeURIComponent(keyword)}`, {
    loadingText: "查询股票"
  });
}
function saveStockWatch(payload) {
  return services_request.postJson("/api/stock/watch", payload, {
    loadingText: "保存自选"
  });
}
function deleteStockWatch(username, code, market) {
  const query = buildIdentityQuery(username, code, market);
  return services_request.request(`/api/stock/watch?${query}`, {
    method: "DELETE",
    loadingText: "移除自选"
  });
}
function saveStockHolding(payload) {
  return services_request.postJson("/api/stock/holding", payload, {
    loadingText: "保存持仓"
  });
}
function deleteStockHolding(username, code, market) {
  const query = buildIdentityQuery(username, code, market);
  return services_request.request(`/api/stock/holding?${query}`, {
    method: "DELETE",
    loadingText: "删除持仓"
  });
}
function getStockKlines(code, period) {
  const query = `code=${encodeURIComponent(code)}&period=${encodeURIComponent(period)}`;
  return services_request.get(`/api/stock/klines?${query}`, {
    loadingText: "读取走势"
  });
}
function previewStockOcr(username, filePath) {
  const url = `${services_config.getApiBaseUrl()}/api/stock/import-ocr-preview`;
  return new Promise((resolve, reject) => {
    common_vendor.index.uploadFile({
      url,
      filePath,
      name: "image",
      formData: {
        username
      },
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(extractUploadMessage(result.data, `股票 OCR 预览失败：${statusCode}`)));
          return;
        }
        resolve(parseUploadData(result.data));
      },
      fail: reject
    });
  });
}
function confirmStockOcr(payload) {
  return services_request.postJson("/api/stock/import-ocr-confirm", payload, {
    loadingText: "确认导入"
  });
}
function buildIdentityQuery(username, code, market) {
  const query = [
    `username=${encodeURIComponent(username)}`,
    `code=${encodeURIComponent(code)}`
  ];
  if (market)
    query.push(`market=${encodeURIComponent(market)}`);
  return query.join("&");
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
exports.confirmStockOcr = confirmStockOcr;
exports.deleteStockHolding = deleteStockHolding;
exports.deleteStockWatch = deleteStockWatch;
exports.getStockDashboard = getStockDashboard;
exports.getStockKlines = getStockKlines;
exports.previewStockOcr = previewStockOcr;
exports.saveStockHolding = saveStockHolding;
exports.saveStockWatch = saveStockWatch;
exports.searchStocks = searchStocks;
