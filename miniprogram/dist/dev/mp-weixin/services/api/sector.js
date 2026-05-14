"use strict";
const services_request = require("../request.js");
var define_import_meta_env_default = {};
const CAPITAL_FLOW_EXCLUDED_NAME_KEYWORDS = [
  "概念",
  "新高",
  "近期新高",
  "百日新高",
  "历史新高",
  "昨日涨停",
  "昨日连板",
  "连板",
  "融资",
  "融券",
  "沪股通",
  "深股通",
  "陆股通",
  "北向资金",
  "南向资金",
  "预盈",
  "预增",
  "预亏",
  "预减",
  "次新",
  "高送转",
  "重仓",
  "QFII",
  "OFII",
  "社保",
  "养老金",
  "MSCI",
  "富时罗素",
  "中特估",
  "ST",
  "小米",
  "华为",
  "苹果",
  "周期股",
  "最近多板",
  "反内卷",
  "深圳特区",
  "上海自贸",
  "海南自贸",
  "西部大开发",
  "雄安新区",
  "粤港澳",
  "长三角",
  "京津冀",
  "一带一路",
  "乡村振兴",
  "3D打印",
  "AH股",
  "氢能源",
  "特高压",
  "数字芯片设计",
  "机器人",
  "人形机器人",
  "飞行汽车",
  "低空经济",
  "数据要素",
  "算力",
  "AIGC",
  "ChatGPT",
  "虚拟现实",
  "元宇宙",
  "东数西算",
  "国企改革",
  "央企改革",
  "参股",
  "壳资源",
  "股权转让",
  "摘帽",
  "低价股",
  "高股息",
  "破净"
];
const DEBUG_MARKET_INDEX = (define_import_meta_env_default == null ? void 0 : define_import_meta_env_default.VITE_DEBUG_MARKET_INDEX) === "true";
function getSectors(force = false) {
  return services_request.get(`/api/fund/sectors${force ? "?force=true" : ""}`, {
    loadingText: "读取板块",
    fallbackData: { top: [], bottom: [], all: [] }
  });
}
function getCapitalFlow(force = false, limit = 30) {
  const query = `limit=${limit}${force ? "&force=true" : ""}`;
  return services_request.get(`/api/fund/capital-flow?${query}`, {
    loadingText: "读取资金",
    fallbackData: { rows: [], inflow: [], outflow: [] }
  }).then(normalizeCapitalFlowResponse);
}
function getGlobalIndices(force = false) {
  return services_request.get(`/api/fund/global-indices${force ? "?force=true" : ""}`, {
    loadingText: "读取大盘",
    fallbackData: []
  }).then((payload) => {
    if (DEBUG_MARKET_INDEX)
      console.warn("[global-indices raw response]", payload);
    const rows = extractGlobalIndexRows(payload).map(normalizeGlobalIndexRow);
    if (DEBUG_MARKET_INDEX) {
      rows.forEach((item) => {
        console.warn("[global-indices normalized]", {
          name: item.name,
          point: item.point ?? item.latest ?? null,
          todayRate: item.todayRate ?? null,
          yearRate: item.yearRate ?? null
        });
      });
    }
    return rows;
  });
}
function normalizeIndexName(value) {
  return String(value || "").replace(/\s*\((?:无数据|异常:.*)\)\s*$/g, "").trim();
}
function extractGlobalIndexRows(payload) {
  if (Array.isArray(payload))
    return payload;
  if (!payload || typeof payload !== "object")
    return [];
  const source = payload;
  const rows = firstArray(source.rows, source.items, source.list, source.data, source.indices);
  return rows;
}
function normalizeGlobalIndexRow(row) {
  const source = row;
  const name = normalizeIndexName(source.name || source.indexName || source.title || source.shortName);
  const code = source.code || source.indexCode || source.symbol || source.secid;
  const market = normalizeMarket(source.market || source.type || source.category, name, code);
  const history = firstArray(source.klines, source.lines, source.history, source.series, source.data, source.items, source.list);
  const point = firstNumber(
    source.point,
    source.latest,
    source.close,
    source.value,
    source.indexValue,
    source.current,
    source.price
  );
  return {
    ...row,
    name: name || row.name,
    code: code === void 0 || code === null ? row.code : String(code),
    market,
    latest: point,
    point,
    todayRate: firstNumber(source.todayRate, source.rate, source.changePercent, source.pct, source.pctChg),
    yearRate: firstNumber(source.yearRate, source.oneYearRate, source.annualRate, source.yearChangePercent),
    klines: history
  };
}
function normalizeMarket(value, name, code) {
  const marketText = String(value || "").toUpperCase();
  if (/港|HK|HONG/.test(marketText))
    return "hk";
  if (/美|US|USA|NASDAQ|NYSE/.test(marketText))
    return "us";
  if (/A股|沪|深|CN|CHINA|大陆/.test(marketText))
    return "cn";
  const text = `${name || ""} ${code || ""}`.toUpperCase();
  if (/恒生|HSI|HSTECH|港股|香港/.test(text))
    return "hk";
  if (/纳斯达克|标普|道琼斯|NDX|IXIC|SPX|DJIA|NASDAQ|S&P/.test(text))
    return "us";
  if (/上证|科创|创业板|沪深|中证|000001|000688|399006/.test(text))
    return "cn";
  return "other";
}
function firstNumber(...values) {
  for (const value of values) {
    if (value === null || value === void 0)
      continue;
    if (typeof value === "string" && value.trim() === "")
      continue;
    const n = Number(value);
    if (Number.isFinite(n))
      return n;
  }
  return null;
}
function firstArray(...values) {
  for (const value of values) {
    if (Array.isArray(value))
      return value;
  }
  return [];
}
function isPureIndustryCapitalFlowItem(item) {
  const source = item;
  const name = String(source.name || source.Name || source.title || source.Title || "").trim();
  if (!name)
    return false;
  const upperName = name.toUpperCase();
  return !CAPITAL_FLOW_EXCLUDED_NAME_KEYWORDS.some((keyword) => {
    const upperKeyword = keyword.toUpperCase();
    return upperName.includes(upperKeyword);
  });
}
function filterIndustryCapitalFlowRows(rows = []) {
  return rows.filter(isPureIndustryCapitalFlowItem);
}
function normalizeCapitalFlowResponse(payload) {
  var _a, _b;
  const rows = filterIndustryCapitalFlowRows(payload.rows || []);
  const inflow = filterIndustryCapitalFlowRows(((_a = payload.inflow) == null ? void 0 : _a.length) ? payload.inflow : rows);
  const outflow = filterIndustryCapitalFlowRows(((_b = payload.outflow) == null ? void 0 : _b.length) ? payload.outflow : rows);
  return {
    ...payload,
    rows,
    inflow,
    outflow
  };
}
exports.filterIndustryCapitalFlowRows = filterIndustryCapitalFlowRows;
exports.getCapitalFlow = getCapitalFlow;
exports.getGlobalIndices = getGlobalIndices;
exports.getSectors = getSectors;
