# 分类标签

`triage` 技能使用以下 GitHub 标签跟踪 issue 状态：

| 角色 | 标签 | 适用场景 |
|------|------|---------|
| 待维护者评估 | `needs-triage` | 新 issue，需要人工评估 |
| 等待报告者补充 | `needs-info` | 需要报告者提供更多信息 |
| 代理可接手 | `ready-for-agent` | 描述完整，AI 代理可以直接接手 |
| 需要人工实现 | `ready-for-human` | 需要人工实现 |
| 不予处理 | `wontfix` | 不会处理 |

## 创建标签

如果标签尚不存在，执行以下命令创建：

```bash
gh label create "needs-triage" --color "D93F0B" --description "待维护者评估"
gh label create "needs-info" --color "FBCA04" --description "等待报告者补充"
gh label create "ready-for-agent" --color "0E8A16" --description "代理可接手"
gh label create "ready-for-human" --color "1D76DB" --description "需要人工实现"
gh label create "wontfix" --color "FFFFFF" --description "不予处理"
```
