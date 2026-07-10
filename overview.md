# 项目当前状态概览

本文件只记录仍然有效的高层事实。详细会计规则以 `CONTEXT.md` 和 ADR 0001、0002、0004 为准；历史修复经过见 `CHANGELOG.md` 与 `docs/reviews/`。

## 正式入口

- 后端：ASP.NET Core 8.0，主要代码在 `Controllers/`、`Services/`、`Models/`。
- 微信小程序：`miniprogram/src/`，正式构建产物为 `miniprogram/dist/build/mp-weixin/`。
- WebApp：`wwwroot/index.html`。
- `frontend/src/` 不是正式入口；`wwwroot/v2/` 已退役。

## 当前关键口径

- 同一北京时间自然日的蚂蚁 OCR 当前金额是首页展示边界，不得再叠加盘中估算或官方净值收益。
- 跨过 00:00 后旧 OCR 退出当前展示优先级；系统从最新确认档案、官方净值或盘中估值继续滚动。
- 周末、节假日和交易日 09:30 前只承接上一交易日金额与累计收益，当前自然日今日收益、今日收益率和单基金涨跌幅为 0。
- `DailyArchive` 保持原交易日归属，盘中估值不得入正式历史日历。
- 单基金场外申赎资金流已退役；行业板块资金只作独立行情参考。

## 主题

- 默认主题：曜石流光。
- 浅色主题：雾光银蓝。
- 旧主题值只做兼容映射，不再形成额外主题选项。

## 最近验证

2026-07-11 已运行后端构建、账务控制台测试、小程序类型检查/页面顺序检查/正式构建，并使用 Playwright 在手机宽度审查 WebApp 两种主题。当前具体结果见 `docs/reviews/2026-07-11-ocr-natural-day-and-theme-audit.md`。
