"use strict";
const common_vendor = require("../../common/vendor.js");
const services_api_influencer = require("../../services/api/influencer.js");
const stores_theme = require("../../stores/theme.js");
if (!Math) {
  AppTabBar();
}
const AppTabBar = () => "../../components/AppTabBar.js";
const _sfc_main = /* @__PURE__ */ common_vendor.defineComponent({
  __name: "index",
  setup(__props) {
    const loading = common_vendor.ref(false);
    const payload = common_vendor.ref({ status: "idle", items: [] });
    const posts = common_vendor.computed(() => (Array.isArray(payload.value.items) ? payload.value.items : []).slice().sort((left, right) => Date.parse(right.createdAt || "") - Date.parse(left.createdAt || "")).slice(0, 20));
    common_vendor.onShow(() => {
      stores_theme.loadTheme();
      loadData(false).catch((error) => console.warn("[tweets:load]", error));
    });
    common_vendor.onPullDownRefresh(async () => {
      try {
        await loadData(true);
      } finally {
        common_vendor.index.stopPullDownRefresh();
      }
    });
    async function loadData(force) {
      if (loading.value)
        return;
      loading.value = true;
      try {
        payload.value = await services_api_influencer.getInfluencerPosts(force);
      } finally {
        loading.value = false;
      }
    }
    function formatTime(value) {
      const date = new Date(value || "");
      if (Number.isNaN(date.getTime()))
        return "--";
      return date.toLocaleString("zh-CN", {
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false
      });
    }
    function formatCount(value) {
      const count = Number(value || 0);
      if (!Number.isFinite(count) || count <= 0)
        return "0";
      if (count >= 1e4)
        return `${(count / 1e4).toFixed(count >= 1e5 ? 0 : 1)}万`;
      return String(Math.round(count));
    }
    function openOriginal(url) {
      if (!url)
        return;
      common_vendor.index.setClipboardData({
        data: url,
        success: () => {
          common_vendor.index.showToast({ title: "原文链接已复制", icon: "none", duration: 1500 });
        },
        fail: () => {
          common_vendor.index.showToast({ title: "复制失败", icon: "none", duration: 1500 });
        }
      });
    }
    function toggleReplies(post) {
      post._showReplies = !post._showReplies;
    }
    return (_ctx, _cache) => {
      return common_vendor.e({
        a: payload.value.fetchedAt
      }, payload.value.fetchedAt ? {
        b: common_vendor.t(formatTime(payload.value.fetchedAt))
      } : {}, {
        c: common_vendor.t(loading.value ? "同步中" : "刷新"),
        d: loading.value,
        e: common_vendor.o(($event) => loadData(true), "82"),
        f: loading.value && posts.value.length === 0
      }, loading.value && posts.value.length === 0 ? {} : payload.value.status === "unavailable" || payload.value.status === "invalid" ? {
        h: common_vendor.o(($event) => loadData(true), "73")
      } : posts.value.length === 0 ? {
        j: common_vendor.o(($event) => loadData(true), "7a")
      } : {}, {
        g: payload.value.status === "unavailable" || payload.value.status === "invalid",
        i: posts.value.length === 0,
        k: common_vendor.f(posts.value, (post, k0, i0) => {
          return common_vendor.e({
            a: common_vendor.t(formatTime(post.createdAt)),
            b: common_vendor.t(post.authorName || "Serenity"),
            c: post.translatedText
          }, post.translatedText ? {
            d: common_vendor.t(post.translatedText)
          } : {}, {
            e: common_vendor.t(post.text),
            f: post.translationStatus === "failed"
          }, post.translationStatus === "failed" ? {} : post.translationStatus === "skipped" && !post.translatedText ? {} : !post.translatedText ? {} : {}, {
            g: post.translationStatus === "skipped" && !post.translatedText,
            h: !post.translatedText,
            i: common_vendor.t(formatCount(post.likeCount)),
            j: common_vendor.t(formatCount(post.retweetCount)),
            k: common_vendor.t(formatCount(post.replyCount)),
            l: common_vendor.o(($event) => openOriginal(post.url), post.externalId || post.id),
            m: post.replies && post.replies.length
          }, post.replies && post.replies.length ? common_vendor.e({
            n: common_vendor.t(post._showReplies ? "收起评论" : `展开评论 (${post.replies.length})`),
            o: common_vendor.o(($event) => toggleReplies(post), post.externalId || post.id),
            p: post._showReplies
          }, post._showReplies ? {
            q: common_vendor.f(post.replies, (reply, k1, i1) => {
              return common_vendor.e({
                a: common_vendor.t(reply.authorName || reply.authorUsername || "回复者"),
                b: common_vendor.t(formatTime(reply.createdAt)),
                c: reply.translatedText
              }, reply.translatedText ? {
                d: common_vendor.t(reply.translatedText)
              } : {}, {
                e: common_vendor.t(reply.text),
                f: reply.translationStatus === "failed"
              }, reply.translationStatus === "failed" ? {} : reply.translationStatus === "skipped" && !reply.translatedText ? {} : !reply.translatedText ? {} : {}, {
                g: reply.translationStatus === "skipped" && !reply.translatedText,
                h: !reply.translatedText,
                i: reply.id || reply.createdAt
              });
            })
          } : {}) : {}, {
            r: post.externalId || post.id
          });
        }),
        l: common_vendor.p({
          active: "tweets"
        }),
        m: common_vendor.n(common_vendor.unref(stores_theme.themeClass))
      });
    };
  }
});
const MiniProgramPage = /* @__PURE__ */ common_vendor._export_sfc(_sfc_main, [["__scopeId", "data-v-2e4090fd"]]);
wx.createPage(MiniProgramPage);
