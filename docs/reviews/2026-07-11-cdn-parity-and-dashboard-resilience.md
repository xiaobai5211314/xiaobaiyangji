# 2026-07-11 CDN 一致性与持仓加载韧性审查

## 已核验根因

1. 直连地址 `https://guzhi.21212121.xyz/index.html` 在 2026-07-11 06:10 UTC 返回 `Last-Modified: 05:28:44 GMT`、`Content-Length: 458369`；CDN 地址 `https://guzhicdn.21212121.xyz/index.html` 同一检查时返回 `Last-Modified: 04:52:42 GMT`、`Content-Length: 458751`。两份正文 SHA-256 不同。
2. GitHub Actions 运行 `29140302904` 在 04:52 UTC 成功执行又拍云前端上传，对应提交 `eb4edfb`；运行 `29141270739` 在 05:28 UTC 成功执行后端部署，对应提交 `f679f5a`。后端发布会复制 `wwwroot/` 到直连站点，而该提交没有触发前端上传工作流，因此 CDN 保持旧页面。
3. 后端对 CDN 来源的 CORS 预检已能返回 `Access-Control-Allow-Origin: https://guzhicdn.21212121.xyz`、`Access-Control-Allow-Headers: authorization,content-type` 和对应 GET/POST 方法；本次卡住的直接证据是页面版本分叉，不是该次检查中的 CORS 拒绝。

来源：2026-07-11 本地 `curl` 响应头/正文 SHA-256 检查，GitHub Actions CLI 对上述运行 ID 的查询，`Program.cs` CORS 管线和 `.github/workflows/upyun-deploy.yml`。

## 修复

- 又拍云工作流把刷新失败从 warning 改为失败：按[又拍云缓存刷新接口](https://help.upyun.com/knowledge-base/purge/)要求计算 `UpYun` 刷新签名并以表单字段提交 URL；随后轮询普通 CDN 地址，比较 SHA-256。CDN 仍返回旧正文时工作流失败，不能再出现“上传成功、用户仍拿到旧页面”。
- WebApp 的 `/api/fund/today` 请求增加 15 秒上限。请求超时或失败时，页面退出骨架加载态、保留已有持仓数据并显示可操作的重试入口。
- 资产显示切换继续以本机设置为第一响应：点击即更新 `privacy_mode`、关闭弹窗并提示当前模式；账号 UI 状态仍在后台防抖同步，网络失败不能阻止本机切换。微信内置 WebView 对 Iconify 运行时改写动态节点的兼容性不稳定，因此入口和弹窗内图标改为静态 Lucide SVG；弹窗使用 `v-show` 保留稳定 DOM，并同步设置遮罩显示状态作为兼容兜底。

## 验收要求

1. 推送包含 `wwwroot/**` 的提交后，前端工作流的“校验 CDN 已同步当前页面”必须通过。
2. 使用无查询参数的直连地址和 CDN 地址分别读取 `index.html`，两份正文应与当前提交的 `wwwroot/index.html` 一致。
3. 在浏览器中阻塞 `/api/fund/today` 超过 15 秒，持仓页不允许无限骨架屏；必须显示重试入口。
4. 四个资产显示选项点击后，弹窗关闭、页面即时按所选范围遮罩，并显示“已切换为...”提示。

## 边界

本轮仅修改 WebApp 请求韧性、资产显示交互、又拍云发布校验和文档；未修改基金金额、收益、OCR、`DailyArchive` 或加减仓计算。
