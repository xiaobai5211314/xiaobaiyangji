# 问题跟踪器

使用 **GitHub Issues** 跟踪问题（仓库 `xiaobai5211314/xiaobaiyangji`）。

## 常用命令

使用 `gh` CLI。所有命令自动解析仓库上下文。

| 操作 | 命令 |
|------|------|
| 创建 issue | `gh issue create --title "..." --body "..." --label "..."` |
| 列出未关闭的 issue | `gh issue list` |
| 查看 issue 详情 | `gh issue view <number>` |
| 添加评论 | `gh issue comment <number> --body "..."` |
| 添加标签 | `gh issue edit <number> --add-label "..."` |
| 关闭 issue | `gh issue close <number>` |

## 约定

- 用标签跟踪分类状态（见 `docs/agents/triage-labels.md`）。
- 当 issue 描述完整、AI 代理可以直接接手时，使用 `--label ready-for-agent`。
- 开始工作时将 issue 分配给自己（`--assignee @me`）。
