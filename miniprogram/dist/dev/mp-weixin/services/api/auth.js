"use strict";
const common_vendor = require("../../common/vendor.js");
const services_config = require("../config.js");
const services_request = require("../request.js");
function toAuthForm(payload) {
  return {
    username: payload.username,
    password: payload.password
  };
}
function login(payload) {
  return services_request.postForm("/api/auth/login", toAuthForm(payload), {
    loadingText: "登录中"
  });
}
function register(payload) {
  return services_request.postForm("/api/auth/register", toAuthForm(payload), {
    loadingText: "注册中"
  });
}
function getProfile(username) {
  return services_request.get(`/api/auth/profile?username=${encodeURIComponent(username)}`, {
    loadingText: "读取用户"
  });
}
function pickUsername(response, fallback = "") {
  var _a, _b;
  return ((_a = response.user) == null ? void 0 : _a.username) || ((_b = response.user) == null ? void 0 : _b.userName) || response.username || response.userName || fallback;
}
function pickDisplayName(response, fallback = "") {
  var _a;
  return ((_a = response.user) == null ? void 0 : _a.displayName) || response.displayName || pickUsername(response, fallback);
}
function pickAvatar(response) {
  var _a, _b;
  return ((_a = response.user) == null ? void 0 : _a.avatarDataUrl) || ((_b = response.user) == null ? void 0 : _b.avatarUrl) || response.avatarDataUrl || response.avatarUrl || "";
}
function parseUploadResponse(data) {
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
function uploadAvatarOnce(endpoint, username, filePath) {
  return new Promise((resolve, reject) => {
    common_vendor.index.uploadFile({
      url: `${services_config.getApiBaseUrl()}${endpoint}`,
      filePath,
      name: "avatarFile",
      formData: { username },
      success: (result) => {
        const statusCode = Number(result.statusCode || 0);
        const parsed = parseUploadResponse(result.data);
        if (statusCode < 200 || statusCode >= 300) {
          reject(new Error(parsed.message || parsed.msg || parsed.title || parsed.error || `头像上传失败：${statusCode}`));
          return;
        }
        resolve(parsed);
      },
      fail: reject
    });
  });
}
async function uploadAvatar(username, filePath) {
  const endpoints = ["/api/auth/avatar-file-v3", "/api/auth/avatar-file-v2", "/api/auth/avatar-file"];
  let lastError = null;
  for (const endpoint of endpoints) {
    try {
      return await uploadAvatarOnce(endpoint, username, filePath);
    } catch (error) {
      lastError = error;
    }
  }
  throw lastError instanceof Error ? lastError : new Error("头像上传失败");
}
exports.getProfile = getProfile;
exports.login = login;
exports.pickAvatar = pickAvatar;
exports.pickDisplayName = pickDisplayName;
exports.pickUsername = pickUsername;
exports.register = register;
exports.uploadAvatar = uploadAvatar;
