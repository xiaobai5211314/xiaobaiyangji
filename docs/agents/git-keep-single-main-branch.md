# How to 只保留一个主分支

本文用于把本地仓库整理成“只保留一个主分支”的状态。适用场景：Visual Studio 的 Git 分支窗口里出现很多历史分支、临时分支、备份分支，希望清理到只保留主分支。

> 当前仓库写作时的主分支是 `master`。来源：本地命令 `git status --short --branch` 输出 `## master...origin/master [ahead 1]`。
>
> Visual Studio 2026 的具体菜单文案我无法实时确认，待核实；以下步骤按 Visual Studio 近年 Git Repository / 分支管理窗口的通用交互写法整理。

## 先确认要保留哪个主分支

本仓库当前建议保留：

```powershell
master
```

如果以后要改成 `main`，那是另一件事：需要先改本地分支名、推送新分支，并到 GitHub/GitLab 设置默认分支后，再删除旧分支。不要在没设置默认分支前直接删远程 `master`。

## 在 Visual Studio 里删除多余本地分支

1. 打开项目。
2. 打开 `Git` 菜单。
3. 进入 `Git Repository / Git 存储库` 或 `Manage Branches / 管理分支`。
4. 先切到要保留的主分支：

   ```text
   master → Checkout / 签出
   ```

5. 确认当前分支已经是 `master`。
6. 在左侧本地分支列表里，右键不想保留的分支。
7. 选择 `Delete / 删除`。
8. 对每个多余本地分支重复第 6-7 步。

注意：不能删除当前正在签出的分支。如果 VS 不让删除某个分支，先切回 `master`，再删除它。

## 命令行兜底

如果 Visual Studio 删除不顺，用 PowerShell 在仓库根目录执行：

```powershell
git switch master
git branch
```

删除已经合并过的本地分支：

```powershell
git branch -d 分支名
```

如果 Git 提示该分支还没有合并，但你确认不要了，再强制删除：

```powershell
git branch -D 分支名
```

`-D` 是强制删除。使用前建议先看这个分支上有什么提交：

```powershell
git log --oneline master..分支名
```

如果这条命令有输出，说明该分支有 `master` 没有的提交；是否删除需要人工确认。

## 如果远程也要只保留一个分支

只清理本地界面时，不需要删远程分支。

如果你确定远程也要删，先查看远程分支：

```powershell
git branch -r
```

删除远程分支：

```powershell
git push origin --delete 分支名
```

例如：

```powershell
git push origin --delete codex/add-influencer-posts
```

删除远程分支会影响其他电脑和协作者。单人项目也建议先确认这个分支已经合并到 `master`。

## 清理 VS 里残留的远程分支显示

远程分支删除后，Visual Studio 里可能还显示旧引用。执行：

```powershell
git fetch --prune
```

然后刷新 Visual Studio 的 Git Repository 窗口。

## 验证结果

本地只剩主分支时：

```powershell
git branch
```

应该只看到类似：

```text
* master
```

远程只剩主分支时：

```powershell
git branch -r
```

应该只看到类似：

```text
origin/master
```

如果还有 `origin/HEAD -> origin/master`，这是远程默认分支指针，不是多余业务分支，可以保留。

## 常见问题

### 删除分支会删除代码吗？

删除分支会删除分支名这个指针。若该分支的提交已经合并到 `master`，代码不会丢。若该分支有未合并提交，删除后会更难找回，所以删除前用下面命令检查：

```powershell
git log --oneline master..分支名
```

### 我应该用 `-d` 还是 `-D`？

- `git branch -d 分支名`：安全删除，Git 会阻止删除未合并分支。
- `git branch -D 分支名`：强制删除，适合确认不要的临时分支或备份分支。

### 图里的 `remotes/origin` 要不要删？

不要直接删 `remotes/origin` 这个分组。它代表远程仓库 `origin` 下的远程跟踪分支。要删的是具体分支，例如 `origin/codex/add-influencer-posts` 对应的远程分支。

## 可核查依据

- 本仓库当前分支状态：`git status --short --branch`。
- Git 官方 `git branch` 文档：<https://git-scm.com/docs/git-branch>
- Git 官方 `git push` 文档：<https://git-scm.com/docs/git-push>
- Git 官方 `git fetch` 文档：<https://git-scm.com/docs/git-fetch>
- Microsoft Learn 的 Visual Studio Git 文档：<https://learn.microsoft.com/visualstudio/version-control/git-with-visual-studio>
