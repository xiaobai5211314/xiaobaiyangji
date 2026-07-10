# 2026-07-10 OCR 修复 + 文档审查总结

## 本次完成的工作

### 1. OCR 晚间导入收益重复计算修复

**文件**：`Controllers/FundController.cs`（第 6659 行附近）

**问题**：晚上 11 点导入 OCR 后，首页"账户总金额"= OCR 金额 + 盘中估算收益，当日收益被计算两遍。

**根因**：`estimate_today` 分支中，有新鲜 OCR 快照时仍将 `todayProfit`（盘中估算收益）叠加到 OCR 当前金额上。晚间 OCR 的当前金额已是平台当天最终值（含当日收益），叠加盘中估算收益即重复。

**修复**：有新鲜 OCR 且 `rawHoldAmount > 0` 时，保持 OCR 当前金额不变，盘中估算收益仅用于临时展示。

### 2. 全量 Markdown 文档审查

审查了项目所有 markdown 文件，发现 6 处过时内容并全部修复：

| 文件 | 问题 | 修复 |
|------|------|------|
| CHANGELOG.md | 缺少 2026-07-10 条目 | 补充 OCR 修复 + 性能审查条目 |
| CONTEXT.md | 账户总金额口径缺少 OCR 不叠加盘中收益边界 | 补充说明 |
| docs/adr/0001 | 验收规则缺少 estimate_today 状态约束 | 补充验收条目 |
| docs/agents/domain.md | ADR 数量写"5 份"但实际有 6 份 | 更正为 6 份 |
| docs/agents/QUICKSTART.md | 缺少 OCR 晚间导入诊断示例 | 补充示例 |
| docs/reviews/2026-07-10 | 审查报告缺少本次修复记录 | 补充第 6 条 |

### 3. 提交信息

- Commit: `709b55c`
- 7 文件改动，+29/-6 行
- 已推送到 `origin/master`

## 手工验算

| 字段 | 修复前 | 修复后 |
|------|--------|--------|
| 基金A rawHoldAmount (OCR=10000, 盘中+150) | 10150 | 10000 |
| 基金B rawHoldAmount (OCR=5000, 盘中+100) | 5100 | 5000 |
| accountTotalAmount | 15250 (错误) | 15000 (正确) |
| intradayProfit (临时展示) | 250 | 250 (不变) |

## 验证

- `dotnet build` 通过，0 错误
- `official_today` 分支不受影响（原本已正确处理）
- 白天无新鲜 OCR 场景不受影响（走原逻辑）
