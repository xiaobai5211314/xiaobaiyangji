# 领域文档

## 布局

单上下文模式：一个 `CONTEXT.md` + `docs/adr/` 位于仓库根目录。

- **`CONTEXT.md`** — 项目领域语言、核心概念和实体关系。`improve-codebase-architecture`、`diagnose`、`tdd` 等技能会读取此文件来理解项目。
- **`docs/adr/`** — 架构决策记录（Architecture Decision Records）。每个 ADR 是一个带编号的 markdown 文件，记录一项历史决策及其理由。

## 使用规则

- 在任何需要理解领域术语或实体关系的任务之前，**读取** `CONTEXT.md`。
- 在提出架构变更之前，**读取** `docs/adr/` 中的所有文件——不要重复讨论已定的决策。
- 做出重大架构选择时（新数据源、认证方式变更、缓存策略等），**写入**新的 ADR 到 `docs/adr/`。

## 当前状态

- `CONTEXT.md` — ✅ 已创建，位于仓库根目录
- `docs/adr/` — ✅ 已创建，现有 ADR：0001-source-of-truth、0002-daily-archive、0003-frontend-cache、0004-ocr-import、0005-influencer-posts
