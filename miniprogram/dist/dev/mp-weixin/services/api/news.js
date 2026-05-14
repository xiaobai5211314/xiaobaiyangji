"use strict";
const services_request = require("../request.js");
function getGlobalNews(force = false, important = false, limit = 60) {
  const query = `mode=global&important=${important}&limit=${limit}${force ? "&force=true" : ""}`;
  return services_request.get(`/api/fund/news?${query}`, {
    loadingText: "读取资讯",
    fallbackData: { items: [] }
  });
}
function getHoldingNews(username, force = false, important = false, limit = 40) {
  const query = `username=${encodeURIComponent(username)}&important=${important}&limit=${limit}${force ? "&force=true" : ""}`;
  return services_request.get(`/api/fund/holding-news?${query}`, {
    loadingText: "读取持仓资讯",
    fallbackData: { items: [] }
  });
}
exports.getGlobalNews = getGlobalNews;
exports.getHoldingNews = getHoldingNews;
