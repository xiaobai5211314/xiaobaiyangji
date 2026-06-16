# Agent 工作流指南

本文档说明 issue 标签的使用时机、流转规则和建议标签体系。

## 标签状态机

```
新建 issue
    │
    ▼
needs-triage ──────────────────────────────────────┐
    │                                               │
    ├─→ needs-info ──→ (用户补充) ──→ needs-triage  │
    │                                               │
    ├─→ ready-for-agent                             │
    │       │                                       │
    │       ▼                                       │
    │   AI 代理接手实现                               │
    │       │                                       │
    │       ▼                                       │
    │   提交 PR / 完成修改                            │
    │                                               │
    ├─→ ready-for-human                             │
    │       │                                       │
    │       ▼                                       │
    │   人工实现                                      │
    │                                               │
    └─→ wontfix ◄───────────────────────────────────┘
```

### 各标签使用时机

| 标签 | 何时使用 | 谁来打 | 示例 |
|------|---------|--------|------|
| `needs-triage` | 新 issue 创建后默认状态 | issue 模板自动 | 所有新 issue |
| `needs-info` | 描述不清、缺截图、缺复现步骤 | 维护者 | "收益显示不对"但没贴蚂蚁截图 |
| `ready-for-agent` | 描述完整、有明确验收标准、AI 可直接接手 | 维护者 | 贴了蚂蚁截图 + 项目截图 + 期望结果 |
| `ready-for-human` | 需要业务判断、上线确认或涉及敏感操作 | 维护者 | 需要确认新的收益计算口径 |
| `wontfix` | 不符合项目目标、重复 issue、无法复现 | 维护者 | 用户误操作导致的问题 |

### 流转规则

1. **新建** → 自动打 `needs-triage`。
2. **triage 后** → 根据内容转为 `needs-info`、`ready-for-agent`、`ready-for-human` 或 `wontfix`。
3. **`needs-info`** → 用户补充信息后，维护者重新 triage。
4. **`ready-for-agent`** → AI 代理接手，实现完成后关闭或转为 `ready-for-human` 做最终确认。
5. **`ready-for-human`** → 人工实现，完成后关闭。

## 建议标签体系

除上述 5 个状态标签外，建议使用以下分类标签：

### 类型标签（type）

| 标签 | 含义 |
|------|------|
| `type:bug` | 缺陷 |
| `type:feature` | 新功能 |
| `type:docs` | 文档 |
| `type:refactor` | 重构 |

### 领域标签（area）

| 标签 | 含义 |
|------|------|
| `area:fund` | 基金模块 |
| `area:ocr` | OCR 导入 |
| `area:calendar` | 盈亏日历 |
| `area:frontend` | 小程序前端 |
| `area:backend` | 后端 API |

### 优先级标签（priority）

| 标签 | 含义 |
|------|------|
| `priority:p0` | 紧急：影响核心功能或数据正确性 |
| `priority:p1` | 重要：影响用户体验但有 workaround |
| `priority:p2` | 一般：优化类、非紧急 |

### 组合示例

| 场景 | 标签组合 |
|------|---------|
| 首页收益显示错误 | `needs-triage` `type:bug` `area:fund` `priority:p0` |
| OCR 识别漏字段 | `needs-triage` `type:bug` `area:ocr` `priority:p1` |
| 盈亏日历新增月度汇总 | `needs-triage` `type:feature` `area:calendar` `priority:p2` |
| 重构 FundController | `needs-triage` `type:refactor` `area:backend` `priority:p2` |
