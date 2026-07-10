# 2026-07-11 OCR、自然日与主题审查

## 根因与修复

1. OCR 展示新鲜度曾复用归档新鲜度判定，导致同日晚间 OCR 在部分官方净值场景不一定保持最高展示优先级。现按 `OcrSnapshotDate == 北京时间自然日` 单独判断；同日 OCR 金额不再叠加估算或确认收益。
2. `effectiveDate` 回退到上一交易日后，上一交易日档案曾被重新标为当前自然日 `official_today`。现休市日和 09:30 前只承接金额与累计收益，今日收益、收益率、单基金涨跌幅和今日曲线归零。
3. 单只资产详情 OCR 属于部分快照。现逐只决定是否使用当前 OCR 或上一交易日档案，不再由任意一只当前 OCR 阻止其他基金使用自己的档案。
4. 浅色主题遗留深色半透明控件和固定白字。现 WebApp 清除浅色主题暗底，小程序根文字改用 CSS 变量，并加深浅色主题财务红绿、警告和状态文字。

代码来源：`Controllers/FundController.cs`、`Services/MarketCalendar.cs`、`Services/PortfolioAccounting.cs`、`miniprogram/src/App.vue`、`miniprogram/src/styles/theme.scss`、`wwwroot/index.html`。

## 手工验算

晚间 OCR 当前金额为 `91942.89`，当天收益为 `-804.25`：

- 有当前自然日 OCR：展示金额仍为 `91942.89`，不能再计算成 `91942.89 - 804.25 = 91138.64`。
- 无当前自然日 OCR，上一确认金额为 `92747.14`：滚动金额为 `92747.14 - 804.25 = 91942.89`。
- OCR 总额 `92942.89` 中含待确认买入 `1000.00`：确认金额为 `91942.89`，账户总额仍为 `92942.89`。

## 验证结果

- `dotnet build`：通过，0 错误；仍有历史可空引用警告。
- 账务控制台测试：通过，覆盖晚间 OCR、待确认金额、周六、工作日 09:29/09:30 和自然日归零。
- 小程序 `typecheck`、页面顺序检查、正式构建：通过；Sass `@import`/legacy API 仍有弃用警告，待单独迁移。
- WebApp Playwright：390px 手机宽度无横向溢出；两种主题均可渲染 2 张模拟持仓卡；浅色主题非渐变文字对比度扫描未发现低于 4.5 的项目；浏览器控制台 0 error。

## Markdown 全量审计

本轮审计了修改前 Git 跟踪的 23 个 Markdown 文件。

| 处理 | 文件 |
|---|---|
| 已同步业务/主题规则 | `AGENTS.md`、`CLAUDE.md`、`CONTEXT.md`、`README.md`、`CHANGELOG.md`、ADR 0001-0004、`docs/agents/QUICKSTART.md`、`overview.md` |
| 已纠正历史或瞬时结论 | `.workbuddy/memory/2026-07-10.md`、`docs/reviews/2026-07-10-performance-and-correctness-audit.md`、`docs/agents/git-keep-single-main-branch.md` |
| 已核对，无需修改 | `.claude/skills/karpathy-guidelines/SKILL.md`、ADR 0005-0006、`docs/agents/agent-workflow.md`、`docs/agents/domain.md`、`docs/agents/issue-tracker.md`、`docs/agents/triage-labels.md`、`docs/deploy/influencer-posts-sidecar.md`、`tools/x_tweets_fetcher/README.md` |

未修改项分别属于通用编码技能、推文 sidecar、已退役基金资金流、Issue 流程或部署说明，与本轮代码事实无冲突。
