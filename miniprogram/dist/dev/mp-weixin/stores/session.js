"use strict";
const common_vendor = require("../common/vendor.js");
const utils_storage = require("../utils/storage.js");
const USERNAME_KEY = "fund_username";
const AVATAR_KEY = "fund_avatar";
const SESSION_KEY = "fund_session";
const TOKEN_KEY = "fund_token";
const sessionState = common_vendor.reactive({
  username: "",
  displayName: "",
  avatarDataUrl: "",
  avatarUrl: "",
  loginTime: 0
});
common_vendor.computed(() => Boolean(sessionState.username));
function normalizeSession(value) {
  const username = String((value == null ? void 0 : value.username) || "").trim();
  if (!username)
    return null;
  return {
    username,
    displayName: String((value == null ? void 0 : value.displayName) || "").trim(),
    avatarDataUrl: String((value == null ? void 0 : value.avatarDataUrl) || ""),
    avatarUrl: String((value == null ? void 0 : value.avatarUrl) || ""),
    loginTime: Number((value == null ? void 0 : value.loginTime) || Date.now())
  };
}
function applySession(value) {
  sessionState.username = (value == null ? void 0 : value.username) || "";
  sessionState.displayName = (value == null ? void 0 : value.displayName) || "";
  sessionState.avatarDataUrl = (value == null ? void 0 : value.avatarDataUrl) || "";
  sessionState.avatarUrl = (value == null ? void 0 : value.avatarUrl) || "";
  sessionState.loginTime = (value == null ? void 0 : value.loginTime) || 0;
}
function saveSession(payload) {
  const next = normalizeSession({
    ...sessionState,
    ...payload,
    loginTime: payload.loginTime || sessionState.loginTime || Date.now()
  });
  applySession(next);
  if (!next)
    return null;
  utils_storage.setStorage(SESSION_KEY, next);
  utils_storage.setStorage(USERNAME_KEY, next.username);
  utils_storage.setStorage(AVATAR_KEY, next.avatarDataUrl || next.avatarUrl || "");
  if (payload.token)
    utils_storage.setStorage(TOKEN_KEY, payload.token);
  return next;
}
function getToken() {
  return utils_storage.getStorage(TOKEN_KEY, "");
}
function loadSession() {
  const stored = normalizeSession(utils_storage.getStorage(SESSION_KEY, null));
  if (stored) {
    applySession(stored);
    return stored;
  }
  const legacy = normalizeSession({
    username: utils_storage.getStorage(USERNAME_KEY, ""),
    avatarDataUrl: utils_storage.getStorage(AVATAR_KEY, ""),
    loginTime: Date.now()
  });
  applySession(legacy);
  if (legacy)
    utils_storage.setStorage(SESSION_KEY, legacy);
  return legacy;
}
function restoreSession() {
  return loadSession();
}
function clearSession() {
  applySession(null);
  utils_storage.removeStorage(SESSION_KEY);
  utils_storage.removeStorage(USERNAME_KEY);
  utils_storage.removeStorage(AVATAR_KEY);
  utils_storage.removeStorage(TOKEN_KEY);
}
exports.clearSession = clearSession;
exports.getToken = getToken;
exports.loadSession = loadSession;
exports.restoreSession = restoreSession;
exports.saveSession = saveSession;
exports.sessionState = sessionState;
