"use strict";
const services_request = require("../request.js");
function getInfluencerPosts(force = false) {
  const query = new URLSearchParams({ limit: "20" });
  if (force)
    query.set("_t", String(Date.now()));
  return services_request.get(`/api/influencer-posts/latest?${query}`, {
    silent: true,
    showErrorToast: false,
    fallbackData: { success: false, status: "unavailable", items: [] }
  });
}
exports.getInfluencerPosts = getInfluencerPosts;
