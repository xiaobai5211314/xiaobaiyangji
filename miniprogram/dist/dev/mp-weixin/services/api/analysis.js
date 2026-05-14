"use strict";
const services_request = require("../request.js");
function getInsightsDashboard(username) {
  return services_request.get(`/api/fund/insights/dashboard?username=${encodeURIComponent(username)}`, {
    loadingText: "读取盈亏",
    fallbackData: {}
  });
}
function getArchives(username, limit = 120) {
  return services_request.get(`/api/fund/get-archives?username=${encodeURIComponent(username)}&limit=${limit}`, {
    loadingText: "读取档案",
    fallbackData: []
  });
}
exports.getArchives = getArchives;
exports.getInsightsDashboard = getInsightsDashboard;
