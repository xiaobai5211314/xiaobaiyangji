"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_auth = require("../../services/api/auth.js");
const stores_session = require("../../stores/session.js");
const stores_theme = require("../../stores/theme.js");
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const submitting = common_vendor.ref(false);
    const registering = common_vendor.ref(false);
    const wechatSubmitting = common_vendor.ref(false);
    const errorMessage = common_vendor.ref("");
    const form = common_vendor.reactive({
      username: "",
      password: ""
    });
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      const session = stores_session.loadSession();
      if (session == null ? void 0 : session.username) {
        common_vendor.index.reLaunch({ url: "/pages/home/index" });
      }
    });
    function getErrorMessage(error) {
      if (error instanceof Error && error.message)
        return error.message;
      if (error && typeof error === "object" && "errMsg" in error) {
        return String(error.errMsg || "");
      }
      return "操作失败，请稍后重试";
    }
    function getWechatErrorMessage(error) {
      const message = getErrorMessage(error);
      if (message.includes("未获取到微信登录凭证") || message.includes("login:fail")) {
        return "可继续使用账号密码登录";
      }
      return message;
    }
    function readCredentials() {
      const username = form.username.trim();
      const password = form.password;
      if (!username || !password) {
        errorMessage.value = "请输入账号和密码";
        common_vendor.index.showToast({ title: "请输入账号和密码", icon: "none" });
        return null;
      }
      return { username, password };
    }
    function getWechatLoginCode() {
      return new Promise((resolve, reject) => {
        common_vendor.index.login({
          provider: "weixin",
          success: (result) => {
            const code = (result.code || "").trim();
            if (!code) {
              reject(new Error("未获取到微信登录凭证"));
              return;
            }
            resolve(code);
          },
          fail: reject
        });
      });
    }
    async function wechatOneTapLogin() {
      if (wechatSubmitting.value)
        return;
      wechatSubmitting.value = true;
      errorMessage.value = "";
      try {
        const code = await getWechatLoginCode();
        const result = await services_api_auth.wechatLogin({ code });
        const username = services_api_auth.pickUsername(result);
        if (!username)
          throw new Error("微信登录成功但未返回账号");
        stores_session.saveSession({
          username,
          displayName: services_api_auth.pickDisplayName(result, username),
          avatarDataUrl: services_api_auth.pickAvatar(result),
          loginTime: Date.now()
        });
        form.password = "";
        common_vendor.index.reLaunch({ url: "/pages/home/index" });
      } catch (error) {
        console.warn("[login:wechat]", error);
        const message = getWechatErrorMessage(error);
        errorMessage.value = message;
        common_vendor.index.showToast({ title: message, icon: "none" });
      } finally {
        wechatSubmitting.value = false;
      }
    }
    async function submit() {
      const credentials = readCredentials();
      if (!credentials)
        return;
      submitting.value = true;
      errorMessage.value = "";
      try {
        const result = await services_api_auth.login(credentials);
        const username = services_api_auth.pickUsername(result, credentials.username);
        stores_session.saveSession({
          username,
          displayName: services_api_auth.pickDisplayName(result, username),
          avatarDataUrl: services_api_auth.pickAvatar(result),
          loginTime: Date.now()
        });
        form.password = "";
        common_vendor.index.reLaunch({ url: "/pages/home/index" });
      } catch (error) {
        console.warn("[login:submit]", error);
        errorMessage.value = getErrorMessage(error);
      } finally {
        submitting.value = false;
      }
    }
    async function registerAccount() {
      const credentials = readCredentials();
      if (!credentials)
        return;
      registering.value = true;
      errorMessage.value = "";
      try {
        await services_api_auth.register(credentials);
        form.password = "";
        common_vendor.index.showToast({ title: "注册成功，请登录", icon: "none" });
      } catch (error) {
        console.warn("[login:register]", error);
        errorMessage.value = getErrorMessage(error);
      } finally {
        registering.value = false;
      }
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: common_vendor.t(wechatSubmitting.value ? "微信登录中..." : "微信一键登录"),
        b: wechatSubmitting.value || submitting.value || registering.value,
        c: common_vendor.o(wechatOneTapLogin, "14"),
        d: form.username,
        e: common_vendor.o(common_vendor.m(($event) => form.username = $event.detail.value, {
          trim: true
        }), "55"),
        f: common_vendor.o(submit, "db"),
        g: form.password,
        h: common_vendor.o(($event) => form.password = $event.detail.value, "19"),
        i: errorMessage.value
      }, errorMessage.value ? {
        j: common_vendor.t(errorMessage.value)
      } : {}, {
        k: common_vendor.t(submitting.value ? "登录中..." : "登录"),
        l: submitting.value,
        m: common_vendor.o(submit, "20"),
        n: common_vendor.t(registering.value ? "注册中..." : "注册账号"),
        o: registering.value || submitting.value,
        p: common_vendor.o(registerAccount, "af"),
        q: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-45258083"]]);
wx.createPage(MiniProgramPage);
