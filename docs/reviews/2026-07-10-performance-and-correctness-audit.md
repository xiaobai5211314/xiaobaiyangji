# 2026-07-10 性能与正确性审查

## 已确认并修正

1. `MarketCacheService` 的数据库缓存命中路径曾用同一个请求作用域 `AppDbContext` 启动 `Task.Run` 更新命中次数。该上下文可能与请求并发使用或在任务运行前释放，并且每次缓存读取都会额外写库。现已从热读取路径移除该统计写入。
2. `FundScraperService` 曾抓取已清仓基金，并按“代码 + 名称”去重，导致同代码名称不一致时重复请求。现只抓取有效持仓/待确认交易，并按基金代码去重。
3. `NavSettlementService` 曾遍历全部历史持仓。现只为仍有金额、份额或待确认交易的基金请求并结算官方净值。
4. `settle-nightly` 接受客户端涨幅并写持仓/档案，`auto-settle` 使用盘中估值写正式清算，违反 ADR 0002。前者已删除，后者已退出路由。
5. 基金级资金流没有普通场外基金可核验的实时申赎数据源，相关接口和两端展示已删除，避免无效请求和误导。
6. OCR 晚间导入收益重复计算：初版修复只覆盖 `estimate_today`。2026-07-11 复审后改为按北京时间当前自然日判断 OCR 展示优先级，同时覆盖盘中估值和官方净值分支；跨自然日收益与主题复审见 `docs/reviews/2026-07-11-ocr-natural-day-and-theme-audit.md`。

## 仍需后续治理

- `Controllers/FundController.cs` 约 10000 行，`wwwroot/index.html` 约 8500 行。两者职责过多，修改容易产生联动回归；拆分属于较大架构改造，本轮未贸然执行。
- `MarketCalendar` 当前只维护 2026 年 A 股/港股休市表，2027 年及以后节假日为“待核实”。进入新年度前必须按交易所公告更新并补测试。
- `UsShareClosedDates` 仍为空。当前场外基金交易日期估算已改用国内销售日历，未再依赖该空表；若未来新增美国场内交易日判断，必须先补权威日历来源。
- 当前 `dotnet build` 仍有历史可空引用警告。已检查本轮触及主链未出现编译错误，但其他控制器的可空告警需要分批消除并补接口测试。
- 小程序构建仍提示 Sass 旧版 JS API 与 `@import` 弃用警告；当前不影响构建，迁移到 `@use` 需单独做样式回归。
- 官方净值后台任务仍按基金代码顺序请求。当前已有清仓过滤，规模较小时足够；若活跃基金达到数十只以上，是否需要有界并发与限流需通过服务器指标验证，现标记“待核实”。

## 验证基线

- 后端：`dotnet build`
- 账务：`dotnet run --project tests/PortfolioAccounting.Tests/PortfolioAccounting.Tests.csproj`
- 小程序：`npm run typecheck`、`npm run check:pages-order`、`npm run build:mp-weixin`
- WebApp 脚本语法：`node --check index_script_check.js`（该脚本依赖浏览器 `Vue` 全局，不能直接作为 Node 程序执行）
