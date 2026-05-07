"use strict";
const common_vendor = require("../common/vendor.js");
function getStorage(key, fallback) {
  try {
    const value = common_vendor.index.getStorageSync(key);
    return value === "" || value === void 0 || value === null ? fallback : value;
  } catch {
    return fallback;
  }
}
function setStorage(key, value) {
  common_vendor.index.setStorageSync(key, value);
}
function removeStorage(key) {
  common_vendor.index.removeStorageSync(key);
}
exports.getStorage = getStorage;
exports.removeStorage = removeStorage;
exports.setStorage = setStorage;
