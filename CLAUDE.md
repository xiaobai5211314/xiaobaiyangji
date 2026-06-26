# CLAUDE.md - 小白养基 Claude Code 入口

本文件给 Claude Code 读取。Codex/OpenAI Agent 的根规则文件是 `AGENTS.md`。

Claude 在本仓库工作时，先读并遵守：

1. `AGENTS.md`
2. `CONTEXT.md`
3. 与当前任务相关的 `docs/adr/` 文件
4. 当前要修改的源码和测试

如果 `CLAUDE.md` 与 `AGENTS.md` 冲突，以 `AGENTS.md` 为准；如果两者与用户当前指令冲突，以用户当前指令为准。

## 核心强制规则

- 不要编造事实。涉及事实、数据、平台规则、时间、人物、外部服务行为时，必须给可核查来源；无法确认就标「待核实」。
- 不要读取、打印、复制或提交任何密钥、cookie、token、私有环境文件内容。
- 不要自动提交、推送、部署，除非用户明确要求。
- 不要顺手重构。只改用户要求范围内的文件。
- 不要把 `frontend/src/` 当正式前端入口。
- 不要恢复 `wwwroot/v2/`。
- 不要把盘中估值写入正式历史档案。
- 不要为了让收益数字看起来接近而改公式。

## 项目事实源

- 项目领域和金额口径：`CONTEXT.md`
- 基金金额事实源：`docs/adr/0001-fund-accounting-source-of-truth.md`
- `DailyArchive` 确认策略：`docs/adr/0002-daily-archive-confirmation-policy.md`
- 前端缓存和 CDN：`docs/adr/0003-frontend-cache-and-cdn-policy.md`
- OCR 字段契约：`docs/adr/0004-ocr-import-data-contract.md`
- 推文 sidecar 边界：`docs/adr/0005-influencer-posts-source-and-cache.md`
- 推文部署和敏感信息：`docs/deploy/influencer-posts-sidecar.md`

## 正式入口

- 后端：ASP.NET Core 8.0，主要目录为 `Controllers/`、`Services/`、`Models/`、`Migrations/`。
- 微信小程序：`miniprogram/src/`。
- WebApp：`wwwroot/index.html`。
- `frontend/src/` 是非正式入口，除非用户明确要求，否则不要修改。
- `wwwroot/v2/` 已废弃，不要恢复。

## 财务和 OCR 红线

修改基金金额、收益、收益率、首页 summary、盈亏日历、`DailyArchive`、基金 OCR 前，必须读 `CONTEXT.md` 和 ADR 0001、0002、0004。

必须保持：

- `平台当前金额 = 已确认持仓金额 + 买入待确认金额`
- 蚂蚁 OCR 快照优先作为平台展示金额校准源，但不是每天唯一的当前金额来源。
- 无新鲜 OCR 时，首页当前金额必须随官方净值或盘中估值的单基当前市值合计滚动。
- `DailyArchive` 只写正式确认金额和收益。
- 盘中估值只作临时展示，不进正式历史日历。
- 昨日收益不能用于反推确认金额或持有收益。

股票截图解析由 `StockOcrParserService` 负责；基金 OCR 契约不要错误绑定到股票 OCR parser。

## 推文 sidecar 红线

- 固定账号：`@aleabitoreddit`。
- 前端只调用后端接口，不直连 X 或翻译服务。
- 私有环境文件只允许在服务器 `/www/wwwroot/小白养基/.secrets/influencer.env`。
- 禁止输出 `X_COOKIE`、`auth_token`、`ct0`、腾讯云 SecretId、SecretKey。
- 抓取或翻译失败只能降级，不得影响基金首页、收益计算、OCR、`DailyArchive` 或首页 summary。

## 常用验证

```powershell
dotnet build
dotnet run --project tests/PortfolioAccounting.Tests/PortfolioAccounting.Tests.csproj
dotnet run --project tests/InfluencerPosts.Tests/InfluencerPosts.Tests.csproj
```

```powershell
cd miniprogram
npm run typecheck
npm run check:pages-order
npm run build:mp-weixin
```

根据改动范围选择验证。无法验证时，在最终回复里明确说明。

## 工作风格

- 先定位事实源，再改文件。
- 先给小计划，再执行有风险操作。
- 用 `rg` 搜索，用最小补丁修改。
- 保留用户已有改动，不覆盖无关文件。
- 遇到业务口径冲突，停下来问。
- 最终回复列出改了哪些文件、怎么验证、还有什么待核实。
- 每次完成代码改动后，必须执行 /changelog-cn 更新项目根目录 CHANGELOG.md。
