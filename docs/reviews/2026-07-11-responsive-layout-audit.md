# 2026-07-11 响应式布局与控件遮挡审查

## 问题与根因

1. WebApp 账户卡片曾把总金额与市场状态放入同一横向网格。1365px 视口下，账户卡实际分配宽度约 360px，状态卡固定占用约 184px，金额容器只有约 140px，而 `¥ 90776.35` 需要约 184px，因而越过容器并压到“市场状态”。
2. WebApp 底部导航使用 `position: fixed`。手机端缺少足够的安全滚动空间，桌面端会直接浮在业务卡片上方；移动端底栏还因内容盒宽度未包含 padding 而左右各越界约 5.5px。
3. 多个 WebApp 弹窗分别写死 `75vh`、`80vh` 或 `90vh`，未统一扣除安全区；小程序首页操作区和盈亏日历工具栏禁止换行，窄屏或系统字体放大时存在裁切风险。

事实来源：修改前的 `wwwroot/index.html`、`miniprogram/src/pages/home/index.vue`、`miniprogram/src/pages/analysis/index.vue`、`miniprogram/src/components/AppTabBar.vue`，以及本地浏览器元素边界测量。

## 修复

- 市场状态改为账户卡片顶部的紧凑状态条，金额独占下一行；标签和状态允许安全换行。
- WebApp 各视口固定底栏增加 `border-box`、安全区和页面底部留白；桌面端曾短暂试用顶部粘性导航，已在同日回退为底部固定导航，避免改变既有导航位置。
- `.modal-mask`、`.modal-overlay` 和直接子卡片统一限制视口尺寸并支持纵向滚动；页头、工具栏、标题和底栏标签增加收缩与换行规则。
- 小程序页头、标签、首页操作区和日历工具栏允许换行；弹窗按安全区计算最大高度；底栏标签在系统字体放大时使用省略保护，但五个导航名称本身保持可见。
- 未修改基金金额、收益、OCR、`DailyArchive` 或首页 summary 的任何计算逻辑。

## 自动化验收

使用 `agent-browser` 连接本地 Chromium，检查正式 WebApp `wwwroot/index.html`：

| 范围 | 视口 | 结果 |
|---|---|---|
| 持仓、行情、资讯、复盘、观点 5 页 | 390×844、768×1024、1365×768、2048×1080 | 横向溢出 0；导航不越界；可见按钮、输入框、选择框无内部裁切 |
| 账户、隐私、编辑、加仓、减仓、板块雷达、板块详情、资讯、大盘、历史、归档、OCR 12 类弹窗 | 390×844、1365×768 | 弹窗不越出视口；横向溢出 0；长内容保留滚动区域 |
| 账户金额与市场状态 | 390×844、1365×768 | 两个矩形边界无相交；金额容器 `scrollWidth == clientWidth` |
| 浏览器控制台 | 当前正式入口 | 0 error |

小程序验证：

```powershell
cd miniprogram
npm run typecheck
npm run check:pages-order
npm run build:mp-weixin
```

三项均通过。构建仍有项目既存的 Sass `@import` 和 legacy JS API 弃用警告，未新增编译错误；迁移到 `@use` 属于单独的样式基础设施任务。

## 文档影响审查

本轮同步更新 `README.md`、`CONTEXT.md`、`CHANGELOG.md` 和 ADR 0003。账务、归档、OCR、交易时序、基金资金流退役、推文 sidecar 与部署文档不受本次纯布局变更影响，因此未改写其业务事实。
