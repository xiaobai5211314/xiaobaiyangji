# DailyArchive 确认策略

## 状态

Accepted

## 背景

`DailyArchive` 是盈亏日历的历史收益档案表。每日需要决定哪些数据可以写入正式档案，哪些只能作为待确认或临时参考。

数据来源优先级：
1. 蚂蚁 OCR 确认数据（最可靠）
2. 官方净值（T+1 确认，可靠但延迟）
3. 盘中估值（实时但不可靠）

## 决策

1. **正式档案**：优先使用蚂蚁 OCR 确认数据写入 `DailyArchive`。
2. **待确认档案**：当日缺少蚂蚁确认时，使用官方净值写入，标记为 `official-nav-pending`。后续蚂蚁确认数据到达后覆盖。
3. **盘中估值禁止入正式档案**：盘中估值（`FundValuationEstimate`）只能用于临时展示，绝对不得写入 `DailyArchive`。
4. **TOTAL 汇总行**：`FundCode = 'TOTAL'` 的汇总行是首页"昨日确认收益"的优先读取来源。

## 影响

- `DailySettlementService` 的写入流程：先查蚂蚁确认 → 有则写正式档案 → 无则用官方净值写 pending → 后续蚂蚁数据覆盖 pending。
- `NavSettlementService` 在 17:00-02:00 期间每 5 分钟执行结算，但只处理官方净值，不处理盘中估值。
- 盈亏日历页面读取 `DailyArchive` 展示历史收益，不会出现盘中估值数据。

## 验收规则

- 正式档案的 `RecordDate` 与蚂蚁确认日期一致。
- `pending` 状态的档案在盈亏日历中不展示为"已确认"。
- 盘中估值不会出现在 `DailyArchive` 表的任何行中。
- 蚂蚁确认数据到达后，对应的 pending 档案被覆盖为正式数据。
